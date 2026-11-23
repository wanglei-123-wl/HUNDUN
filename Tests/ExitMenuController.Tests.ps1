$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/UI/ExitMenuController.cs') -Raw
$scene = Get-Content -LiteralPath (Join-Path $root 'Assets/Scenes/WorldShell.unity') -Raw
$meta = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/UI/ExitMenuController.cs.meta') -Raw
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
    public class GameObject
    {
        public GameObject parent;
        public bool activeSelf;
        public bool activeInHierarchy { get { return activeSelf && (parent == null || parent.activeInHierarchy); } }
        public int changes;
        public void SetActive(bool active) { activeSelf = active; changes++; }
    }
    public class MonoBehaviour { public GameObject gameObject = new GameObject(); }
    public enum KeyCode { Escape }
    public static class Input
    {
        public static bool escape;
        public static bool GetKeyDown(KeyCode key) { return key == KeyCode.Escape && escape; }
    }
}
namespace UnityEngine.UI
{
    public sealed class Button
    {
        public bool interactable = true;
        public readonly ClickEvent onClick = new ClickEvent();
        public sealed class ClickEvent
        {
            private readonly System.Collections.Generic.List<System.Action> listeners =
                new System.Collections.Generic.List<System.Action>();
            public int Count { get { return listeners.Count; } }
            public void AddListener(System.Action action) { listeners.Add(action); }
            public void RemoveListener(System.Action action) { listeners.Remove(action); }
            public void Invoke() { foreach (var action in listeners.ToArray()) action(); }
        }
    }
}
public sealed class GameSession
{
    public int ReturnCalls, QuitCalls;
    public bool AcceptExit;
    public bool ReturnToCharacterSelect() { ReturnCalls++; return AcceptExit; }
    public bool QuitGame() { QuitCalls++; return AcceptExit; }
}
public static class GameplayUiInputGuard
{
    public static bool BlocksWindowShortcuts;
    public static readonly System.Collections.Generic.HashSet<UnityEngine.GameObject> Modals =
        new System.Collections.Generic.HashSet<UnityEngine.GameObject>();
    public static bool BlocksGameplayInput
    {
        get
        {
            foreach (var modal in Modals) if (modal.activeInHierarchy) return true;
            return false;
        }
    }
    public static void RegisterModal(UnityEngine.GameObject window) { if (window != null) Modals.Add(window); }
    public static void UnregisterModal(UnityEngine.GameObject window) { if (window != null) Modals.Remove(window); }
}
'@
Add-Type -TypeDefinition ($source + "`n" + $stubs) -IgnoreWarnings -WarningAction SilentlyContinue | Out-Null

function Require([bool] $condition, [string] $message) {
    if (-not $condition) { throw $message }
}
function Scene-Block([string] $kind, [string] $id) {
    $match = [regex]::Matches($scene, "(?ms)^--- !u!$kind &$id\r?`n.*?(?=^--- !u!|\z)")
    Require ($match.Count -eq 1) "Missing or duplicate scene block: $id"
    return $match[0].Value
}

$flags = [Reflection.BindingFlags]'Instance,NonPublic'
$controller = [Game.UI.ExitMenuController]::new()
$window = [UnityEngine.GameObject]::new()
$content = [UnityEngine.GameObject]::new()
$content.parent = $window
$button = [UnityEngine.UI.Button]::new()
$logoutButton = [UnityEngine.UI.Button]::new()
$quitButton = [UnityEngine.UI.Button]::new()
$session = [GameSession]::new()
$type = $controller.GetType()
$type.GetField('exitWindow', $flags).SetValue($controller, $window)
$type.GetField('menuContent', $flags).SetValue($controller, $content)
$type.GetField('continueButton', $flags).SetValue($controller, $button)
$type.GetField('logoutButton', $flags).SetValue($controller, $logoutButton)
$type.GetField('quitButton', $flags).SetValue($controller, $quitButton)
$type.GetField('gameSession', $flags).SetValue($controller, $session)
$awake = $type.GetMethod('Awake', $flags)
$enable = $type.GetMethod('OnEnable', $flags)
$disable = $type.GetMethod('OnDisable', $flags)
$update = $type.GetMethod('Update', $flags)
$window.SetActive($true)
$awake.Invoke($controller, @()) | Out-Null
$enable.Invoke($controller, @()) | Out-Null
Require (-not $controller.IsOpen -and $button.onClick.Count -eq 1) 'Start hidden and subscribe to Continue.'
Require ([GameplayUiInputGuard]::Modals.Contains($content) -and
    -not [GameplayUiInputGuard]::BlocksGameplayInput) 'The actual menu content must govern modal input blocking.'

$update.Invoke($controller, @()) | Out-Null
Require (-not $controller.IsOpen) 'A frame without Escape must not open the menu.'
[UnityEngine.Input]::escape = $true
$update.Invoke($controller, @()) | Out-Null
Require ($controller.IsOpen -and $content.activeInHierarchy -and
    [GameplayUiInputGuard]::BlocksGameplayInput) 'Escape must show the initially disabled content as well as its root.'
$content.SetActive($false)
Require (-not $controller.IsOpen -and -not [GameplayUiInputGuard]::BlocksGameplayInput) 'Invisible content must not trap gameplay input.'
$update.Invoke($controller, @()) | Out-Null
Require ($controller.IsOpen -and $content.activeInHierarchy) 'Escape must restore hidden content even when its root was already open.'
$changes = $window.changes
$update.Invoke($controller, @()) | Out-Null
Require ($controller.IsOpen -and $window.changes -eq $changes) 'Another Escape must keep the menu open.'
$button.onClick.Invoke()
Require (-not $controller.IsOpen -and -not $content.activeInHierarchy -and
    -not [GameplayUiInputGuard]::BlocksGameplayInput) 'Continue must hide the whole menu and release input blocking.'
