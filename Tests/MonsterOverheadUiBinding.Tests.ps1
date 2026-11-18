$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$marker = Get-Content (Join-Path $root 'Assets/Scripts/Monster/UI/MonsterOverheadUiReferences.cs') -Raw
$presenter = Get-Content (Join-Path $root 'Assets/Scripts/Monster/UI/MonsterHealthBarPresenter.cs') -Raw
$prefab = Get-Content (Join-Path $root 'Assets/预制体/便捷预制体/Canvas.prefab') -Raw
$meta = Get-Content (Join-Path $root 'Assets/Scripts/Monster/UI/MonsterOverheadUiReferences.cs.meta') -Raw
$dragon = Get-Content (Join-Path $root 'Assets/预制体/怪物预制体/FlyingMonsterRoot.prefab') -Raw
$turtle = Get-Content (Join-Path $root 'Assets/预制体/怪物预制体/SL002MonsterRoot.prefab') -Raw
$scene = Get-Content (Join-Path $root 'Assets/Scenes/WorldShell.unity') -Raw
$canvasMeta = Get-Content (Join-Path $root 'Assets/预制体/便捷预制体/Canvas.prefab.meta') -Raw

$ids = [regex]::Matches($prefab, '(?m)^--- !u!\d+ &(\d+)') |
    ForEach-Object { $_.Groups[1].Value }
if (($ids | Select-Object -Unique).Count -ne $ids.Count) {
    throw 'Shared Canvas contains duplicate file IDs.'
}
function Get-PrefabBlock([string]$content, [string]$fileId) {
    return [regex]::Match($content,
        "(?ms)^--- !u!\d+ &$fileId\r?\n(.*?)(?=^--- !u!|\z)").Groups[1].Value
}
$raycasterBlock = Get-PrefabBlock $prefab '2339105812771328681'
if ($raycasterBlock -notmatch '(?m)^\s*m_Enabled: 0$' -or
    $raycasterBlock -notmatch 'guid: dc42784cf147c0c48a680349fa168899') {
    throw 'Monster overhead Canvas must not raycast gameplay clicks.'
}
foreach ($graphicId in @('7310042028829253882', '7757382046613805492',
        '5044018411097094545', '3231133542401151988')) {
    $graphicBlock = Get-PrefabBlock $prefab $graphicId
    if ($graphicBlock -notmatch '(?m)^\s*m_RaycastTarget: 0$') {
        throw "Monster overhead graphic $graphicId must not capture clicks."
    }
}
foreach ($id in @('7416730709314917101', '3231133542401151988',
        '7310042028829253882', '5044018411097094545')) {
    if ($ids -notcontains $id) { throw "Shared Canvas is missing file ID $id." }
}
$guid = [regex]::Match($meta, '(?m)^guid: ([0-9a-f]+)$').Groups[1].Value
if (-not $guid -or -not $prefab.Contains("guid: $guid")) {
    throw 'Shared Canvas does not reference the marker script.'
}
foreach ($reference in @(
        'fillImage: {fileID: 3231133542401151988}',
        'healthText: {fileID: 7310042028829253882}',
        'monsterNameText: {fileID: 5044018411097094545}')) {
    if (-not $prefab.Contains($reference)) {
        throw "Shared Canvas has a missing or incorrect reference: $reference"
    }
}
$canvasGuid = [regex]::Match($canvasMeta, '(?m)^guid: ([0-9a-f]+)$').Groups[1].Value
foreach ($monsterPrefab in @($dragon, $turtle)) {
    if (-not $monsterPrefab.Contains("m_SourcePrefab: {fileID: 100100000, guid: $canvasGuid")) {
        throw 'A monster prefab does not contain the marked shared Canvas.'
    }
}
if ($dragon -notmatch 'target: \{fileID: 5044018411097094545, guid: ' +
        [regex]::Escape($canvasGuid) -or
    -not $dragon.Contains('value: "\u9F99\u795E"') -or
    $dragon -notmatch 'healthBarRoot: \{fileID: 0\}\s+fillImage: \{fileID: 0\}\s+healthText: \{fileID: 0\}') {
    throw 'Dragon name override or auto-binding fields are incorrect.'
}
if (-not $prefab.Contains('m_text: "\u523A\u9F9F"') -or
    $turtle.Contains('value: "\u9F99\u795E"')) {
    throw 'The shared or turtle name must not inherit the dragon override.'
}
$catalog = Get-Content (Join-Path $root 'Assets/Data/Monsters/怪物总表.asset') -Raw
$catalogGuid = [regex]::Match(
    (Get-Content (Join-Path $root 'Assets/Data/Monsters/怪物总表.asset.meta') -Raw),
    '(?m)^guid: ([0-9a-f]+)$').Groups[1].Value
