$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$scene = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scenes/WorldShell.unity') -Raw
$source = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Inventory/Presentation/InventoryCategorySelectionView.cs') -Raw
$meta = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Inventory/Presentation/InventoryCategorySelectionView.cs.meta') -Raw

function Assert-True([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}

function Block([int]$type, [string]$id) {
    $matches = [regex]::Matches(
        $scene, "(?ms)^--- !u!$type &$id`r?`n.*?(?=^--- !u!|\z)")
    Assert-True ($matches.Count -eq 1) "Scene block $id must exist once."
    return $matches[0].Value
}

$navigation = Block 1 '561655418'
$component = Block 114 '9000000000071'
Assert-True ($navigation.Contains('component: {fileID: 9000000000071}') -and
    $component.Contains('m_GameObject: {fileID: 561655418}') -and
    $component.Contains('guid: a83c7b5a9afc4c3bb48616d7824ee102') -and
    $meta.Contains('guid: a83c7b5a9afc4c3bb48616d7824ee102')) `
    'Selection component must belong to the WorldShell bag navigation.'

$bindings = @(
    @('all', '1010781138', '1295250080', '1295250081', '1010781137', '1'),
    @('equipment', '2135118446', '51825275', '51825276', '2135118445', '0'),
    @('consumable', '40206249', '1265239387', '1265239388', '40206248', '0'),
    @('pill', '1568040119', '1849303679', '1849303680', '1568040118', '0'),
    @('other', '299520029', '13685228', '13685229', '299520028', '0')
)
foreach ($binding in $bindings) {
    $name, $button, $effect, $rect, $parent, $active = $binding
    Assert-True ($component.Contains("${name}Button: {fileID: $button}") -and
        $component.Contains("${name}Selection: {fileID: $effect}") -and
        (Block 224 $rect).Contains("m_Father: {fileID: $parent}") -and
        (Block 1 $effect).Contains("m_IsActive: $active")) `
        "$name effect must be under its button and have the correct default visibility."
}
Assert-True ($component.Contains('panel: {fileID: 1536511459}')) `
    'Navigation must synchronize with the existing bag panel.'
$panelComponent = Block 114 '1536511459'
Assert-True ($panelComponent.Contains('pillButton: {fileID: 1568040119}') -and
    $panelComponent.Contains('otherButton: {fileID: 299520029}')) `
    'Empty categories must be connected to the existing bag category controller.'
Assert-True ($component.Contains('bagScroll: {fileID: 1782663212}') -and
    (Block 114 '1782663212').Contains(
        'm_Content: {fileID: 528144965}')) `
    'Navigation must reset the WorldShell bag scroll view.'

$stubs = @'
using System;
using UnityEngine;
using UnityEngine.UI;
namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class DisallowMultipleComponent : Attribute {}
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class AddComponentMenu : Attribute
    {
        public AddComponentMenu(string path) {}
    }
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SerializeField : Attribute {}
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class InspectorName : Attribute
    {
        public InspectorName(string name) {}
    }
    public class MonoBehaviour {}
    public static class Canvas
    {
        public static int Updates;
        public static void ForceUpdateCanvases() { Updates++; }
    }
    public class GameObject
    {
        public bool activeSelf;
        public void SetActive(bool active) { activeSelf = active; }
    }
}
namespace UnityEngine.UI
{
    public sealed class ScrollRect
    {
        public float verticalNormalizedPosition;
        public int StopCount;
        public void StopMovement() { StopCount++; }
    }
    public sealed class Button
    {
        public readonly ButtonClickedEvent onClick = new ButtonClickedEvent();
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
    public enum InventoryItemCategory { All, Equipment, Consumable, Pill, Other }
    public sealed class InventoryPanelView
    {
        public InventoryItemCategory CurrentCategory { get; private set; }
        public event Action<InventoryItemCategory> CategoryChanged;
        public void SetCategory(InventoryItemCategory category)
        {
            CurrentCategory = category;
            CategoryChanged?.Invoke(category);
        }
    }
}
public static class SelectionCheck
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
    private static void AssertSelected(UnityEngine.GameObject[] effects, int index)
    {
        for (int i = 0; i < effects.Length; i++)
            if (effects[i].activeSelf != (i == index))
                throw new Exception("Selection mismatch at " + i + ", expected " + index);
    }
    public static void Run()
    {
        var view = new Game.Inventory.InventoryCategorySelectionView();
        var panel = new Game.Inventory.InventoryPanelView();
        var scroll = new UnityEngine.UI.ScrollRect();
        var buttons = new UnityEngine.UI.Button[5];
        var effects = new UnityEngine.GameObject[5];
        string[] names = { "all", "equipment", "consumable", "pill", "other" };
        Set(view, "panel", panel);
        Set(view, "bagScroll", scroll);
        for (int i = 0; i < names.Length; i++)
        {
            buttons[i] = new UnityEngine.UI.Button();
            effects[i] = new UnityEngine.GameObject { activeSelf = true };
            Set(view, names[i] + "Button", buttons[i]);
            Set(view, names[i] + "Selection", effects[i]);
        }
        Call(view, "OnEnable");
        AssertSelected(effects, 0);
        for (int i = 0; i < buttons.Length; i++)
        {
            scroll.verticalNormalizedPosition = 0.3f;
            buttons[i].onClick.Invoke();
            AssertSelected(effects, i);
            if (scroll.verticalNormalizedPosition != 0.3f)
                throw new Exception("Scroll must wait until the tab redraw finishes.");
            Call(view, "LateUpdate");
            if (scroll.verticalNormalizedPosition != 1f
                || scroll.StopCount != i + 1)
                throw new Exception("Tab click must stop inertia and scroll to top.");
        }
        panel.SetCategory(Game.Inventory.InventoryItemCategory.Equipment);
        AssertSelected(effects, 1);
        panel.SetCategory(Game.Inventory.InventoryItemCategory.Pill);
        AssertSelected(effects, 3);
        panel.SetCategory(Game.Inventory.InventoryItemCategory.Other);
        AssertSelected(effects, 4);
        panel.SetCategory(Game.Inventory.InventoryItemCategory.Equipment);
        AssertSelected(effects, 1);
        Call(view, "OnDisable");
        buttons[4].onClick.Invoke();
        panel.SetCategory(Game.Inventory.InventoryItemCategory.Consumable);
        AssertSelected(effects, 1);
        Call(view, "LateUpdate");
        if (scroll.StopCount != 5)
            throw new Exception("Disabled navigation must not reset scroll.");
        Call(view, "OnEnable");
        AssertSelected(effects, 2);
        buttons[0].onClick.Invoke();
        AssertSelected(effects, 0);
        Call(view, "LateUpdate");
        if (scroll.StopCount != 6 || scroll.verticalNormalizedPosition != 1f)
            throw new Exception("Reopened navigation must reset scroll once.");
    }
}
'@
$sourceBody = $source -replace '(?m)^using .*;\r?\n', ''
Add-Type -TypeDefinition ($stubs + "`n" + $sourceBody) `
    -IgnoreWarnings -WarningAction SilentlyContinue
[SelectionCheck]::Run()
Write-Output 'PASS: WorldShell navigation wiring, default state, exclusive selection and lifecycle.'
