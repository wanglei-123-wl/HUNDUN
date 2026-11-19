[CmdletBinding()]
param([string] $ServerProjectRoot = (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'gameserver/GameServer'))
$ErrorActionPreference = 'Stop'
$clientRoot = Split-Path $PSScriptRoot -Parent
$assetRoot = Join-Path $clientRoot 'Assets/MapTraversalData'
New-Item -ItemType Directory -Path $assetRoot -Force | Out-Null
$geometry = Get-Content -LiteralPath (Join-Path $clientRoot 'Docs/MapTraversalGeometrySources.json') -Raw | ConvertFrom-Json
foreach ($item in $geometry.files) {
    $hash = (Get-FileHash -LiteralPath (Join-Path $ServerProjectRoot $item.sourceFile) -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($hash -ne $item.sourceSha256) { throw 'Backend query code changed. Review and reimport geometry before importing map data.' }
    $adapted = (Get-FileHash -LiteralPath (Join-Path $clientRoot $item.adaptedFile) -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($adapted -ne $item.adaptedSha256) { throw "Adapted query code changed without refreshing provenance: $($item.adaptedFile)" }
}
function Ensure-Meta([string] $path) {
    $meta = $path + '.meta'
    if (-not (Test-Path -LiteralPath $meta)) {
        $guid = [Guid]::NewGuid().ToString('N')
        $content = "fileFormatVersion: 2`nguid: $guid`n"
        if (Test-Path -LiteralPath $path -PathType Container) {
            $content += "folderAsset: yes`nDefaultImporter:`n  externalObjects: {}`n  userData: `n  assetBundleName: `n  assetBundleVariant: `n"
        } elseif ([IO.Path]::GetExtension($path) -eq '.cs') {
            $content += "MonoImporter:`n  externalObjects: {}`n  serializedVersion: 2`n  defaultReferences: []`n  executionOrder: 0`n  icon: {instanceID: 0}`n  userData: `n  assetBundleName: `n  assetBundleVariant: `n"
        } else {
            $importer = if ([IO.Path]::GetExtension($path) -eq '.bytes') { 'TextScriptImporter' } else { 'DefaultImporter' }
            $content += "${importer}:`n  externalObjects: {}`n  userData: `n  assetBundleName: `n  assetBundleVariant: `n"
        }
        Set-Content -LiteralPath $meta -Value $content -Encoding utf8
    }
    [regex]::Match((Get-Content -LiteralPath $meta -Raw), 'guid: ([0-9a-f]{32})').Groups[1].Value
}
function Hash-Bytes([byte[]] $bytes) {
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { [BitConverter]::ToString($algorithm.ComputeHash($bytes)).Replace('-', '').ToLowerInvariant() }
    finally { $algorithm.Dispose() }
}
$catalog = Get-Content -LiteralPath (Join-Path $clientRoot 'Assets/Scripts/WorldMapSceneCatalog.cs') -Raw
$constants = @{}
foreach ($m in [regex]::Matches($catalog, 'public const string (\w+) = "([^"]+)";')) { $constants[$m.Groups[1].Value] = $m.Groups[2].Value }
function Resolve-Token([string] $token) { if ($token.StartsWith('"')) { $token.Trim('"') } else { $constants[$token] } }
$mapBlock = [regex]::Match($catalog, '(?s)\[\] Maps\s*=\s*\{(.*?)\};').Groups[1].Value
$token = '("[^"]+"|\w+)'
$mapMatches = [regex]::Matches($mapBlock, '\(\s*' + $token + '\s*,\s*' + $token + '\s*,\s*' + $token + '\s*\)')
if ($mapMatches.Count -eq 0) { throw 'Map catalog is empty.' }
$groupPath = Join-Path $clientRoot 'Assets/AddressableAssetsData/AssetGroups/RemoteWorldMaps.asset'
$group = Get-Content -LiteralPath $groupPath -Raw
$records = @()
foreach ($match in $mapMatches) {
    $sceneId = Resolve-Token $match.Groups[1].Value
    $mapId = Resolve-Token $match.Groups[2].Value
    $sourceRoot = Join-Path $ServerProjectRoot "Game.Host.Gate/MapData/$mapId"
    $manifest = Get-Content -LiteralPath (Join-Path $sourceRoot 'manifest.json') -Raw | ConvertFrom-Json
    if ($manifest.sceneId -ne $sceneId -or $manifest.mapId -ne $mapId) { throw "Map identity mismatch: $mapId" }
    $names = @('manifest.json')
    foreach ($field in @('heightFile', 'walkableFile', 'colliderFile', 'meshColliderFile', 'groundSurfaceFile', 'navMeshFile', 'regionsFile')) {
        if (-not [string]::IsNullOrWhiteSpace($manifest.$field)) { $names += $manifest.$field }
    }
    $recorded = @{}
    $checksumPath = Join-Path $sourceRoot 'sha256.txt'
    if (Test-Path -LiteralPath $checksumPath) {
        foreach ($line in Get-Content -LiteralPath $checksumPath) {
            if ($line -match '^([a-fA-F0-9]{64})\s+\*?(.+)$') { $recorded[$Matches[2].Trim()] = $Matches[1].ToLowerInvariant() }
        }
    }
    $descriptors = @()
    $payloads = @{}
    foreach ($name in ($names | Sort-Object -Unique)) {
        if ([IO.Path]::GetFileName($name) -ne $name) { throw "Invalid map data filename: $name" }
        $bytes = [IO.File]::ReadAllBytes((Join-Path $sourceRoot $name))
        $hash = Hash-Bytes $bytes
        if ($recorded.ContainsKey($name) -and $hash -ne $recorded[$name]) { throw "Backend checksum mismatch: $mapId/$name" }
        $payloads[$name] = $bytes
        $descriptors += [ordered]@{ name = $name; bytes = $bytes.LongLength; sha256 = $hash }
    }
    $fingerprintText = ($descriptors | ForEach-Object { $_.name + ':' + $_.sha256 }) -join [char]10
    $fingerprint = Hash-Bytes ([Text.Encoding]::UTF8.GetBytes($fingerprintText))
    $descriptor = [ordered]@{ packageVersion = 1; sceneId = $sceneId; mapId = $mapId;
        querySourceFingerprint = $geometry.querySourceFingerprint; packageFingerprint = $fingerprint; files = $descriptors }
    $payloads['package.json'] = [Text.Encoding]::UTF8.GetBytes(($descriptor | ConvertTo-Json -Depth 6))
    $archive = New-Object IO.MemoryStream
    try {
        $zip = [IO.Compression.ZipArchive]::new($archive, [IO.Compression.ZipArchiveMode]::Create, $true)
        try {
            foreach ($name in ($payloads.Keys | Sort-Object)) {
                $entry = $zip.CreateEntry($name, [IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
                $output = $entry.Open()
                try { $output.Write($payloads[$name], 0, $payloads[$name].Length) } finally { $output.Dispose() }
            }
        } finally { $zip.Dispose() }
        $target = Join-Path $assetRoot ($mapId + '.bytes')
        [IO.File]::WriteAllBytes($target, $archive.ToArray())
    } finally { $archive.Dispose() }
    $guid = Ensure-Meta $target
    $address = 'game/map-traversal/' + $mapId
    if ($group -notmatch [regex]::Escape('m_GUID: ' + $guid)) {
        if ($group -match [regex]::Escape('m_Address: ' + $address)) { throw "Address already uses another GUID: $address" }
        $entry = "  - m_GUID: $guid`n    m_Address: $address`n    m_ReadOnly: 0`n    m_SerializedLabels: []`n    FlaggedDuringContentUpdateRestriction: 0`n"
        $group = [regex]::Replace($group, '(?m)^  m_ReadOnly: 0\r?$', { param($m) $entry + $m.Value })
    }
    $records += [ordered]@{ sceneId = $sceneId; mapId = $mapId; formatVersion = $manifest.formatVersion;
        address = $address; asset = "Assets/MapTraversalData/$mapId.bytes"; packageFingerprint = $fingerprint;
        archiveSha256 = (Get-FileHash -LiteralPath $target).Hash.ToLowerInvariant(); archiveBytes = (Get-Item -LiteralPath $target).Length; files = $descriptors }
}
Set-Content -LiteralPath $groupPath -Value $group -Encoding utf8 -NoNewline
Ensure-Meta $assetRoot | Out-Null
$runtimeRoot = Join-Path $clientRoot 'Assets/Scripts/MapTraversal'
Ensure-Meta $runtimeRoot | Out-Null
Get-ChildItem -LiteralPath $runtimeRoot -Recurse | Where-Object { $_.PSIsContainer -or $_.Extension -in '.cs', '.xml' } |
    ForEach-Object { Ensure-Meta $_.FullName | Out-Null }
[ordered]@{ stage = '2-map-data-import'; generatedAtUtc = [DateTime]::UtcNow.ToString('O');
    serverSourceRoot = [IO.Path]::GetFullPath($ServerProjectRoot); querySourceFingerprint = $geometry.querySourceFingerprint;
    runningServerVersionVerified = $false; addressablesContentBuilt = $false; testsRun = $false; maps = $records } |
    ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $clientRoot 'Docs/MapTraversalStage2Import.json') -Encoding utf8
Write-Output 'Imported per-map Addressables data packages with backend checksums and query-source fingerprints.'
