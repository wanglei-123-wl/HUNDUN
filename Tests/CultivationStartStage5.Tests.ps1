$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'CultivationLearnStage4.Tests.ps1')

$network.ConnectionEpoch = 6
$owner.CultivationOperationSessionId = [Guid]::NewGuid().ToString('D')
$runtime.BeginSession($owner, 51, $owner.CultivationOperationSessionId)
$stone = [Game.Inventory.Model.V2.InventoryStackState]::new()
$stone.StackId = 9100
$stone.ItemId = 'stone'
$stone.Count = 1
$inventory.CurrentSnapshot.InventoryRevision = 21
$inventory.CurrentSnapshot.Stacks.Add($stone)
$learnedAutomatic = $automatic.Replace($baseSnapshot, $learned)
[NetMessageDispatcher]::Instance.Dispatch('cultivation_snapshot_result',
    $learnedAutomatic)
$runtime.Refresh()
$queries = @($network.Sent | ForEach-Object { $_ | ConvertFrom-Json })
$catalogQuery = @($queries | Where-Object {
    $_.msgId -eq 'cultivation_catalog' })[-1]
$snapshotQuery = @($queries | Where-Object {
    $_.msgId -eq 'cultivation_snapshot' })[-1]
$itemQuery = @($queries | Where-Object {
    $_.msgId -eq 'item_catalog' })[-1]
$startCatalog = $catalog.Replace('"referenceComprehension":0',
    '"referenceComprehension":100').Replace('insight_stone', 'stone')
[NetMessageDispatcher]::Instance.Dispatch('cultivation_snapshot_result',
    $learnedAutomatic.Replace('"requestSequence":0',
        '"requestSequence":' + $snapshotQuery.requestSequence))
[NetMessageDispatcher]::Instance.Dispatch('cultivation_catalog_result',
    $startCatalog.Replace('"requestSequence":' + $catalogSequence,
        '"requestSequence":' + $catalogQuery.requestSequence))
[NetMessageDispatcher]::Instance.Dispatch('item_catalog_result',
    ('{"msgId":"item_catalog_result","type":"item_catalog_result",' +
        '"requestSequence":' + $itemQuery.requestSequence +
        ',"success":true,"catalogVersion":1,"items":' + $learnItems +
        ',"serverTime":1900}'))

$next = $null
$duration = 0L
Assert-True ($runtime.TryGetNextLevel('xuanqing', [ref]$next) -and
    $next.level -eq 1 -and $next.materials[0].itemId -eq 'stone' -and
    [Game.Cultivation.Runtime.CultivationSessionRuntime]::
        TryEstimateDurationSeconds($next, 15, [ref]$duration) -and
    $duration -eq 192000 -and -not $runtime.CanStart('xuanqing')) `
    'Next-level recipe and comprehension estimate come from the catalog, not the client.'
$fixed = [Game.Cultivation.Protocol.CultivationLevelDto]::new()
$fixed.durationSeconds = 28800
Assert-True ([Game.Cultivation.Runtime.CultivationSessionRuntime]::
    TryEstimateDurationSeconds($fixed, 0, [ref]$duration) -and
    $duration -eq 28800) `
    'A null reference-comprehension configuration retains its fixed duration.'
$zeroComprehension = 0L
Assert-True ([Game.Cultivation.Runtime.CultivationSessionRuntime]::
    TryEstimateDurationSeconds($next, 0, [ref]$zeroComprehension) -and
    $zeroComprehension -eq 2880000) `
    'Zero comprehension uses one as the denominator without truncating the estimate.'
$tooLong = [Game.Cultivation.Protocol.CultivationLevelDto]::new()
$tooLong.durationSeconds = [long]::MaxValue
$tooLong.referenceComprehension = 2
$tooLong.hasReferenceComprehension = $true
Assert-True (-not [Game.Cultivation.Runtime.CultivationSessionRuntime]::
    TryEstimateDurationSeconds($tooLong, 1, [ref]$duration)) `
    'A duration exceeding the supported time range must not be truncated.'
$stone.Count = 3
Assert-True ($runtime.CanStart('xuanqing') -and
    -not $runtime.CanStart('not_learned')) `
    'Owned materials enable only a learned technique with a next level.'
$originalMaterials = $next.materials
$next.materials = @($next.materials[0], $next.materials[0])
Assert-True (-not $runtime.CanStart('xuanqing')) `
    'Repeated recipe entries must be combined before checking owned material totals.'
