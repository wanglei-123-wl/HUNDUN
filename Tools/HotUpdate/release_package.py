"""Build and verify a complete signed publication; never publishes a pointer."""
from __future__ import annotations
import argparse
import json
from pathlib import Path
import tarfile
from release_common import *

def verify_client_proof(path, release, config, acceptance=False):
    proof = verify_envelope(read_json(path), config)
    require(proof.get('kind') == 'QiDaoBaseClientProof' and proof.get('schemaVersion') == 1
            and proof.get('status') == 'verified' and proof.get('target') == release['target']
            and proof.get('unityVersion') == release['unityVersion']
            and proof.get('bootstrapApiVersion') == release['bootstrapApiVersion']
            and proof.get('publisherKeySha256') == digest(config['publicKeyXml'].encode())
            and hex_id(proof.get('baseBuildGuid')) and hex_id(proof.get('baseFilesSha256'), 64)
            and proof.get('businessDllExcluded') is True and proof.get('preloadExecutionVerified') is True
            and proof.get('resourcePairVerified') is True, 'Matching verified base client proof is required.')
    require(acceptance or not proof.get('acceptanceOnly', False), 'Acceptance proof cannot authorize production publication.')
    return proof

def verify_package(root, config, acceptance=False, require_proof=False):
    root = plain(root)
    envelope = read_json(root / 'current.json')
    release = validate_release(verify_envelope(envelope, config), config)
    expected = {'current.json': {'sha256': file_hash(root / 'current.json'), 'bytes': (root / 'current.json').stat().st_size}}
    code = inside(root, release['codePath'])
    require(code.is_file() and code.stat().st_size == release['codeBytes'] and file_hash(code) == release['codeSha256'],
            'Signed game DLL checksum/length differs.')
    expected[release['codePath']] = {'sha256': release['codeSha256'], 'bytes': release['codeBytes']}
    prefix = 'resources/' + release['resourceReleaseId'] + '/'
    for name, record in resource_files(root / prefix, release).items():
        expected[prefix + name] = record
    proof = root / 'base-client-proof.json'
    if proof.exists():
        verify_client_proof(proof, release, config, acceptance)
        expected['base-client-proof.json'] = {'sha256': file_hash(proof), 'bytes': proof.stat().st_size}
    require(not require_proof or proof.exists(), 'The formal matching client has not been verified; publication is blocked.')
    manifest = read_json(root / 'package.json')
    require(manifest.get('schemaVersion') == 1 and manifest.get('releaseId') == release['releaseId']
            and manifest.get('sequence') == release['sequence'] and manifest.get('files') == expected
            and (acceptance or not manifest.get('acceptanceOnly')), 'Package inventory differs from authenticated contents.')
    actual = {p.relative_to(root).as_posix() for p in tree_files(root)}
    require(actual == set(expected) | {'package.json'}, 'Package contains unexpected files.')
    for p in root.rglob('*'):
        plain(p)
    return release, manifest

