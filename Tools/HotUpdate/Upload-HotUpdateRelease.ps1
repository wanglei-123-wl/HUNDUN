[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$ReleaseDirectory,
      [string]$SshTarget='root@47.93.78.106',
      [string]$ServerRoot='/srv/hundun-hotupdate/windows-mono-v1',
      [string]$ServerTools='/srv/hundun-hotupdate-tools/windows-mono-v1',
      [string]$LocalStoreDirectory,[switch]$Acceptance,[switch]$VerifyOnly,[string]$TrustedConfigPath)
. (Join-Path $PSScriptRoot 'HotUpdate.Common.ps1')
if ($TrustedConfigPath) { $script:TrustedConfig = [IO.Path]::GetFullPath($TrustedConfigPath) }
if ($Acceptance -and !$LocalStoreDirectory) { throw 'Acceptance transfers may only target a local isolated store.' }
$release = [IO.Path]::GetFullPath($ReleaseDirectory)
$package = Join-Path $release 'Package'
if (!(Test-Path -LiteralPath $package)) { $package = $release }
$recordRoot = $release
if ($package -eq $release) { $recordRoot = Split-Path $release -Parent }
$arguments = @((Join-Path $PSScriptRoot 'release_package.py'),'verify','--config',$script:TrustedConfig,'--package',$package)
if ($Acceptance) { $arguments += '--acceptance' }
$verified = Invoke-PythonJson $arguments
if ($VerifyOnly) { $verified; return }
$operation = Join-Path $recordRoot ('UploadOperations/'+$verified.releaseId+'/'+[guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($operation) | Out-Null
$descriptor = Join-Path $operation 'offer-descriptor.json'; $offerPath = Join-Path $operation 'offer-response.json'
$arguments = @((Join-Path $PSScriptRoot 'release_transport.py'),'describe','--config',$script:TrustedConfig,'--package',$package,'--output',$descriptor)
if ($Acceptance) { $arguments += '--acceptance' }
& python @arguments; if ($LASTEXITCODE -ne 0) { throw 'Package descriptor verification failed.' }
$connection = @{SshTarget=$SshTarget;ServerRoot=$ServerRoot;ServerTools=$ServerTools;LocalStoreDirectory=$LocalStoreDirectory}
$offer = Invoke-HotUpdateStore -Request @{action='offer'} @connection -InputJsonPath $descriptor
Write-Utf8Json $offerPath $offer
if ($offer.status -eq 'already-current') { $offer; return }
Write-Host ('Missing upload bytes: '+$offer.missingBytes)
$archive = Join-Path $operation 'transfer.tar'
try {
    $meta = Invoke-PythonJson @((Join-Path $PSScriptRoot 'release_transport.py'),'archive','--config',$script:TrustedConfig,
        '--package',$package,'--descriptor',$descriptor,'--offer',$offerPath,'--output',$archive)
    $receipt = Invoke-HotUpdateStore -Request @{action='receive';releaseId=$verified.releaseId;bytes=$meta.bytes;sha256=$meta.sha256} @connection -InputArchivePath $archive
} finally {
    # The immutable Package is the retry source; failed transfer tar files are
    # regenerable and must not accumulate another full resource copy locally.
    if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force }
}
Write-Utf8Json (Join-Path $recordRoot 'staged-hotupdate-receipt.json') $receipt
$receipt
Write-Host 'STAGED_HOTUPDATE_OK - live version was not switched.'
