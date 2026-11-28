"""Package the verified client; export its existing PE icon resources losslessly."""
import ctypes
import hashlib
import json
import shutil
import struct
import subprocess
import sys
from pathlib import Path

sys.stdout.reconfigure(encoding="utf-8")
artifact = Path(sys.argv[1])
report = json.loads((artifact / 'build-report.json').read_text(encoding='utf-8-sig'))
assert report['status'] == 'succeeded', 'A successful native Windows build is required'
delivered = Path(report['outputDirectory'])
assert delivered.parent == Path(r'F:\游戏项目')
source = artifact / 'InstallerPayload'
assert not source.exists(), 'Refusing to reuse an installer payload snapshot'
source.mkdir()
for file in report['files']:
    delivered_file = Path(file['path'])
    staged_file = Path(report['stagingDirectory']) / file['relativePath']
    for original in [delivered_file, staged_file]:
        with original.open('rb') as stream:
            assert hashlib.file_digest(stream, 'sha256').hexdigest() == file['sha256'], str(original)
    destination = source / file['relativePath']
    assert destination.resolve().is_relative_to(source.resolve())
    destination.parent.mkdir(parents=True, exist_ok=True)
    with staged_file.open('rb', buffering=0) as src, destination.open('wb', buffering=0) as dst:
        while chunk := src.read(256 * 1024):
            assert dst.write(chunk) == len(chunk)
    with destination.open('rb') as stream:
        assert hashlib.file_digest(stream, 'sha256').hexdigest() == file['sha256'], str(destination)

kernel = ctypes.WinDLL('kernel32', use_last_error=True)
handle = ctypes.c_void_p
kernel.LoadLibraryExW.argtypes = [ctypes.c_wchar_p, handle, ctypes.c_uint32]
kernel.LoadLibraryExW.restype = handle
kernel.FindResourceW.argtypes = [handle, handle, handle]
kernel.FindResourceW.restype = handle
kernel.LoadResource.argtypes = [handle, handle]
kernel.LoadResource.restype = handle
kernel.LockResource.argtypes = [handle]
kernel.LockResource.restype = handle
kernel.SizeofResource.argtypes = [handle, handle]
kernel.SizeofResource.restype = ctypes.c_uint32
kernel.FreeLibrary.argtypes = [handle]
callback_type = ctypes.WINFUNCTYPE(ctypes.c_bool, handle, handle, handle, ctypes.c_ssize_t)
kernel.EnumResourceNamesW.argtypes = [handle, handle, callback_type, ctypes.c_ssize_t]
kernel.EnumResourceNamesW.restype = ctypes.c_bool
module = kernel.LoadLibraryExW(str(source / '齐道.exe'), None, 0x02 | 0x20)
assert module, ctypes.get_last_error()
try:
    names = []
    def collect(module_handle, resource_type, name, parameter):
        names.append(name if name <= 0xffff else ctypes.wstring_at(name))
        return True
    callback = callback_type(collect)
    assert kernel.EnumResourceNamesW(module, 14, callback, 0) and names, 'No executable icon resource'
    def read_resource(kind, name):
        name_pointer = name if isinstance(name, int) else ctypes.cast(ctypes.c_wchar_p(name), handle)
        resource = kernel.FindResourceW(module, name_pointer, kind)
        assert resource, ctypes.get_last_error()
        size = kernel.SizeofResource(module, resource)
        address = kernel.LockResource(kernel.LoadResource(module, resource))
        assert address and size
        return ctypes.string_at(address, size)
    group = read_resource(14, names[0])
    reserved, icon_type, count = struct.unpack_from('<HHH', group)
    assert reserved == 0 and icon_type == 1 and count
    offset = 6 + count * 16
    entries, images = [], []
    for index in range(count):
        width, height, colors, zero, planes, bits, size, identifier = struct.unpack_from('<BBBBHHIH', group, 6 + index * 14)
        payload = read_resource(3, identifier)
        assert len(payload) == size
        entries.append(struct.pack('<BBBBHHII', width, height, colors, zero, planes, bits, size, offset))
        images.append(payload)
        offset += size
    icon = artifact / 'QiDao.ico'
    icon.write_bytes(struct.pack('<HHH', reserved, icon_type, count) + b''.join(entries) + b''.join(images))
finally:
    kernel.FreeLibrary(module)

output = artifact / 'Installer-final'
output.mkdir(exist_ok=True)
stamp = delivered.name.removeprefix('齐道客户端_')
compiler = Path(r'C:\QiDaoBuild\Tools\InnoSetup\ISCC.exe')
script = artifact / 'QiDao.iss'
command = [str(compiler), '/Qp', '/DClientSource=' + str(source), '/DInstallerOutput=' + str(output), '/DGameIcon=' + str(icon), '/DBuildStamp=' + stamp, '/DAppVersionNumber=' + report['appVersion'], str(script)]
completed = subprocess.run(command, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
(artifact / 'installer-compile.log').write_bytes(completed.stdout)
assert completed.returncode == 0, completed.stdout.decode('utf-8', errors='replace')
installer = next(output.glob('*.exe'))
destination = delivered.parent / installer.name
if destination.exists():
    previous = json.loads((artifact / 'installer-report.json').read_text(encoding='utf-8-sig'))
    assert previous['installerPath'] == str(destination)
    assert hashlib.sha256(destination.read_bytes()).hexdigest() == previous['sha256'], 'Existing installer has changed'
    (artifact / 'installer-report-before-final.json').write_text(json.dumps(previous, ensure_ascii=False, indent=2), encoding='utf-8')
shutil.copyfile(installer, destination)
expected = hashlib.sha256(installer.read_bytes()).hexdigest()
assert hashlib.sha256(destination.read_bytes()).hexdigest() == expected == hashlib.sha256(destination.read_bytes()).hexdigest()
result = dict(status='succeeded', installerPath=str(destination), bytes=destination.stat().st_size,
              sha256=expected, iconPath=str(icon), iconSizesCount=count,
              clientBuildReport=str(artifact / 'build-report.json'), compiler=str(compiler), appVersion=report['appVersion'], payloadDirectory=str(source),
              appId='{D226AB43-70DE-446D-B94B-BE2F22C23584}', cacheDeletionRulesPresent=False,
              defaultDesktopShortcut=True, installerLanguage='Simplified Chinese', installationTested=False, gameLoginTested=False)
(artifact / 'installer-report.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps(result, ensure_ascii=False))
