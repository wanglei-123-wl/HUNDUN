$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$paths = @(
    'Assets/Scripts/Inventory/Protocol/V2/InventoryV2Messages.cs'
    'Assets/Scripts/Inventory/Protocol/V2/InventoryV2SnapshotValidator.cs'
    'Assets/Scripts/Inventory/Protocol/V2/InventoryV2OperationValidator.cs'
    'Assets/Scripts/Inventory/Protocol/V2/InventoryV2Codec.cs'
    'Assets/Scripts/Inventory/Model/V2/InventoryOwnershipSnapshot.cs'
    'Assets/Scripts/Inventory/Model/V2/InventoryEquipmentChanges.cs'
    'Assets/Scripts/Inventory/Model/V2/InventoryV2DeltaChanges.cs'
    'Assets/Scripts/Inventory/Model/V2/InventoryBagLayout.cs'
    'Assets/Scripts/Inventory/Integration/V2/InventoryBagLayoutStorage.cs'
    'Assets/Scripts/Inventory/Integration/V2/InventoryBagLocalController.cs'
)
$files = $paths | ForEach-Object { Get-Content (Join-Path $root $_) -Raw }
$usings = $files | ForEach-Object {
    [regex]::Matches($_, '(?m)^using [^;]+;') | ForEach-Object { $_.Value }
} | Sort-Object -Unique
$sources = ($usings -join "`n") + "`n" +
    (($files | ForEach-Object { [regex]::Replace($_, '(?m)^using [^;]+;\r?\n', '') }) -join "`n")

# The JSON substitute lets these tests run without starting or modifying Unity.
$stubs = @'
public class NetMessageBase
{
    public string msgId;
    public string type;
}
namespace UnityEngine
{
    public static class JsonUtility
    {
        public static T FromJson<T>(string json)
        {
            return System.Text.Json.JsonSerializer.Deserialize<T>(json,
                new System.Text.Json.JsonSerializerOptions { IncludeFields = true });
        }
        public static string ToJson(object value)
        {
            return System.Text.Json.JsonSerializer.Serialize(value,
                new System.Text.Json.JsonSerializerOptions { IncludeFields = true });
        }
    }
    public static class PlayerPrefs
    {
        private static readonly System.Collections.Generic.Dictionary<string, string> Data = new();
        public static string GetString(string key, string fallback) =>
            Data.TryGetValue(key, out string value) ? value : fallback;
        public static void SetString(string key, string value) => Data[key] = value;
        public static void Save() { }
    }
}
'@
Add-Type -TypeDefinition ($sources + "`n" + $stubs) -IgnoreWarnings

function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
}

$codec = [Game.Inventory.Protocol.V2.InventoryV2Codec]
$kind = [Game.Inventory.Protocol.V2.InventorySnapshotProtocol]
$legacy = '{"msgId":"inventory_snapshot_result","type":"inventory_snapshot_result","capacity":3,"slots":[{"slotId":1,"slotIndex":0,"itemId":"potion","count":2}]}'
$state = $null
$errorText = ''
Assert-True ($codec::ReadSnapshot($legacy, [ref]$state, [ref]$errorText) -eq $kind::Legacy) 'Legacy snapshots must be recognized for rejection.'

$v2 = '{"msgId":"inventory_snapshot_result","type":"inventory_snapshot_result","equipmentProtocolVersion":2,"equipmentCatalogVersion":2,"requestSequence":1001,"success":true,"characterDbId":123,"capacity":3,"inventoryRevision":23,"equipmentRevision":9,"stackItems":[{"stackId":101,"itemId":"potion","count":2,"isBound":false}],"equipmentItems":[{"itemInstanceId":"ring-a","equipmentId":"ring_001","isBound":true,"slotCode":0},{"itemInstanceId":"ring-b","equipmentId":"ring_001","isBound":false,"slotCode":6}],"wallets":[],"attributeBonus":{"maxHp":50,"attack":0,"defense":0},"serverTime":1790470800000}'
Assert-True ($codec::ReadSnapshot($v2, [ref]$state, [ref]$errorText) -eq $kind::V2) "Valid V2 snapshot failed: $errorText"
$originalState = $state
Assert-True ($state.CharacterDbId -eq 123 -and $state.Capacity -eq 3) 'Snapshot identity and capacity must be retained.'
$ownedCount = 0L
Assert-True ($state.TryGetItemCount('potion', [ref]$ownedCount) -and
    $ownedCount -eq 2) 'Item counts must come from the current ownership snapshot.'
