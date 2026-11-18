$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Cultivation/Presentation/CultivationMaterialDropTarget.cs') -Raw
$calculator = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Cultivation/Protocol/RefiningPreviewCalculator.cs') -Raw
$meta = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Cultivation/Presentation/CultivationMaterialDropTarget.cs.meta') -Raw
$scene = Get-Content -LiteralPath (Join-Path $root 'Assets/Scenes/WorldShell.unity') -Raw

$stubs = @'
using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Inventory;
using Game.Inventory.Model.V2;
using Game.Inventory.Presentation.V2;
using Game.Items;
using Game.Cultivation.Protocol;
using Game.Cultivation.Runtime;
using Game.UI.Notice;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class DisallowMultipleComponent : Attribute {}
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SerializeField : Attribute {}
    public struct Vector2 { public float x; public float y; }
    public struct Rect { public Vector2 size; }
    public struct Color
    {
        public Color(float r, float g, float b, float a) {}
    }
    public class Component
    {
        public GameObject gameObject = new GameObject();
        public T GetComponent<T>() where T : class => gameObject.GetComponent<T>();
        public T GetComponentInParent<T>(bool includeInactive) where T : class
            => new Canvas() as T;
    }
    public class MonoBehaviour : Component
    {
        protected static T FindObjectOfType<T>() where T : class => null;
        protected static void Destroy(GameObject obj) { obj.SetActive(false); }
    }
    public class Sprite {}
    public class Canvas : Component {}
    public class RectTransform : Component
    {
        public Rect rect = new Rect { size = new Vector2 { x = 120, y = 120 } };
        public Vector2 sizeDelta;
    }
    public class GameObject
    {
        public bool activeSelf;
        public object Component;
        public object ParentComponent;
        public void SetActive(bool active) { activeSelf = active; }
        public T GetComponent<T>() where T : class => Component as T;
        public T GetComponentInParent<T>() where T : class => ParentComponent as T;
    }
}
namespace UnityEngine.EventSystems
{
    public interface IDropHandler { void OnDrop(PointerEventData data); }
    public interface IBeginDragHandler { void OnBeginDrag(PointerEventData data); }
    public interface IDragHandler { void OnDrag(PointerEventData data); }
    public interface IEndDragHandler { void OnEndDrag(PointerEventData data); }
    public class RaycastResult { public UnityEngine.GameObject gameObject; }
    public class PointerEventData
    {
        public enum InputButton { Left, Right }
        public InputButton button;
        public UnityEngine.GameObject pointerDrag;
        public RaycastResult pointerCurrentRaycast = new RaycastResult();
    }
}
namespace UnityEngine.UI
{
    public class Image : UnityEngine.Component
    {
        public UnityEngine.Sprite sprite;
        public bool preserveAspect;
        public UnityEngine.Color color;
        public UnityEngine.RectTransform rectTransform = new UnityEngine.RectTransform();
    }
}
namespace Game.Inventory.Model.V2
{
    public sealed class InventoryStackState
    {
        public long StackId;
        public string Identity;
        public string ItemId;
        public int Count;
    }
    public sealed class InventoryOwnershipSnapshot
    {
        public long CharacterDbId;
        public List<InventoryStackState> Stacks = new List<InventoryStackState>();
    }
}
namespace Game.Inventory.Presentation.V2
{
    public class InventoryBagDragSlot
    {
        public string DragIdentity;
        public string ItemId;
    }
    public static class InventoryDragPreview
    {
        public static bool CreateSucceeds = true;
        public static int MoveCount;
        public static UnityEngine.RectTransform LastPreview;
        public static bool TryCreate(UnityEngine.Component source, string name,
            out UnityEngine.RectTransform preview, out UnityEngine.Canvas canvas)
        {
            preview = null;
            canvas = null;
            if (!CreateSucceeds) return false;
            preview = new UnityEngine.RectTransform();
            preview.gameObject.Component = new UnityEngine.UI.Image();
            LastPreview = preview;
            canvas = new UnityEngine.Canvas();
            return true;
        }
        public static void Move(UnityEngine.RectTransform preview,
            UnityEngine.Canvas canvas, UnityEngine.EventSystems.PointerEventData data)
        {
            if (preview != null && canvas != null && data != null) MoveCount++;
        }
    }
}
namespace Game.Items
{
    public sealed class ItemDefinition {
        public UnityEngine.Sprite Icon;
        public string DisplayName;
    }
    public sealed class ItemDefinitionTable
    {
        public Dictionary<string, ItemDefinition> Items =
            new Dictionary<string, ItemDefinition>();
        public bool TryGet(string id, out ItemDefinition definition)
            => Items.TryGetValue(id, out definition);
    }
}
namespace Game.Inventory
{
    public sealed class InventoryPanelView
    {
        public static InventoryPanelView ActivePanel;
    }
    public sealed class InventoryModuleRuntime
    {
        public Game.Inventory.Model.V2.InventoryOwnershipSnapshot CurrentSnapshot;
        public Game.Items.ItemDefinitionTable ItemDefinitions;
    }
}
public sealed class NetworkManager
{
    public static NetworkManager Instance;
    public event Action<string> OnDisconnected;
    public void Disconnect() => OnDisconnected?.Invoke("test");
}
public sealed class GameSession
{
    public long LocalCharacterDbId;
    public event Action<long, long> LocalWorldEntered;
    public void EnterWorld(long id)
    {
        LocalCharacterDbId = id;
        LocalWorldEntered?.Invoke(1, id);
    }
}
namespace TMPro
{
    public sealed class TMP_FontAsset {}
    public sealed class TMP_Text {
        public string text;
        public TMP_FontAsset font;
        public bool enableAutoSizing;
        public int fontSizeMin;
        public int fontSizeMax;
    }
}
namespace Game.Cultivation.Protocol
{
    public sealed class RefiningRuleDto {
        public string itemId;
        public long secondsPerItem;
        public long cultivationPerItem;
    }
    public sealed class RefiningResultDto {
        public bool success;
        public long configurationRevision;
        public RefiningRuleDto[] rules;
    }
    public sealed class RefiningSnapshotDto {
        public object activeTask;
    }
}
namespace Game.Cultivation.Runtime
{
    public sealed class RefiningSessionRuntime {
        public static RefiningSessionRuntime Instance = new();
        public RefiningResultDto Catalog;
        public Game.Cultivation.Protocol.RefiningSnapshotDto Snapshot;
        public int CatalogRequests;
        public event Action<RefiningResultDto> CatalogChanged;
        public bool RequestCatalog() { CatalogRequests++; return true; }
        public void PublishCatalog(RefiningResultDto result) {
            Catalog = result;
            CatalogChanged?.Invoke(result);
        }
    }
}
namespace Game.UI.Notice
{
    public static class GameNoticeService {
        public static string LastMessage;
        public static bool Show(string message) {
            LastMessage = message; return true;
        }
    }
}
namespace Game.Cultivation.Presentation
{
    public sealed class RefiningQuantityConfirmationView {
        public static RefiningQuantityConfirmationView Last;
        public string Identity;
        public int Maximum;
        public string Error;
        public bool Visible;
        public event Action<string, int> Confirmed;
        public static RefiningQuantityConfirmationView Create(
            UnityEngine.Canvas canvas, TMPro.TMP_FontAsset font) {
            return Last = new RefiningQuantityConfirmationView();
        }
        public void Show(string identity, string name, int count,
            Func<int, bool> canConfirm) {
            Identity = identity; Maximum = count; Visible = true;
        }
        public void Confirm(int count) { Confirmed?.Invoke(Identity, count); }
        public void ShowError(string message) { Error = message; }
        public void Hide() { Visible = false; }
        public void Dispose() { Visible = false; }
    }
}
'@
$source = $source -replace '(?m)^using [^;]+;\r?\n', ''
$calculator = $calculator -replace '(?m)^using [^;]+;\r?\n', ''
Add-Type -TypeDefinition ($stubs + "`n" + $calculator + "`n" + $source) -IgnoreWarnings | Out-Null

