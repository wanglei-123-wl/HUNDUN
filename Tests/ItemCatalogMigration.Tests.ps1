$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$catalogPath = Join-Path $root 'Assets/Data/Items/ItemDefinitionTable.asset'
$oldPath = Join-Path $root 'Assets/Data/Items/装备目录V2.asset'
$assetFolder = Join-Path $root 'Assets/掉落物品配置'
$iconFolder = Join-Path $root 'Assets/物品美术资源/物品图标'
$table = Get-Content -LiteralPath $catalogPath -Raw
$oldCatalog = Get-Content -LiteralPath $oldPath -Raw

function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
}

function Field([string]$source, [string]$field, [int]$indent) {
    $prefix = ' ' * $indent
    return [regex]::Match($source,
        "(?m)^$prefix$([regex]::Escape($field)): ?([^\r\n]*)$").Groups[1].Value
}

Assert-True ($table -match '(?m)^  items: \[\]$') 'Legacy inline item entries must be empty.'
Assert-True ($table -match '(?m)^  equipmentCatalogVersion: 4$') `
    'Shared item table must retain the Gate equipment catalog version.'
$published = @([regex]::Matches($table,
    '(?m)^  - \{fileID: 11400000, guid: ([0-9a-f]{32}), type: 2\}$') |
    ForEach-Object { $_.Groups[1].Value })
Assert-True ($published.Count -eq (@($published | Select-Object -Unique)).Count) `
    'Published references must not repeat a GUID.'

