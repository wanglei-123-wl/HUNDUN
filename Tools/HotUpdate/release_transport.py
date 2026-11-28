"""Local helpers: describe authenticated files and transfer only missing contents."""
import argparse
import base64
import json
from pathlib import Path
import tarfile
from release_common import *
from release_package import verify_package

def describe(package, config, acceptance=False):
    package = plain(package)
    release, manifest = verify_package(package, config, acceptance)
    root = package / 'resources' / release['resourceReleaseId']
    result = {'envelope': read_json(package / 'current.json'),
              'envelopeBytes': base64.b64encode((package / 'current.json').read_bytes()).decode(),
              'controls': {name: base64.b64encode((root / name).read_bytes()).decode()
                           for name in ('catalog_0.1.json', 'catalog_0.1.hash', 'resource-manifest.json')},
              'packageManifest': manifest}
    if (package / 'base-client-proof.json').exists():
        result['clientProof'] = base64.b64encode((package / 'base-client-proof.json').read_bytes()).decode()
    return result

def transfer_archive(package, descriptor, offer, archive_path):
    require(not Path(archive_path).exists(), 'Transfer archive already exists.')
    require(offer['releaseId'] == descriptor['packageManifest']['releaseId'], 'Offer belongs to another release.')
    expected = descriptor['packageManifest']['files']
    try:
        with tarfile.open(archive_path, 'w') as archive:
            import io
            raw = json_bytes(descriptor['packageManifest']); member = tarfile.TarInfo('package.json'); member.size = len(raw)
            archive.addfile(member, io.BytesIO(raw))
            for name in offer['missingFiles']:
                require(name in expected, 'Server requested an unlisted file.')
                path = inside(package, name)
                require(path.is_file() and file_hash(path) == expected[name]['sha256'], 'Local transfer file changed.')
                archive.add(path, arcname=name, recursive=False)
        return {'bytes': Path(archive_path).stat().st_size, 'sha256': file_hash(archive_path)}
    except BaseException:
        # A partial transport archive is regenerable from the immutable package.
        Path(archive_path).unlink(missing_ok=True)
        raise

if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('action', choices=['describe', 'archive'])
    p.add_argument('--package', type=Path, required=True)
    p.add_argument('--config', type=Path, required=True)
    p.add_argument('--output', type=Path, required=True)
    p.add_argument('--offer', type=Path)
    p.add_argument('--descriptor', type=Path)
    p.add_argument('--acceptance', action='store_true')
    a = p.parse_args()
    if a.action == 'describe': write_json(a.output, describe(a.package, read_json(a.config), a.acceptance))
    else: print(json.dumps(transfer_archive(a.package, read_json(a.descriptor), read_json(a.offer), a.output)))
