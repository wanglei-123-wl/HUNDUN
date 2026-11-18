$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'AlchemySessionRuntime.Tests.ps1') | Out-Null

$owner = [GameSession]::new()
$owner.AlchemyOperationSessionId = [Guid]::NewGuid().ToString('D')
$network = [NetworkManager]::new()
$inventory = [Game.Inventory.InventoryModuleRuntime]::new()
$inventory.CurrentSnapshot =
    [Game.Inventory.Model.V2.InventoryOwnershipSnapshot]::new(
        42, 30, [Game.Inventory.Model.V2.InventoryStackState]::new(
            5011, 'ling_shi', 5))
[UnityEngine.MockObjects]::Inventory = $inventory
$runtime = [Game.Alchemy.Runtime.AlchemySessionRuntime]::Ensure($network, $owner)
$runtime.BeginSession($owner, 42, $owner.AlchemyOperationSessionId)
$dispatcher = [NetMessageDispatcher]::Instance
$successfulTask = '{"taskId":"task-claim","recipeId":"pill",' +
    '"recipeName":"pill","status":"ready","succeeded":true,' +
    '"quality":1,"outputItemId":"pill_low","outputCount":3,' +
    '"startedAtUnixMilliseconds":1800000000000,' +
    '"endsAtUnixMilliseconds":1800000060000}'
$failedTask = $successfulTask.Replace('"succeeded":true',
    '"succeeded":false').Replace('"quality":1', '"quality":0').Replace(
    '"outputItemId":"pill_low"', '"outputItemId":""').Replace(
    '"outputCount":3', '"outputCount":0')
function Apply-Task([string]$task, [long]$revision) {
    $runtime.RequestSnapshot() | Out-Null
    $seq = ($network.Sent[-1] | ConvertFrom-Json).requestSequence
    $dispatcher.Dispatch('alchemy_snapshot_result',
        (Result-Json $seq 42 $revision $task 1800000070000))
}
function Claim-Result($request, [bool]$success, [string]$snapshot,
    [long]$revision, [string]$changes, [string]$extra) {
    $status = if ($success) { 'true' } else { 'false' }
    return '{"msgId":"alchemy_claim_result","type":"alchemy_claim_result",' +
        '"alchemyOperationSessionId":"' + $request.alchemyOperationSessionId +
        '","requestSequence":' + $request.requestSequence +
        ',"clientRequestId":"' + $request.clientRequestId +
        '","success":' + $status + ',"serverTime":1800000071000,' +
        '"inventoryRevision":' + $revision + ',"stackItemChanges":' +
        $changes + ',"snapshot":' + $snapshot + $extra + '}'
}
function Empty-Furnace([long]$revision) {
    return '{"characterDbId":42,"revision":' + $revision +
        ',"learnedRecipeIds":[],"currentTask":null}'
}

Apply-Task $successfulTask 1
Assert-True ($runtime.CanClaim($inventory.CurrentSnapshot) -and
    -not $runtime.CanClaim($null)) `
    'Successful ready tasks require a live character-owned inventory.'
Assert-True ($runtime.TryClaim($inventory.CurrentSnapshot) -and
    $runtime.HasUnresolvedClaim -and
    -not $runtime.TryClaim($inventory.CurrentSnapshot)) `
    'Claim must be a single locked write operation.'
$first = $network.Sent[-1] | ConvertFrom-Json
Assert-True ($first.msgId -eq 'alchemy_claim' -and
    $first.type -eq 'alchemy_claim' -and
    $first.taskId -eq 'task-claim' -and
    $first.expectedInventoryRevision -eq 30 -and
    $first.alchemyOperationSessionId -eq $owner.AlchemyOperationSessionId -and
    -not ($first.PSObject.Properties.Name -contains
        'configurationRevision') -and
    -not ($first.PSObject.Properties.Name -contains 'materials')) `
    'Claim sends only the current task, inventory revision and operation identity.'
$success = Claim-Result $first $true (Empty-Furnace 1) 31 `
    '[{"stackId":7001,"itemId":"pill_low","newCount":3,"isBound":false}]' ''
