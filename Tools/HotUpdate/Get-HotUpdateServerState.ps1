[CmdletBinding()]
param([string]$SshTarget='root@47.93.78.106',
      [string]$ServerRoot='/srv/hundun-hotupdate/windows-mono-v1',
      [string]$ServerTools='/srv/hundun-hotupdate-tools/windows-mono-v1',
      [string]$LocalStoreDirectory,[string]$OutputPath)
. (Join-Path $PSScriptRoot 'HotUpdate.Common.ps1')
$result = Invoke-HotUpdateStore -Request @{action='state'} -SshTarget $SshTarget -ServerRoot $ServerRoot -ServerTools $ServerTools -LocalStoreDirectory $LocalStoreDirectory
if ($OutputPath) { Write-Utf8Json $OutputPath $result }
$result
