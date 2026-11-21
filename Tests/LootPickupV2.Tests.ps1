$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$paths = @(
    'Assets/Scripts/Inventory/Protocol/V2/InventoryV2Messages.cs'
    'Assets/Scripts/Loot/Protocol/LootMessageDtos.cs'
    'Assets/Scripts/Loot/Protocol/LootProtocolConstants.cs'
    'Assets/Scripts/Loot/Model/LootEnums.cs'
    'Assets/Scripts/Loot/Model/LootPickupRequestState.cs'
    'Assets/Scripts/Loot/Model/LootPickupSessionState.cs'
    'Assets/Scripts/Loot/Service/LootPickupService.cs'
    'Assets/Scripts/Loot/Service/LootErrorHandler.cs'
    'Assets/Scripts/Loot/UI/LootErrorTextProvider.cs'
)
$files = $paths | ForEach-Object { Get-Content (Join-Path $root $_) -Raw }
$usings = $files | ForEach-Object {
    [regex]::Matches($_, '(?m)^using [^;]+;') | ForEach-Object { $_.Value }
} | Sort-Object -Unique
$sources = ($usings -join "`n") + "`n" +
    (($files | ForEach-Object { [regex]::Replace($_, '(?m)^using [^;]+;\r?\n', '') }) -join "`n")

$stubs = @'
public class NetMessageBase { public string msgId; public string type; }
public class MovementReferencedRequest : NetMessageBase
{
    public string sceneSessionId;
    public long movementEpoch, movementSequence;
}
public static class MovementReferenceError
{
    public static bool DidNotExecute(string error) => error == "stale_movement_epoch"
        || error == "movement_reference_mismatch" || error == "movement_control_required" || error == "movement_locked";
}
namespace UnityEngine
{
    public static class Time { public static float realtimeSinceStartup; }
    public static class Mathf { public static float Max(float a, float b) => System.Math.Max(a, b); }
    public static class Debug
    {
        public static void LogWarning(object message) { }
        public static void LogError(object message) { }
    }
}
public sealed class LootState { public string SceneId; public long AoiRevision; }
public sealed class LootStateStore
{
    public readonly System.Collections.Generic.Dictionary<string, LootState> Items = new();
    public bool TryGet(string id, out LootState state) => Items.TryGetValue(id, out state);
    public void Remove(string id, long revision) => Items.Remove(id);
}
public sealed class LootNetworkSender
{
    public int Sends;
    public long LastSequence;
    public bool ShouldSend = true;
    public readonly System.Collections.Generic.List<(long Sequence, long Tick, string LootId)> Messages = new();
    public bool SendPickup(long sequence, long tick, string id)
    {
        Sends++;
        LastSequence = sequence;
        Messages.Add((sequence, tick, id));
        return ShouldSend;
    }
}
public sealed class WorldLootViewManager
{
    public void SetPickupPending(string id, bool pending) { }
    public void Remove(string id, LootRemoveReason reason) { }
}
public sealed class LootFeedbackPresenter { public string LastError; public void ShowError(string message) => LastError = message; }
public sealed class LootSyncService { public string CurrentSceneId = "world_001"; }
public sealed class LootSystemSettings { public float PickupRequestTimeout = 1f; }
public sealed class PickupFixture
{
    public readonly LootStateStore Store = new();
    public readonly LootNetworkSender Sender = new();
    public readonly WorldLootViewManager View = new();
    public readonly LootFeedbackPresenter Feedback = new();
    public readonly LootSyncService Sync = new();
    public readonly LootPickupService Service;
    public readonly LootErrorHandler Errors;
    public long Tick = 10L;
    public int Pending;
    public int Succeeded;
    public int Rejected;
    public int Reconciliation;
    public int WorldResync;
    public int WorldCheck;
    public static LootPickupResultMessageDto Parse(string json) =>
        System.Text.Json.JsonSerializer.Deserialize<LootPickupResultMessageDto>(json,
            new System.Text.Json.JsonSerializerOptions { IncludeFields = true });
    public PickupFixture(LootPickupSessionState session = null)
    {
        Service = new LootPickupService(Store, Sender, View,
            Sync, new LootSystemSettings(), () => Tick,
            () => UnityEngine.Time.realtimeSinceStartup, session);
        Errors = new LootErrorHandler(Store, View, Service, Feedback, new LootErrorTextProvider());
        Service.PickupPending += _ => Pending++;
        Service.PickupSucceeded += _ => Succeeded++;
        Service.PickupFailed += Errors.HandlePickupFailure;
        Service.PickupResultRejected += _ => Rejected++;
        Service.InventoryReconciliationRequested += () => Reconciliation++;
        Errors.WorldResyncRequested += () => WorldResync++;
        Errors.PlayerWorldStateCheckRequested += () => WorldCheck++;
    }
}
'@
Add-Type -TypeDefinition ($sources + "`n" + $stubs) -IgnoreWarnings

