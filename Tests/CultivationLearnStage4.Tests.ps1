$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'CultivationProtocolStage1.Tests.ps1')

$network.Sent.Clear()
$inventory = [Game.Inventory.InventoryModuleRuntime]::new()
$inventory.CurrentSnapshot =
    [Game.Inventory.Model.V2.InventoryOwnershipSnapshot]::new()
$inventory.CurrentSnapshot.CharacterDbId = 51
$inventory.CurrentSnapshot.InventoryRevision = 20
$bookStack = [Game.Inventory.Model.V2.InventoryStackState]::new()
$bookStack.StackId = 9001
$bookStack.ItemId = 'xuanqing_book'
$bookStack.Count = 2
$inventory.CurrentSnapshot.Stacks.Add($bookStack)
[Game.Inventory.InventoryModuleRuntime]::Shared = $inventory

[NetMessageDispatcher]::Instance.Dispatch('cultivation_snapshot_result',
    $automatic)
$runtime.Refresh()
$catalogQuery = @($network.Sent | ForEach-Object { $_ | ConvertFrom-Json } |
    Where-Object { $_.msgId -eq 'cultivation_catalog' })[-1]
$snapshotQuery = @($network.Sent | ForEach-Object { $_ | ConvertFrom-Json } |
    Where-Object { $_.msgId -eq 'cultivation_snapshot' })[-1]
$itemQuery = @($network.Sent | ForEach-Object { $_ | ConvertFrom-Json } |
    Where-Object { $_.msgId -eq 'item_catalog' })[-1]
$learnCatalog = $catalog.Replace('"requestSequence":' + $catalogSequence,
    '"requestSequence":' + $catalogQuery.requestSequence)
$learnItems = '[{"itemId":"xuanqing_book","kind":"techniquebook",' +
    '"maxStack":99,"learnTechniqueId":"xuanqing"},' +
    '{"itemId":"stone","kind":"material","maxStack":99,' +
    '"learnTechniqueId":""}]'
[NetMessageDispatcher]::Instance.Dispatch('cultivation_snapshot_result',
    $automatic.Replace('"requestSequence":0',
        '"requestSequence":' + $snapshotQuery.requestSequence))
[NetMessageDispatcher]::Instance.Dispatch('cultivation_catalog_result',
    $learnCatalog)
[NetMessageDispatcher]::Instance.Dispatch('item_catalog_result',
    ('{"msgId":"item_catalog_result","type":"item_catalog_result",' +
        '"requestSequence":' + $itemQuery.requestSequence +
        ',"success":true,"catalogVersion":1,"items":' + $learnItems +
        ',"serverTime":1000}'))

$configuredLevels = $runtime.Catalog.techniques[0].levels
$runtime.Catalog.techniques[0].levels =
    [Game.Cultivation.Protocol.CultivationLevelDto[]]@()
Assert-True (-not $runtime.CanLearn(9001)) `
    'A technique without configured levels must not send a book learning request.'
$runtime.Catalog.techniques[0].levels = $configuredLevels
Assert-True ($runtime.IsConfigurationReady -and $runtime.CanLearn(9001) -and
    -not $runtime.CanLearn(9999)) `
    'Only an owned, positive, catalog-linked book stack may be learned.'
$count = $network.Sent.Count
Assert-True (-not $runtime.TryLearn(9999) -and $network.Sent.Count -eq $count) `
    'An unknown stack cannot send a learning request.'
Assert-True ($runtime.TryLearn(9001) -and
    -not $runtime.TryLearn(9001) -and
    $network.Sent.Count -eq ($count + 1)) `
    'Repeated clicks must keep a single in-flight learning request.'
$request = $network.Sent[-1] | ConvertFrom-Json
Assert-True ($request.msgId -eq 'cultivation_learn' -and
    $request.type -eq 'cultivation_learn' -and
    $request.stackId -eq 9001 -and
    $request.configurationRevision -eq 57 -and
    $request.expectedInventoryRevision -eq 20 -and
    $request.cultivationOperationSessionId -eq
        $owner.CultivationOperationSessionId -and
    -not $request.PSObject.Properties['techniqueId']) `
    'The learn wire must use stack ID, current revisions and the cultivation session.'
$learned = $baseSnapshot.Replace('"revision":0', '"revision":1').
    Replace('"techniqueComprehension":0', '"techniqueComprehension":5').
    Replace('"totalComprehension":10', '"totalComprehension":15').
    Replace('"techniques":[]', '"techniques":[{"techniqueId":"xuanqing",' +
        '"level":0,"learnedAt":1000}]')
$response = '{"msgId":"cultivation_learn_result",' +
    '"type":"cultivation_learn_result",' +
    '"cultivationOperationSessionId":"' +
        $owner.CultivationOperationSessionId + '",' +
    '"requestSequence":' + $request.requestSequence +
    ',"clientRequestId":"' + $request.clientRequestId +
    '","success":true,"errorCode":"","idempotentReplay":false,' +
    '"requiresInventorySnapshot":false,"inventoryRevision":21,' +
    '"stackItemChanges":[{"stackId":9001,"itemId":"xuanqing_book",' +
        '"previousCount":0,"newCount":1,"isBound":false}],' +
    '"missingMaterials":[],"serverTime":1100,"snapshot":' + $learned + '}'
$badId = $response.Replace($request.clientRequestId,
    [Guid]::NewGuid().ToString('D'))
[NetMessageDispatcher]::Instance.Dispatch('cultivation_learn_result', $badId)
Assert-True ($runtime.HasUnresolvedOperation -and $inventory.Applied -eq 0) `
    'A result with another client request ID must not mutate inventory or clear pending.'
