param([switch]$CompileOnly)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$scene = Get-Content -LiteralPath (Join-Path $root 'Assets/Scenes/WorldShell.unity') -Raw
$prefabPath = 'Assets/预制体/聊天预制体/好友搜索结果预制体 1.prefab'
$prefab = Get-Content -LiteralPath (Join-Path $root $prefabPath) -Raw

function Assert-True([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}
function Block([string]$yaml, [string]$id) {
    $match = [regex]::Match($yaml, "(?ms)^--- !u!\d+ &$id\r?\n.*?(?=^--- !u!|\z)")
    if (-not $match.Success) { throw "Missing YAML object $id" }
    return $match.Value
}
function Guid-Of([string]$path) {
    $meta = Get-Content -LiteralPath (Join-Path $root "$path.meta") -Raw
    return [regex]::Match($meta, '(?m)^guid: ([a-f0-9]{32})$').Groups[1].Value
}

$controllerGuid = Guid-Of 'Assets/Scripts/Friend/UI/FriendSearchPanelController.cs'
$rowGuid = Guid-Of 'Assets/Scripts/Friend/UI/FriendSearchResultItemView.cs'
$prefabGuid = Guid-Of $prefabPath
$panel = Block $scene '3001000010'
$field = Block $scene '3001000004'
$inputRoot = Block $scene '3001000000'
$viewport = Block $scene '3001000001'
$content = Block $scene '1581931413'
$scroll = Block $scene '1362388931'
$row = Block $prefab '900000000000000001'
Assert-True ($panel.Contains("guid: $controllerGuid") -and
    $panel.Contains('dialogRoot: {fileID: 1282344516}') -and
    $panel.Contains('nicknameInput: {fileID: 3001000004}') -and
    $panel.Contains('searchButton: {fileID: 1774681930}') -and
    $panel.Contains('resultsScroll: {fileID: 1362388931}') -and
    $panel.Contains('resultsContent: {fileID: 1581931414}') -and
    $panel.Contains("guid: $prefabGuid")) 'Search UI references must be complete.'
Assert-True ((Block $scene '1907362641').Contains('component: {fileID: 3001000010}') -and
    (Block $scene '1282344516').Contains('m_IsActive: 0') -and
    (Block $scene '1890450972').Contains('m_IsActive: 1')) `
    'The controller must stay active while the editable dialog is hidden.'
Assert-True ($inputRoot.Contains('m_Layer: 5') -and
    $inputRoot.Contains('component: {fileID: 3001000003}') -and
    $inputRoot.Contains('component: {fileID: 3001000004}') -and
    $inputRoot.Contains('component: {fileID: 3001000005}') -and
    $viewport.Contains('m_Father: {fileID: 2110338591}') -and
    $viewport.Contains('fileID: 254890583') -and
    $field.Contains('m_TextViewport: {fileID: 3001000001}') -and
    $field.Contains('m_TextComponent: {fileID: 254890584}') -and
    $field.Contains('m_TargetGraphic: {fileID: 3001000003}') -and
    (Block $scene '3001000003').Contains('m_RaycastTarget: 1') -and
    (Block $scene '3001000005').Contains('guid: 3312d7739989d2b4e91e6319e9a96d76')) `
    'The nickname field needs a clickable graphic, text and a clipping viewport.'
Assert-True ($content.Contains('component: {fileID: 3001000011}') -and
    $content.Contains('component: {fileID: 3001000012}') -and
    $scroll.Contains('m_Content: {fileID: 1581931414}') -and
    (Block $scene '3001000012').Contains('m_VerticalFit: 2') -and
    $row.Contains("guid: $rowGuid") -and
    $row.Contains('nicknameText: {fileID: 184343873047623371}') -and
    $row.Contains('addButton: {fileID: 9069825381376695863}')) `
    'The result list and row prefab must be fully wired.'

$sourcePaths = @(
    'Assets/Scripts/Friend/Protocol/FriendMessages.cs',
    'Assets/Scripts/Friend/Model/FriendRequestQueue.cs',
    'Assets/Scripts/Friend/Model/FriendState.cs',
    'Assets/Scripts/Friend/UI/FriendSearchResultItemView.cs',
    'Assets/Scripts/Friend/UI/FriendSearchPanelController.cs',
    'Assets/Scripts/Friend/UI/FriendRequestItemView.cs',
    'Assets/Scripts/Friend/UI/FriendRequestsPanelController.cs',
    'Assets/Scripts/Friend/UI/FriendListItemView.cs',
    'Assets/Scripts/Friend/UI/FriendListPanelController.cs'
)
$sources = ($sourcePaths | ForEach-Object {
    (Get-Content -LiteralPath (Join-Path $root $_) -Raw) `
        -replace '(?m)^using [^\r\n]+;\r?\n', ''
}) -join "`n"
$stubs = @'
public class NetMessageBase { public string msgId; public string type; }
namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class DisallowMultipleComponent : Attribute {}
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SerializeField : Attribute {}
    public class Object
    {
        public static T Instantiate<T>(T source, Transform parent) where T : new()
        {
            return Instantiate(source, parent, false);
        }
        public static T Instantiate<T>(T source, Transform parent, bool stay) where T : new()
        {
            T result = new T();
            foreach (var field in typeof(T).GetFields(System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public))
                if (Attribute.IsDefined(field, typeof(SerializeField)))
                    field.SetValue(result, field.GetValue(source));
            if (result is MonoBehaviour component) component.transform.SetParent(parent, false);
            return result;
        }
        public static void Destroy(Object source) {}
    }
    public class GameObject : Object
    {
        public string name;
        public Transform transform;
        public bool activeSelf = true;
        public bool activeInHierarchy => activeSelf;
        public GameObject() : this("", new Type[0]) {}
        public GameObject(string name, params Type[] components)
        {
            this.name = name;
            transform = new RectTransform { gameObject = this };
        }
        public T GetComponent<T>() where T : new() { return new T(); }
        public T GetComponentInChildren<T>() where T : new() { return new T(); }
        public void SetActive(bool active) { activeSelf = active; }
    }
    public class Transform
    {
        public GameObject gameObject;
        public Transform parent;
        public bool IsChildOf(Transform other)
        {
            for (Transform node = this; node != null; node = node.parent)
                if (node == other) return true;
            return false;
        }
        public void SetParent(Transform other, bool world) { parent = other; }
        public void SetAsFirstSibling() {}
        public void SetSiblingIndex(int index) {}
    }
    public class RectTransform : Transform
    {
        public Vector2 anchorMin, anchorMax, pivot, anchoredPosition, sizeDelta;
    }
    public struct Vector2 { public float x, y; public Vector2(float x, float y) { this.x = x; this.y = y; } }
    public struct Color { public float r, g, b, a; public Color(float r, float g, float b, float a = 1) { this.r = r; this.g = g; this.b = b; this.a = a; } }
    public class MonoBehaviour : Object
    {
        public GameObject gameObject = new GameObject();
        public Transform transform => gameObject.transform;
        public string name { get => gameObject.name; set => gameObject.name = value; }
        public bool enabled = true;
        public T GetComponentInChildren<T>() where T : new() { return new T(); }
    }
    public static class Time { public static double realtimeSinceStartupAsDouble; }
    public static class Mathf { public static int Min(int x, int y) { return Math.Min(x,y); } }
    public static class Debug { public static void LogError(string message, Object owner) {} }
}
namespace TMPro
{
    public enum TextOverflowModes { Ellipsis }
    public enum TextAlignmentOptions { Center }
    public class TMP_Text : UnityEngine.MonoBehaviour
    {
        public string text;
        public object font;
        public UnityEngine.Color color;
        public bool raycastTarget, enableWordWrapping, enableAutoSizing;
        public float fontSize, fontSizeMin, fontSizeMax;
        public TextOverflowModes overflowMode;
        public TextAlignmentOptions alignment;
    }
    public class TextMeshProUGUI : TMP_Text {}
    public class StringEvent
    {
        public void AddListener(Action<string> callback) {}
        public void RemoveListener(Action<string> callback) {}
    }
    public class TMP_InputField : UnityEngine.MonoBehaviour
    {
        public string text = "";
        public bool isFocused;
        public TMP_Text textComponent, placeholder;
        public StringEvent onSubmit = new StringEvent(), onValueChanged = new StringEvent();
        public void ForceLabelUpdate() {}
    }
}
namespace UnityEngine.UI
{
    public class Image : UnityEngine.MonoBehaviour
    {
        public UnityEngine.Color color;
        public bool raycastTarget;
    }
    public class ButtonEvent
    {
        public void AddListener(Action callback) {}
        public void RemoveListener(Action callback) {}
    }
    public class Button : UnityEngine.MonoBehaviour
    {
        public bool interactable;
        public ButtonEvent onClick = new ButtonEvent();
    }
    public class ScrollRect : UnityEngine.MonoBehaviour
    {
        public UnityEngine.RectTransform content;
        public float verticalNormalizedPosition;
    }
    public static class LayoutRebuilder
    {
        public static void ForceRebuildLayoutImmediate(UnityEngine.RectTransform rect) {}
    }
}
namespace Game.UI.Notice
{
    public static class GameNoticeService
    {
        public static string LastMessage;
        public static void Show(string message) { LastMessage = message; }
    }
}
namespace Game.Friend.Runtime
{
    public class FriendSessionRuntime
    {
        public static FriendSessionRuntime Instance;
        public Game.Friend.Model.FriendRequestQueue Requests = new Game.Friend.Model.FriendRequestQueue();
        public Game.Friend.Model.FriendState State = new Game.Friend.Model.FriendState();
        public event Action<Game.Friend.Protocol.FriendSearchResult> SearchCompleted;
        public event Action<Game.Friend.Protocol.FriendSendResult> SendCompleted;
        public event Action<Game.Friend.Protocol.FriendOperationResult> OperationCompleted;
        public bool RequestList() { return Requests.RequestList(); }
        public bool Search(string keyword) { return Requests.Search(keyword); }
        public bool SendRequest(long id) { return Requests.Send(id, ""); }
        public bool HandleRequest(string id, bool accept)
        {
            if (!Requests.Handle(id, accept)) return false;
            Requests.TrySendNext(Requests.ConnectionEpoch, 0, request => true);
            return true;
        }
    }
}
'@
Add-Type -TypeDefinition ("using System;`nusing System.Collections.Generic;`n" +
    "using System.Text;`nusing Game.Friend.Protocol;`n" +
    "using Game.Friend.Model;`nusing Game.Friend.Runtime;`n" +
    "using Game.UI.Notice;`n" +
    "using TMPro;`nusing UnityEngine;`nusing UnityEngine.UI;`n" +
    $stubs + $sources) -IgnoreWarnings -WarningAction SilentlyContinue | Out-Null

