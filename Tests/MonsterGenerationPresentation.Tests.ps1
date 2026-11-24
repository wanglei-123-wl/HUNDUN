$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/Monster/Network/MonsterSnapshotManager.cs') -Raw
$slices = @(
    '(?s)    public BattleSyncApplyResult BeginSession\(BattleSyncModuleSessionContext context\).*?(?=    public BattleSyncApplyResult ApplyMonsterSnapshot)'
    '(?s)    public BattleSyncApplyResult ApplyMonsterSnapshot\(MonsterSnapshotMessageDto message\).*?(?=    public BattleSyncApplyResult ApplyMonsterEnter)'
    '(?s)    public BattleSyncApplyResult ApplyMonsterEnter\(MonsterEnterMessageDto message\).*?(?=    public BattleSyncApplyResult ApplyMonsterUpsert)'
    '(?s)    public BattleSyncApplyResult ApplyMonsterRemove\(MonsterRemoveMessageDto message\).*?(?=    public bool TryPlayAttack)'
    '(?s)    private MonsterClientEntry ApplyMonsterState\(MonsterSnapshotItem item, long\? spawnGeneration = null\).*?(?=    private void EnsureCombatStateSubscription)'
    '(?s)    private void HandleCombatStateChanged\(CombatEntityKey key, CombatStateData state\).*?(?=    private bool IsMatchingCombatScene)'
)
$methods = foreach ($pattern in $slices) {
    $match = [regex]::Match($source, $pattern)
    if (-not $match.Success) { throw "Missing manager method for test: $pattern" }
    $match.Value
}

$harness = @'
using System;
using System.Collections.Generic;
using UnityEngine;
namespace UnityEngine
{
    public class GameObject
    {
        public bool Destroyed;
        public bool Active = true;
        public void SetActive(bool value) { Active = value; }
    }
    public static class Debug { public static void Log(string message) {} public static void LogWarning(string message,object context) {} }
}
public static class WorldEntryLoadingView { public static object ActiveWorldEntry; }
public enum BattleSyncApplyResult { Applied, AlreadyApplied, IgnoredStale, IgnoredWrongScene, RetryableFailure, InvalidMessage }
public enum BattleSyncSnapshotRoutingMode { LegacySelfRegistered, CoordinatorOwned }
public sealed class BattleSyncModuleSessionContext
{
    public long ConnectionEpoch;
    public string SceneId;
    public bool IsValid { get { return ConnectionEpoch > 0 && !string.IsNullOrEmpty(SceneId); } }
}
public sealed class MonsterStateDto
{
    public object path;
    public string monsterInstanceId, monsterId, state;
    public long spawnGeneration, aoiRevision;
    public long combatRevision;
    public string lifeState;
    public int hp, maxHp;
}
public sealed class MonsterSnapshotMessageDto
{
    public string reason;

