"""Shared, dependency-free verification for the Windows builder and Linux store."""
from __future__ import annotations
import base64
import contextlib
import datetime as dt
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import stat
import sys
import tempfile
import urllib.parse
import xml.etree.ElementTree as ET

CHANNEL = '/client-hotupdate/windows-mono-v1'
DLL = 'QiDao.GameLogic.dll.bytes'
TARGET = 'windows-x64-mono'
if sys.version_info < (3, 11):
    raise RuntimeError('Python 3.11 or later is required by the release tools.')

def require(ok, message):
    if not ok:
        raise ValueError(message)

def hex_id(value, length=32):
    return isinstance(value, str) and re.fullmatch('[a-f0-9]{%d}' % length, value) is not None

def utcnow():
    return dt.datetime.now(dt.timezone.utc)

def timestamp(value=None):
    return (value or utcnow()).isoformat()

def parse_time(value):
    result = dt.datetime.fromisoformat(value.replace('Z', '+00:00'))
    require(result.tzinfo is not None, 'UTC offset is required.')
    return result.astimezone(dt.timezone.utc)

def digest(data):
    return hashlib.sha256(data).hexdigest()

def file_hash(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()

def read_json(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))

def json_bytes(value):
    return json.dumps(value, ensure_ascii=False, sort_keys=True, separators=(',', ':')).encode('utf-8')

def plain(path):
    """Reject every existing symlink/junction component, including the root."""
    path = Path(os.path.abspath(path))
    for part in [path, *path.parents]:
        if part.exists() or part.is_symlink():
            info = part.lstat()
            require(not stat.S_ISLNK(info.st_mode) and not getattr(info, 'st_file_attributes', 0) & 1024,
                    'Symlinks/reparse points are not allowed: ' + str(part))
    return path

def inside(root, relative):
    require(isinstance(relative, str) and relative and '\\' not in relative
            and not relative.startswith('/') and ':' not in relative
            and all(p not in ('', '.', '..') for p in relative.split('/')), 'Unsafe relative path.')
    root = plain(root)
    path = plain(root / relative)
    require(path != root and path.is_relative_to(root), 'Path escapes the owned root.')
    return path

def tree_files(root):
    root = plain(root)
    result = []
    if not root.exists(): return result
    for child in root.iterdir():
        plain(child)
        if child.is_dir(): result.extend(tree_files(child))
        elif child.is_file(): result.append(child)
        else: raise ValueError('Only regular files/directories are allowed: ' + str(child))
    return result

