$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$paths = @(
    'Assets/Scripts/Inventory/Model/V2/InventoryOwnershipSnapshot.cs',
    'Assets/Scripts/Alchemy/Protocol/AlchemyMessages.cs',
    'Assets/Scripts/Alchemy/Model/AlchemyMaterialSelection.cs'
)
$sources = foreach ($path in $paths) {
    (Get-Content (Join-Path $root $path) -Raw) -replace
        '(?m)^using [^\r\n]+;\r?\n', ''
}
$stubs = @'
using System;
using System.Collections.Generic;
using Game.Alchemy.Model;
using Game.Alchemy.Protocol;
using Game.Cultivation.Protocol;
using Game.Inventory.Model.V2;
using Game.Inventory.Protocol.V2;
public class NetMessageBase { public string msgId; public string type; }
namespace Game.Inventory.Protocol.V2 {
    public class InventoryV2StackChangeDto {
        public long stackId;
        public string itemId;
        public int previousCount;
        public int newCount;
        public bool isBound;
    }
}
namespace Game.Cultivation.Protocol {
    public static class RefiningSnapshotWireState {
        public static bool TryRead(string json, string task, out bool a,
            out bool b, out bool c, out bool d) {
            a = b = c = d = false;
            return true;
        }
        public static bool TryReadTaskBoolean(string json, string task,
            string field, out bool present, out bool? value) {
            present = false;
            value = null;
            return true;
        }
    }
}
public static class Fixtures {
    public static InventoryOwnershipSnapshot Inventory(long owner, long revision,
        params InventoryStackState[] stacks) =>
        new InventoryOwnershipSnapshot(owner, revision, 0, 24, 1, 1,
            new List<InventoryStackState>(stacks),
            new List<InventoryEquipmentState>(),
            new Dictionary<string, long>(),
            new InventoryAttributeBonusState(0, 0, 0));
    public static InventoryOwnershipSnapshot WithEquipment(long owner,
        long revision, InventoryEquipmentState equipment) =>
        new InventoryOwnershipSnapshot(owner, revision, 0, 24, 1, 1,
            new List<InventoryStackState>(),
            new List<InventoryEquipmentState> { equipment },
            new Dictionary<string, long>(),
            new InventoryAttributeBonusState(0, 0, 0));
}
'@
Add-Type -TypeDefinition ($stubs + "`n" + ($sources -join "`n")) `
    -IgnoreWarnings | Out-Null
function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
}
function Stack($id, $item, $count) {
    return [Game.Inventory.Model.V2.InventoryStackState]::new(
        [long]$id, [string]$item, [int]$count, $false)
}
function Recipe {
    $recipe = [Game.Alchemy.Protocol.AlchemyRecipeDto]::new()
    $recipe.recipeId = 'pill'
    $herb = [Game.Alchemy.Protocol.AlchemyRecipeMaterialDto]::new()
    $herb.itemId = 'herb'
    $herb.count = 10
    $stone = [Game.Alchemy.Protocol.AlchemyRecipeMaterialDto]::new()
    $stone.itemId = 'ling_shi'
    $stone.count = 7
    $recipe.materials = @($herb, $stone)
    return $recipe
}
$ok = [Game.Alchemy.Model.AlchemySelectionError]::None
$empty = [Game.Alchemy.Model.AlchemySelectionError]::EmptySelection
$choice = [Game.Alchemy.Model.AlchemyMaterialSelection]::new()
$owner = 42
$stackId = [long]9007199254740993
$inventory = [Fixtures]::Inventory($owner, 20,
    (Stack $stackId 'herb' 4),
    (Stack 12 'herb' 6),
    (Stack 13 'ling_shi' 99),
    (Stack 14 'other' 100))
Assert-True ($choice.SelectRecipe((Recipe), 12, $owner)) `
    'A selected recipe must capture the exact catalog revision and material IDs.'
