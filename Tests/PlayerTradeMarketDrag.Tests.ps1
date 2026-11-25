$ErrorActionPreference = 'Stop'
# Compile and exercise the shared handler's screen-space Canvas behavior first.
. (Join-Path $PSScriptRoot '修炼界面关闭与拖动.Tests.ps1')

$meta = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/UI/WindowDragHandle.cs.meta') -Raw
$guid = [regex]::Match($meta, 'guid: ([0-9a-f]{32})').Groups[1].Value
Assert-True ((Block 1 '580423508').Contains('component: {fileID: 860000000000400021}') -and
    (Block 114 '860000000000400021').Contains("guid: $guid") -and
    (Block 114 '860000000000400021').Contains('panelRoot: {fileID: 850208458}') -and
    (Block 224 '580423509').Contains('m_Father: {fileID: 850208458}') -and
    (Block 114 '580423510').Contains('m_RaycastTarget: 1')) `
    'The market header must bind the market background container and receive pointer events.'

$backgroundObject = [UnityEngine.GameObject]::new()
$background = [UnityEngine.RectTransform]::new()
$backgroundObject.transform = $background
$background.gameObject = $backgroundObject
$background.parent = $canvas
$background.anchoredPosition = [UnityEngine.Vector2]::new(42, 64)
$marketHeader = [UnityEngine.GameObject]::new()
$marketHeader.transform.parent = $background
$marketDrag = [Game.UI.WindowDragHandle]::new()
$marketDrag.gameObject = $marketHeader
$rootField = $marketDrag.GetType().GetField('panelRoot', $flags)
$rootField.SetValue($marketDrag, $background)

$marketDrag.OnBeginDrag((Pointer $marketHeader 5 8 $left))
$marketDrag.OnDrag((Pointer $marketHeader 13 20 $left))
Assert-True ($background.anchoredPosition.x -eq 50 -and $background.anchoredPosition.y -eq 76) `
    'Market background must move in its stable parent coordinate space.'
$marketDrag.OnDrag((Pointer $marketHeader 17 24 $left))
Assert-True ($background.anchoredPosition.x -eq 54 -and $background.anchoredPosition.y -eq 80) `
    'Repeated drag samples must use the original grab offset without accumulating drift.'
$marketDrag.OnEndDrag((Pointer $marketHeader 17 24 $left))
$marketDrag.OnDrag((Pointer $marketHeader 40 50 $left))
Assert-True ($background.anchoredPosition.x -eq 54 -and $background.anchoredPosition.y -eq 80) `
    'Release must stop the market window.'

$marketDrag.OnBeginDrag((Pointer $marketHeader 0 0 $right))
$marketDrag.OnDrag((Pointer $marketHeader 10 20 $right))
Assert-True ($background.anchoredPosition.x -eq 54) 'Right drag must not move the market.'
$marketButton = [UnityEngine.GameObject]::new()
$marketButton.component = [UnityEngine.UI.Button]::new()
$marketButton.transform.parent = $background
$marketDrag.OnBeginDrag((Pointer $marketButton 0 0 $left))
$marketDrag.OnDrag((Pointer $marketButton 20 30 $left))
Assert-True ($background.anchoredPosition.x -eq 54) 'Sibling buttons and lists must not start a window drag.'

$rootField.SetValue($marketDrag, $null)
$marketDrag.OnBeginDrag((Pointer $marketHeader 0 0 $left))
$marketDrag.OnDrag((Pointer $marketHeader 2 3 $left))
Assert-True ($background.anchoredPosition.x -eq 56 -and $background.anchoredPosition.y -eq 83) `
    'Unbound existing instances must resolve the immediate market background parent.'
$marketDrag.GetType().GetMethod('OnDisable', $flags).Invoke($marketDrag, @()) | Out-Null
$marketDrag.OnDrag((Pointer $marketHeader 30 40 $left))
Assert-True ($background.anchoredPosition.x -eq 56 -and $background.anchoredPosition.y -eq 83) `
    'Disabling the market must cancel an in-progress drag.'

Write-Output 'PASS: market scene binding, parent-space drag, grab offset, release, right-button isolation and disable cleanup.'
