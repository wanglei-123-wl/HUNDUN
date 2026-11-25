$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$runtime = Get-Content (Join-Path $root 'Assets/Scripts/NetWork/BattleSync/Runtime/BattleSyncRuntime.cs') -Raw
$entity = Get-Content (Join-Path $root 'Assets/Scripts/NetWork/EntityManager.cs') -Raw
$remote = Get-Content (Join-Path $root 'Assets/Scripts/NetWork/RemotePlayerSync.cs') -Raw
$monster = Get-Content (Join-Path $root 'Assets/Scripts/Monster/Network/MonsterSnapshotManager.cs') -Raw
$handler = [regex]::Match($runtime,
    '(?s)        private void HandleCombatEventApplied\(CombatEventMessage message\).*?(?=        private void RequestCombatRevisionRepair)')
$playerMethod = [regex]::Match($entity,
    '(?s)    public bool TryPlayAttack\(long characterDbId, long actionSequence, long eventId\).*?(?=    public BattleSyncApplyResult BeginSession)')
$remoteMethod = [regex]::Match($remote,
    '(?s)    public void SetAttackEventRoutingEnabled\(bool enabled\).*?(?=    public void ClearPendingSnapshots)')
$remoteInit = [regex]::Match($remote,
    '(?s)    public void Init\(long remoteCharacterDbId, string remoteCharacterId, string remoteDisplayName\).*?(?=    public bool ApplyServerState)')
$remoteClear = [regex]::Match($remote,
    '(?s)    public void ClearPendingSnapshots\(\).*?(?=    private void ApplyAnimation)')
$remoteAnimation = [regex]::Match($remote,
    '(?s)    private void ApplyAnimation\(string animName, bool restartAttack = false\).*?(?=    private void CacheAnimatorParameters)')
$monsterMethod = [regex]::Match($monster,
    '(?s)    public bool TryPlayAttack\(string monsterInstanceId, long actionSequence, long eventId, long serverTick = 0\).*?(?=    public BattleSyncApplyResult ApplyMonsterDynamicState)')
$monsterState = [regex]::Match($monster,
    '(?s)    private MonsterClientEntry ApplyMonsterState\(MonsterSnapshotItem item, long\? spawnGeneration = null\).*?(?=    private void EnsureCombatStateSubscription)')
if (-not $handler.Success -or -not $playerMethod.Success -or
    -not $remoteMethod.Success -or -not $remoteInit.Success -or -not $remoteClear.Success -or
    -not $remoteAnimation.Success -or
    -not $monsterMethod.Success -or -not $monsterState.Success) {
    throw 'Attack presentation method was not found.'
}
if ($runtime -notmatch 'combatSnapshotModule\.CombatEventApplied \+= HandleCombatEventApplied' -or
    $runtime -notmatch 'combatSnapshotModule\.CombatEventApplied-=HandleCombatEventApplied') {
    throw 'Attack presentation subscription must be paired with cleanup.'
}
if (($remote -notmatch 'state\.HasActionState&&state\.ActionState!=0&&!attackEventsDriveAnimation') -or
    ($remote -notmatch 'if \(!attackEventsDriveAnimation')) {
    throw 'Remote state-driven attacks must yield to reliable attack events.'
}