if ($CompileOnly) { return }

$flags = [System.Reflection.BindingFlags]'Instance, NonPublic'
$panelType = [Game.Friend.UI.FriendSearchPanelController]
$valid = $panelType.GetMethod('IsValidKeyword',
    [System.Reflection.BindingFlags]'Static, NonPublic')
Assert-True ([bool]$valid.Invoke($null, @('小')) -and
    -not [bool]$valid.Invoke($null, @(('你' * 6))) -and
    -not [bool]$valid.Invoke($null, @(''))) `
    'A one-character Chinese nickname is valid; oversize and empty keywords are not.'
$runtime = [Game.Friend.Runtime.FriendSessionRuntime]::new()
$runtime.Requests.Reset(9)
$runtime.Requests.EnterWorld(9, 1001) | Out-Null
$runtime.State.Reset(9)
$runtime.State.EnterWorld(9, 1001) | Out-Null
$runtime.State.BeginSnapshot(9, 1)
$runtime.State.AbortSnapshot(9, 1)
$controller = [Game.Friend.UI.FriendSearchPanelController]::new()
$fieldValues = @{
    dialogRoot = [UnityEngine.GameObject]::new()
    nicknameInput = [TMPro.TMP_InputField]::new()
    searchButton = [UnityEngine.UI.Button]::new()
    resultsScroll = [UnityEngine.UI.ScrollRect]::new()
    resultsContent = [UnityEngine.RectTransform]::new()
    runtime = $runtime
    boundEpoch = [long]9
}
foreach ($key in $fieldValues.Keys) {
    $panelType.GetField($key, $flags).SetValue($controller, $fieldValues[$key])
}
$fieldValues.nicknameInput.text = '小'
$panelType.GetMethod('Refresh', $flags).Invoke($controller, @()) | Out-Null
Assert-True $fieldValues.searchButton.interactable `
    'An initial list failure must present an available retry.'
