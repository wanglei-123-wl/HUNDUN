$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

function Read-ProjectFile([string]$path) {
    Get-Content -LiteralPath (Join-Path $root $path) -Raw
}

function Read-Guid([string]$path) {
    $match = [regex]::Match((Read-ProjectFile $path), '(?m)^guid: ([0-9a-f]{32})$')
    if (-not $match.Success) { throw "Missing Unity GUID: $path" }
    $match.Groups[1].Value
}

$managerSource = Read-ProjectFile 'Assets/Scripts/Monster/Network/MonsterSnapshotManager.cs'
$catalogSource = Read-ProjectFile 'Assets/Scripts/Monster/Config/MonsterCatalog.cs'
$assetSource = Read-ProjectFile 'Assets/Scripts/Monster/Config/MonsterDefinitionAsset.cs'
$editorSource = Read-ProjectFile 'Assets/Scripts/Monster/Editor/MonsterDefinitionAssetInspector.cs'
$managerGuid = Read-Guid 'Assets/Scripts/Monster/Network/MonsterSnapshotManager.cs.meta'
$catalogGuid = Read-Guid 'Assets/Data/Monsters/怪物总表.asset.meta'
$definitionScriptGuid = Read-Guid 'Assets/Scripts/Monster/Config/MonsterDefinitionAsset.cs.meta'

$scene = Read-ProjectFile 'Assets/Scenes/WorldShell.unity'
$managerBlocks = @([regex]::Matches($scene, '(?s)--- !u!114 &\d+\r?\nMonoBehaviour:.*?(?=--- !u!|\z)') |
    Where-Object { $_.Value.Contains("guid: $managerGuid, type: 3") })
if ($managerBlocks.Count -ne 1) { throw 'WorldShell must contain exactly one monster snapshot manager.' }
$block = $managerBlocks[0].Value
if (-not $block.Contains("monsterCatalog: {fileID: 11400000, guid: $catalogGuid, type: 2}") -or
    $block.Contains('prefabMappings:')) {
    throw 'The scene must reference the catalog, not legacy prefab mappings.'
}

$catalog = Read-ProjectFile 'Assets/Data/Monsters/怪物总表.asset'
$expected = @(
    @{ Id = 'Ls001'; Prefab = 'FlyingMonsterRoot' },
    @{ Id = 'Ls002'; Prefab = 'SL002MonsterRoot' }
)
foreach ($entry in $expected) {
    $id = $entry.Id
    $assetGuid = Read-Guid "Assets/Data/Monsters/$id.asset.meta"
    $prefabGuid = Read-Guid "Assets/预制体/怪物预制体/$($entry.Prefab).prefab.meta"
    $asset = Read-ProjectFile "Assets/Data/Monsters/$id.asset"
    $prefab = Read-ProjectFile "Assets/预制体/怪物预制体/$($entry.Prefab).prefab"
    if (-not $catalog.Contains("guid: $assetGuid, type: 2") -or
        -not $asset.Contains("guid: $definitionScriptGuid, type: 3") -or
        -not $asset.Contains("id: $id") -or
        -not $asset.Contains("guid: $prefabGuid, type: 3") -or
        -not $prefab.Contains('m_IsActive: 1')) {
        throw "Broken catalog entry: $id"
    }
}

$catalogEntries = @([regex]::Matches(
    $catalog, '(?m)^  - \{fileID: 11400000, guid: ([0-9a-f]{32}), type: 2\}$'))
if ($catalogEntries.Count -ne
    ([regex]::Matches($catalog, '(?m)^  - \{fileID:')).Count) {
    throw 'The catalog contains a malformed monster reference.'
}
if ($catalogEntries.Count -lt $expected.Count) {
    throw 'The catalog is missing baseline monster entries.'
}
$definitionPaths = @{}
Get-ChildItem -LiteralPath (Join-Path $root 'Assets') -Recurse -Filter '*.asset.meta' -File |
    ForEach-Object {
        $path = $_.FullName.Substring($root.Length + 1)
        $guid = [regex]::Match((Read-ProjectFile $path), '(?m)^guid: ([0-9a-f]{32})$')
        if ($guid.Success) {
            $definitionPaths[$guid.Groups[1].Value] = $path.Substring(0, $path.Length - 5)
        }
    }
$prefabPaths = @{}
Get-ChildItem -LiteralPath (Join-Path $root 'Assets') -Recurse -Filter '*.prefab.meta' -File |
    ForEach-Object {
        $path = $_.FullName.Substring($root.Length + 1)
        $guid = [regex]::Match((Read-ProjectFile $path), '(?m)^guid: ([0-9a-f]{32})$')
        if ($guid.Success) {
            $prefabPaths[$guid.Groups[1].Value] = $path.Substring(0, $path.Length - 5)
        }
    }
$seenAssets = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$seenIds = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($entry in $catalogEntries) {
    $guid = $entry.Groups[1].Value
    if (-not $seenAssets.Add($guid) -or -not $definitionPaths.ContainsKey($guid)) {
        throw "Duplicate or missing monster configuration: $guid"
    }
    $asset = Read-ProjectFile $definitionPaths[$guid]
    $id = [regex]::Match($asset, '(?m)^  id: (\S+)$')
    $name = [regex]::Match($asset, '(?m)^  displayName: (\S.+)$')
    $prefab = [regex]::Match(
        $asset, '(?m)^  prefab: \{fileID: \d+, guid: ([0-9a-f]{32}), type: 3\}$')
    if (-not $asset.Contains("guid: $definitionScriptGuid, type: 3") -or
        -not $id.Success -or -not $seenIds.Add($id.Groups[1].Value) -or
        -not $name.Success -or -not $prefab.Success -or
        -not $prefabPaths.ContainsKey($prefab.Groups[1].Value) -or
        -not (Read-ProjectFile $prefabPaths[$prefab.Groups[1].Value]).Contains('m_IsActive: 1')) {
        throw "Broken catalog entry: $guid"
    }
}
if (-not $managerSource.Contains('monsterCatalog.TryGet(id, out MonsterDefinitionAsset definition)') -or
    -not $managerSource.Contains('FindPrefabById(item.monsterId)') -or
    $managerSource.Contains('FindPrefabById(item.prefabId)') -or
    -not $managerSource.Contains('CreateMissingMonster(position, rotation)') -or
    -not $catalogSource.Contains('StringComparer.Ordinal') -or
    -not $assetSource.Contains('menuName = "游戏/怪物/怪物配置"') -or
    -not $editorSource.Contains('new GUIContent("怪物ID"') -or
    -not $editorSource.Contains('GUILayout.Button("发布到怪物总表")')) {
    throw 'Runtime lookup or Chinese publishing workflow is not wired as expected.'
}
Write-Output 'PASS: 场景、怪物总表、预制体和中文发布入口的静态连线。'
