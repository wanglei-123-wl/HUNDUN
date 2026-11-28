[CmdletBinding()]
param([string]$SshTarget='root@47.93.78.106',
      [string]$ServerRoot='/srv/hundun-hotupdate/windows-mono-v1',
      [string]$ServerTools='/srv/hundun-hotupdate-tools/windows-mono-v1',
      [string]$PublicBaseUrl='http://127.0.0.1:18761/client-hotupdate/windows-mono-v1',
      [string]$LocalStoreDirectory,[Parameter(Mandatory=$true)][string]$PlanPath,[switch]$Apply)
. (Join-Path $PSScriptRoot 'HotUpdate.Common.ps1')
$connection = @{SshTarget=$SshTarget;ServerRoot=$ServerRoot;ServerTools=$ServerTools;LocalStoreDirectory=$LocalStoreDirectory}
if (!$Apply) {
    $plan = Invoke-HotUpdateStore -Request @{action='plan'} @connection
    Write-Utf8Json $PlanPath $plan; $plan
} else {
    Invoke-HotUpdateStore -Request @{action='cleanup';publicBaseUrl=$PublicBaseUrl} @connection -InputJsonPath $PlanPath
}
