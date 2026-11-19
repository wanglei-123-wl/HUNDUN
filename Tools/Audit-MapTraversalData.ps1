[CmdletBinding()]
param(
    [string] $ClientProjectRoot = (Split-Path $PSScriptRoot -Parent),
    [string] $ServerProjectRoot,
    [string] $OutputPath
)

$ErrorActionPreference = 'Stop'
$clientRoot = [IO.Path]::GetFullPath($ClientProjectRoot)
if ([string]::IsNullOrWhiteSpace($ServerProjectRoot)) {
    $ServerProjectRoot = Join-Path (Split-Path $clientRoot -Parent) 'gameserver/GameServer'
}
$serverRoot = [IO.Path]::GetFullPath($ServerProjectRoot)
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $clientRoot 'Docs/MapTraversalStage1Audit.json'
}

function Get-Sha256([string] $path) {
    (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Read-MapPackage([string] $manifestPath) {
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { return $null }
    $directory = Split-Path $manifestPath -Parent
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $recordedHashes = @{}
    $checksumPath = Join-Path $directory 'sha256.txt'
    if (Test-Path -LiteralPath $checksumPath -PathType Leaf) {
        foreach ($line in Get-Content -LiteralPath $checksumPath) {
            if ($line -match '^([a-fA-F0-9]{64})\s+\*?(.+)$') {
                $recordedHashes[$Matches[2].Trim()] = $Matches[1].ToLowerInvariant()
            }
        }
    }
    $fileNames = @('manifest.json')
    foreach ($field in @('heightFile', 'walkableFile', 'colliderFile', 'meshColliderFile',
            'groundSurfaceFile', 'navMeshFile', 'regionsFile')) {
        $name = $manifest.$field
        if (-not [string]::IsNullOrWhiteSpace($name)) {
            if ([IO.Path]::GetFileName($name) -ne $name) {
                throw "Map manifest contains a non-local data filename: $manifestPath / $name"
            }
            $fileNames += $name
        }
    }
    $files = @($fileNames | Sort-Object -Unique | ForEach-Object {
        $name = $_
        $filePath = Join-Path $directory $name
        $exists = Test-Path -LiteralPath $filePath -PathType Leaf
        $hash = if ($exists) { Get-Sha256 $filePath } else { $null }
        [pscustomobject]@{
            name = $name
            exists = $exists
            bytes = if ($exists) { (Get-Item -LiteralPath $filePath).Length } else { $null }
            sha256 = $hash
            recordedSha256 = $recordedHashes[$name]
            recordedHashMatches = if ($recordedHashes.ContainsKey($name) -and $exists) {
                $hash -eq $recordedHashes[$name]
            } else { $null }
        }
    })
    $hashInput = ($files | ForEach-Object { $_.name + ':' + $_.sha256 }) -join [char]10
    $hasher = [Security.Cryptography.SHA256]::Create()
    try {
        $fingerprint = [BitConverter]::ToString($hasher.ComputeHash(
            [Text.Encoding]::UTF8.GetBytes($hashInput))).Replace('-', '').ToLowerInvariant()
    } finally { $hasher.Dispose() }
    [pscustomobject]@{
        directory = $directory
        manifest = $manifest
        requiredFilesPresent = @($files | Where-Object { -not $_.exists }).Count -eq 0
        checksumFilePresent = Test-Path -LiteralPath $checksumPath -PathType Leaf
        recordedChecksumMismatchCount = @($files | Where-Object { $_.recordedHashMatches -eq $false }).Count
        packageFingerprintSha256 = $fingerprint
        files = $files
    }
}

$catalogPath = Join-Path $clientRoot 'Assets/Scripts/WorldMapSceneCatalog.cs'
$catalog = Get-Content -LiteralPath $catalogPath -Raw
$constants = @{}
foreach ($match in [regex]::Matches($catalog, 'public const string (\w+) = "([^"]+)";')) {
    $constants[$match.Groups[1].Value] = $match.Groups[2].Value
}
function Resolve-CatalogToken([string] $token) {
    if ($token.StartsWith('"')) { return $token.Trim('"') }
    if (-not $constants.ContainsKey($token)) { throw "Unknown map catalog token: $token" }
    $constants[$token]
}
$mapBlock = [regex]::Match($catalog, '(?s)\[\] Maps\s*=\s*\{(.*?)\};')
if (-not $mapBlock.Success) { throw 'Could not read the client map catalog.' }
$sessionPath = Join-Path $clientRoot 'Assets/Scripts/GameSession.cs'
$controllerDefaults = [ordered]@{}
foreach ($match in [regex]::Matches((Get-Content -LiteralPath $sessionPath -Raw),
        'private float (localPlayerController(?:Height|Radius|SlopeLimit|StepOffset|SkinWidth)) = ([\d.]+)f;')) {
    $controllerDefaults[$match.Groups[1].Value] = [double]::Parse(
        $match.Groups[2].Value, [Globalization.CultureInfo]::InvariantCulture)
}
$clientPackages = @()
$clientDataRoot = Join-Path $clientRoot 'Assets/ServerMapData'
if (Test-Path -LiteralPath $clientDataRoot -PathType Container) {
    $clientPackages = @(Get-ChildItem -LiteralPath $clientDataRoot -Recurse -Filter manifest.json -File |
        ForEach-Object { Read-MapPackage $_.FullName })
}
$token = '("[^"]+"|\w+)'
$pattern = '\(\s*' + $token + '\s*,\s*' + $token + '\s*,\s*' + $token + '\s*\)'
$maps = @([regex]::Matches($mapBlock.Groups[1].Value, $pattern) | ForEach-Object {
    $sceneId = Resolve-CatalogToken $_.Groups[1].Value
    $mapId = Resolve-CatalogToken $_.Groups[2].Value
    $address = Resolve-CatalogToken $_.Groups[3].Value
    $bakeSource = Join-Path $clientRoot "Assets/地图/$sceneId/Editor/MapNavBuilder.cs"
    $bakeSettings = $null
    if (Test-Path -LiteralPath $bakeSource -PathType Leaf) {
        $bakeSettings = [ordered]@{}
        foreach ($match in [regex]::Matches((Get-Content -LiteralPath $bakeSource -Raw),
                'settings\.(agentRadius|agentHeight|agentClimb|agentSlope) = ([\d.]+)f;')) {
            $bakeSettings[$match.Groups[1].Value] = [double]::Parse(
                $match.Groups[2].Value, [Globalization.CultureInfo]::InvariantCulture)
        }
    }
    $server = Read-MapPackage (Join-Path $serverRoot "Game.Host.Gate/MapData/$mapId/manifest.json")
    $candidates = @($clientPackages | Where-Object {
        $_.manifest.sceneId -eq $sceneId -and $_.manifest.mapId -eq $mapId
    })
    $client = if ($candidates.Count -eq 1) { $candidates[0] } else { $null }
    $comparison = if ($null -eq $server) { 'server-source-data-missing' }
        elseif ($server.manifest.sceneId -ne $sceneId -or $server.manifest.mapId -ne $mapId) { 'server-identity-mismatch' }
        elseif ($candidates.Count -gt 1) { 'ambiguous-client-data' }
        elseif ($null -eq $client) { 'client-data-missing' }
        elseif ($server.manifest.formatVersion -ne $client.manifest.formatVersion) { 'format-and-content-differ' }
        elseif ($server.packageFingerprintSha256 -ne $client.packageFingerprintSha256) { 'content-differs' }
        else { 'source-packages-match' }
    [pscustomobject]@{
        sceneId = $sceneId; mapId = $mapId; sceneAddress = $address
        comparison = $comparison; serverSourcePackage = $server; clientExportPackage = $client
        navigationBakeSource = if ($null -ne $bakeSettings) { $bakeSource } else { $null }
        navigationBakeSettings = $bakeSettings
    }
})
if ($maps.Count -eq 0) { throw 'The client map catalog contained no map entries.' }

$algorithmFiles = @('ServerMapData.cs', 'ServerMapGroundSurfaceIndex.cs', 'ServerMapMeshCollisionIndex.cs',
    'ServerMapCollider.cs', 'ServerMapCollisionIndex.cs', 'TriangleDistance.cs', 'MapTraversal.cs')
$algorithmHashes = @($algorithmFiles | ForEach-Object {
    $path = Join-Path $serverRoot "Game.BattleCore/Maps/$_"
    [pscustomobject]@{ file = $path; sha256 = Get-Sha256 $path }
})
$runtimeReferences = @(Get-ChildItem -LiteralPath (Join-Path $clientRoot 'Assets/Scripts') -Recurse -Filter '*.cs' -File |
    Select-String -Pattern '\bServerMapBinaryReader\b|\bServerMapBaking\b|\bCanTraverseGroundSegment\b' |
    ForEach-Object { [pscustomobject]@{ file = $_.Path; line = $_.LineNumber; text = $_.Line.Trim() } })
$exporterFormat = [regex]::Match((Get-Content -LiteralPath (
    Join-Path $clientRoot 'Assets/ServerMapBaking/Shared/ServerMapFormat.cs') -Raw), 'FormatVersion = (\d+);')
$protocol = Get-Content -LiteralPath (Join-Path $clientRoot 'Assets/Scripts/NetWork/NetMessages.cs') -Raw
$entry = [regex]::Match($protocol, '(?s)public class EnterWorldResult.*?(?=\[Serializable\])').Value
$fingerprintFields = @([regex]::Matches($entry, '(?im)^\s*public\s+\w+\s+(\w*(?:mapVersion|mapHash|mapRevision|mapFingerprint)\w*)\s*;') |
    ForEach-Object { $_.Groups[1].Value })
$report = [ordered]@{
    stage = '1-data-audit-and-diagnostics'
    generatedAtUtc = [DateTime]::UtcNow.ToString('O')
    clientProjectRoot = $clientRoot; serverSourceRoot = $serverRoot
    sourceCatalog = $catalogPath
    runningServerVersionVerified = $false
    gameReproductionPerformed = $false
    automatedTestsRun = $false
    runtimeMapLoadingImplemented = $false
    localTraversalPreventionImplemented = $false
    clientControllerDefaultsSource = $sessionPath
    clientControllerDefaults = $controllerDefaults
    controllerDefaultsAreRuntimeMeasurements = $false
    clientExporterFormatVersion = [int]$exporterFormat.Groups[1].Value
    clientRuntimeServerMapReferences = $runtimeReferences
    clientEnterWorldMapFingerprintFields = $fingerprintFields
    serverTraversalSourceFiles = $algorithmHashes
    maps = $maps
}
$absoluteOutputPath = [IO.Path]::GetFullPath($OutputPath)
$report | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath $absoluteOutputPath -Encoding utf8
$maps | Select-Object sceneId, mapId, comparison | Format-Table -AutoSize
Write-Output ("Audit report: " + $absoluteOutputPath)
