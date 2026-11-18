$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Get-Content (Join-Path $root 'Assets/Scripts/Alchemy/Presentation/炼丹界面拖动区域.cs') -Raw
$meta = Get-Content (Join-Path $root 'Assets/Scripts/Alchemy/Presentation/炼丹界面拖动区域.cs.meta') -Raw
$panelSource = Get-Content (Join-Path $root 'Assets/Scripts/Alchemy/Presentation/AlchemyPanelView.cs') -Raw
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
$dragObject = Block 1 '522381163'
$dragComponent = Block 114 '860000000000400019'
$parent = Block 224 '827217726'
$parts = @('1784731223', '804118956', '522381164', '221287374')
Assert-True ($guid -ne '' -and
    $dragObject.Contains('component: {fileID: 860000000000400019}') -and
    $dragComponent.Contains("guid: $guid") -and
    (Block 114 '522381165').Contains('m_RaycastTarget: 1') -and
    (Block 222 '522381166').Contains('m_CullTransparentMesh: 0')) `
    'The transparent alchemy drag area must receive pointer events.'
foreach ($id in $parts) {
    Assert-True ($dragComponent.Contains("- {fileID: $id}") -and
        (Block 224 $id).Contains('m_Father: {fileID: 827217726}') -and
        $parent.Contains("- {fileID: $id}")) `
        "Alchemy part $id must move with the drag area."
}
$panel = Block 114 '860000000000100003'
Assert-True ($panel.Contains('m_GameObject: {fileID: 827217722}') -and
    $panel.Contains('closeButton: {fileID: 221287375}') -and
    (Block 114 '221287375').Contains(
        'm_GameObject: {fileID: 221287373}') -and
    $panelSource.Contains('closeButton.onClick.AddListener(Close)') -and
    $panelSource.Contains('closeButton.onClick.RemoveListener(Close)') -and
    $panelSource -match '(?s)private void Close\(\)\s*\{\s*gameObject.SetActive\(false\)') `
    'The close button must disable the alchemy canvas without duplicate listeners.'

$stubs = @'
namespace UnityEngine
{
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public sealed class DisallowMultipleComponent : System.Attribute {}
    [System.AttributeUsage(System.AttributeTargets.Field)]
    public sealed class SerializeField : System.Attribute {}
    public class GameObject {}
    public class MonoBehaviour
    {
        public GameObject gameObject { get; set; }
        public Transform transform { get; set; }
    }
    public class Transform { public Transform parent { get; set; } }
    public class RectTransform : Transform
    {
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
function Assert-Positions($items, [int]$shiftX, [int]$shiftY) {
    for ($i = 0; $i -lt $items.Length; $i++) {
        Assert-True ($items[$i].anchoredPosition.x -eq 10 * $i + $shiftX -and
            $items[$i].anchoredPosition.y -eq 20 * $i + $shiftY) `
            "Alchemy part $i moved incorrectly."
    }
}

$left = [UnityEngine.EventSystems.PointerEventData+InputButton]::Left
$right = [UnityEngine.EventSystems.PointerEventData+InputButton]::Right
$canvas = [UnityEngine.RectTransform]::new()
$handle = [UnityEngine.GameObject]::new()
$other = [UnityEngine.GameObject]::new()
$drag = [Game.Alchemy.Presentation.炼丹界面拖动区域]::new()
$drag.gameObject = $handle
$drag.transform = [UnityEngine.RectTransform]::new()
$drag.transform.parent = $canvas
$items = @()
for ($i = 0; $i -lt 4; $i++) {
    $item = [UnityEngine.RectTransform]::new()
    $item.parent = $canvas
    $item.anchoredPosition = [UnityEngine.Vector2]::new(10 * $i, 20 * $i)
    $items += $item
}
$field = $drag.GetType().GetField('movableParts',
    [System.Reflection.BindingFlags]'Instance,NonPublic')
$field.SetValue($drag, [UnityEngine.RectTransform[]]$items)
$drag.OnBeginDrag((Pointer $other 5 8 $left))
$drag.OnDrag((Pointer $other 13 20 $left))
Assert-Positions $items 0 0
$drag.OnBeginDrag((Pointer $handle 5 8 $right))
$drag.OnDrag((Pointer $handle 13 20 $right))
Assert-Positions $items 0 0
$drag.OnBeginDrag((Pointer $handle 5 8 $left))
$drag.OnDrag((Pointer $handle 13 20 $left))
Assert-Positions $items 8 12
$drag.OnEndDrag((Pointer $handle 13 20 $left))
$drag.OnDrag((Pointer $handle 20 30 $left))
Assert-Positions $items 8 12

Write-Output 'PASS: WorldShell alchemy close button and full-panel left drag.'
