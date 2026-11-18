$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Get-Content (Join-Path $root 'Assets/Scripts/锻造/锻造面板拖动区域.cs') -Raw
$meta = Get-Content (Join-Path $root 'Assets/Scripts/锻造/锻造面板拖动区域.cs.meta') -Raw
$scene = Get-Content (Join-Path $root 'Assets/Scenes/WorldShell.unity') -Raw

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
Assert-True ($guid -ne '' -and
    (Block 1 '1532901186').Contains(
        'component: {fileID: 860000000000400018}') -and
    (Block 114 '860000000000400018').Contains(
        "guid: $guid") -and
    (Block 114 '860000000000400018').Contains(
        'panel: {fileID: 356352629}') -and
    (Block 224 '1532901187').Contains(
        'm_Father: {fileID: 356352629}') -and
    (Block 114 '1532901188').Contains('m_RaycastTarget: 1') -and
    (Block 222 '1532901189').Contains('m_CullTransparentMesh: 0')) `
    'Forging drag area must raycast and move its background.'

$stubs = @'
namespace UnityEngine
{
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public sealed class DisallowMultipleComponent : System.Attribute {}
    [System.AttributeUsage(System.AttributeTargets.Field)]
    public sealed class SerializeField : System.Attribute {}
    public class MonoBehaviour { public GameObject gameObject { get; set; } }
    public class GameObject { public bool activeInHierarchy { get; set; } = true; }
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
$body = $source -replace '(?m)^using [^\r\n]+;\r?\n', ''
Add-Type -TypeDefinition ("using UnityEngine;`n" +
    "using UnityEngine.EventSystems;`n" + $stubs + "`n" + $body) `
    -IgnoreWarnings | Out-Null

function Pointer($pressed, [int]$x, [int]$y, $button) {
    $data = [UnityEngine.EventSystems.PointerEventData]::new()
    $data.button = $button
    $data.position = [UnityEngine.Vector2]::new($x, $y)
    $hit = [UnityEngine.EventSystems.RaycastResult]::new()
    $hit.gameObject = $pressed
    $data.pointerPressRaycast = $hit
    return $data
}
function Assert-Position($panel, [int]$x, [int]$y, [string]$message) {
    Assert-True ($panel.anchoredPosition.x -eq $x -and
        $panel.anchoredPosition.y -eq $y) $message
}

$left = [UnityEngine.EventSystems.PointerEventData+InputButton]::Left
$right = [UnityEngine.EventSystems.PointerEventData+InputButton]::Right
$handle = [UnityEngine.GameObject]::new()
$other = [UnityEngine.GameObject]::new()
$panel = [UnityEngine.RectTransform]::new()
$panel.parent = [UnityEngine.RectTransform]::new()
$panel.gameObject = [UnityEngine.GameObject]::new()
$panel.anchoredPosition = [UnityEngine.Vector2]::new(10, 20)
$drag = [Game.Forging.Presentation.锻造面板拖动区域]::new()
$drag.gameObject = $handle
$field = $drag.GetType().GetField('panel',
    [System.Reflection.BindingFlags]'Instance,NonPublic')
$field.SetValue($drag, $panel)

$drag.OnBeginDrag((Pointer $other 5 8 $left))
$drag.OnDrag((Pointer $other 13 20 $left))
Assert-Position $panel 10 20 'Other controls must not drag the panel.'
$drag.OnBeginDrag((Pointer $handle 5 8 $right))
$drag.OnDrag((Pointer $handle 13 20 $right))
Assert-Position $panel 10 20 'Right drag must not move the panel.'
$drag.OnBeginDrag((Pointer $handle 5 8 $left))
$drag.OnDrag((Pointer $handle 13 20 $left))
Assert-Position $panel 18 32 'Left drag must move the panel without jumping.'
$drag.OnEndDrag((Pointer $handle 13 20 $left))
$drag.OnDrag((Pointer $handle 20 30 $left))
Assert-Position $panel 18 32 'Release must stop the drag.'

Write-Output 'PASS: forging drag handle wiring and left-button movement.'
