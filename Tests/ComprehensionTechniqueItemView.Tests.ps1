$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Cultivation/Presentation/ComprehensionTechniqueItemView.cs') -Raw
$meta = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Cultivation/Presentation/ComprehensionTechniqueItemView.cs.meta') -Raw
$prefab = Get-Content -LiteralPath (
    Join-Path $root 'Assets/预制体/格子预制体/参悟面板功法预制体.prefab') -Raw
$scene = Get-Content -LiteralPath (Join-Path $root 'Assets/Scenes/WorldShell.unity') -Raw

$stubs = @'
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
namespace UnityEngine
{
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public sealed class DisallowMultipleComponent : System.Attribute {}
    [System.AttributeUsage(System.AttributeTargets.Field)]
    public sealed class SerializeField : System.Attribute {}
    public class MonoBehaviour {}
    public class GameObject {
        public bool activeSelf = true;
        public void SetActive(bool value) { activeSelf = value; }
    }
    public class Sprite {}
    public struct Color { public static Color white => new Color(); }
}
namespace TMPro { public class TMP_Text { public string text; } }
namespace UnityEngine.UI
{
    public class Button {
        public readonly ClickEvent onClick = new ClickEvent();
        public class ClickEvent {
            private event Action handlers;
            public void AddListener(Action handler) { handlers += handler; }
            public void RemoveListener(Action handler) { handlers -= handler; }
            public void Invoke() { handlers?.Invoke(); }
        }
    }
    public class Image
    {
        public UnityEngine.Sprite sprite;
        public UnityEngine.Color color;
        public bool enabled = true;
        public bool preserveAspect;
    }
}
'@
$source = $source -replace '(?m)^using [^;]+;\r?\n', ''
Add-Type -TypeDefinition ($stubs + "`n" + $source) -IgnoreWarnings | Out-Null

function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
}
function Set-Private($target, [string]$name, $value) {
    $target.GetType().GetField(
        $name, [System.Reflection.BindingFlags]'Instance,NonPublic'
    ).SetValue($target, $value)
}
function PrefabBlock([int]$kind, [long]$id) {
    $pattern = "(?s)--- !u!$kind &$id`r?`n(?<body>.*?)(?=`r?`n--- !u!|$)"
    $matches = [regex]::Matches($prefab, $pattern)
    Assert-True ($matches.Count -eq 1) "Prefab block $kind/$id must exist once."
    return $matches[0].Groups['body'].Value
}

$view = [Game.Cultivation.Presentation.ComprehensionTechniqueItemView]::new()
$icon = [UnityEngine.UI.Image]::new()
$button = [UnityEngine.UI.Button]::new()
$name = [TMPro.TMP_Text]::new()
$proficiency = [TMPro.TMP_Text]::new()
$effect = [UnityEngine.GameObject]::new()
$name.text = '清玄功'
$proficiency.text = '入门'
Set-Private $view 'icon' $icon
Set-Private $view 'techniqueName' $name
Set-Private $view 'proficiency' $proficiency
Set-Private $view 'selectionButton' $button
Set-Private $view 'selectedEffect' $effect
$view.GetType().GetMethod(
    'Awake', [System.Reflection.BindingFlags]'Instance,NonPublic'
).Invoke($view, @()) | Out-Null
Assert-True ($view.TechniqueId -eq '' -and $name.text -eq '' -and
    $proficiency.text -eq '' -and -not $icon.enabled -and -not $effect.activeSelf) `
    'Runtime instantiation must not show the authoring examples.'

$sprite = [UnityEngine.Sprite]::new()
Assert-True ($view.Bind('technique_1', '第一功法', $sprite, '熟练') -and
    $view.TechniqueId -eq 'technique_1' -and $name.text -eq '第一功法' -and
    $proficiency.text -eq '熟练' -and $icon.enabled -and
    [object]::ReferenceEquals($icon.sprite, $sprite) -and $icon.preserveAspect) `
    'Binding authoritative fields must fill the existing prefab components.'
$view.SetSelected($true)
Assert-True $effect.activeSelf 'Selecting a row must display its highlight.'
$script:selected = ''
$handler = [Action[string]] { param($id) $script:selected = $id }
$view.add_Selected($handler)
$button.onClick.Invoke()
Assert-True ($script:selected -eq 'technique_1') `
    'The authored button should select its bound technique.'
Assert-True ($view.Bind('technique_2', $null, $null, $null) -and
    $view.TechniqueId -eq 'technique_2' -and $name.text -eq '--' -and
    $proficiency.text -eq '--' -and -not $icon.enabled -and
    $null -eq $icon.sprite -and -not $effect.activeSelf) `
    'Rebinding a row to another technique must clear the previous content and highlight.'
Assert-True (-not $view.Bind(' ', '伪记录', $sprite, '入门') -and
    $view.TechniqueId -eq '' -and $name.text -eq '' -and
    $proficiency.text -eq '' -and -not $icon.enabled) `
    'An entry without an ID must not be presented as a learned technique.'
$view.Bind('technique_3', '第三功法', $sprite, '精通') | Out-Null
$view.SetSelected($true)
$view.Clear()
$script:selected = ''
$button.onClick.Invoke()
Assert-True ($view.TechniqueId -eq '' -and $name.text -eq '' -and
    $proficiency.text -eq '' -and -not $icon.enabled -and
    $script:selected -eq '' -and -not $effect.activeSelf) `
    'Clearing a recycled item must remove its previous identity and content.'
$view.remove_Selected($handler)

$guid = [regex]::Match($meta, '(?m)^guid: ([0-9a-f]{32})$').Groups[1].Value
Assert-True ($guid -ne '') 'The item view needs a stable Unity script GUID.'
Assert-True ((PrefabBlock 1 6316796519686388324) -match
    'component: \{fileID: 900000000000000101\}') `
    'The existing prefab root must own the view script.'
Assert-True ((PrefabBlock 114 900000000000000101) -match
    "(?s)m_GameObject: \{fileID: 6316796519686388324\}.*?guid: $guid.*?" +
    'icon: \{fileID: 8583750598269087735\}.*?' +
    'techniqueName: \{fileID: 5555846883839880719\}.*?' +
    'selectionButton: \{fileID: 2743756062531043584\}.*?' +
    'selectedEffect: \{fileID: 2718835323047511628\}') `
    'Icon, name, button and highlight must bind the authored prefab components.'
Assert-True ((PrefabBlock 1 2718835323047511628) -match 'm_IsActive: 0' -and
    (PrefabBlock 114 7231660669488851898) -match 'm_RaycastTarget: 0') `
    'The authored highlight must start hidden and let pointer input reach the button.'
Assert-True ($scene -notmatch
    'm_SourcePrefab: \{fileID: 100100000, guid: 5289c52c257a9774d9c1e6e2d7caefa6, type: 3\}') `
    'The scene must not introduce a placeholder instance of the item prefab.'
Write-Output 'PASS: comprehension item bind, clear and prefab wiring.'