function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
}
function Invoke-Lifecycle($target, [string]$name) {
    $target.GetType().GetMethod(
        $name, [System.Reflection.BindingFlags]'Instance,NonPublic'
    ).Invoke($target, @()) | Out-Null
}
function Set-Private($target, [string]$name, $value) {
    $target.GetType().GetField(
        $name, [System.Reflection.BindingFlags]'Instance,NonPublic'
    ).SetValue($target, $value)
}
function SceneBlock([int]$kind, [long]$id) {
    $pattern = "(?s)--- !u!$kind &$id`r?`n(?<body>.*?)(?=`r?`n--- !u!)"
    $matches = [regex]::Matches($scene, $pattern)
    Assert-True ($matches.Count -eq 1) "Scene block $kind/$id must exist once."
    return $matches[0].Groups['body'].Value
}

$session = [GameSession]::new()
$session.LocalCharacterDbId = 42
$inventory = [Game.Inventory.InventoryModuleRuntime]::new()
$inventory.ItemDefinitions = [Game.Items.ItemDefinitionTable]::new()
$inventory.CurrentSnapshot = [Game.Inventory.Model.V2.InventoryOwnershipSnapshot]::new()
$inventory.CurrentSnapshot.CharacterDbId = 42
$stack = [Game.Inventory.Model.V2.InventoryStackState]::new()
$stack.StackId = 17
$stack.Identity = 's:17'
$stack.ItemId = 'ling_shi'
$stack.Count = 3
$inventory.CurrentSnapshot.Stacks.Add($stack)
$sprite = [UnityEngine.Sprite]::new()
$definition = [Game.Items.ItemDefinition]::new()
$definition.Icon = $sprite
$definition.DisplayName = '灵石'
$inventory.ItemDefinitions.Items.Add('ling_shi', $definition)
$icon = [UnityEngine.UI.Image]::new()
$network = [NetworkManager]::new()
[NetworkManager]::Instance = $network
$target = [Game.Cultivation.Presentation.CultivationMaterialDropTarget]::new()
$timeText = [TMPro.TMP_Text]::new()
$cultivationText = [TMPro.TMP_Text]::new()
Set-Private $target 'session' $session
Set-Private $target 'inventory' $inventory
Set-Private $target 'materialIcon' $icon
Set-Private $target 'totalTimeText' $timeText
Set-Private $target 'cultivationPreviewText' $cultivationText
Invoke-Lifecycle $target 'OnEnable'
Assert-True (-not $icon.gameObject.activeSelf) 'The icon starts hidden.'
$refining = [Game.Cultivation.Runtime.RefiningSessionRuntime]::Instance
$rule = [Game.Cultivation.Protocol.RefiningRuleDto]::new()
$rule.itemId = 'ling_shi'
$rule.secondsPerItem = 60
$rule.cultivationPerItem = 10
$catalog = [Game.Cultivation.Protocol.RefiningResultDto]::new()
$catalog.success = $true
$catalog.configurationRevision = 12
$catalog.rules = @($rule)
$refining.PublishCatalog($catalog)

