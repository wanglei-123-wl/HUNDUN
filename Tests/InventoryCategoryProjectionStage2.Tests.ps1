$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$projection = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Inventory/Presentation/V2/InventoryBagDisplayOrder.cs') -Raw
$presenter = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Inventory/Presentation/V2/InventoryBagGridPresenter.cs') -Raw
$navigation = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Inventory/Presentation/InventoryCategorySelectionView.cs') -Raw

function Assert-True([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}

Assert-True ($presenter.Contains('showAll ? index : displayOrder[index]') -and
    $presenter.Contains('drag.Bind(layoutIndex, visible ? identity : null,') -and
    $presenter.Contains('InventoryBagDisplayOrder.Filter(') -and
    $presenter.Contains('if (panel.CurrentCategory == InventoryItemCategory.All)') -and
    $presenter.Contains('controller.TryDrop(source, target, identity);') -and
    $presenter.Contains('targetBagIndex >= controller.Layout.Capacity') -and
    $presenter.Contains('panel.CurrentCategory == InventoryItemCategory.Pill') -and
    $presenter.Contains('panel.CurrentCategory == InventoryItemCategory.Other') -and
    $presenter.Contains('serverItem?.kind == "pill"') -and
    $navigation.Contains('bagScroll.StopMovement();') -and
    $navigation.Contains('bagScroll.verticalNormalizedPosition = 1f;')) `
    'Filtered presentation must use real layout indexes, keep All drag rules, and reset scroll.'

$stubs = @'
namespace Game.Inventory.Model.V2
{
    public sealed class InventoryBagLayout
    {
        private readonly string[] slots;
        public int Capacity => slots.Length;
        public InventoryBagLayout(string[] slots) { this.slots = slots; }
        public string GetIdentity(int index) => slots[index];
    }
}
public static class ProjectionCheck
{
    private static void AssertOrder(int[] actual, params int[] expected)
    {
        if (actual.Length != expected.Length)
            throw new System.Exception("Capacity changed.");
        for (int i = 0; i < actual.Length; i++)
            if (actual[i] != expected[i])
                throw new System.Exception("Wrong layout index at " + i);
    }
    public static void Run()
    {
        var layout = new Game.Inventory.Model.V2.InventoryBagLayout(
            new[] { "s:a", null, "e:x", "s:b", null, "s:c" });
        AssertOrder(Game.Inventory.Presentation.V2.InventoryBagDisplayOrder.Filter(
            layout, id => id == "s:a" || id == "s:c"),
            0, 5, 1, 4, 6, 6);
        AssertOrder(Game.Inventory.Presentation.V2.InventoryBagDisplayOrder.Filter(
            layout, id => id == "e:x"),
            2, 1, 4, 6, 6, 6);
        AssertOrder(Game.Inventory.Presentation.V2.InventoryBagDisplayOrder.Filter(
            layout, id => false),
            1, 4, 6, 6, 6, 6);
        string[] original = { "s:a", null, "e:x", "s:b", null, "s:c" };
        for (int i = 0; i < original.Length; i++)
            if (layout.GetIdentity(i) != original[i])
                throw new System.Exception("Filtering must not change All layout.");
        AssertOrder(Game.Inventory.Presentation.V2.InventoryBagDisplayOrder.Filter(
            layout, id => true),
            0, 2, 3, 5, 1, 4);
    }
}
'@
Add-Type -TypeDefinition ($projection + "`n" + $stubs) `
    -IgnoreWarnings -WarningAction SilentlyContinue
[ProjectionCheck]::Run()
Write-Output 'PASS: filtered compact order, real empty targets, placeholder guards and unchanged All layout.'
