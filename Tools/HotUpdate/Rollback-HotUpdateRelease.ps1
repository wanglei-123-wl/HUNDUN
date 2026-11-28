[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$TargetReleaseId,
      [Parameter(Mandatory=$true)][string]$OutputDirectory,
      [string]$SigningKeyPath='C:\QiDaoBuild\Keys\QiDaoHotUpdate-RSA.xml',
      [string]$SshTarget='root@47.93.78.106',
      [string]$ServerRoot='/srv/hundun-hotupdate/windows-mono-v1',
      [string]$ServerTools='/srv/hundun-hotupdate-tools/windows-mono-v1',
      [string]$PublicBaseUrl='http://127.0.0.1:18761/client-hotupdate/windows-mono-v1',
      [string]$LocalStoreDirectory,[switch]$Publish)
. (Join-Path $PSScriptRoot 'HotUpdate.Common.ps1')
if ($TargetReleaseId -notmatch '^[a-f0-9]{32}$') { throw 'Invalid rollback target.' }
$connection = @{SshTarget=$SshTarget;ServerRoot=$ServerRoot;ServerTools=$ServerTools;LocalStoreDirectory=$LocalStoreDirectory}
$server = Invoke-HotUpdateStore -Request @{action='state'} @connection
if($server.pendingRecovery){throw 'An interrupted server transaction needs recovery before a rollback can be signed.'}
$property = $server.state.publications.PSObject.Properties[$TargetReleaseId]
if (!$property -or $property.Value.status -ne 'published') { throw 'Rollback target is not a retained successful publication.' }
$descriptor = $property.Value.descriptor
$old = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($descriptor.envelope.payload)) | ConvertFrom-Json
$newId = [Guid]::NewGuid().ToString('N'); $oldCodePath = $old.codePath
$old.releaseId = $newId; $old.sequence = [long]$server.state.highestSequence + 1
$old.codePath = 'releases/'+$newId+'/QiDao.GameLogic.dll.bytes'
$envelope = Sign-HotUpdateEnvelope $old $SigningKeyPath
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $output) { throw 'Rollback output must be a new directory.' }
[IO.Directory]::CreateDirectory($output) | Out-Null
$currentPath = Join-Path $output 'current.json'; Write-Utf8Json $currentPath $envelope
$raw = [IO.File]::ReadAllBytes($currentPath)
$descriptor.envelope = $envelope; $descriptor.envelopeBytes = [Convert]::ToBase64String($raw)
$files = [ordered]@{}
foreach($item in $descriptor.packageManifest.files.PSObject.Properties) {
    if ($item.Name -eq $oldCodePath) { $files[$old.codePath] = $item.Value }
    elseif ($item.Name -eq 'current.json') { $files['current.json'] = @{sha256=(Get-FileHash -LiteralPath $currentPath).Hash.ToLowerInvariant();bytes=$raw.Length} }
    else { $files[$item.Name] = $item.Value }
}
$descriptor.packageManifest.files = $files; $descriptor.packageManifest.releaseId = $newId
$descriptor.packageManifest.sequence = $old.sequence
$descriptor.packageManifest.createdAtUtc = [DateTimeOffset]::UtcNow.ToString('o')
$descriptorPath = Join-Path $output 'rollback-descriptor.json'; Write-Utf8Json $descriptorPath $descriptor
$offer = Invoke-HotUpdateStore -Request @{action='offer'} @connection -InputJsonPath $descriptorPath
# Retained code/resources must already be complete on the server. Only the new
# signed current.json is supplied by a rollback operation.
if (@($offer.missingFiles | Where-Object { $_ -ne 'current.json' }).Count) {
    Invoke-HotUpdateStore -Request @{action='abort';releaseId=$newId} @connection | Out-Null
    throw 'Rollback target data is missing/corrupt; repair retained resources before rollback.'
}
$offerPath = Join-Path $output 'offer-response.json'; Write-Utf8Json $offerPath $offer
$archive = Join-Path $output 'rollback-transfer.tar'
$meta = Invoke-PythonJson -Arguments @((Join-Path $PSScriptRoot 'release_transport.py'),'archive','--config',$script:TrustedConfig,
    '--package',$output,'--descriptor',$descriptorPath,'--offer',$offerPath,'--output',$archive)
$result = Invoke-HotUpdateStore -Request @{action='receive';releaseId=$newId;bytes=$meta.bytes;sha256=$meta.sha256} @connection -InputArchivePath $archive
Write-Utf8Json (Join-Path $output 'staged-hotupdate-receipt.json') $result
Remove-Item -LiteralPath $archive -Force
Write-Host ('Rollback publication staged: '+$newId+' sequence='+$old.sequence+' target='+$TargetReleaseId)
if ($Publish) {
    Invoke-HotUpdateStore -Request @{action='publish';releaseId=$newId;publicBaseUrl=$PublicBaseUrl} @connection
} else { $result; Write-Host 'Rollback is staged; live version was not switched.' }
