$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '锻造开工阶段3.Tests.ps1')

function Assert-Claim($condition, [string]$message) {
    if (-not $condition) { throw $message }
}

function Enter-Ready([long]$character, [string]$sessionId,
    [long]$revision, [bool]$succeeded) {
    $owner.LocalCharacterDbId = $character
    $owner.ForgingOperationSessionId = $sessionId
    $inventory.CurrentSnapshot = [ForgingFixture]::Bag($character, 50, 101,
        'iron_ore', 1, 100)
    $network.Sent.Clear()
    $runtime.BeginSession($owner, $character, 1, $sessionId)
    $update.Invoke($runtime, @()) | Out-Null
    $outcome = if ($succeeded) { 'true' } else { 'false' }
    $outputId = if ($succeeded) { 'YF001_item' } else { '' }
    $count = if ($succeeded) { 1 } else { 0 }
    $readyTask = '{"characterDbId":' + $character + ',"revision":' +
        $revision + ',"currentTask":{' +
        '"taskId":"33333333-3333-4333-8333-333333333333",' +
        '"recipeId":"YF001_item","recipeName":"玄铁剑",' +
        '"status":"ready","succeeded":' + $outcome + ',' +
        '"outputItemId":"' + $outputId + '","outputCount":' + $count + ',' +
        '"startedAtUnixMilliseconds":1793757600000,' +
        '"endsAtUnixMilliseconds":1793757660000}}'
    $packet = '{"msgId":"forging_snapshot_result",' +
        '"type":"forging_snapshot_result",' +
        '"forgingOperationSessionId":"' + $sessionId + '",' +
        '"requestSequence":' + $network.Sent[1].requestSequence + ',' +
        '"success":true,"serverTime":1793757660000,' +
        '"snapshot":' + $readyTask + '}'
    $dispatcher.Dispatch('forging_snapshot_result', $packet)
    return $readyTask
}

function Claim-Packet($request, [bool]$success, [string]$errorCode,
    [string]$outcome, [string]$snapshot, [long]$revision,
    [string]$equipment, [bool]$replay = $false) {
    $successful = if ($success) { 'true' } else { 'false' }
    $replayed = if ($replay) { 'true' } else { 'false' }
    return '{"msgId":"forging_claim_result","type":"forging_claim_result",' +
        '"forgingOperationSessionId":"' +
        $request.forgingOperationSessionId + '","requestSequence":' +
        $request.requestSequence + ',"clientRequestId":"' +
        $request.clientRequestId + '","success":' + $successful +
        ',"errorCode":"' + $errorCode + '","forgingSucceeded":' +
        $outcome + ',"idempotentReplay":' + $replayed +
        ',"serverTime":1793757661000,"inventoryRevision":' + $revision +
        ',"stackItemChanges":[],"wallets":[],"equipmentItems":' +
        $equipment + ',"snapshot":' + $snapshot + '}'
}

$taskId = '33333333-3333-4333-8333-333333333333'
$itemId = '55555555-5555-4555-8555-555555555555'
$equipment = '[{"itemInstanceId":"' + $itemId +
    '","itemId":"YF001_item","equipmentId":"YF001",' +
    '"isBound":true,"enhanceLevel":0,"durability":100}]'
$empty = '{"characterDbId":43,"revision":3,"currentTask":null}'
$ready = Enter-Ready 43 $sessionA 2 $true
Assert-Claim ($runtime.CanClaim($taskId, $inventory.CurrentSnapshot) -and
    $runtime.TryClaim($taskId, $inventory.CurrentSnapshot) -and
    $network.PersistedBeforeSend -and $runtime.HasUnresolvedClaim -and
    -not $runtime.TryClaim($taskId, $inventory.CurrentSnapshot)) `
    'Only one saved claim may be submitted for a ready task.'
$request = $network.Sent[$network.Sent.Count - 1]
Assert-Claim ($request.msgId -eq 'forging_claim' -and
    $request.configurationRevision -eq 0 -and
    $request.expectedInventoryRevision -eq 50 -and
    $request.taskId -eq $taskId) 'Claim must use the frozen task identity.'
$successPacket = Claim-Packet $request $true '' 'true' $empty 51 $equipment
$dispatcher.Dispatch('forging_claim_result',
    $successPacket.Replace($request.clientRequestId, $sessionB))
Assert-Claim ($inventory.AppliedClaims -eq 0 -and
    $runtime.HasUnresolvedClaim) 'A foreign UUID cannot award equipment.'
$dispatcher.Dispatch('forging_claim_result', $successPacket)
Assert-Claim ($inventory.AppliedClaims -eq 1 -and
    $inventory.CurrentSnapshot.Equipment.Count -eq 1 -and
    $inventory.CurrentSnapshot.Equipment[0].ItemInstanceId -eq $itemId -and
    $inventory.CurrentSnapshot.Equipment[0].IsBound -and
    $null -eq $runtime.Snapshot.currentTask -and
    -not $runtime.HasUnresolvedClaim) `
    'Successful claim must add one independent bound instance and clear the task.'