function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
}
$parsed = [PickupFixture]::Parse('{"msgId":"loot_pickup_result","status":"succeeded","success":true,"inventoryRevision":18,"stackItemChanges":[{"stackId":17,"itemId":"hp_potion_01","previousCount":0,"newCount":2}],"equipmentItemChanges":[{"itemInstanceId":"ring-a","equipmentId":"ring_xingyun_001","slotCode":0}],"walletChanges":[{"currencyId":"gold_coin","previousBalance":0,"newBalance":7}]}')
Assert-True ($parsed.stackItemChanges[0].stackId -eq 17 -and
    $parsed.equipmentItemChanges[0].itemInstanceId -eq 'ring-a' -and
    $parsed.walletChanges[0].newBalance -eq 7) 'Loot result DTO must read all three V2 change arrays.'
$lootId = '0199a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b'
$fixture = [PickupFixture]::new()
$fixture.Store.Items.Add($lootId, [LootState]::new())
$fixture.Store.Items[$lootId].SceneId = 'world_001'
Assert-True ($fixture.Service.RequestPickup($lootId) -eq [LootPickupRequestResult]::Sent -and
    $fixture.Sender.Sends -eq 1 -and $fixture.Sender.LastSequence -eq 1 -and
    $fixture.Service.NextRequestSequence -eq 1) 'Sending pickup does not consume sequence 1.'
Assert-True ($fixture.Service.RequestPickup($lootId) -eq [LootPickupRequestResult]::AlreadyPending) 'A second pickup cannot start before the final result.'
$invalidSequence = [LootPickupResultMessageDto]::new()
$invalidSequence.requestSequence = 0
$invalidSequence.lootId = $lootId
$invalidSequence.status = 'pending'
Assert-True (-not $fixture.Service.HandlePickupResult($invalidSequence) -and
    $fixture.Service.NextRequestSequence -eq 1) 'A zero-sequence response cannot consume the active request.'
$pending = [LootPickupResultMessageDto]::new()
$pending.requestSequence = 1
$pending.lootId = $lootId
$pending.status = 'pending'
$null = $fixture.Service.HandlePickupResult($pending)
Assert-True ($fixture.Pending -eq 1 -and $fixture.Succeeded -eq 0 -and
    $fixture.Service.PendingCount -eq 1 -and
    $fixture.Service.NextRequestSequence -eq 2) 'Pending consumes sequence 1 but is not a grant.'
$success = [LootPickupResultMessageDto]::new()
$success.requestSequence = 1
$success.lootId = $lootId
$success.status = 'succeeded'
$success.success = $true
$success.grantId = '0199a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5c'
Assert-True ($fixture.Service.HandlePickupResult($success) -and
    $fixture.Succeeded -eq 1 -and $fixture.Service.PendingCount -eq 0 -and
    $fixture.Service.NextRequestSequence -eq 2) 'Final success must notify exactly once.'
Assert-True ($fixture.Service.HandlePickupResult($success) -and
    $fixture.Succeeded -eq 1) 'Repeated grantId must not show or apply a second success.'
$sameSequenceOtherGrant = [LootPickupResultMessageDto]::new()
$sameSequenceOtherGrant.requestSequence = 1
$sameSequenceOtherGrant.lootId = $lootId
$sameSequenceOtherGrant.status = 'succeeded'
$sameSequenceOtherGrant.success = $true
$sameSequenceOtherGrant.grantId = 'different-grant'
Assert-True ($fixture.Service.HandlePickupResult($sameSequenceOtherGrant) -and
    $fixture.Succeeded -eq 1) 'A completed sequence cannot grant another reward even with a different grantId.'
$orphanGrant = [LootPickupResultMessageDto]::new()
$orphanGrant.requestSequence = 9
$orphanGrant.lootId = $lootId
$orphanGrant.status = 'succeeded'
$orphanGrant.success = $true
$orphanGrant.grantId = 'orphan-grant'
Assert-True (-not $fixture.Service.HandlePickupResult($orphanGrant) -and
    $fixture.Succeeded -eq 1) 'An unmatched success cannot apply a grant.'
