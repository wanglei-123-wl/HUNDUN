$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$interaction = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/MapTeleporterInteraction.cs') -Raw
$pointerMethod = [regex]::Match($interaction,
    '(?s)    private bool TryGetPointer\(out Vector2 position\)\s*\{.*?\r?\n    \}')
if (-not $pointerMethod.Success) { throw 'Map teleporter pointer handler was not found.' }

$stubs = @'
public struct Vector2 {
    public float x, y;
    public Vector2(float x, float y) { this.x = x; this.y = y; }
}
public static class Input {
    public static bool leftDown;
    public static bool rightDown;
    public static Vector2 mousePosition;
    public static bool GetMouseButtonDown(int button) {
        return button == 0 ? leftDown : button == 1 && rightDown;
    }
}
public class EventSystem {
    public static EventSystem current;
    public bool pointerOverUi;
    public bool IsPointerOverGameObject() => pointerOverUi;
}
public sealed class LootClickBridge {
    public bool isActiveAndEnabled = true;
    public bool lootUnderPointer;
    public bool TryGetLootUnderPointer(out object view) {
        view = lootUnderPointer ? new object() : null;
        return lootUnderPointer;
    }
}
public sealed class MapTeleporterInteraction {
    private LootClickBridge lootClickBridge;
    public LootClickBridge bridge = new LootClickBridge();
    private T FindObjectOfType<T>() where T : class => bridge as T;
    public bool ReadPointer(out Vector2 position) => TryGetPointer(out position);
__POINTER_METHOD__
}
'@
$source = $stubs.Replace('__POINTER_METHOD__', $pointerMethod.Value)
Add-Type -TypeDefinition $source -IgnoreWarnings | Out-Null

function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
}

$reader = [MapTeleporterInteraction]::new()
[EventSystem]::current = [EventSystem]::new()
[Input]::mousePosition = [Vector2]::new(12, 34)
[Input]::rightDown = $true
$position = [Vector2]::new(0, 0)
Assert-True ($reader.ReadPointer([ref]$position) -and
    $position.x -eq 12 -and $position.y -eq 34) `
    'Right-clicking the world must supply the mouse pointer position.'
$reader.bridge.lootUnderPointer = $true
Assert-True (-not $reader.ReadPointer([ref]$position)) `
    'Right-clicking loot must not also activate a map teleporter.'
$reader.bridge.lootUnderPointer = $false
[Input]::rightDown = $false
[Input]::leftDown = $true
Assert-True (-not $reader.ReadPointer([ref]$position)) `
    'Left-clicking the world must not activate a map teleporter.'
[Input]::leftDown = $false
[Input]::rightDown = $true
[EventSystem]::current.pointerOverUi = $true
Assert-True (-not $reader.ReadPointer([ref]$position)) `
    'Right-clicking UI must not activate a map teleporter.'
Write-Output 'PASS: PC right-click only, left-click and UI clicks ignored.'