$dispatcher.Dispatch('forging_claim_result', $successPacket)
Assert-Claim ($inventory.AppliedClaims -eq 1) `
    'A duplicate result cannot award equipment twice.'

$ready = Enter-Ready 43 $sessionA 5 $false
Assert-Claim ($runtime.TryClaim($taskId, $inventory.CurrentSnapshot)) `
    'Probability failure must still allow confirmation.'
$request = $network.Sent[$network.Sent.Count - 1]
$empty = '{"characterDbId":43,"revision":6,"currentTask":null}'
$dispatcher.Dispatch('forging_claim_result',
    (Claim-Packet $request $true '' 'false' $empty 0 '[]'))
Assert-Claim (-not $runtime.HasUnresolvedClaim -and
    $inventory.CurrentSnapshot.Equipment.Count -eq 0 -and
    $inventory.AppliedClaims -eq 1) `
    'Probability failure releases the slot without granting equipment.'

$ready = Enter-Ready 43 $sessionA 7 $true
Assert-Claim ($runtime.TryClaim($taskId, $inventory.CurrentSnapshot)) `
    'A ready task can submit a fresh claim.'
$request = $network.Sent[$network.Sent.Count - 1]
$dispatcher.Dispatch('forging_claim_result',
    (Claim-Packet $request $false 'forging_inventory_full' 'null' $ready 0 '[]'))
Assert-Claim (-not $runtime.HasUnresolvedClaim -and
    $runtime.Snapshot.currentTask.status -eq 'ready' -and
    $inventory.CurrentSnapshot.Equipment.Count -eq 0 -and
    $runtime.CanClaim($taskId, $inventory.CurrentSnapshot)) `
    'Full bag must leave ready intact and allow a new UUID.'
Assert-Claim ($runtime.TryClaim($taskId, $inventory.CurrentSnapshot) -and
    $network.Sent[-1].clientRequestId -ne $request.clientRequestId) `
    'Retry after a definitive full-bag rejection must have a fresh identity.'
$pending = $network.Sent[-1]
$dispatcher.Dispatch('forging_claim_result',
    (Claim-Packet $pending $false 'forging_persist_failed' 'null' 'null' 0 '[]'))
Assert-Claim ($runtime.HasUnresolvedClaim) `
    'Unknown persistence must keep the original claim.'
$owner.ForgingOperationSessionId = $sessionB
$network.Sent.Clear()
$runtime.BeginSession($owner, 43, 1, $sessionB)
$update.Invoke($runtime, @()) | Out-Null
$snapshot = '{"msgId":"forging_snapshot_result","type":"forging_snapshot_result",' +
    '"forgingOperationSessionId":"' + $sessionB + '","requestSequence":' +
    $network.Sent[1].requestSequence + ',"success":true,' +
    '"serverTime":1793757661000,"snapshot":' + $ready + '}'
$dispatcher.Dispatch('forging_snapshot_result', $snapshot)
$update.Invoke($runtime, @()) | Out-Null
$replayed = $network.Sent[-1]
Assert-Claim ($replayed.msgId -eq 'forging_claim' -and
    $replayed.clientRequestId -eq $pending.clientRequestId -and
    $replayed.requestSequence -ne $pending.requestSequence -and
    $replayed.forgingOperationSessionId -eq $sessionB -and
    $replayed.expectedInventoryRevision -eq $pending.expectedInventoryRevision -and
    $replayed.taskId -eq $pending.taskId) `
    'Re-entry keeps the claim UUID and parameters but renews session and sequence.'
$dispatcher.Dispatch('forging_claim_result',
    (Claim-Packet $replayed $true '' 'true' 'null' 0 '[]' $true))
