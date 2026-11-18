$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$service = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/UI/ItemDetail/ItemDetailHoverService.cs') -Raw
$panel = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/UI/ItemDetail/ItemDetailPanelController.cs') -Raw
$trigger = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/UI/ItemDetail/ItemDetailHoverTrigger.cs') -Raw
$drag = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/UI/ItemDetail/ItemDetailPanelDragHandle.cs') -Raw

function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
}

Assert-True ($service -match 'MaxOpenPanels = 5' -and
    $service -match 'Dictionary<ItemDetailHoverTrigger, ItemDetailPanelController>' -and
    $service -match 'panels\.Count >= MaxOpenPanels') `
    'Open panels must be tracked per trigger and capped at five.'
Assert-True ($service -match 'Instantiate\(detailPanel, detailPanel\.transform\.parent\)' -and
    $service -match 'panel\.Show\(detail, screenPosition\)' -and
    $service -match 'panel\.transform\.SetAsLastSibling\(\)') `
    'Each additional detail needs its own panel and newly opened panels must be in front.'
Assert-True ($service -match 'openPanels\.Remove\(trigger\)' -and
    $service -match 'openPanels\.ContainsValue\(panel\)' -and
    $service -match 'Destroy\(panel\.gameObject\)') `
    'Closing a detail must release its panel; scene teardown must clean up copies.'
Assert-True ($service -match 'candidate\.gameObject\.name\.EndsWith\("\(Clone\)"') `
    'A copied panel must not become the template after a scene change.'
Assert-True ($panel -match 'CurrentDetail = detail;' -and
    $panel -match 'ResetScroll\(descriptionScroll\);' -and
    $panel -match 'ResetScroll\(detailScroll\);') `
    'Each panel must bind and reset its own detail and scrolling.'
Assert-True ($trigger -match 'hoverService\.HideIfDetailChanged\(this, detail\)' -and
    $trigger -match 'hoverService\.HideIfDetailChanged\(this, provider\)' -and
    $service -match 'openPanels\.ContainsKey\(trigger\)\)\s*HideIfDetailChanged\(trigger, provider') `
    'Rebinding a reused slot must not keep a stale detail panel.'
Assert-True ($service -match 'GameNoticeService\.Show\("最多同时打开 5 个详情窗口"\)' -and
    $service -match 'openPanels\.Count >= MaxOpenPanels') `
    'The sixth new detail must show the existing unified notice.'
Assert-True ($drag -match 'IPointerClickHandler' -and
    $drag -match 'eventData\.button == PointerEventData\.InputButton\.Right' -and
    $drag -match 'panel\.RequestClose\(\)' -and
    $panel -match 'CloseRequested\?\.Invoke\(this\)' -and
    $service -match 'HandleCloseRequested\(ItemDetailPanelController panel\)') `
    'Right-clicking a drag area must request closure of its own panel only.'

Write-Output 'Item detail multiple popup checks passed.'

$serviceSource = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/UI/ItemDetail/ItemDetailHoverService.cs') -Raw
$dragSource = $drag -replace '(?m)^using UnityEngine(?:\.EventSystems)?;\r?\n', ''
$stubs = @'
namespace UnityEngine
{
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public sealed class DisallowMultipleComponent : System.Attribute {}
    [System.AttributeUsage(System.AttributeTargets.Field)]
    public sealed class SerializeField : System.Attribute {}

    public class Object
    {
        public bool Destroyed;
        public static readonly System.Collections.Generic.List<Object> Registered = new();

        public static T Instantiate<T>(T original, Transform parent)
            where T : Object, new()
        {
            var copy = new T();
            if (copy is MonoBehaviour behaviour)
                behaviour.transform.parent = parent;
            return copy;
        }

        public static void Destroy(Object target)
        {
            target.Destroyed = true;
        }

        public static T[] FindObjectsOfType<T>(bool includeInactive)
            where T : Object
        {
            var result = new System.Collections.Generic.List<T>();
            foreach (Object item in Registered)
                if (item is T match)
                    result.Add(match);
            return result.ToArray();
        }
    }

    public class GameObject : Object
    {
        public string name;
        public int scene;
        public Transform transform = new Transform();
    }

    public class Transform
    {
        public Transform parent;
        public int frontCount;
        public void SetAsLastSibling() { frontCount++; }
    }

    public class RectTransform : Transform
    {
        public Vector3 localPosition;
    }

    public class Component : Object
    {
        public GameObject gameObject = new GameObject();
        public Transform transform => gameObject.transform;
        public T GetComponent<T>() where T : Object { return null; }
    }

