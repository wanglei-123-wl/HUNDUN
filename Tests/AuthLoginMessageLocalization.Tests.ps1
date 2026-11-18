$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$login = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/LoginScripts/LoginUITMP.cs') -Raw
$validator = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/LoginScripts/AuthValidator.cs') -Raw
$scene = Get-Content -LiteralPath (Join-Path $root 'Assets/Scenes/SampleScene.unity') -Raw
$stubs = @'
using System;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
namespace UnityEngine {
    [AttributeUsage(AttributeTargets.Field)] public sealed class SerializeField : Attribute {}
    [AttributeUsage(AttributeTargets.Field)] public sealed class HeaderAttribute : Attribute {
        public HeaderAttribute(string name) {}
    }
    public class MonoBehaviour {}
    public class GameObject {
        public bool activeSelf;
        public object component;
        public void SetActive(bool value) { activeSelf = value; }
        public T GetComponent<T>() where T : class { return component as T; }
    }
    public static class Debug {
        public static void Log(object message) {}
        public static void LogWarning(object message) {}
    }
}
namespace UnityEngine.Events {
    public class UnityEvent<T1, T2> {
        public void Invoke(T1 first, T2 second) {}
    }
}
namespace UnityEngine.UI {
    public class Button {
        public bool interactable = true;
        public ClickEvent onClick = new ClickEvent();
        public class ClickEvent {
            public void AddListener(Action callback) {}
            public void RemoveListener(Action callback) {}
        }
    }
}
namespace TMPro {
    public class TMP_Text { public string text; }
    public class TMP_InputField {
        public enum ContentType { Standard, Password }
        public enum LineType { SingleLine }
        public ContentType contentType;
        public LineType lineType;
        public string text;
        public void ForceLabelUpdate() {}
    }
}
public static class GateEntryHandoff {
    public static long RetryCharacter;
    public static string Failure;
    public static string TakeFailure(out long retryCharacter) {
        retryCharacter = RetryCharacter;
        return Failure;
    }
}
public class AuthSession { public static AuthSession Instance; public bool HasValidGameSession; }
public class CharacterSelectPanelUI {
    public long RetryCharacter;
    public string Failure;
    public void ShowEntryRecovery(string failure, long retryCharacter) {
        Failure = failure;
        RetryCharacter = retryCharacter;
    }
}
public static class WorldEntryLoadingView {
    public static bool Closed;
    public static void CloseWorldEntry() { Closed = true; }
}
'@
$validatorBody = $validator -replace '(?m)^using [^;]+;\r?\n', ''
$loginBody = $login -replace '(?m)^using [^;]+;\r?\n', ''
Add-Type -TypeDefinition ($stubs + "`n" + $validatorBody + "`n" + $loginBody) -IgnoreWarnings -WarningAction SilentlyContinue | Out-Null

function Assert-Equal($actual, $expected, $label) {
    if ($actual -cne $expected) { throw "$label : expected '$expected', got '$actual'." }
}

$view = [LoginUITMP]::new()
$text = [TMPro.TMP_Text]::new()
$button = [UnityEngine.UI.Button]::new()
$flags = [System.Reflection.BindingFlags]'Instance,NonPublic'
[LoginUITMP].GetField('messageText', $flags).SetValue($view, $text)
[LoginUITMP].GetField('loginButton', $flags).SetValue($view, $button)
$view.BeginLogin()
Assert-Equal $text.text '登录中...' 'Login progress'
Assert-Equal $button.interactable $false 'Login button during request'
$view.ShowLoginFailed('')
Assert-Equal $text.text '登录失败' 'Empty login failure'
Assert-Equal $button.interactable $true 'Login button after failure'
$view.ShowLoginFailed('服务器拒绝')
Assert-Equal $text.text '服务器拒绝' 'Provided failure detail'
$view.ShowLoginSuccess()
Assert-Equal $text.text '登录成功' 'Login success'

$selection = [CharacterSelectPanelUI]::new()
$selectionView = [UnityEngine.GameObject]::new()
$selectionView.component = $selection
[LoginUITMP].GetField('characterSelectView', $flags).SetValue($view, $selectionView)
[AuthSession]::Instance = [AuthSession]::new()
[AuthSession]::Instance.HasValidGameSession = $true
[GateEntryHandoff]::Failure = '等待怪物快照超时'
[GateEntryHandoff]::RetryCharacter = 42
[LoginUITMP].GetMethod('Start', $flags).Invoke($view, @()) | Out-Null
Assert-Equal ([WorldEntryLoadingView]::Closed) $true 'Old loading view closes on return'
Assert-Equal $selectionView.activeSelf $true 'Authenticated retry opens selection'
Assert-Equal $selection.RetryCharacter 42 'Retry keeps the original character'
Assert-Equal $selection.Failure '等待怪物快照超时' 'Retry shows the failure reason'

Assert-Equal ([AuthValidator]::GetLoginError('', 'password123')) '请输入用户名' 'Empty username'
Assert-Equal ([AuthValidator]::GetLoginError('ab', 'password123')) '用户名长度须为 3-32 个字符' 'Short username'
Assert-Equal ([AuthValidator]::GetLoginError('tester', '')) '请输入密码' 'Empty password'
Assert-Equal ([AuthValidator]::GetLoginError('tester', 'short')) '密码长度须为 8-128 个字符' 'Short password'
Assert-Equal ([AuthValidator]::GetLoginError('tester', 'password123')) '' 'Valid credentials'
Assert-Equal ([AuthValidator]::GetRegisterError('tester', 'password123', 'different')) '两次输入的密码不一致' 'Mismatched passwords'

$font = 'e73af7cefd86226449d7c1ddcb0273e6'
$material = '-5878913015649278362'
foreach ($id in @('97505933', '552014540')) {
    $block = [regex]::Match($scene, "(?ms)^--- !u!114 &$id`r?`n(?<body>.*?)(?=^--- !u!|\z)")
    if (-not $block.Success -or
        $block.Groups['body'].Value -notmatch "m_fontAsset: \{fileID: 11400000, guid: $font, type: 2\}" -or
        $block.Groups['body'].Value -notmatch "m_sharedMaterial: \{fileID: $material, guid: $font, type: 2\}") {
        throw "Message component $id must use the Chinese font and its material."
    }
}
if ($scene -notmatch 'messageText: \{fileID: 97505933\}' -or
    $scene -notmatch 'messageText: \{fileID: 552014540\}' -or
    $scene -match '(?m)^  m_text: AAAAAA$') {
    throw 'Login/register message bindings or placeholder text are incorrect.'
}

Write-Output 'Login and registration message localization checks passed.'
