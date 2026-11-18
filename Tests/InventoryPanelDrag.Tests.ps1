$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Inventory/Presentation/InventoryPanelDragHandle.cs') -Raw
$meta = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Inventory/Presentation/InventoryPanelDragHandle.cs.meta') -Raw
$scene = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scenes/WorldShell.unity') -Raw

function Assert-True([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}
function Block([int]$type, [string]$id) {
    $matches = [regex]::Matches(
        $scene, "(?ms)^--- !u!$type &$id`r?`n.*?(?=^--- !u!|\z)")
    Assert-True ($matches.Count -eq 1) "Scene block $id must exist once."
    return $matches[0].Value
}

Assert-True ($meta.Contains('guid: f1a08c236b234d1fbfc7cb3c6bce783d')) `
    'Inventory drag script GUID is missing.'
Assert-True ((Block 1 '1267418144').Contains(
    'component: {fileID: 860000000000400017}')) `
    'Drag component must be on the bag drag area.'
$component = Block 114 '860000000000400017'
Assert-True ($component.Contains('m_GameObject: {fileID: 1267418144}') -and
    $component.Contains(
        'guid: f1a08c236b234d1fbfc7cb3c6bce783d') -and
    $component.Contains('panel: {fileID: 1536511456}')) `
    'Drag area must target the WorldShell bag panel.'
Assert-True ((Block 224 '1267418145').Contains(
    'm_Father: {fileID: 1536511456}') -and
    (Block 114 '1267418146').Contains('m_RaycastTarget: 1') -and
    (Block 222 '1267418147').Contains('m_CullTransparentMesh: 0')) `
    'The transparent drag area must receive pointer events.'
Assert-True ((Block 224 '1536511456').Contains(
    '- {fileID: 1267418145}')) `
    'The drag area must be a child of the bag panel.'
$children = @(
    @('495464692', '495464693', '495464694', '495464695'),
    @('1995442747', '1995442748', '1995442749', '1995442750'),
    @('1493676259', '1493676260', '1493676261', '1493676262')
)
$parentRect = Block 224 '1267418145'
foreach ($child in $children) {
    $object, $rect, $image, $renderer = $child
    Assert-True ($parentRect.Contains("- {fileID: $rect}") -and
        (Block 224 $rect).Contains(
            'm_Father: {fileID: 1267418145}') -and
        (Block 1 $object).Contains("component: {fileID: $image}") -and
        (Block 114 $image).Contains('m_RaycastTarget: 1') -and
        (Block 222 $renderer).Contains('m_CullTransparentMesh: 0') -and
        $component.Contains("- {fileID: $rect}")) `
        "Child drag area $object must raycast and be bound to the parent handle."
}

