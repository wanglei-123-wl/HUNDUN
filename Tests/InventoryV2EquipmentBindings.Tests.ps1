$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$assetPath = Join-Path $root 'Assets/Data/Items/装备目录V2.asset'
$asset = Get-Content $assetPath -Raw
$assetMeta = Get-Content "$assetPath.meta" -Raw
$tablePath = Join-Path $root 'Assets/Data/Items/ItemDefinitionTable.asset'
$table = Get-Content $tablePath -Raw
$tableMeta = Get-Content "$tablePath.meta" -Raw
$scene = Get-Content (Join-Path $root 'Assets/Scenes/WorldShell.unity') -Raw

function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
}

Assert-True ($asset -match '(?m)^  equipmentCatalogVersion: 2$') 'Equipment catalog must match the Gate V2 version.'
Assert-True ($asset -match 'guid: f4fd379e3e1247e296acdeee35dfdb98') 'Catalog must reference its V2 ScriptableObject type.'
$catalogGuid = [regex]::Match($assetMeta, '(?m)^guid: ([0-9a-f]{32})$').Groups[1].Value
Assert-True ($catalogGuid -ne '') 'Equipment catalog is missing its Unity GUID.'
$tableGuid = [regex]::Match($tableMeta, '(?m)^guid: ([0-9a-f]{32})$').Groups[1].Value
Assert-True ($tableGuid -ne '' -and $table -match '(?m)^  equipmentCatalogVersion: 4$') `
    'The published item table must carry the twelve-slot catalog version.'

$expected = @{
    sword_001 = 1; armor_001 = 3; ring_001 = 5
    weapon_xingyun_001 = 1; helmet_xingyun_001 = 2
    armor_xingyun_001 = 3; necklace_xingyun_001 = 4
    ring_xingyun_001 = 5; bracelet_xingyun_001 = 6
}
$entries = [regex]::Matches($asset,
    '(?ms)^  - equipmentId: ([^\r\n]+)\r?\n(.*?)(?=^  - equipmentId: |\z)')
Assert-True ($entries.Count -eq $expected.Count) 'Equipment catalog must contain exactly the nine documented definitions.'
$icons = @{}
Get-ChildItem (Join-Path $root 'Assets/物品美术资源/物品图标') -Filter '*.png.meta' |
    ForEach-Object {
        $meta = Get-Content $_.FullName -Raw
        $guid = [regex]::Match($meta, '(?m)^guid: ([0-9a-f]{32})$').Groups[1].Value
        if ($guid) { $icons[$guid] = $_.FullName }
    }
$seen = @{}
foreach ($entry in $entries) {
    $id = $entry.Groups[1].Value.Trim()
    $body = $entry.Groups[2].Value
    Assert-True ($expected.ContainsKey($id) -and -not $seen.ContainsKey($id)) "Unexpected or duplicate equipmentId: $id"
    $seen[$id] = $true
    Assert-True ($body -match "(?m)^    itemId: $($id)_item$") "itemId mapping is wrong: $id"
    Assert-True ($body -match "(?m)^    categoryCode: $($expected[$id])$") "Slot category is wrong: $id"
    Assert-True ($body -match '(?m)^    displayName: ".+"$') "Display name is missing: $id"
    $iconGuid = [regex]::Match($body, '(?m)^    icon: \{fileID: 21300000, guid: ([0-9a-f]{32}), type: 3\}$').Groups[1].Value
    if ($iconGuid) {
        Assert-True ($icons.ContainsKey($iconGuid)) "Referenced equipment icon cannot be resolved: $id"
        $iconMeta = Get-Content $icons[$iconGuid] -Raw
        Assert-True ($iconMeta -match '(?m)^  textureType: 8$' -and
            $iconMeta -match '(?m)^  spriteMode: 1$') "Equipment icon is not imported as a Sprite: $id"
    }
    else {
        Assert-True ($id -eq 'sword_001' -and
            $body -match '(?m)^    icon: \{fileID: 0\}$') "Equipment icon is missing: $id"
    }
}

$documents = @{}
foreach ($match in [regex]::Matches($scene, '(?ms)^--- !u!\d+ &(\d+)\r?\n(.*?)(?=^--- !u!|\z)')) {
    $documents[$match.Groups[1].Value] = $match.Groups[2].Value
}
function Document([string]$id) {
    Assert-True ($documents.ContainsKey($id)) "Scene object is missing: $id"
    return $documents[$id]
}
function Object-Name([string]$document) {
    $value = [regex]::Match($document, '(?m)^  m_Name: (.+)$').Groups[1].Value.Trim()
    if ($value.StartsWith('"')) { return ($value | ConvertFrom-Json) }
    return $value
}
function Components([string]$gameObject) {
    return @([regex]::Matches($gameObject, '(?m)^  - component: \{fileID: (\d+)\}$') |
        ForEach-Object { $_.Groups[1].Value })
}

$runtime = Document '390961493'
Assert-True ($runtime -match 'guid: fe8c7d6ef91f46ca809f0958aa209075' -and
    $runtime -match "itemDefinitions: \{fileID: 11400000, guid: $tableGuid, type: 2\}" -and
    $runtime -notmatch '(?m)^  equipmentCatalog:' -and
    $runtime -match 'equipmentSlotsRoot: \{fileID: 1896928450\}') 'Scene InventorySystem must reference only the item table and equipment root.'
$bagSource = Get-Content (Join-Path $root 'Assets/Scripts/Inventory/Presentation/V2/InventoryBagGridPresenter.cs') -Raw
$equipmentSource = Get-Content (Join-Path $root 'Assets/Scripts/Inventory/Presentation/V2/InventoryEquipmentPanelView.cs') -Raw
$sessionSource = Get-Content (Join-Path $root 'Assets/Scripts/Inventory/Integration/V2/InventoryV2Session.cs') -Raw
foreach ($slotLabel in @('武器', '头盔', '护甲', '项链', '戒指', '手镯',
    '鞋子', '盾牌', '法宝', '法阵')) {
    Assert-True ($equipmentSource.Contains("`"$slotLabel`"")) "Equipment label is not bound: $slotLabel"
}
Assert-True ($equipmentSource -match 'root\.Find\("装备框按钮" \+ code\)' -and
    $equipmentSource -match 'root\.Find\(SlotLabels\[code\] \+ code\)' -and
    $equipmentSource -match 'candidate == icon\.transform' -and
    $equipmentSource -match 'label = candidate;') `
    'Equipment binding must support both saved and renamed slot hierarchies.'
foreach ($source in @($bagSource, $equipmentSource, $sessionSource)) {
    Assert-True ($source -match 'TryGetByEquipmentId\(' -and
        $source -notmatch 'InventoryEquipmentCatalog') `
        'Bag, equipment and session must use the shared table without the old catalog.'
}
Assert-True ($bagSource -match 'knownEquipment = items != null\s*&& items\.TryGetByEquipmentId\(' -and
    $equipmentSource -match 'if \(catalog != null\s*&& catalog\.TryGetByEquipmentId\(' -and
    $bagSource -notmatch 'CatalogMatchesClient|IsCatalogCompatible' -and
    $equipmentSource -notmatch 'CatalogMatchesClient|IsCatalogCompatible') `
    'Known equipment must render by exact ID even when catalog version labels differ.'
$bagDrag = Get-Content (Join-Path $root 'Assets/Scripts/Inventory/Presentation/V2/InventoryBagDragSlot.cs') -Raw
$equipmentDrag = Get-Content (Join-Path $root 'Assets/Scripts/Inventory/Presentation/V2/InventoryEquipmentSlotView.cs') -Raw
Assert-True ($equipmentDrag -match 'emptySlotLabel\.SetActive\(itemIdentity == null\)' -and
    $equipmentDrag -match 'emptySlotLabel\.SetActive\(true\)') `
    'Only the direct empty-slot label must follow authoritative equipment occupancy.'
