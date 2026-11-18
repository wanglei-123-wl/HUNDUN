$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$bridgePath = Join-Path $root 'Assets/Scripts/Loot/Interaction/LootClickBridge.cs'
$bridgeSource = (Get-Content -LiteralPath $bridgePath -Raw) `
    -replace '(?m)^using [^\r\n]+;\r?\n', ''
$teleporterPath = Join-Path $root 'Assets/Scripts/MapTeleporterInteraction.cs'
$teleporterSource = Get-Content -LiteralPath $teleporterPath -Raw
$pointerMethod = [regex]::Match($teleporterSource,
    '(?s)    private bool TryGetPointer\(out Vector2 position\)\s*\{.*?\r?\n    \}')
if (-not $pointerMethod.Success) {
    throw 'Map teleporter pointer handler was not found.'
}
$scene = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scenes/WorldShell.unity') -Raw
if ($scene -notmatch
    '(?s)m_Script: \{fileID: 11500000, guid: 51718463ab81fa5479eb34e24b46459d, type: 3\}(?:(?!--- !u!).)*mouseButton: 2') {
    throw 'WorldShell must preserve the configured middle-click pickup.'
}

$stubs = @'
namespace UnityEngine
{
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public sealed class DisallowMultipleComponent : System.Attribute {}
    [System.AttributeUsage(System.AttributeTargets.Field)]
    public sealed class SerializeField : System.Attribute {}
    [System.AttributeUsage(System.AttributeTargets.Field)]
    public sealed class HeaderAttribute : System.Attribute
    {
        public HeaderAttribute(string title) {}
    }
    [System.AttributeUsage(System.AttributeTargets.Field)]
    public sealed class MinAttribute : System.Attribute
    {
        public MinAttribute(float value) {}
    }
    [System.AttributeUsage(System.AttributeTargets.Field)]
    public sealed class RangeAttribute : System.Attribute
    {
        public RangeAttribute(float min, float max) {}
    }
    public class MonoBehaviour
    {
        public bool isActiveAndEnabled = true;
        protected T FindObjectOfType<T>() where T : class => TestObjects.Bridge as T;
        public T GetComponent<T>() where T : class => null;
    }
    public static class TestObjects { public static LootClickBridge Bridge; }
    public struct LayerMask
    {
        public static implicit operator LayerMask(int value) => new LayerMask();
    }
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static implicit operator Vector2(Vector3 point) =>
            new Vector2(point.x, point.y);
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z)
        { this.x = x; this.y = y; this.z = z; }
    }
    public class Transform { public Vector3 position; }
    public struct Ray {}
    public class Camera : MonoBehaviour
    {
        public static Camera main;
        public Ray ScreenPointToRay(Vector3 point) => new Ray();
    }
    public static class Input
    {
        public static bool[] Down = new bool[3];
        public static Vector3 mousePosition = new Vector3(12, 34, 0);
        public static bool GetMouseButtonDown(int button) => Down[button];
    }
    public static class Mathf
    {
        public static float Max(float a, float b) => System.Math.Max(a, b);
    }
    public enum QueryTriggerInteraction { Collide }
    public class Collider
    {
        public WorldLootView View;
        public T GetComponentInParent<T>() where T : class => View as T;
    }
    public struct RaycastHit
    {
        public float distance;
        public Collider collider;
    }
    public static class Physics
    {
        public static RaycastHit[] Hits = System.Array.Empty<RaycastHit>();
        public static RaycastHit[] RaycastAll(
            Ray ray, float distance, LayerMask layers, QueryTriggerInteraction triggers)
            => Hits;
    }
}
namespace UnityEngine.EventSystems
{
    public sealed class EventSystem
    {
        public static EventSystem current;
        public bool PointerOverUi;
        public bool IsPointerOverGameObject() => PointerOverUi;
    }
}
public static class GameplayUiInputGuard { public static bool BlocksGameplayInput; }
public sealed class LocalPlayerProvider
{
    public static LocalPlayerProvider Instance;
    public UnityEngine.Transform PlayerTransform;
}
public sealed class ThirdPersonCameraFollow : UnityEngine.MonoBehaviour {}
public sealed class WorldLootView
{
    public bool isActiveAndEnabled = true;
    public string LootId = "loot-a";
}
public enum LootInteractionResult
{
    InvalidTarget, ProtectedByOther, MoveCloserRequested,
    LootNotFound, WrongScene, PickupRejected, PickupRequested
}
public sealed class LootFeedbackPresenter
{
    public void ShowError(string text) {}
}
public sealed class LootModuleRuntime
{
    public int Requests;
    public WorldLootView LastView;
    public LootFeedbackPresenter FeedbackPresenter;
    public LootInteractionResult TryHandleClick(
        WorldLootView view, UnityEngine.Vector3 playerPosition)
    {
        Requests++;
        LastView = view;
        return LootInteractionResult.PickupRequested;
    }
}
'@
$teleporterHarness = @'
public sealed class MapTeleporterInteraction : UnityEngine.MonoBehaviour
{
    private LootClickBridge lootClickBridge;
    public bool ReadPointer(out UnityEngine.Vector2 position) =>
        TryGetPointer(out position);
__POINTER_METHOD__
}
'@.Replace('__POINTER_METHOD__', $pointerMethod.Value)
$source = "using System;`nusing UnityEngine;`nusing UnityEngine.EventSystems;`n" `
    + $stubs + $bridgeSource + $teleporterHarness
