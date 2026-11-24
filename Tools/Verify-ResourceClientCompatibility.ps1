[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$ReleaseDirectory,
    [Parameter(Mandatory=$true)][string]$ClientSummary
)
$ErrorActionPreference='Stop'
$release=[IO.Path]::GetFullPath($ReleaseDirectory).TrimEnd('\','/')
$id=[IO.Path]::GetFileName($release)
if($id -notmatch '^[a-f0-9]{32}$'){throw 'Invalid resource release ID.'}
$manifestPath=Join-Path $release 'release-manifest.json'
$manifest=Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8|ConvertFrom-Json
$summaryPath=[IO.Path]::GetFullPath($ClientSummary)
$summary=Get-Content -LiteralPath $summaryPath -Raw -Encoding UTF8|ConvertFrom-Json
if($summary.status -ne 'completed' -or !$summary.nativeBuildSucceeded -or !$summary.binaryRequiredTypesVerified){throw 'Verified client build required.'}
$build=Get-Content -LiteralPath $summary.buildReport -Raw -Encoding UTF8|ConvertFrom-Json
$request=Get-Content -LiteralPath (Join-Path (Split-Path $summary.buildReport -Parent) 'build-request.json') -Raw -Encoding UTF8|ConvertFrom-Json
if($manifest.remoteLoadPath.TrimEnd('/') -ne $build.resourceServer.TrimEnd('/')){throw 'Resource endpoint differs from the existing client.'}
$sources=@($request.productionSourceFiles)
$seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach($source in $sources){
    if((Get-FileHash -LiteralPath $source.original -Algorithm SHA256).Hash.ToLowerInvariant() -ne $source.sha256){throw ('Client script changed: '+$source.relativePath)}
    $null=$seen.Add([IO.Path]::GetFullPath($source.original))
}
$project=Split-Path $PSScriptRoot -Parent
$current=@(Get-ChildItem -LiteralPath (Join-Path $project 'Assets/Scripts') -Recurse -File|Where-Object {$_.Name -like '*.cs' -or $_.Name -like '*.cs.meta'})
if($current.Count -ne $seen.Count -or @($current|Where-Object {!$seen.Contains($_.FullName)}).Count){throw 'The client script file set changed.'}
$baseline=@($request.playerContentFiles|Where-Object relativePath -Like '*.bundle')
$newPlayer=[IO.Path]::GetFullPath([string]$manifest.playerContentDirectory).TrimEnd('\','/')
$local=@(Get-ChildItem -LiteralPath $newPlayer -Recurse -Filter '*.bundle' -File)
if($local.Count -ne $baseline.Count){throw 'Local bootstrap bundle count changed; content-only release is not yet compatible.'}
$localProof=@()
foreach($file in $local){
    $relative=$file.FullName.Substring($newPlayer.Length+1).Replace('\','/')
    $old=@($baseline|Where-Object relativePath -EQ $relative)
    $hash=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    if($old.Count -ne 1 -or $hash -ne $old[0].sha256){throw ('Local bootstrap bundle changed: '+$relative)}
    $localProof+=@{relativePath=$relative;sha256=$hash;identicalToClient=$true}
}
if((Get-FileHash -LiteralPath $summary.installerPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $summary.installerSha256){throw 'Existing client installer changed.'}
$proof=[ordered]@{status='succeeded';resourceReleaseId=$id;baseClientReleaseId=$summary.releaseId;appVersion=$summary.appVersion;existingClientSupported=$true;runtimeScriptsUnchanged=$true;localBundlesIdentical=$true;runtimeSourceCount=$sources.Count;localBundles=$localProof;manifestSha256=(Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash.ToLowerInvariant();verifiedAtUtc=[DateTime]::UtcNow.ToString('o')}
$proofPath=Join-Path $release 'client-compatibility.json'
[IO.File]::WriteAllText($proofPath,($proof|ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
$client=[ordered]@{releaseId=$id;appVersion=$summary.appVersion;baseClientReleaseId=$summary.releaseId;summaryPath=$summaryPath;summarySha256=(Get-FileHash -LiteralPath $summaryPath -Algorithm SHA256).Hash.ToLowerInvariant();installerPath=$summary.installerPath;installerSha256=$summary.installerSha256;binaryVerificationPath=$summary.binaryReport;binaryVerificationSha256=(Get-FileHash -LiteralPath $summary.binaryReport -Algorithm SHA256).Hash.ToLowerInvariant();compatibilityPath=$proofPath;compatibilitySha256=(Get-FileHash -LiteralPath $proofPath -Algorithm SHA256).Hash.ToLowerInvariant()}
[IO.File]::WriteAllText((Join-Path $release 'matching-client.json'),($client|ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
Write-Host 'EXISTING_CLIENT_COMPATIBILITY_OK - runtime scripts and local bootstrap bundles are unchanged.'
