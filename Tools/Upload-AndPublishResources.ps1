[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$ReleaseDirectory,
    [string]$SshTarget='root@47.93.78.106',
    [switch]$VerifyOnly
)
$ErrorActionPreference='Stop'
$release=[IO.Path]::GetFullPath($ReleaseDirectory).TrimEnd('\','/')
$manifestPath=Join-Path $release 'release-manifest.json'
$manifest=Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8|ConvertFrom-Json
if([IO.Path]::GetFullPath([string]$manifest.releaseDirectory).TrimEnd('\','/') -ne $release){throw 'Manifest belongs to another release.'}
$client=Get-Content -LiteralPath (Join-Path $ReleaseDirectory 'matching-client.json') -Raw -Encoding UTF8|ConvertFrom-Json
if(!$client.compatibilityPath){throw 'Content-only compatibility proof missing.'}
if((Get-FileHash -LiteralPath $client.compatibilityPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $client.compatibilitySha256){throw 'Compatibility proof changed.'}
$compatibility=Get-Content -LiteralPath $client.compatibilityPath -Raw -Encoding UTF8|ConvertFrom-Json
if($compatibility.status -ne 'succeeded' -or !$compatibility.existingClientSupported -or $compatibility.resourceReleaseId -ne [IO.Path]::GetFileName($release) -or
    $compatibility.manifestSha256 -ne (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()){throw 'Client compatibility proof does not match this release.'}
if($VerifyOnly){
    & (Join-Path $PSScriptRoot 'Upload-StagedResources.ps1') -ReleaseDirectory $release -VerifyOnly
    Write-Host 'RESOURCE_UPLOAD_AND_PUBLICATION_READY'
    return
}
$source=Get-Content -LiteralPath (Join-Path $ReleaseDirectory 'Upload-Source.json') -Raw -Encoding UTF8|ConvertFrom-Json
$catalogHashPath=Join-Path $source.payloadDirectory 'catalog_0.1.hash'
$hashRecord=@($manifest.files|Where-Object relativePath -eq 'catalog_0.1.hash')
if($source.releaseId -ne [IO.Path]::GetFileName($release) -or $hashRecord.Count -ne 1 -or
    (Get-FileHash -LiteralPath $catalogHashPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $hashRecord[0].sha256){throw 'Catalog hash file does not match this release.'}
$wanted=(Get-Content -LiteralPath $catalogHashPath -Raw -Encoding UTF8).Trim()
if($wanted -notmatch '^[a-f0-9]{32}$'){throw 'Invalid desired catalog hash.'}
$response=Invoke-WebRequest -UseBasicParsing -Uri ($manifest.remoteLoadPath.TrimEnd('/')+'/catalog_0.1.hash?content_update='+[Guid]::NewGuid().ToString('N')) -TimeoutSec 30
$current=([Text.Encoding]::UTF8.GetString($response.RawContentStream.ToArray())).Trim()
if($current -eq $wanted){
    Write-Host 'PUBLIC_CATALOG_ALREADY_CURRENT - no resource version change; upload was skipped.' -ForegroundColor Green
    return
}
& (Join-Path $PSScriptRoot 'Upload-StagedResources.ps1') -ReleaseDirectory $ReleaseDirectory -SshTarget $SshTarget
& (Join-Path $PSScriptRoot 'Publish-StagedResources.ps1') -ReleaseDirectory $ReleaseDirectory -SshTarget $SshTarget
