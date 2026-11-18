$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Inventory/Presentation/V2/InventoryBagRightDragScroll.cs') -Raw
$stubs = @'
namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class RequireComponent : Attribute
    {
        public RequireComponent(Type component) {}
    }
    public class MonoBehaviour
    {
        public object component;
        public T GetComponent<T>() where T : class { return component as T; }
    }
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    }
    public struct Rect { public float height; }
    public struct Bounds { public Vector3 size; }
    public class RectTransform { public Rect rect; }
    public static class Mathf
    {
        public static float Clamp01(float value)
        { return Math.Max(0f, Math.Min(1f, value)); }
    }
    public static class RectTransformUtility
    {
        public static bool ScreenPointToLocalPointInRectangle(
            RectTransform viewport, Vector2 position, object camera, out Vector2 local)
        {
            local = position;
            return true;
        }
        public static Bounds CalculateRelativeRectTransformBounds(
            RectTransform viewport, RectTransform content)
        {
            return new Bounds { size = new Vector3 { y = content.rect.height } };
        }
    }
}
namespace UnityEngine.EventSystems
{
    public interface IBeginDragHandler { void OnBeginDrag(PointerEventData data); }
    public interface IDragHandler { void OnDrag(PointerEventData data); }
    public interface IEndDragHandler { void OnEndDrag(PointerEventData data); }
    public class PointerEventData
    {
        public enum InputButton { Left, Right, Middle }
        public InputButton button;
        public int pointerId;
        public UnityEngine.Vector2 position;
        public object pressEventCamera;
    }
}
namespace UnityEngine.UI
{
    public class ScrollRect : UnityEngine.MonoBehaviour
    {
        public UnityEngine.RectTransform viewport, content;
        public bool vertical = true;
        public float verticalNormalizedPosition = 1f;
        public bool active = true;
        public bool stopped;
        public bool IsActive() { return active; }
        public void StopMovement() { stopped = true; }
    }
}
public static class InventoryBagRightDragScrollTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
    public static void Run()
    {
        var scroll = new UnityEngine.UI.ScrollRect
        {
            viewport = new UnityEngine.RectTransform {
                rect = new UnityEngine.Rect { height = 100f } },
            content = new UnityEngine.RectTransform {
                rect = new UnityEngine.Rect { height = 200f } }
        };
        var adapter = new Game.Inventory.Presentation.V2.InventoryBagRightDragScroll();
        adapter.component = scroll;
        typeof(Game.Inventory.Presentation.V2.InventoryBagRightDragScroll)
            .GetMethod("Awake", System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance)
            .Invoke(adapter, null);

        var left = new UnityEngine.EventSystems.PointerEventData {
            button = UnityEngine.EventSystems.PointerEventData.InputButton.Left,
            pointerId = -1 };
        adapter.OnBeginDrag(left);
        left.position = new UnityEngine.Vector2(0f, 30f);
        adapter.OnDrag(left);
        Check(scroll.verticalNormalizedPosition == 1f && !scroll.stopped,
            "Left drag must stay outside the right-button scroll path.");

        var right = new UnityEngine.EventSystems.PointerEventData {
            button = UnityEngine.EventSystems.PointerEventData.InputButton.Right,
            pointerId = -2 };
        adapter.OnBeginDrag(right);
        right.position = new UnityEngine.Vector2(0f, 25f);
        adapter.OnDrag(right);
        Check(scroll.stopped && Math.Abs(scroll.verticalNormalizedPosition - 0.75f) < 0.001f,
            "Dragging up with the right button must scroll vertically.");

        adapter.OnBeginDrag(left);
        right.position = new UnityEngine.Vector2(0f, 50f);
        adapter.OnDrag(right);
        Check(Math.Abs(scroll.verticalNormalizedPosition - 0.5f) < 0.001f,
            "A simultaneous left drag must not cancel the right-button scroll.");
        right.position = new UnityEngine.Vector2(0f, 25f);
        adapter.OnDrag(right);
        Check(Math.Abs(scroll.verticalNormalizedPosition - 0.75f) < 0.001f,
            "Dragging back down must move the scroll in the opposite direction.");

        var otherPointer = new UnityEngine.EventSystems.PointerEventData {
            button = UnityEngine.EventSystems.PointerEventData.InputButton.Right,
            pointerId = 42, position = new UnityEngine.Vector2(0f, 80f) };
        adapter.OnDrag(otherPointer);
        Check(Math.Abs(scroll.verticalNormalizedPosition - 0.75f) < 0.001f,
            "Another pointer must not hijack the active drag.");
        adapter.OnEndDrag(right);
        right.position = new UnityEngine.Vector2(0f, 80f);
        adapter.OnDrag(right);
        Check(Math.Abs(scroll.verticalNormalizedPosition - 0.75f) < 0.001f,
            "Releasing the right button must stop dragging.");

        scroll.verticalNormalizedPosition = 1f;
        scroll.content.rect = new UnityEngine.Rect { height = 100f };
        right.position = new UnityEngine.Vector2(0f, 0f);
        adapter.OnBeginDrag(right);
        right.position = new UnityEngine.Vector2(0f, 25f);
        adapter.OnDrag(right);
        Check(scroll.verticalNormalizedPosition == 1f,
            "Content no taller than the viewport must not move.");
    }
}
'@
Add-Type -TypeDefinition ("using System;`n" + $source + "`n" + $stubs) -IgnoreWarnings
[InventoryBagRightDragScrollTests]::Run()

$slot = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Inventory/Presentation/V2/InventoryBagDragSlot.cs') -Raw
foreach ($callback in @('OnBeginDrag', 'OnDrag', 'OnEndDrag')) {
    if ($slot -notmatch ("rightDragScroll\?\." + $callback + "\(eventData\)")) {
        throw "Bag slot does not forward $callback to the right-drag handler."
    }
}
$scene = Get-Content -LiteralPath (Join-Path $root 'Assets/Scenes/WorldShell.unity') -Raw
if ($scene -notmatch 'm_GameObject: \{fileID: 1782663210\}[\s\S]*?m_Script: \{fileID: 11500000, guid: e975642c70b7473d9809100e561113bb') {
    throw 'The bag Scroll View is missing its right-drag component.'
}
Write-Output 'Inventory bag right-drag scrolling tests passed.'