$dragPreview = Get-Content (Join-Path $root 'Assets/Scripts/Inventory/Presentation/V2/InventoryDragPreview.cs') -Raw
$overlayObject = Document '1442857358'
$overlayCanvas = Document '1442857361'
$overlayRaycaster = Document '1442857359'
Assert-True ((Object-Name $overlayObject) -eq '拖拽显示层' -and
    $overlayObject -match 'm_IsActive: 1' -and
    (Document '1442857362') -match 'm_Father: \{fileID: 0\}' -and
    $overlayCanvas -match 'm_RenderMode: 0' -and
    $overlayCanvas -match 'm_SortingOrder: 100' -and
    $overlayRaycaster -match 'm_Enabled: 0') 'The drag display canvas must stay active, above the panels, and not raycast.'
Assert-True ($dragPreview -match 'canvas\.name != OverlayName' -and
    $dragPreview -match 'canvas\.gameObject\.scene != source\.gameObject\.scene' -and
    $dragPreview -match 'preview\.SetParent\(overlay\.transform, false\)' -and
    $dragPreview -match 'image\.raycastTarget = false' -and
    $dragPreview -match 'ScreenPointToLocalPointInRectangle\(') 'Drag preview must use the named canvas in the source scene without intercepting drops.'
foreach ($source in @($bagDrag, $equipmentDrag)) {
    Assert-True ($source -match 'InventoryDragPreview\.TryCreate\(' -and
        $source -match 'InventoryDragPreview\.Move\(' -and
        $source -notmatch 'AddComponent<Canvas>\(' -and
        $source -match 'void OnDrop\(PointerEventData eventData\)') 'Drag preview must draw above both canvases without intercepting equipment drop events.'
}

