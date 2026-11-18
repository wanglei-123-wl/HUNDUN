$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dto = Get-Content (Join-Path $root `
    'Assets/Scripts/Cultivation/Protocol/CultivationItemCatalogMessages.cs') -Raw
Add-Type -TypeDefinition ($dto + "`n" +
    'public class NetMessageBase { public string msgId; public string type; }') `
    -IgnoreWarnings

function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
}

$options = [System.Text.Json.JsonSerializerOptions]::new()
$options.IncludeFields = $true
$resultType = [Game.Cultivation.Protocol.CultivationItemCatalogResultDto]
$raw = '{"items":[{"itemId":"pill_empty","kind":"pill","effects":[]},' +
    '{"itemId":"pill_usable","kind":"pill","effects":[{"type":"add_jingjie_exp",' +
    '"valueMode":"flat","amount":25}]},' +
    '{"itemId":"potion","kind":"consumable","effects":[]},' +
    '{"itemId":"pill_missing","kind":"pill"},' +
    '{"itemId":"pill_unknown","kind":"pill","effects":[{"type":"unknown",' +
    '"valueMode":"flat","amount":1}]}]}'
$result = [System.Text.Json.JsonSerializer]::Deserialize(
    $raw, $resultType, $options)
Assert-True ($result.items.Length -eq 5 -and
    $result.items[0].effects.Length -eq 0 -and
    -not $result.items[0].HasUsablePillEffect -and
    $result.items[1].HasUsablePillEffect -and
    -not $result.items[2].HasUsablePillEffect -and
    -not $result.items[3].HasUsablePillEffect -and
    -not $result.items[4].HasUsablePillEffect) `
    'Pill eligibility must come from supported, nonempty authoritative effects.'

$inventory = Get-Content (Join-Path $root `
    'Assets/Scripts/Inventory/Integration/V2/InventoryV2Session.cs') -Raw
$panel = Get-Content (Join-Path $root `
    'Assets/Scripts/Inventory/Presentation/InventoryPanelView.cs') -Raw
$presenter = Get-Content (Join-Path $root `
    'Assets/Scripts/Inventory/Presentation/V2/InventoryBagGridPresenter.cs') -Raw
$grid = Get-Content (Join-Path $root `
    'Assets/Scripts/Inventory/Presentation/InventoryGridView.cs') -Raw
Assert-True ($inventory.Contains('item?.kind == "pill" && item.HasUsablePillEffect') -and
    $inventory.Contains('if (!CanUseItem(stack.ItemId))') -and
    $panel.Contains('consumableButton.onClick.AddListener(HandleConsumableClicked)') -and
    $panel.Contains('pillButton.onClick.AddListener(HandlePillClicked)') -and
    -not $panel.Contains('Instantiate(') -and
    -not $panel.Contains('RestoreCategoryLayout') -and
    $presenter.Contains('serverItem?.kind == "pill"') -and
    $presenter.Contains('serverItem?.kind != "consumable" && !isPill') -and
    $presenter.Contains('grid.EnsureCapacity(layout.Capacity)') -and
    $grid.Contains('while (slotViews.Count < capacity)') -and
    $grid.Contains('slotView = Instantiate(') -and
    $grid.Contains('slotPrefab,') -and
    $presenter.Contains('public void RefreshCatalog()') -and
    $inventory.Contains('presenter.RefreshCatalog()')) `
    'The original consumable behavior must remain unchanged; pills stay visible there without changing grid or item-use gates.'
$scene = Get-Content (Join-Path $root 'Assets/Scenes/WorldShell.unity') -Raw
Assert-True ($scene.Contains('allButton: {fileID: 1010781138}') -and
    $scene.Contains('equipmentButton: {fileID: 2135118446}') -and
    $scene.Contains('consumableButton: {fileID: 40206249}') -and
    $scene.Contains('pillButton: {fileID: 1568040119}') -and
    $scene.Contains('otherButton: {fileID: 299520029}')) `
    'WorldShell must bind the consumables, pill and other category buttons.'
Write-Output 'PASS: pill effect DTO, WorldShell bag tabs, grid and item-use gate.'
