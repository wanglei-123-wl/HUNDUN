$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/PlayerTrade/Presentation/PlayerTradeMarketToggleController.cs') -Raw

$stubs = @'
namespace UnityEngine
{
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public sealed class DisallowMultipleComponent : System.Attribute {}
    [System.AttributeUsage(System.AttributeTargets.Field)]
    public sealed class SerializeField : System.Attribute {}
    [System.AttributeUsage(System.AttributeTargets.Field)]
    public sealed class InspectorNameAttribute : System.Attribute
    {
        public InspectorNameAttribute(string name) {}
    }
    [System.AttributeUsage(System.AttributeTargets.Field)]
    public sealed class HeaderAttribute : System.Attribute
    {
        public HeaderAttribute(string name) {}
    }
    public enum KeyCode { V }
    public sealed class Transform {}
    public sealed class GameObject
    {
        public bool activeSelf;
        public bool activeInHierarchy => activeSelf;
        public Transform transform = new Transform();
        public void SetActive(bool active) { activeSelf = active; }
    }
    public class MonoBehaviour
    {
        public GameObject gameObject = new GameObject();
        public bool enabled = true;
    }
    public static class Input
    {
        public static bool pressed;
        public static bool GetKeyDown(KeyCode key) => pressed && key == KeyCode.V;
    }
    public static class Debug
    {
        public static void LogError(string message, object context) {}
    }
}
public static class GameplayUiInputGuard
{
    public static bool IsEditingText;
    public static bool BlocksWindowShortcuts => IsEditingText;
}
'@

Add-Type -TypeDefinition ($source + "`n" + $stubs) -IgnoreWarnings | Out-Null

function Invoke-Private($target, [string]$method) {
    $flags = [System.Reflection.BindingFlags]'Instance,NonPublic'
    $target.GetType().GetMethod($method, $flags).Invoke($target, @()) | Out-Null
}

$controller = [Game.PlayerTrade.Presentation.PlayerTradeMarketToggleController]::new()
$market = [UnityEngine.GameObject]::new()
$field = $controller.GetType().GetField(
    'marketPanel', [System.Reflection.BindingFlags]'Instance,NonPublic')
$field.SetValue($controller, $market)

Invoke-Private $controller 'Awake'
if ($controller.IsVisible) { throw 'Market must respect an initially hidden scene object.' }
Invoke-Private $controller 'OnEnable'
if ([Game.PlayerTrade.Presentation.PlayerTradeMarketToggleController]::ActiveMarket -ne $controller) {
    throw 'Market controller must register while its panel is hidden.'
}
$market.SetActive($true)
if (!$controller.IsVisible -or $controller.MarketRoot -ne $market.transform) {
    throw 'Manually enabling the market must update visibility and hit root.'
}
Invoke-Private $controller 'Awake'
if (!$controller.IsVisible) { throw 'Startup must not override an enabled scene object.' }
$controller.Toggle()
if ($controller.IsVisible) { throw 'Toggle must hide the market.' }
$controller.Show()
if (!$controller.IsVisible) { throw 'Show must open the market.' }
$controller.Hide()
if ($controller.IsVisible) { throw 'Hide must close the market.' }
[UnityEngine.Input]::pressed = $true
[GameplayUiInputGuard]::IsEditingText = $true
Invoke-Private $controller 'Update'
if ($controller.IsVisible) { throw 'V must not open the market while typing.' }
[GameplayUiInputGuard]::IsEditingText = $false
Invoke-Private $controller 'Update'
if (!$controller.IsVisible) { throw 'V must reopen the market while it is hidden.' }
Invoke-Private $controller 'OnDisable'
if ([Game.PlayerTrade.Presentation.PlayerTradeMarketToggleController]::ActiveMarket -ne $null) {
    throw 'Disabling the controller must clear its active reference.'
}

Write-Output 'PASS: market initial state, manual visibility, V key, text focus and input root.'