$next.materials = $originalMaterials
$runtime.Catalog.techniques[0].enabled = $false
Assert-True (-not $runtime.CanStart('xuanqing')) `
    'Disabled catalog techniques cannot be started despite sufficient materials.'
$runtime.Catalog.techniques[0].enabled = $true
$beforeStart = $network.Sent.Count
Assert-True ($runtime.TryStart('xuanqing') -and
    -not $runtime.TryStart('xuanqing') -and
    $network.Sent.Count -eq $beforeStart + 1) `
    'Repeated start clicks keep one unresolved write.'
$request = $network.Sent[-1] | ConvertFrom-Json
Assert-True ($request.msgId -eq 'cultivation_start' -and
    $request.type -eq 'cultivation_start' -and
    $request.techniqueId -eq 'xuanqing' -and
    $request.configurationRevision -eq 57 -and
    $request.expectedInventoryRevision -eq 21 -and
    $request.cultivationOperationSessionId -eq
        $owner.CultivationOperationSessionId -and
    -not $request.PSObject.Properties['stackId'] -and
    -not $request.PSObject.Properties['durationSeconds']) `
    'The start wire submits only identity, revisions and the selected technique.'
$failure = '{"msgId":"cultivation_start_result",' +
    '"type":"cultivation_start_result",' +
    '"cultivationOperationSessionId":"' +
        $owner.CultivationOperationSessionId + '",' +
    '"requestSequence":' + $request.requestSequence +
    ',"clientRequestId":"' + $request.clientRequestId +
    '","success":false,"errorCode":"cultivation_materials_insufficient",' +
    '"missingMaterials":[{"itemId":"stone","quantity":1}],' +
    '"serverTime":2000,"snapshot":null}'
$beforeInventory = $inventory.Applied
[NetMessageDispatcher]::Instance.Dispatch('cultivation_start_result', $failure)
Assert-True (-not $runtime.HasUnresolvedOperation -and
    $inventory.Applied -eq $beforeInventory -and
    $null -eq $runtime.Snapshot.activeTask) `
    'A definite materials failure releases pending without changing task or inventory.'
Assert-True ($runtime.TryStart('xuanqing')) `
    'A new confirmed attempt after a business failure gets a new write identity.'
$request = $network.Sent[-1] | ConvertFrom-Json
$startTask = '{"taskId":"task-start","techniqueId":"xuanqing",' +
    '"fromLevel":0,"targetLevel":1,"startedAt":2000,"endsAt":192002000,' +
    '"configurationRevision":57,"paidMaterials":[{"itemId":"stone",' +
    '"quantity":2}],"status":"active"}'
$started = $learned.Replace('"revision":1', '"revision":2').
    Replace('"activeTask":null', '"activeTask":' + $startTask)
$success = '{"msgId":"cultivation_start_result",' +
    '"type":"cultivation_start_result",' +
    '"cultivationOperationSessionId":"' +
        $owner.CultivationOperationSessionId + '",' +
    '"requestSequence":' + $request.requestSequence +
    ',"clientRequestId":"' + $request.clientRequestId +
    '","success":true,"errorCode":"","idempotentReplay":false,' +
    '"requiresInventorySnapshot":false,"inventoryRevision":22,' +
    '"stackItemChanges":[{"stackId":9100,"itemId":"stone",' +
        '"previousCount":0,"newCount":1,"isBound":false}],' +
    '"missingMaterials":[],"serverTime":2100,"snapshot":' + $started + '}'
[NetMessageDispatcher]::Instance.Dispatch('cultivation_start_result',
    $success.Replace($request.clientRequestId,
        [Guid]::NewGuid().ToString('D')))
Assert-True ($runtime.HasUnresolvedOperation -and
    $inventory.Applied -eq $beforeInventory) `
    'A different request ID cannot confirm a start or consume inventory.'
$beforeProgression = $owner.ProgressionRequests
[NetMessageDispatcher]::Instance.Dispatch('cultivation_start_result', $success)
Assert-True (-not $runtime.HasUnresolvedOperation -and
    $inventory.Applied -eq $beforeInventory + 1 -and
    $runtime.Snapshot.activeTask.taskId -eq 'task-start' -and
    $runtime.Snapshot.techniques[0].level -eq 0 -and
    $runtime.Snapshot.totalComprehension -eq 15 -and
    $owner.ProgressionRequests -gt $beforeProgression -and
    -not $runtime.CanStart('xuanqing')) `
    'A first success applies one absolute inventory update and the authoritative task only.'
[NetMessageDispatcher]::Instance.Dispatch('cultivation_start_result', $success)
Assert-True ($inventory.Applied -eq $beforeInventory + 1) `
    'Duplicate start results cannot consume materials twice.'

