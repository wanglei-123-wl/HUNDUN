"""Owned hot-update channel store. Legacy resources/backend directories are out of scope."""
from __future__ import annotations
import argparse
import base64
import copy
import http.client
import sys
import tarfile
import uuid
import urllib.request
from release_common import *
from release_package import verify_package, verify_client_proof, extract_archive
from release_retention import plan_retention

class Store:
    def __init__(self, root):
        self.root = plain(root)
        self.settings = read_json(self.root / 'store.json')
        self.public = plain(self.settings['publicRoot'])
        require(self.root != self.public and not self.root.is_relative_to(self.public)
                and not self.public.is_relative_to(self.root), 'Private and public roots must be disjoint.')
        self.config = self.settings['trustedConfig']
        self.policy = read_json(self.root / 'policy.json')
        self.acceptance = self.settings['acceptanceOnly']

    @classmethod
    def initialize(cls, root, public, config, policy, acceptance=False):
        root, public = plain(root), plain(public)
        require(root != public and not root.is_relative_to(public) and not public.is_relative_to(root), 'Roots overlap.')
        require(not (root / 'store.json').exists(), 'Store already initialized; trust configuration is immutable.')
        require(not root.exists() or not any(root.iterdir()), 'Private store directory is not empty.')
        require(not public.exists() or not any(public.iterdir()), 'New channel public directory is not empty; import is required.')
        new_public_parents=[]; parent=public
        while not parent.exists(): new_public_parents.append(parent); parent=parent.parent
        root.mkdir(parents=True, exist_ok=True); public.mkdir(parents=True, exist_ok=True)
        if os.name != 'nt':
            root.chmod(0o700)
            for path in new_public_parents+[public]: path.chmod(0o755)
        require(root.stat().st_dev == public.stat().st_dev, 'Private/public stores must share a filesystem for immutable hard links.')
        for name in ('incoming', 'staging', 'blobs', 'audit', 'trash'):
            (root / name).mkdir()
        for name in ('releases', 'resources'):
            (public / name).mkdir()
            if os.name != 'nt': (public / name).chmod(0o755)
        write_json(root / 'store.json', {'schemaVersion': 1, 'publicRoot': str(public), 'trustedConfig': config, 'acceptanceOnly': acceptance})
        write_json(root / 'policy.json', policy)
        write_json(root / 'state.json', {'schemaVersion': 1, 'currentId': None, 'highestSequence': 0,
                   'publications': {}, 'resources': {}, 'blobs': {}, 'uploads': {}, 'generation': 0})
        return cls(root)

    def state(self):
        state = read_json(self.root / 'state.json')
        require(state.get('schemaVersion') == 1 and isinstance(state.get('generation'), int), 'Invalid store state.')
        require(state.get('currentId') is None or hex_id(state['currentId']), 'Invalid current publication ID.')
        for key in state['resources']: require(hex_id(key), 'Invalid resource registry ID.')
        for key in state['blobs']: require(hex_id(key, 64), 'Invalid blob registry hash.')
        for key, publication in state['publications'].items():
            require(hex_id(key) and publication['release']['releaseId'] == key, 'Invalid publication registry ID.')
            verified = validate_release(verify_envelope(publication['descriptor']['envelope'], self.config), self.config)
            require(verified == publication['release'], 'Publication registry was changed after signature verification.')
        for key in state['uploads']: require(hex_id(key), 'Invalid upload registry ID.')
        return state

    def save(self, state):
        state['generation'] += 1
        write_json(self.root / 'state.json', state)

    def audit(self, action, detail):
        write_json(self.root / 'audit' / (utcnow().strftime('%Y%m%dT%H%M%S%fZ') + '-' + uuid.uuid4().hex + '.json'),
                   {'action': action, 'timeUtc': timestamp(), 'detail': detail})

    def pointer_hash(self):
        path = self.public / 'current.json'
        return file_hash(path) if path.exists() else None

    def recover_transaction(self):
        path = self.root / 'pending-publication.json'
        if not path.exists():
            self.recover_cleanup()
            return
        pending = read_json(path); state = self.state()
        if state.get('committedTransaction') == pending['transactionId'] and self.pointer_hash() == pending['newHash']:
            path.unlink(); self.recover_cleanup(); return
        require(self.pointer_hash() in (pending['newHash'], pending['oldHash']), 'Public pointer changed outside the publication transaction.')
        if pending['oldEnvelope'] is None:
            if (self.public / 'current.json').exists():
                (self.public / 'current.json').unlink()
        else:
            atomic(self.public / 'current.json', base64.b64decode(pending['oldEnvelope']), 0o644)
        write_json(self.root / 'state.json', pending['oldState'])
        self.audit('recover-interrupted-publication', {'releaseId': pending['releaseId']})
        path.unlink()
        self.recover_cleanup()

    def check_pointer(self, state):
        current = state.get('currentId')
        require(self.pointer_hash() == (state['publications'][current]['envelopeSha256'] if current else None),
                'Public pointer does not match the verified registry; cleanup/publish stopped.')

    def offer(self, descriptor):
        """An authenticated small descriptor precedes any large content transfer."""
        state = self.state(); self.check_pointer(state)
        r = validate_release(verify_envelope(descriptor['envelope'], self.config), self.config)
        rid = r['releaseId']; controls = {}
        def control(name, size, sha):
            raw = base64.b64decode(descriptor['controls'][name], validate=True)
            require(len(raw) == size and digest(raw) == sha, 'Offer control file hash differs: ' + name)
            controls[name] = raw
            return raw
        manifest_bytes = control('resource-manifest.json', r['resourceManifestBytes'], r['resourceManifestSha256'])
        catalog = control('catalog_0.1.json', r['resourceCatalogBytes'], r['resourceCatalogSha256'])
        hash_bytes = base64.b64decode(descriptor['controls']['catalog_0.1.hash'], validate=True)
        require(hash_bytes.decode().strip() == r['resourceCatalogHash'], 'Offered catalog hash differs.')
        controls['catalog_0.1.hash'] = hash_bytes
        manifest = json.loads(manifest_bytes)
        pairs = {'resourceReleaseId': 'resourceReleaseId', 'scriptTypeHash': 'scriptTypeHash', 'catalogHash': 'resourceCatalogHash',
                 'catalogSha256': 'resourceCatalogSha256', 'catalogBytes': 'resourceCatalogBytes', 'buildRoot': 'resourceBuildRoot',
                 'target': 'target', 'unityVersion': 'unityVersion', 'gameAssemblyName': 'assemblyName'}
        require(manifest.get('schemaVersion') == 1 and all(manifest.get(k) == r[v] for k, v in pairs.items()), 'Offer resource binding differs.')
        prefix = 'resources/' + r['resourceReleaseId'] + '/'
        files = {r['codePath']: {'sha256': r['codeSha256'], 'bytes': r['codeBytes']}}
        files.update({prefix + name: {'sha256': digest(raw), 'bytes': len(raw)} for name, raw in controls.items()})
        for b in manifest['bundles']:
            require(re.fullmatch(r'bundles/[A-Za-z0-9_.-]+\.bundle', b['relativePath'])
                    and hex_id(b['sha256'], 64) and b['bytes'] > 0 and hex_id(b['cacheHash'])
                    and 0 < b['crc'] <= 0xffffffff and b['bundleName'], 'Malformed offered bundle.')
            name = prefix + b['relativePath']; require(name not in files, 'Duplicate offered bundle.')
            files[name] = {'sha256': b['sha256'], 'bytes': b['bytes']}
        envelope_bytes = base64.b64decode(descriptor['envelopeBytes'], validate=True)
        require(json.loads(envelope_bytes) == descriptor['envelope'], 'Offer envelope bytes differ.')
        files['current.json'] = {'sha256': digest(envelope_bytes), 'bytes': len(envelope_bytes)}
        if descriptor.get('clientProof'):
            proof = base64.b64decode(descriptor['clientProof'], validate=True)
            verify_envelope(json.loads(proof), self.config)
            files['base-client-proof.json'] = {'sha256': digest(proof), 'bytes': len(proof)}
        require(descriptor['packageManifest']['files'] == files and descriptor['packageManifest']['releaseId'] == rid
                and (self.acceptance or not descriptor['packageManifest'].get('acceptanceOnly')), 'Offered package inventory differs.')
        if rid in state['publications']:
            require(state['publications'][rid]['envelopeSha256'] == digest(envelope_bytes), 'Publication IDs are immutable.')
            if rid == state.get('currentId'):
                return {'releaseId': rid, 'status': 'already-current', 'missingFiles': [], 'missingBytes': 0}
        if rid in state['uploads']:
            require(state['uploads'][rid]['envelopeSha256'] == digest(envelope_bytes), 'Upload ID reused with different contents.')
        require(r['sequence'] > state['highestSequence'] or rid == state.get('currentId'), 'Publication sequence is not newer; sign a new publication for rollback.')
        if os.name == 'nt':
            for name in files:
                # The live server runs Linux. Local Windows acceptance must use
                # a short store/public root, including real long bundle names.
                require(max(len(str(self.root / 'incoming' / 'x-12345678' / name)),
                            len(str(self.root / 'staging' / rid / name)), len(str(self.public / name))) < 250,
                        'Local Windows store path is too long; use a short isolated root such as C:/QiDaoBuild/S4Server.')
        missing = []
        for name, item in files.items():
            blob = inside(self.root, 'blobs/' + item['sha256'])
            if not blob.exists() or blob.stat().st_size != item['bytes'] or file_hash(blob) != item['sha256']:
                missing.append(name)
        # Archive, extracted missing files and new immutable blobs coexist until
        # the receive transaction finishes. Hard-linked staging adds no data copy.
        required = sum(files[name]['bytes'] for name in missing) * 3 + len(json_bytes(descriptor)) * 4 + 1048576
        free = shutil.disk_usage(self.root).free
        require(free >= required, f'Insufficient disk space: required={required}, available={free}. Protected versions were not removed.')
        state['uploads'][rid] = {'release': r, 'files': files, 'descriptor': descriptor, 'status': 'offered',
                                'updatedAtUtc': timestamp(), 'envelopeSha256': digest(envelope_bytes), 'missing': missing}
        self.save(state)
        return {'releaseId': rid, 'missingFiles': missing, 'missingBytes': sum(files[n]['bytes'] for n in missing),
                'requiredFreeBytes': required, 'availableFreeBytes': free}

    def receive(self, rid, stream, size, sha):
        require(hex_id(rid) and hex_id(sha, 64) and size > 0, 'Invalid transfer parameters.')
        state = self.state(); upload = state['uploads'][rid]
        require(upload['status'] in ('offered', 'failed'), 'Upload is not offered.')
        free = shutil.disk_usage(self.root).free
        require(free >= size + 2 * sum(upload['files'][n]['bytes'] for n in upload['missing']) + 1048576,
                'Insufficient space for archive, extraction and new immutable blobs.')
        upload['status'] = 'receiving'; upload['updatedAtUtc'] = timestamp(); self.save(state)
        archive = inside(self.root, 'incoming/' + rid + '.tar')
        extraction = inside(self.root, 'incoming/x-' + uuid.uuid4().hex[:8])
        try:
            with archive.open('wb') as output:
                remaining = size
                while remaining:
                    block = stream.read(min(1024 * 1024, remaining))
                    require(block, 'Transfer was interrupted.')
                    output.write(block); remaining -= len(block)
                output.flush(); os.fsync(output.fileno())
            require(file_hash(archive) == sha, 'Transferred archive checksum differs.')
            descriptor = upload['descriptor']; manifest_bytes = json_bytes(descriptor['packageManifest'])
            expected = {name: upload['files'][name] for name in upload['missing']}
            expected['package.json'] = {'sha256': digest(manifest_bytes), 'bytes': len(manifest_bytes)}
            extract_archive(archive, extraction, expected)
            require((extraction / 'package.json').read_bytes() == manifest_bytes, 'Transferred package inventory differs.')
            actual = {p.relative_to(extraction).as_posix() for p in extraction.rglob('*') if p.is_file()}
            require(actual == set(upload['missing']) | {'package.json'}, 'Transfer contains unrequested or missing files.')
            stage = inside(self.root, 'staging/' + rid)
            if stage.exists(): self.remove_owned(stage)
            stage.mkdir()
            for name, item in upload['files'].items():
                blob = inside(self.root, 'blobs/' + item['sha256'])
                if name in upload['missing']:
                    copy_verified(inside(extraction, name), blob, item['sha256'], item['bytes'])
                require(blob.is_file() and file_hash(blob) == item['sha256'], 'Stored blob differs.')
                destination = inside(stage, name); destination.parent.mkdir(parents=True, exist_ok=True)
                os.link(blob, destination)
                if os.name != 'nt': blob.chmod(0o444)
                state['blobs'][item['sha256']] = {'bytes': item['bytes']}
            atomic(stage / 'package.json', manifest_bytes)
            release, _ = verify_package(stage, self.config, self.acceptance)
            resource_prefix = 'resources/' + release['resourceReleaseId'] + '/'
            resource_records = {n[len(resource_prefix):]: rec for n, rec in upload['files'].items() if n.startswith(resource_prefix)}
            if release['resourceReleaseId'] in state['resources']:
                require(state['resources'][release['resourceReleaseId']]['files'] == resource_records, 'Resource IDs cannot be rebound.')
            else:
                state['resources'][release['resourceReleaseId']] = {'files': resource_records}
            prior = state['publications'].get(rid)
            if prior:
                require(prior['envelopeSha256'] == upload['envelopeSha256'], 'Existing publication metadata differs.')
            else:
                state['publications'][rid] = {'release': release, 'status': 'staged', 'stagedAtUtc': timestamp(),
                    'envelopeSha256': upload['envelopeSha256'], 'descriptor': descriptor, 'pinned': False}
            upload['status'] = 'staged'; upload['updatedAtUtc'] = timestamp(); self.save(state)
            self.audit('stage', {'releaseId': rid, 'transferredBytes': size, 'missingBytes': sum(upload['files'][n]['bytes'] for n in upload['missing'])})
            return {'status': 'staged', 'releaseId': rid, 'published': False}
        except BaseException:
            upload['status'] = 'failed'; upload['updatedAtUtc'] = timestamp(); self.save(state)
            raise
        finally:
            if extraction.exists(): self.remove_owned(extraction)
            if archive.exists(): archive.unlink()

    def remove_owned(self, path):
        path = plain(path)
        allowed = [self.root / n for n in ('incoming', 'staging', 'blobs', 'trash')] + [self.public / n for n in ('releases', 'resources')]
        require(any(path != base and path.is_relative_to(base) for base in allowed), 'Deletion is outside an owned artifact subdirectory.')
        if path.is_dir():
            for child in path.iterdir(): self.remove_owned(child)
            path.rmdir()
        elif path.exists():
            path.unlink()

    def http_verify(self, base, files):
        uri = urllib.parse.urlsplit(base)
        require(uri.scheme in ('http', 'https') and not uri.username and not uri.query and not uri.fragment
                and uri.path.rstrip('/') == CHANNEL, 'Public URL must identify the separate hot-update channel.')
        if self.acceptance:
            require(uri.hostname in ('127.0.0.1', 'localhost', '::1'), 'Acceptance stores can verify loopback URLs only.')
        opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
        for name, record in files.items():
            request = urllib.request.Request(base.rstrip('/') + '/' + name + '?verify=' + uuid.uuid4().hex,
                                             headers={'Cache-Control': 'no-cache'})
            checksum = hashlib.sha256(); size = 0
            with opener.open(request, timeout=60) as response:
                require(response.status == 200, 'Public file is unavailable: ' + name)
                while block := response.read(1024 * 1024): checksum.update(block); size += len(block)
            require(size == record['bytes'] and checksum.hexdigest() == record['sha256'], 'Public file checksum/size differs: ' + name)

    def publish(self, rid, public_base):
        require(hex_id(rid), 'Invalid publication ID.')
        state = self.state(); self.check_pointer(state)
        require(rid in state['publications'] and state['publications'][rid]['status'] == 'staged', 'Only a staged unpublished candidate may be published.')
        stage = inside(self.root, 'staging/' + rid)
        release, package = verify_package(stage, self.config, self.acceptance, require_proof=True)
        require(release['sequence'] > state['highestSequence'], 'Publication sequence must increase, including rollback.')
        resource = release['resourceReleaseId']; prefix = 'resources/' + resource + '/'
        resource_records = {name[len(prefix):]: rec for name, rec in package['files'].items() if name.startswith(prefix)}
        if resource in state['resources']:
            require(state['resources'][resource]['files'] == resource_records, 'Resource IDs are immutable.')
        def promote(source, destination, expected):
            destination = plain(destination)
            if destination.exists():
                actual = {p.relative_to(destination).as_posix() for p in tree_files(destination)}
                require(actual == set(expected), 'Existing immutable directory contains unexpected files.')
                for name, record in expected.items():
                    require(file_hash(inside(destination, name)) == record['sha256'], 'Existing immutable public file differs.')
                return
            temp = destination.with_name('.prepare-' + uuid.uuid4().hex)
            temp.mkdir(parents=True)
            for name, record in expected.items():
                origin = inside(source, name); target = inside(temp, name); target.parent.mkdir(parents=True, exist_ok=True)
                require(file_hash(origin) == record['sha256'], 'Immutable promotion source differs.')
                os.link(origin, target)
            if os.name != 'nt':
                for path in [temp, *[p for p in temp.rglob('*') if p.is_dir()]]: path.chmod(0o755)
            os.replace(temp, destination)
        promote(stage / prefix, self.public / prefix, resource_records)
        code_record = package['files'][release['codePath']]
        promote(stage / 'releases' / rid, self.public / 'releases' / rid, {DLL: code_record})
        envelope = (stage / 'current.json').read_bytes()
        transaction = uuid.uuid4().hex
        old = (self.public / 'current.json').read_bytes() if state['currentId'] else None
        pending = {'transactionId': transaction, 'releaseId': rid, 'oldState': state, 'oldHash': self.pointer_hash(),
                   'newHash': digest(envelope), 'oldEnvelope': base64.b64encode(old).decode() if old else None}
        write_json(self.root / 'pending-publication.json', pending)
        try:
            atomic(self.public / 'current.json', envelope, 0o644)
            public_files = {k: v for k, v in package['files'].items() if k != 'base-client-proof.json'}
            self.http_verify(public_base, public_files)
            new = copy.deepcopy(state); now = timestamp()
            if new['currentId']:
                new['publications'][new['currentId']]['retiredAtUtc'] = now
            new['publications'][rid].update(status='published', publishedAtUtc=now, retiredAtUtc=None)
            new['resources'][resource] = {'files': resource_records}
            new['currentId'] = rid; new['highestSequence'] = release['sequence']; new['committedTransaction'] = transaction
            new['lastPublicVerification'] = {'envelopeSha256': digest(envelope), 'verifiedAtUtc': now}
            # Once public links are verified, staging duplicates are not a retained version.
            new['uploads'].pop(rid, None)
            self.save(new)
            (self.root / 'pending-publication.json').unlink()
            self.audit('publish', {'releaseId': rid, 'sequence': release['sequence'], 'publicFilesVerified': len(public_files)})
        except BaseException:
            self.recover_transaction()
            raise
        # Keep small immutable signed metadata, not another full resource directory.
        result = {'status': 'published', 'releaseId': rid, 'sequence': release['sequence']}
        try:
            saved = envelope; self.remove_owned(stage); stage.mkdir(); atomic(stage / 'published-envelope.json', saved)
        except Exception as error:
            result['stagingCleanupWarning'] = str(error)
            self.audit('published-staging-cleanup-failed', result)
        return result

    def inventory(self):
        state = self.state(); self.check_pointer(state)
        unknown = []
        for category, known in [('releases', set(state['publications'])), ('resources', set(state['resources']))]:
            for path in (self.public / category).iterdir():
                plain(path)
                if path.name not in known: unknown.append(str(path))
        for path in (self.root / 'blobs').iterdir():
            plain(path)
            if path.name not in state['blobs']: unknown.append(str(path))
        for path in self.public.iterdir():
            plain(path)
            if path.name not in ('current.json', 'releases', 'resources'): unknown.append(str(path))
        for rid, info in state['resources'].items():
            root = self.public / 'resources' / rid
            if root.exists():
                actual = {p.relative_to(root).as_posix() for p in tree_files(root)}
                if actual != set(info['files']): unknown.append(str(root))
                for path in root.rglob('*'): plain(path)
        for rid in state['publications']:
            root = self.public / 'releases' / rid
            if root.exists():
                if {p.name for p in root.iterdir()} != {DLL}: unknown.append(str(root))
                for path in root.iterdir(): plain(path)
        return state, unknown

    def cleanup_plan(self, now=None):
        state, unknown = self.inventory()
        require(state.get('lastPublicVerification') and state['currentId'], 'No successfully verified publication; automatic cleanup is disabled.')
        plan = plan_retention(state, self.policy, now)
        plan['snapshotSha256'] = digest(json_bytes(state) + json_bytes(self.policy) + (self.pointer_hash() or '').encode())
        plan['unknownPaths'] = unknown
        plan['canApply'] = not unknown
        plan['estimatedContentBytes'] = sum(state['blobs'][h]['bytes'] for h in plan['removeBlobs'])
        return plan

    def cleanup(self, plan, public_base):
        current = self.cleanup_plan()
        require(current['canApply'] and current['snapshotSha256'] == plan['snapshotSha256'], 'Cleanup snapshot changed or unknown files exist; nothing was deleted.')
        age = utcnow() - parse_time(plan['createdAtUtc'])
        require(dt.timedelta(0) <= age <= dt.timedelta(hours=1), 'Cleanup preview is expired or from the future.')
        # Recompute candidates at the caller's signed/recorded planning time.
        exact = self.cleanup_plan(parse_time(plan['createdAtUtc']))
        for key in ('removePublications', 'removeUploads', 'removeResources', 'removeBlobs'):
            require(plan[key] == exact[key], 'Cleanup candidates were altered.')
        state = self.state(); self.check_pointer(state)
        live = state['publications'][state['currentId']]['descriptor']['packageManifest']['files']
        self.http_verify(public_base, {name: record for name, record in live.items() if name != 'base-client-proof.json'})
        self.audit('cleanup-preview', plan)
        write_json(self.root / 'pending-cleanup.json', {'plan': plan, 'pointerHash': self.pointer_hash(), 'state': state})
        return self.recover_cleanup()

    def recover_cleanup(self):
        journal = self.root / 'pending-cleanup.json'
        if not journal.exists(): return None
        pending = read_json(journal); plan = pending['plan']; state = pending['state']
        require(self.pointer_hash() == pending['pointerHash'], 'Public pointer changed during cleanup; recovery stopped.')
        free_before = shutil.disk_usage(self.root).free
        for rid in plan['removePublications']:
            for path in [self.public / 'releases' / rid, self.root / 'staging' / rid]:
                if path.exists(): self.remove_owned(path)
            state['publications'].pop(rid)
        for rid in plan['removeUploads']:
            for path in [self.root / 'staging' / rid, self.root / 'incoming' / (rid + '.tar')]:
                if path.exists(): self.remove_owned(path)
            state['uploads'].pop(rid, None)
        for rid in plan['removeResources']:
            path = self.public / 'resources' / rid
            if path.exists(): self.remove_owned(path)
            state['resources'].pop(rid)
        for sha in plan['removeBlobs']:
            path = self.root / 'blobs' / sha
            if path.exists(): self.remove_owned(path)
            state['blobs'].pop(sha)
        self.save(state)
        result = {'status': 'cleaned', 'diskFreeDeltaBytes': shutil.disk_usage(self.root).free - free_before,
                  'removedPublications': plan['removePublications'], 'removedResources': plan['removeResources']}
        self.audit('cleanup-complete', result)
        journal.unlink()
        return result