$invalid = [LootPickupResultMessageDto]::new()
$invalid.lootId = $lootId
$invalid.status = 'succeeded'
$invalid.success = $false
$invalid.grantId = 'different-grant'
Assert-True (-not $fixture.Service.HandlePickupResult($invalid) -and
    $fixture.Rejected -eq 3 -and $fixture.Succeeded -eq 1) 'A failed response must not be treated as a grant.'
$timedOut = [PickupFixture]::new()
$timedOut.Store.Items.Add($lootId, [LootState]::new())
$timedOut.Store.Items[$lootId].SceneId = 'world_001'
[UnityEngine.Time]::realtimeSinceStartup = 0
$null = $timedOut.Service.RequestPickup($lootId)
$timedOut.Tick = 11
[UnityEngine.Time]::realtimeSinceStartup = 2
$timedOut.Service.UpdateTimeouts()
[UnityEngine.Time]::realtimeSinceStartup = 4
$timedOut.Service.UpdateTimeouts()
$timedOut.Service.UpdateTimeouts()
Assert-True ($timedOut.Reconciliation -eq 1 -and $timedOut.Sender.Sends -eq 2) 'Timeout must retry once and request reconciliation once.'
Assert-True ($timedOut.Sender.LastSequence -eq 1 -and
    $timedOut.Service.NextRequestSequence -eq 1) 'Retry does not consume another pickup sequence.'
Assert-True ($timedOut.Sender.Messages.Count -eq 2 -and
    $timedOut.Sender.Messages[0] -eq $timedOut.Sender.Messages[1]) 'Timeout retry must preserve all fields of the original pickup request.'
$timedOut.Service.ResolveReconcilingRequests()
Assert-True ($timedOut.Service.PickupSuspended -and
    $timedOut.Service.NextRequestSequence -eq 1) 'An unconfirmed timed-out request cannot allocate another sequence.'
$pendingTimeout = [PickupFixture]::new()
$pendingTimeout.Store.Items.Add($lootId, [LootState]::new())
$pendingTimeout.Store.Items[$lootId].SceneId = 'world_001'
[UnityEngine.Time]::realtimeSinceStartup = 0
$null = $pendingTimeout.Service.RequestPickup($lootId)
$null = $pendingTimeout.Service.HandlePickupResult($pending)
[UnityEngine.Time]::realtimeSinceStartup = 2
$pendingTimeout.Service.UpdateTimeouts()
Assert-True ($pendingTimeout.Sender.Sends -eq 2 -and
    $pendingTimeout.Reconciliation -eq 0 -and
    $pendingTimeout.Service.PendingCount -eq 1) 'Pending timeout must query the server with the original request before snapshot repair.'
Assert-True ($pendingTimeout.Sender.Messages[0] -eq $pendingTimeout.Sender.Messages[1]) 'Pending retry must preserve all original fields.'
$null = $pendingTimeout.Service.HandlePickupResult($pending)
Assert-True ($pendingTimeout.Pending -eq 1) 'Duplicate pending must not restart the timeout.'
[UnityEngine.Time]::realtimeSinceStartup = 4
$pendingTimeout.Service.UpdateTimeouts()
Assert-True ($pendingTimeout.Reconciliation -eq 1 -and
    $pendingTimeout.Sender.Sends -eq 2 -and
    $pendingTimeout.Service.PendingCount -eq 1) 'Unconfirmed pending result requests one snapshot while retaining the active request.'
$null = $pendingTimeout.Service.HandlePickupResult($pending)
[UnityEngine.Time]::realtimeSinceStartup = 6
$pendingTimeout.Service.UpdateTimeouts()
Assert-True ($pendingTimeout.Reconciliation -eq 1 -and
    $pendingTimeout.Sender.Sends -eq 2) 'Late duplicate pending cannot restart a reconciliation cycle.'
Assert-True ($pendingTimeout.Service.HandlePickupResult($success) -and
    $pendingTimeout.Service.PendingCount -eq 0 -and
    $pendingTimeout.Service.NextRequestSequence -eq 2) 'Final result after pending timeout still completes the same request.'

