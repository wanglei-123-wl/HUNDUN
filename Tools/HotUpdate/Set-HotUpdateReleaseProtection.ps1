[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$ReleaseId,[switch]$Protect,[switch]$Unprotect,
      [string]$SshTarget='root@47.93.78.106',
      [string]$ServerRoot='/srv/hundun-hotupdate/windows-mono-v1',
      [string]$ServerTools='/srv/hundun-hotupdate-tools/windows-mono-v1',
      [string]$LocalStoreDirectory)
. (Join-Path $PSScriptRoot 'HotUpdate.Common.ps1')
if($ReleaseId -notmatch '^[a-f0-9]{32}$' -or ([bool]$Protect -eq [bool]$Unprotect)){throw 'Specify one valid release and either -Protect or -Unprotect.'}
Invoke-HotUpdateStore -Request @{action='pin';releaseId=$ReleaseId;pinned=[bool]$Protect} -SshTarget $SshTarget -ServerRoot $ServerRoot -ServerTools $ServerTools -LocalStoreDirectory $LocalStoreDirectory