Assert-True ($choice.CheckEligibility($inventory, 12) -eq $ok -and
    $choice.CheckReady($inventory, 12) -eq $empty -and
    $choice.SetStack($inventory, 14, 1) -eq $ok -and
    $choice.SelectedCount('other') -eq 1 -and
    $choice.CheckReady($inventory, 12) -eq $ok -and
    $choice.RemoveStack(14)) `
    'A wrong recipe material is preselectable; only an empty submit is rejected locally.'
Assert-True ($choice.SetStack($inventory, 13, 100) -eq
    [Game.Alchemy.Model.AlchemySelectionError]::InvalidQuantity -and
    $choice.SetStack($inventory, 0, 1) -eq
    [Game.Alchemy.Model.AlchemySelectionError]::InvalidQuantity -and
    $choice.SetStack($inventory, 555, 1) -eq
    [Game.Alchemy.Model.AlchemySelectionError]::StackMissing) `
    'A nonpositive, unknown or overdrawn stack must not enter the request.'
$badIdBag = [Fixtures]::Inventory($owner, 20, (Stack 15 $null 2))
Assert-True ($choice.SetStack($badIdBag, 15, 1) -eq
    [Game.Alchemy.Model.AlchemySelectionError]::MaterialMismatch) `
    'A malformed inventory item ID must be rejected without throwing.'
Assert-True ($choice.SetStack($inventory, $stackId, 4) -eq $ok -and
    $choice.SelectedCount('herb') -eq 4 -and
    $choice.CheckReady($inventory, 12) -eq $ok) `
    'Underfilling a recipe remains a valid server-checked preselection.'
Assert-True ($choice.SetStack($inventory, 12, 6) -eq $ok -and
    $choice.SetStack($inventory, 13, 7) -eq $ok -and
    $choice.SelectedCount('herb') -eq 10 -and
    $choice.SelectedCountForStack(12) -eq 6 -and
    $choice.SelectedCount('ling_shi') -eq 7 -and
    $choice.CheckReady($inventory, 12) -eq $ok) `
    'Distinct herb stacks and a partial stone stack can satisfy the recipe.'
$selectedItemId = $null
$selectedAmount = 0
Assert-True ($choice.SetStack($inventory, 13, 8) -eq $ok -and
    $choice.SelectedStackCount -eq 3 -and
    $choice.SelectedStackIds[0] -eq $stackId -and
    $choice.SelectedStackIds[1] -eq 12 -and
    $choice.SelectedStackIds[2] -eq 13 -and
    $choice.TryGetSelectedStack(13, [ref]$selectedItemId,
        [ref]$selectedAmount) -and
    $selectedItemId -eq 'ling_shi' -and $selectedAmount -eq 8 -and
    $choice.CheckReady($inventory, 12) -eq $ok) `
    'Display order follows placement; redropping a stack updates its shown count.'
Assert-True ($choice.SetStack($inventory, 13, 7) -eq $ok -and
    $choice.CheckReady($inventory, 13) -eq
    [Game.Alchemy.Model.AlchemySelectionError]::ConfigurationChanged) `
    'A new catalog revision requires reconfirming against the new recipe.'

$changedBag = [Fixtures]::Inventory($owner, 21,
    (Stack $stackId 'herb' 4), (Stack 12 'herb' 6),
    (Stack 13 'ling_shi' 99))
$materials = $null
$revision = [long]0
Assert-True ($choice.PrepareStart($changedBag, 12, [ref]$materials,
    [ref]$revision) -eq $ok -and $revision -eq 21 -and
    $materials.Length -eq 3 -and $materials[0].stackId -eq 12 -and
    $materials[1].stackId -eq 13 -and
    $materials[2].stackId -eq $stackId -and
    $materials[1].count -eq 7 -and $choice.IsLocked) `
    'Start captures sorted exact stacks and the freshly verified inventory revision.'
