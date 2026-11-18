$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$scene = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scenes/WorldShell.unity') -Raw
$service = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/UI/ItemDetail/ItemDetailHoverService.cs') -Raw

function Assert-True([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}

$popupGuid = 'c20a996ad376be34983ba2388da4bc68'
$serviceGuid = '0b8cccc488b94b6eb25283ada4780e8f'
Assert-True (-not $scene.Contains($popupGuid) -and
    -not $scene.Contains($serviceGuid) -and
    -not $scene.Contains('3002000000') -and
    -not $scene.Contains('3002000100') -and
    -not $scene.Contains('1839745227')) `
    'WorldShell must not retain the old popup, its service or its drag child.'
Assert-True ($service.Contains('IsConfiguredInScene(Component source)') -and
    $service.Contains('service.gameObject.scene == source.gameObject.scene')) `
    'Legacy popup triggers must be scoped to a configured scene.'

foreach ($path in @(
    'Assets/Scripts/Inventory/Presentation/InventorySlotView.cs',
    'Assets/Scripts/Inventory/Presentation/V2/InventoryEquipmentSlotView.cs',
    'Assets/Scripts/Combat/Skills/技能栏槽位视图.cs',
    'Assets/Scripts/PlayerTrade/Presentation/PlayerTradeSellPanelController.cs',
    'Assets/Scripts/PlayerTrade/Presentation/PlayerTradeListingCardView.cs',
    'Assets/Scripts/PlayerTrade/Presentation/PlayerTradeClaimCardView.cs'
)) {
    $source = Get-Content -LiteralPath (Join-Path $root $path) -Raw
    Assert-True ($source.Contains('ItemDetailHoverService.IsConfiguredInScene(this)')) `
        "The legacy trigger must be gated in $path."
}

Write-Output 'PASS: WorldShell legacy popup removed and triggers remain scene-scoped.'
