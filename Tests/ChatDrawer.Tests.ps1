$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$path = Join-Path $root 'Assets/Scripts/Chat/UI/聊天框侧边伸缩控制器.cs'
$source = (Get-Content -LiteralPath $path -Raw) -replace '(?m)^using [^\r\n]+;\r?\n', ''
$switcherPath = Join-Path $root 'Assets/Scripts/Chat/UI/ChatPanelSwitcher.cs'
$switcherSource = (Get-Content -LiteralPath $switcherPath -Raw) -replace '(?m)^using [^\r\n]+;\r?\n', ''
$stubs = @'
namespace UnityEngine
{
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public sealed class RequireComponent : System.Attribute
    {
        public RequireComponent(System.Type type) {}
    }
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public sealed class DisallowMultipleComponent : System.Attribute {}
    [System.AttributeUsage(System.AttributeTargets.Field)]
    public sealed class SerializeField : System.Attribute {}
    [System.AttributeUsage(System.AttributeTargets.Field)]
    public sealed class MinAttribute : System.Attribute
    {
        public MinAttribute(float minimum) {}
    }
    public class Coroutine
    {
        public IEnumerator routine;
        public bool active = true;
    }
    public class MonoBehaviour
    {
        public bool enabled = true;
        public Transform transform;
        private Coroutine running;
        protected Coroutine StartCoroutine(IEnumerator routine)
        {
            running = new Coroutine { routine = routine };
            running.active = routine.MoveNext();
            return running;
        }
        protected void StopCoroutine(Coroutine routine)
        {
            routine.active = false;
            if (running == routine) running = null;
        }
        public void AdvanceCoroutine()
        {
            if (running == null || !running.active) return;
            running.active = running.routine.MoveNext();
            if (!running.active) running = null;
        }
    }
    public class Transform
    {
        public Transform parent;
        public Dictionary<string, Transform> children = new Dictionary<string, Transform>();
        public object component;
        public Transform Find(string path)
        {
            Transform current = this;
            foreach (string name in path.Split('/'))
            {
                if (!current.children.TryGetValue(name, out Transform node))
                    return null;
                current = node;
            }
            return current;
        }
        public T GetComponent<T>() where T : class => component as T;
    }
    public sealed class GameObject
    {
        public Transform transform;
        public bool activeSelf = true;
        public GameObject(Transform node) { transform = node; }
        public void SetActive(bool value) { activeSelf = value; }
    }
    public sealed class RectTransform : Transform
    {
        public Vector2 anchoredPosition;
    }
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 left => new Vector2(-1, 0);
        public static Vector2 operator *(Vector2 point, float scalar) =>
            new Vector2(point.x * scalar, point.y * scalar);
        public static Vector2 operator +(Vector2 a, Vector2 b) =>
            new Vector2(a.x + b.x, a.y + b.y);
        public static Vector2 Lerp(Vector2 start, Vector2 end, float amount) =>
            new Vector2(start.x + (end.x - start.x) * amount,
                start.y + (end.y - start.y) * amount);
    }
    public static class Mathf
    {
        public static float Clamp01(float value) => System.Math.Clamp(value, 0f, 1f);
        public static float SmoothStep(float start, float end, float progress) =>
            start + (end - start) * progress * progress * (3f - 2f * progress);
    }
    public static class Time { public static float unscaledDeltaTime = 0.25f; }
    public static class Debug
    {
        public static int Errors;
        public static void LogError(string text, object context) => Errors++;
    }
}
namespace UnityEngine.Serialization
{
    [System.AttributeUsage(System.AttributeTargets.Field)]
    public sealed class FormerlySerializedAsAttribute : System.Attribute
    {
        public FormerlySerializedAsAttribute(string oldName) {}
    }
}
namespace UnityEngine.UI
{
    public sealed class Button
    {
        public UnityEngine.Transform transform;
        public ClickEvent onClick = new ClickEvent();
    }
    public sealed class ClickEvent
    {
        private event System.Action listeners;
        public void AddListener(System.Action listener) => listeners += listener;
        public void RemoveListener(System.Action listener) => listeners -= listener;
        public void Invoke() => listeners?.Invoke();
    }
}
'@
Add-Type -TypeDefinition ("using System;`nusing System.Collections;`nusing System.Collections.Generic;`nusing UnityEngine;`nusing UnityEngine.Serialization;`nusing UnityEngine.UI;`n" + $stubs + $source + $switcherSource) -IgnoreWarnings | Out-Null