Add-Type -TypeDefinition $source -IgnoreWarnings | Out-Null

$failures = [System.Collections.Generic.List[string]]::new()
function Check([bool]$condition, [string]$message) {
    if (-not $condition) { $failures.Add($message) }
}
function Set-Buttons([bool]$left, [bool]$right, [bool]$middle) {
    [UnityEngine.Input]::Down[0] = $left
    [UnityEngine.Input]::Down[1] = $right
    [UnityEngine.Input]::Down[2] = $middle
}
function Click-Loot {
    [LootClickBridge].GetMethod('Update', $flags).Invoke($bridge, @()) | Out-Null
}

$flags = [System.Reflection.BindingFlags]'Instance,NonPublic'
$bridge = [LootClickBridge]::new()
$runtime = [LootModuleRuntime]::new()
$camera = [UnityEngine.Camera]::new()
[LootClickBridge].GetField('moduleRuntime', $flags).SetValue($bridge, $runtime)
[LootClickBridge].GetField('inputCamera', $flags).SetValue($bridge, $camera)
[UnityEngine.TestObjects]::Bridge = $bridge
[LocalPlayerProvider]::Instance = [LocalPlayerProvider]::new()
[LocalPlayerProvider]::Instance.PlayerTransform = [UnityEngine.Transform]::new()
[UnityEngine.EventSystems.EventSystem]::current =
    [UnityEngine.EventSystems.EventSystem]::new()
$view = [WorldLootView]::new()
$hit = [UnityEngine.RaycastHit]::new()
$hit.distance = 1
$hit.collider = [UnityEngine.Collider]::new()
$hit.collider.View = $view
[UnityEngine.Physics]::Hits = [UnityEngine.RaycastHit[]]@($hit)

Set-Buttons $false $false $true
Click-Loot
Check ($runtime.Requests -eq 1 -and $runtime.LastView -eq $view) `
    'Middle click must keep using the existing loot interaction.'

Set-Buttons $false $true $false
Click-Loot
Check ($runtime.Requests -eq 2 -and $runtime.LastView -eq $view) `
    'Right click on a valid loot view must use the same interaction.'

[UnityEngine.Physics]::Hits = [UnityEngine.RaycastHit[]]@()
$before = $runtime.Requests
Click-Loot
Check ($runtime.Requests -eq $before) `
    'Right click without a loot target must not send a pickup.'

[UnityEngine.Physics]::Hits = [UnityEngine.RaycastHit[]]@($hit)
[UnityEngine.EventSystems.EventSystem]::current.PointerOverUi = $true
Click-Loot
Check ($runtime.Requests -eq $before) `
    'Right click over UI must not send a pickup.'
[UnityEngine.EventSystems.EventSystem]::current.PointerOverUi = $false
[GameplayUiInputGuard]::BlocksGameplayInput = $true
Click-Loot
Check ($runtime.Requests -eq $before) `
    'Right click during blocked gameplay must not send a pickup.'
[GameplayUiInputGuard]::BlocksGameplayInput = $false

Set-Buttons $false $true $true
Click-Loot
Check ($runtime.Requests -eq ($before + 1)) `
    'Middle and right pressed in one frame must request pickup only once.'

$teleporter = [MapTeleporterInteraction]::new()
$position = [UnityEngine.Vector2]::new(0, 0)
Set-Buttons $false $true $false
Check (-not $teleporter.ReadPointer([ref]$position)) `
    'Right click on loot must not also activate a teleporter behind it.'
[UnityEngine.Physics]::Hits = [UnityEngine.RaycastHit[]]@()
Check ($teleporter.ReadPointer([ref]$position) -and
    $position.x -eq 12 -and $position.y -eq 34) `
    'Right click without loot must remain available to teleporters.'
[UnityEngine.Physics]::Hits = [UnityEngine.RaycastHit[]]@($hit)
$bridge.isActiveAndEnabled = $false
Check ($teleporter.ReadPointer([ref]$position)) `
    'A disabled loot bridge must not suppress teleporter clicks.'
$bridge.isActiveAndEnabled = $true
[UnityEngine.EventSystems.EventSystem]::current.PointerOverUi = $true
Check (-not $teleporter.ReadPointer([ref]$position)) `
    'Right click over UI must not activate a teleporter.'

if ($failures.Count -gt 0) {
    foreach ($failure in $failures) { Write-Output "FAIL: $failure" }
    throw 'Right-click loot routing does not yet meet the acceptance tests.'
}
Write-Output 'PASS: right/middle loot routing, UI guard, single request and teleporter priority.'
