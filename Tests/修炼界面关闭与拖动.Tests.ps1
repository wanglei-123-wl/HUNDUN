$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Cultivation/Presentation/修炼界面拖动区域.cs') -Raw
$sharedDragSource = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/UI/WindowDragHandle.cs') -Raw -Encoding UTF8
$meta = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Cultivation/Presentation/修炼界面拖动区域.cs.meta') -Raw
$toggleSource = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Cultivation/Presentation/CultivationWindowToggle.cs') -Raw
$scene = Get-Content -LiteralPath (Join-Path $root 'Assets/Scenes/WorldShell.unity') -Raw

function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
}
function Block([int]$type, [string]$id) {
    $match = [regex]::Match(
        $scene, "(?ms)^--- !u!$type &$id`r?`n.*?(?=^--- !u!|\z)")
    Assert-True $match.Success "Missing scene block $id."
    return $match.Value
}

$guid = ([regex]::Match($meta, 'guid: ([0-9a-f]{32})')).Groups[1].Value
$handle = Block 1 '359960275'
$dragComponent = Block 114 '860000000000400020'
$parent = Block 224 '112115725'
$parts = @('1581756385', '904205821', '1266172303',
    '359960276', '1625952859', '1548782251', '1012737364', '605858914')
Assert-True ($guid -ne '' -and
    $handle.Contains('component: {fileID: 860000000000400020}') -and
    $dragComponent.Contains("guid: $guid") -and
    (Block 114 '359960277').Contains('m_RaycastTarget: 1') -and
    (Block 222 '359960278').Contains('m_CullTransparentMesh: 0')) `
    'Cultivation drag area must receive pointer events.'
foreach ($id in $parts) {
    Assert-True ((Block 224 $id).Contains('m_Father: {fileID: 112115725}') -and
        $parent.Contains("- {fileID: $id}")) `
        "Cultivation part $id must move with the drag area."
}
Assert-True ($dragComponent.Contains('panelRoot: {fileID: 112115725}') -and
    -not $dragComponent.Contains('movableParts:')) `
    'Drag must bind the cultivation root and discover all current children.'
$controller = Block 114 '860000000000500005'
Assert-True ($controller.Contains('cultivationWindow: {fileID: 112115721}') -and
    $controller.Contains('hudToggleButton: {fileID: 489239712}') -and
    $controller.Contains('closeButton: {fileID: 1625952860}') -and
    (Block 114 '1625952860').Contains('m_GameObject: {fileID: 1625952858}')) `
    'Cultivation close button must be wired to the rooted window controller.'

