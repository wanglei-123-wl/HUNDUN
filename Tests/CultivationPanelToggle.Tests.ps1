$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Cultivation/Presentation/CultivationPanelToggle.cs') -Raw
$scene = Get-Content -LiteralPath (Join-Path $root 'Assets/Scenes/WorldShell.unity') -Raw
$meta = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Cultivation/Presentation/CultivationPanelToggle.cs.meta') -Raw

$stubs = @'
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
        public bool isActiveAndEnabled;
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
'@
Add-Type -TypeDefinition ($source + "`n" + $stubs) -IgnoreWarnings | Out-Null

function Invoke-Lifecycle($target, [string]$method) {
    $flags = [System.Reflection.BindingFlags]'Instance,NonPublic'
    $target.GetType().GetMethod($method, $flags).Invoke($target, @()) | Out-Null
}
function Assert-SceneBlock([string]$kind, [string]$id, [string]$expected) {
    $pattern = "(?s)--- !u!$kind &$id`r?`n(?<body>.*?)(?=`r?`n--- !u!)"
    $matches = [regex]::Matches($scene, $pattern)
    if ($matches.Count -ne 1 -or $matches[0].Groups['body'].Value -notmatch "(?s)$expected") {
        throw "Scene block $kind/$id missing or incorrectly wired."
    }
}

$controller = [Game.Cultivation.Presentation.CultivationPanelToggle]::new()
$cultivate = [UnityEngine.UI.Button]::new()
$comprehension = [UnityEngine.UI.Button]::new()
$skill = [UnityEngine.UI.Button]::new()
$pages = @([UnityEngine.GameObject]::new(), [UnityEngine.GameObject]::new(), [UnityEngine.GameObject]::new())
$highlights = @([UnityEngine.GameObject]::new(), [UnityEngine.GameObject]::new(), [UnityEngine.GameObject]::new())
$buttons = @($comprehension, $cultivate, $skill)
$flags = [System.Reflection.BindingFlags]'Instance,NonPublic'
$bindings = @{
    comprehensionButton = $comprehension; comprehensionContent = $pages[0]; comprehensionSelected = $highlights[0]
    cultivateButton = $cultivate; cultivationContent = $pages[1]; cultivationSelected = $highlights[1]
    skillButton = $skill; skillContent = $pages[2]; skillSelected = $highlights[2]
}
foreach ($name in $bindings.Keys) {
    $controller.GetType().GetField($name, $flags).SetValue($controller, $bindings[$name])
}
function Assert-Selected([int]$selected) {
    for ($index = 0; $index -lt $pages.Count; $index++) {
        $expected = $index -eq $selected
        if ($pages[$index].activeSelf -ne $expected -or $highlights[$index].activeSelf -ne $expected) {
            throw "Page and highlight must match selected tab $selected (tab $index)."
        }
    }
}

Invoke-Lifecycle $controller 'OnEnable'
Assert-Selected 0
foreach ($index in @(1, 2, 0, 2, 1)) {
    $buttons[$index].onClick.Invoke()
    Assert-Selected $index
}
$setActiveCalls = $pages[1].setActiveCalls
$cultivate.onClick.Invoke()
Assert-Selected 1
if ($pages[1].setActiveCalls -ne $setActiveCalls -or
    [Game.Cultivation.Runtime.RefiningSessionRuntime]::Instance.refreshes -ne 2) {
    throw 'Only entering the refining tab should activate the panel and refresh refining.'
}
Invoke-Lifecycle $controller 'OnDisable'
$comprehension.onClick.Invoke()
$skill.onClick.Invoke()
Assert-Selected 1
Invoke-Lifecycle $controller 'OnEnable'
Assert-Selected 0
$skill.onClick.Invoke()
Assert-Selected 2
Invoke-Lifecycle $controller 'OnDisable'

Invoke-Lifecycle $controller 'OpenSkills'
Invoke-Lifecycle $controller 'OnEnable'
Assert-Selected 2
Invoke-Lifecycle $controller 'OnDisable'
Invoke-Lifecycle $controller 'OnEnable'
Assert-Selected 0
Invoke-Lifecycle $controller 'OnDisable'

$guid = [regex]::Match($meta, '(?m)^guid: ([0-9a-f]{32})$').Groups[1].Value
if (-not $guid) { throw 'Controller script GUID missing.' }
Assert-SceneBlock '1' '112115721' 'component: \{fileID: 2200000001\}'
Assert-SceneBlock '114' '2200000001' (
    "guid: $guid.*?" +
    'comprehensionButton: \{fileID: 1910929965\}.*?comprehensionContent: \{fileID: 1581756384\}.*?' +
    'comprehensionSelected: \{fileID: 1832157816\}.*?' +
    'cultivateButton: \{fileID: 1802858645\}.*?cultivationContent: \{fileID: 904205820\}.*?' +
    'cultivationSelected: \{fileID: 794189225\}.*?' +
    'skillButton: \{fileID: 209222904\}.*?skillContent: \{fileID: 1266172302\}.*?' +
    'skillSelected: \{fileID: 779501687\}')
foreach ($id in @('1581756384', '1832157816')) {
    Assert-SceneBlock '1' $id 'm_IsActive: 1'
}
foreach ($id in @('904205820', '794189225', '1266172302', '779501687')) {
    Assert-SceneBlock '1' $id 'm_IsActive: 0'
}

Write-Output 'PASS: inner cultivation navigation and scene wiring.'