function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
}
function Assert-Position($node, [double]$x, [double]$y, [string]$message) {
    Assert-True ([Math]::Abs($node.anchoredPosition.x - $x) -lt 0.01 -and
        [Math]::Abs($node.anchoredPosition.y - $y) -lt 0.01) $message
}
function Invoke-Lifecycle($component, [string]$methodName) {
    $component.GetType().GetMethod($methodName, $flags).Invoke($component, @()) | Out-Null
}
function Assert-SceneBinding([string]$name) {
    $scene = Get-Content -LiteralPath (
        Join-Path $root "Assets/Scenes/$name") -Raw
    $controller = [regex]::Match($scene,
        '(?ms)^--- !u!114 &1907362643\r?\n.*?(?=^--- !u!|\z)').Value
    $privateObject = [regex]::Match($scene,
        '(?ms)^--- !u!1 &392896514\r?\n.*?(?=^--- !u!|\z)').Value
    $privateRect = [regex]::Match($scene,
        '(?ms)^--- !u!224 &392896515\r?\n.*?(?=^--- !u!|\z)').Value
    $chatRect = [regex]::Match($scene,
        '(?ms)^--- !u!224 &411326655\r?\n.*?(?=^--- !u!|\z)').Value
    Assert-True ($controller -match
        'guid: 23f1f315bdc94f6893dc1a895091924b' -and
        $controller -match '"\\u804A\\u5929\\u5185\\u5BB9\\u8282\\u70B9": \{fileID: 849528546\}' -and
        $controller -match '"\\u79C1\\u804A\\u8282\\u70B9": \{fileID: 392896515\}' -and
        $controller -match '"\\u56DE\\u6536\\u5C55\\u5F00\\u6309\\u94AE": \{fileID: 612897302\}' -and
        $privateObject -match 'component: \{fileID: 392896515\}' -and
        $privateRect -match 'm_Father: \{fileID: 411326655\}' -and
        $chatRect -match 'fileID: 392896515') `
        "$name must bind the private RectTransform under the chat frame."
}
Assert-SceneBinding 'WorldShell.unity'

$rootNode = [UnityEngine.RectTransform]::new()
$content = [UnityEngine.RectTransform]::new()
$buttonNode = [UnityEngine.RectTransform]::new()
$button = [UnityEngine.UI.Button]::new()
$content.parent = $rootNode
$buttonNode.parent = $rootNode
$buttonNode.component = $button
$button.transform = $buttonNode
$content.anchoredPosition = [UnityEngine.Vector2]::new(-104.8, -208.4)
$buttonNode.anchoredPosition = [UnityEngine.Vector2]::new(230, 12)
$rootNode.children['聊天位置总控'] = $content
$rootNode.children['回收展开'] = $buttonNode

$drawer = [Game.Chat.UI.聊天框侧边伸缩控制器]::new()
$drawer.transform = $rootNode
$flags = [System.Reflection.BindingFlags]'Instance,NonPublic'
foreach ($methodName in @('Awake', 'OnEnable')) {
    [Game.Chat.UI.聊天框侧边伸缩控制器].GetMethod(
        $methodName, $flags).Invoke($drawer, @()) | Out-Null
}
Assert-True ($drawer.enabled -and [UnityEngine.Debug]::Errors -eq 0) `
    'A chat root with a direct content child and Button must initialize.'
$button.onClick.Invoke()
Assert-True ([Math]::Abs($content.anchoredPosition.x + 424.8) -lt 0.01 -and
    [Math]::Abs($buttonNode.anchoredPosition.x + 90) -lt 0.01 -and
    [Math]::Abs($content.anchoredPosition.y + 208.4) -lt 0.01 -and
    [Math]::Abs($buttonNode.anchoredPosition.y - 12) -lt 0.01) `
    'Collapse must move both chat content and its handle left without moving vertically.'
$button.onClick.Invoke()
Assert-True ([Math]::Abs($content.anchoredPosition.x + 104.8) -lt 0.01 -and
    $buttonNode.anchoredPosition.x -eq 230) `
    'A second click must restore the original positions without accumulated drift.'
[Game.Chat.UI.聊天框侧边伸缩控制器].GetField(
    '向左收回距离', $flags).SetValue($drawer, [float]80)
$button.onClick.Invoke()
Assert-True ([Math]::Abs($content.anchoredPosition.x + 184.8) -lt 0.01 -and
    $buttonNode.anchoredPosition.x -eq 150) `
    'Changing the Inspector distance must alter the collapse target.'
[Game.Chat.UI.聊天框侧边伸缩控制器].GetMethod(
    'OnDisable', $flags).Invoke($drawer, @()) | Out-Null
$button.onClick.Invoke()
Assert-True ($buttonNode.anchoredPosition.x -eq 150 -and
    $source -notmatch '\.SetActive\s*\(') `
    'Disabled controller must unhook the button; collapsing must never hide objects.'

$chatInterface = [UnityEngine.RectTransform]::new()
$frame = [UnityEngine.RectTransform]::new()
$publicRoot = [UnityEngine.RectTransform]::new()
$publicContent = [UnityEngine.RectTransform]::new()
$privateRoot = [UnityEngine.RectTransform]::new()
$handleRect = [UnityEngine.RectTransform]::new()
$handle = [UnityEngine.UI.Button]::new()
$publicTab = [UnityEngine.UI.Button]::new()
$privateTab = [UnityEngine.UI.Button]::new()
$chatInterface.children['聊天框'] = $frame
$frame.parent = $chatInterface
$frame.children['公聊'] = $publicRoot
$frame.children['私聊'] = $privateRoot
$frame.children['回收展开'] = $handleRect
$publicRoot.parent = $frame
$privateRoot.parent = $frame
$handleRect.parent = $frame
$publicRoot.children['聊天位置总控'] = $publicContent
$publicContent.parent = $publicRoot
$publicContent.anchoredPosition = [UnityEngine.Vector2]::new(-64.1, -208.4)
$privateRoot.anchoredPosition = [UnityEngine.Vector2]::new(52.4, -1.1)
$handleRect.anchoredPosition = [UnityEngine.Vector2]::new(-478.8, -270.1)
$handleRect.component = $handle
$handle.transform = $handleRect
$publicMessages = [UnityEngine.GameObject]::new([UnityEngine.RectTransform]::new())
$publicInput = [UnityEngine.GameObject]::new([UnityEngine.RectTransform]::new())
$publicMessages.transform.parent = $publicContent
$publicInput.transform.parent = $publicContent
$privatePanel = [UnityEngine.GameObject]::new($privateRoot)
$privatePanel.SetActive($false)

$drawer = [Game.Chat.UI.聊天框侧边伸缩控制器]::new()
$drawer.transform = $chatInterface
Invoke-Lifecycle $drawer 'Awake'
Invoke-Lifecycle $drawer 'OnEnable'
Assert-True ($drawer.enabled -and
    [Game.Chat.UI.聊天框侧边伸缩控制器].GetField(
        '私聊节点', $flags).GetValue($drawer) -eq $privateRoot) `
    'The drawer must find the sibling private panel in the scene hierarchy.'

$switcher = [Game.Chat.UI.ChatPanelSwitcher]::new()
foreach ($entry in @{
    publicButton = $publicTab
    privateButton = $privateTab
    publicContent = $publicMessages
    publicInput = $publicInput
    privatePanel = $privatePanel
}.GetEnumerator()) {
    [Game.Chat.UI.ChatPanelSwitcher].GetField(
        $entry.Key, $flags).SetValue($switcher, $entry.Value)
}
Invoke-Lifecycle $switcher 'Awake'
Invoke-Lifecycle $switcher 'OnEnable'
Assert-True $switcher.enabled 'The existing tab switcher must initialize.'

[UnityEngine.Time]::unscaledDeltaTime = 0.05
$handle.onClick.Invoke()
Assert-True ($publicContent.anchoredPosition.x -lt -64.1 -and
    $privateRoot.anchoredPosition.x -lt 52.4 -and
    $handleRect.anchoredPosition.x -lt -478.8) `
    'Both panels and the handle must start moving together.'
$privateTab.onClick.Invoke()
Assert-True ($privatePanel.activeSelf -and
    -not $publicMessages.activeSelf -and -not $publicInput.activeSelf -and
    $privateRoot.anchoredPosition.x -lt 52.4) `
    'Switching to private chat during collapse must preserve the drawer offset.'
for ($step = 0; $step -lt 8; $step++) { $drawer.AdvanceCoroutine() }
Assert-Position $publicContent -384.1 -208.4 'Public chat must finish collapsed.'
Assert-Position $privateRoot -267.6 -1.1 'Private chat must finish collapsed.'
Assert-Position $handleRect -798.8 -270.1 'The handle must finish collapsed.'
$publicTab.onClick.Invoke()
Assert-True (-not $privatePanel.activeSelf -and
    $publicMessages.activeSelf -and $publicInput.activeSelf) `
    'Switching to public chat while collapsed must show public content.'
Assert-Position $publicContent -384.1 -208.4 'Switching tabs must keep public chat collapsed.'
$privateTab.onClick.Invoke()
Assert-True ($privatePanel.activeSelf -and
    -not $publicMessages.activeSelf -and -not $publicInput.activeSelf) `
    'Switching back to private chat while collapsed must show private content.'
Assert-Position $privateRoot -267.6 -1.1 'Switching tabs must keep private chat collapsed.'

$handle.onClick.Invoke()
$handle.onClick.Invoke()
$handle.onClick.Invoke()
for ($step = 0; $step -lt 8; $step++) { $drawer.AdvanceCoroutine() }
Assert-Position $publicContent -64.1 -208.4 'Rapid reversal must restore public chat.'
Assert-Position $privateRoot 52.4 -1.1 'Rapid reversal must restore private chat.'
Assert-Position $handleRect -478.8 -270.1 'Rapid reversal must restore the handle.'
Assert-True $privatePanel.activeSelf `
    'Expanding must leave the selected private chat visible.'
$publicTab.onClick.Invoke()
Assert-True (-not $privatePanel.activeSelf -and
    $publicMessages.activeSelf -and $publicInput.activeSelf) `
    'Switching back to public chat must preserve the expanded positions.'
Assert-Position $publicContent -64.1 -208.4 'Public chat must not drift after switching tabs.'
Assert-Position $privateRoot 52.4 -1.1 'Private chat must not drift after switching tabs.'
Invoke-Lifecycle $switcher 'OnDisable'
Invoke-Lifecycle $drawer 'OnDisable'

Write-Output 'PASS: both scene bindings, legacy drawer, public/private tabs, interrupted animation and restoration.'