$drag = [Game.Inventory.Presentation.V2.InventoryBagDragSlot]::new()
$eventData = [UnityEngine.EventSystems.PointerEventData]::new()
$eventData.pointerDrag = [UnityEngine.GameObject]::new()
$eventData.pointerDrag.Component = $drag
$eventData.pointerDrag.Component = [object]::new()
$target.OnDrop($eventData)
Assert-True (-not $icon.gameObject.activeSelf) 'Reject a non-inventory drag source.'
$eventData.pointerDrag.Component = $drag
$drag.DragIdentity = 's:wrong'
$drag.ItemId = 'ling_shi'
$target.OnDrop($eventData)
Assert-True (-not $icon.gameObject.activeSelf) 'Reject a missing stack.'
$drag.DragIdentity = 's:17'
$drag.ItemId = 'other'
$target.OnDrop($eventData)
Assert-True (-not $icon.gameObject.activeSelf) 'Reject an item ID mismatch.'
$drag.ItemId = 'ling_shi'
$inventory.CurrentSnapshot.CharacterDbId = 99
$target.OnDrop($eventData)
Assert-True (-not $icon.gameObject.activeSelf) 'Reject another character snapshot.'
$inventory.CurrentSnapshot.CharacterDbId = 42
$stack.Count = 0
$target.OnDrop($eventData)
Assert-True (-not $icon.gameObject.activeSelf) 'Reject an empty stack.'
$stack.Count = 3
$inventory.ItemDefinitions.Items.Remove('ling_shi') | Out-Null
$target.OnDrop($eventData)
Assert-True (-not $icon.gameObject.activeSelf) 'Reject an unregistered item.'
$inventory.ItemDefinitions.Items.Add('ling_shi', $definition)
$definition.Icon = $null
$target.OnDrop($eventData)
Assert-True (-not $icon.gameObject.activeSelf) 'Reject an unregistered icon.'
$definition.Icon = $sprite
$refining.Catalog = $null
$target.OnDrop($eventData)
Assert-True (-not $icon.gameObject.activeSelf -and
    $refining.CatalogRequests -eq 1 -and
    [Game.UI.Notice.GameNoticeService]::LastMessage -eq '炼化目录尚未同步') `
    'A stack must not be selected without a server catalog.'
$refining.PublishCatalog($catalog)
$refining.Snapshot = [Game.Cultivation.Protocol.RefiningSnapshotDto]::new()
$refining.Snapshot.activeTask = [object]::new()
$target.OnDrop($eventData)
Assert-True (-not $icon.gameObject.activeSelf -and
    [Game.UI.Notice.GameNoticeService]::LastMessage -eq '已有炼化任务') `
    'An active server task must reject another local material selection.'