Assert-True ($state.TryGetItemCount('ling_shi', [ref]$ownedCount) -and
    $ownedCount -eq 0) 'A valid absent item must have zero owned quantity.'
Assert-True (-not $state.TryGetItemCount('', [ref]$ownedCount)) `
    'An empty item ID must not be interpreted as an inventory item.'
$multiStack = $v2.Replace('"stackItems":[{"stackId":101,"itemId":"potion","count":2,"isBound":false}]',
    '"stackItems":[{"stackId":101,"itemId":"ling_shi","count":9,"isBound":false},{"stackId":102,"itemId":"ling_shi","count":6,"isBound":true}]')
$multiState = $null
Assert-True ($codec::ReadSnapshot($multiStack.Replace('"capacity":3', '"capacity":4'),
    [ref]$multiState, [ref]$errorText) -eq $kind::V2 -and
    $multiState.TryGetItemCount('ling_shi', [ref]$ownedCount) -and
    $ownedCount -eq 15) 'Count lookup must sum bound and unbound stacks without hard-coded costs.'
Assert-True ($state.Stacks[0].Identity -eq 's:101' -and $state.Equipment[0].Identity -eq 'e:ring-a') 'Stable typed identities must be distinct.'
Assert-True ($state.Equipment[1].SlotCode -eq 6 -and $state.CatalogMatchesClient) 'Equipped slot and catalog must be preserved.'

$push = $v2.Replace('"requestSequence":1001', '"requestSequence":0').Replace('"stackItems":[{"stackId":101,"itemId":"potion","count":2,"isBound":false}]', '"stackItems":[]').Replace('"equipmentItems":[{"itemInstanceId":"ring-a","equipmentId":"ring_001","isBound":true,"slotCode":0},{"itemInstanceId":"ring-b","equipmentId":"ring_001","isBound":false,"slotCode":6}]', '"equipmentItems":[]').Replace('"capacity":3', '"capacity":60')
Assert-True ($codec::ReadSnapshot($push, [ref]$state, [ref]$errorText) -eq $kind::V2) "Unsolicited empty inventory failed: $errorText"
Assert-True ($state.Capacity -eq 60 -and $state.Stacks.Count -eq 0 -and $state.Equipment.Count -eq 0) 'Pushed empty inventory must retain capacity for slots.'
$negativeSequence = $v2.Replace('"requestSequence":1001', '"requestSequence":-1')
Assert-True ($codec::ReadSnapshot($negativeSequence, [ref]$state, [ref]$errorText) -eq $kind::Invalid) 'Negative request sequence must be rejected.'
$unknownCatalog = $v2.Replace('"equipmentCatalogVersion":2', '"equipmentCatalogVersion":3')
Assert-True ($codec::ReadSnapshot($unknownCatalog, [ref]$state, [ref]$errorText) -eq $kind::V2) 'Unknown catalog must remain inspectable.'
Assert-True (-not $state.CatalogMatchesClient) 'Unknown catalog must block trusted local catalog use.'
$v4 = $v2.Replace('"equipmentProtocolVersion":2', '"equipmentProtocolVersion":3').
    Replace('"equipmentCatalogVersion":2', '"equipmentCatalogVersion":4').
    Replace('"ring_001"', '"artifact_001"').
    Replace('"slotCode":6', '"slotCode":11').
    Replace('"attributeBonus":{"maxHp":50,"attack":0,"defense":0}',
        '"attributeBonus":{"maxHp":50,"attack":0,"defense":0,"maxMp":30,"critRateBps":500,"critDamageBps":15000,"magicAttack":12,"magicDefense":8}')
$v4State = $null
Assert-True ($codec::ReadSnapshot($v4, [ref]$v4State, [ref]$errorText) -eq $kind::V2) `
    "V4 snapshot failed to parse: $errorText"
