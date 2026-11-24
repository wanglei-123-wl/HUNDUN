$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/锻造/锻造窗口开关.cs') -Raw

$stubs = @'
namespace UnityEngine
{
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public sealed class DisallowMultipleComponent : System.Attribute {}
    [System.AttributeUsage(System.AttributeTargets.Field)]
    public sealed class SerializeField : System.Attribute {}
    public sealed class GameObject
    {
        public bool activeSelf = true;
        public int setActiveCalls;
        public void SetActive(bool value) { activeSelf = value; setActiveCalls++; }
    }
    public class MonoBehaviour
    {
        public GameObject gameObject = new GameObject();
    }
    public enum KeyCode { X, Z }
    public static class Input
    {
        public static bool down;
        public static KeyCode key;
        public static bool GetKeyDown(KeyCode value) => down && key == value;
    }
}
public static class GameplayUiInputGuard
{
    public static bool IsEditingText;
    public static bool BlocksWindowShortcuts => IsEditingText;
}
'@
Add-Type -TypeDefinition ($source + "`n" + $stubs) -IgnoreWarnings | Out-Null

function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
}

$controller = [Game.Forging.Presentation.锻造窗口开关]::new()
$window = [UnityEngine.GameObject]::new()
$field = $controller.GetType().GetField('forgingWindow',
    [System.Reflection.BindingFlags]'Instance,NonPublic')
$field.SetValue($controller, $window)
$awake = $controller.GetType().GetMethod('Awake',
    [System.Reflection.BindingFlags]'Instance,NonPublic')
$update = $controller.GetType().GetMethod('Update',
    [System.Reflection.BindingFlags]'Instance,NonPublic')
$awake.Invoke($controller, @()) | Out-Null
Assert-True (-not $controller.IsOpen -and $window.setActiveCalls -eq 1) `
    'Forging must start hidden even when enabled for scene preview.'

[UnityEngine.Input]::down = $true
[UnityEngine.Input]::key = [UnityEngine.KeyCode]::Z
$update.Invoke($controller, @()) | Out-Null
Assert-True ($controller.IsOpen -and $window.setActiveCalls -eq 2) `
    'Z must open forging.'

[GameplayUiInputGuard]::IsEditingText = $true
$update.Invoke($controller, @()) | Out-Null
Assert-True ($controller.IsOpen -and $window.setActiveCalls -eq 2) `
    'Typing Z must not close forging.'

[GameplayUiInputGuard]::IsEditingText = $false
[UnityEngine.Input]::key = [UnityEngine.KeyCode]::X
$update.Invoke($controller, @()) | Out-Null
Assert-True ($controller.IsOpen -and $window.setActiveCalls -eq 2) `
    'Alchemy X must not toggle forging.'

[UnityEngine.Input]::key = [UnityEngine.KeyCode]::Z
$update.Invoke($controller, @()) | Out-Null
Assert-True (-not $controller.IsOpen -and $window.setActiveCalls -eq 3) `
    'Z must close forging.'

$field.SetValue($controller, $controller.gameObject)
$controller.Toggle()
Assert-True ($controller.gameObject.setActiveCalls -eq 0) `
    'A misbound controller must not disable its own host.'

$scene = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scenes/WorldShell.unity') -Raw
$controllerHost = [regex]::Match($scene,
    '(?s)--- !u!1 &860000000000500001\r?\nGameObject:.*?(?=--- !u!)').Value
$transform = [regex]::Match($scene,
    '(?s)--- !u!4 &860000000000500002\r?\nTransform:.*?(?=--- !u!)').Value
$binding = [regex]::Match($scene,
    '(?s)--- !u!114 &860000000000500003\r?\nMonoBehaviour:.*?(?=--- !u!)').Value
$window = [regex]::Match($scene,
    '(?s)--- !u!1 &239332388\r?\nGameObject:.*?(?=--- !u!)').Value
$roots = [regex]::Match($scene,
    '(?s)--- !u!1660057539 &9223372036854775807\r?\nSceneRoots:.*').Value
Assert-True ($controllerHost.Contains('m_IsActive: 1') -and
    $controllerHost.Contains('860000000000500002') -and
    $controllerHost.Contains('860000000000500003') -and
    $transform.Contains('m_Father: {fileID: 0}') -and
    $roots.Contains('{fileID: 860000000000500002}') -and
    $window.Contains('m_IsActive: 0')) `
    'The Z controller must stay active as a scene root while forging starts hidden.'
Assert-True ($binding.Contains(
        'guid: 708ef87b5b528a54bafba080d578a243') -and
    $binding.Contains('m_GameObject: {fileID: 860000000000500001}') -and
    $binding.Contains('forgingWindow: {fileID: 239332388}') -and
    ([regex]::Matches($scene,
        'guid: 708ef87b5b528a54bafba080d578a243')).Count -eq 1) `
    'WorldShell must bind exactly one Z controller to its forging window.'

Write-Output '锻造 Z 键、输入焦点和初始隐藏检查通过'
