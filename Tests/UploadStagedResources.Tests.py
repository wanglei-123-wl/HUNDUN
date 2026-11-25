"""Exercise staged uploads with real server hash checks and isolated transports."""
import base64
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import uuid

PROJECT = Path(__file__).resolve().parents[1]
SCRIPT = PROJECT / "Tools/Upload-StagedResources.ps1"
BASH = Path(r"C:\Program Files\Git\bin\bash.exe")
ROOT = Path(r"C:\QiDaoBuild") / ("UploadResumeTests-" + uuid.uuid4().hex)


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def quote(value):
    return "'" + str(value).replace("'", "''") + "'"


def posix(path):
    value = str(path).replace("\\", "/")
    return "/" + value[0].lower() + value[2:]


def run(case, mode="resume", damage=None):
    root = ROOT / case
    release = root / uuid.uuid4().hex
    payload = release / "RemotePayload"
    payload.mkdir(parents=True)
    (payload / "catalog_0.1.hash").write_text("1" * 32, encoding="ascii")
    (payload / "nested").mkdir()
    (payload / "nested/content.bundle").write_bytes(b"verified resource")
    manifest = release / "release-manifest.json"
    manifest.write_text(json.dumps({
        "releaseDirectory": str(release),
        "files": [{"relativePath": path.relative_to(payload).as_posix(),
                   "bytes": path.stat().st_size, "sha256": digest(path)}
                  for path in sorted(payload.rglob("*")) if path.is_file()],
    }), encoding="utf-8")
    (release / "startup-download-verification.json").write_text(json.dumps({
        "status": "succeeded", "releaseId": release.name,
        "allPublishedBundlesIncluded": True, "allNewBackgroundsIncluded": True,
        "allRequiredStartupAssetsIncluded": True, "manifestSha256": digest(manifest),
    }), encoding="utf-8")
    records = json.loads(manifest.read_text(encoding="utf-8"))["files"]
    (release / "SHA256SUMS").write_text("".join(
        record["sha256"] + "  " + record["relativePath"] + "\n" for record in records),
        encoding="ascii", newline="\n")
    server = root / "server"
    stage = server / release.name
    if mode != "upload":
        shutil.copytree(release, stage)
    if damage == "payload":
        (stage / "RemotePayload/nested/content.bundle").write_bytes(b"corrupt! resource")
    elif damage == "control":
        # A stale/self-consistent server checksum list must not be trusted.
        changed = stage / "RemotePayload/nested/content.bundle"
        changed.write_bytes(b"corrupt! resource")
        sums = stage / "SHA256SUMS"
        sums.write_text(sums.read_text().replace(records[1]["sha256"], digest(changed)), encoding="ascii")
    elif damage == "extra":
        (stage / "RemotePayload/unexpected.bundle").write_bytes(b"extra")
    elif damage == "manifest":
        (stage / "release-manifest.json").write_text("{}", encoding="ascii")
    elif damage == "startup":
        (stage / "startup-download-verification.json").write_text("{}", encoding="ascii")
    elif damage == "local":
        (payload / "nested/content.bundle").write_bytes(b"corrupt! resource")

    calls = root / "calls.log"
    flag = {"resume": "-VerifyRemoteOnly", "upload": "", "local": "-VerifyOnly",
            "conflict": "-VerifyOnly -VerifyRemoteOnly"}[mode]
    wrapper = f"""
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
Import-Module (Join-Path $PSHOME 'Modules/Microsoft.PowerShell.Utility/Microsoft.PowerShell.Utility.psd1')
Import-Module (Join-Path $PSHOME 'Modules/Microsoft.PowerShell.Management/Microsoft.PowerShell.Management.psd1')
function global:ssh.exe {{
    Add-Content -LiteralPath {quote(calls)} -Value 'ssh'
    if ({quote(damage)} -eq 'disconnect') {{ $global:LASTEXITCODE = 255; return }}
    $command = ([string]$args[-1]).Replace('/srv/hundun-upload', {quote(posix(server))})
    $commandFile = Join-Path {quote(root)} ('remote-' + [Guid]::NewGuid().ToString('N') + '.sh')
    [IO.File]::WriteAllText($commandFile, $command, [Text.UTF8Encoding]::new($false))
    $bashPath = '/' + $commandFile.Substring(0,1).ToLowerInvariant() + $commandFile.Substring(2).Replace([char]92,[char]47)
    & {quote(BASH)} $bashPath
    $global:LASTEXITCODE = $LASTEXITCODE
}}
function global:scp.exe {{
    Add-Content -LiteralPath {quote(calls)} -Value 'scp'
    foreach ($source in @($args[-5],$args[-4],$args[-3],$args[-2])) {{
        Copy-Item -LiteralPath $source -Destination {quote(stage)} -Recurse -Force
    }}
    $global:LASTEXITCODE = 0
}}
try {{ & {quote(SCRIPT)} -ReleaseDirectory {quote(release)} {flag} }}
catch {{ Write-Output $_.Exception.Message; exit 1 }}
"""
    encoded = base64.b64encode(wrapper.encode("utf-16le")).decode("ascii")
    result = subprocess.run(["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass",
                             "-EncodedCommand", encoded], capture_output=True, timeout=30,
                            env=dict(os.environ, MSYS="winsymlinks:sys"))
    output = (result.stdout + result.stderr).decode("utf-8", errors="replace")
    (root / "output.txt").write_text(output, encoding="utf-8")
    actual_calls = calls.read_text().splitlines() if calls.exists() else []
    receipt = release / "staged-upload-success.json"
    should_fail = damage is not None or mode == "conflict"
    if should_fail:
        assert result.returncode != 0, (case, output)
        assert not receipt.exists(), case
        assert "STAGED_UPLOAD_OK" not in output, case
    else:
        assert result.returncode == 0, (case, output)
        if mode == "local":
            assert not receipt.exists() and actual_calls == [], (case, actual_calls)
        else:
            proof = json.loads(receipt.read_text(encoding="utf-8-sig"))
            assert proof["releaseId"] == release.name and proof["allRemoteFileHashesVerified"]
            assert not proof["publicCatalogChanged"] and proof["requiresMatchingClient"]
            assert "STAGED_UPLOAD_OK" in output, (case, output)
    expected_calls = ["ssh", "scp", "ssh"] if mode == "upload" else ["ssh"]
    if mode in ("local", "conflict") or damage == "local":
        expected_calls = []
    assert actual_calls == expected_calls, (case, actual_calls, output)
    return {"case": case, "passed": True, "exitCode": result.returncode,
            "transportCalls": actual_calls}


results = [run("normal-upload", mode="upload"), run("resume-existing"),
           run("payload-corruption", damage="payload"), run("stale-checksums", damage="control"),
           run("unexpected-file", damage="extra"), run("wrong-manifest", damage="manifest"),
           run("wrong-startup-proof", damage="startup"), run("ssh-disconnected", damage="disconnect"),
           run("local-corruption", damage="local"), run("local-only", mode="local"),
           run("conflicting-flags", mode="conflict")]
(ROOT / "results.json").write_text(json.dumps({"status": "succeeded", "cases": results}, indent=2), encoding="utf-8")
print("PASS: normal upload, resume without SCP, control-file binding and failure rejection.")
print(ROOT / "results.json")