Assert-True ($v4State.CatalogMatchesClient -and $v4State.Equipment[1].SlotCode -eq 11 -and
    $v4State.AttributeBonus.MaxMp -eq 30 -and
    $v4State.AttributeBonus.CritRateBps -eq 500 -and
    $v4State.AttributeBonus.CritDamageBps -eq 15000 -and
    $v4State.AttributeBonus.MagicAttack -eq 12 -and
    $v4State.AttributeBonus.MagicDefense -eq 8) `
    "V4 state lost catalog, slot or bonus: catalog=$($v4State.CatalogMatchesClient), slot=$($v4State.Equipment[1].SlotCode), maxMp=$($v4State.AttributeBonus.MaxMp), crit=$($v4State.AttributeBonus.CritRateBps)."
foreach ($newSlot in 9..12) {
    $slotSnapshot = $null
    $slotJson = $v4.Replace('"slotCode":11', "`"slotCode`":$newSlot")
    Assert-True ($codec::ReadSnapshot($slotJson, [ref]$slotSnapshot, [ref]$errorText) -eq $kind::V2 -and
        $slotSnapshot.Equipment[1].SlotCode -eq $newSlot) `
        "New equipment slot $newSlot must survive a full snapshot."
}
$invalidSlot = $v4.Replace('"slotCode":11', '"slotCode":13')
Assert-True ($codec::ReadSnapshot($invalidSlot, [ref]$state, [ref]$errorText) -eq $kind::Invalid) `
    'Slot codes above the supported range must be rejected.'
$v3Items = [System.Collections.Generic.List[object]]::new()
for ($i = 1; $i -le 45; $i++) {
    $v3Items.Add(@{ itemInstanceId = "bag-$i"; equipmentId = 'ring_001'
        isBound = $false; slotCode = 0 })
}
for ($slot = 1; $slot -le 8; $slot++) {
    $v3Items.Add(@{ itemInstanceId = "equipped-$slot"; equipmentId = 'ring_001'
        isBound = $false; slotCode = $slot })
}
$v3 = @{
    msgId = 'inventory_snapshot_result'; type = 'inventory_snapshot_result'
    equipmentProtocolVersion = 3; equipmentCatalogVersion = 2
    requestSequence = 0; success = $true; characterDbId = 123
    capacity = 60; inventoryRevision = 163; equipmentRevision = 41
    stackItems = @(@{ stackId = 101; itemId = 'potion'; count = 5; isBound = $false })
    equipmentItems = $v3Items.ToArray(); wallets = @()
    attributeBonus = @{ maxHp = 60; attack = 15; defense = 8 }
    serverTime = 1790586144319
} | ConvertTo-Json -Depth 10 -Compress
Assert-True ($codec::ReadSnapshot($v3, [ref]$state, [ref]$errorText) -eq $kind::V2) "V3 push with optional fields omitted failed: $errorText"
$bagEquipment = @($state.Equipment | Where-Object SlotCode -eq 0)
$equippedEquipment = @($state.Equipment | Where-Object SlotCode -gt 0)
Assert-True ($state.Capacity -eq 60 -and $state.Stacks.Count -eq 1 -and
    $state.Equipment.Count -eq 53 -and $bagEquipment.Count -eq 45 -and
    $equippedEquipment.Count -eq 8 -and $state.CatalogMatchesClient) `
    'V3 snapshot must retain all owned instances, protocol slots and the independent catalog version.'
$v3Response = $v3.Replace('"requestSequence":0', '"requestSequence":1')
Assert-True ($codec::ReadSnapshot($v3Response, [ref]$state, [ref]$errorText) -eq $kind::V2) `
    'A matched V3 snapshot response must use the same ownership parser.'