    public class MonoBehaviour : Component {}

    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 operator +(Vector2 left, Vector2 right)
        {
            return new Vector2(left.x + right.x, left.y + right.y);
        }
        public static Vector2 operator -(Vector2 left, Vector2 right)
        {
            return new Vector2(left.x - right.x, left.y - right.y);
        }
    }

    public struct Vector3
    {
        public float x, y, z;
        public static explicit operator Vector2(Vector3 value)
        {
            return new Vector2(value.x, value.y);
        }
    }

    public static class RectTransformUtility
    {
        public static bool ScreenPointToLocalPointInRectangle(
            RectTransform parent, Vector2 screen, object camera, out Vector2 local)
        {
            local = screen;
            return true;
        }
    }

    public enum KeyCode { Escape }
    public static class Input
    {
        public static bool EscapePressed;
        public static bool GetKeyDown(KeyCode key)
        {
            bool pressed = EscapePressed;
            EscapePressed = false;
            return pressed;
        }
    }
}

namespace UnityEngine.EventSystems
{
    public class PointerEventData
    {
        public enum InputButton { Left, Right, Middle }
        public InputButton button;
        public UnityEngine.Vector2 position;
        public object pressEventCamera;
    }

    public interface IBeginDragHandler
    {
        void OnBeginDrag(PointerEventData eventData);
    }
    public interface IDragHandler
    {
        void OnDrag(PointerEventData eventData);
    }
    public interface IEndDragHandler
    {
        void OnEndDrag(PointerEventData eventData);
    }
    public interface IPointerClickHandler
    {
        void OnPointerClick(PointerEventData eventData);
    }
}

namespace Game.Items
{
    public sealed class ItemDetailData
    {
        public string Identity;
        public bool IsValid => !string.IsNullOrEmpty(Identity);
    }
}

namespace Game.UI.Notice
{
    public static class GameNoticeService
    {
        public static int Calls;
        public static string LastMessage;
        public static bool Show(string message)
        {
            Calls++;
            LastMessage = message;
            return true;
        }
    }
}

namespace Game.UI.ItemDetail
{
    public sealed class ItemDetailHoverTrigger : UnityEngine.MonoBehaviour
    {
        public bool notified;
        public void NotifyServiceHidden(ItemDetailHoverService service)
        {
            notified = true;
        }
    }

    public sealed class ItemDetailPanelController : UnityEngine.MonoBehaviour
    {
        public bool IsVisible { get; private set; }
        public Game.Items.ItemDetailData CurrentDetail { get; private set; }
        public UnityEngine.RectTransform PanelRect = new UnityEngine.RectTransform();
        public event System.Action<ItemDetailPanelController> CloseRequested;

        public void SetPanelLocalPosition(UnityEngine.Vector2 position)
        {
            PanelRect.localPosition = new UnityEngine.Vector3
                { x = position.x, y = position.y };
        }

        public void RequestClose()
        {
            CloseRequested?.Invoke(this);
        }

        public void Show(Game.Items.ItemDetailData detail, UnityEngine.Vector2 position)
        {
            CurrentDetail = detail;
            IsVisible = true;
        }

        public void Hide()
        {
            CurrentDetail = null;
            IsVisible = false;
        }
    }
}
'@
Add-Type -TypeDefinition ("using UnityEngine.EventSystems;`n" +
    $serviceSource + "`n" + $dragSource + "`n" + $stubs) -IgnoreWarnings | Out-Null

$sceneOwner = [UnityEngine.Component]::new()
$sceneOwner.gameObject.scene = 1
$configuredService = [Game.UI.ItemDetail.ItemDetailHoverService]::new()
$configuredService.gameObject.scene = 2
[UnityEngine.Object]::Registered.Add($configuredService)
Assert-True (-not [Game.UI.ItemDetail.ItemDetailHoverService]::IsConfiguredInScene($sceneOwner)) `
    'Another scene must not enable the legacy popup trigger.'
$configuredService.gameObject.scene = 1
Assert-True ([Game.UI.ItemDetail.ItemDetailHoverService]::IsConfiguredInScene($sceneOwner)) `
    'The legacy popup trigger must remain available in its own configured scene.'
[UnityEngine.Object]::Registered.Clear()

$manager = [Game.UI.ItemDetail.ItemDetailHoverService]::new()
$original = [Game.UI.ItemDetail.ItemDetailPanelController]::new()
$flags = [System.Reflection.BindingFlags]'Instance, NonPublic'
$manager.GetType().GetField('detailPanel', $flags).SetValue($manager, $original)
$windows = $manager.GetType().GetField('openPanels', $flags).GetValue($manager)
$panels = $manager.GetType().GetField('panels', $flags).GetValue($manager)
$triggers = @(1..6 | ForEach-Object {
    [Game.UI.ItemDetail.ItemDetailHoverTrigger]::new()
})
$details = @(1..6 | ForEach-Object {
    $detail = [Game.Items.ItemDetailData]::new()
    $detail.Identity = "item-$_"
    $detail
})
$position = [UnityEngine.Vector2]::new(20, 30)

for ($i = 0; $i -lt 5; $i++) {
    $manager.Show($triggers[$i], $details[$i], $position)
}
Assert-True ($windows.Count -eq 5 -and $panels.Count -eq 5) `
    'Five independent panels should open.'
Assert-True ($panels[0] -eq $original -and
    @($panels | Where-Object { $_.IsVisible }).Count -eq 5) `
    'The original panel and four copies should remain visible.'

