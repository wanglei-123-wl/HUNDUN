$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$inventory = Get-Content (Join-Path $root 'Assets/Scripts/Inventory/Model/V2/InventoryOwnershipSnapshot.cs') -Raw
$forging = Get-Content (Join-Path $root 'Assets/Scripts/锻造/锻造交互数据.cs') -Raw
$sources = @($inventory, $forging) | ForEach-Object {
    $_ -replace '(?m)^using [^\r\n]+;\r?\n', ''
}
$fixture = @'
using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Inventory.Model.V2;
using UnityEngine;
namespace UnityEngine { public class Sprite {} }
public static class ForgingFixture {
    public static InventoryOwnershipSnapshot Bag(long owner, long revision,
        params InventoryStackState[] stacks) =>
        new InventoryOwnershipSnapshot(owner, revision, 0, 24, 1, 1,
            new List<InventoryStackState>(stacks),
            new List<InventoryEquipmentState>(),
            new Dictionary<string, long>(),
            new InventoryAttributeBonusState(0, 0, 0));
}
'@
Add-Type -TypeDefinition ($fixture + "`n" + ($sources -join "`n")) `
    -IgnoreWarnings | Out-Null

function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
}
function Stack($id, $item, $count) {
    [Game.Inventory.Model.V2.InventoryStackState]::new(
        [long]$id, [string]$item, [int]$count, $false)
}

$choice = [Game.Forging.锻造本地选择]::new()
$bag = [ForgingFixture]::Bag(42, 9,
    (Stack 1 'iron' 3), (Stack 2 'iron' 4), (Stack 3 'fuel' 2))
$recipe = [Game.Forging.锻造图谱]::new()
$recipe.recipeId = 'weapon'
$iron = [Game.Forging.锻造材料需求]::new()
$iron.itemId = 'iron'
$iron.count = 5
$fuel = [Game.Forging.锻造材料需求]::new()
$fuel.itemId = 'fuel'
$fuel.count = 1
$recipe.materials = @($iron, $fuel)

Assert-True (-not $choice.TrySet($bag, 43, 's:1', 'iron', 1) -and
    -not $choice.TrySet($bag, 42, 'e:1', 'iron', 1) -and
    -not $choice.TrySet($bag, 42, 's:1', 'iron', 4) -and
    -not $choice.TrySet($bag, 42, 's:1', 'fuel', 1)) `
    'Cannot select another character, equipment, too many items, or a changed item.'
Assert-True ($choice.TrySet($bag, 42, 's:1', 'iron', 3) -and
    $choice.TrySet($bag, 42, 's:2', 'iron', 2) -and
    $choice.TrySet($bag, 42, 's:3', 'fuel', 1) -and
    $choice.Matches($recipe) -and $choice.IsCurrent($bag, 42)) `
    'Multiple stacks must match exactly while the inventory is current.'
Assert-True ($choice.Materials.Count -eq 3 -and
    $choice.Materials[0].Identity -eq 's:1' -and
    $choice.Materials[1].Identity -eq 's:2') `
    'Selected stacks must keep their display order.'

$changed = [ForgingFixture]::Bag(42, 10,
    (Stack 1 'iron' 3), (Stack 2 'iron' 1), (Stack 3 'fuel' 2))
Assert-True (-not $choice.IsCurrent($changed, 42) -and
    $choice.Sync($changed, 42) -and $choice.Materials.Count -eq 2 -and
    -not $choice.Matches($recipe)) `
    'An inventory change must invalidate the affected selection.'
Assert-True ($choice.Remove('s:1') -and
    -not $choice.Remove('s:1') -and $choice.Materials.Count -eq 1) `
    'Removal must not affect unrelated stacks.'
Assert-True ($choice.Sync($null, 42) -and $choice.Materials.Count -eq 0) `
    'A missing snapshot must clear stale selection.'

$scene = Get-Content (Join-Path $root 'Assets/Scenes/WorldShell.unity') -Raw
$rowMeta = Get-Content (Join-Path $root 'Assets/预制体/格子预制体/锻造图谱预制体.prefab.meta') -Raw
$slotMeta = Get-Content (Join-Path $root 'Assets/预制体/格子预制体/锻造材料格子预制体.prefab.meta') -Raw
$rowGuid = ([regex]::Match($rowMeta, 'guid: (\w+)')).Groups[1].Value
$slotGuid = ([regex]::Match($slotMeta, 'guid: (\w+)')).Groups[1].Value
$panelGuid = ([regex]::Match(
    (Get-Content (Join-Path $root 'Assets/Scripts/锻造/锻造面板视图.cs.meta') -Raw),
    'guid: (\w+)')).Groups[1].Value
$toggleGuid = ([regex]::Match(
    (Get-Content (Join-Path $root 'Assets/Scripts/锻造/锻造窗口开关.cs.meta') -Raw),
    'guid: (\w+)')).Groups[1].Value
Assert-True ($rowGuid -ne $slotGuid -and
    $rowGuid -ne '8a7829139ea2c7b4b885a5b761355aba' -and
    $slotGuid -ne '3e341fa6472bcc845a89c83002e4d3fa' -and
    $scene.Contains("guid: $rowGuid") -and $scene.Contains("guid: $slotGuid") -and
    $scene.Contains("guid: $panelGuid") -and $scene.Contains("guid: $toggleGuid")) `
    'The scene must use its own prefabs and forging components.'