$sharedSession = [LootPickupSessionState]::new()
$beforeSceneChange = [PickupFixture]::new($sharedSession)
$beforeSceneChange.Store.Items.Add($lootId, [LootState]::new())
$beforeSceneChange.Store.Items[$lootId].SceneId = 'world_001'
$null = $beforeSceneChange.Service.RequestPickup($lootId)
$afterSceneChange = [PickupFixture]::new($sharedSession)
$afterSceneChange.Sync.CurrentSceneId = 'world007'
$destinationLootId = '0199a1b2-c3d4-7e5f-9a9b-0c1d2e3f4a5b'
$afterSceneChange.Store.Items.Add($destinationLootId, [LootState]::new())
$afterSceneChange.Store.Items[$destinationLootId].SceneId = 'world007'
Assert-True ($afterSceneChange.Service.PendingCount -eq 1 -and
    $afterSceneChange.Service.NextRequestSequence -eq 1 -and
    $afterSceneChange.Service.RequestPickup($destinationLootId) -eq [LootPickupRequestResult]::AlreadyPending) 'The next scene cannot overwrite an unresolved pickup from the source scene.'
Assert-True ($afterSceneChange.Service.HandlePickupResult($pending) -and
    $afterSceneChange.Service.NextRequestSequence -eq 2) 'Recreated module accepts pending for its original request.'
Assert-True ($afterSceneChange.Service.HandlePickupResult($success) -and
    $afterSceneChange.Service.PendingCount -eq 0 -and
    $afterSceneChange.Service.NextRequestSequence -eq 2) 'Recreated module accepts the final result.'
Assert-True ($afterSceneChange.Service.RequestPickup($destinationLootId) -eq [LootPickupRequestResult]::Sent -and
    $afterSceneChange.Sender.LastSequence -eq 2 -and
    $afterSceneChange.Sender.Messages[0].Item3 -eq $destinationLootId) 'Next pickup uses the consumed sequence plus one for the destination loot.'
Assert-True ($afterSceneChange.Service.HandlePickupResult($success) -and
    $afterSceneChange.Succeeded -eq 1 -and
    $afterSceneChange.Service.PendingCount -eq 1) 'Duplicate source-scene success cannot credit twice or close the destination request.'
$afterSceneChange.Service.ResetConnectionState()
Assert-True ($afterSceneChange.Service.PendingCount -eq 0 -and
    $afterSceneChange.Service.NextRequestSequence -eq 1 -and
    $sharedSession.LastConsumedSequence -eq 0) 'New enter-world session resets pickup sequence and active request.'

$rejectedFixture = [PickupFixture]::new()
$rejectedFixture.Store.Items.Add($lootId, [LootState]::new())
$rejectedFixture.Store.Items[$lootId].SceneId = 'world_001'
$null = $rejectedFixture.Service.RequestPickup($lootId)
$rejectedResult = [LootPickupResultMessageDto]::new()
$rejectedResult.requestSequence = 1
$rejectedResult.lootId = $lootId
$rejectedResult.status = 'rejected'
$rejectedResult.errorCode = 'loot_too_far'
Assert-True ($rejectedFixture.Service.HandlePickupResult($rejectedResult) -and
    $rejectedFixture.Service.NextRequestSequence -eq 2 -and
    -not $rejectedFixture.Service.PickupSuspended) 'Normal rejected result consumes pickup sequence.'
Assert-True ($rejectedFixture.Service.HandlePickupResult($rejectedResult) -and
    $rejectedFixture.Service.NextRequestSequence -eq 2) 'Duplicate rejected result cannot consume a second sequence.'
$rejectedSuccessFlag = [LootPickupResultMessageDto]::new()
$rejectedSuccessFlag.requestSequence = 2
$rejectedSuccessFlag.lootId = $lootId
$rejectedSuccessFlag.status = 'rejected'
$rejectedSuccessFlag.success = $true
$null = $rejectedFixture.Service.RequestPickup($lootId)
Assert-True (-not $rejectedFixture.Service.HandlePickupResult($rejectedSuccessFlag) -and
    $rejectedFixture.Service.PendingCount -eq 1) 'Rejected with success=true must not finish an active request.'
$sendFailure = [PickupFixture]::new()
$sendFailure.Store.Items.Add($lootId, [LootState]::new())
$sendFailure.Store.Items[$lootId].SceneId = 'world_001'
$sendFailure.Sender.ShouldSend = $false
Assert-True ($sendFailure.Service.RequestPickup($lootId) -eq [LootPickupRequestResult]::NetworkSendFailed -and
    $sendFailure.Service.NextRequestSequence -eq 1 -and
    $sendFailure.Service.PendingCount -eq 0) 'Failed transport send must not consume sequence.'
