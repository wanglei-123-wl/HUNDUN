"""Run the same isolated Windows base player against signed code publications."""
import argparse
import base64
import hashlib
import json
import pathlib
import shutil
import subprocess
import uuid
import concurrent.futures

parser = argparse.ArgumentParser()
parser.add_argument("--artifact", type=pathlib.Path, required=True)
parser.add_argument("--cache-parent", type=pathlib.Path, required=True)
args = parser.parse_args()
art = args.artifact.resolve()
web = art / "Web"
url = json.loads((art / "http-server.json").read_text())["baseUrl"] + "current.json"
exe = art / "Acceptance/BasePlayer/HotUpdateAcceptance.exe"
bundle = art / "Acceptance/ProbeBundle/hotcodeprobe.bundle"
before = hashlib.sha256(exe.read_bytes()).hexdigest()
base_directory = exe.parent
base_files_before = {p.relative_to(base_directory).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
                     for p in base_directory.rglob("*") if p.is_file()}
run = uuid.uuid4().hex
cache = args.cache_parent / ("Accepted-" + run)
results = []


def publish(variant):
    source = art / ("Code" + variant)
    shutil.copytree(source / "releases", web / "releases", dirs_exist_ok=True)
    shutil.copy2(source / "current.json", web / "current.json")


def case(name, *, version=None, success=True, recovery=False, directory=None, restart=False):
    report = art / ("native-" + name + ".json")
    log = art / ("native-" + name + ".log")
    command = [str(exe), "-batchmode", "-nographics", "-hotupdate-probe-report", str(report),
               "-hotupdate-probe-url", url, "-hotupdate-probe-cache", str(directory or cache),
               "-hotupdate-probe-bundle", str(bundle), "-logFile", str(log)]
    if recovery:
        command.append("-hotupdate-recovery")
    startup = subprocess.STARTUPINFO()
    startup.dwFlags |= subprocess.STARTF_USESHOWWINDOW
    startup.wShowWindow = 0
    process = subprocess.Popen(command, startupinfo=startup)
    try:
        code = process.wait(timeout=45)
    except subprocess.TimeoutExpired:
        process.kill()
        process.wait()
        raise AssertionError("Owned acceptance player timed out: " + name)
    data = json.loads(report.read_text(encoding="utf-8-sig"))
    assert data["succeeded"] == success, (name, data)
    assert code == (0 if success else 1), (name, code)
    assert data["preloadedGameAssembly"] is False, (name, "game DLL was in the base")
    if success:
        assert data["codeStartupConfirmed"] is True, (name, "resource downloading would leave code marked as crashed")
        assert data["assemblyLocation"] == "" and data["version"] == version, (name, data)
        assert data["prefabResult"] == "game-" + version + "-42", (name, data)
        assert data["scriptableObjectResult"] == version + "-73", (name, data)
    if restart:
        assert data["requiresRestart"] is True, (name, data)
    results.append(dict(name=name, report=str(report), expectedSuccess=success,
                        version=data.get("version"), recovered=data["recovered"], passed=True))
    print("NATIVE_CASE_PASSED " + name, flush=True)


publish("A")
shared = args.cache_parent / ("Concurrent-" + run)
with concurrent.futures.ThreadPoolExecutor(max_workers=2) as executor:
    futures = [executor.submit(case, "concurrent-" + str(i), version="A", directory=shared) for i in range(2)]
    for future in futures:
        future.result()
case("first-a", version="A")
publish("B")
case("update-b", version="B")
b_code = cache / "releases" / ("b" * 32) / "QiDao.GameLogic.dll.bytes"
mtime = b_code.stat().st_mtime_ns
case("warm-b", version="B")
assert b_code.stat().st_mtime_ns == mtime, "A warm start rewrote valid cached game code."

envelope = json.loads((web / "current.json").read_text())
signature = bytearray(base64.b64decode(envelope["signature"]))
signature[0] ^= 1
envelope["signature"] = base64.b64encode(signature).decode()
(web / "current.json").write_text(json.dumps(envelope))
case("invalid-signature", success=False, directory=args.cache_parent / ("BadSignature-" + run))

publish("B")
served = web / "releases" / ("b" * 32) / "QiDao.GameLogic.dll.bytes"
valid = served.read_bytes()
damaged = bytearray(valid)
damaged[100] ^= 1
served.write_bytes(damaged)
try:
    case("damaged-code", success=False, directory=args.cache_parent / ("Damaged-" + run))
finally:
    served.write_bytes(valid)

publish("A")
case("replay-old-manifest", version="B")
publish("FAIL")
case("failed-entry", success=False, restart=True)
case("recover-after-failure", version="B", recovery=True)
case("quarantined-version", version="B")

cold = args.cache_parent / ("ColdFailure-" + run)
case("cold-failed-entry", success=False, directory=cold, restart=True)
publish("B")
case("cold-restart-fetches-fix", version="B", directory=cold, recovery=True)
assert hashlib.sha256(exe.read_bytes()).hexdigest() == before, "The base executable changed during code updates."
base_files_after = {p.relative_to(base_directory).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
                    for p in base_directory.rglob("*") if p.is_file()}
assert base_files_after == base_files_before, "Base player files changed during the code update tests."
(art / "native-acceptance-summary.json").write_text(json.dumps(dict(
    status="succeeded", baseExecutable=str(exe), baseExecutableSha256=before,
    sameBaseExecutableForAllCases=True, cacheDirectory=str(cache), cases=results,
    warmCodeCacheReusedWithoutRewrite=True, allBasePlayerFilesUnchanged=True,
    basePlayerFileHashes=base_files_before), ensure_ascii=False, indent=2), encoding="utf-8")
print("NATIVE_CODE_UPDATE_ACCEPTANCE_OK", flush=True)
