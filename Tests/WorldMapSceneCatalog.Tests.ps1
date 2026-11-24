$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$catalogPath = Join-Path $root 'Assets/Scripts/WorldMapSceneCatalog.cs'
Add-Type -Path $catalogPath

$cases = @(
    @{ Scene = 'world_001'; Map = 'map_demo_1'; Address = 'game/scene/demo1-map' },
    @{ Scene = 'world_002'; Map = 'map_001'; Address = 'game/scene/map001' },
    @{ Scene = 'world_001'; Map = 'map_001'; Address = $null },
    @{ Scene = 'world_002'; Map = 'map_demo_1'; Address = $null },
    @{ Scene = 'world_003'; Map = 'map_001'; Address = $null },
    @{ Scene = 'world_002'; Map = 'map_unknown'; Address = $null },
    @{ Scene = 'World_002'; Map = 'map_001'; Address = $null },
    @{ Scene = $null; Map = 'map_001'; Address = $null },
    @{ Scene = 'world_002'; Map = $null; Address = $null }
)
foreach ($case in $cases) {
    $address = 'stale'
    $found = [WorldMapSceneCatalog]::TryGetAddress(
        $case.Scene, $case.Map, [ref]$address)
    if ($found -ne ($null -ne $case.Address) -or
        $address -cne $case.Address) {
        throw "Wrong scene mapping: $($case.Scene)/$($case.Map) => $address"
    }
}

$remoteMaps = Get-Content -LiteralPath (Join-Path $root 'Assets/AddressableAssetsData/AssetGroups/RemoteWorldMaps.asset') -Raw
foreach ($i in 5..10) {
    $suffix = '{0:000}' -f $i
    $scene = "world$suffix"
    $map = "map_$suffix"
    $expectedAddress = "game/scene/$scene"
    $address = $null
    $resolvedScene = $null
    $resolvedMap = $null
    $meta = Join-Path $root "Assets/地图/$scene/Generated/${scene}_Gameplay.unity.meta"
    $guid = [regex]::Match((Get-Content -LiteralPath $meta -Raw), '(?m)^guid: ([a-f0-9]{32})\r?$').Groups[1].Value
    if (!$guid -or
        ![WorldMapSceneCatalog]::TryGetAddress($scene, $map, [ref]$address) -or $address -cne $expectedAddress -or
        ![WorldMapSceneCatalog]::TryGetMapByAddress($address, [ref]$resolvedScene, [ref]$resolvedMap) -or
        $resolvedScene -cne $scene -or $resolvedMap -cne $map -or
        $remoteMaps -notmatch "m_GUID: $guid\r?\n    m_Address: $expectedAddress\r?\n") {
        throw "The Gate map pair is not linked to its packed scene: $scene / $map."
    }
    if ([WorldMapSceneCatalog]::TryGetAddress("world_$suffix", $map, [ref]$address)) {
        throw "An unregistered scene ID with an underscore was accepted: world_$suffix."
    }
}

$messages = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/NetWork/NetMessages.cs') -Raw
$group = $remoteMaps
$migration = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/Item/Editor/UsedGameContentMigration.cs') -Raw
$updater = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/StartupContentUpdate.cs') -Raw
$integration = Get-Content -LiteralPath (Join-Path $root 'Assets/Editor/Map001IntegrationSetup.cs') -Raw
$demoGuid = [regex]::Match(
    (Get-Content -LiteralPath (Join-Path $root 'Assets/Scenes/Demo 1 Map.unity.meta') -Raw),
    '(?m)^guid: ([0-9a-f]{32})\r?$').Groups[1].Value
$villageGuid = [regex]::Match(
    (Get-Content -LiteralPath (Join-Path $root 'Assets/Scenes/Map001 Gameplay.unity.meta') -Raw),
    '(?m)^guid: ([0-9a-f]{32})\r?$').Groups[1].Value

if (!$demoGuid -or !$villageGuid) { throw 'Map scene GUIDs must resolve before checking group ownership.' }

if ($messages -notmatch '(?s)public class EnterWorldResult.*?public string sceneId;\s+public string mapId;\s+public float x;' -or
    $group -notmatch "(?s)m_GUID: $demoGuid\r?\n\s+m_Address: game/scene/demo1-map.*?required-game-content" -or
    $group -notmatch "(?s)m_GUID: $villageGuid\r?\n\s+m_Address: game/scene/map001.*?required-game-content" -or
    $migration -notmatch 'EnsureEntry\(settings, remoteGroup, DemoMapScene, RemoteGameContent.DemoMapSceneAddress\)' -or
    $migration -notmatch 'remoteGroup.entries.Count != 5' -or
    $updater -notmatch 'StartupResourceDownloadPlan.Create\(Addressables.ResourceLocators\)' -or
    $updater -notmatch 'if \(!allResources.Succeeded\)' -or
    $integration -match 'RemoveAssetEntry\(AssetDatabase.AssetPathToGUID\(DemoMapPath\)\)') {
    throw 'The enter-world map field or remote map resources are not configured correctly.'
}

Write-Output 'Strict world/map mapping and remote resource checks passed.'
