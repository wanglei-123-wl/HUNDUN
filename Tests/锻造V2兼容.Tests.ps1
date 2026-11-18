$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '锻造协议阶段1.Tests.ps1')

function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
}

Assert-True ([Game.Forging.Protocol.锻造消息]::IsSupportedVersion(1) -and
    [Game.Forging.Protocol.锻造消息]::IsSupportedVersion(2) -and
    -not [Game.Forging.Protocol.锻造消息]::IsSupportedVersion(3)) `
    'Only the known forging protocol versions should be admitted.'

$owner.ForgingProtocolVersion = 2
$owner.ForgingOperationSessionId = $sessionB
$inventory = [Game.Inventory.InventoryModuleRuntime]::new()
$inventory.CurrentSnapshot = [ForgingFixture]::Bag(
    43, 292, 2597, 'tz001', 1, 0)
[Game.Inventory.InventoryModuleRuntime]::Instance = $inventory
$network.Sent.Clear()
$runtime.BeginSession($owner, 43, 2, $sessionB)
Assert-True ($runtime.IsSessionActive -and
    -not $runtime.IsRecipeLearned('WQ002') -and
    $runtime.RequestCatalog() -and $runtime.RequestSnapshot()) `
    'V2 session must request the forging catalog and snapshot.'
$catalogRequest = $network.Sent | Where-Object msgId -eq 'forging_catalog' |
    Select-Object -Last 1
$snapshotRequest = $network.Sent | Where-Object msgId -eq 'forging_snapshot' |
    Select-Object -Last 1
$catalogResponse = '{"msgId":"forging_catalog_result",' +
    '"type":"forging_catalog_result","forgingOperationSessionId":"' +
    $sessionB + '","requestSequence":' + $catalogRequest.requestSequence +
    ',"success":true,"serverTime":1793757600000,' +
    '"configurationRevision":12,' +
    '"catalogSyncId":"66666666-6666-4666-8666-666666666666",' +
    '"batchIndex":1,"batchCount":1,"totalCount":0,"recipes":[]}'
$dispatcher.Dispatch('forging_catalog_result', $catalogResponse)
$snapshotResponse = '{"msgId":"forging_snapshot_result",' +
    '"type":"forging_snapshot_result","forgingOperationSessionId":"' +
    $sessionB + '","requestSequence":' + $snapshotRequest.requestSequence +
    ',"success":true,"serverTime":1793757600000,' +
    '"snapshot":{"characterDbId":43,"revision":1,' +
    '"learnedRecipeIds":[],"currentTask":null}}'
$dispatcher.Dispatch('forging_snapshot_result', $snapshotResponse)
Assert-True ($runtime.IsCatalogCurrent -and $runtime.IsSnapshotCurrent) `
    'V2 catalog and snapshot responses must reach the active runtime.'

Assert-True ($runtime.TryLearn(2597, $inventory.CurrentSnapshot)) `
    'V2 session must allow learning an owned blueprint.'
$request = $network.Sent | Where-Object msgId -eq 'forging_learn' |
    Select-Object -Last 1
Assert-True ($request.forgingOperationSessionId -eq $sessionB -and
    $request.expectedInventoryRevision -eq 292 -and
    $request.stackId -eq 2597 -and
    $request.requestSequence -gt 0) `
    'V2 learning must preserve the session, revision and selected stack.'

$response = '{"msgId":"forging_learn_result","type":"forging_learn_result",' +
    '"forgingOperationSessionId":"' + $sessionB +
    '","requestSequence":' + $request.requestSequence +
    ',"clientRequestId":"' + $request.clientRequestId +
    '","success":true,"inventoryRevision":293,' +
    '"stackItemChanges":[{"stackId":2597,"itemId":"",' +
    '"newCount":0}],"snapshot":{"learnedRecipeIds":["WQ002"]}}'
$dispatcher.Dispatch('forging_learn_result', $response)
Assert-True (-not $runtime.HasUnresolvedLearn -and
    $runtime.IsRecipeLearned('WQ002') -and
    $inventory.CurrentSnapshot.InventoryRevision -eq 293 -and
    $inventory.CurrentSnapshot.Stacks.Count -eq 0) `
    'V2 result must unlock the recipe and remove the final blueprint.'

$inventory.CurrentSnapshot = [ForgingFixture]::Bag(
    43, 294, 101, 'iron_ore', 5, 0)