$directFinal = [PickupFixture]::new()
$directFinal.Store.Items.Add($lootId, [LootState]::new())
$directFinal.Store.Items[$lootId].SceneId = 'world_001'
$null = $directFinal.Service.RequestPickup($lootId)
Assert-True ($directFinal.Service.HandlePickupResult($success) -and
    $directFinal.Service.NextRequestSequence -eq 2 -and
    $directFinal.Succeeded -eq 1) 'Final success without a prior pending result consumes sequence once.'
$null = $directFinal.Service.RequestPickup($lootId)
$missingStatus = [LootPickupResultMessageDto]::new()
$missingStatus.requestSequence = 2
$missingStatus.lootId = $lootId
$missingStatus.success = $true
$missingStatus.grantId = 'statusless-grant'
Assert-True (-not $directFinal.Service.HandlePickupResult($missingStatus) -and
    $directFinal.Service.PendingCount -eq 1 -and
    $directFinal.Succeeded -eq 1) 'V2 pickup results require an explicit status, even when the request matches.'
$contradictoryPending = [LootPickupResultMessageDto]::new()
$contradictoryPending.requestSequence = 2
$contradictoryPending.lootId = $lootId
$contradictoryPending.status = 'pending'
$contradictoryPending.success = $true
Assert-True (-not $directFinal.Service.HandlePickupResult($contradictoryPending) -and
    $directFinal.Service.PendingCount -eq 1 -and
    $directFinal.Pending -eq 0) 'Pending with success=true must not consume an active request.'
Assert-True (-not $directFinal.Service.HandlePickupResult($orphanGrant) -and
    $directFinal.Service.PendingCount -eq 1 -and
    $directFinal.Succeeded -eq 1) 'A stale or unrelated success cannot finish the next in-flight request.'
$reusedGrant = [LootPickupResultMessageDto]::new()
$reusedGrant.requestSequence = 2
$reusedGrant.lootId = $lootId
$reusedGrant.status = 'succeeded'
$reusedGrant.success = $true
$reusedGrant.grantId = $success.grantId
Assert-True (-not $directFinal.Service.HandlePickupResult($reusedGrant) -and
    $directFinal.Service.PendingCount -eq 1 -and
    $directFinal.Succeeded -eq 1) 'A prior grantId cannot complete a different active request.'
$secondSuccess = [LootPickupResultMessageDto]::new()
$secondSuccess.requestSequence = 2
$secondSuccess.lootId = $lootId
$secondSuccess.status = 'succeeded'
$secondSuccess.success = $true
$secondSuccess.grantId = 'second-grant'
Assert-True ($directFinal.Service.HandlePickupResult($secondSuccess) -and
    $directFinal.Service.PendingCount -eq 0 -and
    $directFinal.Succeeded -eq 2) 'A matching new success still completes the active request.'

