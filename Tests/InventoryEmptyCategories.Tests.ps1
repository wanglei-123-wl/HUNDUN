$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$enums = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Inventory/Model/InventoryEnums.cs') -Raw
$panel = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Inventory/Presentation/InventoryPanelView.cs') -Raw
$presenter = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Inventory/Presentation/V2/InventoryBagGridPresenter.cs') -Raw

if ($presenter -notmatch '(?s)private bool MatchesCategory\(.*?if \(panel\.CurrentCategory == InventoryItemCategory\.Pill\s*\|\| panel\.CurrentCategory == InventoryItemCategory\.Other\)\s*return false;' -or
    $presenter -notmatch '(?s)drag\.enabled = showAll \|\| allowEquipmentDrag \|\| allowConsumableDrag;' -or
    $presenter -notmatch '(?s)private void HandleUnequipRequested\(.*?panel\.CurrentCategory == InventoryItemCategory\.Pill\s*\|\| panel\.CurrentCategory == InventoryItemCategory\.Other') {
    throw 'Pill and Other must project no items and accept no bag drops.'
}

$stubs = @'
using System;
using UnityEngine;
using UnityEngine.UI;
namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class DisallowMultipleComponent : Attribute {}
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SerializeField : Attribute {}
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class Header : Attribute
    {
        public Header(string title) {}
    }
    public class MonoBehaviour
    {
        public GameObject gameObject = new GameObject();
    }
    public class GameObject
    {
        public bool activeSelf;
        public void SetActive(bool active) { activeSelf = active; }
    }
}
namespace UnityEngine.UI
{
    public sealed class Button
    {
        public readonly ButtonClickedEvent onClick = new ButtonClickedEvent();
        public bool interactable = true;
    }
    public sealed class ButtonClickedEvent
    {
        private event Action listeners;
        public void AddListener(Action listener) { listeners += listener; }
        public void RemoveListener(Action listener) { listeners -= listener; }
        public void Invoke() { listeners?.Invoke(); }
    }
}
namespace Game.Inventory
{
    public sealed class InventoryGridView {}
}
public static class EmptyCategoryCheck
{
    private static void Set(object owner, string field, object value)
    {
        owner.GetType().GetField(field,
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance).SetValue(owner, value);
    }
    private static void Call(object owner, string method)
    {
        owner.GetType().GetMethod(method,
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance).Invoke(owner, null);
    }
    public static void Run()
    {
        var panel = new Game.Inventory.InventoryPanelView();
        var buttons = new UnityEngine.UI.Button[5];
        string[] names = { "all", "equipment", "consumable", "pill", "other" };
        for (int i = 0; i < buttons.Length; i++)
        {
            buttons[i] = new UnityEngine.UI.Button();
            Set(panel, names[i] + "Button", buttons[i]);
        }
        int changeCount = 0;
        panel.CategoryChanged += category => changeCount++;
        Call(panel, "OnEnable");
        if (panel.CurrentCategory != Game.Inventory.InventoryItemCategory.All)
            throw new Exception("Bag must start on All.");
        buttons[2].onClick.Invoke();
        if (panel.CurrentCategory != Game.Inventory.InventoryItemCategory.Consumable)
            throw new Exception("Consumables route changed.");
        buttons[3].onClick.Invoke();
        if (panel.CurrentCategory != Game.Inventory.InventoryItemCategory.Pill)
            throw new Exception("Pill button must select an empty category.");
        buttons[4].onClick.Invoke();
        if (panel.CurrentCategory != Game.Inventory.InventoryItemCategory.Other)
            throw new Exception("Other button must select an empty category.");
        buttons[0].onClick.Invoke();
        if (panel.CurrentCategory != Game.Inventory.InventoryItemCategory.All
            || changeCount != 4)
            throw new Exception("Returning to All must trigger the original layout refresh.");
        panel.SetLoading(true);
        foreach (var button in buttons)
            if (button.interactable) throw new Exception("Loading must disable every tab.");
        panel.SetLoading(false);
        foreach (var button in buttons)
            if (!button.interactable) throw new Exception("Tabs must resume after loading.");
        Call(panel, "OnDisable");
        buttons[3].onClick.Invoke();
        if (panel.CurrentCategory != Game.Inventory.InventoryItemCategory.All)
            throw new Exception("Disabled panel must remove tab handlers.");
    }
}
'@
$panelBody = $panel -replace '(?m)^using .*;\r?\n', ''
Add-Type -TypeDefinition ($stubs + "`n" + $enums + "`n" + $panelBody) `
    -IgnoreWarnings -WarningAction SilentlyContinue
[EmptyCategoryCheck]::Run()
Write-Output 'PASS: empty Pill/Other tabs, clear display path, loading and All restoration.'
