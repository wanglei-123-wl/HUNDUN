$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/UI/提示面板交互.cs') -Raw

$stubs = @'
namespace UnityEngine
{
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public sealed class DisallowMultipleComponent : System.Attribute {}
    [System.AttributeUsage(System.AttributeTargets.Field)]
    public sealed class SerializeField : System.Attribute {}
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 operator +(Vector2 a, Vector2 b) =>
            new Vector2(a.x + b.x, a.y + b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) =>
            new Vector2(a.x - b.x, a.y - b.y);
    }
    public class GameObject
    {
        public bool activeSelf = true;
        public bool activeInHierarchy => activeSelf;
        public Transform transform;
        public GameObject() { transform = new RectTransform { gameObject = this }; }
        public void SetActive(bool value) { activeSelf = value; }
    }
    public class Transform
    {
        public GameObject gameObject;
        public Transform parent;
        public bool IsChildOf(Transform ancestor)
        {
            for (Transform current = this; current != null; current = current.parent)
                if (current == ancestor) return true;
            return false;
        }
    }
    public class RectTransform : Transform
    {
        public Vector2 anchoredPosition;
    }
    public class MonoBehaviour
    {
        public GameObject gameObject = new GameObject();
    }
    public static class RectTransformUtility
    {
        public static bool ScreenPointToLocalPointInRectangle(
            RectTransform parent, Vector2 screen, object camera, out Vector2 local)
        {
            local = screen;
            return parent != null;
        }
    }
}
namespace UnityEngine.UI
{
    public sealed class Button
    {
        public GameObject gameObject = new GameObject();
        public Transform transform => gameObject.transform;
        public ButtonClickedEvent onClick = new ButtonClickedEvent();
    }
    public sealed class ButtonClickedEvent
    {
        private event System.Action callbacks;
        public void AddListener(System.Action callback) { callbacks += callback; }
        public void RemoveListener(System.Action callback) { callbacks -= callback; }
        public void Invoke() { callbacks?.Invoke(); }
    }
}
namespace UnityEngine.EventSystems
{
    public interface IBeginDragHandler { void OnBeginDrag(PointerEventData eventData); }
    public interface IDragHandler { void OnDrag(PointerEventData eventData); }
    public interface IEndDragHandler { void OnEndDrag(PointerEventData eventData); }
    public struct RaycastResult { public GameObject gameObject; }
    public sealed class PointerEventData
    {
        public enum InputButton { Left, Right }
        public InputButton button;
        public Vector2 position;
        public object pressEventCamera;
        public RaycastResult pointerPressRaycast;
    }
}
'@
Add-Type -TypeDefinition ($source + "`n" + $stubs) -IgnoreWarnings | Out-Null

function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
}
function Pointer($target, $x, $y, $button = 'Left') {
    $data = [UnityEngine.EventSystems.PointerEventData]::new()
    $data.button = [UnityEngine.EventSystems.PointerEventData+InputButton]::$button
    $data.position = [UnityEngine.Vector2]::new($x, $y)
    $hit = [UnityEngine.EventSystems.RaycastResult]::new()
    $hit.gameObject = $target
    $data.pointerPressRaycast = $hit
    return $data
}

$controller = [Game.UI.提示面板交互]::new()
$toggle = [UnityEngine.UI.Button]::new()
$back = [UnityEngine.UI.Button]::new()
$panelObject = [UnityEngine.GameObject]::new()
$panel = [UnityEngine.RectTransform]$panelObject.transform
$panel.parent = [UnityEngine.GameObject]::new().transform
$panel.anchoredPosition = [UnityEngine.Vector2]::new(-64, 36)
$panelObject.SetActive($false)
$back.transform.parent = $panel
$type = $controller.GetType()
$flags = [System.Reflection.BindingFlags]'Instance,NonPublic'
$type.GetField('提示按钮', $flags).SetValue($controller, $toggle)
$type.GetField('提示面板', $flags).SetValue($controller, $panel)
$type.GetField('提示面板返回按钮', $flags).SetValue($controller, $back)
$type.GetMethod('OnEnable', $flags).Invoke($controller, @()) | Out-Null
Assert-True (-not $panelObject.activeSelf) 'The existing hidden state must remain unchanged.'

$toggle.onClick.Invoke()
Assert-True ($panelObject.activeSelf) 'The hint button must open the panel.'
$toggle.onClick.Invoke()
Assert-True (-not $panelObject.activeSelf) 'The hint button must also close the panel.'
$toggle.onClick.Invoke()

$start = Pointer $panelObject 10 10
$controller.OnBeginDrag($start)
$controller.OnDrag((Pointer $panelObject 30 35))
$controller.OnEndDrag($start)
Assert-True ($panel.anchoredPosition.x -eq -44 -and
    $panel.anchoredPosition.y -eq 61) `
    'Dragging the panel must preserve the pointer offset and move freely.'

$controller.OnBeginDrag((Pointer $back.gameObject 0 0))
$controller.OnDrag((Pointer $back.gameObject 60 60))
$controller.OnEndDrag($null)
Assert-True ($panel.anchoredPosition.x -eq -44 -and
    $panel.anchoredPosition.y -eq 61) `
    'Pressing on the return button must never start a panel drag.'

$controller.OnBeginDrag((Pointer $panelObject 0 0 'Right'))
$controller.OnDrag((Pointer $panelObject 60 60 'Right'))
$controller.OnEndDrag($null)
Assert-True ($panel.anchoredPosition.x -eq -44) `
    'Only left mouse dragging may move the panel.'

$back.onClick.Invoke()
Assert-True (-not $panelObject.activeSelf) 'The return button must close the panel.'
$toggle.onClick.Invoke()
Assert-True ($panelObject.activeSelf -and
    $panel.anchoredPosition.x -eq -44) `
    'Reopening the panel must keep its dragged position.'

$type.GetMethod('OnDisable', $flags).Invoke($controller, @()) | Out-Null
$toggle.onClick.Invoke()
Assert-True ($panelObject.activeSelf) `
    'Disabling the controller must remove its button listeners.'

$scene = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scenes/WorldShell.unity') -Raw
$meta = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/UI/提示面板交互.cs.meta') -Raw
$guid = [regex]::Match($meta, '(?m)^guid: ([0-9a-f]{32})\r?$').Groups[1].Value
$component = [regex]::Match($scene,
    '(?s)--- !u!114 &860000000000300001\r?\nMonoBehaviour:.*?(?=--- !u!)').Value
$panelBlock = [regex]::Match($scene,
    '(?s)--- !u!1 &1439563607\r?\nGameObject:.*?(?=--- !u!)').Value
Assert-True ($guid.Length -eq 32 -and
    $scene.Contains('component: {fileID: 860000000000300001}') -and
    $component.Contains("guid: $guid") -and
    $component.Contains('{fileID: 1529479446}') -and
    $component.Contains('{fileID: 1439563608}') -and
    $component.Contains('{fileID: 708610282}') -and
    $panelBlock.Contains('m_IsActive: 0')) `
    'The saved scene must wire the existing buttons and keep the panel hidden.'

Write-Output '提示面板按钮和拖拽检查通过'
