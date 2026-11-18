$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$scene = Get-Content -LiteralPath (Join-Path $root 'Assets/Scenes/WorldShell.unity') -Raw
$switcherSource = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Chat/UI/ChatPanelSwitcher.cs') -Raw
$addSource = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Chat/UI/添加好友弹窗控制器.cs') -Raw
$requestSource = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Chat/UI/FriendRequestDialogController.cs') -Raw
$lifeSource = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Player/LocalPlayerLifeController.cs') -Raw

function Get-Record([string]$kind, [string]$id) {
    $match = [regex]::Match($scene,
        "(?ms)^--- !u!$kind &$id\r?\n.*?(?=^--- !u!|\z)")
    if (-not $match.Success) { throw "Missing scene record $kind $id" }
    return $match.Value
}
function Assert-SceneState([string]$name, [string]$id,
    [string]$transformId, [bool]$active) {
    $object = Get-Record '1' $id
    $transform = Get-Record '224' $transformId
    $value = if ($active) { '1' } else { '0' }
    if ($object -notmatch "m_IsActive: $value" -or
        $transform -notmatch 'm_LocalScale: \{x: (?!0(?:\.0+)?[,}])[^,]+, y: (?!0(?:\.0+)?[,}])[^,]+, z: (?!0(?:\.0+)?[,}])[^}]+\}') {
        throw "$name must have its intended active state and a nonzero scale."
    }
}

function Assert-CanvasState([string]$name, [string]$objectId,
    [string]$canvasId, [bool]$active) {
    $object = Get-Record '1' $objectId
    $canvas = Get-Record '223' $canvasId
    $value = if ($active) { '1' } else { '0' }
    if ($object -notmatch "m_IsActive: $value" -or
        $object -notmatch "component: \{fileID: $canvasId\}" -or
        $canvas -notmatch "m_GameObject: \{fileID: $objectId\}" -or
        $canvas -notmatch 'm_Enabled: 1' -or
        $canvas -notmatch 'm_RenderMode: 0') {
        throw "$name must have its intended active state and an enabled screen-space canvas."
    }
}

$addRoot = Get-Record '1' '1282344516'
$addTransform = Get-Record '4' '1282344517'
if ($addRoot -notmatch 'm_IsActive: 0' -or
    $addTransform -notmatch 'm_LocalScale: \{x: 1, y: 1, z: 1\}') {
    throw 'Add friend outer dialog must be hidden with a usable scale.'
}
Assert-CanvasState 'Add friend inner canvas' '1890450972' '1890450976' $true
Assert-CanvasState 'Friend request dialog' '36594508' '36594511' $false
Assert-SceneState 'Private chat' '392896514' '392896515' $false
Assert-CanvasState 'Public chat frame' '411326654' '411326658' $true
Assert-SceneState 'Public messages' '512687379' '512687380' $true
Assert-SceneState 'Public input' '1632168009' '1632168010' $true
Assert-CanvasState 'Character panel canvas' '118479039' '118479043' $true
if ((Get-Record '1' '2008553968') -notmatch 'm_IsActive: 0' -or
    (Get-Record '224' '118479040') -notmatch 'm_Father: \{fileID: 2008553969\}') {
    throw 'The character panel canvas must be under its hidden panel root.'
}
Assert-CanvasState 'Market panel' '1523190445' '1523190448' $false
Assert-SceneState 'Death dialog' '978004983' '978004984' $false