$network.ConnectionEpoch = 7
$owner.CultivationOperationSessionId = [Guid]::NewGuid().ToString('D')
$runtime.BeginSession($owner, 51, $owner.CultivationOperationSessionId)
[NetMessageDispatcher]::Instance.Dispatch('cultivation_snapshot_result',
    $learnedAutomatic)
$runtime.Refresh()
$queries = @($network.Sent | ForEach-Object { $_ | ConvertFrom-Json })
$catalogQuery = @($queries | Where-Object {
    $_.msgId -eq 'cultivation_catalog' })[-1]
$itemQuery = @($queries | Where-Object {
    $_.msgId -eq 'item_catalog' })[-1]
[NetMessageDispatcher]::Instance.Dispatch('cultivation_catalog_result',
    $startCatalog.Replace('"requestSequence":' + $catalogSequence,
        '"requestSequence":' + $catalogQuery.requestSequence))
[NetMessageDispatcher]::Instance.Dispatch('item_catalog_result',
    ('{"msgId":"item_catalog_result","type":"item_catalog_result",' +
        '"requestSequence":' + $itemQuery.requestSequence +
        ',"success":true,"catalogVersion":1,"items":' + $learnItems +
        ',"serverTime":2200}'))
Assert-True ($runtime.TryStart('xuanqing')) `
    'A fresh session can prepare a start against an accepted learned snapshot.'
$replayRequest = $network.Sent[-1] | ConvertFrom-Json
$replay = $success.Replace($request.clientRequestId,
    $replayRequest.clientRequestId).Replace(
    '"requestSequence":' + $request.requestSequence,
    '"requestSequence":' + $replayRequest.requestSequence).
    Replace('"cultivationOperationSessionId":"' +
        $request.cultivationOperationSessionId + '"',
        '"cultivationOperationSessionId":"' +
        $owner.CultivationOperationSessionId + '"').
    Replace('"idempotentReplay":false', '"idempotentReplay":true').
    Replace('"inventoryRevision":22', '"inventoryRevision":0').
    Replace('"stackItemChanges":[{"stackId":9100,"itemId":"stone",' +
        '"previousCount":0,"newCount":1,"isBound":false}]',
        '"stackItemChanges":[]')
$beforeReplayApply = $inventory.Applied
$beforeReplayRepair = $inventory.Reconciliations
[NetMessageDispatcher]::Instance.Dispatch('cultivation_start_result', $replay)
Assert-True (-not $runtime.HasUnresolvedOperation -and
    $inventory.Applied -eq $beforeReplayApply -and
    $inventory.Reconciliations -gt $beforeReplayRepair -and
    $null -eq $runtime.Snapshot.activeTask -and
    -not $runtime.CanStart('xuanqing')) `
    'Replayed historical state cannot set a task or reopen start before repair.'

$network.ConnectionEpoch = 8
$owner.CultivationOperationSessionId = [Guid]::NewGuid().ToString('D')
$runtime.BeginSession($owner, 51, $owner.CultivationOperationSessionId)
[NetMessageDispatcher]::Instance.Dispatch('cultivation_snapshot_result',
    $learnedAutomatic)
$runtime.Refresh()
$queries = @($network.Sent | ForEach-Object { $_ | ConvertFrom-Json })
$catalogQuery = @($queries | Where-Object {
    $_.msgId -eq 'cultivation_catalog' })[-1]
$itemQuery = @($queries | Where-Object {
    $_.msgId -eq 'item_catalog' })[-1]
[NetMessageDispatcher]::Instance.Dispatch('cultivation_catalog_result',
    $startCatalog.Replace('"requestSequence":' + $catalogSequence,
        '"requestSequence":' + $catalogQuery.requestSequence))
[NetMessageDispatcher]::Instance.Dispatch('item_catalog_result',
    ('{"msgId":"item_catalog_result","type":"item_catalog_result",' +
        '"requestSequence":' + $itemQuery.requestSequence +
        ',"success":true,"catalogVersion":1,"items":' + $learnItems +
        ',"serverTime":2300}'))