$refining.Snapshot = $null
$target.OnDrop($eventData)
Assert-True (-not $icon.gameObject.activeSelf -and
    [Game.Cultivation.Presentation.RefiningQuantityConfirmationView]::Last.Visible) `
    'Dropping a supported item must ask for quantity before selecting it.'
$quantity = [Game.Cultivation.Presentation.RefiningQuantityConfirmationView]::Last
$quantity.Confirm(0)
Assert-True (-not $icon.gameObject.activeSelf -and $quantity.Error) `
    'Zero cannot be confirmed even if the view callback is invoked.'
$quantity.Confirm(2)
Assert-True ($icon.gameObject.activeSelf -and
    [object]::ReferenceEquals($icon.sprite, $sprite) -and $icon.preserveAspect) `
    'Confirming a valid count must show its item definition icon.'
$selectedId = [long]0
$selectedCount = [int]0
$selectedRevision = [long]0
Assert-True ($target.TryGetSelection([ref]$selectedId,
    [ref]$selectedCount, [ref]$selectedRevision) -and
    $selectedId -eq 17 -and $selectedCount -eq 2 -and
    $selectedRevision -eq 12 -and $timeText.text -eq '00:02:00' -and
    $cultivationText.text -eq '预计 +20' -and -not $quantity.Visible) `
    'Confirmation must retain stackId/count/revision and show local estimates.'

$release = [UnityEngine.EventSystems.PointerEventData]::new()
$bag = [Game.Inventory.InventoryPanelView]::new()
[Game.Inventory.InventoryPanelView]::ActivePanel = $bag
$bagHit = [UnityEngine.GameObject]::new()
$bagHit.ParentComponent = $bag
$release.pointerCurrentRaycast.gameObject = $bagHit
$release.button = [UnityEngine.EventSystems.PointerEventData+InputButton]::Right
$target.OnBeginDrag($release)
$target.OnEndDrag($release)
Assert-True ($icon.gameObject.activeSelf -and
    $null -eq [Game.Inventory.Presentation.V2.InventoryDragPreview]::LastPreview) `
    'Right click cannot drag material out of the slot.'
$release.button = [UnityEngine.EventSystems.PointerEventData+InputButton]::Left
[Game.Inventory.Presentation.V2.InventoryDragPreview]::CreateSucceeds = $false
$target.OnBeginDrag($release)
$target.OnEndDrag($release)
Assert-True ($icon.gameObject.activeSelf) `
    'A missing drag overlay must not clear the material selection.'
