[CmdletBinding()]
param([string]$SshTarget='root@47.93.78.106',
      [string]$ServerRoot='/srv/hundun-hotupdate/windows-mono-v1',
      [string]$ServerTools='/srv/hundun-hotupdate-tools/windows-mono-v1',
      [string]$PublicRoot='/srv/hundun-resources/client-hotupdate/windows-mono-v1',
      [string]$LocalStoreDirectory,[string]$LocalPublicDirectory,[switch]$Acceptance,[string]$TrustedConfigPath,[switch]$UpdateToolsOnly)
. (Join-Path $PSScriptRoot 'HotUpdate.Common.ps1')
if ($TrustedConfigPath) { $script:TrustedConfig = [IO.Path]::GetFullPath($TrustedConfigPath) }
if ($Acceptance -and !$LocalStoreDirectory) { throw 'Acceptance initialization is restricted to a local isolated store.' }
if (!$LocalStoreDirectory) {
    if ($SshTarget -notmatch '^[A-Za-z0-9_.-]+@[A-Za-z0-9.-]+$') { throw 'Invalid SSH login.' }
    Assert-ServerPath $ServerRoot; Assert-ServerPath $ServerTools; Assert-ServerPath $PublicRoot
    $versionedTools = $ServerTools+'/'+(Get-HotUpdateToolchainHash)
    & ssh.exe $SshTarget "set -eu; python3 -c 'import sys; assert sys.version_info >= (3,11), `"Python 3.11+ required`"'; mkdir -p -- '$versionedTools'"
    if ($LASTEXITCODE -ne 0) { throw 'Server tool preparation failed.' }
    $files = @('release_common.py','release_package.py','release_retention.py','release_store.py') | ForEach-Object { Join-Path $PSScriptRoot $_ }
    & scp.exe @files "${SshTarget}:${versionedTools}/"
    if ($LASTEXITCODE -ne 0) { throw 'Server tool copy failed.' }
    $checks = $files | ForEach-Object { "'$((Get-FileHash -Algorithm SHA256 -LiteralPath $_).Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($_))'" }
    $verify = "set -eu; cd '$versionedTools'; printf '%s\n' " + ($checks -join ' ') + ' | sha256sum --strict -c -'
    & ssh.exe $SshTarget $verify
    if ($LASTEXITCODE -ne 0) { throw 'Server tools did not pass SHA256 verification.' }
}
if($UpdateToolsOnly){
    Invoke-HotUpdateStore -Request @{action='state'} -SshTarget $SshTarget -ServerRoot $ServerRoot -ServerTools $ServerTools -LocalStoreDirectory $LocalStoreDirectory
    return
}
$public = $PublicRoot
if ($LocalStoreDirectory) {
    if (!$LocalPublicDirectory) { throw 'Local public directory is required.' }
    $public = [IO.Path]::GetFullPath($LocalPublicDirectory)
}
$request = @{action='init'; publicRoot=$public; config=(Read-Utf8Json $script:TrustedConfig);
             policy=(Read-Utf8Json (Join-Path $PSScriptRoot 'RetentionPolicy.json')); acceptanceOnly=[bool]$Acceptance}
Invoke-HotUpdateStore -Request $request -SshTarget $SshTarget -ServerRoot $ServerRoot -ServerTools $ServerTools -LocalStoreDirectory $LocalStoreDirectory