$dispatcher.Dispatch('alchemy_claim_result',
    $success.Replace($first.clientRequestId, [Guid]::NewGuid().ToString('D')))
Assert-True ($runtime.HasUnresolvedClaim -and $inventory.DeltaCalls -eq 0) `
    'An unrelated UUID cannot release a furnace or grant items.'
$dispatcher.Dispatch('alchemy_claim_result', $success)
Assert-True (-not $runtime.HasUnresolvedClaim -and
    $null -eq $runtime.Snapshot.currentTask -and
    $runtime.Snapshot.revision -eq 1 -and
    $inventory.DeltaCalls -eq 1 -and
    $inventory.CurrentSnapshot.InventoryRevision -eq 31 -and
    $inventory.CurrentSnapshot.Stacks.Count -eq 2 -and
    $inventory.CurrentSnapshot.Stacks[1].StackId -eq 7001 -and
    $inventory.CurrentSnapshot.Stacks[1].Count -eq 3) `
    'Even at the same revision, a newer explicit currentTask:null releases the furnace and grants output once.'
$dispatcher.Dispatch('alchemy_claim_result', $success)
Assert-True ($inventory.DeltaCalls -eq 1) `
    'A duplicate claim response cannot grant output again.'

Apply-Task $failedTask 3
$savedInventory = $inventory.CurrentSnapshot
$inventory.CurrentSnapshot = $null
Assert-True ($runtime.CanClaim($null) -and $runtime.TryClaim($null)) `
    'A failed ready task releases the furnace even without inventory state.'
$failedClaim = $network.Sent[-1] | ConvertFrom-Json
Assert-True ($failedClaim.expectedInventoryRevision -eq 0) `
    'A failed brew does not manufacture a stale inventory revision.'
$dispatcher.Dispatch('alchemy_claim_result',
    (Claim-Result $failedClaim $true (Empty-Furnace 4) 0 '[]' ''))
Assert-True ($null -eq $runtime.Snapshot.currentTask -and
    -not $runtime.HasUnresolvedClaim -and
    -not $runtime.IsInventoryRecovering -and
    $inventory.DeltaCalls -eq 1) `
    'Failure release needs no award or inventory delta.'
$inventory.CurrentSnapshot = $savedInventory

Apply-Task $failedTask 5
Assert-True ($runtime.TryClaim($null)) `
    'A later failed furnace can be released without inventory state.'
$delayedClaim = $network.Sent[-1] | ConvertFrom-Json
$sentBeforeDelayed = $network.Sent.Count
$dispatcher.Dispatch('alchemy_claim_result',
    (Claim-Result $delayedClaim $true (Empty-Furnace 5) 0 '[]' ''
        ).Replace('"serverTime":1800000071000',
        '"serverTime":1800000069000'))
Assert-True (-not $runtime.HasUnresolvedClaim -and
    $runtime.Snapshot.currentTask.taskId -eq 'task-claim' -and
    -not $runtime.IsSnapshotCurrent -and
    $network.Sent.Count -eq ($sentBeforeDelayed + 1) -and
    ($network.Sent[-1] | ConvertFrom-Json).msgId -eq 'alchemy_snapshot') `
    'A stale successful claim result must query current state without clearing an unverified furnace.'
$delayedRecovery = ($network.Sent[-1] | ConvertFrom-Json).requestSequence
$dispatcher.Dispatch('alchemy_snapshot_result',
    (Result-Json $delayedRecovery 42 6 'null' 1800000072000))
Assert-True ($runtime.IsSnapshotCurrent -and
    $null -eq $runtime.Snapshot.currentTask) `
    'A current authoritative empty snapshot must release the stale furnace.'

Apply-Task $successfulTask 7
Assert-True ($runtime.TryClaim($inventory.CurrentSnapshot)) `
    'A later ready task must permit a new claim.'
