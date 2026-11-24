[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$ReleaseDirectory,
    [string]$SshTarget='root@47.93.78.106',
    [switch]$VerifyOnly
)
$ErrorActionPreference='Stop'
$release=[IO.Path]::GetFullPath($ReleaseDirectory).TrimEnd('\','/')
$id=[IO.Path]::GetFileName($release)
if($id -notmatch '^[a-f0-9]{32}$' -or $SshTarget -notmatch '^[A-Za-z0-9_.-]+@[A-Za-z0-9.-]+$'){throw 'Invalid release or SSH login.'}
& (Join-Path $PSScriptRoot 'Upload-StagedResources.ps1') -ReleaseDirectory $release -VerifyOnly
$receipt=Get-Content -LiteralPath (Join-Path $release 'staged-upload-success.json') -Raw -Encoding UTF8|ConvertFrom-Json
if($receipt.releaseId -ne $id -or !$receipt.allRemoteFileHashesVerified -or $receipt.server -ne $SshTarget){throw 'Matching server staging receipt required.'}
$client=Get-Content -LiteralPath (Join-Path $release 'matching-client.json') -Raw -Encoding UTF8|ConvertFrom-Json
if($client.releaseId -ne $id){throw 'Client and resource releases do not match.'}
foreach($proof in @(@{path=$client.summaryPath;hash=$client.summarySha256},@{path=$client.installerPath;hash=$client.installerSha256},@{path=$client.binaryVerificationPath;hash=$client.binaryVerificationSha256})){
    if((Get-FileHash -LiteralPath $proof.path -Algorithm SHA256).Hash.ToLowerInvariant() -ne $proof.hash){throw ('Matching client proof changed: '+$proof.path)}
}
$summary=Get-Content -LiteralPath $client.summaryPath -Raw -Encoding UTF8|ConvertFrom-Json
if($summary.status -ne 'completed' -or $summary.releaseId -ne $id -or !$summary.binaryRequiredTypesVerified -or !$summary.pairedBootstrapVerified){throw 'Verified matching client build required.'}
$manifestPath=Join-Path $release 'release-manifest.json'
$manifest=Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8|ConvertFrom-Json
$source=Get-Content -LiteralPath (Join-Path $release 'Upload-Source.json') -Raw -Encoding UTF8|ConvertFrom-Json
$newHash=(Get-Content -LiteralPath (Join-Path $source.payloadDirectory 'catalog_0.1.hash') -Raw -Encoding UTF8).Trim()
if($newHash -notmatch '^[a-f0-9]{32}$'){throw 'Invalid new catalog hash.'}
$publisher=Join-Path $PSScriptRoot 'Publish-StagedResources.sh'
if(!(Test-Path -LiteralPath $publisher)){throw 'Server publisher is missing.'}
Write-Host 'MATCHED_CLIENT_AND_RESOURCES_OK'
if($VerifyOnly){return}

$serverScript=Join-Path $release 'Publish-StagedResources.sh'
[IO.File]::WriteAllText($serverScript,[IO.File]::ReadAllText($publisher).Replace("`r`n","`n"),[Text.UTF8Encoding]::new($false))

$base=([string]$manifest.remoteLoadPath).TrimEnd('/')
$response=Invoke-WebRequest -UseBasicParsing -Uri ($base+'/catalog_0.1.hash?before_publish='+[Guid]::NewGuid().ToString('N')) -TimeoutSec 30
$previousHash=([Text.Encoding]::UTF8.GetString($response.RawContentStream.ToArray())).Trim()
if($previousHash -notmatch '^[a-f0-9]{32}$'){throw 'Invalid current public catalog hash.'}
foreach($program in @('ssh.exe','scp.exe')){$null=Get-Command $program -ErrorAction Stop}
function Invoke-Native([string]$Program,[string[]]$Arguments){
    & $Program @Arguments
    if($LASTEXITCODE -ne 0){throw "$Program failed (exit $LASTEXITCODE). Publishing stopped."}
}
$staging='/srv/hundun-upload/'+$id
$options=@('-o','ConnectTimeout=20','-o','ServerAliveInterval=15')
Invoke-Native 'scp.exe' ($options+@($serverScript,"${SshTarget}:${staging}/Publish-StagedResources.sh"))
$manifestHash=(Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant()
$checksumsHash=(Get-FileHash -LiteralPath (Join-Path $release 'SHA256SUMS') -Algorithm SHA256).Hash.ToLowerInvariant()
Invoke-Native 'ssh.exe' ($options+@($SshTarget,"bash '$staging/Publish-StagedResources.sh' '$id' '$manifestHash' '$previousHash' '$newHash' '$checksumsHash'"))
Write-Host 'Checking the public resource URLs and catalog hashes.'
foreach($file in $manifest.files){
    $encoded=(($file.relativePath.Split('/')|ForEach-Object{[Uri]::EscapeDataString($_)}) -join '/')
    $request=[Net.HttpWebRequest]::Create($base+'/'+$encoded+'?verify='+[Guid]::NewGuid().ToString('N'))
    $request.Method='HEAD';$request.Timeout=30000;$request.Proxy=$null
    $result=$request.GetResponse()
    try{if([int]$result.StatusCode -ne 200 -or $result.ContentLength -ne [long]$file.bytes){throw ('Public file size/status mismatch: '+$encoded)}}finally{$result.Close()}
    if($file.relativePath -match '^catalog_[A-Za-z0-9_.-]+\.(json|hash)$'){
        $request=[Net.HttpWebRequest]::Create($base+'/'+$encoded+'?verify='+[Guid]::NewGuid().ToString('N'))
        $request.Timeout=30000;$request.Proxy=$null
        $result=$request.GetResponse();$algorithm=[Security.Cryptography.SHA256]::Create()
        try{$actual=[BitConverter]::ToString($algorithm.ComputeHash($result.GetResponseStream())).Replace('-','').ToLowerInvariant();if($actual -ne $file.sha256){throw ('Public catalog SHA256 mismatch: '+$encoded)}}finally{$algorithm.Dispose();$result.Close()}
    }
}
$published=[ordered]@{releaseId=$id;verifiedAtUtc=[DateTime]::UtcNow.ToString('o');publicBaseUrl=$base;publicHeadersAndCatalogHashesVerified=$true;allRemoteFileHashesVerifiedByServer=$true;matchingClientVersion=$client.appVersion;catalogHash=$newHash;previousCatalogHash=$previousHash}
[IO.File]::WriteAllText((Join-Path $release 'upload-success.json'),($published|ConvertTo-Json),[Text.UTF8Encoding]::new($false))
Write-Host 'PUBLIC_MATCHING_LOADING_RESOURCES_OK' -ForegroundColor Green
Write-Host 'Close and reopen the matching client to download the newly published resources.'