Assert-True ($scene.Contains('guid: 10a7f7691de29204993ae30a96ab01f6') -and
    $scene.Contains('forgingWindow: {fileID: 239332388}')) `
    'The forging background and window toggle must remain wired.'
$toggle = Get-Content (Join-Path $root 'Assets/Scripts/锻造/锻造窗口开关.cs') -Raw
$alchemyToggle = Get-Content (
    Join-Path $root 'Assets/Scripts/Alchemy/Presentation/AlchemyWindowToggle.cs') -Raw
Assert-True ($toggle.Contains('Input.GetKeyDown(KeyCode.Z)') -and
    $toggle.Contains('!GameplayUiInputGuard.IsEditingText') -and
    -not $toggle.Contains('KeyCode.K') -and
    $alchemyToggle.Contains('Input.GetKeyDown(KeyCode.X)')) `
    'Forging must open with Z without changing the alchemy X shortcut.'
$canvas = [regex]::Match($scene,
    '(?s)--- !u!223 &239332391\r?\nCanvas:.*?(?=--- !u!)').Value
Assert-True ($canvas.Contains('m_SortingOrder: ')) `
    'The forging canvas must retain its own scene sorting order.'
$recipeContent = [regex]::Match($scene,
    '(?s)--- !u!224 &120287805\r?\nRectTransform:.*?(?=--- !u!)').Value
$materialContent = [regex]::Match($scene,
    '(?s)--- !u!224 &1097225973\r?\nRectTransform:.*?(?=--- !u!)').Value
Assert-True ($recipeContent.Contains('m_AnchorMax: {x: 1, y: 1}') -and
    $materialContent.Contains('m_AnchorMax: {x: 1, y: 1}')) `
    'Both scrolling contents must span their full viewports.'
$recipeIconRect = [regex]::Match($scene,
    '(?s)--- !u!224 &523011721\r?\nRectTransform:.*?(?=--- !u!)').Value
$requirementsRect = [regex]::Match($scene,
    '(?s)--- !u!224 &1722801170\r?\nRectTransform:.*?(?=--- !u!)').Value
$iconY = [double]([regex]::Match($recipeIconRect,
    'm_AnchoredPosition: \{x: [^,]+, y: ([^}]+)\}').Groups[1].Value)
$iconHeight = [double]([regex]::Match($recipeIconRect,
    'm_SizeDelta: \{x: [^,]+, y: ([^}]+)\}').Groups[1].Value)
$requirementsY = [double]([regex]::Match($requirementsRect,
    'm_AnchoredPosition: \{x: [^,]+, y: ([^}]+)\}').Groups[1].Value)
$requirementsHeight = [double]([regex]::Match($requirementsRect,
    'm_SizeDelta: \{x: [^,]+, y: ([^}]+)\}').Groups[1].Value)
Assert-True ($requirementsY + $requirementsHeight / 2 -lt
    $iconY - $iconHeight / 2) `
    'Required-material text must not overlap the current recipe icon.'
$panel = Get-Content (Join-Path $root 'Assets/Scripts/锻造/锻造面板视图.cs') -Raw
Assert-True ($panel.Contains('锻造数量确认弹窗.Create') -and
    $panel.Contains('LastEscapeFrame != Time.frameCount') -and
    $panel.Contains('taskStatus?.gameObject') -and
    $panel.Contains('RefreshCanvasSorting();') -and
    $panel.Contains('Mathf.Min(normalSortingOrder, bagCanvas.sortingOrder - 1)') -and
    $panel.Contains('FindRecipe(recipeId) == null || task != null') -and
    $panel.Contains('if (task != null || FindRecipe(recipeId) == null') -and
    $panel -notmatch 'Alchemy|SendMessage|SendRequest') `
    'Local UI must retain modal and failed-result interactions without an alchemy or network call.'
$forgingPanel = [regex]::Match($scene,
    '(?s)--- !u!114 &239332393\r?\nMonoBehaviour:.*?(?=--- !u!)').Value
Assert-True ($forgingPanel.Contains("guid: $panelGuid") -and
    $forgingPanel.Contains('requiredMaterials: {fileID: 1722801171}') -and
    $forgingPanel.Contains('closeButton: {fileID: 1081838192}') -and
    $forgingPanel.Contains('taskFill: {fileID: 0}') -and
    $forgingPanel.Contains('taskStatus: {fileID: 0}') -and
    $panel.Contains('EnsureTaskFeedback();') -and
    $panel.Contains('generatedTaskStatus.gameObject.name = "锻造任务状态"') -and
    $panel.Contains('generatedTaskFill.gameObject.name = "锻造任务进度"') -and
    $panel.Contains('180f * taskFill.fillAmount') -and
    $panel.Contains('taskStatus.raycastTarget = task != null && task.ready')) `
    'The real forging canvas must retain editable bindings and create only missing task feedback.'

Write-Output '锻造客户端交互及场景引用检查通过'