$byGuid = @{}
$byItem = @{}
$byEquipment = @{}
$spriteGuids = @{}
Get-ChildItem -LiteralPath $iconFolder -Filter '*.png.meta' | ForEach-Object {
    $meta = Get-Content -LiteralPath $_.FullName -Raw
    $guid = [regex]::Match($meta, '(?m)^guid: ([0-9a-f]{32})$').Groups[1].Value
    if ($guid) {
        Assert-True (($meta -match '(?m)^  textureType: 8$') -and
            ($meta -match '(?m)^  spriteMode: 1$')) `
            "Referenced icon must be imported as a Sprite: $($_.Name)"
        $spriteGuids[$guid] = $true
    }
}
Get-ChildItem -LiteralPath $assetFolder -Filter '*.asset' | ForEach-Object {
    $meta = "$($_.FullName).meta"
    if (-not (Test-Path -LiteralPath $meta)) { return }
    $source = Get-Content -LiteralPath $_.FullName -Raw
    if ($source -notmatch 'guid: 8421170d3fcf4ee19af40e46ad78fd45') { return }
    $guid = [regex]::Match((Get-Content -LiteralPath $meta -Raw),
        '(?m)^guid: ([0-9a-f]{32})$').Groups[1].Value
    Assert-True ($guid -ne '' -and -not $byGuid.ContainsKey($guid)) `
        "Missing or duplicate asset GUID: $($_.Name)"
    $id = Field $source 'ItemId' 4
    Assert-True ($id -ne '' -and -not $byItem.ContainsKey($id)) `
        "Missing or duplicate item ID: $($_.Name)"
    $entry = @{ Path = $_.FullName; Text = $source; Id = $id; Guid = $guid }
    $iconGuid = [regex]::Match((Field $source 'Icon' 4),
        'guid: ([0-9a-f]{32})').Groups[1].Value
    Assert-True ($iconGuid -ne '' -and $spriteGuids.ContainsKey($iconGuid)) `
        "Published item has no resolvable Sprite icon: $id"
    $byGuid[$guid] = $entry
    $byItem[$id] = $entry
    $equipmentId = Field $source 'EquipmentId' 4
    if ($equipmentId) {
        Assert-True (-not $byEquipment.ContainsKey($equipmentId)) `
            "Duplicate equipment ID: $equipmentId"
        $byEquipment[$equipmentId] = $entry
    }
}

Assert-True ($published.Count -eq $byGuid.Count) `
    'Every authored item must be published exactly once.'
foreach ($guid in $published) {
    Assert-True ($byGuid.ContainsKey($guid)) "Published GUID has no item asset: $guid"
}

$oldEntries = [regex]::Matches($oldCatalog,
    '(?ms)^  - equipmentId: ([^\r\n]+)\r?\n(.*?)(?=^  - equipmentId: |\z)')
foreach ($match in $oldEntries) {
    $equipmentId = $match.Groups[1].Value
    $old = $match.Groups[2].Value
    $icon = Field $old 'icon' 4
    if ($icon -eq '{fileID: 0}') {
        Assert-True (($equipmentId -eq 'sword_001') -and
            (-not $byEquipment.ContainsKey($equipmentId))) `
            "An unillustrated equipment type must not be silently published: $equipmentId"
        continue
    }
    Assert-True ($byEquipment.ContainsKey($equipmentId)) `
        "Equipment entry is not published: $equipmentId"
    $item = $byEquipment[$equipmentId]
    Assert-True ($item.Id -eq (Field $old 'itemId' 4)) `
        "Item ID mapping changed: $equipmentId"
    Assert-True ((Field $item.Text 'EquipmentCategory' 4) -eq
        (Field $old 'categoryCode' 4)) "Equipment category changed: $equipmentId"
    Assert-True ((Field $item.Text 'DisplayName' 4) -eq
        (Field $old 'displayName' 4)) "Equipment name changed: $equipmentId"
    Assert-True ((Field $item.Text 'Icon' 4) -eq $icon) `
        "Equipment icon changed: $equipmentId"
}

$potion = $byItem['hp_potion_01']
$gold = $byItem['gold_coin']
Assert-True ($potion -and $gold) 'Legacy potion and currency must be published.'
Assert-True (((Field $potion.Text 'MaxStack' 4) -eq '99') -and
    ((Field $potion.Text 'Category' 4) -eq '2') -and
    ((Field $gold.Text 'IsCurrency' 4) -eq '1') -and
    ((Field $gold.Text 'MaxStack' 4) -eq '999999')) `
    'Legacy potion and currency metadata must remain intact.'
Assert-True ((Field $potion.Text 'DisplayName' 4) -eq
    '"\u751F\u547D\u836F\u6C34"') 'Potion name must remain unchanged.'
Assert-True ((Field $potion.Text 'Description' 4) -eq
    '"\u56DE\u590D\u5341\u70B9\u751F\u547D\u503C"') `
    'Potion description must remain unchanged.'
Assert-True ((Field $gold.Text 'DisplayName' 4) -eq
    '"\u94B1\u5305\u8D27\u5E01"') 'Currency name must remain unchanged.'
Assert-True ((Field $potion.Text 'Icon' 4) -eq
    '{fileID: 21300000, guid: fee25953bb5b76f40950a89d5a1147a7, type: 3}' -and
    (Field $gold.Text 'Icon' 4) -eq
    '{fileID: 21300000, guid: 2c20aa8d7600b5349a14e6b6520bc719, type: 3}') `
    'Missing legacy icons must use the matching existing item Sprites.'
Assert-True ((Field $potion.Text 'PickupSound' 4) -eq '{fileID: 0}' -and
    (Field $gold.Text 'PickupSound' 4) -eq '{fileID: 0}') `
    'Missing legacy pickup sounds must not keep dangling GUIDs.'
Assert-True (((Field $potion.Text 'WorldPrefab' 4) -eq
    '{fileID: 949667627940910289, guid: 1f4e1cd68b6d2ce43a6063a59f8af985, type: 3}') -and
    ((Field $gold.Text 'WorldPrefab' 4) -eq
    '{fileID: 949667627940910289, guid: 1f4e1cd68b6d2ce43a6063a59f8af985, type: 3}')) `
    'Potion and currency world prefab references must remain unchanged.'
Write-Output 'PASS: published item assets, equipment ID mappings and existing icon references.'