Assert-True ($runtime.TryStart('xuanqing')) `
    'Uncertain start recovery begins with one in-flight identity.'
$unconfirmed = $network.Sent[-1] | ConvertFrom-Json
$uncertain = '{"msgId":"cultivation_start_result",' +
    '"type":"cultivation_start_result",' +
    '"cultivationOperationSessionId":"' +
        $owner.CultivationOperationSessionId + '",' +
    '"requestSequence":' + $unconfirmed.requestSequence +
    ',"clientRequestId":"' + $unconfirmed.clientRequestId +
    '","success":false,"errorCode":"cultivation_persist_failed",' +
    '"serverTime":2400,"snapshot":null}'
[NetMessageDispatcher]::Instance.Dispatch('cultivation_start_result', $uncertain)
Assert-True ($runtime.HasUnresolvedOperation) `
    'An uncertain persistence result must retain the original operation.'
$beforeRetry = $network.Sent.Count
[UnityEngine.Time]::unscaledTime = 100
$update.Invoke($runtime, @()) | Out-Null
$retry = @($network.Sent | Select-Object -Skip $beforeRetry |
    ForEach-Object { $_ | ConvertFrom-Json } | Where-Object {
        $_.msgId -eq 'cultivation_start' })[-1]
Assert-True ($retry.clientRequestId -eq $unconfirmed.clientRequestId -and
    $retry.requestSequence -eq $unconfirmed.requestSequence -and
    $retry.expectedInventoryRevision -eq
        $unconfirmed.expectedInventoryRevision) `
    'Same-session uncertain retry keeps request ID, sequence and original inputs.'

$network.ConnectionEpoch = 9
$owner.CultivationOperationSessionId = [Guid]::NewGuid().ToString('D')
$runtime.BeginSession($owner, 51, $owner.CultivationOperationSessionId)
[NetMessageDispatcher]::Instance.Dispatch('cultivation_snapshot_result',
    $learnedAutomatic)
$beforeReentry = $network.Sent.Count
[UnityEngine.Time]::unscaledTime = 110
$update.Invoke($runtime, @()) | Out-Null
$reentered = @($network.Sent | Select-Object -Skip $beforeReentry |
    ForEach-Object { $_ | ConvertFrom-Json } | Where-Object {
        $_.msgId -eq 'cultivation_start' })[-1]
Assert-True ($reentered.clientRequestId -eq $unconfirmed.clientRequestId -and
    $reentered.requestSequence -ne $unconfirmed.requestSequence -and
    $reentered.cultivationOperationSessionId -eq
        $owner.CultivationOperationSessionId -and
    $reentered.configurationRevision -eq
        $unconfirmed.configurationRevision -and
    $reentered.expectedInventoryRevision -eq
        $unconfirmed.expectedInventoryRevision -and
    $reentered.techniqueId -eq $unconfirmed.techniqueId) `
    'Re-entry changes only session and sequence, preserving the start business identity.'
$oldQuery = @($network.Sent | ForEach-Object { $_ | ConvertFrom-Json } |
    Where-Object { $_.msgId -eq 'cultivation_snapshot' })[-1]
$beforeRepairApply = $inventory.Applied
$beforeRepairCount = $inventory.Reconciliations
$repairResult = $success.Replace($request.clientRequestId,
    $reentered.clientRequestId).Replace(
    '"requestSequence":' + $request.requestSequence,
    '"requestSequence":' + $reentered.requestSequence).
    Replace('"cultivationOperationSessionId":"' +
        $request.cultivationOperationSessionId + '"',
        '"cultivationOperationSessionId":"' +
        $owner.CultivationOperationSessionId + '"').
    Replace('"requiresInventorySnapshot":false',
        '"requiresInventorySnapshot":true').
    Replace('"snapshot":' + $started, '"snapshot":null')
[NetMessageDispatcher]::Instance.Dispatch('cultivation_start_result',
    $repairResult)
$newQuery = @($network.Sent | ForEach-Object { $_ | ConvertFrom-Json } |
    Where-Object { $_.msgId -eq 'cultivation_snapshot' })[-1]
Assert-True (-not $runtime.HasUnresolvedOperation -and
    $inventory.Applied -eq $beforeRepairApply -and
    $inventory.Reconciliations -gt $beforeRepairCount -and
    $newQuery.requestSequence -ne $oldQuery.requestSequence -and
    -not $runtime.CanStart('xuanqing')) `
    'A success requiring inventory repair with null snapshot cannot apply deltas or reuse a pre-write query.'
$oldSnapshotResult = $learnedAutomatic.Replace(
    '"requestSequence":0',
    '"requestSequence":' + $oldQuery.requestSequence)
[NetMessageDispatcher]::Instance.Dispatch('cultivation_snapshot_result',
    $oldSnapshotResult)
Assert-True ($null -eq $runtime.Snapshot.activeTask -and
    -not $runtime.CanStart('xuanqing')) `
    'A pre-write snapshot cannot release the pending task synchronization guard.'
