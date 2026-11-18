$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '锻造协议阶段1.Tests.ps1')

function Assert-LearnRejection([string]$reason, [long]$stackId,
    [Game.Inventory.Model.V2.InventoryOwnershipSnapshot]$bag) {
    [UnityEngine.Debug]::Warnings.Clear()
    if ($runtime.TryLearn($stackId, $bag)) {
        throw "Expected rejection: $reason"
    }
    $message = [UnityEngine.Debug]::Warnings |
        Where-Object { $_ -match '^\[Forging\]\[Learn\] rejected reason=' } |
        Select-Object -Last 1
    if (-not $message -or
        -not $message.Contains("reason=$reason ") -or
        -not $message.Contains("stackId=$stackId ")) {
        throw "Wrong rejection log for ${reason}: $message"
    }
    if ($network.Sent.Count -ne 0) {
        throw "Rejection must not send a request: $reason"
    }
}

$inventory = [Game.Inventory.InventoryModuleRuntime]::new()
$inventory.CurrentSnapshot = [ForgingFixture]::Bag(
    43, 10, 415, 'tz001', 1, 0)
[Game.Inventory.InventoryModuleRuntime]::Instance = $inventory
$network.Sent.Clear()
$runtime.BeginSession($owner, 43, 1, 'invalid-session')
if (-not ([UnityEngine.Debug]::Warnings |
    Where-Object { ($_ -match '^\[Forging\]\[Session\] entry rejected') -and
        ($_ -match 'sessionIdValid=False') })) {
    throw 'Invalid entry must log the session validity.'
}
Assert-LearnRejection 'session_not_started' 415 $inventory.CurrentSnapshot

$runtime.BeginSession($owner, 43, 1, $sessionB)
$owner.ForgingProtocolVersion = 3
Assert-LearnRejection 'protocol_version_mismatch' 415 $inventory.CurrentSnapshot
$owner.ForgingProtocolVersion = 1

$stale = [ForgingFixture]::Bag(43, 10, 415, 'tz001', 1, 0)
Assert-LearnRejection 'inventory_snapshot_stale' 415 $stale
Assert-LearnRejection 'stack_not_owned' 999 $inventory.CurrentSnapshot

if (-not $runtime.TryLearn(415, $inventory.CurrentSnapshot)) {
    throw 'A valid learning request must still be sent.'
}
$network.Sent.Clear()
Assert-LearnRejection 'learn_pending' 415 $inventory.CurrentSnapshot

$inventoryEntry = Get-Content (Join-Path $root `
    'Assets/Scripts/Inventory/Integration/V2/InventoryV2Session.cs') -Raw
if (-not $inventoryEntry.Contains(
    '[Forging][Learn] rejected reason=runtime_missing')) {
    throw 'Missing runtime must have its own diagnostic log.'
}

Write-Output 'PASS: forging learning rejection diagnostics.'
