$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$runtime = Get-Content (Join-Path $root 'Assets/Scripts/Inventory/Integration/InventoryModuleRuntime.cs') -Raw
$session = Get-Content (Join-Path $root 'Assets/Scripts/Inventory/Integration/V2/InventoryV2Session.cs') -Raw
$coordinator = Get-Content (Join-Path $root 'Assets/Scripts/NetWork/BattleSync/Core/BattleSyncCoordinator.cs') -Raw
$presenter = Get-Content (Join-Path $root 'Assets/Scripts/Inventory/Presentation/V2/InventoryBagGridPresenter.cs') -Raw
$gameSession = Get-Content (Join-Path $root 'Assets/Scripts/GameSession.cs') -Raw

if ($runtime -match 'new InventoryModule\(' -or
    $runtime -match 'new InventorySnapshotRequestDto\(' -or
    $runtime -match 'useV2InventoryProtocol' -or
    $coordinator -match 'Register\(BattleSyncProtocolConstants.MessageIds.InventorySnapshotResult') {
    throw 'The running inventory path must not initialize or subscribe to legacy snapshots.'
}
if ($runtime -notmatch 'private void Awake\(' -or
    $runtime -notmatch 'new InventoryV2Session\(' -or
    $session -notmatch 'dispatcher.Register\(InventoryV2Protocol.SnapshotResult' -or
    $session -notmatch 'header.requestSequence == pendingSequence' -or
    $session -notmatch 'header.requestSequence > 0 && !requested' -or
    $presenter -notmatch 'grid.EnsureCapacity\(layout.Capacity\)') {
    throw 'V2 receiver must register before Start, accept unsolicited snapshots and render capacity.'
}
if ($gameSession -notmatch 'EquipmentOperationSessionId = result.equipmentOperationSessionId' -or
    $runtime -notmatch 'session.EquipmentOperationSessionId' -or
    $session -notmatch 'equipmentOperationSessionId = activeEquipmentOperationSessionId' -or
    $session -notmatch 'nextEquipmentOperationSequence = 0') {
    throw 'The accepted equipment session ID must reach inventory and reset with a new world session.'
}
if ($gameSession -notmatch 'WorldDropOperationSessionId = result.worldDropOperationSessionId' -or
    $runtime -notmatch 'session.WorldDropOperationSessionId' -or
    $session -notmatch 'worldDropOperationSessionId = activeWorldDropOperationSessionId' -or
    $session -notmatch 'nextWorldDropOperationSequence = 0' -or
    $session -notmatch 'dispatcher.Register\(InventoryV2Protocol.WorldDropResult') {
    throw 'World-drop session, independent sequence and result handler must be wired.'
}

Write-Output 'PASS: inventory entry, pushed snapshot, equipment and world-drop sessions.'