def atomic(path, data, mode=None):
    path = plain(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    name = None
    try:
        with tempfile.NamedTemporaryFile(prefix='.write-', dir=path.parent, delete=False) as stream:
            name = Path(stream.name)
            stream.write(data)
            stream.flush()
            os.fsync(stream.fileno())
        if mode is not None and os.name != 'nt': name.chmod(mode)
        os.replace(name, path)
    finally:
        if name and name.exists():
            name.unlink()

def write_json(path, data):
    atomic(path, json_bytes(data))

def copy_verified(source, target, expected, size):
    source, target = plain(source), plain(target)
    require(source.is_file() and source.stat().st_size == size and file_hash(source) == expected,
            'Source checksum/size differs: ' + str(source))
    target.parent.mkdir(parents=True, exist_ok=True)
    for attempt in range(3):
        temp = target.with_name('.copy-' + os.urandom(8).hex())
        try:
            with source.open('rb') as inp, temp.open('xb') as out:
                shutil.copyfileobj(inp, out, 1024 * 1024)
                out.flush()
                os.fsync(out.fileno())
            if temp.stat().st_size == size and file_hash(temp) == expected and file_hash(source) == expected:
                os.replace(temp, target)
                return
        finally:
            if temp.exists():
                temp.unlink()
    raise ValueError('Copy checksum verification failed: ' + str(target))

@contextlib.contextmanager
def exclusive_lock(path):
    path = plain(path)
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open('a+b') as stream:
        if stream.tell() == 0:
            stream.write(b'0'); stream.flush()
        stream.seek(0)
        if os.name == 'nt':
            import msvcrt
            msvcrt.locking(stream.fileno(), msvcrt.LK_NBLCK, 1)
        else:
            import fcntl
            fcntl.flock(stream, fcntl.LOCK_EX | fcntl.LOCK_NB)
        try:
            yield
        finally:
            stream.seek(0)
            if os.name == 'nt':
                msvcrt.locking(stream.fileno(), msvcrt.LK_UNLCK, 1)
            else:
                fcntl.flock(stream, fcntl.LOCK_UN)

def verify_envelope(envelope, config):
    require(isinstance(envelope, dict) and len(envelope.get('payload', '')) <= 65536, 'Invalid envelope.')
    payload = base64.b64decode(envelope['payload'], validate=True)
    signature = base64.b64decode(envelope['signature'], validate=True)
    key = ET.fromstring(config['publicKeyXml'])
    number = lambda name: int.from_bytes(base64.b64decode(key.findtext(name), validate=True), 'big')
    modulus, exponent = number('Modulus'), number('Exponent')
    length = (modulus.bit_length() + 7) // 8
    require(length >= 256 and len(signature) == length and int.from_bytes(signature, 'big') < modulus,
            'Invalid RSA signature length/value.')
    decoded = pow(int.from_bytes(signature, 'big'), exponent, modulus).to_bytes(length, 'big')
    # DER DigestInfo for SHA-256. Require the complete PKCS#1 v1.5 encoding.
    tail = bytes.fromhex('3031300d060960864801650304020105000420') + hashlib.sha256(payload).digest()
    expected = b'\x00\x01' + b'\xff' * (length - len(tail) - 3) + b'\x00' + tail
    require(decoded == expected, 'Publisher signature does not verify.')
    return json.loads(payload)

def validate_release(release, config):
    r = release
    require(r.get('schemaVersion') == 1 and hex_id(r.get('releaseId')) and type(r.get('sequence')) is int
            and r['sequence'] > 0 and r.get('target') == TARGET and r.get('unityVersion') == config['unityVersion']
            and r.get('bootstrapApiVersion') == config['bootstrapApiVersion']
            and r.get('assemblyName') == 'QiDao.GameLogic' and r.get('entryType') == 'QiDao.GameLogic.GameEntry'
            and r.get('codePath') == 'releases/' + r['releaseId'] + '/' + DLL
            and type(r.get('codeBytes')) is int and 0 < r['codeBytes'] <= config['maxCodeBytes']
            and hex_id(r.get('codeSha256'), 64), 'Incompatible code release.')
    require(hex_id(r.get('resourceReleaseId')) and hex_id(r.get('resourceCatalogHash'))
            and hex_id(r.get('scriptTypeHash')) and hex_id(r.get('resourceCatalogSha256'), 64)
            and hex_id(r.get('resourceManifestSha256'), 64)
            and 0 < r.get('resourceCatalogBytes', 0) <= 4194304
            and 0 < r.get('resourceManifestBytes', 0) <= 1048576, 'Incomplete resource binding.')
    url = urllib.parse.urlsplit(r['resourceBuildRoot'])
    require(url.scheme in ('http', 'https') and url.netloc and not url.username and not url.password
            and not url.query and not url.fragment and url.path == CHANNEL + '/resources/' + r['resourceReleaseId'],
            'Unexpected immutable resource root.')
    return r

def channel_key(release):
    return '|'.join(str(release[k]) for k in ('target', 'unityVersion', 'bootstrapApiVersion'))

def resource_files(directory, release):
    root = plain(directory)
    expected = {}
    for name, size, sha in [('catalog_0.1.json', release['resourceCatalogBytes'], release['resourceCatalogSha256']),
                            ('resource-manifest.json', release['resourceManifestBytes'], release['resourceManifestSha256'])]:
        path = inside(root, name)
        require(path.is_file() and path.stat().st_size == size and file_hash(path) == sha, 'Resource control file differs: ' + name)
        expected[name] = {'sha256': sha, 'bytes': size}
    hash_path = inside(root, 'catalog_0.1.hash')
    require(hash_path.read_text(encoding='utf-8').strip() == release['resourceCatalogHash'], 'Catalog hash differs.')
    expected['catalog_0.1.hash'] = {'sha256': file_hash(hash_path), 'bytes': hash_path.stat().st_size}
    manifest = read_json(root / 'resource-manifest.json')
    pairs = {'resourceReleaseId': 'resourceReleaseId', 'scriptTypeHash': 'scriptTypeHash', 'catalogHash': 'resourceCatalogHash',
             'catalogSha256': 'resourceCatalogSha256', 'catalogBytes': 'resourceCatalogBytes', 'buildRoot': 'resourceBuildRoot',
             'target': 'target', 'unityVersion': 'unityVersion', 'gameAssemblyName': 'assemblyName'}
    require(manifest.get('schemaVersion') == 1 and all(manifest.get(k) == release[v] for k, v in pairs.items()),
            'Resource manifest does not match the signed code binding.')
    require(isinstance(manifest.get('bundles'), list) and manifest['bundles'], 'Empty resource manifest.')
    for bundle in manifest['bundles']:
        name = bundle['relativePath']
        require(re.fullmatch(r'bundles/[A-Za-z0-9_.-]+\.bundle', name) and name not in expected
                and hex_id(bundle['sha256'], 64) and hex_id(bundle['cacheHash'])
                and 0 < bundle['crc'] <= 0xffffffff and bundle['bytes'] > 0
                and isinstance(bundle['bundleName'], str) and bundle['bundleName'], 'Invalid resource bundle record.')
        file = inside(root, name)
        require(file.is_file() and file.stat().st_size == bundle['bytes'] and file_hash(file) == bundle['sha256'],
                'Bundle checksum/size differs: ' + name)
        expected[name] = {'sha256': bundle['sha256'], 'bytes': bundle['bytes']}
    actual = {p.relative_to(root).as_posix() for p in tree_files(root)}
    require(actual == set(expected), 'Unlisted resource files are present.')
    for p in root.rglob('*'):
        plain(p)
    return expected
