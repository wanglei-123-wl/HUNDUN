$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$paths = @(
    'Assets/Scripts/Friend/Protocol/FriendMessages.cs',
    'Assets/Scripts/Friend/UI/FriendSearchResultItemView.cs'
)
$sources = ($paths | ForEach-Object {
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
    public class MonoBehaviour { public bool enabled = true; }
    public static class Debug
    {
        public static void LogError(string message, MonoBehaviour owner) {}
    }
}
namespace TMPro
{
    public enum TextOverflowModes { Ellipsis }
    public class TMP_Text
    {
        public string text;
        public bool enableWordWrapping, enableAutoSizing;
        public float fontSizeMin;
        public TextOverflowModes overflowMode;
    }
}
namespace UnityEngine.UI
{
    public class ButtonEvent
    {
        public void AddListener(Action callback) {}
        public void RemoveListener(Action callback) {}
    }
    public class Button
    {
        public bool interactable;
        public ButtonEvent onClick = new ButtonEvent();
    }
}
'@
Add-Type -TypeDefinition ("using System;`nusing Game.Friend.Protocol;`n" +
    "using TMPro;`nusing UnityEngine;`nusing UnityEngine.UI;`n" +
    $stubs + $sources) -IgnoreWarnings -WarningAction SilentlyContinue | Out-Null

function Check([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}

$flags = [System.Reflection.BindingFlags]'Instance, NonPublic'
$row = [Game.Friend.UI.FriendSearchResultItemView]::new()
$name = [TMPro.TMP_Text]::new()
$button = [UnityEngine.UI.Button]::new()
$row.GetType().GetField('nicknameText', $flags).SetValue($row, $name)
$row.GetType().GetField('addButton', $flags).SetValue($row, $button)
$target = [Game.Friend.Protocol.FriendSearchPlayer]@{
    characterDbId = [long]1002
    nickname = 'offline target'
    online = $false
}
$row.Bind($target, $null)
Check ($row.CharacterDbId -eq 1002 -and $button.interactable -and
    $name.text.Contains('offline target')) `
    'An offline search result must keep its database ID and enabled add button.'
$row.UpdateState($false, $true, $false, $false)
Check (-not $button.interactable) 'A sent request must disable repeated submissions.'
$row.UpdateState($false, $false, $true, $false)
Check (-not $button.interactable -and $name.text.Contains(' (')) `
    'A deleted target must be marked unavailable rather than offline.'
$row.UpdateState($false, $false, $false, $true)
Check (-not $button.interactable) 'A queued friend operation must block a second add.'
$row.UpdateState($false, $false, $false, $false)
Check ($button.interactable) 'A temporary failure must allow retry.'

$panel = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Friend/UI/FriendSearchPanelController.cs') -Raw
Check ($panel.Contains('runtime.SendRequest(player.characterDbId)') -and
    $panel.Contains('result.errorCode == "target_not_found"') -and
    -not $panel.Contains('!player.online')) `
    'The search panel must submit offline IDs and only retire invalid targets.'
Write-Output 'PASS: offline search rows can request, while invalid and pending targets cannot.'