$compatible = $v2.Replace('"equipmentProtocolVersion":2', '"equipmentProtocolVersion":4')
Assert-True ($codec::ReadSnapshot($compatible, [ref]$state, [ref]$errorText) -eq $kind::V2 -and
    $state.Equipment[1].ItemInstanceId -eq 'ring-b') `
    'A newer wire version with the same required fields must preserve ownership.'
$olderCompatible = $v2.Replace('"equipmentProtocolVersion":2', '"equipmentProtocolVersion":1')
Assert-True ($codec::ReadSnapshot($olderCompatible, [ref]$state, [ref]$errorText) -eq $kind::V2) `
    'A positive wire version with the same required fields must be accepted.'
$incompleteCompatible = $compatible.Replace('"wallets":[]', '"wallets":null')
Assert-True ($codec::ReadSnapshot($incompleteCompatible, [ref]$state, [ref]$errorText) -eq $kind::Invalid) `
    'A version label must not bypass required snapshot fields.'
$missingVersion = $v2.Replace('"equipmentProtocolVersion":2,', '')
Assert-True ($codec::ReadSnapshot($missingVersion, [ref]$state, [ref]$errorText) -eq $kind::Unsupported) 'V2-shaped message without version must not clear legacy inventory.'
$duplicate = $v2.Replace('"itemInstanceId":"ring-b"', '"itemInstanceId":"ring-a"')
Assert-True ($codec::ReadSnapshot($duplicate, [ref]$state, [ref]$errorText) -eq $kind::Invalid) 'Duplicate equipment instances must be rejected.'
$duplicateSlot = $v2.Replace('"slotCode":0', '"slotCode":6')
Assert-True ($codec::ReadSnapshot($duplicateSlot, [ref]$state, [ref]$errorText) -eq $kind::Invalid) 'Duplicate equipped slots must be rejected.'
$overCapacity = $v2.Replace('"capacity":3', '"capacity":1')
Assert-True ($codec::ReadSnapshot($overCapacity, [ref]$state, [ref]$errorText) -eq $kind::Invalid) 'Bag occupancy over capacity must be rejected.'
$missingArray = $v2.Replace('"wallets":[],', '')
Assert-True ($codec::ReadSnapshot($missingArray, [ref]$state, [ref]$errorText) -eq $kind::Invalid) 'Incomplete V2 snapshot must be rejected.'

$operation = '{"msgId":"equipment_operation_result","type":"equipment_operation_result","requestSequence":1004,"operation":"slot_move","success":true,"characterDbId":123,"inventoryRevision":25,"equipmentRevision":12,"equipmentItemChanges":[{"itemInstanceId":"ring-a","equipmentId":"ring_001","isBound":false,"slotCode":6},{"itemInstanceId":"ring-b","equipmentId":"ring_001","isBound":true,"slotCode":5}],"attributeBonus":{"maxHp":100,"attack":0,"defense":0},"serverTime":1790470800000}'
$v4Operation = $operation.Replace('"operation":"slot_move"', '"operation":"equip"').
    Replace('"inventoryRevision":25', '"inventoryRevision":24').
    Replace('"equipmentRevision":12', '"equipmentRevision":10').
    Replace('"ring_001"', '"artifact_001"').
    Replace('"slotCode":6', '"slotCode":11').
    Replace('"slotCode":5', '"slotCode":0').
    Replace('"attributeBonus":{"maxHp":100,"attack":0,"defense":0}',
        '"attributeBonus":{"maxHp":100,"attack":0,"defense":0,"maxMp":40,"critRateBps":600,"critDamageBps":16000,"magicAttack":20,"magicDefense":10}')
$v4Result = $null
$v4Replaced = $null
$v4Intent = [Game.Inventory.Model.V2.InventoryEquipmentIntent]::new(
    [Game.Inventory.Model.V2.InventoryEquipmentOperationKind]::Equip, 'e:ring-a', 0, 11)
Assert-True ($codec::TryReadOperation($v4Operation, [ref]$v4Result, [ref]$errorText) -and
    [Game.Inventory.Model.V2.InventoryEquipmentChanges]::TryApply(
        $v4State, $v4Intent, $v4Result, [ref]$v4Replaced, [ref]$errorText) -and
    $v4Replaced.Equipment[0].SlotCode -eq 11 -and
    $v4Replaced.Equipment[1].SlotCode -eq 0 -and
    $v4Replaced.AttributeBonus.MaxMp -eq 40 -and
    $v4Replaced.AttributeBonus.CritRateBps -eq 600) `
    "New-slot replacement must apply both instance changes and overwrite equipment bonuses: $errorText"