$panelType.GetMethod('Search', $flags).Invoke($controller, @()) | Out-Null
Assert-True ($runtime.Requests.WaitingCount -eq 1 -and
    [Game.UI.Notice.GameNoticeService]::LastMessage -match '正在获取') `
    'The retry must enqueue a list request before attempting search.'

$pendingSearch = $panelType.GetField('pendingSearchSequence', $flags)
$searchedKeyword = $panelType.GetField('searchedKeyword', $flags)
$pendingSearch.SetValue($controller, [long]3)
$searchedKeyword.SetValue($controller, '小')
$outdated = [Game.Friend.Protocol.FriendSearchResult]::new()
$outdated.requestSequence = 2
$outdated.success = $true
$outdated.players = @()
$panelType.GetMethod('HandleSearchResult', $flags).Invoke($controller, @($outdated)) | Out-Null
Assert-True ($pendingSearch.GetValue($controller) -eq 3) `
    'A response for another sequence must not clear the active search.'
$boundEpoch = $panelType.GetField('boundEpoch', $flags)
$boundEpoch.SetValue($controller, [long]8)
$outdated.requestSequence = 3
$panelType.GetMethod('HandleSearchResult', $flags).Invoke($controller, @($outdated)) | Out-Null
Assert-True ($pendingSearch.GetValue($controller) -eq 3) `
    'A response after the connection epoch changed must not complete a stale search.'
$boundEpoch.SetValue($controller, [long]9)
$fieldValues.nicknameInput.text = '小明'
[Game.UI.Notice.GameNoticeService]::LastMessage = $null
$panelType.GetMethod('HandleSearchResult', $flags).Invoke($controller, @($outdated)) | Out-Null
Assert-True ($pendingSearch.GetValue($controller) -eq 0 -and
    [string]::IsNullOrEmpty([Game.UI.Notice.GameNoticeService]::LastMessage)) `
    'A response for an older keyword must not render over the edited input.'

