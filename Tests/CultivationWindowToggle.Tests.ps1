$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Cultivation/Presentation/CultivationWindowToggle.cs') -Raw
$innerSource = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Cultivation/Presentation/CultivationPanelToggle.cs') -Raw
$scene = Get-Content -LiteralPath (Join-Path $root 'Assets/Scenes/WorldShell.unity') -Raw
$meta = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Cultivation/Presentation/CultivationWindowToggle.cs.meta') -Raw

$stubs = @'
public sealed class SkillLearnResult
{
    public bool success;
    public string errorCode;
    public SkillSnapshotEntry learnedSkill;
}
public sealed class SkillSnapshotEntry { public string skillId; }
public sealed class GameSession
{
    public event System.Action<SkillLearnResult> SkillLearnCompleted;
    public void Complete(SkillLearnResult result) => SkillLearnCompleted?.Invoke(result);
}
public sealed class LearnedSkillListView
{
    public string selected;
    public void SelectSkill(string skillId) { selected = skillId; }
}
namespace Game.Inventory
{
    public sealed class InventoryModuleRuntime
    {
        public bool closed;
        public void Close() { closed = true; }
    }
}
namespace Game.Cultivation.Presentation
{
    public sealed class CultivationPanelToggle
    {
        public bool skillsOpen;
        public void OpenSkills() { skillsOpen = true; }
    }
}
namespace Game.UI.Notice
{
    public static class GameNoticeService { public static void Show(string value) {} }
}
namespace Game.Cultivation.Runtime
{
    public sealed class RefiningSessionRuntime
    {
        public static RefiningSessionRuntime Instance = new RefiningSessionRuntime();
        public int refreshes;
        public void Refresh() { refreshes++; }
    }
}
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
    public enum KeyCode { C }
    public static class Input
    {
        public static bool pressed;
        public static bool GetKeyDown(KeyCode key) => pressed && key == KeyCode.C;
    }
}
namespace UnityEngine.UI
{
    public sealed class Button
    {
        public readonly ClickEvent onClick = new ClickEvent();
    }
    public sealed class ClickEvent
    {
        private event System.Action listeners;
        public void AddListener(System.Action callback) { listeners += callback; }
        public void RemoveListener(System.Action callback) { listeners -= callback; }
        public void Invoke() { listeners?.Invoke(); }
    }
}
public static class GameplayUiInputGuard
{
    public static bool IsEditingText;
    public static bool BlocksWindowShortcuts => IsEditingText;
}
'@
Add-Type -TypeDefinition ($source + "`n" + $stubs) -IgnoreWarnings | Out-Null

function Invoke-Lifecycle($target, [string]$method) {
    $flags = [System.Reflection.BindingFlags]'Instance,NonPublic'
    $target.GetType().GetMethod($method, $flags).Invoke($target, @()) | Out-Null
}
function Get-SceneBlock([string]$kind, [string]$id) {
    $pattern = "(?s)--- !u!$kind &$id`r?`n(?<body>.*?)(?=`r?`n--- !u!)"
    $matches = [regex]::Matches($scene, $pattern)
    if ($matches.Count -ne 1) { throw "Scene block $kind/$id missing or duplicated." }
    return $matches[0].Groups['body'].Value
}
function Assert-Match([string]$source, [string]$pattern, [string]$message) {
    if ($source -notmatch "(?s)$pattern") { throw $message }
}

$controller = [Game.Cultivation.Presentation.CultivationWindowToggle]::new()
$window = [UnityEngine.GameObject]::new()
$button = [UnityEngine.UI.Button]::new()
$flags = [System.Reflection.BindingFlags]'Instance,NonPublic'
$controller.GetType().GetField('cultivationWindow', $flags).SetValue($controller, $window)
$controller.GetType().GetField('hudToggleButton', $flags).SetValue($controller, $button)
Invoke-Lifecycle $controller 'OnEnable'
if ($controller.IsOpen -or $window.setActiveCalls -ne 0) {
    throw 'The entry controller must respect manual scene visibility.'
}
$button.onClick.Invoke()
if (-not $controller.IsOpen -or $window.setActiveCalls -ne 1 -or
    [Game.Cultivation.Runtime.RefiningSessionRuntime]::Instance.refreshes -ne 1) {
    throw 'The external button must activate and refresh the cultivation window.'
}
$button.onClick.Invoke()
if ($controller.IsOpen -or $window.setActiveCalls -ne 2 -or
    [Game.Cultivation.Runtime.RefiningSessionRuntime]::Instance.refreshes -ne 1) {
    throw 'Closing the cultivation window must not refresh refining.'
}
[UnityEngine.Input]::pressed = $true
Invoke-Lifecycle $controller 'Update'
if (-not $controller.IsOpen -or $window.setActiveCalls -ne 3 -or
    [Game.Cultivation.Runtime.RefiningSessionRuntime]::Instance.refreshes -ne 2) {
    throw 'C must reopen and refresh the cultivation window.'
}
[GameplayUiInputGuard]::IsEditingText = $true
Invoke-Lifecycle $controller 'Update'
if (-not $controller.IsOpen -or $window.setActiveCalls -ne 3) {
    throw 'Typing C must not toggle the cultivation window.'
}
[GameplayUiInputGuard]::IsEditingText = $false
Invoke-Lifecycle $controller 'Update'
if ($controller.IsOpen -or $window.setActiveCalls -ne 4) {
    throw 'C must close the entire cultivation window.'
}
[UnityEngine.Input]::pressed = $false
Invoke-Lifecycle $controller 'OnDisable'
$button.onClick.Invoke()
if ($controller.IsOpen -or $window.setActiveCalls -ne 4) {
    throw 'Disabling the host must remove the external button listener.'
}
$window.SetActive($true)
Invoke-Lifecycle $controller 'OnEnable'
if (-not $controller.IsOpen -or $window.setActiveCalls -ne 5) {
    throw 'An initially visible window must remain visible on enable.'
}
Invoke-Lifecycle $controller 'OnDisable'
$controller.GetType().GetField('cultivationWindow', $flags).SetValue(
    $controller, $controller.gameObject)