$missingTime = $response.Replace('"serverTime":1100', '"serverTime":0')
[NetMessageDispatcher]::Instance.Dispatch('cultivation_learn_result',
    $missingTime)
Assert-True ($runtime.HasUnresolvedOperation -and $inventory.Applied -eq 0) `
    'A malformed success must keep the write unresolved and request repair.'
[NetMessageDispatcher]::Instance.Dispatch('cultivation_learn_result', $response)
Assert-True (-not $runtime.HasUnresolvedOperation -and
    $inventory.Applied -eq 1 -and
    $inventory.Reconciliations -ge 1 -and
    $runtime.Snapshot.techniques[0].level -eq 0 -and
    $runtime.Snapshot.totalComprehension -eq 15 -and
    $null -eq $runtime.Snapshot.activeTask -and
    $owner.ProgressionRequests -ge 1 -and
    -not $runtime.CanLearn(9001)) `
    'A first matching success applies the absolute inventory update and learned zero level.'
[NetMessageDispatcher]::Instance.Dispatch('cultivation_learn_result', $response)
Assert-True ($inventory.Applied -eq 1) `
    'A duplicate success must not deduct the book twice.'

$network.ConnectionEpoch = 3
$owner.CultivationOperationSessionId = [Guid]::NewGuid().ToString('D')
$runtime.BeginSession($owner, 51, $owner.CultivationOperationSessionId)
$inventory.CurrentSnapshot.InventoryRevision = 21
[NetMessageDispatcher]::Instance.Dispatch('cultivation_snapshot_result',
    $automatic)
$runtime.Refresh()
$queries = @($network.Sent | ForEach-Object { $_ | ConvertFrom-Json })
$catalogQuery = @($queries | Where-Object {
    $_.msgId -eq 'cultivation_catalog' })[-1]
$snapshotQuery = @($queries | Where-Object {
    $_.msgId -eq 'cultivation_snapshot' })[-1]
$itemQuery = @($queries | Where-Object {
    $_.msgId -eq 'item_catalog' })[-1]
[NetMessageDispatcher]::Instance.Dispatch('cultivation_snapshot_result',
    $automatic.Replace('"requestSequence":0',
        '"requestSequence":' + $snapshotQuery.requestSequence))
[NetMessageDispatcher]::Instance.Dispatch('cultivation_catalog_result',
    $catalog.Replace('"requestSequence":' + $catalogSequence,
        '"requestSequence":' + $catalogQuery.requestSequence))
[NetMessageDispatcher]::Instance.Dispatch('item_catalog_result',
    ('{"msgId":"item_catalog_result","type":"item_catalog_result",' +
        '"requestSequence":' + $itemQuery.requestSequence +
        ',"success":true,"catalogVersion":1,"items":' + $learnItems +
        ',"serverTime":1200}'))
Assert-True ($runtime.CanLearn(9001) -and $runtime.TryLearn(9001)) `
    'A fresh session may start a new confirmed learning operation.'
$newRequest = $network.Sent[-1] | ConvertFrom-Json
$beforeReplayInventory = $inventory.Applied
$beforeReplayReconcile = $inventory.Reconciliations
$replay = $response.Replace($request.clientRequestId,
    $newRequest.clientRequestId).Replace(
    '"requestSequence":' + $request.requestSequence,
    '"requestSequence":' + $newRequest.requestSequence).
    Replace('"idempotentReplay":false', '"idempotentReplay":true').
    Replace('"inventoryRevision":21', '"inventoryRevision":0').
    Replace('"stackItemChanges":[{"stackId":9001,"itemId":"xuanqing_book",' +
        '"previousCount":0,"newCount":1,"isBound":false}]',
        '"stackItemChanges":[]')