$result = $null
Assert-True ($codec::TryReadOperation($operation, [ref]$result, [ref]$errorText)) "Valid move operation failed: $errorText"
Assert-True ($result.equipmentItemChanges.Length -eq 2) 'Atomic swap must retain both item changes.'
$invalidOperation = $operation.Replace('"slotCode":5', '"slotCode":6')
Assert-True (-not $codec::TryReadOperation($invalidOperation, [ref]$result, [ref]$errorText)) 'Colliding operation slots must be rejected.'
$failure = $operation.Replace('"success":true', '"success":false').Replace('"equipmentItemChanges":[{"itemInstanceId":"ring-a","equipmentId":"ring_001","isBound":false,"slotCode":6},{"itemInstanceId":"ring-b","equipmentId":"ring_001","isBound":true,"slotCode":5}]', '"equipmentItemChanges":[]')
Assert-True ($codec::TryReadOperation($failure, [ref]$result, [ref]$errorText)) 'Server rejection must remain parseable.'

$changes = [Game.Inventory.Model.V2.InventoryEquipmentChanges]
$intentKind = [Game.Inventory.Model.V2.InventoryEquipmentOperationKind]
$equipIntent = [Game.Inventory.Model.V2.InventoryEquipmentIntent]::new($intentKind::Equip, 'e:ring-a', 0, 6)
$equipJson = $operation.Replace('"requestSequence":1004', '"requestSequence":1002').Replace('"operation":"slot_move"', '"operation":"equip"').Replace('"inventoryRevision":25', '"inventoryRevision":24').Replace('"equipmentRevision":12', '"equipmentRevision":10').Replace('"itemInstanceId":"ring-b","equipmentId":"ring_001","isBound":true,"slotCode":5', '"itemInstanceId":"ring-b","equipmentId":"ring_001","isBound":true,"slotCode":0')
$equipResult = $null
Assert-True ($codec::TryReadOperation($equipJson, [ref]$equipResult, [ref]$errorText)) "Equip fixture failed: $errorText"
$equipped = $null
Assert-True ($changes::TryApply($originalState, $equipIntent, $equipResult, [ref]$equipped, [ref]$errorText)) "Atomic replacement failed: $errorText"
Assert-True ($equipped.Equipment[0].SlotCode -eq 6 -and $equipped.Equipment[1].SlotCode -eq 0 -and $equipped.AttributeBonus.MaxHp -eq 100) 'Replacement must update both owners and authoritative bonus.'
$unknownChange = $equipJson.Replace('"itemInstanceId":"ring-b"', '"itemInstanceId":"ring-c"')
$unknownResult = $null
$null = $codec::TryReadOperation($unknownChange, [ref]$unknownResult, [ref]$errorText)
Assert-True (-not $changes::TryApply($originalState, $equipIntent, $unknownResult, [ref]$equipped, [ref]$errorText)) 'Unexpected item identities must not be applied.'
$badRevision = $equipJson.Replace('"equipmentRevision":10', '"equipmentRevision":12')
$badResult = $null
$null = $codec::TryReadOperation($badRevision, [ref]$badResult, [ref]$errorText)
Assert-True (-not $changes::TryApply($originalState, $equipIntent, $badResult, [ref]$equipped, [ref]$errorText)) 'Revision gaps must request a full snapshot.'
$unequipIntent = [Game.Inventory.Model.V2.InventoryEquipmentIntent]::new($intentKind::Unequip, 'e:ring-b', 6, 0)
$unequipJson = $equipJson.Replace('"operation":"equip"', '"operation":"unequip"').Replace('"inventoryRevision":24', '"inventoryRevision":24').Replace('"equipmentRevision":10', '"equipmentRevision":10').Replace('"equipmentItemChanges":[{"itemInstanceId":"ring-a","equipmentId":"ring_001","isBound":false,"slotCode":6},{"itemInstanceId":"ring-b","equipmentId":"ring_001","isBound":true,"slotCode":0}]', '"equipmentItemChanges":[{"itemInstanceId":"ring-b","equipmentId":"ring_001","isBound":true,"slotCode":0}]')
$unequipResult = $null
Assert-True ($codec::TryReadOperation($unequipJson, [ref]$unequipResult, [ref]$errorText)) 'Unequip fixture failed.'
$unequipped = $null
Assert-True ($changes::TryApply($originalState, $unequipIntent, $unequipResult, [ref]$unequipped, [ref]$errorText)) "Unequip failed: $errorText"
Assert-True ($unequipped.Equipment[1].SlotCode -eq 0) 'Unequip must add the instance to bag ownership.'
$swapStateJson = $v2.Replace('"slotCode":0', '"slotCode":5')
$swapState = $null
Assert-True ($codec::ReadSnapshot($swapStateJson, [ref]$swapState, [ref]$errorText) -eq $kind::V2) 'Swap fixture failed.'
$moveIntent = [Game.Inventory.Model.V2.InventoryEquipmentIntent]::new($intentKind::SlotMove, 'e:ring-a', 5, 6)
$swapJson = $operation.Replace('"inventoryRevision":25', '"inventoryRevision":23').Replace('"equipmentRevision":12', '"equipmentRevision":10')
$swapResult = $null
Assert-True ($codec::TryReadOperation($swapJson, [ref]$swapResult, [ref]$errorText)) 'Swap response fixture failed.'
$swapped = $null
Assert-True ($changes::TryApply($swapState, $moveIntent, $swapResult, [ref]$swapped, [ref]$errorText)) "Equipped swap failed: $errorText"
Assert-True ($swapped.Equipment[0].SlotCode -eq 6 -and $swapped.Equipment[1].SlotCode -eq 5 -and $swapped.InventoryRevision -eq 23) 'Slot swap must not advance bag revision.'
$emptyMove = $unequipJson.Replace('"operation":"unequip"', '"operation":"slot_move"').Replace('"inventoryRevision":24', '"inventoryRevision":23').Replace('"slotCode":0}]', '"slotCode":5}]')
$emptyMoveResult = $null
Assert-True ($codec::TryReadOperation($emptyMove, [ref]$emptyMoveResult, [ref]$errorText)) 'Empty-target move fixture failed.'
$emptyMoveIntent = [Game.Inventory.Model.V2.InventoryEquipmentIntent]::new($intentKind::SlotMove, 'e:ring-b', 6, 5)
$moved = $null
Assert-True ($changes::TryApply($originalState, $emptyMoveIntent, $emptyMoveResult, [ref]$moved, [ref]$errorText)) "Empty-target slot move failed: $errorText"
Assert-True ($moved.Equipment[1].SlotCode -eq 5 -and $moved.InventoryRevision -eq 23) 'Moving to an empty equipped slot must not change bag membership.'
$fullBagJson = $v2.Replace('"capacity":3', '"capacity":2')
$fullBag = $null
Assert-True ($codec::ReadSnapshot($fullBagJson, [ref]$fullBag, [ref]$errorText) -eq $kind::V2) 'Full-bag fixture failed.'
Assert-True (-not $changes::TryApply($fullBag, $unequipIntent, $unequipResult, [ref]$moved, [ref]$errorText)) 'Unequip into a full bag must not be applied.'
$resync = $equipJson.Replace('"serverTime":', '"requiresSnapshot":true,"serverTime":')
$resyncResult = $null
$null = $codec::TryReadOperation($resync, [ref]$resyncResult, [ref]$errorText)
Assert-True (-not $changes::TryApply($originalState, $equipIntent, $resyncResult, [ref]$moved, [ref]$errorText)) 'Server-directed resync must not apply a partial delta.'