$stubs = @'
public sealed class SkillLearnResult
{
    public bool success;
    public string errorCode;
    public SkillSnapshotEntry learnedSkill;
}
public sealed class SkillSnapshotEntry { public string skillId; }
public sealed class GameSession { public event System.Action<SkillLearnResult> SkillLearnCompleted; }
public sealed class LearnedSkillListView { public void SelectSkill(string skillId) {} }
namespace Game.Inventory
{
    public sealed class InventoryModuleRuntime { public void Close() {} }
}
namespace Game.Cultivation.Presentation
{
    public sealed class CultivationPanelToggle { public void OpenSkills() {} }
}
namespace Game.UI.Notice
{
    public static class GameNoticeService { public static void Show(string value) {} }
}
namespace Game.Cultivation.Runtime
{
    public sealed class RefiningSessionRuntime
    {
        public static RefiningSessionRuntime Instance = new RefiningSessionRuntime();
        public int refreshes;
        public void Refresh() { refreshes++; }
    }
}
namespace UnityEngine
{
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public sealed class DisallowMultipleComponent : System.Attribute {}
    [System.AttributeUsage(System.AttributeTargets.Field)]
    public sealed class SerializeField : System.Attribute {}
    [System.AttributeUsage(System.AttributeTargets.Field)]
    public sealed class InspectorNameAttribute : System.Attribute
    { public InspectorNameAttribute(string name) {} }
    public class GameObject
    {
        public bool activeSelf;
        public Transform transform;
        public object component;
        public GameObject() { transform = new Transform { gameObject = this }; }
        public T GetComponentInParent<T>() where T : class
        {
            for (Transform node = transform; node != null; node = node.parent)
                if (node.gameObject?.component is T value) return value;
            return null;
        }
        public int setActiveCalls;
        public void SetActive(bool value) { activeSelf = value; setActiveCalls++; }
    }
    public class MonoBehaviour
    {
        public GameObject gameObject = new GameObject();
        public Transform transform {
            get => gameObject.transform;
            set { gameObject.transform = value; value.gameObject = gameObject; }
        }
    }
    public class Transform {
        public GameObject gameObject;
        private Transform parentValue;
        private readonly System.Collections.Generic.List<Transform> children = new();
        public Transform parent {
            get => parentValue;
            set { parentValue?.children.Remove(this); parentValue = value; value?.children.Add(this); }
        }
        public T GetComponent<T>() where T : class => gameObject?.component as T;
        public int childCount => children.Count;
        public Transform GetChild(int index) => children[index];
        public bool IsChildOf(Transform target) {
            for (Transform node = this; node != null; node = node.parent)
                if (node == target) return true;
            return false;
        }
    }
    public class RectTransform : Transform
    {
        public Vector2 anchoredPosition { get; set; }
    }
    public enum RenderMode { ScreenSpaceOverlay, ScreenSpaceCamera, WorldSpace }
    public class Canvas { public bool isRootCanvas = true; public RenderMode renderMode = RenderMode.ScreenSpaceOverlay; }
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 operator +(Vector2 a, Vector2 b) =>
            new Vector2(a.x + b.x, a.y + b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) =>
            new Vector2(a.x - b.x, a.y - b.y);
    }
    public static class RectTransformUtility
    {
        public static bool ScreenPointToLocalPointInRectangle(
            RectTransform rect, Vector2 screen, object camera, out Vector2 local)
        {
            local = screen;
            return rect != null;
        }
    }
    public enum KeyCode { C }
    public static class Input
    {
        public static bool pressed;
        public static bool GetKeyDown(KeyCode key) => pressed && key == KeyCode.C;
    }
}
namespace UnityEngine.UI
{
    public class Selectable {}
    public sealed class Button : Selectable { public readonly ClickEvent onClick = new ClickEvent(); }
    public sealed class ClickEvent
    {
        private event System.Action listeners;
        public void AddListener(System.Action callback) { listeners += callback; }
        public void RemoveListener(System.Action callback) { listeners -= callback; }
        public void Invoke() { listeners?.Invoke(); }
    }
}
namespace UnityEngine.EventSystems
{
    public interface IBeginDragHandler { void OnBeginDrag(PointerEventData data); }
    public interface IDragHandler { void OnDrag(PointerEventData data); }
    public interface IEndDragHandler { void OnEndDrag(PointerEventData data); }
    public struct RaycastResult { public UnityEngine.GameObject gameObject; }
    public class PointerEventData
    {
        public enum InputButton { Left, Right, Middle }
        public InputButton button;
        public RaycastResult pointerPressRaycast;
        public UnityEngine.Vector2 position;
        public object pressEventCamera;
    }
}
public static class GameplayUiInputGuard { public static bool IsEditingText; public static bool BlocksWindowShortcuts => IsEditingText; }
'@
$sharedDragBody = $sharedDragSource -replace '(?m)^using [^\r\n]+;\r?\n', ''
$body = $source -replace '(?m)^using [^\r\n]+;\r?\n', ''
$toggleBody = $toggleSource -replace '(?m)^using [^\r\n]+;\r?\n', ''
Add-Type -TypeDefinition ("using UnityEngine;`nusing UnityEngine.EventSystems;`n" +
    "using UnityEngine.UI;`nusing Game.UI;`nusing Game.Cultivation.Runtime;`nusing Game.Inventory;`nusing Game.UI.Notice;`n" +
    $stubs + "`n" + $sharedDragBody + "`n" + $body + "`n" + $toggleBody) -IgnoreWarnings | Out-Null

