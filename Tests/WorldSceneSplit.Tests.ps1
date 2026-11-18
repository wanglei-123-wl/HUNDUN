$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$shell = Get-Content -LiteralPath (Join-Path $root 'Assets/Scenes/WorldShell.unity') -Raw
$map = Get-Content -LiteralPath (Join-Path $root 'Assets/Scenes/Demo 1 Map.unity') -Raw
$map001 = Get-Content -LiteralPath (Join-Path $root 'Assets/Scenes/Map001 Gameplay.unity') -Raw
$login = Get-Content -LiteralPath (Join-Path $root 'Assets/Scenes/SampleScene.unity') -Raw
$group = Get-Content -LiteralPath (Join-Path $root 'Assets/AddressableAssetsData/AssetGroups/RemoteGameContent.asset') -Raw
$navigation = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/RemoteGameContent.cs') -Raw
$loader = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/WorldMapLayerLoader.cs') -Raw
$session = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/GameSession.cs') -Raw
$navMeshPath = Join-Path $root 'Assets/Scenes/Demo 1 Map/NavMesh-Navigation.asset'
$navMeshGuid = [regex]::Match(
    (Get-Content -LiteralPath "$navMeshPath.meta" -Raw),
    '(?m)^guid: ([0-9a-f]{32})$').Groups[1].Value
$shellGuid = [regex]::Match(
    (Get-Content -LiteralPath (Join-Path $root 'Assets/Scenes/WorldShell.unity.meta') -Raw),
    '(?m)^guid: ([0-9a-f]{32})$').Groups[1].Value
$mapGuid = [regex]::Match(
    (Get-Content -LiteralPath (Join-Path $root 'Assets/Scenes/Map001 Gameplay.unity.meta') -Raw),
    '(?m)^guid: ([0-9a-f]{32})$').Groups[1].Value
$loaderGuid = [regex]::Match(
    (Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/WorldMapLayerLoader.cs.meta') -Raw),
    '(?m)^guid: ([0-9a-f]{32})$').Groups[1].Value

foreach ($name in @('GameSession', 'EntityManager', 'NetworkSystem', 'EventSystem')) {
    if ($shell -notmatch "(?m)^  m_Name: $name`r?`n") {
        throw "Shared scene is missing $name."
    }
    if ($map -match "(?m)^  m_Name: $name`r?`n") {
        throw "Map scene contains a duplicate $name."
    }
}
if ($shell -notmatch '(?m)^--- !u!223 &\d+\r?\nCanvas:' -or
    $shell -notmatch '(?m)^  startOnSceneLoad: 0$' -or
    $shell -notmatch "(?m)^  m_Script: \{fileID: 11500000, guid: $loaderGuid, type: 3\}$" -or
    $shell -match '(?m)^  mapAddress:' -or
    $shell -match '(?m)^  m_Name: _Terrain$' -or
    $map -notmatch '(?m)^  m_Name: _Terrain$' -or
    $map -notmatch '(?m)^  m_Name: ServerMapRoot$' -or
    $map001 -notmatch '(?m)^  m_Name: Directional Light$' -or
    $login -notmatch '(?m)^  successSceneName: WorldShell$' -or
    $group -notmatch "(?s)m_GUID: $shellGuid.*?m_Address: game/scene/world" -or
    $group -notmatch "(?s)m_GUID: $mapGuid.*?m_Address: game/scene/map001" -or
    $navigation -notmatch 'LoadSceneMode.Additive' -or
    $loader -notmatch 'WorldMapSceneCatalog.TryGetAddress\(sceneId, mapId, out mapAddress\)' -or
    $loader -notmatch 'gameSession.StartSession\(\)' -or
    $session -notmatch 'if \(startOnSceneLoad\)') {
    throw 'Shared UI scene or additive map loading is not wired correctly.'
}

if (-not (Test-Path -LiteralPath $navMeshPath -PathType Leaf) -or
    -not $navMeshGuid -or
    $map -notmatch "m_NavMeshData: \{fileID: 23800000, guid: $navMeshGuid, type: 2\}") {
    throw 'Demo 1 Map must reference its relocated NavMesh data.'
}

Write-Output 'Shared UI and Demo 1 map split checks passed.'