$fullRequest = $network.Sent[-1] | ConvertFrom-Json
$dispatcher.Dispatch('alchemy_claim_result',
    (Claim-Result $fullRequest $false 'null' 0 '[]' `
        ',"errorCode":"alchemy_inventory_full"'))
Assert-True (-not $runtime.HasUnresolvedClaim -and
    $runtime.Snapshot.currentTask.taskId -eq 'task-claim' -and
    $runtime.IsInventoryRecovering -and
    -not $runtime.CanClaim($inventory.CurrentSnapshot) -and
    $inventory.DeltaCalls -eq 1) `
    'A full bag keeps the task and blocks retry until inventory is refreshed.'
$inventory.CurrentSnapshot =
    [Game.Inventory.Model.V2.InventoryOwnershipSnapshot]::new(
        42, 32, $savedInventory.Stacks.ToArray())
$tick = [Game.Alchemy.Runtime.AlchemySessionRuntime].GetMethod(
    'Update', [System.Reflection.BindingFlags]'Instance, NonPublic')
$tick.Invoke($runtime, @()) | Out-Null
$refresh = @($network.Sent | ForEach-Object { $_ | ConvertFrom-Json } |
    Where-Object msgId -eq 'alchemy_snapshot')[-1]
$dispatcher.Dispatch('alchemy_snapshot_result',
    (Result-Json $refresh.requestSequence 42 7 $successfulTask 1800000072000))
Assert-True ($runtime.CanClaim($inventory.CurrentSnapshot) -and
    $runtime.TryClaim($inventory.CurrentSnapshot)) `
    'After a fresh bag and task snapshot, a new claim may be submitted.'
$uncertain = $network.Sent[-1] | ConvertFrom-Json
Assert-True ($uncertain.clientRequestId -ne $fullRequest.clientRequestId -and
    $uncertain.expectedInventoryRevision -eq 32) `
    'A definite full-bag failure needs a new UUID and current inventory revision.'
$dispatcher.Dispatch('alchemy_claim_result',
    (Claim-Result $uncertain $false 'null' 0 '[]' `
        ',"errorCode":"alchemy_persist_failed"'))
Assert-True ($runtime.HasUnresolvedClaim -and
    -not $runtime.CanClaim($inventory.CurrentSnapshot)) `
    'Uncertain persistence keeps the original claim locked.'
$operation = [Game.Alchemy.Runtime.AlchemySessionRuntime].GetField(
    'claimOperation', [System.Reflection.BindingFlags]'Instance, NonPublic'
    ).GetValue($runtime)
$pending = [Game.Alchemy.Runtime.AlchemyClaimOperationState].GetField(
    'pending', [System.Reflection.BindingFlags]'Instance, NonPublic'
    ).GetValue($operation)
$pending.GetType().GetField('lastSentAt').SetValue($pending,
    [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds() - 20000)
$tick.Invoke($runtime, @()) | Out-Null
$retry = $network.Sent[-1] | ConvertFrom-Json
Assert-True ($retry.msgId -eq 'alchemy_claim' -and
    $retry.clientRequestId -eq $uncertain.clientRequestId -and
    $retry.requestSequence -eq $uncertain.requestSequence -and
    $retry.taskId -eq $uncertain.taskId -and
    $retry.expectedInventoryRevision -eq 32) `
    'An uncertain same-session retry must preserve every business parameter.'

$network.Disconnect()
$replacementNetwork = [NetworkManager]::new()
$owner.AlchemyOperationSessionId = [Guid]::NewGuid().ToString('D')
$replacement = [Game.Alchemy.Runtime.AlchemySessionRuntime]::Ensure(
    $replacementNetwork, $owner)
$replacement.BeginSession($owner, 42, $owner.AlchemyOperationSessionId)
Assert-True ($replacement.HasUnresolvedClaim) `
    'A new runtime must reload an unresolved claim from persistent state.'
$tick.Invoke($replacement, @()) | Out-Null
$recovered = $replacementNetwork.Sent[-1] | ConvertFrom-Json
Assert-True ($recovered.msgId -eq 'alchemy_claim' -and
    $recovered.clientRequestId -eq $uncertain.clientRequestId -and
    $recovered.alchemyOperationSessionId -eq $owner.AlchemyOperationSessionId -and
    $recovered.requestSequence -ne $uncertain.requestSequence -and
    $recovered.taskId -eq $uncertain.taskId -and
    $recovered.expectedInventoryRevision -eq 32) `
    'Re-entry changes session/sequence but preserves UUID, task and inventory version.'
$beforeReplay = $inventory.DeltaCalls
$dispatcher.Dispatch('alchemy_claim_result',
    (Claim-Result $recovered $true 'null' 0 '[]' `
        ',"idempotentReplay":true,"requiresInventorySnapshot":true'))
Assert-True (-not $replacement.HasUnresolvedClaim -and
    $replacement.IsInventoryRecovering -and
    $inventory.DeltaCalls -eq $beforeReplay -and
    ($replacementNetwork.Sent[-1] | ConvertFrom-Json).msgId -eq
        'alchemy_snapshot') `
    'Replayed claims only request fresh alchemy/inventory snapshots.'

$runtime = $replacement
$network = $replacementNetwork
Apply-Task $failedTask 9
Assert-True ($runtime.TryClaim($null)) `
    'A ready failed task can still be released during inventory repair.'
$unclear = $network.Sent[-1] | ConvertFrom-Json
$dispatcher.Dispatch('alchemy_claim_result',
    (Claim-Result $unclear $true 'null' 0 '[]' ''))
Assert-True ($runtime.HasUnresolvedClaim -and
    $runtime.Snapshot.currentTask.taskId -eq 'task-claim' -and
    ($network.Sent[-1] | ConvertFrom-Json).msgId -eq 'alchemy_snapshot') `
    'A first success with snapshot:null cannot silently release the furnace.'
$dispatcher.Dispatch('error',
    '{"msgId":"error","requestMsgId":"alchemy_claim",' +
    '"errorCode":"alchemy_invalid_request"}')
Assert-True ($runtime.HasUnresolvedClaim -and
    -not $runtime.CanClaim($null)) `
    'A general claim error must keep the original identity locked.'

$panel = Get-Content (Join-Path (Split-Path $PSScriptRoot -Parent) `
    'Assets/Scripts/Alchemy/Presentation/AlchemyPanelView.cs') -Raw
$scene = Get-Content (Join-Path (Split-Path $PSScriptRoot -Parent) `
    'Assets/Scenes/WorldShell.unity') -Raw
$outputImage = [regex]::Match($scene,
    '(?s)--- !u!114 &1531636763\s+MonoBehaviour:.*?(?=--- !u!)').Value
$statusText = [regex]::Match($scene,
    '(?s)--- !u!114 &1949619011\s+MonoBehaviour:.*?(?=--- !u!)').Value
Assert-True ($panel.Contains('IBeginDragHandler, IDragHandler, IEndDragHandler') -and
    $panel.Contains('private void ClaimAlchemy()') -and
    $panel.Contains('runtime.TryClaim(inventory?.CurrentSnapshot)') -and
    $panel.Contains('task.status != "ready"') -and
    $panel.Contains('pressed != (hasIcon ? resultIcon.gameObject') -and
    $panel.Contains('task.ParsedSucceeded == false') -and
    $panel.Contains('InventoryPanelView.ActivePanel') -and
    $panel.Contains('bag.GridView.GetSlot(slot.DisplayIndex) == slot') -and
    $panel.Contains('bool overBagSlot = taskId != null && IsOverBagSlot(eventData);') -and
    $outputImage.Contains('m_RaycastTarget: 1') -and
    $statusText.Contains('m_RaycastTarget: 1') -and
    -not $panel.Contains('claimButton') -and
    -not $panel.Contains('CreateClaimButton')) `
    'Only a ready output (or failed task status) dropped on a real bag slot may submit the existing claim.'
Write-Output 'PASS: alchemy claim identity, award, failed brew, full bag and replay recovery.'