$switchBinding = Get-Record '114' '2098661147'
if ($switchBinding -notmatch 'publicContent: \{fileID: 512687379\}' -or
    $switchBinding -notmatch 'publicInput: \{fileID: 1632168009\}' -or
    $switchBinding -notmatch 'privatePanel: \{fileID: 392896514\}' -or
    $switchBinding -notmatch 'publicButton: \{fileID: 1906492698\}' -or
    $switchBinding -notmatch 'privateButton: \{fileID: 1307372577\}') {
    throw 'Public/private switcher scene references are incomplete.'
}
$requestBinding = Get-Record '114' '2180000002'
if ($requestBinding -notmatch 'requestDialog: \{fileID: 36594508\}' -or
    $requestBinding -notmatch 'closeButton: \{fileID: 772474620\}') {
    throw 'Friend request buttons must reference the hidden dialog.'
}
$addBinding = Get-Record '114' '2087384513'
if ($addBinding -notmatch 'guid: 6f37d34bd1a245b5bd2305d7ec3532ca' -or
    $addBinding -notmatch '\{fileID: 669300628\}' -or
    $addBinding -notmatch '\{fileID: 1700599431\}' -or
    $addBinding -notmatch '\{fileID: 1282344516\}' -or
    $addBinding -notmatch '\{fileID: 1890450972\}') {
    throw 'Add friend buttons and canvas must reference the hidden outer dialog.'
}
if ($addSource -match '(?s)private void Awake\(\).*?SetActive\(false\).*?private void OnEnable' -or
    $requestSource -match '(?s)private void Awake\(\).*?SetActive\(false\).*?private void OnEnable' -or
    $switcherSource -match '(?s)private void Awake\(\).*?ShowPublic\(\).*?private void OnEnable') {
    throw 'Dialog and chat startup must preserve scene visibility.'
}
if ($lifeSource -notmatch 'SetDeathDialogVisible\(false\)' -or
    $lifeSource -notmatch 'SetDeathDialogVisible\(true\)' -or
    (Get-Record '114' '2087645007') -notmatch 'deathDialog: \{fileID: 978004983\}') {
    throw 'Death dialog must remain controlled by the life-state controller.'
}

$stubs = @'
namespace UnityEngine
{
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public sealed class DisallowMultipleComponent : System.Attribute {}
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public sealed class RequireComponent : System.Attribute
    {
        public RequireComponent(System.Type type) {}
    }
    [System.AttributeUsage(System.AttributeTargets.Field)]
    public sealed class SerializeField : System.Attribute {}
    [System.AttributeUsage(System.AttributeTargets.Field)]
    public sealed class HeaderAttribute : System.Attribute
    {
        public HeaderAttribute(string label) {}
    }
    public sealed class Transform
    {
        public Transform parent;
        public bool IsChildOf(Transform root)
        {
            for (Transform node = this; node != null; node = node.parent)
                if (node == root) return true;
            return false;
        }
    }
    public sealed class GameObject
    {
        public bool activeSelf;
        public object component;
        public Transform transform = new Transform();
        public void SetActive(bool value) { activeSelf = value; }
    }
    public class MonoBehaviour
    {
        public bool enabled = true;
        public GameObject gameObject = new GameObject();
        public T GetComponent<T>() where T : class => gameObject.component as T;
    }
    public static class Debug
    {
        public static void LogError(string message, object context) {}
    }
}
namespace UnityEngine.UI
{
    public sealed class Button
    {
        public UnityEngine.Transform transform = new UnityEngine.Transform();
        public ClickEvent onClick = new ClickEvent();
    }
    public sealed class ClickEvent
    {
        private event System.Action listeners;
        public void AddListener(System.Action listener) { listeners += listener; }
        public void RemoveListener(System.Action listener) { listeners -= listener; }
        public void Invoke() { listeners?.Invoke(); }
    }
}
public static class GameplayUiInputGuard
{
    public static readonly System.Collections.Generic.HashSet<UnityEngine.GameObject> Registered =
        new System.Collections.Generic.HashSet<UnityEngine.GameObject>();
    public static void RegisterModal(UnityEngine.GameObject dialog) { Registered.Add(dialog); }
    public static void UnregisterModal(UnityEngine.GameObject dialog) { Registered.Remove(dialog); }
}
'@
$dialogSources = (($addSource + "`n" + $requestSource) -replace
    '(?m)^using [^\r\n]+;\r?\n', '')