$harness = @'
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
namespace UnityEngine
{
    public static class Debug
    {
        public static int WarningCount;
        public static void LogWarning(string message, object context) { WarningCount++; }
        public static void Log(string message) { }
    }
    public sealed class Animator
    {
        public int TriggerCount;
        public static int StringToHash(string value) => value.GetHashCode();
        public void SetBool(string name, bool value) { }
        public void ResetTrigger(string name) { }
        public void SetTrigger(string name) { TriggerCount++; }
        public bool HasState(int layer, int hash) => true;
        public void CrossFade(string state, float fade) { }
    }
    public sealed class GameObject { public string name; }
    public sealed class NameText { public string text; }
}
namespace Game.Client.BattleSync
{
    public enum BattleSyncSnapshotRoutingMode { CoordinatorOwned, LegacySelfRegistered }
    public static class BattleSyncProtocolConstants
    {
        public static class EntityTypes
        {
            public const string Player = "player";
            public const string Monster = "monster";
        }
    }
    public sealed class CombatEntityMessage
    {
        public string entityType;
        public string entityId;
    }
    public sealed class CombatEventMessage
    {
        public string eventType;
        public long actionSequence;
        public long eventId;
        public long serverTick;
        public CombatEntityMessage source;
        public CombatEntityMessage target;
    }
    public sealed class RemotePlayerSync
    {
        private const string RunningParameter = "IsRunning";
        private bool initialized;
        private long characterDbId;
        private string characterId;
        private string displayName;
        private NameText nameText;
        private GameObject gameObject = new GameObject();
        private Animator animator = new Animator();
        private string attackStateName = "Attack";
        private string idleStateName = "Idle";
        private string runStateName = "Run";
        private string moveBoolName = "IsMoving";
        private string attackTriggerName = "Attack";
        private string currentAnim;
        private bool hasMoveBoolParameter = true;
        private bool hasRunningParameter;
        private bool hasAttackTriggerParameter = true;
        private bool attackEventsDriveAnimation;
        private long lastAttackActionSequence;
        private long lastAttackEventId;
        private bool hasReceivedState;
        private long lastServerTick;
        private long lifeCombatRevision, lastRemoteRollId, meditationRevision;
        private object movement;
        private bool remoteMeditating, remoteRollPlaying;
        public long VisibilityRevision { get; private set; }
        public bool IsDead { get; private set; }
        private void EndRemoteRoll() { remoteRollPlaying = false; }
        private void SetRemoteMeditationPosture(bool active) { remoteMeditating = active; }
        private void SetDeathCollidersDisabled(bool disabled) { }
        private static class PlayerDeathAnimation { public static void SetDead(Animator animator, bool dead) { } }
        public int Animations => animator.TriggerCount;
__REMOTE_INIT__
__REMOTE__
__REMOTE_CLEAR__
__REMOTE_ANIMATION__
    }
    public sealed class EntityManager
    {
        private long localCharacterDbId;
        private readonly Dictionary<long, RemotePlayerSync> remotePlayers =
            new Dictionary<long, RemotePlayerSync>();
        public void SetLocal(long id) { localCharacterDbId = id; }
        public void AddRemote(long id, RemotePlayerSync view) { remotePlayers[id] = view; }
__PLAYER__
    }
    public sealed class MonsterActionData
    {
        public string entityId;
        public long eventSequence;
        public int actionSlot;
        public float actionSpeed;
    }
    public sealed class MonsterNetworkView
    {
        public int Animations;
        public int MovementStops;
        public int CurrentHp = 100;
        public bool ThrowOnPlay;
        public long LastEventSequence;
        public bool ApplyState(object state) => true;
        public void StopMovement() { MovementStops++; }
        public void BootstrapState(string state) { }
        public void SetMovementPath(object path, long time) { }
        public bool IsFinishingMovement => false;
        public bool PlayAction(MonsterActionData action)
        {
            if (ThrowOnPlay) throw new InvalidOperationException("Visual failed.");
            if (action.eventSequence <= LastEventSequence) return false;
            LastEventSequence = action.eventSequence;
            Animations++;
            return true;
        }
    }
    public sealed class MonsterSnapshotItem
    {
        public string monsterInstanceId;
        public string state;
    }
    public sealed class CombatStateData { public long SpawnGeneration; }
    public sealed class CombatStateStore
    {
        public bool TryGetState(string type, string id, out CombatStateData state)
        {
            state = null;
            return false;
        }
    }
    public sealed class MonsterSnapshotManager
    {
        private sealed class MonsterClientEntry
        {
            public GameObject gameObject;
            public MonsterNetworkView networkView;
            public string lastServerState;
            public string lastAiState;
            public long lastAttackActionSequence;
            public long lastAttackEventId;
            public long lastAttackServerTick, lastMotionServerTick;
        }
        private readonly Dictionary<string, MonsterClientEntry> monsters =
            new Dictionary<string, MonsterClientEntry>();
        private readonly Dictionary<string, long> monsterSpawnGenerations =
            new Dictionary<string, long>();
        private CombatStateStore combatStateStore;
        private long localEventSequence;
        private BattleSyncSnapshotRoutingMode snapshotRoutingMode =
            BattleSyncSnapshotRoutingMode.CoordinatorOwned;
        public void AddMonster(string id, MonsterNetworkView view, string state)
        {
            monsters[id] = new MonsterClientEntry {
                gameObject = new GameObject(), networkView = view, lastServerState = state
            };
        }
        public void SetMotionTick(string id, long tick) { monsters[id].lastMotionServerTick = tick; }
        public void SetMode(BattleSyncSnapshotRoutingMode mode) { snapshotRoutingMode = mode; }
        public void SimulateState(string id, string state)
        {
            ApplyMonsterState(new MonsterSnapshotItem { monsterInstanceId = id, state = state });
        }
        private MonsterClientEntry SpawnMonster(MonsterSnapshotItem item) => null;
        private void DisposeMonster(MonsterClientEntry entry) { }
        private object BuildViewState(MonsterSnapshotItem item) => new object();
        private void ApplyStoredCombatState(string id, MonsterClientEntry entry,
            long? spawnGeneration = null) { }
        private static bool IsAttackState(string state) =>
            string.Equals(state, "Attack", StringComparison.OrdinalIgnoreCase);
        private static bool CanPlayPath(MonsterClientEntry entry, string state) =>
            entry.networkView.CurrentHp > 0 && (state == "Patrol" || state == "Chase" || state == "Return");
__MONSTER__
__MONSTER_STATE__
    }
    public sealed class BattleSyncRuntimeHarness
    {
        private readonly EntityManager playerSnapshotModule;
        private readonly MonsterSnapshotManager monsterSnapshotModule;
        public BattleSyncRuntimeHarness(EntityManager players, MonsterSnapshotManager monsters)
        {
            playerSnapshotModule = players;
            monsterSnapshotModule = monsters;
        }
        public void Dispatch(CombatEventMessage message) => HandleCombatEventApplied(message);
__HANDLER__
    }
}
'@.Replace('__REMOTE_INIT__', $remoteInit.Value).
    Replace('__REMOTE__', $remoteMethod.Value).
    Replace('__REMOTE_CLEAR__', $remoteClear.Value).
    Replace('__REMOTE_ANIMATION__', $remoteAnimation.Value).
    Replace('__PLAYER__', $playerMethod.Value).
    Replace('__MONSTER__', $monsterMethod.Value).
    Replace('__MONSTER_STATE__', $monsterState.Value).
    Replace('__HANDLER__', $handler.Value)
