$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Get-Content (Join-Path $root 'Assets/Scripts/Monster/Network/MonsterSnapshotManager.cs') -Raw
$dynamic = [regex]::Match($source,
    '(?s)    public BattleSyncApplyResult ApplyMonsterDynamicState\(DecodedMonsterState state,long serverTick,long serverTime\).*?(?=    private static string ResolveAiState)')
$resolveAi = [regex]::Match($source,
    '(?s)    private static string ResolveAiState\(byte value\).*?(?=    private static MonsterSnapshotItem ConvertMonster)')
$resolveMovement = [regex]::Match($source,
    '(?s)    private static MonsterLocomotionMode ResolveLocomotionMode\(string state\).*?(?=    private static bool IsAttackState)')
if (-not $dynamic.Success -or -not $resolveAi.Success -or -not $resolveMovement.Success) {
    throw 'Monster dynamic presentation methods were not found.'
}

$harness = @'
using System;
using System.Collections.Generic;
using UnityEngine;
namespace UnityEngine
{
    public static class Debug
    {
        public static int WarningCount;
        public static void LogWarning(string message, object context) { WarningCount++; }
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x=x; this.y=y; this.z=z; }
        public static Vector3 zero => new Vector3();
    }
    public struct Quaternion
    {
        public static Quaternion Euler(float x, float y, float z) => new Quaternion();
    }
    public sealed class Transform
    {
        public Vector3 position;
        public Quaternion rotation;
    }
    public sealed class GameObject
    {
        public readonly Transform transform = new Transform();
    }
}
public enum MonsterLocomotionMode { Grounded, TakingOff, Flying, Landing }
public enum BattleSyncSnapshotRoutingMode { LegacySelfRegistered, CoordinatorOwned }
public sealed class MonsterViewState
{
    public string entityId;
    public long stateSequence;
    public Vector3 position;
    public Quaternion rotation;
    public Vector3 velocity;
    public bool preserveTransform, preservePresentation;
    public float moveSpeed;
    public MonsterLocomotionMode locomotionMode;
    public int hp, maxHp;
}
public sealed class MonsterNetworkView
{
    public int CurrentHp = 100;
    public int MaxHp = 100;
    public int DeathSignals;
    public MonsterLocomotionMode LastLocomotionMode;
    public MonsterViewState LastState;
    public bool ApplyState(MonsterViewState state)
    {
        LastLocomotionMode = state.locomotionMode;
        LastState = state;
        return true;
    }
    public void SetMovementPath(object path,long time) {}
    public void CorrectMovementPath(Vector3 p,long time) {}
    public void BootstrapState(string state)
    {
        if (state == "Dead") DeathSignals++;
    }
}
public sealed class DecodedMonsterState
{
    public string EntityId;
    public bool HasPosition, HasAiState, HasHp, HasCombatRevision;
    public float X, Y, Z, YawDegrees;
    public byte AiState;
    public bool HasPathUpdate;
    public object Path;
    public int Hp;
    public long CombatRevision;
}
public sealed class CombatStateData
{
    public long CombatRevision;
    public long SpawnGeneration, GenerationServerTick;
}
public sealed class CombatStateStore
{
    public CombatStateData State;
    public string ActiveSceneId;
    public bool TryGetState(string type, string id, out CombatStateData state)
    {
        state = State;
        return state != null;
    }
    public bool MergeDynamicMonsterHealth(string scene, string id, int hp, long revision, long tick,
        long generation) { return true; }
}
public enum BattleSyncApplyResult { Applied, InvalidMessage, RetryableFailure, IgnoredStale }
public sealed class MonsterSnapshotManager
{
    private sealed class MonsterClientEntry
    {
        public GameObject gameObject = new GameObject();
        public MonsterNetworkView networkView;
        public string lastServerState;
        public string lastAiState;
        public int unknownAiFrames;
        public long lastUnknownAiRepairTick;
    }
    private readonly Dictionary<string, MonsterClientEntry> monsters =
        new Dictionary<string, MonsterClientEntry>();
    private readonly Dictionary<string, long> monsterSpawnGenerations =
        new Dictionary<string, long>();
    private long localStateSequence;
    private BattleSyncSnapshotRoutingMode snapshotRoutingMode = BattleSyncSnapshotRoutingMode.LegacySelfRegistered;
    private CombatStateStore combatStateStore;
    private string activeSceneId;
    public event Action<string> CombatRevisionGapDetected;
    public event Action<string> MonsterSnapshotRepairRequested;
    public int RepairCount;
    public string GetAiState(string id) { return monsters[id].lastAiState; }
    public void SetCombatState(CombatStateData state, long generation)
    {
        combatStateStore = new CombatStateStore { State = state };
        activeSceneId = "world_001";
        combatStateStore.ActiveSceneId = activeSceneId;
        snapshotRoutingMode = BattleSyncSnapshotRoutingMode.CoordinatorOwned;
        monsterSpawnGenerations["sl002"] = generation;
    }
    public MonsterSnapshotManager()
    {
        MonsterSnapshotRepairRequested += id => RepairCount++;
    }
    public void AddMonster(string id, MonsterNetworkView view, string state)
    {
        monsters[id] = new MonsterClientEntry {
            networkView = view, lastServerState = state, lastAiState = state
        };
    }
    private static bool IsAttackState(string s) => s == "Attack";
__DYNAMIC__
__RESOLVE_AI__
__RESOLVE_MOVEMENT__
}
'@.Replace('__DYNAMIC__', $dynamic.Value).
    Replace('__RESOLVE_AI__', $resolveAi.Value).
    Replace('__RESOLVE_MOVEMENT__', $resolveMovement.Value)