Assert-Claim ($runtime.HasUnresolvedClaim -and
    $inventory.AppliedClaims -eq 1 -and
    $runtime.Snapshot.currentTask.status -eq 'ready') `
    'Replay without a task snapshot must not erase ready or grant twice.'
$owner.LocalCharacterDbId = 44
$inventory.CurrentSnapshot = [ForgingFixture]::Bag(44, 60, 101,
    'iron_ore', 1, 100)
$runtime.BeginSession($owner, 44, 1, $sessionA)
Assert-Claim (-not $runtime.HasUnresolvedClaim) `
    'A pending claim for another character must not leak into this session.'
$owner.LocalCharacterDbId = 43
$owner.ForgingOperationSessionId = $sessionA
$inventory.CurrentSnapshot = [ForgingFixture]::Bag(43, 51, 101,
    'iron_ore', 1, 100)
$network.Sent.Clear()
$runtime.BeginSession($owner, 43, 1, $sessionA)
$update.Invoke($runtime, @()) | Out-Null
$empty = '{"characterDbId":43,"revision":8,"currentTask":null}'
$snapshot = '{"msgId":"forging_snapshot_result","type":"forging_snapshot_result",' +
    '"forgingOperationSessionId":"' + $sessionA + '","requestSequence":' +
    $network.Sent[1].requestSequence + ',"success":true,' +
    '"serverTime":1793757662000,"snapshot":' + $empty + '}'
$dispatcher.Dispatch('forging_snapshot_result', $snapshot)
$update.Invoke($runtime, @()) | Out-Null
$again = $network.Sent[-1]
Assert-Claim ($again.msgId -eq 'forging_claim' -and
    $again.clientRequestId -eq $pending.clientRequestId) `
    'An unresolved claim resumes only for its original character.'
$dispatcher.Dispatch('forging_claim_result',
    (Claim-Packet $again $true '' 'true' 'null' 0 '[]' $true))
$update.Invoke($runtime, @()) | Out-Null
$repairSequence = $network.Sent[-1].requestSequence
$repair = $snapshot.Replace(
    '"requestSequence":' + $network.Sent[1].requestSequence,
    '"requestSequence":' + $repairSequence)
$dispatcher.Dispatch('forging_snapshot_result', $repair)
$update.Invoke($runtime, @()) | Out-Null
Assert-Claim (-not $runtime.HasUnresolvedClaim -and
    $inventory.AppliedClaims -eq 1) `
    'Replay resolves after a newer empty task and repaired inventory.'

$ready = Enter-Ready 43 $sessionA 9 $true
Assert-Claim ($runtime.TryClaim($taskId, $inventory.CurrentSnapshot)) `
    'A new ready task can be claimed after recovery.'
$request = $network.Sent[-1]
$beforeRepair = $inventory.SnapshotRequests
$empty = '{"characterDbId":43,"revision":10,"currentTask":null}'
$needsSnapshot = (Claim-Packet $request $true '' 'true' $empty 52 `
    $equipment).Replace('"idempotentReplay":false',
        '"idempotentReplay":false,"requiresInventorySnapshot":true')
$dispatcher.Dispatch('forging_claim_result', $needsSnapshot)
Assert-Claim ($runtime.HasUnresolvedClaim -and
    $inventory.AppliedClaims -eq 1 -and
    $inventory.SnapshotRequests -gt $beforeRepair) `
    'Snapshot-required claim must never apply an out-of-order award locally.'
$inventory.CurrentSnapshot =
    [ForgingFixture]::BagWithEquipment(43, 52, $itemId)
$update.Invoke($runtime, @()) | Out-Null
Assert-Claim (-not $runtime.HasUnresolvedClaim -and
    $inventory.AppliedClaims -eq 1 -and
    $inventory.CurrentSnapshot.Equipment.Count -eq 1) `
    'Only the authoritative repaired equipment snapshot resolves the claim.'

$panel = Get-Content (Join-Path $root 'Assets/Scripts/锻造/锻造面板视图.cs') -Raw
Assert-Claim ($panel.Contains('领取请求 += ClaimForging') -and
    $panel.Contains('runtime.TryClaim(taskId, inventory?.CurrentSnapshot)') -and
    $panel.Contains('runtime.HasUnresolvedClaim')) `
    'The existing result drag must submit through the claim runtime.'
Write-Output 'PASS: forging claim, failure, full bag, replay and character isolation.'
