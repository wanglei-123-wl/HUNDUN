$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Alchemy/Presentation/AlchemyWindowToggle.cs') -Raw
$meta = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Alchemy/Presentation/AlchemyWindowToggle.cs.meta') -Raw
$scene = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scenes/WorldShell.unity') -Raw
$panel = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Alchemy/Presentation/AlchemyPanelView.cs') -Raw

$stubs = @'
namespace UnityEngine
{
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public sealed class DisallowMultipleComponent : System.Attribute {}
    [System.AttributeUsage(System.AttributeTargets.Field)]
    public sealed class SerializeField : System.Attribute {}
    public sealed class GameObject
    {
        public bool activeSelf;
        public int setActiveCalls;
        public void SetActive(bool value) { activeSelf = value; setActiveCalls++; }
    }
    public class MonoBehaviour
    {
        public GameObject gameObject = new GameObject();
    }
    public enum KeyCode { X }
    public static class Input
    {
        public static bool pressed;
        public static bool GetKeyDown(KeyCode key) => pressed && key == KeyCode.X;
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
function Scene-Block([string]$kind, [string]$id) {
    $pattern = "(?s)--- !u!$kind &$id`r?`n(?<body>.*?)(?=`r?`n--- !u!|$)"
    $matches = [regex]::Matches($scene, $pattern)
    Assert-True ($matches.Count -eq 1) "Scene block $kind/$id missing or duplicated."
    return $matches[0].Groups['body'].Value
}

$controller = [Game.Alchemy.Presentation.AlchemyWindowToggle]::new()
$window = [UnityEngine.GameObject]::new()
$field = $controller.GetType().GetField('alchemyWindow',
    [System.Reflection.BindingFlags]'Instance,NonPublic')
$field.SetValue($controller, $window)
$update = $controller.GetType().GetMethod('Update',
    [System.Reflection.BindingFlags]'Instance,NonPublic')
Assert-True (-not $controller.IsOpen -and $window.setActiveCalls -eq 0) `
    'The controller must respect an initially hidden alchemy window.'
[UnityEngine.Input]::pressed = $true
$update.Invoke($controller, @()) | Out-Null
Assert-True ($controller.IsOpen -and $window.setActiveCalls -eq 1) `
    'X must open the entire alchemy window.'
[GameplayUiInputGuard]::IsEditingText = $true
$update.Invoke($controller, @()) | Out-Null
Assert-True ($controller.IsOpen -and $window.setActiveCalls -eq 1) `
    'Typing X in a text field must not close the window.'
[GameplayUiInputGuard]::IsEditingText = $false
$update.Invoke($controller, @()) | Out-Null
Assert-True (-not $controller.IsOpen -and $window.setActiveCalls -eq 2) `
    'X must close the alchemy window and remain available for reopening.'
$update.Invoke($controller, @()) | Out-Null
Assert-True ($controller.IsOpen -and $window.setActiveCalls -eq 3) `
    'X must reopen the alchemy window after it was disabled.'
$field.SetValue($controller, $controller.gameObject)
$controller.Toggle()
Assert-True ($controller.gameObject.setActiveCalls -eq 0) `
    'A misbound controller must never disable its own host.'

$guid = [regex]::Match($meta, '(?m)^guid: ([0-9a-f]{32})$').Groups[1].Value
Assert-True ($guid.Length -eq 32) 'Toggle script GUID must exist.'
$worldHost = Scene-Block '1' '860000000000500001'
$worldToggle = Scene-Block '114' '860000000000500004'
$worldWindow = Scene-Block '1' '827217722'
$worldWindowRoot = Scene-Block '224' '827217726'
$worldPanel = Scene-Block '114' '860000000000100003'
$worldRoots = Scene-Block '1660057539' '9223372036854775807'
Assert-True ($worldHost.Contains('m_IsActive: 1') -and
    $worldHost.Contains('component: {fileID: 860000000000500003}') -and
    $worldHost.Contains('component: {fileID: 860000000000500004}') -and
    $worldRoots.Contains('{fileID: 860000000000500002}') -and
    $worldToggle.Contains('m_GameObject: {fileID: 860000000000500001}') -and
    $worldToggle.Contains("guid: $guid") -and
    $worldToggle.Contains('alchemyWindow: {fileID: 827217722}') -and
    $worldWindow.Contains('m_IsActive: 0') -and
    $worldWindowRoot.Contains('m_Father: {fileID: 0}') -and
    $worldPanel.Contains('m_GameObject: {fileID: 827217722}') -and
    $panel.Contains('private void OnEnable()') -and
    $panel.Contains('RefreshQueries();') -and
    ([regex]::Matches($scene, "guid: $guid")).Count -eq 1) `
    'WorldShell must bind exactly one X toggle on the active shortcut host.'

Write-Output 'PASS: alchemy X key, text focus and scene window binding.'
