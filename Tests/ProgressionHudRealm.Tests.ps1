$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$presenter = Get-Content (Join-Path $root 'Assets/Scripts/CharacterPanel/Presentation/ProgressionHudPresenter.cs') -Raw
$display = Get-Content (Join-Path $root 'Assets/Scripts/Cultivation/Presentation/CultivationProgressDisplay.cs') -Raw
$scene = Get-Content (Join-Path $root 'Assets/Scenes/WorldShell.unity') -Raw

$stubs = @'
namespace UnityEngine
{
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public sealed class DisallowMultipleComponent : System.Attribute {}
    [System.AttributeUsage(System.AttributeTargets.Field)]
    public sealed class SerializeField : System.Attribute {}
    public class MonoBehaviour
    {
        protected static T FindObjectOfType<T>() where T : class => null;
        protected T[] GetComponentsInChildren<T>(bool includeInactive) => System.Array.Empty<T>();
    }
    public class GameObject
    {
        public string name;
        public bool activeSelf = true;
        public bool activeInHierarchy = true;
    }
    public struct Color { public float a; }
    public struct Rect { public float width; public float height; }
    public struct Vector3 { }
    public class RectTransform
    {
        public Rect rect;
        public Vector3 localScale;
    }
    public class Transform
    {
        public string name;
        public T[] GetComponentsInChildren<T>(bool includeInactive) => System.Array.Empty<T>();
    }
    public static class Mathf
    {
        public static float Clamp01(float value) => System.Math.Clamp(value, 0f, 1f);
    }
    public static class Debug
    {
        public static void Log(object message) {}
        public static void LogWarning(object message) {}
    }
}
namespace UnityEngine.UI
{
    public class Image
    {
        public enum Type { Simple, Filled }
        public Type type;
        public float fillAmount;
    }
}
namespace TMPro
{
    public class TMP_Text
    {
        public string text;
        public bool enabled = true;
        public UnityEngine.GameObject gameObject = new UnityEngine.GameObject();
        public UnityEngine.RectTransform rectTransform = new UnityEngine.RectTransform();
        public UnityEngine.Color color;
    }
}
public sealed class NetworkManager
{
    public static NetworkManager Instance;
    public event System.Action<string> OnDisconnected;
    public void Disconnect() => OnDisconnected?.Invoke("test");
}
public sealed class GameSession
{
    public bool IsWorldReady = true;
    public long LocalCharacterDbId = 42;
    public string LocalNickname = "测试昵称";
    public ProgressionSnapshotResult LocalProgressionSnapshot;
    public int Requests;
    private int currentMp = -1;
    private int maxMp;
    public event System.Action<ProgressionSnapshotResult> LocalProgressionSnapshotChanged;
    public event System.Action LocalManaChanged;
    public event System.Action<long, long> LocalWorldEntered;
    public void RequestProgressionSnapshot() => Requests++;
    public bool TryGetLocalMana(out int currentMp, out int maxMp)
    {
        currentMp = this.currentMp;
        maxMp = this.maxMp;
        return currentMp >= 0;
    }
    public void EmitMana(int current, int maximum)
    {
        currentMp = current;
        maxMp = maximum;
        LocalManaChanged?.Invoke();
    }
    public void Emit(ProgressionSnapshotResult result)
    {
        LocalProgressionSnapshot = result;
        if (result.success && result.characterDbId == LocalCharacterDbId)
            EmitMana(result.currentMp, result.maxMp);
        LocalProgressionSnapshotChanged?.Invoke(result);
    }
    public void EnterWorld(long id)
    {
        LocalCharacterDbId = id;
        LocalProgressionSnapshot = null;
        currentMp = -1;
        maxMp = 0;
        LocalWorldEntered?.Invoke(1, id);
    }
}
public sealed class ProgressionSnapshotResult
{
    public bool success;
    public long characterDbId;
    public long jingjieExp;
    public long requiredJingjieExp;
    public long totalJingjieExp;
    public bool hasNextJingjie;
    public int currentMp;
    public int maxMp;
    public string jingjieName;
}
'@
$source = ($display + "`n" + $presenter) -replace
    '(?m)^using [^;]+;\r?\n', ''
$imports = "using System;`nusing System.Globalization;`n" +
    "using Game.Cultivation.Presentation;`nusing TMPro;`n" +
    "using UnityEngine;`nusing UnityEngine.UI;`n"
Add-Type -TypeDefinition ($imports + $stubs + "`n" + $source) -IgnoreWarnings | Out-Null

function Set-Private($target, [string]$name, $value) {
    $field = $target.GetType().GetField(
        $name, [System.Reflection.BindingFlags]'Instance,NonPublic')
    $field.SetValue($target, $value)
}
function Invoke-Private($target, [string]$name) {
    $method = $target.GetType().GetMethod(
        $name, [System.Reflection.BindingFlags]'Instance,NonPublic')
    $method.Invoke($target, @()) | Out-Null
}
function Assert-Text($text, [string]$expected, [string]$message) {
    if ($text.text -ne $expected) { throw "$message (actual: $($text.text))" }
}

$network = [NetworkManager]::new()
[NetworkManager]::Instance = $network
$session = [GameSession]::new()
$hud = [Game.CharacterPanel.Presentation.ProgressionHudPresenter]::new()
$realm = [TMPro.TMP_Text]::new()
$nickname = [TMPro.TMP_Text]::new()
$experience = [TMPro.TMP_Text]::new()
$experienceFill = [UnityEngine.UI.Image]::new()
$mana = [TMPro.TMP_Text]::new()
Set-Private $hud 'session' $session
Set-Private $hud 'realmText' $realm
Set-Private $hud 'nicknameText' $nickname
Set-Private $hud 'experienceText' $experience
Set-Private $hud 'experienceFill' $experienceFill
Set-Private $hud 'mpText' $mana