$network.Sent.Clear()
$runtime.BeginSession($owner, 43, 2, $sessionB)
Assert-True (-not $runtime.IsRecipeLearned('WQ002') -and
    $runtime.RequestCatalog() -and $runtime.RequestSnapshot()) `
    'A fresh V2 session must refresh forging state.'
$catalogRequest = $network.Sent[0]
$snapshotRequest = $network.Sent[1]
$recipe = '[{"recipeId":"WQ002","name":"玄铁剑",' +
    '"durationSeconds":60,"successRateBps":8000,' +
    '"materials":[{"itemId":"iron_ore","count":5}]}]'
$catalogResponse = '{"msgId":"forging_catalog_result",' +
    '"type":"forging_catalog_result","forgingOperationSessionId":"' +
    $sessionB + '","requestSequence":' + $catalogRequest.requestSequence +
    ',"success":true,"serverTime":1793757600000,' +
    '"configurationRevision":12,' +
    '"catalogSyncId":"66666666-6666-4666-8666-666666666666",' +
    '"batchIndex":1,"batchCount":1,"totalCount":1,"recipes":' +
    $recipe + '}'
$dispatcher.Dispatch('forging_catalog_result', $catalogResponse)
$snapshotResponse = '{"msgId":"forging_snapshot_result",' +
    '"type":"forging_snapshot_result","forgingOperationSessionId":"' +
    $sessionB + '","requestSequence":' + $snapshotRequest.requestSequence +
    ',"success":true,"serverTime":1793757600000,' +
    '"snapshot":{"characterDbId":43,"revision":1,' +
    '"learnedRecipeIds":["WQ002"],"currentTask":null}}'
$dispatcher.Dispatch('forging_snapshot_result', $snapshotResponse)
$selected = [System.Collections.Generic.List[Game.Forging.锻造已选材料]]::new()
$selected.Add([Game.Forging.锻造已选材料]::new(101, 'iron_ore', 5))
$intent = [Game.Forging.锻造开始意向]::new(
    43, 294, 'WQ002', $selected.AsReadOnly())
Assert-True ($runtime.CanStart($intent, $inventory.CurrentSnapshot) -and
    $runtime.TryStart($intent, $inventory.CurrentSnapshot)) `
    'V2 must allow starting a learned recipe with owned materials.'
$startRequest = $network.Sent | Where-Object msgId -eq 'forging_start' |
    Select-Object -Last 1
Assert-True ($startRequest.forgingOperationSessionId -eq $sessionB -and
    $startRequest.expectedInventoryRevision -eq 294 -and
    $startRequest.recipeId -eq 'WQ002') `
    'V2 start request must use the active session and recipe.'
$taskId = '33333333-3333-4333-8333-333333333333'
$activeTask = '{"characterDbId":43,"revision":2,' +
    '"learnedRecipeIds":["WQ002"],"currentTask":{' +
    '"taskId":"' + $taskId + '","recipeId":"WQ002",' +
    '"status":"active","succeeded":null,' +
    '"startedAtUnixMilliseconds":1793757600000,' +
    '"endsAtUnixMilliseconds":1793757660000}}'
$startResult = '{"msgId":"forging_start_result",' +
    '"type":"forging_start_result","forgingOperationSessionId":"' +
    $sessionB + '","requestSequence":' + $startRequest.requestSequence +
    ',"clientRequestId":"' + $startRequest.clientRequestId +
    '","success":true,"configurationRevision":12,' +
    '"serverTime":1793757600000,"inventoryRevision":295,' +
    '"stackItemChanges":[{"stackId":101,"itemId":"","newCount":0}],' +
    '"wallets":[],"equipmentItems":[],"snapshot":' + $activeTask + '}'
$dispatcher.Dispatch('forging_start_result', $startResult)
Assert-True (-not $runtime.HasUnresolvedStart -and
    $inventory.CurrentSnapshot.InventoryRevision -eq 295) `
    'V2 start result must resolve and apply the inventory change.'
$readyTask = $activeTask.Replace('"revision":2', '"revision":3').
    Replace('"status":"active","succeeded":null',
        '"status":"ready","succeeded":true,' +
        '"outputItemId":"WQ002","outputCount":1')
$changed = '{"msgId":"forging_changed","type":"forging_changed",' +
    '"forgingOperationSessionId":"' + $sessionB +
    '","requestSequence":0,"clientRequestId":"",' +
    '"success":true,"serverTime":1793757660000,"snapshot":' +
    $readyTask + '}'
$dispatcher.Dispatch('forging_changed', $changed)
Assert-True ($runtime.TryClaim($taskId, $inventory.CurrentSnapshot)) `
    'V2 must allow claiming a ready forging task.'
$claimRequest = $network.Sent | Where-Object msgId -eq 'forging_claim' |
    Select-Object -Last 1
Assert-True ($claimRequest.forgingOperationSessionId -eq $sessionB -and
    $claimRequest.expectedInventoryRevision -eq 295) `
    'V2 claim request must use the current session and inventory revision.'
$itemId = '55555555-5555-4555-8555-555555555555'
$claimResult = '{"msgId":"forging_claim_result",' +
    '"type":"forging_claim_result","forgingOperationSessionId":"' +
    $sessionB + '","requestSequence":' + $claimRequest.requestSequence +
    ',"clientRequestId":"' + $claimRequest.clientRequestId +
    '","success":true,"forgingSucceeded":true,' +
    '"serverTime":1793757661000,"inventoryRevision":296,' +
    '"stackItemChanges":[],"wallets":[],"equipmentItems":[' +
    '{"itemInstanceId":"' + $itemId +
    '","equipmentId":"WQ002","isBound":true}],' +
    '"snapshot":{"characterDbId":43,"revision":4,' +
    '"learnedRecipeIds":["WQ002"],"currentTask":null}}'
$dispatcher.Dispatch('forging_claim_result', $claimResult)
Assert-True (-not $runtime.HasUnresolvedClaim -and
    $inventory.CurrentSnapshot.InventoryRevision -eq 296 -and
    $inventory.CurrentSnapshot.Equipment.Count -eq 1) `
    'V2 claim result must clear the task and add the equipment.'

$owner.ForgingProtocolVersion = 3
Assert-True (-not $runtime.IsSessionActive) `
    'An unknown version must not reuse an active V2 session.'

Write-Output 'PASS: forging V2 entry, read, learn, start and claim.'
