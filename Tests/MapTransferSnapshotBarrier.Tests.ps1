$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$sources = @(
    'Assets/Scripts/NetWork/BattleSync/Core/BattleSyncSessionState.cs'
    'Assets/Scripts/NetWork/BattleSync/Core/InitialSnapshotBarrier.cs'
) | ForEach-Object {
    (Get-Content -LiteralPath (Join-Path $root $_) -Raw) `
        -replace '(?m)^using [^\r\n]+;\r?\n', ''
}
Add-Type -TypeDefinition ("using System;`nusing System.Collections.Generic;`n" +
    ($sources -join "`n")) -IgnoreWarnings | Out-Null

function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
}

$session = [Game.Client.BattleSync.BattleSyncSessionState]::new()
$barrier = [Game.Client.BattleSync.InitialSnapshotBarrier]::new(
    [Game.Client.BattleSync.InitialSnapshotKind]::World)
$session.BeginConnection(9) | Out-Null
$session.MarkEnterWorldSent(9) | Out-Null
$session.BeginInitialSnapshotBatch(9, 'world_002', 42, 7) | Out-Null
$session.CompleteSnapshotBatch(9, 'world_002', 7) | Out-Null
Assert-True ($session.IsReady) 'The source scene must be ready before transfer.'
$transition = $session.BeginMapTransferSnapshotBatch(9, 'world_001', 42, 11)
Assert-True ($transition.IsSuccess -and $session.SceneId -eq 'world_001' -and
    $session.PendingSnapshotRevision -eq 11 -and
    $session.Phase -eq [Game.Client.BattleSync.BattleSyncSessionPhase]::InitializingSnapshots) `
    'A committed transfer must start the destination batch on the same connection.'

$required = [Game.Client.BattleSync.InitialSnapshotKind]::World -bor
    [Game.Client.BattleSync.InitialSnapshotKind]::Monster -bor
    [Game.Client.BattleSync.InitialSnapshotKind]::Loot -bor
    [Game.Client.BattleSync.InitialSnapshotKind]::Combat
$barrier.BeginBatch(9, 'world_001', 11, 100, $required) | Out-Null
foreach ($kind in @('World', 'Monster', 'Loot')) {
    $receipt = [Game.Client.BattleSync.InitialSnapshotReceipt]::new(9, 'world_001',
        [Game.Client.BattleSync.InitialSnapshotKind]::$kind, 11, 8, 200,
        'map_transfer', 101)
    Assert-True ($barrier.MarkApplied($receipt).IsAccepted -and
        -not $barrier.IsComplete) `
        'World, monster and loot alone cannot release the transfer barrier.'
}
$stale = [Game.Client.BattleSync.InitialSnapshotReceipt]::new(9, 'world_002',
    [Game.Client.BattleSync.InitialSnapshotKind]::Combat, 11, 8, 200,
    'map_transfer', 102)
Assert-True (-not $barrier.MarkApplied($stale).IsAccepted) `
    'A source-scene packet cannot finish the destination batch.'
$combat = [Game.Client.BattleSync.InitialSnapshotReceipt]::new(9, 'world_001',
    [Game.Client.BattleSync.InitialSnapshotKind]::Combat, 11, 8, 200,
    'map_transfer', 102)
Assert-True ($barrier.MarkApplied($combat).IsComplete -and
    $session.CompleteSnapshotBatch(9, 'world_001', 11).IsSuccess -and
    $session.IsReady -and $session.ConnectionEpoch -eq 9) `
    'All destination snapshots must complete before control resumes.'

$next = $session.BeginMapTransferSnapshotBatch(9, 'world_002', 42, 15)
Assert-True ($next.IsSuccess -and $session.SceneId -eq 'world_002') `
    'Returning to a previously visited map must still create a new batch.'
Assert-True (-not $session.BeginMapTransferSnapshotBatch(9, 'world_001', 42, 16).IsSuccess) `
    'An overlapping transfer must be rejected.'

$coordinator = Get-Content -LiteralPath (Join-Path $root `
    'Assets/Scripts/NetWork/BattleSync/Core/BattleSyncCoordinator.cs') -Raw
$game = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/GameSession.cs') -Raw
$transferMethod = [regex]::Match($coordinator,
    '(?s)public BattleSyncCoordinatorResult HandleMapTransferAccepted\(.*?(?=public BattleSyncCoordinatorResult SeedInitialPlayers)').Value
Assert-True ($transferMethod.Contains('!TryBeginModule(moduleBindings.Combat,') -and
    -not $transferMethod.Contains('moduleBindings.Inventory') -and
    $coordinator.Contains('mapTransferInitializing') -and
    $game.Contains('BeginDeferredMapTransferMessages();') -and
    $game.Contains('ReplayDeferredWorldMessages();') -and
    $game.Contains('CompleteCommittedTransfer();')) `
    'The transfer must retain business modules and replay after the new scene is ready.'

$lootRuntime = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/Loot/Integration/LootModuleRuntime.cs') -Raw
$switchLootScene = [regex]::Match($lootRuntime,
    '(?s)public BattleSyncApplyResult SwitchScene\(.*?(?=public BattleSyncApplyResult ApplyLootSnapshot)').Value
Assert-True ($transferMethod.Contains('loot.SwitchScene(moduleContext)') -and
    $transferMethod -notmatch '(EndModule|TryBeginModule)\(moduleBindings\.Loot' -and
    $switchLootScene.Contains('context.ConnectionEpoch != activeConnectionEpoch') -and
    $switchLootScene.Contains('LeaveScene(currentSceneId);') -and
    $switchLootScene.Contains('EnterScene(context.SceneId);') -and
    -not $switchLootScene.Contains('ResetConnectionState')) `
    'The transfer must clear old ground loot and keep the connection-owned pickup sequence and pending results.'

Write-Output 'PASS: same-connection transfer barrier, scene isolation and repeat-map generation.'