Add-Type -TypeDefinition $harness -IgnoreWarnings

$manager = [MonsterSnapshotManager]::new()
$view = [MonsterNetworkView]::new()
$manager.AddMonster('sl002', $view, 'Idle')
$state = [DecodedMonsterState]::new()
$state.EntityId = 'sl002'
$state.HasAiState = $true
$state.AiState = 3
$manager.ApplyMonsterDynamicState($state, 1, 1) | Out-Null
if ($view.DeathSignals -ne 0 -or
    $view.LastLocomotionMode -ne [MonsterLocomotionMode]::Flying) {
    throw 'Chase must animate movement without triggering death.'
}
$state.HasPosition = $true
$state.X = -12
$state.Y = 29.67
$state.Z = -95.1
$state.AiState = 2
$manager.ApplyMonsterDynamicState($state, 2, 2) | Out-Null
if ($view.LastLocomotionMode -ne [MonsterLocomotionMode]::TakingOff -or
    [Math]::Abs($view.LastState.position.y - 29.67) -gt 0.001 -or
    [Math]::Abs($view.LastState.position.x + 12) -gt 0.001) {
    throw 'Takeoff must retain signed position and flight altitude.'
}
$state.AiState = 6
$manager.ApplyMonsterDynamicState($state, 3, 3) | Out-Null
if ($view.LastLocomotionMode -ne [MonsterLocomotionMode]::Landing -or
    [Math]::Abs($view.LastState.position.y - 29.67) -gt 0.001) {
    throw 'Landing must retain altitude until a new server position arrives.'
}
$state.HasPosition = $false
$state.HasAiState = $false
$manager.ApplyMonsterDynamicState($state, 4, 4) | Out-Null
if ($view.LastLocomotionMode -ne [MonsterLocomotionMode]::Landing) {
    throw 'AI-free frames must preserve the last AI locomotion.'
}
$state.HasAiState = $true
$state.AiState = 3
$manager.ApplyMonsterDynamicState($state, 5, 5) | Out-Null
$state.HasAiState = $true
$state.AiState = 0
$manager.ApplyMonsterDynamicState($state, 7, 7) | Out-Null
$manager.ApplyMonsterDynamicState($state, 8, 8) | Out-Null
if ($manager.GetAiState('sl002') -ne 'Chase' -or $manager.RepairCount -ne 0 -or
    $view.LastLocomotionMode -ne [MonsterLocomotionMode]::Flying) {
    throw 'Unknown AI state must preserve the current animation without an early repair.'
}
$manager.ApplyMonsterDynamicState($state, 9, 9) | Out-Null
$manager.ApplyMonsterDynamicState($state, 10, 10) | Out-Null
if ($manager.RepairCount -ne 1 -or [UnityEngine.Debug]::WarningCount -ne 1) {
    throw 'Continuous unknown AI must trigger one throttled diagnostic and repair.'
}

$state.HasAiState = $true
$state.AiState = 7
$manager.ApplyMonsterDynamicState($state, 3, 3) | Out-Null
$manager.ApplyMonsterDynamicState($state, 4, 4) | Out-Null
if ($view.DeathSignals -ne 1) { throw 'Dead AI state must trigger death only once.' }

$state.HasAiState = $false
$manager.ApplyMonsterDynamicState($state, 5, 5) | Out-Null
$state.HasAiState = $true
$state.AiState = 7
$manager.ApplyMonsterDynamicState($state, 6, 6) | Out-Null
if ($view.DeathSignals -ne 1) { throw 'An AI-free frame must not reset the death deduplication.' }

$storeState = [CombatStateData]::new()
$storeState.SpawnGeneration = 1
$storeState.GenerationServerTick = 10
$manager.SetCombatState($storeState, 2)
$state.AiState = 3
if ($manager.ApplyMonsterDynamicState($state, 11, 11) -ne [BattleSyncApplyResult]::IgnoredStale -or
    $manager.RepairCount -ne 2) {
    throw 'Mismatched combat generation must not update the monster presentation.'
}

Write-Output 'PASS: monster compact movement persists and death routes once to the visual driver.'