$storage = [Game.Inventory.Integration.V2.InventoryBagPlayerPrefsStorage]::new()
$bag = [Game.Inventory.Integration.V2.InventoryBagLocalController]::new($storage)
$bag.ApplySnapshot($originalState)
Assert-True ($bag.Layout.Slots.Count -eq 3 -and $bag.Layout.GetIdentity(0) -eq 's:101' -and $bag.Layout.GetIdentity(1) -eq 'e:ring-a') 'Only bag-owned items should occupy local slots.'
$appliedEquip = $null
Assert-True ($changes::TryApply($originalState, $equipIntent, $equipResult, [ref]$appliedEquip, [ref]$errorText)) 'Equip fixture must still apply.'
$bag.ApplySnapshot($appliedEquip)
Assert-True ($bag.Layout.GetIdentity(0) -eq 's:101' -and $bag.Layout.GetIdentity(1) -eq 'e:ring-b') 'Replacement must preserve stack position and append displaced equipment.'
$bag.ApplySnapshot($originalState)
$bag.ApplySnapshot($unequipped)
Assert-True ($bag.TryDrop(2, 0, 'e:ring-b')) 'Server-authorized unequip may be placed into the drop target locally.'
Assert-True ($bag.Layout.GetIdentity(0) -eq 'e:ring-b' -and $bag.Layout.GetIdentity(2) -eq 's:101') 'Occupied bag drop should only swap local visual positions.'
$bag.ApplySnapshot($originalState)
Assert-True ($bag.TryDrop(2, 0, 's:101')) 'Restoring the original bag item must preserve its identity.'
Assert-True ($bag.TryDrop(0, 1, 's:101')) 'Occupied target must exchange.'
Assert-True ($bag.Layout.GetIdentity(0) -eq 'e:ring-a' -and $bag.Layout.GetIdentity(1) -eq 's:101') 'Exchange must preserve both identities.'
Assert-True ($bag.TryDrop(1, 2, 's:101')) 'Empty target must accept a move.'
Assert-True ($bag.Layout.GetIdentity(1) -eq $null -and $bag.Layout.GetIdentity(2) -eq 's:101') 'Move must leave source empty.'
Assert-True (-not $bag.TryDrop(1, 0, 's:101') -and -not $bag.TryDrop(0, 5, 'e:ring-a')) 'Empty source and out-of-range drops must be ignored.'
Assert-True (-not $bag.TryDrop(0, 2, 's:101')) 'A stale drag identity must not move another item.'
$bag.Reset()
$bag.ApplySnapshot($originalState)
Assert-True ($bag.Layout.GetIdentity(2) -eq 's:101') 'Persisted holes and positions must survive reload.'
$newBagItem = $v2.Replace('"stackId":101', '"stackId":102')
$next = $null
Assert-True ($codec::ReadSnapshot($newBagItem, [ref]$next, [ref]$errorText) -eq $kind::V2) 'Changed ownership fixture must parse.'
$bag.ApplySnapshot($next)
Assert-True ($bag.Layout.GetIdentity(0) -eq 'e:ring-a' -and $bag.Layout.GetIdentity(1) -eq 's:102' -and $bag.Layout.GetIdentity(2) -eq $null) 'Snapshot must prune absent IDs and fill an empty slot.'
$otherCharacter = $v2.Replace('"characterDbId":123', '"characterDbId":124')
$other = $null
Assert-True ($codec::ReadSnapshot($otherCharacter, [ref]$other, [ref]$errorText) -eq $kind::V2) 'Other character fixture must parse.'
$bag.ApplySnapshot($other)
Assert-True ($bag.Layout.GetIdentity(0) -eq 's:101') 'Local positions must not leak between characters.'

