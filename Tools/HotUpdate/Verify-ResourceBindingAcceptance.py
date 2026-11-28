"""Exercise immutable code/resource pairs in one unchanged native base player."""
import argparse
import base64
import concurrent.futures
import hashlib
import http.server
import json
import pathlib
import subprocess
import threading
import urllib.parse
import uuid

parser = argparse.ArgumentParser()
parser.add_argument('--artifact', type=pathlib.Path, required=True)
parser.add_argument('--cache-parent', type=pathlib.Path, required=True)
parser.add_argument('--full', action='store_true')
parser.add_argument('--reuse-cache', type=pathlib.Path)
parser.add_argument('--base-exe', type=pathlib.Path)
args = parser.parse_args()
art = args.artifact.resolve()
exe = args.base_exe.resolve() if args.base_exe else art / 'BasePlayer/PairedResourceAcceptance.exe'
sha = lambda p: hashlib.file_digest(p.open('rb'), 'sha256').hexdigest()
base_hashes = {p.relative_to(exe.parent).as_posix(): sha(p) for p in exe.parent.rglob('*') if p.is_file()}
roots = {}
for name in ['CodeA1', 'CodeA2', 'CodeB2', 'CodeFAIL', 'CodeBadSchema', 'FullCode']:
    if (art / name / 'releases').exists():
        for release in (art / name / 'releases').iterdir():
            roots['/releases/' + release.name + '/'] = release
for name in ['FixtureResources1', 'FixtureResources2', 'FullResourcesVerified']:
    report = art / name / 'resource-build-report.json'
    if report.exists():
        data = json.loads(report.read_text(encoding='utf-8-sig'))
        roots['/resources/' + data['resourceReleaseId'] + '/'] = pathlib.Path(data['catalogPath']).parent

state = dict(current=b'', requests=[], override={}, switch=None)

class Handler(http.server.BaseHTTPRequestHandler):
    def log_message(self, *_):
        pass

    def do_GET(self):
        path = urllib.parse.urlsplit(self.path).path
        state['requests'].append(path)
        data = state['override'].get(path)
        file = None
        if path == '/current.json':
            data = state['current']
        elif data is None:
            for prefix, root in roots.items():
                if path.startswith(prefix):
                    candidate = (root / path[len(prefix):]).resolve()
                    if candidate.is_relative_to(root.resolve()) and candidate.is_file():
                        file = candidate
                    break
        if data is None and file is None:
            self.send_error(404)
            return
        if state['switch'] and path.endswith('/resource-manifest.json'):
            state['current'] = state['switch']
            state['switch'] = None
        self.send_response(200)
        self.send_header('Content-Length', str(len(data) if data is not None else file.stat().st_size))
        self.end_headers()
        try:
            if data is not None:
                self.wfile.write(data)
            else:
                with file.open('rb') as stream:
                    while chunk := stream.read(262144):
                        self.wfile.write(chunk)
        except ConnectionError:
            pass

server = http.server.ThreadingHTTPServer(('127.0.0.1', 0), Handler)
threading.Thread(target=server.serve_forever, daemon=True).start()
url = 'http://127.0.0.1:' + str(server.server_port) + '/current.json'
run = uuid.uuid4().hex
cache = args.cache_parent.resolve() / run
if args.reuse_cache:
    cache = args.reuse_cache.resolve()
    assert cache.is_relative_to(args.cache_parent.resolve()) and cache.is_dir(), 'Reuse only an existing isolated acceptance cache.'
results = []

def publish(name):
    state['current'] = (art / name / 'current.json').read_bytes()

def case(name, *, version=None, marker=None, success=True, directory=None, full=False, recovery=False):
    report, log = [art / ('native-resource-' + name + suffix) for suffix in ['.json', '.log']]
    report.unlink(missing_ok=True)
    command = [str(exe), '-batchmode', '-nographics', '-resource-probe-cache', str(directory or cache),
               '-resource-probe-url', url, '-resource-probe-report', str(report), '-logFile', str(log)]
    if full:
        command.append('-resource-probe-full')
    if recovery:
        command.append('-hotupdate-recovery')
    startup = subprocess.STARTUPINFO()
    startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW
    startup.wShowWindow = 0
    before = len(state['requests'])
    process = subprocess.Popen(command, startupinfo=startup)
    try:
        code = process.wait(timeout=300 if full else 60)
    except subprocess.TimeoutExpired:
        subprocess.run(['taskkill.exe', '/PID', str(process.pid), '/T', '/F'], stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, check=False)
        process.wait()
        raise AssertionError('Owned acceptance player timed out: ' + name)
    data = json.loads(report.read_text(encoding='utf-8-sig'))
    assert data['succeeded'] == success and code == (0 if success else 1), (name, code, data)
    if success:
        assert data['preloadedVerifiedCode'], 'Unity did not preload the selected verified code before deserialization.'
        assert data['pinned'] and data['cacheVerified'] and data['readyPairCommitted'], (name, data)
        if not full:
            assert data['fixtureAssetsLoaded'] and data['nestedDataRestored'] and data['codeVersion'] == version and data['marker'] == marker, (name, data)
        else:
            assert data['gameContentPrepared'] and data['mapDataCount'] > 0 and data['loginScene'] == 'SampleScene', (name, data)
    requests = state['requests'][before:]
    assert not any('StandaloneWindows64' in p for p in requests), 'Legacy resource channel was used.'
    results.append(dict(name=name, passed=True, report=str(report), result=data, requests=requests))
    print('NATIVE_RESOURCE_CASE_PASSED ' + name, flush=True)
    return data, requests

