$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$panel = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/UI/ItemDetail/ItemDetailPanelController.cs') -Raw
$drag = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/UI/ItemDetail/ItemDetailPanelDragHandle.cs') -Raw
$scene = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scenes/WorldShell.unity') -Raw

function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
}

Assert-True (-not $scene.Contains('1839745227') -and
    -not $scene.Contains('3002000001')) `
    'WorldShell must omit the legacy detail drag area.'
Assert-True ($panel -match 'panelRect\.Find\("拖动区域"\)' -and
    $panel -match 'handle\.gameObject\.AddComponent<ItemDetailPanelDragHandle>\(\)' -and
    $panel -match 'handle\.SetAsLastSibling\(\)' -and
    $panel -match 'image\.canvasRenderer\.cullTransparentMesh = false') `
    'Existing scene handles and prefab instances without a handle must both be supported.'
Assert-True ($panel -match 'visualCanvasGroup\.blocksRaycasts = visible' -and
    $panel -match 'visualCanvasGroup\.interactable = visible' -and
    $panel -match 'visualCanvasGroup\.alpha = visible \? 1f : 0f') `
    'Only the visible detail panel may receive UI input.'
Assert-True ($drag -match 'IBeginDragHandler, IDragHandler, IEndDragHandler' -and
    $drag -match 'eventData\.button != PointerEventData\.InputButton\.Left' -and
    $drag -match 'ScreenPointToLocalPointInRectangle' -and
    $drag -match 'pointerOffset') `
    'Left-button dragging must retain the pointer offset in canvas coordinates.'
Assert-True ($drag -match 'IPointerClickHandler' -and
    $drag -match 'eventData\.button == PointerEventData\.InputButton\.Right' -and
    $drag -match 'panel\.RequestClose\(\)') `
    'Only a right-click on the drag handle may request panel closure.'
Assert-True ($panel -match 'panelRect\.GetWorldCorners\(panelCorners\)' -and
    $panel -match 'ClampToCanvas\(\)' -and
    $panel -notmatch 'CalculateRelativeRectTransformBounds\(parent, panelRect\)') `
    'Positioning must clamp the panel rectangle, not masked scroll contents.'

Write-Output 'Item detail drag checks passed.'
