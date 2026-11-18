$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Get-Content (Join-Path $root 'Assets/Scripts/CharacterPanel/Presentation/CharacterPanelDragHandle.cs') -Raw
$meta = Get-Content (Join-Path $root 'Assets/Scripts/CharacterPanel/Presentation/CharacterPanelDragHandle.cs.meta') -Raw
$scene = Get-Content (Join-Path $root 'Assets/Scenes/WorldShell.unity') -Raw

function Assert-True([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}
function Block([string]$type, [string]$id) {
    [regex]::Match($scene, "(?ms)^--- !u!$type &$id`r?`n(.*?)(?=^--- !u!|\z)").Value
}

$guid = ([regex]::Match($meta, 'guid: ([0-9a-f]{32})')).Groups[1].Value
Assert-True ($guid -ne '') 'Drag component meta GUID is missing.'
Assert-True ((Block 1 124146496) -match 'component: \{fileID: 860000000000400006\}') `
    'The drag component must be attached to the primary drag area.'
Assert-True ((Block 114 860000000000400006) -match
    "m_Script: \{fileID: 11500000, guid: $guid, type: 3\}" -and
    (Block 114 860000000000400006) -match 'panel: \{fileID: 1180333856\}' -and
    (Block 114 860000000000400006) -match 'secondaryHandle: \{fileID: 741746968\}') `
    'The drag component must target the panel background and the second drag area.'
Assert-True ((Block 224 741746968) -match 'm_Father: \{fileID: 124146497\}' -and
    (Block 114 124146498) -match 'm_RaycastTarget: 1' -and
    (Block 114 741746969) -match 'm_RaycastTarget: 1' -and
    (Block 222 124146499) -match 'm_CullTransparentMesh: 0' -and
    (Block 222 741746970) -match 'm_CullTransparentMesh: 0') `
    'Both transparent drag areas must remain raycastable.'
$children = (Block 224 1180333856)
Assert-True ($children.IndexOf('fileID: 124146497') -lt
    $children.IndexOf('fileID: 1896928450') -and
    $children.IndexOf('fileID: 124146497') -lt
    $children.IndexOf('fileID: 900979443')) `
    'Equipment slots and close button must remain above the drag area.'
foreach ($renderer in @(
    439793031, 757758204, 1013164750, 1124003735,
    1155698276, 1305373801, 2036572012, 2121055920
)) {
    Assert-True ((Block 222 $renderer) -match 'm_CullTransparentMesh: 0') `
        "Equipment slot renderer $renderer must remain raycastable above the drag area."
}

$fixture = @'
namespace UnityEngine
{
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public sealed class DisallowMultipleComponent : System.Attribute {}
    [System.AttributeUsage(System.AttributeTargets.Field)]
    public sealed class SerializeField : System.Attribute {}
    public class MonoBehaviour { public Transform transform { get; set; } }
    public class GameObject
    {
        public Transform transform { get; set; }
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
Add-Type -TypeDefinition ($source + "`n" + $fixture) -IgnoreWarnings | Out-Null

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
$primary = [UnityEngine.GameObject]::new()
$secondary = [UnityEngine.GameObject]::new()
$equipment = [UnityEngine.GameObject]::new()
$primary.transform = [UnityEngine.RectTransform]::new()
$secondary.transform = [UnityEngine.RectTransform]::new()
$equipment.transform = [UnityEngine.RectTransform]::new()
$parent = [UnityEngine.RectTransform]::new()
$panel = [UnityEngine.RectTransform]::new()
$panel.parent = $parent
$panel.gameObject = [UnityEngine.GameObject]::new()
$panel.anchoredPosition = [UnityEngine.Vector2]::new(10, 20)
$drag = [Game.CharacterPanel.Presentation.CharacterPanelDragHandle]::new()
$drag.transform = $primary.transform
$fields = [System.Reflection.BindingFlags]'Instance,NonPublic'
$drag.GetType().GetField('panel', $fields).SetValue($drag, $panel)
$drag.GetType().GetField('secondaryHandle', $fields).SetValue($drag, $secondary.transform)

$drag.OnBeginDrag((Pointer $equipment 5 8 $left))
$drag.OnDrag((Pointer $equipment 13 20 $left))
Assert-Position $panel 10 20 'Equipment drag must not move the character panel.'
$drag.OnBeginDrag((Pointer $primary 5 8 $right))
$drag.OnDrag((Pointer $primary 13 20 $right))
Assert-Position $panel 10 20 'Right-button drag must not move the panel.'
$drag.OnBeginDrag((Pointer $primary 5 8 $left))
$drag.OnDrag((Pointer $primary 13 20 $left))
Assert-Position $panel 18 32 'Primary area must move the panel without a pointer jump.'
$drag.OnEndDrag((Pointer $primary 13 20 $left))
$drag.OnDrag((Pointer $primary 20 30 $left))
Assert-Position $panel 18 32 'Dragging must stop after release.'
$drag.OnBeginDrag((Pointer $secondary 13 20 $left))
$drag.OnDrag((Pointer $secondary 20 30 $left))
Assert-Position $panel 25 42 'Second area must move the same panel.'
$drag.GetType().GetMethod('OnDisable', $fields).Invoke($drag, @()) | Out-Null
$drag.OnDrag((Pointer $secondary 30 40 $left))
Assert-Position $panel 25 42 'Disabling the handle must cancel the drag.'
$panel.gameObject.activeInHierarchy = $false
$drag.OnBeginDrag((Pointer $primary 20 30 $left))
$drag.OnDrag((Pointer $primary 30 40 $left))
Assert-Position $panel 25 42 'A hidden panel must not move.'

Write-Output 'PASS: character panel drag areas and interactive control exclusion.'