    public string sceneId;
    public long serverTime, serverTick;
    public MonsterStateDto[] monsters;
}
public sealed class MonsterEnterMessageDto
{
    public string sceneId;
    public MonsterStateDto monster;
    public long aoiRevision, serverTime, serverTick;
}
public sealed class MonsterRemoveMessageDto
{
    public string sceneId, monsterInstanceId;
    public long spawnGeneration;
}
public sealed class MonsterSnapshotItem
{
    public string monsterInstanceId, monsterId, state;
    public long aoiRevision;
    public int hp, maxHp;
}
public sealed class MonsterViewState { }
public sealed class MonsterActionData
{
    public string entityId;
    public long eventSequence;
    public int actionSlot;
    public float actionSpeed;
}
public sealed class MonsterNetworkView
{
    public bool enabled = true;
    public int CurrentHp, MaxHp, HealthUpdates;
    public bool IsTargetable => enabled && CurrentHp > 0;
    public int DeathSignals;
    public bool ApplyState(MonsterViewState state) { return true; }
    public bool PlayAction(MonsterActionData action) { return true; }
    public void ApplyHealth(int hp, int maxHp) { CurrentHp = hp; MaxHp = maxHp; HealthUpdates++; }
    public void BootstrapState(string state) { if (state == "Dead") DeathSignals++; }
    public void SetMovementPath(object path,long time) {}
    public void StopNetworkInterpolation() { }
    public void ClearPendingSnapshots() { }
}
public sealed class CombatEntityKey
{
    public string EntityType, EntityId;
    public CombatEntityKey(string type, string id) { EntityType = type; EntityId = id; }
}
public sealed class CombatStateData
{
    public long SpawnGeneration;
    public int Hp, MaxHp;
    public string LifeState;
}
public sealed class CombatStateStore
{
    public CombatStateData State;
    public string ActiveSceneId = "world_001";
    public bool TryGetState(string type, string id, out CombatStateData value)
    {
        value = State;
        return State != null;
    }
    public bool MergeMonsterSpawnState(string scene, string id, int hp, int maxHp,
        string lifeState, long revision, long generation, long tick)
    {
        State = new CombatStateData { SpawnGeneration = generation, Hp = hp, MaxHp = maxHp };
        return true;
    }
}
public sealed class MonsterSnapshotManager
{
    private static bool CanPlayPath(MonsterClientEntry e,string s) => e.networkView.CurrentHp > 0 && (s == "Chase" || s == "Return" || s == "Patrol");
    private sealed class MonsterClientEntry
    {
        public UnityEngine.GameObject gameObject;
        public MonsterNetworkView networkView;
        public string lastServerState, lastAiState;
        public long lastAttackActionSequence, lastAttackEventId;
        public int unknownAiFrames;
        public long lastUnknownAiRepairTick;
    }
    private struct MonsterVersion
    {
        public long revision, serverTime;
        public MonsterVersion(long revision, long serverTime)
        {
            this.revision = revision; this.serverTime = serverTime;
        }
    }
    private readonly Dictionary<string, MonsterClientEntry> monsters = new Dictionary<string, MonsterClientEntry>();
    private readonly Dictionary<string, MonsterVersion> monsterVersions = new Dictionary<string, MonsterVersion>();
    private readonly Dictionary<string, long> monsterSpawnGenerations = new Dictionary<string, long>();
    private readonly Dictionary<string, string> monsterDefinitions = new Dictionary<string, string>();
    private readonly HashSet<string> reportedMissingMonsterIds = new HashSet<string>();
    private readonly HashSet<string> snapshotMonsterIds = new HashSet<string>();
    private BattleSyncSnapshotRoutingMode snapshotRoutingMode = BattleSyncSnapshotRoutingMode.CoordinatorOwned;
    private CombatStateStore combatStateStore = new CombatStateStore();
    private long activeConnectionEpoch;
    private string activeSceneId = "";
    private long localEventSequence;
    public int SpawnCount;
    public MonsterNetworkView GetView(string id) { return monsters.ContainsKey(id) ? monsters[id].networkView : null; }
    public UnityEngine.GameObject GetObject(string id) { return monsters.ContainsKey(id) ? monsters[id].gameObject : null; }
    public long GetGeneration(string id) { return monsterSpawnGenerations[id]; }
    public void SetOldHealth(long generation, int hp)
    {
        combatStateStore.State = new CombatStateData { SpawnGeneration = generation, Hp = hp, MaxHp = 100 };
    }
    public void ApplyCombatState(long generation, string lifeState, int hp)
    {
        HandleCombatStateChanged(
            new CombatEntityKey("monster", "mon_sp42_000001"),
            new CombatStateData { SpawnGeneration = generation, LifeState = lifeState, Hp = hp, MaxHp = 100 });
    }
    public void SetAttackSequence(string id, long sequence) { monsters[id].lastAttackActionSequence = sequence; }
    public long GetAttackSequence(string id) { return monsters[id].lastAttackActionSequence; }
    private void ClearAllMonsters() { monsters.Clear(); snapshotMonsterIds.Clear(); }
    private void CacheMonsterDefinition(MonsterSnapshotItem item) { monsterDefinitions[item.monsterInstanceId] = item.monsterId; }
    private void RemoveMissingMonsters()
    {
        foreach (string id in new List<string>(monsters.Keys))
            if (!snapshotMonsterIds.Contains(id)) monsters.Remove(id);
    }
    private static MonsterSnapshotItem ConvertMonster(MonsterStateDto source)
    {
        return new MonsterSnapshotItem {
            monsterInstanceId = source.monsterInstanceId, monsterId = source.monsterId,
            state = source.state, aoiRevision = source.aoiRevision, hp = source.hp, maxHp = source.maxHp
        };
    }
    private static bool HasRequiredMonsterComponents(UnityEngine.GameObject go) => true;
    private UnityEngine.GameObject FindPrefabById(string id) => null;
    private MonsterClientEntry SpawnMonster(MonsterSnapshotItem item)
    {
        SpawnCount++;
        var entry = new MonsterClientEntry {
            gameObject = new UnityEngine.GameObject(), networkView = new MonsterNetworkView(),
            lastServerState = "", lastAiState = item.state
        };
        entry.networkView.ApplyHealth(item.hp, item.maxHp);
        monsters[item.monsterInstanceId] = entry;
        return entry;
    }
    private void DisposeMonster(MonsterClientEntry entry)
    {
        entry.networkView.enabled = false;
        entry.gameObject.SetActive(false);
        entry.gameObject.Destroyed = true;
    }
    private MonsterViewState BuildViewState(MonsterSnapshotItem item) { return new MonsterViewState(); }
    private static bool IsAttackState(string state) { return state == "Attack"; }
    private void EnsureCombatStateSubscription() { }
    private bool IsMatchingCombatScene() { return true; }
__METHODS__
}
'@
Add-Type -TypeDefinition ($harness.Replace('__METHODS__', $methods -join "`n")) -IgnoreWarnings -ErrorAction Stop

