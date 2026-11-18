$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '锻造协议阶段1.Tests.ps1')

function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
}

$network.IsConnected = $true
$network.ConnectionEpoch = 3
$owner.LocalCharacterDbId = 43
$owner.ForgingProtocolVersion = 1
$owner.ForgingOperationSessionId = $sessionB
$inventory = [Game.Inventory.InventoryModuleRuntime]::new()
$inventory.CurrentSnapshot = [ForgingFixture]::Bag(43, 10, 415, 'tz001', 1, 0)
[Game.Inventory.InventoryModuleRuntime]::Instance = $inventory
$network.Sent.Clear()
$runtime.BeginSession($owner, 43, 1, $sessionB)
Assert-True ($runtime.TryLearn(415, $inventory.CurrentSnapshot) -and
    $network.PersistedBeforeSend -and $runtime.HasUnresolvedLearn) `
    'Learning must persist its identity before sending.'
$request = $network.Sent[0]
$wire = [Newtonsoft.Json.JsonConvert]::SerializeObject($request) |
    ConvertFrom-Json
Assert-True ($wire.msgId -eq 'forging_learn' -and
    $wire.type -eq 'forging_learn' -and
    $wire.forgingOperationSessionId -eq $sessionB -and
    $wire.requestSequence -gt 0 -and
    [guid]::TryParse($wire.clientRequestId, [ref]([guid]::Empty)) -and
    $wire.expectedInventoryRevision -eq 10 -and
    $wire.stackId -eq 415 -and
    @($wire.PSObject.Properties).Count -eq 7 -and
    -not $runtime.TryLearn(415, $inventory.CurrentSnapshot)) `
    'The request must contain exactly the contract fields and block duplicate clicks.'
$foreign = '{"msgId":"forging_learn_result","type":"forging_learn_result",' +
    '"forgingOperationSessionId":"' + $sessionB +
    '","requestSequence":' + $request.requestSequence +
    ',"clientRequestId":"' + $sessionA + '","success":true}'
$dispatcher.Dispatch('forging_learn_result', $foreign)
Assert-True ($runtime.HasUnresolvedLearn -and
    $inventory.SnapshotRequests -eq 0) `
    'A foreign UUID cannot resolve a pending learning request.'
$learnState = $runtime.GetType().GetField('learn',
    [System.Reflection.BindingFlags]'NonPublic,Instance').GetValue($runtime)
$learnState.MarkSent(
    [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds() - 11000)
$update = $runtime.GetType().GetMethod('Update',
    [System.Reflection.BindingFlags]'NonPublic,Instance')
$update.Invoke($runtime, @()) | Out-Null
$sameSessionRetry = $network.Sent |
    Where-Object msgId -eq 'forging_learn' | Select-Object -Last 1
Assert-True ($sameSessionRetry.clientRequestId -eq $request.clientRequestId -and
    $sameSessionRetry.requestSequence -eq $request.requestSequence -and
    $sameSessionRetry.expectedInventoryRevision -eq 10 -and
    $sameSessionRetry.stackId -eq 415) `
    'A timed-out request must keep its UUID, sequence and business parameters.'

$owner.ForgingOperationSessionId = $sessionA
$network.Sent.Clear()
$runtime.BeginSession($owner, 43, 1, $sessionA)
$update.Invoke($runtime, @()) | Out-Null
$recovered = $network.Sent | Where-Object msgId -eq 'forging_learn' |
    Select-Object -Last 1
Assert-True ($null -ne $recovered -and
    $recovered.clientRequestId -eq $request.clientRequestId -and
    $recovered.forgingOperationSessionId -eq $sessionA -and
    $recovered.requestSequence -ne $request.requestSequence -and
    $recovered.expectedInventoryRevision -eq 10 -and
    $recovered.stackId -eq 415) `
    'Re-entry must change the session and sequence but retain UUID and parameters.'
$success = '{"msgId":"forging_learn_result","type":"forging_learn_result",' +
    '"forgingOperationSessionId":"' + $sessionA +
    '","requestSequence":' + $recovered.requestSequence +
    ',"clientRequestId":"' + $recovered.clientRequestId +
    '","success":true,"idempotentReplay":true,' +
    '"requiresInventorySnapshot":true,"inventoryRevision":11,' +
    '"stackItemChanges":[{"stackId":415,"itemId":"","newCount":0}],' +
    '"snapshot":{"learnedRecipeIds":["WQ002"]}}'
$dispatcher.Dispatch('forging_learn_result', $success)
Assert-True (-not $runtime.HasUnresolvedLearn -and
    -not [UnityEngine.PlayerPrefs]::Values.ContainsKey(
        'forging_learn_in_flight_43') -and
    $inventory.CurrentSnapshot.InventoryRevision -eq 10) `
    'A confirmed replay must not apply a debit or retain the pending request.'
$update.Invoke($runtime, @()) | Out-Null
Assert-True ($inventory.SnapshotRequests -gt 0 -and
    @($network.Sent | Where-Object msgId -eq 'forging_snapshot').Count -gt 0) `
    'Until stage 3 applies deltas, a success must refresh both authoritative snapshots.'
$dispatcher.Dispatch('forging_learn_result', $success)
Assert-True ($inventory.CurrentSnapshot.InventoryRevision -eq 10) `
    'A duplicate response cannot debit the blueprint twice.'

$network.Sent.Clear()
$runtime.BeginSession($owner, 43, 1, $sessionA)
Assert-True ($runtime.TryLearn(415, $inventory.CurrentSnapshot)) `
    'A definitive result must release the pending identity for another attempt.'
$rejectedRequest = $network.Sent[0]
$rejected = '{"msgId":"forging_learn_result","type":"forging_learn_result",' +
    '"forgingOperationSessionId":"' + $sessionA +
    '","requestSequence":' + $rejectedRequest.requestSequence +
    ',"clientRequestId":"' + $rejectedRequest.clientRequestId +
    '","success":false,"errorCode":"forging_recipe_unavailable"}'
$dispatcher.Dispatch('forging_learn_result', $rejected)
Assert-True (-not $runtime.HasUnresolvedLearn -and
    $inventory.CurrentSnapshot.Stacks[0].ItemId -eq 'tz001' -and
    $inventory.CurrentSnapshot.Stacks[0].Count -eq 1) `
    'An unavailable recipe must retain the blueprint and release the request.'

Write-Output 'PASS: forging learn request, persistence, correlation and recovery.'