Add-Type -TypeDefinition ($switcherSource + "`n" + $dialogSources + "`n" + $stubs) `
    -IgnoreWarnings | Out-Null

$switcher = [Game.Chat.UI.ChatPanelSwitcher]::new()
$publicButton = [UnityEngine.UI.Button]::new()
$privateButton = [UnityEngine.UI.Button]::new()
$publicContent = [UnityEngine.GameObject]::new()
$publicInput = [UnityEngine.GameObject]::new()
$privatePanel = [UnityEngine.GameObject]::new()
$sharedParent = [UnityEngine.Transform]::new()
$publicContent.transform.parent = $sharedParent
$publicInput.transform.parent = $sharedParent
$publicContent.SetActive($true)
$publicInput.SetActive($true)
$privatePanel.SetActive($false)
$flags = [System.Reflection.BindingFlags]'Instance,NonPublic'
foreach ($entry in @(
    @('publicButton', $publicButton),
    @('privateButton', $privateButton),
    @('publicContent', $publicContent),
    @('publicInput', $publicInput),
    @('privatePanel', $privatePanel)
)) {
    $switcher.GetType().GetField($entry[0], $flags).SetValue($switcher, $entry[1])
}
$switcher.GetType().GetMethod('Awake', $flags).Invoke($switcher, @()) | Out-Null
if (!$publicContent.activeSelf -or !$publicInput.activeSelf -or $privatePanel.activeSelf) {
    throw 'Switcher startup must preserve the Inspector selection.'
}
$switcher.GetType().GetMethod('OnEnable', $flags).Invoke($switcher, @()) | Out-Null
$privateButton.onClick.Invoke()
if ($publicContent.activeSelf -or $publicInput.activeSelf -or !$privatePanel.activeSelf) {
    throw 'Private mode must hide both public messages and public input.'
}
$publicButton.onClick.Invoke()
if (!$publicContent.activeSelf -or !$publicInput.activeSelf -or $privatePanel.activeSelf) {
    throw 'Public mode must restore both regions and hide private chat.'
}
$switcher.GetType().GetMethod('OnDisable', $flags).Invoke($switcher, @()) | Out-Null
$privateButton.onClick.Invoke()
if (!$publicContent.activeSelf -or !$publicInput.activeSelf -or $privatePanel.activeSelf) {
    throw 'Disabled switcher must not keep handling button clicks.'
}

$add = [Game.Chat.UI.添加好友弹窗控制器]::new()
$addButton = [UnityEngine.UI.Button]::new()
$addClose = [UnityEngine.UI.Button]::new()
$addDialog = [UnityEngine.GameObject]::new()
$addCanvas = [UnityEngine.GameObject]::new()
$addCanvas.transform.parent = $addDialog.transform
$addClose.transform.parent = $addCanvas.transform
$addCanvas.SetActive($true)
foreach ($entry in @(
    @('添加按钮', $addButton),
    @('搜索玩家界面关闭按钮', $addClose),
    @('添加好友弹窗', $addDialog),
    @('添加好友弹窗画布', $addCanvas)
)) {
    $add.GetType().GetField($entry[0], $flags).SetValue($add, $entry[1])
}
$add.GetType().GetMethod('Awake', $flags).Invoke($add, @()) | Out-Null
if ($addDialog.activeSelf) { throw 'Add friend startup must not open the dialog.' }
$add.GetType().GetMethod('OnEnable', $flags).Invoke($add, @()) | Out-Null
if (![GameplayUiInputGuard]::Registered.Contains($addDialog)) {
    throw 'Add friend dialog must register its input capture.'
}
$addButton.onClick.Invoke()
if (!$addDialog.activeSelf) { throw 'Add friend button must open the dialog.' }
$addClose.onClick.Invoke()
if ($addDialog.activeSelf) { throw 'Add friend close button must hide the dialog.' }
$add.GetType().GetMethod('OnDisable', $flags).Invoke($add, @()) | Out-Null
if ([GameplayUiInputGuard]::Registered.Contains($addDialog)) {
    throw 'Add friend dialog must unregister its input capture.'
}

$request = [Game.Chat.UI.FriendRequestDialogController]::new()
$requestButton = [UnityEngine.UI.Button]::new()
$requestClose = [UnityEngine.UI.Button]::new()
$requestDialog = [UnityEngine.GameObject]::new()
$requestClose.transform.parent = $requestDialog.transform
$request.gameObject.component = $requestButton
$request.GetType().GetField('closeButton', $flags).SetValue($request, $requestClose)
$request.GetType().GetField('requestDialog', $flags).SetValue($request, $requestDialog)
$request.GetType().GetMethod('Awake', $flags).Invoke($request, @()) | Out-Null
if ($requestDialog.activeSelf) { throw 'Request startup must not open the dialog.' }
$request.GetType().GetMethod('OnEnable', $flags).Invoke($request, @()) | Out-Null
if (![GameplayUiInputGuard]::Registered.Contains($requestDialog)) {
    throw 'Request dialog must register its input capture.'
}
$requestButton.onClick.Invoke()
if (!$requestDialog.activeSelf) { throw 'Request button must open the dialog.' }
$requestClose.onClick.Invoke()
if ($requestDialog.activeSelf) { throw 'Request close button must hide the dialog.' }
$request.GetType().GetMethod('OnDisable', $flags).Invoke($request, @()) | Out-Null
if ([GameplayUiInputGuard]::Registered.Contains($requestDialog)) {
    throw 'Request dialog must unregister its input capture.'
}

Write-Output 'PASS: initial visibility, dialog and chat buttons, modal capture and death-state binding.'