$replay = $replay.Replace(
    [regex]::Match($replay, '"cultivationOperationSessionId":"[^"]+"').Value,
    '"cultivationOperationSessionId":"' +
        $owner.CultivationOperationSessionId + '"')
[NetMessageDispatcher]::Instance.Dispatch('cultivation_learn_result', $replay)
Assert-True (-not $runtime.HasUnresolvedOperation -and
    $inventory.Applied -eq $beforeReplayInventory -and
    $inventory.Reconciliations -gt $beforeReplayReconcile -and
    $runtime.Snapshot.techniques.Count -eq 0 -and
    ($network.Sent[-1] | ConvertFrom-Json).msgId -eq
        'cultivation_snapshot') `
    'An idempotent replay must repair inventory and query current state, not apply old state.'

Assert-True ($runtime.TryLearn(9001)) `
    'A new intent after an acknowledged operation needs a new request identity.'
$unconfirmed = $network.Sent[-1] | ConvertFrom-Json
$previousSessionId = $owner.CultivationOperationSessionId
$network.ConnectionEpoch = 4
$owner.CultivationOperationSessionId = [Guid]::NewGuid().ToString('D')
$runtime.BeginSession($owner, 51, $owner.CultivationOperationSessionId)
$oldResult = $response.Replace($request.clientRequestId,
    $unconfirmed.clientRequestId).Replace(
    '"requestSequence":' + $request.requestSequence,
    '"requestSequence":' + $unconfirmed.requestSequence).
    Replace(
    [regex]::Match($response,
        '"cultivationOperationSessionId":"[^"]+"').Value,
    '"cultivationOperationSessionId":"' + $previousSessionId + '"')
[NetMessageDispatcher]::Instance.Dispatch('cultivation_learn_result',
    $oldResult)
Assert-True ($runtime.HasUnresolvedOperation) `
    'A previous-session result must not confirm an unresolved write.'
[UnityEngine.Time]::unscaledTime = 50
$beforeRecovery = $network.Sent.Count
$update.Invoke($runtime, @()) | Out-Null
$recovered = @($network.Sent | Select-Object -Skip $beforeRecovery |
    ForEach-Object { $_ | ConvertFrom-Json } |
    Where-Object { $_.msgId -eq 'cultivation_learn' })[-1]
Assert-True ($recovered.clientRequestId -eq $unconfirmed.clientRequestId -and
    $recovered.cultivationOperationSessionId -eq
        $owner.CultivationOperationSessionId -and
    $recovered.requestSequence -ne $unconfirmed.requestSequence -and
    $recovered.stackId -eq $unconfirmed.stackId -and
    $recovered.configurationRevision -eq
        $unconfirmed.configurationRevision -and
    $recovered.expectedInventoryRevision -eq
        $unconfirmed.expectedInventoryRevision) `
    'Re-entry changes only session and sequence, preserving the write identity and inputs.'
$failedPersistence = '{"msgId":"cultivation_learn_result",' +
    '"type":"cultivation_learn_result",' +
    '"cultivationOperationSessionId":"' +
        $owner.CultivationOperationSessionId + '",' +
    '"requestSequence":' + $recovered.requestSequence +
    ',"clientRequestId":"' + $recovered.clientRequestId +
    '","success":false,"errorCode":"cultivation_persist_failed",' +
    '"snapshot":null,"serverTime":1500}'
[NetMessageDispatcher]::Instance.Dispatch('cultivation_learn_result',
    $failedPersistence)
Assert-True ($runtime.HasUnresolvedOperation) `
    'An uncertain persistence failure must retain the same pending identity.'
$beforeRetry = $network.Sent.Count
[UnityEngine.Time]::unscaledTime = 55
$update.Invoke($runtime, @()) | Out-Null
$retry = @($network.Sent | Select-Object -Skip $beforeRetry |
    ForEach-Object { $_ | ConvertFrom-Json } |
    Where-Object { $_.msgId -eq 'cultivation_learn' })[-1]
Assert-True ($retry.clientRequestId -eq $recovered.clientRequestId -and
    $retry.requestSequence -eq $recovered.requestSequence) `
    'A same-session uncertain retry must reuse the full original wire identity.'
[NetMessageDispatcher]::Instance.Dispatch('error',
    '{"msgId":"error","requestMsgId":"cultivation_learn",' +
    '"errorCode":"session_lease_lost","serverTime":1600}')
Assert-True ($runtime.HasUnresolvedOperation) `
    'Losing the write lease must not discard an unresolved operation.'
$beforeLeaseRetry = $network.Sent.Count
[UnityEngine.Time]::unscaledTime = 70
$update.Invoke($runtime, @()) | Out-Null
$leaseRetries = @($network.Sent | Select-Object -Skip $beforeLeaseRetry |
    ForEach-Object { $_ | ConvertFrom-Json } |
    Where-Object { $_.msgId -eq 'cultivation_learn' })
Assert-True ($leaseRetries.Count -eq 0) `
    'A lost lease must block retry within this session.'