if (-not $scene.Contains("monsterCatalog: {fileID: 11400000, guid: $catalogGuid, type: 2}")) {
    throw 'Scene monster manager no longer references the monster catalog.'
}
foreach ($mapping in @(
    @{ Id = 'Ls001'; PrefabGuid = 'ff126016c58935a468e2a6fd49a26558' },
    @{ Id = 'Ls002'; PrefabGuid = 'ac349ad9cbf74cad9418251964b79c23' })) {
    $asset = Get-Content (Join-Path $root "Assets/Data/Monsters/$($mapping.Id).asset") -Raw
    $assetGuid = [regex]::Match(
        (Get-Content (Join-Path $root "Assets/Data/Monsters/$($mapping.Id).asset.meta") -Raw),
        '(?m)^guid: ([0-9a-f]+)$').Groups[1].Value
    if (-not $catalog.Contains("guid: $assetGuid, type: 2") -or
        -not $asset.Contains("id: $($mapping.Id)") -or
        -not $asset.Contains("guid: $($mapping.PrefabGuid), type: 3")) {
        throw "Monster catalog entry does not point to the expected prefab: $($mapping.Id)"
    }
}

$stubs = @'
using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class DisallowMultipleComponent : Attribute {}
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SerializeField : Attribute {}
    public sealed class GameObject
    {
        public bool activeSelf = true;
        public void SetActive(bool active) { activeSelf = active; }
    }
    public class MonoBehaviour
    {
        public GameObject gameObject = new GameObject();
        public object[] descendants = Array.Empty<object>();
        public MonsterNetworkView networkView;
        protected T GetComponent<T>() where T : class => networkView as T;
        protected T GetComponentInParent<T>() where T : class => null;
        protected T[] GetComponentsInChildren<T>(bool includeInactive) where T : class
        {
            if (!includeInactive) throw new Exception("Inactive Canvas must be discoverable.");
            return descendants.OfType<T>().ToArray();
        }
    }
    public static class Debug
    {
        public static readonly List<string> Errors = new List<string>();
        public static void LogError(object message, object context) => Errors.Add(message.ToString());
    }
    public static class Mathf
    {
        public static int Max(int a, int b) => Math.Max(a, b);
        public static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(max, value));
        public static float Clamp01(float value) => Math.Max(0, Math.Min(1, value));
    }
}
namespace UnityEngine.UI
{
    public sealed class Image { public float fillAmount; }
}
namespace TMPro
{
    public sealed class TMP_Text { public string text; }
}
public sealed class MonsterNetworkView
{
    public int CurrentHp { get; private set; }
    public int MaxHp { get; private set; }
    public event Action<int, int> OnHealthChanged;
    public void SetHealth(int current, int max)
    {
        CurrentHp = current;
        MaxHp = max;
        OnHealthChanged?.Invoke(current, max);
    }
}
'@
$markerSource = [regex]::Replace($marker, '(?m)^using .+;\r?\n', '')
$presenterSource = [regex]::Replace($presenter, '(?m)^using .+;\r?\n', '')
Add-Type -TypeDefinition ($stubs + "`n" + $markerSource + "`n" + $presenterSource) -IgnoreWarnings

function Set-Field($target, [string]$name, $value) {
    $field = $target.GetType().GetField($name,
        [System.Reflection.BindingFlags]'Instance,NonPublic')
    $field.SetValue($target, $value)
}
function Invoke-Lifecycle($target, [string]$name) {
    $method = $target.GetType().GetMethod($name,
        [System.Reflection.BindingFlags]'Instance,NonPublic')
    $method.Invoke($target, @()) | Out-Null
}

