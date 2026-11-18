$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$paths = @(
    'Assets/Scripts/Inventory/Protocol/V2/InventoryV2Messages.cs'
    'Assets/Scripts/Loot/Protocol/LootMessageDtos.cs'
    'Assets/Scripts/Loot/Protocol/LootProtocolConstants.cs'
    'Assets/Scripts/Loot/Model/LootEnums.cs'
    'Assets/Scripts/Loot/Model/LootState.cs'
    'Assets/Scripts/Loot/Model/LootStateStore.cs'
    'Assets/Scripts/Loot/Model/LootPickupRequestState.cs'
    'Assets/Scripts/Loot/Model/LootPickupSessionState.cs'
    'Assets/Scripts/Loot/Service/LootServerTimeService.cs'
    'Assets/Scripts/Loot/Service/LootSyncService.cs'
    'Assets/Scripts/Loot/Service/LootPickupService.cs'
)
$files = $paths | ForEach-Object { Get-Content (Join-Path $root $_) -Raw }
$usings = $files | ForEach-Object {
    [regex]::Matches($_, '(?m)^using [^;]+;') | ForEach-Object { $_.Value }
} | Sort-Object -Unique
$sources = ($usings -join "`n") + "`n" +
    (($files | ForEach-Object { [regex]::Replace($_, '(?m)^using [^;]+;\r?\n', '') }) -join "`n")

$stubs = @'
public class NetMessageBase { public string msgId; public string type; }
namespace UnityEngine
{
    public struct Vector3
    {
        public Vector3(float x, float y, float z) {}
    }
    public static class Time
    {
        public static float realtimeSinceStartup;
        public static double realtimeSinceStartupAsDouble;
    }
    public static class Mathf { public static float Max(float a, float b) => System.Math.Max(a, b); }
    public static class Debug { public static void LogWarning(object message) {} }
}
public sealed class LootNetworkSender
{
    public bool SendPickup(long sequence, long tick, string lootId) => true;
}
public sealed class WorldLootViewManager
{
    public void SetPickupPending(string lootId, bool pending) {}
}
public sealed class LootSystemSettings { public float PickupRequestTimeout = 1f; }
public sealed class OrderingFixture
{
    public readonly LootStateStore Store = new LootStateStore();
    public readonly LootSyncService Sync;
    public readonly LootPickupService Pickup;
    public int Granted;
    public int Removed;
    public OrderingFixture(string lootId)
    {
        Sync = new LootSyncService(Store, new LootServerTimeService(() => 0d));
        Sync.SetCurrentScene("world_001");
        Store.Upsert(new LootState(lootId, "world_001", "ring_xingyun_001_item",
            1, new UnityEngine.Vector3(0, 0, 0), null, null, 0, 0, 1, "active"));
        Pickup = new LootPickupService(Store, new LootNetworkSender(),
            new WorldLootViewManager(), Sync, new LootSystemSettings(),
            () => 10L, () => 0f);
        Pickup.PickupSucceeded += _ => Granted++;
        Sync.LootRemoved += (_, _) => Removed++;
    }
}
'@
Add-Type -TypeDefinition ($sources + "`n" + $stubs) -IgnoreWarnings

function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
}

$lootId = '0199a1b2-c3d4-7e5f-8a9b-0c1d2e3f4a5b'
$result = [LootPickupResultMessageDto]::new()
$result.requestSequence = 1
$result.lootId = $lootId
$result.status = 'succeeded'
$result.success = $true
$result.grantId = 'grant-a'
$pending = [LootPickupResultMessageDto]::new()
$pending.requestSequence = 1
$pending.lootId = $lootId
$pending.status = 'pending'
$remove = [LootRemoveMessageDto]::new()
$remove.sceneId = 'world_001'
$remove.lootId = $lootId
$remove.reason = 'picked'
$remove.aoiRevision = 2
$remove.serverTime = 100

$removeFirst = [OrderingFixture]::new($lootId)
$null = $removeFirst.Pickup.RequestPickup($lootId)
$null = $removeFirst.Pickup.HandlePickupResult($pending)
$null = $removeFirst.Sync.ApplyRemove($remove)
$state = $null
Assert-True (-not $removeFirst.Store.TryGet($lootId, [ref]$state) -and
    $removeFirst.Removed -eq 1 -and $removeFirst.Granted -eq 0 -and
    $removeFirst.Pickup.PendingCount -eq 1) 'Removing ground loot must not grant or finish an active pickup.'
$null = $removeFirst.Pickup.HandlePickupResult($result)
Assert-True ($removeFirst.Granted -eq 1 -and $removeFirst.Pickup.PendingCount -eq 0) 'A later final result must still grant once.'

$grantFirst = [OrderingFixture]::new($lootId)
$null = $grantFirst.Pickup.RequestPickup($lootId)
$null = $grantFirst.Pickup.HandlePickupResult($pending)
$null = $grantFirst.Pickup.HandlePickupResult($result)
Assert-True ($grantFirst.Granted -eq 1 -and
    $grantFirst.Store.TryGet($lootId, [ref]$state)) 'A final grant must not remove ground loot on its own.'
$null = $grantFirst.Sync.ApplyRemove($remove)
Assert-True (-not $grantFirst.Store.TryGet($lootId, [ref]$state) -and
    $grantFirst.Removed -eq 1 -and $grantFirst.Granted -eq 1) 'A later ground remove must not grant again.'

Write-Output 'PASS: loot_remove and final pickup are independent in both arrival orders.'