Add-Type -TypeDefinition $harness -IgnoreWarnings

function Assert-True($condition, [string]$description) {
    if (-not $condition) { throw $description }
}
function New-Attack([string]$type, [string]$id,
    [long]$sequence = 1, [long]$eventId = 1) {
    $message = [Game.Client.BattleSync.CombatEventMessage]::new()
    $message.eventType = 'attack'
    $message.actionSequence = $sequence
    $message.eventId = $eventId
    $message.source = [Game.Client.BattleSync.CombatEntityMessage]::new()
    $message.source.entityType = $type
    $message.source.entityId = $id
    return $message
}

$players = [Game.Client.BattleSync.EntityManager]::new()
$players.SetLocal(11)
$remoteView = [Game.Client.BattleSync.RemotePlayerSync]::new()
$remoteView.Init(22, 'hero', 'Hero')
$remoteView.SetAttackEventRoutingEnabled($true)
$players.AddRemote(22, $remoteView)
$monsters = [Game.Client.BattleSync.MonsterSnapshotManager]::new()
$monsterView = [Game.Client.BattleSync.MonsterNetworkView]::new()
$monsters.AddMonster('m1', $monsterView, 'Idle')
$runtimeView = [Game.Client.BattleSync.BattleSyncRuntimeHarness]::new($players, $monsters)

