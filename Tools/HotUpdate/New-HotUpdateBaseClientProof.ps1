[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$ClientRoot,
      [Parameter(Mandatory=$true)][string]$NativeAcceptanceSummary,
      [Parameter(Mandatory=$true)][string]$OutputPath,
      [string]$SigningKeyPath='C:\QiDaoBuild\Keys\QiDaoHotUpdate-RSA.xml',
      [switch]$Acceptance)
. (Join-Path $PSScriptRoot 'HotUpdate.Common.ps1')
$client = [IO.Path]::GetFullPath($ClientRoot).TrimEnd('\','/')
$output = [IO.Path]::GetFullPath($OutputPath)
if ($output.StartsWith($client+'\',[StringComparison]::OrdinalIgnoreCase)) { throw 'Put the signed proof outside the immutable client directory.' }
$build = Read-Utf8Json (Join-Path $client 'base-client-build.json')
$native = Read-Utf8Json $NativeAcceptanceSummary
if ($build.kind -ne 'QiDaoBaseClientBuild' -or $build.status -ne 'built' -or !$build.businessDllExcluded -or $build.baseBuildGuid -notmatch '^[a-f0-9]{32}$' -or
    $native.status -ne 'succeeded' -or !$native.sameBaseFilesUnchanged -or !$native.basePlayerFileHashes) { throw 'Verified base build and matching native acceptance are required.' }
if ($build.acceptanceOnly -and !$Acceptance) { throw 'Acceptance client cannot authorize a formal publication.' }
$allCases = @($native.cases)
if (!$allCases.Count -or @($allCases | Where-Object {!$_.passed -or !$_.result.succeeded -or !$_.result.preloadedVerifiedCode -or !$_.result.gameContentPrepared -or $_.result.mapDataCount -le 0 -or $_.result.loginScene -ne 'SampleScene'}).Count) {
    throw 'Full game code preload, content, map package and login scene acceptance must pass.'
}
$actual = @(Get-ChildItem -LiteralPath $client -Recurse -File -Force)
$expected = @($native.basePlayerFileHashes.PSObject.Properties)
if ($actual.Count -ne $expected.Count) { throw 'Client file inventory differs from the native acceptance snapshot.' }
$lines = foreach($entry in ($expected | Sort-Object Name)) {
    $relative=[string]$entry.Name
    if($relative.StartsWith('/') -or $relative.Contains('\') -or $relative.Split('/') -contains '..' -or $relative.Contains(':')){throw 'Unsafe client inventory path.'}
    $file=[IO.Path]::GetFullPath((Join-Path $client $relative))
    if(!$file.StartsWith($client+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Client inventory escaped its root.'}
    $item=Get-Item -LiteralPath $file
    if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){throw 'Client files must not be symbolic links.'}
    $sha=(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
    if($sha -ne $entry.Value){throw ('Client file changed after native acceptance: '+$relative)}
    $relative+' '+$sha
}
$algorithm=[Security.Cryptography.SHA256]::Create()
try{$baseHash=[BitConverter]::ToString($algorithm.ComputeHash([Text.Encoding]::UTF8.GetBytes(($lines -join "`n")))).Replace('-','').ToLowerInvariant()}finally{$algorithm.Dispose()}
$payload=@{kind='QiDaoBaseClientProof';schemaVersion=1;status='verified';target=$build.target;unityVersion=$build.unityVersion;
    bootstrapApiVersion=$build.bootstrapApiVersion;publisherKeySha256=$build.publisherKeySha256;baseBuildGuid=$build.baseBuildGuid;
    baseFilesSha256=$baseHash;businessDllExcluded=$true;preloadExecutionVerified=$true;resourcePairVerified=$true;
    acceptanceOnly=[bool]($build.acceptanceOnly -or $Acceptance);verifiedAtUtc=[DateTimeOffset]::UtcNow.ToString('o');
    nativeEvidenceSha256=(Get-FileHash -LiteralPath $NativeAcceptanceSummary).Hash.ToLowerInvariant()}
Write-Utf8Json $output (Sign-HotUpdateEnvelope $payload $SigningKeyPath)
Write-Host ('BASE_CLIENT_PROOF_SIGNED '+$output)