Assert-True ($choice.SetStack($changedBag, 13, 6) -eq
    [Game.Alchemy.Model.AlchemySelectionError]::Locked -and
    -not $choice.Clear() -and -not $choice.SelectRecipe((Recipe), 13, $owner)) `
    'An unresolved request cannot be overwritten by a new selection.'
$materials[0].count = 100
Assert-True ($choice.SelectedCount('herb') -eq 10) `
    'The returned request array cannot mutate the held selection.'
$choice.ResolveOperation($false)
$less = [Fixtures]::Inventory($owner, 22,
    (Stack $stackId 'herb' 4), (Stack 12 'herb' 5),
    (Stack 13 'ling_shi' 99))
Assert-True ($choice.CheckReady($less, 12) -eq
    [Game.Alchemy.Model.AlchemySelectionError]::InvalidQuantity -and
    $choice.CheckReady([Fixtures]::Inventory(99, 22), 12) -eq
    [Game.Alchemy.Model.AlchemySelectionError]::WrongCharacter) `
    'A changed stack or another character must block the stale selection.'
Assert-True ($choice.RemoveInvalidStacks([Fixtures]::Inventory(99, 22)) -eq 0 -and
    $choice.RemoveInvalidStacks($less) -eq 1 -and
    $choice.SelectedStackIds.Count -eq 2 -and
    $choice.SelectedCount('herb') -eq 4 -and
    $choice.SelectedCount('ling_shi') -eq 7 -and
    $choice.CheckReady($less, 12) -eq $ok) `
    'Only invalid selections in the current character inventory are removed.'
Assert-True (-not $choice.RemoveStack(12) -and
    $choice.CheckReady($less, 12) -eq $ok -and
    $choice.SetStack($less, 12, 5) -eq $ok -and
    $choice.CheckReady($less, 12) -eq $ok) `
    'Removed invalid stacks cannot be removed again; refilling is explicit.'
$choice.ResolveOperation($true)
Assert-True ($choice.SelectedStackCount -eq 0 -and
    $choice.CheckReady($changedBag, 12) -eq $empty -and
    $choice.PrepareStart($changedBag, 12, [ref]$materials,
        [ref]$revision) -eq $empty -and -not $choice.IsLocked) `
    'After a confirmed operation, the local material selection is empty.'
Assert-True ($choice.SetStack($inventory, 13, 7) -eq $ok -and
    $choice.RemoveItem('ling_shi') -eq 1 -and
    $choice.SelectedCountForStack(13) -eq 0) `
    'Removing a material slot must clear its selected stack without touching inventory.'

$freeChoice = [Game.Alchemy.Model.AlchemyMaterialSelection]::new()
Assert-True ($freeChoice.SelectRecipe((Recipe), 12, $owner) -and
    $freeChoice.SetStack($inventory, 14, 1) -eq $ok -and
    $freeChoice.SetStack($inventory, 13, 99) -eq $ok -and
    $freeChoice.SetStack($inventory, $stackId, 4) -eq $ok -and
    $freeChoice.PrepareStart($inventory, 12, [ref]$materials,
        [ref]$revision) -eq $ok -and $materials.Length -eq 3 -and
    $materials[0].stackId -eq 13 -and $materials[0].count -eq 99 -and
    $materials[1].stackId -eq 14 -and $materials[1].count -eq 1 -and
    $materials[2].stackId -eq $stackId -and $materials[2].count -eq 4) `
    'Submit includes every wrong and overselected stack without clipping or filtering.'
