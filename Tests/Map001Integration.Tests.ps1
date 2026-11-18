$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$original = Get-Content -LiteralPath (Join-Path $root 'Assets/Map001/map001.unity') -Raw
$gameplay = Get-Content -LiteralPath (Join-Path $root 'Assets/Scenes/Map001 Gameplay.unity') -Raw
$shell = Get-Content -LiteralPath (Join-Path $root 'Assets/Scenes/WorldShell.unity') -Raw
$group = Get-Content -LiteralPath (Join-Path $root 'Assets/AddressableAssetsData/AssetGroups/RemoteGameContent.asset') -Raw
$navigation = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/RemoteGameContent.cs') -Raw
$loader = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/WorldMapLayerLoader.cs') -Raw
$migration = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/Item/Editor/UsedGameContentMigration.cs') -Raw
$oldMapGuid = [regex]::Match(
    (Get-Content -LiteralPath (Join-Path $root 'Assets/Scenes/Demo 1 Map.unity.meta') -Raw),
    '(?m)^guid: ([0-9a-f]{32})$').Groups[1].Value
$mapGuid = [regex]::Match(
    (Get-Content -LiteralPath (Join-Path $root 'Assets/Scenes/Map001 Gameplay.unity.meta') -Raw),
    '(?m)^guid: ([0-9a-f]{32})$').Groups[1].Value

if ($original -notmatch '(?m)^  m_Name: Main Camera$' -or
    $gameplay -match '(?m)^  m_Name: Main Camera$' -or
    $gameplay -notmatch '(?m)^  m_Name: Directional Light$' -or
    $gameplay -notmatch '(?m)^  m_Name: Global Volume$' -or
    $gameplay -match '(?m)^  m_Name: (Canvas|GameSession)$' -or
    $gameplay -notmatch '(?s)--- !u!1 &890567748\r?\n.*?m_Name: EventSystem.*?--- !u!114 &890567752\r?\n.*?guid: 154cfb3c72e247b79f12f8b38d14d819' -or
    $shell -match '(?m)^  mapAddress:' -or
    $group -notmatch "(?s)m_GUID: $mapGuid.*?m_Address: game/scene/map001" -or
    $group -notmatch "(?s)m_GUID: $oldMapGuid.*?m_Address: game/scene/demo1-map" -or
    $navigation -notmatch 'Map001SceneAddress = WorldMapSceneCatalog.VillageMapAddress' -or
    $loader -notmatch 'WorldMapSceneCatalog.TryGetAddress\(sceneId, mapId, out mapAddress\)' -or
    $migration -notmatch 'private const string Map001Scene = "Assets/Scenes/Map001 Gameplay.unity"') {
    throw 'Map001 gameplay scene or default UI/map route is not configured correctly.'
}

Write-Output 'Map001 gameplay scene integration checks passed.'