function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
}

$id = 'mon_sp42_000001'
$manager = [MonsterSnapshotManager]::new()
$session = [BattleSyncModuleSessionContext]::new()
$session.ConnectionEpoch = 1
$session.SceneId = 'world_001'
Assert-True ($manager.BeginSession($session) -eq [BattleSyncApplyResult]::Applied) 'Session did not start.'

function Enter-Life([long]$generation, [string]$state, [int]$hp) {
    $monster = [MonsterStateDto]::new()
    $monster.monsterInstanceId = $id
    $monster.monsterId = 'Ls003'
    $monster.spawnGeneration = $generation
    $monster.state = $state
    $monster.hp = $hp
    $monster.maxHp = 100
    $message = [MonsterEnterMessageDto]::new()
    $message.sceneId = 'world_001'
    $message.monster = $monster
    $message.aoiRevision = $generation
    $message.serverTime = $generation
    return $manager.ApplyMonsterEnter($message)
}

Assert-True ((Enter-Life 1 'Dead' 0) -eq [BattleSyncApplyResult]::Applied) 'Initial life failed.'
$oldView = $manager.GetView($id)
$oldObject = $manager.GetObject($id)
$manager.SetAttackSequence($id, 42)
$manager.SetOldHealth(1, 0)
Assert-True ((Enter-Life 2 'Idle' 100) -eq [BattleSyncApplyResult]::Applied) 'Second life failed.'
Assert-True ($manager.SpawnCount -eq 2 -and $oldObject.Destroyed -and -not $oldObject.Active -and
    -not $oldView.enabled -and -not [object]::ReferenceEquals($oldView, $manager.GetView($id))) 'New life must replace and stop the old view.'
Assert-True ($manager.GetView($id).CurrentHp -eq 100 -and $manager.GetAttackSequence($id) -eq 0) 'New life must retain its own HP and clear old attack state.'

