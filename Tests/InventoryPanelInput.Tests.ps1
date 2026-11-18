$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$captureSource = Get-Content (Join-Path $root 'Assets/Scripts/CharacterPanel/Presentation/CharacterPanelPointerCapture.cs') -Raw
Add-Type -TypeDefinition $captureSource

function Assert-Equal([bool]$actual, [bool]$expected, [string]$message) {
    if ($actual -ne $expected) { throw $message }
}

$capture = [Game.CharacterPanel.Presentation.CharacterPanelPointerCapture]::new()
Assert-Equal ($capture.Sample($false, $false, 0, 0, 0)) $false 'Closed inventory must allow world input.'
Assert-Equal ($capture.Sample($true, $false, 0, 0, 0)) $false 'Outside inventory must allow world input.'
Assert-Equal ($capture.Sample($true, $true, 0, 0, 0)) $true 'Hovering inventory must block keyboard and camera input.'
Assert-Equal ($capture.Sample($true, $true, 1, 1, 0)) $true 'Category button press must be captured.'
Assert-Equal ($capture.Sample($true, $false, 1, 0, 0)) $true 'Dragging outside inventory must remain blocked.'
Assert-Equal ($capture.Sample($false, $false, 0, 0, 1)) $true 'Closing on release must not trigger world attack.'
Assert-Equal ($capture.Sample($false, $false, 0, 0, 0)) $false 'World input resumes after release.'

$panelSource = Get-Content (Join-Path $root 'Assets/Scripts/Inventory/Presentation/InventoryPanelView.cs') -Raw
$guardSource = Get-Content (Join-Path $root 'Assets/Scripts/Player/GameplayUiInputGuard.cs') -Raw
$marketSource = Get-Content (Join-Path $root 'Assets/Scripts/PlayerTrade/Presentation/PlayerTradeMarketToggleController.cs') -Raw
$scene = Get-Content (Join-Path $root 'Assets/Scenes/WorldShell.unity') -Raw
if ($panelSource -notmatch 'ActivePanel = this' -or $panelSource -notmatch 'ActivePanel = null') {
    throw 'Inventory panel must register only while visible.'
}
if ($guardSource -notmatch 'InventoryPanelView.ActivePanel' -or
    $guardSource -notmatch 'CharacterPanelInputGuard.BlocksGameplayInput' -or
    $guardSource -notmatch 'inventoryCapture.Sample') {
    throw 'Gameplay guard must combine character and inventory pointer capture.'
}
if ($marketSource -notmatch 'ActiveMarket = this' -or
    $marketSource -notmatch 'ActiveMarket == this\) ActiveMarket = null' -or
    $guardSource -notmatch 'PlayerTradeMarketToggleController.ActiveMarket' -or
    $guardSource -notmatch 'market\.IsVisible' -or
    $guardSource -notmatch 'EventSystem.current' -or
    $guardSource -notmatch 'RaycastAll\(pointerEventData, pointerHits\)' -or
    $guardSource -notmatch 'hitTransform.IsChildOf\(panel.transform\)' -or
    $guardSource -notmatch 'hitTransform.IsChildOf\(market.MarketRoot\)' -or
    $guardSource -notmatch 'marketCapture.Sample') {
    throw 'Market and inventory must capture pointer presses on their visible UI descendants.'
}
if ($scene -notmatch '(?s)--- !u!1 &1523190445\r?\n(?:(?!--- !u!).)*m_IsActive: 0' -or
    $scene -notmatch '(?s)--- !u!223 &1523190448\r?\n(?:(?!--- !u!).)*m_Enabled: 1(?:(?!--- !u!).)*m_RenderMode: 0' -or
    $scene -notmatch '(?s)--- !u!223 &118479043\r?\n(?:(?!--- !u!).)*m_Enabled: 1(?:(?!--- !u!).)*m_RenderMode: 0' -or
    $scene -notmatch '(?s)--- !u!223 &1966549455\r?\n(?:(?!--- !u!).)*m_Enabled: 1(?:(?!--- !u!).)*m_RenderMode: 0' -or
    $scene -notmatch '(?s)--- !u!1 &1966549451\r?\n(?:(?!--- !u!).)*- component: \{fileID: 916274839204\}' -or
    $scene -notmatch '(?s)--- !u!114 &916274839204\r?\n(?:(?!--- !u!).)*m_GameObject: \{fileID: 1966549451\}(?:(?!--- !u!).)*marketPanel: \{fileID: 1523190445\}' -or
    $scene -match '(?s)--- !u!1 &1523190445\r?\n(?:(?!--- !u!).)*- component: \{fileID: 916274839204\}') {
    throw 'Scene must keep market hidden with enabled screen-space canvases and host its toggle on the visible HUD.'
}
$marketCapture = [Game.CharacterPanel.Presentation.CharacterPanelPointerCapture]::new()
Assert-Equal ($marketCapture.Sample($false, $true, 1, 1, 0)) $false 'Hidden market must not block world input.'
Assert-Equal ($marketCapture.Sample($true, $true, 1, 1, 0)) $true 'Market scroll press must be captured.'
Assert-Equal ($marketCapture.Sample($true, $false, 1, 0, 0)) $true 'Market drag outside must remain captured.'
Assert-Equal ($marketCapture.Sample($true, $false, 0, 0, 1)) $true 'Market release frame must remain blocked.'
Assert-Equal ($marketCapture.Sample($true, $false, 0, 0, 0)) $false 'World input resumes after market release.'
foreach ($path in @(
    'Assets/Scripts/Player/AuthorityMovement/LocalPlayerInputController.cs',
    'Assets/Scripts/Player/ThirdPersonCameraFollow.cs',
    'Assets/Scripts/Loot/Interaction/LootClickBridge.cs'
)) {
    $source = Get-Content (Join-Path $root $path) -Raw
    if ($source -notmatch 'GameplayUiInputGuard.BlocksGameplayInput') {
        throw "$path must use the shared gameplay input guard."
    }
}

Write-Output 'PASS: inventory and market hover, drag and release boundaries; gameplay input wiring.'