$delta = [Game.Inventory.Model.V2.InventoryV2DeltaChanges]
$deltaResult = [Game.Inventory.Model.V2.InventoryV2DeltaResult]
$stackChange = [Game.Inventory.Protocol.V2.InventoryV2StackChangeDto]::new()
$stackChange.stackId = 101
$stackChange.itemId = 'potion'
$stackChange.previousCount = 2
$stackChange.newCount = 5
$equipmentChange = [Game.Inventory.Protocol.V2.InventoryV2EquipmentDto]::new()
$equipmentChange.itemInstanceId = 'ring-c'
$equipmentChange.equipmentId = 'ring_001'
$equipmentChange.slotCode = 0
$walletChange = [Game.Inventory.Protocol.V2.InventoryV2WalletChangeDto]::new()
$walletChange.currencyId = 'gold_coin'
$walletChange.previousBalance = 0
$walletChange.newBalance = 7
$stackChanges = [Game.Inventory.Protocol.V2.InventoryV2StackChangeDto[]]@($stackChange)
$equipmentChanges = [Game.Inventory.Protocol.V2.InventoryV2EquipmentDto[]]@($equipmentChange)
$walletChanges = [Game.Inventory.Protocol.V2.InventoryV2WalletChangeDto[]]@($walletChange)
$deltaState = $null
Assert-True ($delta::TryApplyPickup($originalState, 24, $stackChanges, $equipmentChanges,
    $walletChanges, [ref]$deltaState, [ref]$errorText) -eq $deltaResult::Applied) "V2 pickup delta failed: $errorText"