$terminalFailure = $failedPersistence.Replace('cultivation_persist_failed',
    'cultivation_already_learned')
[NetMessageDispatcher]::Instance.Dispatch('cultivation_learn_result',
    $terminalFailure)
Assert-True (-not $runtime.HasUnresolvedOperation -and
    $inventory.Applied -eq $beforeReplayInventory) `
    'A definitive failure clears pending without applying any inventory mutation.'

$network.ConnectionEpoch = 5
$owner.CultivationOperationSessionId = [Guid]::NewGuid().ToString('D')
$runtime.BeginSession($owner, 51, $owner.CultivationOperationSessionId)
[NetMessageDispatcher]::Instance.Dispatch('cultivation_snapshot_result',
    $automatic)
$runtime.Refresh()
$queries = @($network.Sent | ForEach-Object { $_ | ConvertFrom-Json })
$catalogQuery = @($queries | Where-Object {
    $_.msgId -eq 'cultivation_catalog' })[-1]
$snapshotQuery = @($queries | Where-Object {
    $_.msgId -eq 'cultivation_snapshot' })[-1]
$itemQuery = @($queries | Where-Object {
    $_.msgId -eq 'item_catalog' })[-1]
[NetMessageDispatcher]::Instance.Dispatch('cultivation_snapshot_result',
    $automatic.Replace('"requestSequence":0',
        '"requestSequence":' + $snapshotQuery.requestSequence))
[NetMessageDispatcher]::Instance.Dispatch('cultivation_catalog_result',
    $catalog.Replace('"requestSequence":' + $catalogSequence,
        '"requestSequence":' + $catalogQuery.requestSequence))
[NetMessageDispatcher]::Instance.Dispatch('item_catalog_result',
    ('{"msgId":"item_catalog_result","type":"item_catalog_result",' +
        '"requestSequence":' + $itemQuery.requestSequence +
        ',"success":true,"catalogVersion":1,"items":' + $learnItems +
        ',"serverTime":1700}'))
Assert-True ($runtime.TryLearn(9001)) `
    'A new session after a definitive failure can submit a fresh book request.'
$snapshotRequiredRequest = $network.Sent[-1] | ConvertFrom-Json
$beforeSnapshotRequiredApply = $inventory.Applied
$beforeSnapshotRequiredRepair = $inventory.Reconciliations
$beforeSnapshotRequiredQuery = @($network.Sent | ForEach-Object {
    $_ | ConvertFrom-Json } | Where-Object {
        $_.msgId -eq 'cultivation_snapshot' }).Count
$snapshotRequired = '{"msgId":"cultivation_learn_result",' +
    '"type":"cultivation_learn_result",' +
    '"cultivationOperationSessionId":"' +
        $owner.CultivationOperationSessionId + '",' +
    '"requestSequence":' + $snapshotRequiredRequest.requestSequence +
    ',"clientRequestId":"' + $snapshotRequiredRequest.clientRequestId +
    '","success":true,"errorCode":"","idempotentReplay":false,' +
    '"requiresInventorySnapshot":true,"inventoryRevision":22,' +
    '"stackItemChanges":[{"stackId":9001,"itemId":"xuanqing_book",' +
        '"newCount":0,"isBound":false}],"serverTime":1800,"snapshot":null}'
[NetMessageDispatcher]::Instance.Dispatch('cultivation_learn_result',
    $snapshotRequired)
$afterSnapshotRequiredQuery = @($network.Sent | ForEach-Object {
    $_ | ConvertFrom-Json } | Where-Object {
        $_.msgId -eq 'cultivation_snapshot' }).Count
Assert-True (-not $runtime.HasUnresolvedOperation -and
    $inventory.Applied -eq $beforeSnapshotRequiredApply -and
    $inventory.Reconciliations -gt $beforeSnapshotRequiredRepair -and
    $afterSnapshotRequiredQuery -gt $beforeSnapshotRequiredQuery -and
    $runtime.Snapshot.techniques.Count -eq 0) `
    'A confirmed write requiring inventory repair must skip the local delta and query current state.'

$inventorySource = Get-Content (
    Join-Path $root 'Assets/Scripts/Inventory/Integration/InventoryModuleRuntime.cs') -Raw
Assert-True ($inventorySource.Contains(
    'ApplyCultivationInventoryChanges(long inventoryRevision,') -and
    $inventorySource.Contains('"cultivation_learn_result"')) `
    'Cultivation inventory changes must use the versioned absolute V2 delta adapter.'

Write-Output 'PASS: cultivation learn identity, inventory mutation, replay and snapshot guards.'