def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--root', type=Path, required=True)
    p.add_argument('--request-b64', required=True)
    args = p.parse_args(); request = json.loads(base64.b64decode(args.request_b64, validate=True))
    names = ['release_common.py', 'release_package.py', 'release_retention.py', 'release_store.py']
    tool_hash = digest(''.join(name + ' ' + file_hash(Path(__file__).parent / name) + '\n' for name in names).encode())
    require(request.get('toolchainSha256') == tool_hash, 'Server/client tool versions differ; deploy the matching immutable toolchain.')
    action = request['action']
    if action == 'init':
        Store.initialize(args.root, request['publicRoot'], request['config'], request['policy'], request.get('acceptanceOnly', False))
        result = {'status': 'initialized'}
    else:
        store = Store(args.root)
        with exclusive_lock(store.root / '.publish.lock'):
            pending = (store.root / 'pending-publication.json').exists() or (store.root / 'pending-cleanup.json').exists()
            if action in ('state', 'plan'):
                require(not pending or action == 'state', 'A mutation must recover the interrupted transaction before a cleanup preview.')
            else:
                store.recover_transaction()
            if action == 'offer': result = store.offer(json.load(sys.stdin))
            elif action == 'receive': result = store.receive(request['releaseId'], sys.stdin.buffer, request['bytes'], request['sha256'])
            elif action == 'publish':
                result = store.publish(request['releaseId'], request['publicBaseUrl'])
                try:
                    plan = store.cleanup_plan(); write_json(store.root / 'last-cleanup-plan.json', plan)
                    result['cleanup'] = store.cleanup(plan, request['publicBaseUrl']) if plan['canApply'] else {'status': 'protected-unknown-files', 'paths': plan['unknownPaths']}
                except Exception as error:
                    result['cleanup'] = {'status': 'failed-after-successful-publish', 'error': str(error)}
            elif action == 'plan': result = store.cleanup_plan()
            elif action == 'cleanup': result = store.cleanup(json.load(sys.stdin), request['publicBaseUrl'])
            elif action == 'state':
                state = store.state()
                if not pending: store.check_pointer(state)
                result = {'state': state, 'freeBytes': shutil.disk_usage(store.root).free, 'pendingRecovery': pending}
            elif action == 'abort':
                state = store.state(); upload = state['uploads'][request['releaseId']]
                require(upload['status'] != 'staged', 'A verified candidate must be superseded or explicitly unpinned, not aborted.')
                upload['status'] = 'failed'; upload['updatedAtUtc'] = timestamp(); store.save(state)
                result = {'status': 'abandoned'}
            elif action == 'pin':
                state = store.state(); state['publications'][request['releaseId']]['pinned'] = bool(request['pinned']); store.save(state)
                result = {'status': 'updated'}
            else: raise ValueError('Unknown store action.')
    print(json.dumps(result, ensure_ascii=False))

if __name__ == '__main__':
    main()
