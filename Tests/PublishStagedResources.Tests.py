"""Run the server publisher against isolated files and a mock HTTP endpoint."""
import hashlib
import json
import os
from pathlib import Path
import shlex
import shutil
import subprocess
import uuid

PROJECT = Path(__file__).resolve().parents[1]
BASH = Path(r"C:\Program Files\Git\bin\bash.exe")
ROOT = Path(r"C:\QiDaoBuild") / ("PublisherTests-" + uuid.uuid4().hex)
SCRIPT = (PROJECT / "Tools/Publish-StagedResources.sh").read_text(encoding="utf-8")
ENVIRONMENT = dict(os.environ, MSYS="winsymlinks:sys")
RELEASE = "f96e32b65901488e80636c8bea3829dc"
OLD = "1" * 32
NEW = "2" * 32


def posix(path):
    text = str(path).replace("\\", "/")
    return "/" + text[0].lower() + text[2:]


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def run(case, fail_after=False, tamper=False, stale=False, live_directory=False):
    root = ROOT / case
    staging = root / "upload" / RELEASE
    payload = staging / "RemotePayload"
    old = root / "versions/old"
    site = root / "site"
    commands = root / "bin"
    for directory in (payload, old, site, commands):
        directory.mkdir(parents=True, exist_ok=True)
    (old / "catalog_0.1.hash").write_text(OLD, encoding="ascii")
    (old / "catalog_0.1.json").write_text('{"old":true}', encoding="ascii")
    (old / "old-client.bundle").write_bytes(b"retained old bundle")
    (payload / "catalog_0.1.hash").write_text(NEW, encoding="ascii")
    (payload / "catalog_0.1.json").write_text('{"new":true}', encoding="ascii")
    (payload / "new-backgrounds.bundle").write_bytes(b"verified new backgrounds")
    manifest = staging / "release-manifest.json"
    manifest.write_text('{"releaseId":"' + RELEASE + '"}', encoding="ascii")
    sums = staging / "SHA256SUMS"
    sums.write_text("".join(digest(path) + "  " + path.name + "\n" for path in sorted(payload.iterdir())), encoding="ascii", newline="\n")
    if tamper:
        (payload / "new-backgrounds.bundle").write_bytes(b"corrupt resource")
    patched = SCRIPT.replace("/srv/hundun-upload", posix(root / "upload"))
    patched = patched.replace("/srv/hundun-resources", posix(site))
    patched = patched.replace("/srv/hundun-releases", posix(root / "versions"))
    patched = patched.replace("/srv/hundun-release-publish.lock", posix(root / "publish.lock"))
    script = root / "publish.sh"
    script.write_text(patched, encoding="utf-8", newline="\n")
    (commands / "flock").write_text("#!/bin/bash\nexit 0\n", encoding="ascii", newline="\n")
    (commands / "curl").write_text('''#!/bin/bash
url="${@: -1}"
request="${url%%\\?*}"
name="${request##*/}"
if [[ "${MOCK_FAIL_AFTER:-0}" == 1 && "$url" == *after=* && "$name" == catalog_0.1.hash ]]; then
    printf '00000000000000000000000000000000'
else
    cat "$MOCK_SITE/StandaloneWindows64/$name"
fi
''', encoding="ascii", newline="\n")
    setup = "ln -s -- " + shlex.quote(posix(old)) + " " + shlex.quote(posix(site / "StandaloneWindows64"))
    if live_directory:
        shutil.copytree(old, site / "StandaloneWindows64")
    else:
        subprocess.run([str(BASH), "-c", setup], check=True, capture_output=True, env=ENVIRONMENT)
    command = "export PATH=" + shlex.quote(posix(commands)) + ':"$PATH"; '
    command += "export MOCK_SITE=" + shlex.quote(posix(site)) + "; "
    command += "export MOCK_FAIL_AFTER=" + ("1" if fail_after else "0") + "; "
    command += "bash " + shlex.quote(posix(script)) + " " + " ".join(map(shlex.quote, [
        RELEASE, digest(manifest), "3" * 32 if stale else OLD, NEW, digest(sums)]))
    result = subprocess.run([str(BASH), "-c", command], capture_output=True, text=True, encoding="utf-8", errors="replace", env=ENVIRONMENT)
    (root / "output.txt").write_text(result.stdout + result.stderr, encoding="utf-8")
    target = subprocess.run([str(BASH), "-c", "readlink -f -- " + shlex.quote(posix(site / "StandaloneWindows64"))],
                            check=True, capture_output=True, text=True, env=ENVIRONMENT).stdout.strip()
    if fail_after or tamper or stale:
        assert result.returncode != 0, result.stdout
        if live_directory and fail_after:
            assert target.startswith(posix(root / "versions/previous-")), target
            restored = subprocess.run([str(BASH), "-c", "cat " + shlex.quote(posix(site / "StandaloneWindows64/catalog_0.1.hash"))],
                                      check=True, capture_output=True, text=True, env=ENVIRONMENT).stdout
            assert restored == OLD
        else:
            assert target == posix(old), (case, target, result.stdout, result.stderr)
        if fail_after:
            assert "ROLLED_BACK_TO=" in result.stderr
    else:
        assert result.returncode == 0, result.stdout + result.stderr
        assert target == posix(root / "versions" / RELEASE), (target, posix(root / "versions" / RELEASE))
        assert (root / "versions" / RELEASE / "old-client.bundle").exists()
        assert (root / "versions" / RELEASE / "catalog_0.1.hash").read_text() == NEW
        assert "ROLLBACK_COMMAND=" in result.stdout
    return {"case": case, "passed": True, "exitCode": result.returncode}


results = [run("success"), run("tampered", tamper=True), run("rollback", fail_after=True), run("stale", stale=True),
           run("directory-success", live_directory=True), run("directory-rollback", live_directory=True, fail_after=True)]
(ROOT / "results.json").write_text(json.dumps({"status": "succeeded", "cases": results}, indent=2), encoding="utf-8")
print("PASS: atomic publish, retained old bundle URLs, hash rejection, automatic rollback and stale-catalog rejection.")
print(ROOT / "results.json")
