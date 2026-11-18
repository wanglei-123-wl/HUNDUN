$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$scene = Get-Content -LiteralPath (Join-Path $root 'Assets/Scenes/WorldShell.unity') -Raw
$script = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Inventory/Presentation/V2/InventorySelectedItemDetailPanel.cs') -Raw
$grid = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Inventory/Presentation/InventoryGridView.cs') -Raw
$slot = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Inventory/Presentation/InventorySlotView.cs') -Raw
$presenter = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Inventory/Presentation/V2/InventoryBagGridPresenter.cs') -Raw

function Assert-True([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}
function Block([int]$type, [string]$id) {
    $pattern = "(?ms)^--- !u!$type &$id`r?`n(.*?)(?=^--- !u!|\z)"
    $matches = [regex]::Matches($scene, $pattern)
    Assert-True ($matches.Count -eq 1) "Scene block $id must exist exactly once."
    return $matches[0].Value
}

$owner = Block 1 '1536511455'
$component = Block 114 '1536511460'
$panel = Block 1 '814920884'
Assert-True ($owner.Contains('component: {fileID: 1536511460}') -and
    $component.Contains('m_GameObject: {fileID: 1536511455}') -and
    $component.Contains('guid: f2da6f8a7b464ea79c1062fb30278cda')) `
    'Selected detail presenter must belong to the WorldShell bag root.'
foreach ($binding in @(
    'grid: {fileID: 528144968}',
    'detailPanel: {fileID: 814920884}',
    'itemIcon: {fileID: 395722625}',
    'itemName: {fileID: 354103361}',
    'description: {fileID: 663798806}',
    'descriptionScroll: {fileID: 873128656}',
    'attributes: {fileID: 376573277}',
    'attributesScroll: {fileID: 562957741}'
)) {
    Assert-True ($component.Contains($binding)) "Missing detail binding: $binding"
}
Assert-True ($panel.Contains('m_Name: "\u53F3\u4FA7\u8BE6\u60C5\u9762\u677F"') -and
    $scene.Contains('m_Content: {fileID: 1650203250}') -and
    $scene.Contains('m_Content: {fileID: 1519645509}')) `
    'The existing right panel and its scroll content must be retained.'
Assert-True ($slot.Contains('BoundDetail = detail;') -and
    $slot.Contains('BoundDetail = null;') -and
    $grid.Contains('V2SelectionChanged?.Invoke(selectedSlot);') -and
    $grid.Contains('V2SelectionChanged?.Invoke(null);') -and
    $presenter.Contains('grid.RestoreV2Selection();')) `
    'Selected item details must update after catalog binding, refresh, and clear.'

$stubs = @'
using System;
using System.Collections.Generic;
namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Class)] public sealed class DisallowMultipleComponent : Attribute {}
    [AttributeUsage(AttributeTargets.Field)] public sealed class SerializeField : Attribute {}
    public class MonoBehaviour {}
    public class GameObject { public bool activeSelf; public void SetActive(bool active) { activeSelf = active; } }
    public class Sprite {}
    public class Transform {}
    public struct Rect { public float height; public Rect(float value) { height = value; } }
    public class RectTransform : Transform
    {
        public enum Axis { Horizontal, Vertical }
        public Rect rect = new Rect(80);
        public Vector2 anchoredPosition;
        public float height;
        public void SetSizeWithCurrentAnchors(Axis axis, float size) { height = size; }
    }
    public struct Vector2 { public float y; }
    public static class Mathf
    {
        public static float Max(float a, float b) { return Math.Max(a, b); }
        public static float Ceil(float a) { return (float)Math.Ceiling(a); }
    }
    public static class Canvas { public static void ForceUpdateCanvases() {} }
}
namespace UnityEngine.UI
{
    public class Button
    {
        public UnityEngine.GameObject gameObject = new UnityEngine.GameObject();
        public ButtonClickedEvent onClick = new ButtonClickedEvent();
        public class ButtonClickedEvent
        {
            private event Action clicked;
            public void AddListener(Action listener) { clicked += listener; }
            public void RemoveListener(Action listener) { clicked -= listener; }
            public void Invoke() { clicked?.Invoke(); }
        }
    }
    public class Image
    {
        public UnityEngine.Sprite sprite;
        public UnityEngine.GameObject gameObject = new UnityEngine.GameObject();
    }
    public class ScrollRect
    {
        public UnityEngine.RectTransform content = new UnityEngine.RectTransform();
        public UnityEngine.RectTransform viewport = new UnityEngine.RectTransform();
        public UnityEngine.Transform transform = new UnityEngine.RectTransform();
        public float verticalNormalizedPosition;
        public void StopMovement() {}
    }
}
namespace TMPro
{
    public class TMP_Text
    {
        public string text;
        public float preferredHeight = 125;
        public UnityEngine.RectTransform rectTransform = new UnityEngine.RectTransform();
    }
}
namespace Game.Items
{
    public class ItemDetailEntry
    {
        public string Label;
        public string Value;
        public bool IsValid { get { return !string.IsNullOrWhiteSpace(Label) || !string.IsNullOrWhiteSpace(Value); } }
    }
    public class ItemDetailData
    {
        public UnityEngine.Sprite Icon;
        public string DisplayName, Description, DetailText;
        public List<ItemDetailEntry> Entries = new List<ItemDetailEntry>();
    }
}
namespace Game.Inventory
{
    public class InventorySlotView
    {
        public string V2Identity;
        public Game.Items.ItemDetailData BoundDetail;
    }
    public class InventoryGridView
    {
        public event Action<InventorySlotView> V2SelectionChanged;
        public InventorySlotView SelectedV2Slot;
        public void Select(InventorySlotView slot)
        {
            SelectedV2Slot = slot;
            V2SelectionChanged?.Invoke(slot);
        }
        public void ClearSelection() { Select(null); }
    }
}
public static class SelectedDetailStage2Check
{
    static void Require(bool value, string message) { if (!value) throw new Exception(message); }
    static void Set(object owner, string name, object value)
    {
        owner.GetType().GetField(name,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(owner, value);
    }
    static void Call(object owner, string name)
    {
        owner.GetType().GetMethod(name,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(owner, null);
    }
    public static void Run()
    {
        var view = new Game.Inventory.Presentation.V2.InventorySelectedItemDetailPanel();
        var grid = new Game.Inventory.InventoryGridView();
        var panel = new UnityEngine.GameObject();
        var icon = new UnityEngine.UI.Image();
        var name = new TMPro.TMP_Text();
        var description = new TMPro.TMP_Text();
        var attributes = new TMPro.TMP_Text();
        var descriptionScroll = new UnityEngine.UI.ScrollRect();
        var attributesScroll = new UnityEngine.UI.ScrollRect();
        var learn = new UnityEngine.UI.Button();
        var use = new UnityEngine.UI.Button();
        var sell = new UnityEngine.UI.Button();
        string learned = null, used = null, sold = null;
        view.LearnRequested += identity => learned = identity;
        view.UseRequested += identity => used = identity;
        view.SellRequested += identity => sold = identity;
        Set(view, "grid", grid);
        Set(view, "detailPanel", panel);
        Set(view, "itemIcon", icon);
        Set(view, "itemName", name);
        Set(view, "description", description);
        Set(view, "descriptionScroll", descriptionScroll);
        Set(view, "attributes", attributes);
        Set(view, "attributesScroll", attributesScroll);
        Set(view, "learnButton", learn);
        Set(view, "useButton", use);
        Set(view, "sellButton", sell);
        Call(view, "OnEnable");
        Require(!panel.activeSelf, "Enabling without a selection must not show the manually hidden panel.");
        learn.onClick.Invoke();
        use.onClick.Invoke();
        sell.onClick.Invoke();
        Require(learned == null && used == null && sold == null,
            "Buttons without selection must do nothing.");
        var sprite = new UnityEngine.Sprite();
        var detail = new Game.Items.ItemDetailData {
            Icon = sprite, DisplayName = "Sword", Description = "Intro", DetailText = "Power"
        };
        detail.Entries.Add(new Game.Items.ItemDetailEntry { Label = "Attack", Value = "12" });
        grid.Select(new Game.Inventory.InventorySlotView { V2Identity = "e:1", BoundDetail = detail });
        Require(panel.activeSelf && icon.sprite == sprite && icon.gameObject.activeSelf,
            "Selection must show the catalog icon and panel.");
        Require(name.text == "Sword" && description.text == "Intro" &&
            attributes.text.Replace("\r\n", "\n") == "Power\nAttack：12",
            "Catalog fields must populate the correct areas.");
        Require(descriptionScroll.verticalNormalizedPosition == 1f &&
            descriptionScroll.content.height >= 125f &&
            attributesScroll.verticalNormalizedPosition == 1f, "Scroll areas must reset to the top.");
        view.SetAttributesResolver(identity => identity == "e:1" ? "Attack：+27" : string.Empty);
        grid.Select(new Game.Inventory.InventorySlotView { V2Identity = "e:1", BoundDetail = detail });
        Require(attributes.text == "Attack：+27" && name.text == "Sword" && description.text == "Intro"
            && icon.sprite == sprite, "The server data resolver must replace only the base attributes.");
        grid.Select(new Game.Inventory.InventorySlotView { V2Identity = "e:2", BoundDetail = detail });
        Require(attributes.text == string.Empty, "An empty catalog value must clear the previous attributes.");
        grid.Select(new Game.Inventory.InventorySlotView { V2Identity = "e:1", BoundDetail = detail });
        learn.onClick.Invoke();
        Require(learned == "e:1" && used == null, "Learn click must request the selected identity only.");
        use.onClick.Invoke();
        Require(used == "e:1", "Use click must request the selected identity.");
        sell.onClick.Invoke();
        Require(sold == "e:1", "Sell click must request the selected identity.");
        grid.Select(new Game.Inventory.InventorySlotView {
            V2Identity = "s:2", BoundDetail = new Game.Items.ItemDetailData { DisplayName = "Potion" }
        });
        sell.onClick.Invoke();
        Require(sold == "s:2", "Sell click must use the latest selected identity.");
        Require(panel.activeSelf && name.text == "Potion" && icon.sprite == null &&
            !icon.gameObject.activeSelf && attributes.text == "", "Next selection must replace old fields.");
        grid.ClearSelection();
        Require(!panel.activeSelf && name.text == "" && description.text == "",
            "Clearing selection must hide and clear the panel.");
        learn.onClick.Invoke();
        use.onClick.Invoke();
        sell.onClick.Invoke();
        Require(learned == "e:1" && used == "e:1" && sold == "s:2",
            "Cleared selection must not request an action.");
        Call(view, "OnDisable");
        grid.Select(new Game.Inventory.InventorySlotView { V2Identity = "e:1", BoundDetail = detail });
        learn.onClick.Invoke();
        use.onClick.Invoke();
        sell.onClick.Invoke();
        Require(!panel.activeSelf, "Closed bag must not receive selection events.");
        Require(learned == "e:1" && used == "e:1" && sold == "s:2",
            "Closed bag must remove button listeners.");
    }
}
'@
$stubBodies = $stubs -replace '(?m)^using (System|System.Collections.Generic);\r?\n', ''
$source = "using System;`nusing System.Collections.Generic;`n" +
    ($script -replace '(?m)^namespace Game.Inventory.Presentation.V2',
        ($stubBodies + "`nnamespace Game.Inventory.Presentation.V2"))
Add-Type -TypeDefinition $source -IgnoreWarnings -WarningAction SilentlyContinue
[SelectedDetailStage2Check]::Run()
Write-Output 'PASS: WorldShell detail bindings and V2 selection, catalog text, scrolling, clearing.'
