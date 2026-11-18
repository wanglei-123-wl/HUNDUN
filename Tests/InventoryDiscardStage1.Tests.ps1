$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$rules = Get-Content (Join-Path $root 'Assets/Scripts/Inventory/Model/V2/InventoryWorldDropRules.cs') -Raw
Add-Type -TypeDefinition $rules
function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
}
$type = [Game.Inventory.Model.V2.InventoryWorldDropRules]
Assert-True ($type::ShouldOffer($true, $true, $false, $false, $false)) `
    'Only a bag drag released outside all UI should offer world drop.'
foreach ($cases in @(
    @($false, $true, $false, $false, $false),
    @($true, $false, $false, $false, $false),
    @($true, $true, $true, $false, $false),
    @($true, $true, $false, $true, $false),
    @($true, $true, $false, $false, $true)
)) {
    Assert-True (-not $type::ShouldOffer($cases[0], $cases[1],
        $cases[2], $cases[3], $cases[4])) 'Protected drop must not offer world drop.'
}
$drag = Get-Content (Join-Path $root 'Assets/Scripts/Inventory/Presentation/V2/InventoryBagDragSlot.cs') -Raw
$presenter = Get-Content (Join-Path $root 'Assets/Scripts/Inventory/Presentation/V2/InventoryBagGridPresenter.cs') -Raw
$session = Get-Content (Join-Path $root 'Assets/Scripts/Inventory/Integration/V2/InventoryV2Session.cs') -Raw
$equipment = Get-Content (Join-Path $root 'Assets/Scripts/Inventory/Presentation/V2/InventoryEquipmentSlotView.cs') -Raw
$view = Get-Content (Join-Path $root 'Assets/Scripts/Inventory/Presentation/V2/InventoryDiscardConfirmationView.cs') -Raw
Assert-True ($drag.Contains('InventoryWorldDropRules.ShouldOffer(') -and
    $drag.Contains('EventSystem.current.RaycastAll(') -and
    $drag.Contains('WorldDropOffered?.Invoke(releasedIdentity)') -and
    -not $equipment.Contains('WorldDropOffered')) 'World drop must start only from the bag drag.'
Assert-True ($presenter.Contains('WorldDropOffered?.Invoke(identity)') -and
    $session.Contains('presenter.WorldDropOffered += HandleWorldDropOffered;') -and
    -not $session.Contains('discardConfirmation.Confirmed += HandlePermanentDiscardConfirmed;') -and
    $session.Contains('discardConfirmation.Confirmed += HandleStackWorldDropConfirmed;')) `
    'A bag world-drop offer must not be wired to permanent inventory discard.'
Assert-True ($view.Contains('SetConfirmEnabled(false)') -and
    $view.Contains('quantity = maximum') -and
    $view.Contains('if (!pending && Input.GetKeyDown(KeyCode.Escape)) Hide()')) `
    'The quantity dialog must remain non-committal until the world-drop protocol is connected.'
Write-Output 'PASS: bag-only outside release, guarded UI drops and no permanent discard route.'