Assert-True ($deltaState.Stacks[0].Count -eq 5 -and $deltaState.Equipment.Count -eq 3 -and
    $deltaState.Equipment[2].Identity -eq 'e:ring-c' -and
    $deltaState.Wallets['gold_coin'] -eq 7 -and
    $deltaState.EquipmentRevision -eq 9) 'Pickup must apply absolute counts, instance identities and balances.'
$bag.ApplySnapshot($deltaState)
Assert-True ($bag.Layout.Slots -contains 'e:ring-a' -and
    $bag.Layout.Slots -contains 'e:ring-c' -and
    $bag.Layout.Slots -contains 's:101') 'Pickup must preserve prior local positions and place the new instance.'
$replayState = $null
Assert-True ($delta::TryApplyPickup($deltaState, 24, $stackChanges, $equipmentChanges,
    $walletChanges, [ref]$replayState, [ref]$errorText) -eq $deltaResult::Stale -and
    $null -eq $replayState) 'Duplicate pickup revision must not add items twice.'
$gapState = $null
Assert-True ($delta::TryApplyPickup($deltaState, 26, $stackChanges, $equipmentChanges,
    $walletChanges, [ref]$gapState, [ref]$errorText) -eq $deltaResult::NeedsSnapshot -and
    $null -eq $gapState) 'Revision gap must require a complete snapshot.'
$stackChange.previousCount = 4
Assert-True ($delta::TryApplyPickup($deltaState, 25, $stackChanges,
    [Game.Inventory.Protocol.V2.InventoryV2EquipmentDto[]]@(),
    [Game.Inventory.Protocol.V2.InventoryV2WalletChangeDto[]]@(),
    [ref]$gapState, [ref]$errorText) -eq $deltaResult::NeedsSnapshot -and
    $deltaState.Stacks[0].Count -eq 5) 'Incorrect previous count must not partially change ownership.'
$stackChange.previousCount = 5
$stackChange.newCount = 0
$removed = $null
Assert-True ($delta::TryApplyPickup($deltaState, 25, $stackChanges,
    [Game.Inventory.Protocol.V2.InventoryV2EquipmentDto[]]@(),
    [Game.Inventory.Protocol.V2.InventoryV2WalletChangeDto[]]@(),
    [ref]$removed, [ref]$errorText) -eq $deltaResult::Applied) "Stack removal failed: $errorText"
$bag.ApplySnapshot($removed)
Assert-True ($removed.Stacks.Count -eq 0 -and
    $bag.Layout.Slots -notcontains 's:101') 'Zero count must remove the stack and its local layout key.'
Write-Output 'PASS: V2 protocol, local bag order and authoritative equipment changes.'
