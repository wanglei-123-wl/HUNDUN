$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$scene = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scenes/WorldShell.unity') -Raw
$prefabMeta = Get-Content -LiteralPath (
    Join-Path $root 'Assets/预制体/格子预制体/背包格子预制体.prefab.meta') -Raw
$slot = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Inventory/Presentation/InventorySlotView.cs') -Raw
$grid = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Inventory/Presentation/InventoryGridView.cs') -Raw
$presenter = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Inventory/Presentation/V2/InventoryBagGridPresenter.cs') -Raw
$drag = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Inventory/Presentation/V2/InventoryBagDragSlot.cs') -Raw

function Assert-True([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}

Assert-True ($prefabMeta -match 'guid: d51e73fbb1064584f9d68da46fa9db74' -and
    $scene -match 'slotPrefab: \{fileID: 860000000000500010, guid: d51e73fbb1064584f9d68da46fa9db74, type: 3\}' -and
    $scene -notmatch 'slotPrefab: \{fileID: 8319775690080126274, guid: a718fe8eaec4fdd46b7b497874249ec7, type: 3\}' -and
    $scene -match '(?s)m_GameObject: \{fileID: 528144964\}.*?m_CellSize: \{x: 75, y: 75\}\s+m_Spacing: \{x: 10, y: 10\}') `
    'WorldShell must use the new slot without changing the user grid layout.'

Assert-True ($slot -match '(?s)private void HandleButtonClicked\(\)\s*\{\s*if \(!HasItem\).*?Clicked\?\.Invoke\(this\)' -and
    $slot.Contains('public bool HasItem => Item != null || V2Identity != null;') -and
    $grid.Contains('slotView.Clicked += HandleSlotClicked;') -and
    $grid.Contains('selectedV2Identity = clickedSlot.V2Identity;') -and
    $grid.Contains('string.Equals(selectedV2Identity, slotView.V2Identity,') -and
    $grid.Contains('if (selectedSlot == null) selectedV2Identity = null;') -and
    $grid.Contains('if (clickedSlot.Item != null)') -and
    $grid.Contains('SlotClicked?.Invoke(clickedSlot);') -and
    $presenter.Contains('grid.RestoreV2Selection();')) `
    'V2 click and refresh selection must work while the V1 selection callback stays V1-only.'

Assert-True ($slot.Contains('if (itemIcon != null)') -and
    $slot.Contains('itemIcon.gameObject.SetActive(icon != null);') -and
    $slot.Contains('stackCountText.gameObject.SetActive(count > 1);') -and
    $slot.Contains('SetSelected(false);') -and
    $slot.Contains('event Action<InventorySlotView> V2DoubleClicked;') -and
    $presenter.Contains('view.V2DoubleClicked += HandleDoubleClick;') -and
    $presenter.Contains('drag = view.gameObject.AddComponent<InventoryBagDragSlot>();') -and
    $drag.Contains('DropRequested?.Invoke(source.displayIndex, displayIndex, source.dragIdentity);') -and
    $drag.Contains('UnequipRequested?.Invoke(equipment.SlotCode, displayIndex, equipment.DragIdentity);') -and
    -not $presenter.Contains('InventoryBagNewItemTracker') -and
    -not $presenter.Contains('view.SetNew(') -and
    -not $presenter.Contains('QualityColor')) `
    'Icon, count, double click and drag must remain without new/quality extras.'

Write-Output 'PASS: WorldShell UI-only slot, V2 selection, icon/count, drag and double-click wiring.'