foreach ($errorCode in @('loot_pickup_sequence_gap', 'loot_pickup_duplicate_or_stale', 'player_not_in_world')) {
    $errorFixture = [PickupFixture]::new()
    $errorFixture.Store.Items.Add($lootId, [LootState]::new())
    $errorFixture.Store.Items[$lootId].SceneId = 'world_001'
    $null = $errorFixture.Service.RequestPickup($lootId)
    $errorResult = [LootPickupResultMessageDto]::new()
    $errorResult.requestSequence = 1
    $errorResult.lootId = $lootId
    $errorResult.status = 'rejected'
    $errorResult.errorCode = $errorCode
    Assert-True ($errorFixture.Service.HandlePickupResult($errorResult) -and
        $errorFixture.Service.NextRequestSequence -eq 1 -and
        $errorFixture.Service.PickupSuspended -and
        $errorFixture.Service.PendingCount -eq 0 -and
        $errorFixture.Service.RequestPickup($lootId) -eq [LootPickupRequestResult]::Suspended) "Unconsumed $errorCode must stop new pickup without guessing sequence."
    if ($errorCode -eq 'player_not_in_world') {
        Assert-True ($errorFixture.WorldCheck -eq 1) 'Not-in-world should request a world-state check.'
    } else {
        Assert-True ($errorFixture.WorldResync -eq 1 -and
            $errorFixture.Feedback.LastError.Contains('重新进入游戏')) 'Sequence mismatch must request reentry and show actionable error.'
    }
    $errorFixture.Service.ResetConnectionState()
    Assert-True (-not $errorFixture.Service.PickupSuspended -and
        $errorFixture.Service.NextRequestSequence -eq 1) 'New entry clears suspended pickup state.'
}
$movingFixture = [PickupFixture]::new()
$movingFixture.Store.Items.Add($lootId, [LootState]::new())
$movingFixture.Store.Items[$lootId].SceneId = 'world_001'
$null = $movingFixture.Service.RequestPickup($lootId)
$movementError = [NetErrorMessageDto]::new()
$movementError.requestMsgId = 'loot_pickup'
$movementError.requestSequence = 1
$movementError.errorCode = 'movement_reference_mismatch'
$movingFixture.Service.HandleProtocolError($movementError)
Assert-True (-not $movingFixture.Service.PickupSuspended -and
    $movingFixture.Service.PendingCount -eq 0 -and $movingFixture.Service.NextRequestSequence -eq 1) `
    'A rejected movement reference must leave the pickup sequence available for retry.'

$tcpFixture = [PickupFixture]::new()
$tcpFixture.Store.Items.Add($lootId, [LootState]::new())
$tcpFixture.Store.Items[$lootId].SceneId = 'world_001'
$null = $tcpFixture.Service.RequestPickup($lootId)
$unrelatedError = [NetErrorMessageDto]::new()
$unrelatedError.requestMsgId = 'inventory_snapshot'
$unrelatedError.errorCode = 'invalid_loot_pickup'
$tcpFixture.Service.HandleProtocolError($unrelatedError)
Assert-True ($tcpFixture.Service.PendingCount -eq 1 -and
    -not $tcpFixture.Service.PickupSuspended) 'Unrelated TCP errors cannot cancel a pickup.'
$pickupError = [NetErrorMessageDto]::new()
$pickupError.requestMsgId = 'loot_pickup'
$pickupError.errorCode = 'invalid_loot_pickup'
$tcpFixture.Service.HandleProtocolError($pickupError)
Assert-True ($tcpFixture.Service.PendingCount -eq 0 -and
    $tcpFixture.Service.NextRequestSequence -eq 1 -and
    $tcpFixture.Service.PickupSuspended -and
    $tcpFixture.Feedback.LastError.Contains('重新进入游戏')) 'Invalid pickup TCP error is unconsumed and stops retrying.'
$unknownTcp = [PickupFixture]::new()
$unknownTcp.Store.Items.Add($lootId, [LootState]::new())
$unknownTcp.Store.Items[$lootId].SceneId = 'world_001'
$null = $unknownTcp.Service.RequestPickup($lootId)
$unknownError = [NetErrorMessageDto]::new()
$unknownError.requestMsgId = 'loot_pickup'
$unknownError.errorCode = 'unknown_pickup_error'
$unknownTcp.Service.HandleProtocolError($unknownError)
Assert-True ($unknownTcp.Service.PickupSuspended -and
    $unknownTcp.Service.NextRequestSequence -eq 1 -and
    $unknownTcp.Service.PendingCount -eq 0 -and
    $unknownTcp.Feedback.LastError.Contains('重新进入游戏')) 'Unspecified TCP error must not guess consumption or suggest immediate retry.'

$lootModule = Get-Content (Join-Path $root 'Assets/Scripts/Loot/LootModule.cs') -Raw
$lootRuntime = Get-Content (Join-Path $root 'Assets/Scripts/Loot/Integration/LootModuleRuntime.cs') -Raw
Assert-True ($lootModule -notmatch 'NetworkSender\.SendInventorySnapshotRequest\(' -and
    $lootModule -match 'dependencies\.RequestInventoryReconciliation\(\)' -and
    $lootRuntime -match 'inventoryModuleRuntime\.RequestReconciliation\(\)') 'Loot reconciliation must use the numbered V2 snapshot entry.'
Assert-True ($lootModule -notmatch 'OnLeaveScene[\s\S]*?PickupService\.ClearPendingRequests' -and
    $lootRuntime -match 'PickupSessionState = networkManager\.LootPickupSession' -and
    $lootRuntime -match 'BeginSession[\s\S]*?module\.ResetConnectionState\(\)') 'Scene recreation must retain the network-owned pickup state; new enter-world resets it.'
Write-Output 'PASS: pickup sequence consumption, exact retry, error suspension, session persistence and V2 reconciliation routing.'