$stubs = @'
namespace UnityEngine
{
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public sealed class DisallowMultipleComponent : System.Attribute {}
    [System.AttributeUsage(System.AttributeTargets.Field)]
    public sealed class SerializeField : System.Attribute {}
    public class MonoBehaviour
    {
        public GameObject gameObject { get; set; }
    }
    public class GameObject
    {
        public bool activeInHierarchy { get; set; } = true;
    }
    public class Transform { public Transform parent { get; set; } }
    public class RectTransform : Transform
    {
        public GameObject gameObject { get; set; }
        public Vector2 anchoredPosition { get; set; }
    }
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
'@
Add-Type -TypeDefinition ($source + "`n" + $stubs) -IgnoreWarnings | Out-Null

function Pointer($pressed, [int]$x, [int]$y, $button) {
    $data = [UnityEngine.EventSystems.PointerEventData]::new()
    $data.button = $button
    $data.position = [UnityEngine.Vector2]::new($x, $y)
    $hit = [UnityEngine.EventSystems.RaycastResult]::new()
    $hit.gameObject = $pressed
    $data.pointerPressRaycast = $hit
    $data
}
function Assert-Position($panel, [int]$x, [int]$y, [string]$message) {
    Assert-True ($panel.anchoredPosition.x -eq $x -and
        $panel.anchoredPosition.y -eq $y) $message
}

$left = [UnityEngine.EventSystems.PointerEventData+InputButton]::Left
$right = [UnityEngine.EventSystems.PointerEventData+InputButton]::Right
$handle = [UnityEngine.GameObject]::new()
$childHandles = @(
    [UnityEngine.RectTransform]::new(),
    [UnityEngine.RectTransform]::new(),
    [UnityEngine.RectTransform]::new()
)
foreach ($child in $childHandles) {
    $child.gameObject = [UnityEngine.GameObject]::new()
}
$slot = [UnityEngine.GameObject]::new()
$parent = [UnityEngine.RectTransform]::new()
$panel = [UnityEngine.RectTransform]::new()
$panel.parent = $parent
$panel.gameObject = [UnityEngine.GameObject]::new()
$panel.anchoredPosition = [UnityEngine.Vector2]::new(10, 20)
$drag = [Game.Inventory.InventoryPanelDragHandle]::new()
$drag.gameObject = $handle
$fields = [System.Reflection.BindingFlags]'Instance,NonPublic'
$drag.GetType().GetField('panel', $fields).SetValue($drag, $panel)
$drag.GetType().GetField('childHandles', $fields).SetValue(
    $drag, [UnityEngine.RectTransform[]]$childHandles)

$drag.OnBeginDrag((Pointer $slot 5 8 $left))
$drag.OnDrag((Pointer $slot 13 20 $left))
Assert-Position $panel 10 20 'Dragging a slot must not move the bag panel.'
$drag.OnBeginDrag((Pointer $handle 5 8 $right))
$drag.OnDrag((Pointer $handle 13 20 $right))
Assert-Position $panel 10 20 'Right-button drag must not move the panel.'
$drag.OnBeginDrag((Pointer $handle 5 8 $left))
$drag.OnDrag((Pointer $handle 13 20 $left))
Assert-Position $panel 18 32 'The handle must move the panel without a jump.'
$drag.OnEndDrag((Pointer $handle 13 20 $left))
$drag.OnDrag((Pointer $handle 20 30 $left))
Assert-Position $panel 18 32 'Dragging must stop after release.'
$x = 18
$y = 32
$startX = 13
$startY = 20
foreach ($child in $childHandles) {
    $pressed = $child.gameObject
    $drag.OnBeginDrag((Pointer $pressed $startX $startY $left))
    $drag.OnDrag((Pointer $pressed ($startX + 7) ($startY + 10) $left))
    $x += 7
    $y += 10
    Assert-Position $panel $x $y 'Each child must move the same bag panel.'
    $drag.OnEndDrag((Pointer $pressed ($startX + 7) ($startY + 10) $left))
    $startX += 7
    $startY += 10
}
$drag.OnBeginDrag((Pointer $childHandles[0].gameObject $startX $startY $right))
$drag.OnDrag((Pointer $childHandles[0].gameObject ($startX + 7) ($startY + 10) $right))
Assert-Position $panel $x $y 'Right-button drag on a child must not move the panel.'
$drag.OnBeginDrag((Pointer $childHandles[0].gameObject $startX $startY $left))
$drag.GetType().GetMethod('OnDisable', $fields).Invoke($drag, @()) | Out-Null
$drag.OnDrag((Pointer $childHandles[0].gameObject ($startX + 7) ($startY + 10) $left))
Assert-Position $panel $x $y 'Disabling the handle must cancel child dragging.'
$panel.gameObject.activeInHierarchy = $false
$drag.OnBeginDrag((Pointer $childHandles[0].gameObject $startX $startY $left))
$drag.OnDrag((Pointer $childHandles[0].gameObject ($startX + 7) ($startY + 10) $left))
Assert-Position $panel $x $y 'An inactive panel must not move.'

Write-Output 'PASS: WorldShell bag and child drag areas move only the bag panel.'