$runtimeView.Dispatch((New-Attack 'player' '22' 1 101))
Assert-True ($remoteView.Animations -eq 1) 'Remote player source must use its existing animation entry.'
$runtimeView.Dispatch((New-Attack 'player' '22' 1 102))
Assert-True ($remoteView.Animations -eq 1) 'Same remote player action sequence must not restart animation.'
$runtimeView.Dispatch((New-Attack 'player' '22' 2 103))
Assert-True ($remoteView.Animations -eq 2) 'Next remote player action must restart the attack animation.'
$remoteView.Init(22, 'hero', 'Hero')
$remoteView.ClearPendingSnapshots()
$runtimeView.Dispatch((New-Attack 'player' '22' 2 103))
Assert-True ($remoteView.Animations -eq 2) 'Repeated enter and snapshot cleanup must preserve attack deduplication.'
$runtimeView.Dispatch((New-Attack 'player' '22' 3 105))
Assert-True ($remoteView.Animations -eq 3) 'New attack after repeated enter must still animate.'
$runtimeView.Dispatch((New-Attack 'player' '11' 1 104))
Assert-True ($remoteView.Animations -eq 3) 'Local player source must not replay the predicted remote animation.'
$monsters.SimulateState('m1', 'Attack')
Assert-True ($monsterView.Animations -eq 0) 'Coordinator-owned monster state must not trigger attack before the reliable event.'
$runtimeView.Dispatch((New-Attack 'monster' 'm1' 1 201))
Assert-True ($monsterView.Animations -eq 1 -and $monsterView.MovementStops -eq 1) 'Monster attack plays and cancels movement.'
$monsters.SimulateState('m1', 'Attack')
$runtimeView.Dispatch((New-Attack 'monster' 'm1' 1 202))
Assert-True ($monsterView.Animations -eq 1 -and $monsterView.MovementStops -eq 1) 'Duplicate attack must not replay the attack or cancel movement again.'
$monsters.SetMotionTick('m1', 40)
$lateAttack = New-Attack 'monster' 'm1' 2 203
$lateAttack.serverTick = 30
$runtimeView.Dispatch($lateAttack)
Assert-True ($monsterView.Animations -eq 2 -and $monsterView.LastEventSequence -eq 2 -and $monsterView.MovementStops -eq 1) 'Late attack still plays but must not cancel newer server movement.'

$runtimeView.Dispatch((New-Attack 'monster' 'missing'))
$runtimeView.Dispatch((New-Attack 'player' '999'))
$runtimeView.Dispatch((New-Attack 'player' 'not-a-number'))
$runtimeView.Dispatch($null)
$damage = New-Attack 'monster' 'm1'
$damage.eventType = 'damage'
$runtimeView.Dispatch($damage)
Assert-True ($remoteView.Animations -eq 3 -and $monsterView.Animations -eq 2) 'Unknown sources and non-attack events must not play an action.'

$legacyMonsters = [Game.Client.BattleSync.MonsterSnapshotManager]::new()
$legacyMonsters.SetMode([Game.Client.BattleSync.BattleSyncSnapshotRoutingMode]::LegacySelfRegistered)
$legacyView = [Game.Client.BattleSync.MonsterNetworkView]::new()
$legacyMonsters.AddMonster('legacy', $legacyView, 'Idle')
$legacyMonsters.SimulateState('legacy', 'Attack')
Assert-True ($legacyView.Animations -eq 1) 'Legacy monster state must keep its original attack animation path.'

$throwingView = [Game.Client.BattleSync.MonsterNetworkView]::new()
$throwingView.ThrowOnPlay = $true
$monsters.AddMonster('m2', $throwingView, 'Idle')
$runtimeView.Dispatch((New-Attack 'monster' 'm2'))
Assert-True ([UnityEngine.Debug]::WarningCount -eq 1) 'Visual failures must not throw into reliable business processing.'

Write-Output 'PASS: attack presentation routing, event/state deduplication and legacy fallback.'
