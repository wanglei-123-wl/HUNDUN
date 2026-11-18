$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$constants = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/NetWork/BattleSync/Protocol/BattleSyncProtocolConstants.cs') -Raw
$registrySource = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/NetWork/BattleSync/Domain/BattleEntityRegistry.cs') -Raw
Add-Type -TypeDefinition ($registrySource + "`n" + $constants) -ErrorAction Stop

function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
}

$kind = [Game.Client.BattleSync.BattleEntityKind]::Monster
$statuses = [Game.Client.BattleSync.BattleEntityRegistryStatus]
$registry = [Game.Client.BattleSync.BattleEntityRegistry]::new()
$registry.BeginSession(1, 'world_001')
$instanceId = 'mon_sp42_000001'

$first = [Game.Client.BattleSync.BattleEntityRegistration]::new(
    1, 'world_001', $kind, $instanceId, 501, 3, 100, 1)
Assert-True ($registry.RegisterOrUpdate($first, 1000).Status -eq $statuses::Registered) 'Initial monster registration failed.'
$frame = [Game.Client.BattleSync.BattleEntityStateObservation]::new(
    1, 'world_001', $kind, 501, 3, 101)
Assert-True ($registry.ObserveState($frame).Status -eq $statuses::StateAccepted) 'First life frame was rejected.'

$nextLife = [Game.Client.BattleSync.BattleEntityRegistration]::new(
    1, 'world_001', $kind, $instanceId, 501, 4, 40, 2)
Assert-True ($registry.RegisterOrUpdate($nextLife, 1001).Status -eq $statuses::Updated) 'New life was not registered.'
$active = $null
Assert-True ($registry.TryGetByEntity($kind, $instanceId, [ref]$active) -and
    $active.LastStateServerTick -eq 40 -and $active.SpawnGeneration -eq 2) 'New life must reset its frame tick baseline.'
$newFrame = [Game.Client.BattleSync.BattleEntityStateObservation]::new(
    1, 'world_001', $kind, 501, 4, 41)
Assert-True ($registry.ObserveState($newFrame).Status -eq $statuses::StateAccepted) 'A valid new life frame was rejected.'

$oldRemoval = [Game.Client.BattleSync.BattleEntityRemoval]::new(
    1, 'world_001', $kind, $instanceId, 501, 5, 45, 1, '')
Assert-True ($registry.Remove($oldRemoval, 1002).Status -eq $statuses::StaleSpawnGeneration) 'Old life removal must be ignored.'
$futureRemoval = [Game.Client.BattleSync.BattleEntityRemoval]::new(
    1, 'world_001', $kind, $instanceId, 501, 5, 45, 3, '')
$futureResult = $registry.Remove($futureRemoval, 1003)
Assert-True ($futureResult.Status -eq $statuses::GenerationMismatch -and
    $futureResult.RequiresSnapshotRepair) 'A future life removal must request repair without removing the current life.'
Assert-True ($registry.TryGetByEntity($kind, $instanceId, [ref]$active) -and
    $active.SpawnGeneration -eq 2) 'The current life was removed by a mismatched generation.'

$currentRemoval = [Game.Client.BattleSync.BattleEntityRemoval]::new(
    1, 'world_001', $kind, $instanceId, 501, 5, 45, 2, '')
Assert-True ($registry.Remove($currentRemoval, 1004).Status -eq $statuses::Removed) 'Empty removal reason must be permitted for monsters.'
$oldEntry = [Game.Client.BattleSync.BattleEntityRegistration]::new(
    1, 'world_001', $kind, $instanceId, 502, 6, 46, 1)
Assert-True ($registry.RegisterOrUpdate($oldEntry, 1005).Status -eq $statuses::StaleSpawnGeneration) 'Tombstone must reject a late old life.'
$snapshotPlan = $null
$oldSnapshot = [Game.Client.BattleSync.BattleEntityRegistration[]]@($oldEntry)
Assert-True ($registry.TryPrepareSnapshotReplacement(1, 'world_001', $kind, 10,
    $oldSnapshot, [ref]$snapshotPlan).Status -eq $statuses::StaleSpawnGeneration) 'Stale snapshot must fail before visual application.'
$newEntry = [Game.Client.BattleSync.BattleEntityRegistration]::new(
    1, 'world_001', $kind, $instanceId, 502, 6, 46, 3)
Assert-True ($registry.RegisterOrUpdate($newEntry, 1006).Status -eq $statuses::Registered) 'A later life must pass the tombstone.'
Assert-True ($registry.Remove($currentRemoval, 1007).Status -eq $statuses::StaleSpawnGeneration) 'Late removal must not remove the rebound life.'
$badVisibility = [Game.Client.BattleSync.BattleEntityRegistration]::new(
    1, 'world_001', $kind, $instanceId, 503, 5, 10, 4)
Assert-True ($registry.TryPrepareSnapshotReplacement(1, 'world_001', $kind, 11,
    [Game.Client.BattleSync.BattleEntityRegistration[]]@($badVisibility),
    [ref]$snapshotPlan).Status -eq $statuses::StaleVisibilityRevision) 'A newer generation cannot lower the current visibility revision.'
$rebound = [Game.Client.BattleSync.BattleEntityRegistration]::new(
    1, 'world_001', $kind, $instanceId, 503, 7, 10, 4)
Assert-True ($registry.RegisterOrUpdate($rebound, 1008).Status -eq $statuses::Rebound) 'New life netId rebind failed.'
Assert-True ($registry.TryGetByEntity($kind, $instanceId, [ref]$active) -and
    $active.LastStateServerTick -eq 10) 'Rebound life must reset its frame tick baseline.'
$oldFrame = [Game.Client.BattleSync.BattleEntityStateObservation]::new(
    1, 'world_001', $kind, 502, 6, 47)
Assert-True ($registry.ObserveState($oldFrame).Status -eq $statuses::TombstoneRejected) 'Old short ID must remain a tombstone.'
$reboundFrame = [Game.Client.BattleSync.BattleEntityStateObservation]::new(
    1, 'world_001', $kind, 503, 7, 11)
Assert-True ($registry.ObserveState($reboundFrame).Status -eq $statuses::StateAccepted) 'New short ID frame was rejected.'

Assert-True ($registry.EndSession(1)) 'Session end failed.'
$registry.BeginSession(2, 'world_001')
$reconnected = [Game.Client.BattleSync.BattleEntityRegistration]::new(
    2, 'world_001', $kind, $instanceId, 501, 1, 1, 1)
Assert-True ($registry.RegisterOrUpdate($reconnected, 1009).Status -eq $statuses::Registered) 'Reconnection must clear old short IDs and tombstones.'
Write-Output 'PASS: generation, removal, snapshot preflight, rebind, tombstone, and reconnect registry behavior.'
