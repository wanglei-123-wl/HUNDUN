$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$prefabPath = Join-Path $root 'Assets/预制体/聊天预制体/好友框预制体.prefab'
$prefab = Get-Content -LiteralPath $prefabPath -Raw
$scene = Get-Content -LiteralPath (Join-Path $root 'Assets/Scenes/WorldShell.unity') -Raw
$panel = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Friend/UI/FriendListPanelController.cs') -Raw
$runtime = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Friend/Runtime/FriendSessionRuntime.cs') -Raw
function Check([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}
function Block([string]$yaml, [string]$id) {
    $match = [regex]::Match($yaml, "(?ms)^--- !u!\d+ &$id\r?\n.*?(?=^--- !u!|\z)")
    if (-not $match.Success) { throw "Missing YAML object $id" }
    return $match.Value
}

$row = Block $prefab '900000000000000003'
$text = Block $prefab '1668164784260500846'
$textRect = Block $prefab '2230959359096882358'
$scenePresenter = Block $scene '3001000030'
$prefabMeta = Get-Content -LiteralPath "$prefabPath.meta" -Raw
$prefabGuid = [regex]::Match($prefabMeta, '(?m)^guid: ([a-f0-9]{32})$').Groups[1].Value
Check ($row.Contains('nicknameText: {fileID: 1668164784260500846}') -and
    $scenePresenter.Contains("guid: $prefabGuid") -and
    $panel.Contains('row.Bind(friend, HandleFriendSelected);') -and
    $runtime.Contains('JsonUtility.FromJson<FriendListResult>(json)') -and
    $runtime.Contains('state.ApplyDelta(epoch, JsonUtility.FromJson<FriendListDelta>(json))')) `
    'Scene and runtime must connect the friend DTO to the nickname text.'
Check ($text.Contains('m_text: "\u597D\u53CB\u6635\u79F0"') -and
    $text.Contains('m_enableAutoSizing: 1') -and
    $text.Contains('m_RaycastTarget: 0')) `
    'Prefab preview must not show a fixed player name or intercept selection.'
Check ($textRect.Contains('m_AnchoredPosition: {x: -3, y: 0}') -and
    $textRect.Contains('m_SizeDelta: {x: 64, y: 17}') -and
    $text.Contains('m_fontSizeMin: 9')) `
    'Nickname text must fit a full line at the minimum font size inside the row.'

$paths = @(
    'Assets/Scripts/Friend/Protocol/FriendMessages.cs',
    'Assets/Scripts/Friend/Model/FriendState.cs',
    'Assets/Scripts/Friend/UI/FriendListItemView.cs'
)
$sources = ($paths | ForEach-Object {
    (Get-Content -LiteralPath (Join-Path $root $_) -Raw) `
        -replace '(?m)^using [^\r\n]+;\r?\n', ''
}) -join "`n"
$stubs = @'
public class NetMessageBase { public string msgId; public string type; }
namespace UnityEngine {
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class DisallowMultipleComponent : Attribute {}
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SerializeField : Attribute {}
    public class Object {}
    public class MonoBehaviour : Object { public bool enabled = true; }
    public struct Color {
        public float r, g, b, a;
        public Color(float red, float green, float blue) {
            r = red; g = green; b = blue; a = 1;
        }
    }
    public static class Debug { public static void LogError(string message, Object source) {} }
}
namespace TMPro {
    public enum TextOverflowModes { Ellipsis }
    public class TMP_Text {
        public string text;
        public bool enableWordWrapping, enableAutoSizing, raycastTarget;
        public float fontSizeMin, fontSizeMax;
        public TextOverflowModes overflowMode;
    }
}
namespace UnityEngine.UI {
    public class Image { public UnityEngine.Color color; public bool raycastTarget; }
    public class ButtonEvent {
        public void AddListener(Action callback) {}
        public void RemoveListener(Action callback) {}
    }
    public class Button { public bool interactable; public ButtonEvent onClick = new ButtonEvent(); }
}
'@
Add-Type -TypeDefinition ("using System;`nusing System.Collections.Generic;`n" +
    "using Game.Friend.Protocol;`nusing TMPro;`nusing UnityEngine;`n" +
    "using UnityEngine.UI;`n" + $stubs + $sources) -IgnoreWarnings `
    -WarningAction SilentlyContinue | Out-Null

$state = [Game.Friend.Model.FriendState]::new()
$state.Reset(7)
$state.EnterWorld(7, 1001) | Out-Null
$state.BeginSnapshot(7, 1)
$snapshot = [Game.Friend.Protocol.FriendListResult]@{
    msgId = 'friend_list_result'
    type = 'friend_list_result'
    requestSequence = 1
    friends = @([Game.Friend.Protocol.FriendDto]@{
        characterDbId = 1002; nickname = '小雨'; online = $true
    })
    blocks = @()
    recentContacts = @()
    pendingReceivedRequests = @()
    pendingSentRequests = @()
}
Check ($state.ApplySnapshot(7, $snapshot)) 'Friend snapshot must apply.'
$view = [Game.Friend.UI.FriendListItemView]::new()
$flags = [System.Reflection.BindingFlags]'Instance,NonPublic'
$viewType = $view.GetType()
$nickname = [TMPro.TMP_Text]::new()
$viewType.GetField('nicknameText', $flags).SetValue($view, $nickname)
$viewType.GetField('onlineImage', $flags).SetValue($view, [UnityEngine.UI.Image]::new())
$viewType.GetField('selectButton', $flags).SetValue($view, [UnityEngine.UI.Button]::new())
$view.Bind($state.Friends[[long]1002], $null)
Check ($nickname.text -eq '小雨' -and $nickname.fontSizeMax -eq 12 -and
    -not $nickname.raycastTarget) 'Snapshot nickname must appear on the friend row.'

$state.ApplyDelta(7, [Game.Friend.Protocol.FriendListDelta]@{
    msgId = 'friend_list_delta'
    type = 'friend_list_delta'
    changeType = 'add'
    friend = [Game.Friend.Protocol.FriendDto]@{
        characterDbId = 1002; nickname = '新昵称'; online = $false
    }
})
$view.Bind($state.Friends[[long]1002], $null)
Check ($nickname.text -eq '新昵称') 'Friend delta must update the visible nickname.'
$state.Friends[[long]1002].nickname = ''
$view.Bind($state.Friends[[long]1002], $null)
Check ($nickname.text -eq '角色 #1002') 'Missing backend nicknames must not keep an old name.'
Write-Output 'PASS: friend nickname snapshot, delta, prefab wiring and fallback.'