def build_package(code_dir, resource_report, output, config, proof=None, acceptance=False):
    code_dir, output = plain(code_dir), plain(output)
    require(not output.exists(), 'Package directory is immutable; choose a new output directory.')
    report = read_json(resource_report)
    code_report = read_json(code_dir / 'code-build-report.json')
    release = validate_release(verify_envelope(read_json(code_dir / 'current.json'), config), config)
    require(code_report.get('status') == 'succeeded' and code_report.get('resourceCompatibilityVerified')
            and code_report.get('codeSha256') == release['codeSha256']
            and (acceptance or not code_report.get('probeVariant')), 'Production code build proof is missing or is a probe.')
    require(report.get('status') == 'succeeded' and report.get('allBundlesNativeCrcVerified')
            and report.get('assetDependencyOrderVerified') and report.get('resourceReleaseId') == release['resourceReleaseId']
            and report.get('scriptTypeHash') == release['scriptTypeHash'], 'Matching native resource build proof is required.')
    resources = plain(Path(report['catalogPath']).parent)
    expected_resources = resource_files(resources, release)
    output.mkdir(parents=True)
    expected = {}
    def copy(source, relative, record=None):
        source = plain(source)
        record = record or {'sha256': file_hash(source), 'bytes': source.stat().st_size}
        copy_verified(source, inside(output, relative), record['sha256'], record['bytes'])
        expected[relative] = record
    copy(code_dir / 'current.json', 'current.json')
    copy(code_dir / release['codePath'], release['codePath'], {'sha256': release['codeSha256'], 'bytes': release['codeBytes']})
    prefix = 'resources/' + release['resourceReleaseId'] + '/'
    for name, record in expected_resources.items():
        copy(resources / name, prefix + name, record)
    if proof:
        verify_client_proof(proof, release, config, acceptance)
        copy(proof, 'base-client-proof.json')
    manifest = {'schemaVersion': 1, 'releaseId': release['releaseId'], 'sequence': release['sequence'],
                'createdAtUtc': timestamp(), 'acceptanceOnly': acceptance, 'files': expected}
    write_json(output / 'package.json', manifest)
    verify_package(output, config, acceptance)
    return manifest

def archive_package(package, destination, config, acceptance=False):
    verify_package(package, config, acceptance)
    destination = plain(destination)
    require(not destination.exists(), 'Archive already exists.')
    with tarfile.open(destination, 'w') as archive:
        for p in sorted(plain(package).rglob('*')):
            if p.is_file():
                archive.add(p, arcname=p.relative_to(package).as_posix(), recursive=False)
    return {'sha256': file_hash(destination), 'bytes': destination.stat().st_size}

def extract_archive(archive_path, directory, expected=None):
    directory = plain(directory)
    require(not directory.exists(), 'Extraction directory must be new.')
    directory.mkdir(parents=True)
    seen = set()
    with tarfile.open(plain(archive_path), 'r:') as archive:
        members = archive.getmembers()
        require(0 < len(members) <= 100000, 'Invalid archive member count.')
        if expected is not None:
            require({m.name for m in members} == set(expected), 'Archive does not contain exactly the requested files.')
        for member in members:
            require(member.isfile() and member.name not in seen, 'Only unique regular archive files are allowed.')
            seen.add(member.name)
            target = inside(directory, member.name)
            require(member.size >= 0, 'Invalid archive member size.')
            if expected is not None:
                require(member.size == expected[member.name]['bytes'], 'Archive member size differs from authenticated inventory.')
            target.parent.mkdir(parents=True, exist_ok=True)
            valid = False
            for attempt in range(3):
                with archive.extractfile(member) as source, target.open('wb') as output:
                    shutil.copyfileobj(source, output, 1024 * 1024)
                    output.flush(); os.fsync(output.fileno())
                valid = expected is None or file_hash(target) == expected[member.name]['sha256']
                if valid: break
            require(valid, 'Extracted file checksum differs after bounded retries: ' + member.name)
    return directory

def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('action', choices=['build', 'verify', 'archive'])
    p.add_argument('--config', type=Path, required=True)
    p.add_argument('--package', type=Path, required=True)
    p.add_argument('--code-directory', type=Path)
    p.add_argument('--resource-report', type=Path)
    p.add_argument('--client-proof', type=Path)
    p.add_argument('--archive', type=Path)
    p.add_argument('--acceptance', action='store_true')
    args = p.parse_args(); config = read_json(args.config)
    if args.action == 'build':
        result = build_package(args.code_directory, args.resource_report, args.package, config, args.client_proof, args.acceptance)
    elif args.action == 'verify':
        release, _ = verify_package(args.package, config, args.acceptance)
        result = {'status': 'verified', 'releaseId': release['releaseId']}
    else:
        result = archive_package(args.package, args.archive, config, args.acceptance)
    print(json.dumps(result, ensure_ascii=False))

if __name__ == '__main__':
    main()
