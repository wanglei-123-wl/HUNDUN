$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$scene = Get-Content -LiteralPath (Join-Path $root 'Assets/Scenes/WorldShell.unity') -Raw
$view = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Inventory/Presentation/V2/InventorySelectedItemDetailPanel.cs') -Raw
$session = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Inventory/Integration/V2/InventoryV2Session.cs') -Raw
$runtime = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Inventory/Integration/InventoryModuleRuntime.cs') -Raw

function Assert-True([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}
function Block([int]$type, [string]$id) {
    $pattern = "(?ms)^--- !u!$type &$id`r?`n(.*?)(?=^--- !u!|\z)"
    $matches = [regex]::Matches($scene, $pattern)
    Assert-True ($matches.Count -eq 1) "Scene block $id must exist exactly once."
    return $matches[0].Value
}

$component = Block 114 '1536511460'
$learn = Block 114 '712745389'
$use = Block 114 '1824379789'
$sell = Block 114 '1433993389'
Assert-True ($component.Contains('learnButton: {fileID: 712745389}') -and
    $component.Contains('useButton: {fileID: 1824379789}') -and
    $learn.Contains('guid: 4e29b1a8efbd4b44bb3f3716e73f07ff') -and
    $use.Contains('guid: 4e29b1a8efbd4b44bb3f3716e73f07ff') -and
    $sell.Contains('m_Calls: []')) `
    'WorldShell must bind the existing learn/use buttons without changing sell button callbacks.'
Assert-True ($view.Contains('LearnRequested?.Invoke(identity);') -and
    $view.Contains('UseRequested?.Invoke(identity);') -and
    $view.Contains('grid?.SelectedV2Slot?.V2Identity') -and
    $view.Contains('learnButton.onClick.RemoveListener(HandleLearnClicked);') -and
    $view.Contains('useButton.onClick.RemoveListener(HandleUseClicked);')) `
    'Detail buttons must use current selection and clean up listeners.'
Assert-True ($runtime.Contains('panelView.GetComponent<InventorySelectedItemDetailPanel>()') -and
    $session.Contains('selectedDetailPanel.LearnRequested += HandleLearnRequested;') -and
    $session.Contains('selectedDetailPanel.UseRequested += HandleUseRequested;') -and
    $session.Contains('selectedDetailPanel.LearnRequested -= HandleLearnRequested;') -and
    $session.Contains('selectedDetailPanel.UseRequested -= HandleUseRequested;') -and
    $session.Contains('HandleStackActivation(identity, allowLearn: true, allowUse: true);') -and
    $session.Contains('HandleStackActivation(identity, allowLearn: true, allowUse: false, allowSkillLearn: true);') -and
    $session.Contains('HandleStackActivation(identity, allowLearn: false, allowUse: true);')) `
    'Button requests must share the existing double-click logic with separate action types.'
Write-Output 'PASS: WorldShell learn/use buttons route selected identity to existing V2 action logic.'