$manager.Show($triggers[5], $details[5], $position)
Assert-True ($windows.Count -eq 5 -and $panels.Count -eq 5) `
    'A sixth click must leave the existing panels unchanged.'
Assert-True ([Game.UI.Notice.GameNoticeService]::Calls -eq 1 -and
    [Game.UI.Notice.GameNoticeService]::LastMessage -eq '最多同时打开 5 个详情窗口') `
    'A sixth click must show the limit notice.'

$handle = [Game.UI.ItemDetail.ItemDetailPanelDragHandle]::new()
$handle.Initialize($panels[1])
$panels[1].PanelRect.parent = [UnityEngine.RectTransform]::new()
$left = [UnityEngine.EventSystems.PointerEventData]::new()
$left.button = [UnityEngine.EventSystems.PointerEventData+InputButton]::Left
$left.position = $position
$previousFrontCount = $panels[1].transform.frontCount
$handle.OnPointerClick($left)
Assert-True ($windows.Count -eq 5 -and $panels[1].IsVisible -and
    $panels[1].transform.frontCount -eq $previousFrontCount + 1) `
    'Left-clicking the drag handle must focus without closing the detail.'
$handle.OnBeginDrag($left)
$left.position = [UnityEngine.Vector2]::new(35, 45)
$handle.OnDrag($left)
$handle.OnEndDrag($left)
Assert-True ($panels[1].PanelRect.localPosition.x -eq 15 -and
    $panels[1].PanelRect.localPosition.y -eq 15 -and
    $panels[1].transform.frontCount -eq $previousFrontCount + 2) `
    'Left dragging must retain pointer offset and bring its panel forward.'

$right = [UnityEngine.EventSystems.PointerEventData]::new()
$right.button = [UnityEngine.EventSystems.PointerEventData+InputButton]::Right
$handle.OnPointerClick($right)
Assert-True ($windows.Count -eq 4 -and -not $panels[1].IsVisible -and
    $original.IsVisible) `
    'Right-clicking a handle must close only its own detail.'
$handle.OnPointerClick($right)
Assert-True ($windows.Count -eq 4) `
    'Right-clicking a hidden panel must not close another detail.'
$manager.Show($triggers[1], $details[1], $position)
Assert-True ($windows.Count -eq 5 -and $panels.Count -eq 5 -and
    $panels[1].IsVisible) `
    'Closing via a panel must free its slot for reuse.'

$manager.Show($triggers[0], $details[0], $position)
Assert-True ($windows.Count -eq 4 -and -not $original.IsVisible) `
    'Clicking an open item must close only its own panel.'
$manager.Show($triggers[5], $details[5], $position)
Assert-True ($windows.Count -eq 5 -and $panels.Count -eq 5 -and
    $original.CurrentDetail.Identity -eq 'item-6') `
    'The freed panel must be reused for the next item.'

$manager.HideIfDetailChanged($triggers[5], $details[5])
Assert-True ($windows.Count -eq 5) `
    'Rebinding the same item must keep its open panel.'
$replacement = [Game.Items.ItemDetailData]::new()
$replacement.Identity = 'item-replaced'
$manager.HideIfDetailChanged($triggers[5], $replacement)
Assert-True ($windows.Count -eq 4 -and -not $original.IsVisible) `
    'Rebinding a different item must close only its stale panel.'

$script:providerCalls = 0
$provider = [Func[Game.Items.ItemDetailData]]{
    $script:providerCalls++
    $details[5]
}
$manager.HideIfDetailChanged($triggers[5], $provider)
Assert-True ($script:providerCalls -eq 0) `
    'An unopened slot must not evaluate its detail provider during rebinding.'
$manager.Show($triggers[5], $details[5], $position)
$manager.HideIfDetailChanged($triggers[5], $provider)
Assert-True ($windows.Count -eq 5 -and $script:providerCalls -eq 1) `
    'An open slot should retain its window when its provider still resolves the same item.'

[UnityEngine.Input]::EscapePressed = $true
$manager.GetType().GetMethod('Update', $flags).Invoke($manager, @())
Assert-True ($windows.Count -eq 0 -and
    @($panels | Where-Object { $_.IsVisible }).Count -eq 0) `
    'Escape must close all detail windows without leaving visible copies.'
$manager.Show($triggers[0], $details[0], $position)
$manager.Show($triggers[1], $details[1], $position)
Assert-True ($windows.Count -eq 2 -and $panels.Count -eq 5) `
    'Panels should be reusable after Escape closes them all.'

$manager.GetType().GetMethod('OnDisable', $flags).Invoke($manager, @())
Assert-True ($windows.Count -eq 0 -and $panels.Count -eq 0) `
    'Disabling the service must release open and pooled panels.'
Assert-True (@($panels | Where-Object { $_.IsVisible }).Count -eq 0 -and
    @($triggers | Where-Object { $_.notified }).Count -eq 2) `
    'Service teardown must hide windows and notify their triggers.'

Write-Output 'Item detail multiple popup behavior checks passed.'