$ui = [MonsterOverheadUiReferences]::new()
$fill = [UnityEngine.UI.Image]::new()
$health = [TMPro.TMP_Text]::new()
$name = [TMPro.TMP_Text]::new()
$name.text = 'local name'
Set-Field $ui 'fillImage' $fill
Set-Field $ui 'healthText' $health
Set-Field $ui 'monsterNameText' $name

$presenterInstance = [MonsterHealthBarPresenter]::new()
$presenterInstance.descendants = @($ui)
$presenterInstance.networkView = [MonsterNetworkView]::new()
$oldRoot = [UnityEngine.GameObject]::new()
Set-Field $presenterInstance 'healthBarRoot' $oldRoot
Invoke-Lifecycle $presenterInstance 'Awake'
if ($ui.gameObject.activeSelf -or -not $oldRoot.activeSelf) {
    throw 'Auto-discovered Canvas must override the stale legacy root.'
}
Invoke-Lifecycle $presenterInstance 'OnEnable'
$presenterInstance.networkView.SetHealth(25, 100)
if (-not $ui.gameObject.activeSelf -or $fill.fillAmount -ne 0.25 -or
    $health.text -ne '25 / 100' -or $name.text -ne 'local name') {
    throw 'Health updates must use the discovered controls without overwriting the name.'
}
Invoke-Lifecycle $presenterInstance 'OnDisable'

$replacement = [MonsterOverheadUiReferences]::new()
$replacementFill = [UnityEngine.UI.Image]::new()
$replacementHealth = [TMPro.TMP_Text]::new()
$replacementName = [TMPro.TMP_Text]::new()
$replacementName.text = 'replacement name'
Set-Field $replacement 'fillImage' $replacementFill
Set-Field $replacement 'healthText' $replacementHealth
Set-Field $replacement 'monsterNameText' $replacementName
$presenterInstance.descendants = @($replacement)
Invoke-Lifecycle $presenterInstance 'OnEnable'
if (-not $replacement.gameObject.activeSelf -or $replacementFill.fillAmount -ne 0.25 -or
    $replacementHealth.text -ne '25 / 100' -or $replacementName.text -ne 'replacement name') {
    throw 'Reattached Canvas must bind and render current health without manual field assignment.'
}
Invoke-Lifecycle $presenterInstance 'OnDisable'

$legacy = [MonsterHealthBarPresenter]::new()
$legacyRoot = [UnityEngine.GameObject]::new()
$legacyFill = [UnityEngine.UI.Image]::new()
$legacyText = [TMPro.TMP_Text]::new()
$legacy.networkView = [MonsterNetworkView]::new()
Set-Field $legacy 'healthBarRoot' $legacyRoot
Set-Field $legacy 'fillImage' $legacyFill
Set-Field $legacy 'healthText' $legacyText
Invoke-Lifecycle $legacy 'Awake'
Invoke-Lifecycle $legacy 'OnEnable'
$legacy.networkView.SetHealth(10, 20)
if (-not $legacyRoot.activeSelf -or $legacyFill.fillAmount -ne 0.5 -or
    $legacyText.text -ne '10 / 20') {
    throw 'Unmarked legacy Canvas must retain its previous bindings.'
}
Invoke-Lifecycle $legacy 'OnDisable'

$duplicate = [MonsterHealthBarPresenter]::new()
$duplicate.descendants = @($ui, [MonsterOverheadUiReferences]::new())
$duplicate.networkView = [MonsterNetworkView]::new()
Invoke-Lifecycle $duplicate 'Awake'
if (-not ([UnityEngine.Debug]::Errors -match 'multiple')) {
    throw 'Multiple markers must produce an explicit error.'
}

$invalid = [MonsterHealthBarPresenter]::new()
$invalid.descendants = @([MonsterOverheadUiReferences]::new())
$invalid.networkView = [MonsterNetworkView]::new()
Invoke-Lifecycle $invalid 'Awake'
if (-not ([UnityEngine.Debug]::Errors -match 'missing its fill')) {
    throw 'An incomplete marker must report missing UI references.'
}

Write-Output 'PASS: monster prefabs, catalog wiring, replacement auto-binding, legacy fallback, and invalid marker errors.'