Invoke-Private $hud 'OnEnable'
Assert-Text $realm '--' 'HUD must clear the editor placeholder before a snapshot.'
Assert-Text $nickname '测试昵称' 'HUD nickname must share the character panel session.'
if ($session.Requests -ne 1) { throw 'HUD must request missing progression data.' }
$session.EmitMana(120, 250)
Assert-Text $mana '120/250' 'A full equipment snapshot must update HUD mana while progression is still pending.'
Assert-Text $realm '--' 'A resource update must not invent a progression snapshot.'

$snapshot = [ProgressionSnapshotResult]::new()
$snapshot.success = $true
$snapshot.characterDbId = 42
$snapshot.jingjieExp = 40
$snapshot.requiredJingjieExp = 100
$snapshot.totalJingjieExp = 60
$snapshot.hasNextJingjie = $true
$snapshot.currentMp = 8
$snapshot.maxMp = 10
$snapshot.jingjieName = '武夫一重'
$session.Emit($snapshot)
Assert-Text $realm '武夫一重' 'HUD must display the server realm name.'
Assert-Text $nickname '测试昵称' 'Progress updates must retain the local nickname.'
Assert-Text $experience '40/100' 'HUD must display current cultivation and the next realm requirement.'
Assert-Text $mana '8/10' 'HUD mana must render from a snapshot without level fields.'
if ([math]::Abs($experienceFill.fillAmount - 0.4) -gt 0.0001) {
    throw 'HUD cultivation fill must match the cultivation panel.'
}
$snapshot.currentMp = 9
$session.Emit($snapshot)
Assert-Text $mana '9/10' 'HUD must refresh mana after a snapshot update.'
Assert-Text $experience '40/100' 'A mana update must preserve cultivation text.'
$snapshot.jingjieExp = 130
$session.Emit($snapshot)
Assert-Text $experience '130/100' 'Usable cultivation must update the HUD.'
if ($experienceFill.fillAmount -ne 1) {
    throw 'Overfilled cultivation must clamp at full.'
}
$snapshot.hasNextJingjie = $false
$session.Emit($snapshot)
Assert-Text $experience '130/--' 'Without a breakthrough path HUD must hide the requirement.'
if ($experienceFill.fillAmount -ne 0) {
    throw 'Without a breakthrough path the HUD fill must be empty.'
}
$snapshot.hasNextJingjie = $true
$snapshot.requiredJingjieExp = 0
$session.Emit($snapshot)
Assert-Text $experience '130/0' 'Zero required cultivation is a valid configured path.'
if ($experienceFill.fillAmount -ne 1) {
    throw 'Zero required cultivation with a path must show full progress.'
}
$snapshot.requiredJingjieExp = 100

$other = [ProgressionSnapshotResult]::new()
$other.success = $true
$other.characterDbId = 99
$other.jingjieName = '其他角色境界'
$session.Emit($other)
Assert-Text $realm '--' 'Another character must not populate the local realm.'
Assert-Text $experience '--/--' 'Another character must not populate the HUD cultivation.'
$snapshot.jingjieName = ''
$session.Emit($snapshot)
Assert-Text $realm '--' 'A missing name must not invent a realm.'
$snapshot.jingjieName = '武夫一重'
$session.Emit($snapshot)
Invoke-Private $hud 'OnDisable'
Invoke-Private $hud 'OnEnable'
Assert-Text $realm '武夫一重' 'Reopening must render the cached local realm.'
if ($session.Requests -ne 1) { throw 'Cached progression must not request again.' }
$session.LocalNickname = '新角色'
$session.EnterWorld(99)
Assert-Text $nickname '新角色' 'Entering as another character must refresh the nickname.'
Assert-Text $realm '--' 'Entering as another character must clear the prior realm.'
Assert-Text $experience '--/--' 'Entering as another character must clear the prior cultivation.'
$network.Disconnect()
Assert-Text $nickname '--' 'Disconnect must clear the nickname.'
Assert-Text $realm '--' 'Disconnect must clear the realm.'
Assert-Text $experience '--/--' 'Disconnect must clear the HUD cultivation.'
Invoke-Private $hud 'OnDisable'

if ($scene -notmatch 'm_Name: "\\u5883\\u754C\\u663E\\u793A\\u6846"' -or
    $scene -notmatch 'guid: 3e5e4d4f6dd44e58b8f9a1ef7cf723a1, type: 3\}\s+m_Name: \s+m_EditorClassIdentifier: \s+session: \{fileID: 2108349142\}\s+realmText: \{fileID: 1705721523\}') {
    throw 'WorldShell HUD must bind the realm display text to the progression presenter.'
}
if ($scene -match '\b310000001[0-7]\b|levelText:' -or $presenter -match '\blevelText\b') {
    throw 'WorldShell must remove the retired HUD level row and its bindings.'
}
if ($scene -notmatch 'm_Name: "\\u4FEE\\u4E3A\\u6761"' -or
    $scene -notmatch 'm_Name: "\\u4FEE\\u4E3A\\u6570\\u503C"' -or
    $scene -notmatch 'experienceText: \{fileID: 1395329221\}\s+experienceFill: \{fileID: 1875507875\}') {
    throw 'WorldShell HUD cultivation text and fill must remain bound to the same widgets.'
}
Write-Output 'PASS: HUD realm and cultivation rendering, lifecycle and scene binding.'