$pendingSend = $panelType.GetField('pendingSendSequence', $flags)
$pendingSend.SetValue($controller, [long]5)
$panelType.GetField('pendingTargetId', $flags).SetValue($controller, [long]1002)
$send = [Game.Friend.Protocol.FriendSendResult]::new()
$send.success = $true
$send.requestId = [Guid]::NewGuid().ToString('D')
$send.requestSequence = 4
$panelType.GetMethod('HandleSendResult', $flags).Invoke($controller, @($send)) | Out-Null
Assert-True ($pendingSend.GetValue($controller) -eq 5) `
    'An unrelated send result must not complete a different button action.'
$send.requestSequence = 5
$runtime.State.RecordSentRequest(9, 1002, $send.requestId)
$panelType.GetMethod('HandleSendResult', $flags).Invoke($controller, @($send)) | Out-Null
Assert-True ($runtime.State.SentRequestIds[[long]1002] -eq $send.requestId -and
    [bool]$panelType.GetMethod('IsRequested', $flags).Invoke(
        $controller, @([long]1002)) -and
    $pendingSend.GetValue($controller) -eq 0) `
    'A matched send result must retain its server ID in the friend state.'

$rowView = [Game.Friend.UI.FriendSearchResultItemView]::new()
$rowType = $rowView.GetType()
$name = [TMPro.TextMeshProUGUI]::new()
$add = [UnityEngine.UI.Button]::new()
$rowType.GetField('nicknameText', $flags).SetValue($rowView, $name)
$rowType.GetField('addButton', $flags).SetValue($rowView, $add)
$player = [Game.Friend.Protocol.FriendSearchPlayer]::new()
$player.characterDbId = 1002
$player.nickname = '小明'
$player.online = $true
$rowView.Bind($player, $null)
Assert-True ($rowView.CharacterDbId -eq 1002 -and $add.interactable) `
    'An online row must retain the numeric character ID and enable its button.'
$rowView.UpdateState($true, $false, $false, $false)
Assert-True (-not $add.interactable) 'An existing friend cannot be added.'
$rowView.UpdateState($false, $true, $false, $false)
Assert-True (-not $add.interactable) 'An already-requested player cannot be added.'
$rowView.UpdateState($false, $false, $false, $false)
$player.online = $false
$rowView.UpdateState($false, $false, $false, $false)
Assert-True ($add.interactable -and $name.text -match '离线') `
    'An offline player must remain visible and allow an application.'

Write-Output 'PASS: friend search UI compiles, scene and prefab bindings, retry and row states.'