$freeChoice.ResolveOperation($false)
Assert-True ($freeChoice.SelectedStackCount -eq 3 -and
    $freeChoice.SetStack($inventory, 13, 7) -eq $ok) `
    'A definite rejection keeps the selection editable.'

$equipment = [Game.Inventory.Model.V2.InventoryEquipmentState]::new(
    'eq-1', 'armour', $false, 0)
$equipmentBag = [Fixtures]::WithEquipment($owner, 20, $equipment)
$equipmentChoice = [Game.Alchemy.Model.AlchemyMaterialSelection]::new()
Assert-True ($equipmentChoice.SelectRecipe((Recipe), 12, $owner) -and
    $equipmentChoice.SetUnsupportedItem($equipmentBag, 'e:eq-1') -eq $ok -and
    $equipmentChoice.SetUnsupportedItem($equipmentBag, 'e:eq-1') -eq $ok -and
    $equipmentChoice.UnsupportedIdentities.Count -eq 1 -and
    $equipmentChoice.CheckEligibility($equipmentBag, 12) -eq $ok -and
    $equipmentChoice.CheckReady($equipmentBag, 12) -eq
        [Game.Alchemy.Model.AlchemySelectionError]::UnsupportedItem -and
    $equipmentChoice.PrepareStart($equipmentBag, 12, [ref]$materials,
        [ref]$revision) -eq
        [Game.Alchemy.Model.AlchemySelectionError]::UnsupportedItem -and
    $null -eq $materials -and -not $equipmentChoice.IsLocked) `
    'An owned equipment item may be preplaced but cannot produce a fake stack ID.'
Assert-True ($equipmentChoice.RemoveUnsupportedItem('e:eq-1') -and
    $equipmentChoice.CheckReady($equipmentBag, 12) -eq $empty -and
    $equipmentChoice.SetUnsupportedItem($equipmentBag, 'e:unknown') -eq
        [Game.Alchemy.Model.AlchemySelectionError]::StackMissing) `
    'Removing equipment clears the local blocker; unknown equipment is not accepted.'

$held = [Game.Alchemy.Model.AlchemyMaterialSelection]::new()
$pending = @(
    [Game.Alchemy.Protocol.AlchemySelectedMaterialDto]::new($stackId, 4),
    [Game.Alchemy.Protocol.AlchemySelectedMaterialDto]::new(999, 2))
Assert-True (-not $held.RestorePending('pill', 12, 42,
    @($pending[0], $pending[0]), $inventory) -and
    -not $held.RestorePending('pill', 12, 42,
        @([Game.Alchemy.Protocol.AlchemySelectedMaterialDto]::new(1, 0)),
        $inventory)) `
    'Duplicate submitted stacks must not restore a selection.'
Assert-True ($held.RestorePending('pill', 12, 42, $pending, $null) -and
    $held.IsLocked -and $held.SelectedStackCount -eq 2 -and
    $held.SelectedCountForStack(999) -eq 2 -and
    $held.SetStack($inventory, 13, 1) -eq
        [Game.Alchemy.Model.AlchemySelectionError]::Locked) `
    'A saved request restores exact quantities and stays locked without inventory.'
$held.SyncRestoredStackItems([Fixtures]::Inventory(99, 20,
    (Stack $stackId 'foreign' 4)))
$item = $null
$amount = 0
Assert-True ($held.TryGetSelectedStack($stackId, [ref]$item,
    [ref]$amount) -and $null -eq $item) `
    'Another character cannot supply the restored icon or item ID.'
$held.SyncRestoredStackItems($inventory)
Assert-True ($held.TryGetSelectedStack($stackId, [ref]$item,
    [ref]$amount) -and $item -eq 'herb' -and $amount -eq 4) `
    'Fresh owner inventory may populate display item IDs without changing quantities.'
$held.ResolveOperation($false)
Assert-True ($held.SelectedStackCount -eq 2 -and
    $held.CheckReady($inventory, 12) -eq
        [Game.Alchemy.Model.AlchemySelectionError]::StackMissing -and
    $held.RemoveStack(999) -and $held.CheckReady($inventory, 12) -eq $ok) `
    'A missing stack stays visible and removable after a confirmed failure.'
Write-Output 'PASS: alchemy material preselection, exact stacks and request lock.'