$update.Invoke($controller, @()) | Out-Null
Require $controller.IsOpen 'The menu must reopen after Continue hides it.'
$disable.Invoke($controller, @()) | Out-Null
Require (-not $controller.IsOpen -and $button.onClick.Count -eq 0 -and
    -not [GameplayUiInputGuard]::Modals.Contains($content)) 'Disabling the controller must close and unsubscribe cleanly.'
$enable.Invoke($controller, @()) | Out-Null
Require ($button.onClick.Count -eq 1) 'Re-enabling must not duplicate the Continue listener.'
[GameplayUiInputGuard]::BlocksWindowShortcuts = $true
$update.Invoke($controller, @()) | Out-Null
Require (-not $controller.IsOpen) 'Respect the existing text-input and loading shortcut guard.'
[GameplayUiInputGuard]::BlocksWindowShortcuts = $false
$update.Invoke($controller, @()) | Out-Null
$button.onClick.Invoke()
Require (-not $controller.IsOpen) 'Continue must work after a disable/enable cycle.'
$disable.Invoke($controller, @()) | Out-Null
$type.GetField('exitWindow', $flags).SetValue($controller, $controller.gameObject)
$controller.Show()
$controller.Hide()
Require ($controller.gameObject.changes -eq 0) 'A bad reference must not disable its own listener host.'
$type.GetField('exitWindow', $flags).SetValue($controller, $window)
$type.GetField('menuContent', $flags).SetValue($controller, $null)
$controller.Show()
Require (-not $window.activeSelf) 'Missing menu content must not open an empty modal root.'
$type.GetField('menuContent', $flags).SetValue($controller, $content)

foreach ($action in @('logout', 'quit')) {
    $controller = [Game.UI.ExitMenuController]::new()
    $type.GetField('exitWindow', $flags).SetValue($controller, $window)
    $type.GetField('menuContent', $flags).SetValue($controller, $content)
    $type.GetField('continueButton', $flags).SetValue($controller, $button)
    $type.GetField('logoutButton', $flags).SetValue($controller, $logoutButton)
    $type.GetField('quitButton', $flags).SetValue($controller, $quitButton)
    $session = [GameSession]::new()
    $type.GetField('gameSession', $flags).SetValue($controller, $session)
    $exit = if ($action -eq 'logout') { $logoutButton } else { $quitButton }
    $enable.Invoke($controller, @()) | Out-Null
    $controller.Show()
    $exit.onClick.Invoke()
    Require ($button.interactable -and $logoutButton.interactable -and $quitButton.interactable) 'A rejected exit must leave buttons available.'
    $session.AcceptExit = $true
    $exit.onClick.Invoke()
    $button.onClick.Invoke()
    $logoutButton.onClick.Invoke()
    $quitButton.onClick.Invoke()
    Require ($controller.IsOpen -and -not $button.interactable -and -not $logoutButton.interactable -and
        -not $quitButton.interactable) 'A pending exit must keep the modal open and disable all three buttons.'
    $expectedReturnCalls = if ($action -eq 'logout') { 2 } else { 0 }
    Require ($session.ReturnCalls -eq $expectedReturnCalls -and $session.QuitCalls -eq (2 - $expectedReturnCalls)) 'Route each action once after it is accepted.'
    $disable.Invoke($controller, @()) | Out-Null
    Require ($logoutButton.onClick.Count -eq 0 -and $quitButton.onClick.Count -eq 0) 'Remove both exit listeners on disable.'
    $button.interactable = $logoutButton.interactable = $quitButton.interactable = $true
}

$guid = [regex]::Match($meta, '(?m)^guid: ([0-9a-f]{32})\r?$').Groups[1].Value
Require ($guid.Length -eq 32) 'Missing script GUID.'
$shortcutHost = Scene-Block '1' '860000000000500001'
$binding = Scene-Block '114' '860000000000500006'
$menu = Scene-Block '1' '1739274038'
$background = Scene-Block '1' '471584290'
$backgroundTransform = Scene-Block '224' '471584291'
$continue = Scene-Block '114' '1381009768'
Require ($shortcutHost -match 'm_IsActive: 1' -and $shortcutHost -match 'component: \{fileID: 860000000000500006\}') 'The listener host must stay active.'
Require ($binding -match "guid: $guid" -and
    $binding -match 'm_GameObject: \{fileID: 860000000000500001\}' -and
    $binding -match 'exitWindow: \{fileID: 1739274038\}' -and
    $binding -match 'menuContent: \{fileID: 471584290\}' -and
    $binding -match 'continueButton: \{fileID: 1381009768\}' -and
    $background -match 'm_IsActive: 1' -and
    $backgroundTransform -match 'm_Father: \{fileID: 1739274042\}' -and
    $continue -match 'm_Interactable: 1') 'Bind enabled menu content under the outer window; Awake must hide the window even if the editor preview is enabled.'
$logout = Scene-Block '114' '271775295'
$quit = Scene-Block '114' '1644284682'
$worldSession = Scene-Block '114' '2108349142'
Require ($binding -match 'logoutButton: \{fileID: 271775295\}' -and
    $binding -match 'quitButton: \{fileID: 1644284682\}' -and
    $binding -match 'gameSession: \{fileID: 2108349142\}' -and
    $logout -match 'm_GameObject: \{fileID: 271775293\}' -and
    $quit -match 'm_GameObject: \{fileID: 1644284680\}' -and
    $worldSession -match 'm_Enabled: 1') 'Bind Logout and Quit to the real WorldShell buttons and session.'
Write-Output 'PASS: ESC visibility and input, Continue, logout and quit routing, duplicate prevention, and scene wiring.'