$worldEquipmentRoot = Document '1896928450'
Assert-True ((Object-Name (Document '1896928449')) -eq '装备栏') 'Equipment root is not the named UI object.'
$worldSlots = @([regex]::Matches($worldEquipmentRoot, '(?m)^  - \{fileID: (\d+)\}$') |
    ForEach-Object { $_.Groups[1].Value })
Assert-True ($worldSlots.Count -eq 12) 'WorldShell equipment root must retain all twelve visual slots.'
$slotNames = @('武器1', '头盔2', '护甲3', '项链4', '戒指5', '戒指6', '手镯7', '手镯8', '鞋子9', '盾牌10', '法宝11', '法阵12')
$labelNames = @('武器', '头盔', '衣服', '项链', '戒指1', '戒指2', '手镯1', '手镯2', '鞋子', 'Image', '法宝', '法阵')
for ($index = 0; $index -lt $slotNames.Count; $index++) {
    $slotTransform = (Document $worldSlots[$index])
    Assert-True ($slotTransform -match 'm_Father: \{fileID: 1896928450\}') "Equipment slot must be a direct child: $($slotNames[$index])"
    $slotObjectId = [regex]::Match($slotTransform, 'm_GameObject: \{fileID: (\d+)\}').Groups[1].Value
    Assert-True ((Object-Name (Document $slotObjectId)) -eq $slotNames[$index]) `
        "WorldShell slot order changed: $($slotNames[$index])"
    $slotComponents = Components (Document $slotObjectId)
    Assert-True (@($slotComponents | Where-Object {
        (Document $_) -match 'guid: 4e29b1a8efbd4b44bb3f3716e73f07ff'
    }).Count -eq 1) "Slot button is missing: $($slotNames[$index])"
    $visuals = @([regex]::Matches($slotTransform, '(?m)^  - \{fileID: (\d+)\}$') |
        ForEach-Object { $_.Groups[1].Value })
    Assert-True ($visuals.Count -eq 2) "Slot must contain one icon and one label: $($slotNames[$index])"
    $iconTransform = (Document $visuals[0])
    $iconId = [regex]::Match($iconTransform, 'm_GameObject: \{fileID: (\d+)\}').Groups[1].Value
    Assert-True ((Object-Name (Document $iconId)) -match '^装备图标显示板( \(\d+\))?$') `
        "Icon display is missing: $($slotNames[$index])"
    $iconComponents = Components (Document $iconId)
    Assert-True (@($iconComponents | Where-Object {
        (Document $_) -match 'guid: fe87c0e1cc204ed48ad3b37840f39efc'
    }).Count -eq 1) "Slot icon Image is missing: $($slotNames[$index])"
    $labelTransform = (Document $visuals[1])
    $labelId = [regex]::Match($labelTransform, 'm_GameObject: \{fileID: (\d+)\}').Groups[1].Value
    Assert-True ((Object-Name (Document $labelId)) -eq $labelNames[$index]) `
        "Direct empty-slot label is wrong: $($slotNames[$index])"
}
Write-Output 'PASS: shared item table, scene equipment binding and dedicated drag canvas.'