try:
    if args.full:
        publish('FullCode')
        case('full-game-cold', full=True)
        warm, _ = case('full-game-warm', full=True)
        assert warm['downloadBytes'] == 0
    else:
        publish('CodeA1')
        first, _ = case('cold-a-r1', version='A', marker=73)
        artifact = cache / 'Code/artifacts' / first['codeSha256'] / 'QiDao.GameLogic.dll.bytes'
        stamp = artifact.stat().st_mtime_ns
        publish('CodeA2')
        second, requests = case('resource-only-r2', version='A', marker=74)
        assert first['codeSha256'] == second['codeSha256'] and artifact.stat().st_mtime_ns == stamp
        assert not any(p.startswith('/releases/') for p in requests), 'Resource-only update downloaded unchanged code.'
        publish('CodeB2')
        third, requests = case('code-only-b', version='B', marker=74)
        assert third['downloadBytes'] == 0 and not any(p.endswith('.bundle') for p in requests)
        warm, _ = case('warm-b-r2', version='B', marker=74)
        assert warm['downloadBytes'] == 0
        publish('CodeFAIL')
        case('failed-code-entry', success=False)
        recovered, _ = case('recover-code-and-resource-pair', version='B', marker=74, recovery=True)
        assert recovered['recovered']
        publish('CodeB2')
        manifest = json.loads((roots['/resources/' + second['resourceId'] + '/'] / 'resource-manifest.json').read_text(encoding='utf-8'))
        cached_before = {p: sha(p) for p in (cache / 'Bundles').rglob('__data')}
        changed = next(b for b in manifest['bundles'] if b['bytes'] == second['downloadBytes'])
        damaged = next(p for p in cached_before if p.parent.name == changed['cacheHash'])
        body = bytearray(damaged.read_bytes())
        body[len(body) // 2] ^= 1
        damaged.write_bytes(body)
        repaired, requests = case('repair-one-damaged-cache-entry', version='B', marker=74)
        assert any(p.endswith(changed['relativePath']) for p in requests), 'Damaged cache entry was not redownloaded.'
        assert all(p.exists() and sha(p) == digest for p, digest in cached_before.items() if p != damaged), 'Repair touched unrelated cache versions.'
        control = cache / 'Code/resource-catalogs' / second['resourceId'] / 'catalog_0.1.json'
        control.write_bytes(b'{}')
        _, requests = case('repair-cached-control-file', version='B', marker=74)
        assert any(p.endswith('/catalog_0.1.json') for p in requests)
        publish('CodeA1')
        shared = cache.parent / (run + '-concurrent')
        with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
            futures = [pool.submit(case, 'concurrent-' + str(i), version='A', marker=73, directory=shared) for i in range(2)]
            for future in futures:
                future.result()
        publish('CodeA1')
        state['switch'] = (art / 'CodeB2/current.json').read_bytes()
        frozen, _ = case('pointer-changed-mid-start', version='A', marker=73, directory=cache.parent / (run + '-frozen'))
        assert frozen['resourceId'] == first['resourceId']
        publish('CodeA2')
        r2 = second['resourceId']
        path = '/resources/' + r2 + '/catalog_0.1.hash'
        state['override'][path] = b'f' * 32
        case('wrong-catalog-hash', success=False, directory=cache.parent / (run + '-hash'))
        state['override'].clear()
        publish('CodeBadSchema')
        case('signed-incompatible-schema', success=False, directory=cache.parent / (run + '-schema'))
        publish('CodeA2')
        bundle = manifest['bundles'][0]
        path = '/resources/' + r2 + '/' + bundle['relativePath']
        body = bytearray((roots['/resources/' + r2 + '/'] / bundle['relativePath']).read_bytes())
        body[len(body) // 2] ^= 1
        state['override'][path] = bytes(body)
        case('corrupt-remote-bundle', success=False, directory=cache.parent / (run + '-bundle'))
        state['override'].clear()
        pair_cache = cache.parent / (run + '-recovery')
        publish('CodeA1')
        case('ready-pair-before-failure', version='A', marker=73, directory=pair_cache)
        publish('CodeA2')
        path = '/resources/' + r2 + '/catalog_0.1.hash'
        state['override'][path] = b'f' * 32
        case('new-resource-pair-fails', success=False, directory=pair_cache)
        restored, _ = case('restore-complete-previous-pair', version='A', marker=73, directory=pair_cache, recovery=True)
        assert restored['recovered'] and restored['resourceId'] == first['resourceId']
        state['override'].clear()
        path = '/resources/' + r2 + '/catalog_0.1.json'
        state['override'][path] = b'{}'
        case('corrupt-catalog', success=False, directory=cache.parent / (run + '-catalog'))
        state['override'].clear()
    assert base_hashes == {p.relative_to(exe.parent).as_posix(): sha(p) for p in exe.parent.rglob('*') if p.is_file()}
    output = art / ('native-full-resource-summary.json' if args.full else 'native-resource-summary.json')
    output.write_text(json.dumps(dict(status='succeeded', sameBaseFilesUnchanged=True, cacheDirectory=str(cache), reusedExistingCache=bool(args.reuse_cache),
                                     basePlayerFileHashes=base_hashes, cases=results), ensure_ascii=False, indent=2), encoding='utf-8')
    print('NATIVE_RESOURCE_BINDING_ACCEPTANCE_OK', flush=True)
finally:
    server.shutdown()
    server.server_close()
