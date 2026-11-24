$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$summaryPath=Join-Path $root 'Build/WindowsRelease/0.1.2_20261124_132951/release-summary.json'
$summary=Get-Content -LiteralPath $summaryPath -Raw -Encoding UTF8|ConvertFrom-Json
$request=Get-Content -LiteralPath (Join-Path (Split-Path $summary.buildReport -Parent) 'build-request.json') -Raw -Encoding UTF8|ConvertFrom-Json
$build=Get-Content -LiteralPath $summary.buildReport -Raw -Encoding UTF8|ConvertFrom-Json
$fixture=Join-Path 'C:/QiDaoBuild' ('ResourceCompatibilityTests-'+[Guid]::NewGuid().ToString('N'))
$release=Join-Path $fixture ([Guid]::NewGuid().ToString('N'))
$player=Join-Path $release 'PlayerContent'
New-Item -ItemType Directory -Path $player -Force|Out-Null
$bundles=@($request.playerContentFiles|Where-Object relativePath -Like '*.bundle')
if(!$bundles.Count){throw 'Baseline local bundles are missing.'}
foreach($bundle in $bundles){
    $target=Join-Path $player $bundle.relativePath
    New-Item -ItemType Directory -Path (Split-Path $target -Parent) -Force|Out-Null
    Copy-Item -LiteralPath $bundle.path -Destination $target
}
$manifest=@{playerContentDirectory=$player;remoteLoadPath=$build.resourceServer}
[IO.File]::WriteAllText((Join-Path $release 'release-manifest.json'),($manifest|ConvertTo-Json),[Text.UTF8Encoding]::new($false))
$tool=Join-Path $root 'Tools/Verify-ResourceClientCompatibility.ps1'
& $tool -ReleaseDirectory $release -ClientSummary $summaryPath
$proof=Get-Content -LiteralPath (Join-Path $release 'client-compatibility.json') -Raw -Encoding UTF8|ConvertFrom-Json
if($proof.status -ne 'succeeded' -or !$proof.existingClientSupported -or !$proof.localBundlesIdentical){throw 'Unchanged client was rejected.'}
[IO.File]::WriteAllText((Join-Path $player $bundles[0].relativePath),'tampered local bootstrap')
$process=Start-Process -FilePath powershell.exe -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File',('"'+$tool+'"'),'-ReleaseDirectory',('"'+$release+'"'),'-ClientSummary',('"'+$summaryPath+'"')) -WindowStyle Hidden -Wait -PassThru -RedirectStandardOutput (Join-Path $fixture 'negative-out.txt') -RedirectStandardError (Join-Path $fixture 'negative-error.txt')
if($process.ExitCode -eq 0){throw 'Changed local bootstrap bundle was accepted.'}
Write-Output 'PASS: unchanged runtime and bootstrap are compatible; changed local bootstrap is rejected.'
