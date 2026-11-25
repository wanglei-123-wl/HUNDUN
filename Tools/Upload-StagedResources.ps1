[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$ReleaseDirectory,
    [string]$SshTarget = 'root@47.93.78.106',
    [switch]$VerifyOnly,
    [switch]$VerifyRemoteOnly
)
$ErrorActionPreference = 'Stop'
if ($VerifyOnly -and $VerifyRemoteOnly) { throw 'Choose local verification or remote verification, not both.' }
$release = [IO.Path]::GetFullPath($ReleaseDirectory).TrimEnd('\','/')
$releaseId = [IO.Path]::GetFileName($release)
if ($releaseId -notmatch '^[a-f0-9]{32}$') { throw 'Invalid release directory ID.' }
if ($SshTarget -notmatch '^[A-Za-z0-9_.-]+@[A-Za-z0-9.-]+$') { throw 'Invalid SSH login.' }
$manifestPath = Join-Path $release 'release-manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ([IO.Path]::GetFullPath([string]$manifest.releaseDirectory).TrimEnd('\','/') -ne $release) {
    throw 'Manifest belongs to another release.'
}
$payload = Join-Path $release 'RemotePayload'
$sourcePath = Join-Path $release 'Upload-Source.json'
if (Test-Path -LiteralPath $sourcePath) {
    $source = Get-Content -LiteralPath $sourcePath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($source.releaseId -ne $releaseId) { throw 'Upload source belongs to another release.' }
    $payload = [IO.Path]::GetFullPath([string]$source.payloadDirectory).TrimEnd('\','/')
}
$startupPath = Join-Path $release 'startup-download-verification.json'
$startup = Get-Content -LiteralPath $startupPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($startup.status -ne 'succeeded' -or !$startup.allPublishedBundlesIncluded -or
    !$startup.allNewBackgroundsIncluded -or $startup.releaseId -ne $releaseId) {
    throw 'Startup download verification is missing or failed.'
}
if ($startup.PSObject.Properties.Name -contains 'allRequiredStartupAssetsIncluded' -and
    !$startup.allRequiredStartupAssetsIncluded) { throw 'Required new resources are missing from startup downloads.' }
if ((Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $startup.manifestSha256) {
    throw 'Release manifest changed after startup verification.'
}
if (@($manifest.files).Count -eq 0) { throw 'Release contains no files.' }
$expected = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$checks = [Collections.Generic.List[string]]::new()
foreach ($record in $manifest.files) {
    $relative = [string]$record.relativePath
    if ($relative -notmatch '^[A-Za-z0-9_./-]+$' -or $relative.StartsWith('/') -or
        ($relative.Split('/') | Where-Object { $_ -eq '.' -or $_ -eq '..' })) { throw 'Invalid manifest path.' }
    $file = [IO.Path]::GetFullPath((Join-Path $payload $relative))
    if (!$file.StartsWith($payload + '\', [StringComparison]::OrdinalIgnoreCase) -or
        !(Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing payload: $relative" }
    $item = Get-Item -LiteralPath $file
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -or $item.Length -ne [long]$record.bytes) {
        throw "Payload size or file type changed: $relative"
    }
    $hash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne [string]$record.sha256) { throw "SHA256 mismatch: $relative" }
    if (!$expected.Add($file)) { throw "Duplicate manifest path: $relative" }
    $checks.Add("$hash  $relative")
}
$files = @(Get-ChildItem -LiteralPath $payload -File -Recurse -Force)
if ($files.Count -ne $expected.Count -or @($files | Where-Object { !$expected.Contains($_.FullName) }).Count) {
    throw 'Payload contains unexpected files.'
}
$checksums = Join-Path $release 'SHA256SUMS'
[IO.File]::WriteAllText($checksums, ([string]::Join("`n",$checks) + "`n"), [Text.UTF8Encoding]::new($false))
Write-Host 'LOCAL_HASH_OK - payload and startup download verification passed.'
Write-Host "Upload source: $payload"
if ($VerifyOnly) { return }

$programs = @('ssh.exe')
if (!$VerifyRemoteOnly) { $programs += 'scp.exe' }
foreach ($program in $programs) { $null = Get-Command $program -ErrorAction Stop }
function Invoke-Native([string]$Program, [string[]]$Arguments) {
    & $Program @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Program failed; upload stopped (exit $LASTEXITCODE)." }
}
$staging = "/srv/hundun-upload/$releaseId"
$sshOptions = @('-o','ConnectTimeout=20','-o','ServerAliveInterval=15')
if (!$VerifyRemoteOnly) {
    Invoke-Native 'ssh.exe' ($sshOptions + @($SshTarget, "mkdir -p -- '$staging'"))
    Invoke-Native 'scp.exe' ($sshOptions + @('-r',$payload,$checksums,$manifestPath,$startupPath,"${SshTarget}:${staging}/"))
} else {
    Write-Host 'REMOTE_VERIFY_ONLY - checking existing staged files; no files will be uploaded.'
}
# Pin the uploaded control files to this locally verified release before trusting
# their payload hashes. This also makes verification safe to resume after SCP.
$metadataChecks = foreach ($metadataPath in @($checksums,$manifestPath,$startupPath)) {
    $metadataHash = (Get-FileHash -LiteralPath $metadataPath -Algorithm SHA256).Hash.ToLowerInvariant()
    "'$metadataHash  $([IO.Path]::GetFileName($metadataPath))'"
}
$metadataCommand = "cd '$staging'; printf '%s\n' " + ($metadataChecks -join ' ') + ' | sha256sum --strict -c -; '
$verifyCommand = "set -eu; $metadataCommand" + "test ! -L '$staging/RemotePayload'; test `"`$(realpath -m -- '$staging/RemotePayload')`" = '$staging/RemotePayload'; test -z `"`$(find '$staging/RemotePayload' -type l -print -quit)`"; test `"`$(find '$staging/RemotePayload' -type f | wc -l)`" -eq $($expected.Count); cd '$staging/RemotePayload'; sha256sum --strict -c '../SHA256SUMS'"
Invoke-Native 'ssh.exe' ($sshOptions + @($SshTarget,$verifyCommand))
$receipt = [ordered]@{releaseId=$releaseId; status='staged'; stagedAtUtc=[DateTime]::UtcNow.ToString('o');
    server=$SshTarget; serverDirectory=$staging; allRemoteFileHashesVerified=$true;
    publicCatalogChanged=$false; requiresMatchingClient=$true}
[IO.File]::WriteAllText((Join-Path $release 'staged-upload-success.json'), ($receipt|ConvertTo-Json),[Text.UTF8Encoding]::new($false))
Write-Host "STAGED_UPLOAD_OK - verified at ${SshTarget}:$staging" -ForegroundColor Green
Write-Host 'Live resources were not switched. Publish after matching client compatibility has been verified.'
