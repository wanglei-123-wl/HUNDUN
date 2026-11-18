$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$map = Get-Content -LiteralPath (Join-Path $root 'Assets/Scenes/Demo 1 Map.unity') -Raw
$interactionGuid = (Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/MapTeleporterInteraction.cs.meta') -Raw) `
    -replace '(?s).*guid: ([0-9a-f]{32}).*', '$1'
$prefabGuid = (Get-Content -LiteralPath (Join-Path $root 'Assets/预制体/NPC预制体/传送员.prefab.meta') -Raw) `
    -replace '(?s).*guid: ([0-9a-f]{32}).*', '$1'

$instance = [regex]::Match($map, "(?ms)^--- !u!1001 &(\d+)\r?\nPrefabInstance:\r?\n(?:(?!^--- !u!).)*?m_SourcePrefab: \{fileID: 100100000, guid: $prefabGuid, type: 3\}")
$instanceId = $instance.Groups[1].Value
$legacy = [regex]::Match($map, '(?ms)^--- !u!1 &919132147928020577\r?\nGameObject:\r?\n.*?(?=^--- !u!)').Value
if (!$instance.Success -or
    $instance.Value -notmatch '(?m)^      propertyPath: sourcePointId\r?\n      value: [a-f0-9]{32}\r?$' -or
    $instance.Value -notmatch '(?m)^      propertyPath: height\r?\n      value: 70\r?$' -or
    $map -notmatch "(?ms)^SceneRoots:\r?\n.*?^  - \{fileID: $instanceId\}" -or
    $legacy -notmatch '(?m)^  m_IsActive: 0\r?$' -or
    $map -match "guid: $interactionGuid") {
    throw 'Demo 1 Map must use the shared prefab and retain its point and light settings.'
}

Write-Output 'PASS: Demo 1 Map uses the registered shared teleporter prefab.'
