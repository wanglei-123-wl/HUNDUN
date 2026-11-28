$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$table = Get-Content -LiteralPath (Join-Path $root 'Assets/Data/Items/ItemDefinitionTable.asset') -Raw
$group = Get-Content -LiteralPath (Join-Path $root 'Assets/AddressableAssetsData/AssetGroups/RemoteTrialOnly.asset') -Raw
$scene = Get-Content -LiteralPath (Join-Path $root 'Assets/Scenes/启动场景.unity') -Raw

$assetGuids = [regex]::Matches($table, '(?m)^  - \{fileID: 11400000, guid: ([0-9a-f]{32}), type: 2\}$') |
    ForEach-Object { $_.Groups[1].Value }
$groupEntries = [regex]::Matches($group, '(?s)m_GUID: ([0-9a-f]{32})\s+m_Address: ([^\r\n]+)')
$remoteAddresses = @($groupEntries | ForEach-Object { $_.Groups[2].Value.Trim() })
if ($assetGuids.Count -ne 25 -or $groupEntries.Count -ne 23 -or
    @($remoteAddresses | Select-Object -Unique).Count -ne 23) {
    throw 'The audited item table or remote group has changed; re-audit before publishing.'
}
if ($scene -match 'requiredItemIcons: \{fileID: 11400000' -or
    $scene -match 'chineseFont: \{fileID: 11400000' -or
    (Get-Content -LiteralPath (Join-Path $root 'Assets/Bootstrap/Runtime/StartupContentUpdate.cs') -Raw) -notmatch
        'Addressables\.LoadAssetAsync<ItemDefinitionTable>\(') {
    throw 'Startup must load the item table by Addressables, not hold a local asset reference.'
}

$byGuid = @{}
Get-ChildItem -LiteralPath (Join-Path $root 'Assets/掉落物品配置') -Filter '*.asset.meta' |
    ForEach-Object {
        $meta = Get-Content -LiteralPath $_.FullName -Raw
        if ($meta -match '(?m)^guid: ([0-9a-f]{32})') {
            $byGuid[$Matches[1]] = $_.FullName -replace '\.meta$', ''
        }
    }
$expected = @{}
foreach ($guid in $assetGuids) {
    if (!$byGuid.ContainsKey($guid)) { throw "Missing item definition: $guid" }
    $asset = Get-Content -LiteralPath $byGuid[$guid] -Raw
    $itemId = [regex]::Match($asset, '(?m)^    ItemId: ([^\r\n]+)').Groups[1].Value
    $address = [regex]::Match($asset, '(?m)^    RemoteIconAddress: ([^\r\n]+)').Groups[1].Value
    if ($asset -notmatch '(?m)^    Icon: \{fileID: 0\}$' -or !$address -or !$itemId) {
        throw "Local icon reference or remote address mismatch: $($byGuid[$guid])"
    }
    $canonicalId = switch ($itemId) {
        'armor_xingyun_001_item' { 'armor_001_item' }
        'ring_xingyun_001_item' { 'ring_001_item' }
        default { $itemId }
    }
    $expectedAddress = if ($itemId -eq 'dan1_huichundan') {
        'trial/icon/huichun'
    } else {
        'item/icon/' + $canonicalId
    }
    if ($address -ne $expectedAddress) {
        throw "Incorrect address for $itemId`: $address"
    }
    $expected[$address] = $true
}
if ($expected.Count -ne 23 -or @($remoteAddresses | Where-Object {
    !$expected.ContainsKey($_)
}).Count -ne 0) {
    throw 'Item definitions and Addressables remote group do not match exactly.'
}
Write-Output 'Remote-only item icon ownership: 25 definitions, 23 unique addresses, no local Icon references.'
