[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$ReleaseId,
      [string]$SshTarget='root@47.93.78.106',
      [string]$ServerRoot='/srv/hundun-hotupdate/windows-mono-v1',
      [string]$ServerTools='/srv/hundun-hotupdate-tools/windows-mono-v1',
      [string]$PublicBaseUrl='http://127.0.0.1:18761/client-hotupdate/windows-mono-v1',
      [string]$LocalStoreDirectory)
. (Join-Path $PSScriptRoot 'HotUpdate.Common.ps1')
if ($ReleaseId -notmatch '^[a-f0-9]{32}$') { throw 'Invalid release ID.' }
$result = Invoke-HotUpdateStore -Request @{action='publish';releaseId=$ReleaseId;publicBaseUrl=$PublicBaseUrl} `
    -SshTarget $SshTarget -ServerRoot $ServerRoot -ServerTools $ServerTools -LocalStoreDirectory $LocalStoreDirectory
$result
if ($result.cleanup.status -eq 'failed-after-successful-publish') { Write-Warning 'Publication succeeded but cleanup failed. Review the reported error; old files were not assumed removed.' }