[Game.Inventory.Presentation.V2.InventoryDragPreview]::CreateSucceeds = $true
$release.pointerCurrentRaycast.gameObject = $null
$target.OnBeginDrag($release)
$preview = [Game.Inventory.Presentation.V2.InventoryDragPreview]::LastPreview
Assert-True ($preview.sizeDelta.x -eq 120 -and
    [object]::ReferenceEquals($preview.GetComponent[UnityEngine.UI.Image]().sprite, $sprite)) `
    'Dragging out should preview the selected item icon.'
$target.OnDrag($release)
$target.OnEndDrag($release)
Assert-True ($icon.gameObject.activeSelf -and -not $preview.gameObject.activeSelf -and
    [Game.Inventory.Presentation.V2.InventoryDragPreview]::MoveCount -ge 1) `
    'Releasing outside the bag keeps the selection and destroys the preview.'
$otherPanel = [Game.Inventory.InventoryPanelView]::new()
$otherHit = [UnityEngine.GameObject]::new()
$otherHit.ParentComponent = $otherPanel
$release.pointerCurrentRaycast.gameObject = $otherHit
$target.OnBeginDrag($release)
$target.OnEndDrag($release)
Assert-True ($icon.gameObject.activeSelf) `
    'Releasing on another panel cannot clear the selection.'
$release.pointerCurrentRaycast.gameObject = $bagHit
$target.OnBeginDrag($release)
$target.OnEndDrag($release)
Assert-True (-not $icon.gameObject.activeSelf -and $null -eq $icon.sprite -and
    $stack.Count -eq 3 -and $inventory.CurrentSnapshot.Stacks.Count -eq 1) `
    'Returning to the open bag clears only the local selection, not inventory.'
$target.OnBeginDrag($release)
Assert-True (-not $icon.gameObject.activeSelf) `
    'An empty material slot cannot start a new drag.'
$target.OnEndDrag($release)
$target.OnDrop($eventData)
$quantity.Confirm(2)
$target.OnBeginDrag($release)
$activePreview = [Game.Inventory.Presentation.V2.InventoryDragPreview]::LastPreview
Invoke-Lifecycle $target 'OnDisable'
Assert-True (-not $icon.gameObject.activeSelf -and
    -not $activePreview.gameObject.activeSelf) `
    'Closing the panel cancels an in-flight drag and clears the local selection.'
Invoke-Lifecycle $target 'OnEnable'
$inventory.CurrentSnapshot.Stacks.Clear()
Invoke-Lifecycle $target 'Update'
Assert-True (-not $icon.gameObject.activeSelf -and $null -eq $icon.sprite) `
    'Removing the selected stack must clear the local display.'
$inventory.CurrentSnapshot.Stacks.Add($stack)
$target.OnDrop($eventData)
$quantity.Confirm(2)
$network.Disconnect()
Assert-True (-not $icon.gameObject.activeSelf) 'Disconnect must clear selection.'
$target.OnDrop($eventData)
$session.EnterWorld(43)
Assert-True (-not $icon.gameObject.activeSelf) 'Entering a world must clear selection.'
$session.LocalCharacterDbId = 42
$target.OnDrop($eventData)
$quantity.Confirm(2)
Invoke-Lifecycle $target 'OnDisable'
Assert-True (-not $icon.gameObject.activeSelf) 'Closing the panel clears selection.'

Invoke-Lifecycle $target 'OnEnable'
$rule.secondsPerItem = 60
$refining.PublishCatalog($catalog)
$target.OnDrop($eventData)
$quantity.Confirm(2)
$stack.Count = 1
Invoke-Lifecycle $target 'Update'
Assert-True (-not $icon.gameObject.activeSelf -and
    -not $target.TryGetSelection([ref]$selectedId,
        [ref]$selectedCount, [ref]$selectedRevision)) `
    'A reduced stack cannot leave an oversized local selection.'
