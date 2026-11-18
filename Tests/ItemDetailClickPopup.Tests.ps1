$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$trigger = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/UI/ItemDetail/ItemDetailHoverTrigger.cs') -Raw
$service = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/UI/ItemDetail/ItemDetailHoverService.cs') -Raw

function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
}

Assert-True ($trigger -match 'MonoBehaviour, IPointerClickHandler' -and
    $trigger -match 'void OnPointerClick\(PointerEventData eventData\)' -and
    $trigger -notmatch 'IPointerEnterHandler|IPointerExitHandler|void OnPointerExit') `
    'Item details must open on a click, not on hover or pointer exit.'
Assert-True ($trigger -match 'eventData\.button == PointerEventData\.InputButton\.Left' -and
    $trigger -match 'eventData\.clickCount == 1') `
    'Right clicks and the second click of a double click must not toggle details.'
Assert-True ($trigger -match 'hoverService\.Show\(this, detail, screenPosition\)' -and
    $trigger -match 'hoverService\.Hide\(this\)') `
    'Click must show the bound item and clearing a binding must still hide it.'
Assert-True ($trigger -match 'components\[index\] is IPointerClickHandler' -and
    $trigger -match 'ExecuteEvents\.ExecuteHierarchy<IPointerClickHandler>' -and
    $trigger -match 'ExecuteEvents\.pointerClickHandler') `
    'Child detail triggers must preserve other parent click actions.'
Assert-True ($service -match 'openPanels\.ContainsKey\(trigger\)' -and
    $service -match 'Hide\(trigger\)' -and
    $service -match 'Input\.GetKeyDown\(KeyCode\.Escape\)' -and
    $service -match 'HideAll\(\)') `
    'Clicking an open item closes only its panel; Escape closes all panels.'

$quickbar = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Combat/Skills/快捷栏图标显示控制器.cs') -Raw
Assert-True ($quickbar -notmatch 'ItemDetailHoverTrigger|ItemDetailHoverService|BindItem\(|Game\.UI\.ItemDetail' -and
    $quickbar -match '背包运行时\?\.TryUseQuickbarSlot\(图标\.槽位\)' -and
    $quickbar -match '图片\.raycastTarget = true' -and
    $quickbar -match 'IDropHandler, IBeginDragHandler, IDragHandler, IEndDragHandler') `
    'Quickbar must use items and accept drops without any detail popup binding.'

Write-Output 'Item detail click popup checks passed.'