$controller.Toggle()
if ($controller.gameObject.setActiveCalls -ne 0) {
    throw 'An incorrect self-reference must never disable the controller.'
}

$session = [GameSession]::new()
$window = [UnityEngine.GameObject]::new()
$inventory = [Game.Inventory.InventoryModuleRuntime]::new()
$panels = [Game.Cultivation.Presentation.CultivationPanelToggle]::new()
$skills = [LearnedSkillListView]::new()
$bindings = @{ session = $session; cultivationWindow = $window; inventory = $inventory;
    panels = $panels; learnedSkills = $skills }
foreach ($name in $bindings.Keys) {
    $controller.GetType().GetField($name, $flags).SetValue($controller, $bindings[$name])
}
Invoke-Lifecycle $controller 'OnEnable'
$session.Complete([SkillLearnResult]@{ errorCode = 'skill_already_learned' })
if ($controller.IsOpen -or $inventory.closed -or $panels.skillsOpen) {
    throw 'A failed skill learn must not navigate away from inventory.'
}
$session.Complete([SkillLearnResult]@{ success = $true;
    learnedSkill = [SkillSnapshotEntry]@{ skillId = 'fireball_01' } })
if (-not ($controller.IsOpen -and $inventory.closed -and $panels.skillsOpen) -or
    $skills.selected -ne 'fireball_01') {
    throw 'A confirmed skill learn must open and select the learned skill.'
}
Invoke-Lifecycle $controller 'OnDisable'

if ($innerSource -match 'hudToggleButton|KeyCode\.C|GetKeyDown') {
    throw 'The inner navigation controller must not handle the external entry.'
}
$guid = [regex]::Match($meta, '(?m)^guid: ([0-9a-f]{32})\r?$').Groups[1].Value
if (-not $guid) { throw 'Window controller GUID missing.' }
Assert-Match (Get-SceneBlock '1' '860000000000500001') (
    'component: \{fileID: 860000000000500003\}.*?' +
    'component: \{fileID: 860000000000500004\}.*?' +
    'component: \{fileID: 860000000000500005\}.*?m_IsActive: 1') (
    'WorldShell shortcut host must stay active with Z, X and C controllers.')
Assert-Match (Get-SceneBlock '114' '860000000000500005') (
    "m_GameObject: \{fileID: 860000000000500001\}.*?guid: $guid.*?" +
    'cultivationWindow: \{fileID: 112115721\}.*?' +
    'hudToggleButton: \{fileID: 489239712\}') (
    'WorldShell C controller must bind the outer window and HUD button.')
Assert-Match (Get-SceneBlock '1' '112115721') 'm_IsActive: 0' (
    'WorldShell cultivation window must start hidden.')
Assert-Match (Get-SceneBlock '1' '904205820') 'm_IsActive: 0' (
    'The internal cultivation content must retain its own state.')
Assert-Match (Get-SceneBlock '224' '112115725') (
    'm_Father: \{fileID: 0\}') 'The outer cultivation panel must not contain the host.'
Assert-Match (Get-SceneBlock '1' '489239710') 'm_IsActive: 1' (
    'WorldShell external HUD button must remain active.')
Assert-Match (Get-SceneBlock '114' '489239712') (
    'm_GameObject: \{fileID: 489239710\}.*?' +
    'guid: 4e29b1a8efbd4b44bb3f3716e73f07ff') (
    'WorldShell HUD target must be a Button component.')
if ($scene -notmatch '\{fileID: 860000000000500002\}' -or
    ([regex]::Matches($scene, "guid: $guid")).Count -ne 1) {
    throw 'WorldShell must have exactly one rooted C controller.'
}

Write-Output 'PASS: outer cultivation window entry, C key, text focus and scene wiring.'