$stack.Count = 3
$target.OnDrop($eventData)
$catalog.configurationRevision = 13
$refining.PublishCatalog($catalog)
$quantity.Confirm(2)
Assert-True (-not $icon.gameObject.activeSelf -and $quantity.Error) `
    'A catalog changed while the dialog is open must require a new drop.'
$target.OnDrop($eventData)
$quantity.Confirm(2)
$catalog.configurationRevision = 14
$refining.PublishCatalog($catalog)
Assert-True (-not $icon.gameObject.activeSelf) `
    'A changed catalog invalidates any older confirmed selection.'
Invoke-Lifecycle $target 'OnDisable'

$seconds = [long]0
$reward = [long]0
Assert-True ([Game.Cultivation.Protocol.RefiningPreviewCalculator]::TryCalculate(
    $rule, 5, [ref]$seconds, [ref]$reward) -and
    $seconds -eq 300 -and $reward -eq 50) `
    'Batch time and cultivation must multiply the server rule by the chosen count.'
$rule.secondsPerItem = 31536000
Assert-True (-not [Game.Cultivation.Protocol.RefiningPreviewCalculator]::TryCalculate(
    $rule, 2, [ref]$seconds, [ref]$reward)) `
    'A batch exceeding the configured duration limit must be rejected.'
$rule.secondsPerItem = 60
$rule.cultivationPerItem = [long]::MaxValue
Assert-True (-not [Game.Cultivation.Protocol.RefiningPreviewCalculator]::TryCalculate(
    $rule, 2, [ref]$seconds, [ref]$reward)) `
    'Reward overflow must be rejected rather than wrapped.'
Assert-True ([Game.Cultivation.Protocol.RefiningPreviewCalculator]::FormatDuration(
    31536000) -eq '365天 00:00:00') `
    'Long estimates must display days rather than wrapping the hour counter.'

$guid = [regex]::Match($meta, '(?m)^guid: ([0-9a-f]{32})$').Groups[1].Value
Assert-True ($guid -ne '') 'Drop-target script must have a Unity GUID.'
Assert-True ((SceneBlock 1 1322811155) -match 'm_IsActive: 0') `
    'The material icon must be manually disabled in the scene.'
Assert-True ((SceneBlock 114 1322811157) -match 'm_RaycastTarget: 0') `
    'The material icon cannot cover the drop target.'
Assert-True ((SceneBlock 224 904205821) -match
    '- \{fileID: 1887203101\}') 'The drop zone must be a content child.'
Assert-True ((SceneBlock 1 1887203100) -match 'm_IsActive: 1') `
    'The drop zone must remain active when the icon is hidden.'
Assert-True ((SceneBlock 114 1887203102) -match
    '(?s)m_Color: \{r: 1, g: 1, b: 1, a: 0\}.*?m_RaycastTarget: 1') `
    'The transparent drop image must receive UI raycasts.'
Assert-True ((SceneBlock 114 1887203104) -match
    "(?s)guid: $guid.*?session: \{fileID: 2108349142\}.*?" +
    'inventory: \{fileID: 390961493\}.*?' +
    'materialIcon: \{fileID: 1322811157\}.*?' +
    'totalTimeText: \{fileID: 1798083088\}.*?' +
    'cultivationPreviewText: \{fileID: 267538611\}') `
    'The drop target must bind only the existing material and preview UI.'
Assert-True ((SceneBlock 224 1887203101) -match
    '(?s)m_AnchoredPosition: \{x: -147.2, y: 33.1\}.*?' +
    'm_SizeDelta: \{x: 120, y: 120\}') `
    'The drop zone must cover the existing material icon.'
Write-Output 'PASS: cultivation material drag validates ownership and clears presentation.'