$currentSnapshotResult = $learnedAutomatic.Replace(
    '"requestSequence":0',
    '"requestSequence":' + $newQuery.requestSequence).
    Replace($learned, $started)
[NetMessageDispatcher]::Instance.Dispatch('cultivation_snapshot_result',
    $currentSnapshotResult)
Assert-True ($runtime.Snapshot.activeTask.taskId -eq 'task-start' -and
    $runtime.Snapshot.techniques[0].level -eq 0 -and
    -not $runtime.CanStart('xuanqing')) `
    'A current authoritative task snapshot restores the timer without local leveling.'

$network.ConnectionEpoch = 10
$owner.CultivationOperationSessionId = [Guid]::NewGuid().ToString('D')
$runtime.BeginSession($owner, 51, $owner.CultivationOperationSessionId)
[NetMessageDispatcher]::Instance.Dispatch('cultivation_snapshot_result',
    $learnedAutomatic)
$runtime.Refresh()
$queries = @($network.Sent | ForEach-Object { $_ | ConvertFrom-Json })
$catalogQuery = @($queries | Where-Object {
    $_.msgId -eq 'cultivation_catalog' })[-1]
$itemQuery = @($queries | Where-Object {
    $_.msgId -eq 'item_catalog' })[-1]
[NetMessageDispatcher]::Instance.Dispatch('cultivation_catalog_result',
    $startCatalog.Replace('"requestSequence":' + $catalogSequence,
        '"requestSequence":' + $catalogQuery.requestSequence))
[NetMessageDispatcher]::Instance.Dispatch('item_catalog_result',
    ('{"msgId":"item_catalog_result","type":"item_catalog_result",' +
        '"requestSequence":' + $itemQuery.requestSequence +
        ',"success":true,"catalogVersion":1,"items":' + $learnItems +
        ',"serverTime":2500}'))
Assert-True ($runtime.TryStart('xuanqing')) `
    'A task-active rejection must be tested with an actual pending start.'
$taskActiveRequest = $network.Sent[-1] | ConvertFrom-Json
$taskActiveResult = '{"msgId":"cultivation_start_result",' +
    '"type":"cultivation_start_result",' +
    '"cultivationOperationSessionId":"' +
        $owner.CultivationOperationSessionId + '",' +
    '"requestSequence":' + $taskActiveRequest.requestSequence +
    ',"clientRequestId":"' + $taskActiveRequest.clientRequestId +
    '","success":false,"errorCode":"cultivation_task_active",' +
    '"serverTime":2600,"snapshot":null}'
[NetMessageDispatcher]::Instance.Dispatch('cultivation_start_result',
    $taskActiveResult)
Assert-True (-not $runtime.HasUnresolvedOperation -and
    $null -eq $runtime.Snapshot.activeTask -and
    -not $runtime.CanStart('xuanqing')) `
    'Task-active rejection with no snapshot cannot reopen start on stale null state.'
$activeNotification = '{"msgId":"cultivation_changed",' +
    '"type":"cultivation_changed",' +
    '"cultivationOperationSessionId":"' +
        $owner.CultivationOperationSessionId + '",' +
    '"serverTime":2700,"snapshot":' + $started + '}'
[NetMessageDispatcher]::Instance.Dispatch('cultivation_changed',
    $activeNotification)
Assert-True ($runtime.Snapshot.activeTask.taskId -eq 'task-start' -and
    -not $runtime.CanStart('xuanqing')) `
    'A newer authoritative task resolves the missing-state guard as occupied.'

$inventorySource = Get-Content (
    Join-Path $root 'Assets/Scripts/Inventory/Integration/InventoryModuleRuntime.cs') -Raw
Assert-True ($inventorySource.Contains(
    'ApplyCultivationStartInventoryChanges(long inventoryRevision,') -and
    $inventorySource.Contains('"cultivation_start_result"')) `
    'Start inventory changes use the V2 absolute update with their own source ID.'
Write-Output 'PASS: cultivation start request, recipe, task, inventory and replay guards.'