function Pointer($pressed, [int]$x, [int]$y, $button) {
    $data = [UnityEngine.EventSystems.PointerEventData]::new()
    $data.button = $button
    $data.position = [UnityEngine.Vector2]::new($x, $y)
    $hit = [UnityEngine.EventSystems.RaycastResult]::new()
    $hit.gameObject = $pressed
    $data.pointerPressRaycast = $hit
    return $data
}
function Assert-Positions($items, [int]$shiftX, [int]$shiftY) {
    for ($i = 0; $i -lt $items.Length; $i++) {
        Assert-True ($items[$i].anchoredPosition.x -eq 10 * $i + $shiftX -and
            $items[$i].anchoredPosition.y -eq 20 * $i + $shiftY) `
            "Cultivation part $i moved incorrectly."
    }
}

$left = [UnityEngine.EventSystems.PointerEventData+InputButton]::Left
$right = [UnityEngine.EventSystems.PointerEventData+InputButton]::Right
$canvas = [UnityEngine.RectTransform]::new()
$canvasObject = [UnityEngine.GameObject]::new()
$canvasObject.transform = $canvas
$canvas.gameObject = $canvasObject
$canvasObject.component = [UnityEngine.Canvas]::new()
$handleObject = [UnityEngine.GameObject]::new()
$other = [UnityEngine.GameObject]::new()
$drag = [Game.Cultivation.Presentation.修炼界面拖动区域]::new()
$drag.gameObject = $handleObject
$drag.transform = [UnityEngine.RectTransform]::new()
$drag.transform.parent = $canvas
$items = @()
for ($i = 0; $i -lt 5; $i++) {
    $item = [UnityEngine.RectTransform]::new()
    $item.parent = $canvas
    $item.anchoredPosition = [UnityEngine.Vector2]::new(10 * $i, 20 * $i)
    $items += $item
}
$flags = [System.Reflection.BindingFlags]'Instance,NonPublic'
$drag.GetType().GetField('panelRoot', $flags).SetValue($drag, $canvas)
$drag.OnBeginDrag((Pointer $other 5 8 $left))
$drag.OnDrag((Pointer $other 13 20 $left))
Assert-Positions $items 0 0
$drag.OnBeginDrag((Pointer $handleObject 5 8 $right))
$drag.OnDrag((Pointer $handleObject 13 20 $right))
Assert-Positions $items 0 0
$drag.OnBeginDrag((Pointer $handleObject 5 8 $left))
$drag.OnDrag((Pointer $handleObject 13 20 $left))
Assert-Positions $items 8 12
$drag.OnEndDrag((Pointer $handleObject 13 20 $left))
$drag.OnDrag((Pointer $handleObject 20 30 $left))
Assert-Positions $items 8 12

# A newly added direct child must join the next drag without editing any list.
$newPart = [UnityEngine.RectTransform]::new()
$newPart.parent = $canvas
$newPart.anchoredPosition = [UnityEngine.Vector2]::new(100, 200)
$label = [UnityEngine.GameObject]::new()
$label.transform.parent = $drag.transform
$drag.OnBeginDrag((Pointer $label 0 0 $left))
$drag.OnDrag((Pointer $label 4 6 $left))
Assert-Positions $items 12 18
Assert-True ($newPart.anchoredPosition.x -eq 104 -and $newPart.anchoredPosition.y -eq 206) `
    'New window children and drag-handle labels must move with the panel.'
$drag.GetType().GetMethod('OnDisable', $flags).Invoke($drag, @()) | Out-Null
$drag.OnDrag((Pointer $label 30 40 $left))
Assert-Positions $items 12 18

$buttonInsideHandle = [UnityEngine.GameObject]::new()
$buttonInsideHandle.transform.parent = $drag.transform
$buttonInsideHandle.component = [UnityEngine.UI.Button]::new()
$drag.OnBeginDrag((Pointer $buttonInsideHandle 0 0 $left))
$drag.OnDrag((Pointer $buttonInsideHandle 15 20 $left))
Assert-Positions $items 12 18
$drag.GetType().GetField('panelRoot', $flags).SetValue($drag, [UnityEngine.RectTransform]::new())
$drag.OnBeginDrag((Pointer $handleObject 0 0 $left))
$drag.OnDrag((Pointer $handleObject 15 20 $left))
Assert-Positions $items 12 18
$drag.GetType().GetField('panelRoot', $flags).SetValue($drag, $null)
$drag.OnBeginDrag((Pointer $handleObject 0 0 $left))
$drag.OnDrag((Pointer $handleObject 2 3 $left))
Assert-Positions $items 14 21
$drag.OnEndDrag((Pointer $handleObject 2 3 $left))

$toggle = [Game.Cultivation.Presentation.CultivationWindowToggle]::new()
$window = [UnityEngine.GameObject]::new()
$hud = [UnityEngine.UI.Button]::new()
$close = [UnityEngine.UI.Button]::new()
$toggle.GetType().GetField('cultivationWindow', $flags).SetValue($toggle, $window)
$toggle.GetType().GetField('hudToggleButton', $flags).SetValue($toggle, $hud)
$toggle.GetType().GetField('closeButton', $flags).SetValue($toggle, $close)
$toggle.GetType().GetMethod('OnEnable', $flags).Invoke($toggle, @()) | Out-Null
$hud.onClick.Invoke()
Assert-True ($window.activeSelf -and
    [Game.Cultivation.Runtime.RefiningSessionRuntime]::Instance.refreshes -eq 1) `
    'The HUD button must still open and refresh the cultivation window.'
$close.onClick.Invoke()
Assert-True (-not $window.activeSelf -and $window.setActiveCalls -eq 2) `
    'The close button must disable the outer window.'
$close.onClick.Invoke()
Assert-True ($window.setActiveCalls -eq 2) `
    'Closing an already hidden window must do nothing.'
[UnityEngine.Input]::pressed = $true
$toggle.GetType().GetMethod('Update', $flags).Invoke($toggle, @()) | Out-Null
Assert-True ($window.activeSelf -and
    [Game.Cultivation.Runtime.RefiningSessionRuntime]::Instance.refreshes -eq 2) `
    'C must still reopen and refresh the cultivation window.'
$toggle.GetType().GetMethod('OnDisable', $flags).Invoke($toggle, @()) | Out-Null
$close.onClick.Invoke()
Assert-True ($window.activeSelf -and $window.setActiveCalls -eq 3) `
    'Disabling the host must remove the close listener.'

Write-Output 'PASS: WorldShell cultivation close button and full-panel left drag.'