$remove = [MonsterRemoveMessageDto]::new()
$remove.sceneId = 'world_001'
$remove.monsterInstanceId = $id
$remove.spawnGeneration = 1
Assert-True ($manager.ApplyMonsterRemove($remove) -eq [BattleSyncApplyResult]::IgnoredStale -and
    $manager.GetView($id) -ne $null) 'Late old removal must not remove the new life.'
$remove.spawnGeneration = 3
Assert-True ($manager.ApplyMonsterRemove($remove) -eq [BattleSyncApplyResult]::RetryableFailure -and
    $manager.GetView($id) -ne $null) 'Future removal must not remove the new life.'

$snapshot = [MonsterSnapshotMessageDto]::new()
$snapshot.sceneId = 'world_001'
$snapshot.serverTime = 10
$late = [MonsterStateDto]::new()
$late.monsterInstanceId = $id
$late.monsterId = 'Ls003'
$late.spawnGeneration = 1
$late.state = 'Dead'
$snapshot.monsters = @($late)
Assert-True ($manager.ApplyMonsterSnapshot($snapshot) -eq [BattleSyncApplyResult]::IgnoredStale -and
    $manager.GetView($id).CurrentHp -eq 100 -and $manager.SpawnCount -eq 2) 'Late snapshot must not change the new life.'

$remove.spawnGeneration = 2
Assert-True ($manager.ApplyMonsterRemove($remove) -eq [BattleSyncApplyResult]::Applied -and
    $manager.GetView($id) -eq $null -and $manager.GetGeneration($id) -eq 2) 'Removal must retain the generation tombstone.'
Assert-True ((Enter-Life 1 'Idle' 100) -eq [BattleSyncApplyResult]::IgnoredStale) 'Late enter must not recreate a removed old life.'
Assert-True ((Enter-Life 3 'Idle' 100) -eq [BattleSyncApplyResult]::Applied) 'New generation must survive the previous removal.'

$manager.EndSession(1)
$session.ConnectionEpoch = 2
Assert-True ($manager.BeginSession($session) -eq [BattleSyncApplyResult]::Applied -and
    (Enter-Life 1 'Idle' 100) -eq [BattleSyncApplyResult]::Applied) 'Reconnection must discard the previous connection generation.'
$view = $manager.GetView($id)
$manager.ApplyCombatState(1, 'Dead', 0)
Assert-True ($view.DeathSignals -eq 1 -and $view.CurrentHp -eq 0) 'Current-generation death must reach the visual driver.'
Assert-True ((Enter-Life 2 'Idle' 100) -eq [BattleSyncApplyResult]::Applied) 'Respawn after death failed.'
$newView = $manager.GetView($id)
$manager.ApplyCombatState(1, 'Dead', 0)
Assert-True ($newView.DeathSignals -eq 0 -and $newView.CurrentHp -eq 100) 'Old-generation death must not affect the new life.'
$manager.ApplyCombatState(2, 'Dead', 0)
Assert-True ($newView.DeathSignals -eq 1) 'New-generation death must reach the new visual driver.'

$manager = [MonsterSnapshotManager]::new()
Assert-True ($manager.BeginSession($session) -eq [BattleSyncApplyResult]::Applied) 'Snapshot death session failed.'
Assert-True ((Enter-Life 1 'Idle' 100) -eq [BattleSyncApplyResult]::Applied) 'Alive monster entry failed.'
$view = $manager.GetView($id)
Assert-True ((Enter-Life 1 'Dead' 0) -eq [BattleSyncApplyResult]::Applied -and
    $view.DeathSignals -eq 1) 'Complete-state death must reach the existing monster view.'
Assert-True ((Enter-Life 1 'Dead' 0) -eq [BattleSyncApplyResult]::Applied -and
    $view.DeathSignals -eq 1) 'Repeated complete-state death must not restart the animation.'
Assert-True ((Enter-Life 2 'Idle' 100) -eq [BattleSyncApplyResult]::Applied -and
    $manager.GetView($id).DeathSignals -eq 0) 'New life must not inherit the old death animation.'

Write-Output 'PASS: monster generation isolation, combat/complete-state death routing, and reconnect.'
