using System;
using System.Collections.Generic;
using Game.Client.BattleSync;
using Game.Client.BattleSync.Protocol;
using Game.Monsters.Config;
using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-50)]
public class MonsterSnapshotManager : MonoBehaviour, IMonsterBattleSyncModule, IBattleSyncSnapshotRoutingControl
{
    public event Action<string> CombatRevisionGapDetected;
    public event Action<string> MonsterSnapshotRepairRequested;
    private sealed class MonsterClientEntry
    {
        public GameObject gameObject;
        public MonsterNetworkView networkView;
        public string lastServerState;
        public string lastAiState;
        public long lastAttackActionSequence;
        public long lastAttackEventId;
        public int unknownAiFrames;
        public long lastUnknownAiRepairTick;
    }

    private sealed class MonsterDefinition
    {
        public string monsterId;
        public string name;
    }

    private struct MonsterVersion
    {
        public long revision;
        public long serverTime;

        public MonsterVersion(long revision, long serverTime)
        {
            this.revision = revision;
            this.serverTime = serverTime;
        }
    }

    [Header("Scene")]
    [SerializeField] private string expectedSceneId = "world_001";
    [SerializeField] private Transform monsterRoot;
    [SerializeField] private bool removeMissingMonsters = true;

    [Header("怪物总表")]
    [SerializeField] private MonsterCatalog monsterCatalog;

    [Header("Combat")]
    [SerializeField] private CombatStateStore combatStateStore;

    private readonly Dictionary<string, MonsterClientEntry> monsters =
        new Dictionary<string, MonsterClientEntry>();
    private readonly Dictionary<string, MonsterVersion> monsterVersions =
        new Dictionary<string, MonsterVersion>();
    private readonly Dictionary<string,long> monsterSpawnGenerations=new Dictionary<string,long>();
    private readonly Dictionary<string, MonsterDefinition> monsterDefinitions =
        new Dictionary<string, MonsterDefinition>();
    private readonly HashSet<string> reportedMissingMonsterIds = new HashSet<string>(StringComparer.Ordinal);
    private readonly HashSet<string> snapshotMonsterIds = new HashSet<string>();
    private readonly List<string> removedMonsterIds = new List<string>();

    private NetMessageDispatcher registeredDispatcher;
    private CombatStateStore subscribedCombatStateStore;
    private long localStateSequence;
    private long localEventSequence;
    private string activeSceneId;
    private long activeConnectionEpoch;
    private BattleSyncSnapshotRoutingMode snapshotRoutingMode = BattleSyncSnapshotRoutingMode.LegacySelfRegistered;

    public void CopyLivingMonsterPositions(string sceneId, List<Vector2> positions)
    {
        positions.Clear();
        if (!isActiveAndEnabled || !string.Equals(activeSceneId, sceneId, StringComparison.Ordinal))
            return;

        foreach (MonsterClientEntry monster in monsters.Values)
        {
            MonsterNetworkView view = monster.networkView;
            if (view == null || !view.IsTargetable || monster.gameObject == null)
                continue;

            Vector3 position = monster.gameObject.transform.position;
            positions.Add(new Vector2(position.x, position.z));
        }
    }

    private void OnEnable()
    {
        EnsureMessageRegistration();
        EnsureCombatStateSubscription();
    }

    private void Update()
    {
        EnsureMessageRegistration();
        EnsureCombatStateSubscription();
    }

    private void OnDisable()
    {
        RemoveMessageRegistration();
        RemoveCombatStateSubscription();
    }

    private void OnDestroy()
    {
        ClearAllMonsters();
    }

    private void EnsureMessageRegistration()
    {
        NetMessageDispatcher currentDispatcher = NetMessageDispatcher.Instance;

        if (registeredDispatcher == currentDispatcher)
        {
            return;
        }

        RemoveMessageRegistration();

        if (currentDispatcher == null)
        {
            return;
        }

        currentDispatcher.Register("enter_world_result", HandleEnterWorldResult);
        if (snapshotRoutingMode == BattleSyncSnapshotRoutingMode.LegacySelfRegistered)
            currentDispatcher.Register("monster_snapshot", HandleMonsterSnapshot);
        if(snapshotRoutingMode==BattleSyncSnapshotRoutingMode.LegacySelfRegistered)
        {
            currentDispatcher.Register("monster_upsert", HandleMonsterUpsert);
            currentDispatcher.Register("monster_remove", HandleMonsterRemove);
        }
        registeredDispatcher = currentDispatcher;
    }

    public void SetSnapshotRoutingMode(BattleSyncSnapshotRoutingMode mode)
    {
        if (snapshotRoutingMode == mode) return;
        RemoveMessageRegistration();snapshotRoutingMode = mode;
        if(isActiveAndEnabled)EnsureMessageRegistration();
    }

    public BattleSyncApplyResult BeginSession(BattleSyncModuleSessionContext context)
    {
        if (!context.IsValid) return BattleSyncApplyResult.InvalidMessage;
        activeConnectionEpoch = context.ConnectionEpoch;
        activeSceneId = context.SceneId;
        ClearAllMonsters(); monsterVersions.Clear(); monsterDefinitions.Clear();monsterSpawnGenerations.Clear();
        reportedMissingMonsterIds.Clear();
        return BattleSyncApplyResult.Applied;
    }

    public void EndSession(long connectionEpoch)
    {
        if (connectionEpoch != activeConnectionEpoch) return;
        ClearAllMonsters(); monsterVersions.Clear(); monsterDefinitions.Clear();monsterSpawnGenerations.Clear();
        reportedMissingMonsterIds.Clear();
        activeConnectionEpoch = 0; activeSceneId = string.Empty;
    }

    public BattleSyncApplyResult ApplyMonsterSnapshot(MonsterSnapshotMessageDto message)
    {
        if (message == null) return BattleSyncApplyResult.InvalidMessage;
        if (!string.Equals(activeSceneId, message.sceneId, StringComparison.Ordinal)) return BattleSyncApplyResult.IgnoredWrongScene;
        MonsterStateDto[] items = message.monsters ?? Array.Empty<MonsterStateDto>();
        HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < items.Length; i++)
            if (items[i] == null || string.IsNullOrWhiteSpace(items[i].monsterInstanceId)
                || string.IsNullOrWhiteSpace(items[i].monsterId)
                || items[i].spawnGeneration < 0 || !ids.Add(items[i].monsterInstanceId))
                return BattleSyncApplyResult.InvalidMessage;
        if (WorldEntryLoadingView.ActiveWorldEntry != null || message.reason == "map_transfer")
        {
            foreach (MonsterStateDto item in items)
            {
                GameObject prefab = FindPrefabById(item.monsterId);
                if (prefab != null && HasRequiredMonsterComponents(prefab))
                    continue;
                if (reportedMissingMonsterIds.Add(item.monsterId))
                    Debug.LogWarning("[BATTLE_SYNC][MONSTER_RESOURCE_MISSING] monsterId=" + item.monsterId, this);
                return BattleSyncApplyResult.RetryableFailure;
            }
        }
        for (int i = 0; i < items.Length; i++)
            if (monsterSpawnGenerations.TryGetValue(items[i].monsterInstanceId, out long currentGeneration)
                && items[i].spawnGeneration < currentGeneration)
                return BattleSyncApplyResult.IgnoredStale;
        snapshotMonsterIds.Clear();
        for (int i = 0; i < items.Length; i++)
        {
            MonsterSnapshotItem item = ConvertMonster(items[i]); snapshotMonsterIds.Add(item.monsterInstanceId);
            MonsterClientEntry entry = ApplyMonsterState(item, items[i].spawnGeneration);
            if (entry == null) return BattleSyncApplyResult.RetryableFailure;
            CacheMonsterDefinition(item); monsterVersions[item.monsterInstanceId] = new MonsterVersion(item.aoiRevision, message.serverTime);
            monsterSpawnGenerations[item.monsterInstanceId] = items[i].spawnGeneration;
            MergeMonsterSpawnCombatState(items[i], message.serverTick);
            entry.networkView.SetMovementPath(CanPlayPath(entry, item.state) ? items[i].path : null, message.serverTime);
        }
        RemoveMissingMonsters();
        return BattleSyncApplyResult.Applied;
    }

    public BattleSyncApplyResult ApplyMonsterEnter(MonsterEnterMessageDto message)
    {
        if(message==null||message.monster==null||string.IsNullOrWhiteSpace(message.monster.monsterId)
            ||message.monster.spawnGeneration<0)
            return BattleSyncApplyResult.InvalidMessage;
        if(!string.Equals(activeSceneId,message.sceneId,StringComparison.Ordinal))return BattleSyncApplyResult.IgnoredWrongScene;
        string id=message.monster.monsterInstanceId;long generation=message.monster.spawnGeneration;
        if(monsterSpawnGenerations.TryGetValue(id,out long currentGeneration)&&generation<currentGeneration)
            return BattleSyncApplyResult.IgnoredStale;
        MonsterSnapshotItem item=ConvertMonster(message.monster);item.aoiRevision=message.aoiRevision;
        MonsterClientEntry entry=ApplyMonsterState(item,generation);
        if(entry==null)return BattleSyncApplyResult.RetryableFailure;
        CacheMonsterDefinition(item);monsterSpawnGenerations[id]=generation;
        MergeMonsterSpawnCombatState(message.monster, message.serverTick);
        entry.networkView.SetMovementPath(CanPlayPath(entry,item.state) ? message.monster.path : null,message.serverTime);
        monsterVersions[id]=new MonsterVersion(message.aoiRevision,message.serverTime);
        return BattleSyncApplyResult.Applied;
    }

    private void MergeMonsterSpawnCombatState(MonsterStateDto item, long serverTick)
    {
        EnsureCombatStateSubscription();
        if (combatStateStore == null || serverTick <= 0
            || !string.Equals(combatStateStore.ActiveSceneId, activeSceneId, StringComparison.Ordinal))
            return;
        combatStateStore.MergeMonsterSpawnState(activeSceneId, item.monsterInstanceId,
            item.hp, item.maxHp, item.lifeState, item.combatRevision,
            item.spawnGeneration, serverTick);
        if (combatStateStore.TryGetState("monster", item.monsterInstanceId, out CombatStateData state)
            && state.SpawnGeneration == item.spawnGeneration
            && monsters.TryGetValue(item.monsterInstanceId, out MonsterClientEntry entry))
            entry.networkView.ApplyHealth(state.Hp, state.MaxHp);
    }

    public BattleSyncApplyResult ApplyMonsterUpsert(MonsterUpsertMessageDto message)
    {
        if (message == null || message.monster == null
            || string.IsNullOrWhiteSpace(message.monster.monsterInstanceId))
            return BattleSyncApplyResult.InvalidMessage;
        MonsterSnapshotItem item = ConvertMonster(message.monster);
        return ApplyLegacyMonsterUpsert(message.sceneId, item, message.aoiRevision,
            message.serverTime);
    }

    public BattleSyncApplyResult ApplyMonsterRemove(MonsterRemoveMessageDto message)
    {
        if(message==null||string.IsNullOrWhiteSpace(message.monsterInstanceId))return BattleSyncApplyResult.InvalidMessage;
        if(!string.Equals(activeSceneId,message.sceneId,StringComparison.Ordinal))return BattleSyncApplyResult.IgnoredWrongScene;
        if(monsterSpawnGenerations.TryGetValue(message.monsterInstanceId,out long generation))
        {
            if(message.spawnGeneration<generation)return BattleSyncApplyResult.IgnoredStale;
            if(message.spawnGeneration>generation && monsters.ContainsKey(message.monsterInstanceId))
                return BattleSyncApplyResult.RetryableFailure;
        }
        if(!monsters.TryGetValue(message.monsterInstanceId,out MonsterClientEntry entry))
        {
            monsterSpawnGenerations[message.monsterInstanceId]=message.spawnGeneration;
            return BattleSyncApplyResult.AlreadyApplied;
        }
        monsters.Remove(message.monsterInstanceId);DisposeMonster(entry);
        monsterSpawnGenerations[message.monsterInstanceId]=message.spawnGeneration;
        return BattleSyncApplyResult.Applied;
    }

    internal bool TryGetTargetableMonster(string monsterInstanceId, out MonsterNetworkView monster)
    {
        monster = null;
        if (string.IsNullOrEmpty(monsterInstanceId)
            || !monsters.TryGetValue(monsterInstanceId, out MonsterClientEntry entry)
            || entry == null || entry.networkView == null || !entry.networkView.IsTargetable)
            return false;

        monster = entry.networkView;
        return true;
    }

    public bool TryPlayAttack(string monsterInstanceId, long actionSequence, long eventId)
    {
        if (string.IsNullOrEmpty(monsterInstanceId)
            || !monsters.TryGetValue(monsterInstanceId, out MonsterClientEntry entry)
            || entry == null || entry.networkView == null)
            return false;
        if (combatStateStore != null
            && combatStateStore.TryGetState("monster", monsterInstanceId, out CombatStateData current)
            && monsterSpawnGenerations.TryGetValue(monsterInstanceId, out long generation)
            && current.SpawnGeneration != generation)
            return false;
        if ((actionSequence > 0 && actionSequence <= entry.lastAttackActionSequence)
            || (eventId > 0 && eventId <= entry.lastAttackEventId))
            return true;
        localEventSequence++;
        if (!entry.networkView.PlayAction(new MonsterActionData
        {
            entityId = monsterInstanceId,
            eventSequence = localEventSequence,
            actionSlot = 0,
            actionSpeed = 1f
        })) return false;
        if (actionSequence > 0) entry.lastAttackActionSequence = actionSequence;
        if (eventId > 0) entry.lastAttackEventId = eventId;
        return true;
    }

    public BattleSyncApplyResult ApplyMonsterDynamicState(DecodedMonsterState state,long serverTick,long serverTime)
    {
        if(string.IsNullOrWhiteSpace(state.EntityId))return BattleSyncApplyResult.InvalidMessage;
        if(!monsters.TryGetValue(state.EntityId,out MonsterClientEntry entry)||entry==null||entry.networkView==null)
            return BattleSyncApplyResult.RetryableFailure;
        if (snapshotRoutingMode == BattleSyncSnapshotRoutingMode.CoordinatorOwned
            && (combatStateStore == null
                || !string.Equals(combatStateStore.ActiveSceneId, activeSceneId, StringComparison.Ordinal)
                || !combatStateStore.TryGetState("monster", state.EntityId, out _)))
        {
            MonsterSnapshotRepairRequested?.Invoke(state.EntityId);
            return BattleSyncApplyResult.RetryableFailure;
        }
        if (combatStateStore != null && combatStateStore.TryGetState("monster", state.EntityId,
                out CombatStateData combat)
            && monsterSpawnGenerations.TryGetValue(state.EntityId, out long generation)
            && (combat.SpawnGeneration != generation
                || (combat.GenerationServerTick > 0 && serverTick < combat.GenerationServerTick)))
        {
            MonsterSnapshotRepairRequested?.Invoke(state.EntityId);
            return BattleSyncApplyResult.IgnoredStale;
        }
        Vector3 position=state.HasPosition?new Vector3(state.X,state.Y,state.Z):entry.gameObject.transform.position;
        Quaternion rotation=state.HasPosition?Quaternion.Euler(0f,state.YawDegrees,0f):entry.gameObject.transform.rotation;
        bool unknownAi = state.HasAiState && state.AiState == 0;
        string aiState=entry.networkView.CurrentHp <= 0 ? "Dead"
            : state.HasAiState && !unknownAi ? ResolveAiState(state.AiState) : entry.lastAiState;
        if (unknownAi)
        {
            if (entry.unknownAiFrames < 3) entry.unknownAiFrames++;
            if (entry.unknownAiFrames >= 3
                && (entry.lastUnknownAiRepairTick == 0
                    || serverTick - entry.lastUnknownAiRepairTick >= 100))
            {
                entry.lastUnknownAiRepairTick = serverTick;
                Debug.LogWarning("[BATTLE_SYNC][MONSTER_AI_UNKNOWN] monsterInstanceId="
                    + state.EntityId + " serverTick=" + serverTick, this);
                MonsterSnapshotRepairRequested?.Invoke(state.EntityId);
            }
        }
        else if (state.HasAiState) entry.unknownAiFrames = 0;
        bool behaviorChanged=state.HasAiState&&!unknownAi
            &&!string.Equals(entry.lastAiState,aiState,StringComparison.OrdinalIgnoreCase);
        if(behaviorChanged)entry.networkView.SetMovementPath(null,serverTime);
        localStateSequence++;
        MonsterViewState viewState=new MonsterViewState{entityId=state.EntityId,stateSequence=localStateSequence,
            position=position,rotation=rotation,velocity=Vector3.zero,
            preserveTransform=!state.HasPosition,
            preservePresentation=!state.HasAiState || unknownAi,
            moveSpeed=string.Equals(aiState,"Hover",StringComparison.OrdinalIgnoreCase) ? 0f
                : ResolveLocomotionMode(aiState)==MonsterLocomotionMode.Flying ? 1f : 0f,
            locomotionMode=ResolveLocomotionMode(aiState),
            hp=entry.networkView.CurrentHp,maxHp=entry.networkView.MaxHp};
        if(!entry.networkView.ApplyState(viewState))return BattleSyncApplyResult.IgnoredStale;
        if(behaviorChanged&&!IsAttackState(aiState))
            entry.networkView.BootstrapState(aiState);
        if(state.HasAiState&&!unknownAi)entry.lastAiState=aiState;
        if(state.HasHp&&state.HasCombatRevision&&combatStateStore!=null)
        {
            bool revisionGap=combatStateStore.TryGetState("monster",state.EntityId,out CombatStateData current)
                &&current!=null&&state.CombatRevision>current.CombatRevision
                &&state.CombatRevision-current.CombatRevision>1;
            if (monsterSpawnGenerations.TryGetValue(state.EntityId, out long currentGeneration)
                && combatStateStore.MergeDynamicMonsterHealth(activeSceneId, state.EntityId,
                    state.Hp, state.CombatRevision, serverTick, currentGeneration)
                && revisionGap)
                CombatRevisionGapDetected?.Invoke(state.EntityId);
        }
        if (state.HasPathUpdate || !CanPlayPath(entry,aiState))
            entry.networkView.SetMovementPath(CanPlayPath(entry,aiState) ? state.Path : null,serverTime);
        else if (state.HasPosition)
            entry.networkView.CorrectMovementPath(position,serverTime);
        return BattleSyncApplyResult.Applied;
    }

    private static bool CanPlayPath(MonsterClientEntry entry,string state) =>
        entry.networkView.CurrentHp > 0 && (string.Equals(state,"Chase",StringComparison.OrdinalIgnoreCase)
            || string.Equals(state,"Return",StringComparison.OrdinalIgnoreCase)
            || string.Equals(state,"Patrol",StringComparison.OrdinalIgnoreCase));

    private static string ResolveAiState(byte value)
    {
        switch(value){case 1:return "Idle";case 2:return "TakeOff";case 3:return "Chase";
            case 4:return "Attack";case 5:return "Return";case 6:return "Landing";case 7:return "Dead";case 8:return "Patrol";case 9:return "Hover";default:return "Unknown";}
    }

    private static MonsterSnapshotItem ConvertMonster(MonsterStateDto item)
    {
        return new MonsterSnapshotItem { monsterInstanceId=item.monsterInstanceId, spawnPointId=item.spawnPointId,
            monsterId=item.monsterId, prefabId=item.prefabId, name=item.name, level=item.level, sceneId=item.sceneId,
            mapId=item.mapId, instanceId=item.instanceId, spawnGroupId=item.spawnGroupId, x=item.x,y=item.y,z=item.z,
            rotY=item.rotY,hp=item.hp,maxHp=item.maxHp,state=item.state,aoiRevision=item.aoiRevision };
    }

    private void RemoveMessageRegistration()
    {
        if (registeredDispatcher == null)
        {
            return;
        }

        registeredDispatcher.Unregister("enter_world_result", HandleEnterWorldResult);
        registeredDispatcher.Unregister("monster_snapshot", HandleMonsterSnapshot);
        registeredDispatcher.Unregister("monster_upsert", HandleMonsterUpsert);
        registeredDispatcher.Unregister("monster_remove", HandleMonsterRemove);
        registeredDispatcher = null;
    }

    private void HandleEnterWorldResult(string json)
    {
        EnterWorldResult message = JsonUtility.FromJson<EnterWorldResult>(json);
        if (message == null || !message.success)
        {
            return;
        }

        ClearAllMonsters();
        monsterVersions.Clear();
        monsterDefinitions.Clear();
        monsterSpawnGenerations.Clear();
        reportedMissingMonsterIds.Clear();
        activeSceneId = !string.IsNullOrEmpty(message.sceneId)
            ? message.sceneId
            : (message.self == null ? "" : message.self.sceneId);
    }

    private void HandleMonsterSnapshot(string json)
    {
        MonsterSnapshotMessage message;

        try
        {
            message = JsonUtility.FromJson<MonsterSnapshotMessage>(json);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("Monster snapshot parse failed: " + ex.Message, this);
            return;
        }

        if (message == null || message.monsters == null || !IsExpectedScene(message.sceneId))
        {
            return;
        }

        if (!string.IsNullOrEmpty(activeSceneId)
            && !string.Equals(activeSceneId, message.sceneId, StringComparison.Ordinal))
        {
            ClearAllMonsters();
            monsterVersions.Clear();
            monsterDefinitions.Clear();
            monsterSpawnGenerations.Clear();
            reportedMissingMonsterIds.Clear();
        }

        activeSceneId = message.sceneId;
        snapshotMonsterIds.Clear();

        for (int index = 0; index < message.monsters.Length; index++)
        {
            MonsterSnapshotItem item = message.monsters[index];
            if (item == null || string.IsNullOrWhiteSpace(item.monsterInstanceId))
            {
                continue;
            }

            snapshotMonsterIds.Add(item.monsterInstanceId);
            CacheMonsterDefinition(item);
            monsterVersions[item.monsterInstanceId] = new MonsterVersion(item.aoiRevision, message.serverTime);

            bool created = !monsters.ContainsKey(item.monsterInstanceId);
            MonsterClientEntry entry = ApplyMonsterState(item);
            if (created && entry != null)
            {
                Debug.Log(
                    "[CLIENT] MONSTER_SNAPSHOT created id=" + item.monsterInstanceId
                    + " state=" + item.state
                    + " revision=" + item.aoiRevision);
            }
        }

        if (removeMissingMonsters)
        {
            RemoveMissingMonsters();
        }
    }

    private void HandleMonsterUpsert(string json)
    {
        MonsterUpsertMessage message;

        try
        {
            message = JsonUtility.FromJson<MonsterUpsertMessage>(json);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("Monster upsert parse failed: " + ex.Message, this);
            return;
        }

        if (message == null) return;
        ApplyLegacyMonsterUpsert(message.sceneId, message.monster,
            message.aoiRevision, message.serverTime);
    }

    private BattleSyncApplyResult ApplyLegacyMonsterUpsert(
        string sceneId, MonsterSnapshotItem item, long aoiRevision, long serverTime)
    {
        if (item == null || string.IsNullOrWhiteSpace(item.monsterInstanceId))
            return BattleSyncApplyResult.InvalidMessage;
        if (!IsActiveScene(sceneId)) return BattleSyncApplyResult.IgnoredWrongScene;
        string monsterInstanceId = item.monsterInstanceId;
        if (snapshotRoutingMode == BattleSyncSnapshotRoutingMode.CoordinatorOwned
            && monsterSpawnGenerations.ContainsKey(monsterInstanceId))
            return BattleSyncApplyResult.IgnoredStale;
        long revision = aoiRevision != 0 ? aoiRevision : item.aoiRevision;
        if (IsOlder(monsterInstanceId, revision, serverTime))
        {
            Debug.Log(
                "[CLIENT] MONSTER_UPSERT dropped old id=" + monsterInstanceId
                + " revision=" + revision
                + " serverTime=" + serverTime);
            return BattleSyncApplyResult.IgnoredStale;
        }

        bool hadVersion = monsterVersions.ContainsKey(monsterInstanceId);
        bool existed = monsters.ContainsKey(monsterInstanceId);
        string previousState = existed ? monsters[monsterInstanceId].lastServerState : "";

        item.aoiRevision = revision;
        if (string.IsNullOrEmpty(item.sceneId))
        {
            item.sceneId = sceneId;
        }

        CacheMonsterDefinition(item);
        ApplyCachedDefinition(item);
        monsterVersions[monsterInstanceId] = new MonsterVersion(revision, serverTime);

        MonsterClientEntry entry = ApplyMonsterState(item);
        if (entry == null)
        {
            return BattleSyncApplyResult.RetryableFailure;
        }

        bool recreated = !existed && hadVersion;
        if (recreated)
        {
            Debug.Log(
                "[CLIENT] MONSTER_UPSERT recreated id=" + monsterInstanceId
                + " state=" + item.state
                + " revision=" + revision
                + " serverTime=" + serverTime);
        }
        else if (!existed || !string.Equals(previousState, item.state, StringComparison.OrdinalIgnoreCase))
        {
            Debug.Log(
                "[CLIENT] MONSTER_UPSERT applied id=" + monsterInstanceId
                + " state=" + item.state
                + " revision=" + revision
                + " serverTime=" + serverTime);
        }
        return BattleSyncApplyResult.Applied;
    }

    private void HandleMonsterRemove(string json)
    {
        MonsterRemoveMessage message;

        try
        {
            message = JsonUtility.FromJson<MonsterRemoveMessage>(json);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("Monster remove parse failed: " + ex.Message, this);
            return;
        }

        if (message == null
            || string.IsNullOrWhiteSpace(message.monsterInstanceId)
            || !IsActiveScene(message.sceneId)
            || IsOlder(message.monsterInstanceId, message.aoiRevision, message.serverTime))
        {
            return;
        }

        monsterVersions[message.monsterInstanceId] =
            new MonsterVersion(message.aoiRevision, message.serverTime);

        if (!monsters.TryGetValue(message.monsterInstanceId, out MonsterClientEntry entry))
        {
            return;
        }

        monsters.Remove(message.monsterInstanceId);
        DisposeMonster(entry);

        Debug.Log(
            "[CLIENT] MONSTER_REMOVE removed id=" + message.monsterInstanceId
            + " revision=" + message.aoiRevision
            + " serverTime=" + message.serverTime);
    }

    private MonsterClientEntry ApplyMonsterState(MonsterSnapshotItem item, long? spawnGeneration = null)
    {
        if (spawnGeneration.HasValue
            && monsters.TryGetValue(item.monsterInstanceId, out MonsterClientEntry previous)
            && (!monsterSpawnGenerations.TryGetValue(item.monsterInstanceId, out long currentGeneration)
                || spawnGeneration.Value > currentGeneration))
        {
            monsters.Remove(item.monsterInstanceId);
            DisposeMonster(previous);
        }

        bool created = false;
        if (!monsters.TryGetValue(item.monsterInstanceId, out MonsterClientEntry entry)
            || entry == null || entry.networkView == null || entry.gameObject == null)
        {
            if (entry != null)
            {
                monsters.Remove(item.monsterInstanceId);
                DisposeMonster(entry);
            }
            entry = SpawnMonster(item);
            if (entry == null)
            {
                return null;
            }

            created = true;
        }
        else
        {
            if (!entry.networkView.ApplyState(BuildViewState(item)))
                return null;
        }

        ApplyStoredCombatState(item.monsterInstanceId, entry, spawnGeneration);
        if (!created && !IsAttackState(item.state)
            && !string.Equals(entry.lastAiState, item.state, StringComparison.OrdinalIgnoreCase))
            entry.networkView.BootstrapState(item.state);
        if (!string.Equals(entry.lastAiState,item.state,StringComparison.OrdinalIgnoreCase)
            || !CanPlayPath(entry, item.state)) entry.networkView.SetMovementPath(null, 0);

        bool animatorApplied = true;
        if (!created && IsAttackState(item.state)
            && snapshotRoutingMode != BattleSyncSnapshotRoutingMode.CoordinatorOwned)
        {
            localEventSequence++;
            animatorApplied = entry.networkView.PlayAction(new MonsterActionData
            {
                entityId = item.monsterInstanceId,
                eventSequence = localEventSequence,
                actionSlot = 0,
                actionSpeed = 1f
            });
        }

        if (!string.Equals(entry.lastServerState, item.state, StringComparison.OrdinalIgnoreCase))
        {
            Debug.Log(
                "[CLIENT] MONSTER_STATE id=" + item.monsterInstanceId
                + " " + entry.lastServerState + " -> " + item.state
                + " animatorApplied=" + animatorApplied);
        }

        entry.lastServerState = item.state;
        entry.lastAiState = item.state;
        return entry;
    }

    private void EnsureCombatStateSubscription()
    {
        CombatStateStore currentStore = combatStateStore;
        if (currentStore == null)
        {
            currentStore = FindObjectOfType<CombatStateStore>();
        }

        if (subscribedCombatStateStore == currentStore)
        {
            return;
        }

        RemoveCombatStateSubscription();

        if (currentStore == null)
        {
            return;
        }

        combatStateStore = currentStore;
        subscribedCombatStateStore = currentStore;
        subscribedCombatStateStore.StateChanged += HandleCombatStateChanged;
    }

    private void RemoveCombatStateSubscription()
    {
        if (subscribedCombatStateStore == null)
        {
            return;
        }

        subscribedCombatStateStore.StateChanged -= HandleCombatStateChanged;
        subscribedCombatStateStore = null;
    }

    private void HandleCombatStateChanged(CombatEntityKey key, CombatStateData state)
    {
        if (state == null
            || !string.Equals(key.EntityType, "monster", StringComparison.Ordinal)
            || !IsMatchingCombatScene())
        {
            return;
        }

        if (monsters.TryGetValue(key.EntityId, out MonsterClientEntry entry)
            && entry != null
            && entry.networkView != null
            && (!monsterSpawnGenerations.TryGetValue(key.EntityId, out long currentGeneration)
                || state.SpawnGeneration == currentGeneration))
        {
            entry.networkView.ApplyHealth(state.Hp, state.MaxHp);
            if (string.Equals(state.LifeState, "Dead", StringComparison.OrdinalIgnoreCase))
                entry.networkView.BootstrapState("Dead");
        }
    }

    private void ApplyStoredCombatState(string monsterInstanceId, MonsterClientEntry entry,
        long? spawnGeneration = null)
    {
        EnsureCombatStateSubscription();

        if (entry == null
            || entry.networkView == null
            || combatStateStore == null
            || !IsMatchingCombatScene()
            || !combatStateStore.TryGetState("monster", monsterInstanceId, out CombatStateData state)
            || (spawnGeneration.HasValue && state.SpawnGeneration != spawnGeneration.Value))
        {
            return;
        }

        entry.networkView.ApplyHealth(state.Hp, state.MaxHp);
        if (string.Equals(state.LifeState, "Dead", StringComparison.OrdinalIgnoreCase))
            entry.networkView.BootstrapState("Dead");
    }

    private bool IsMatchingCombatScene()
    {
        return combatStateStore != null
            && (string.IsNullOrEmpty(activeSceneId)
                || string.IsNullOrEmpty(combatStateStore.ActiveSceneId)
                || string.Equals(
                    activeSceneId,
                    combatStateStore.ActiveSceneId,
                    StringComparison.Ordinal));
    }

    private MonsterClientEntry SpawnMonster(MonsterSnapshotItem item)
    {
        ApplyCachedDefinition(item);
        GameObject prefab = FindPrefabById(item.monsterId);
        if (prefab != null && !HasRequiredMonsterComponents(prefab))
        {
            Debug.LogWarning("Monster prefab has incomplete network presentation: " + prefab.name, this);
            prefab = null;
        }
        Vector3 position = new Vector3(item.x, item.y, item.z);
        Quaternion rotation = Quaternion.Euler(0f, item.rotY, 0f);
        GameObject instance = prefab == null ? null : Instantiate(prefab, position, rotation, monsterRoot);
        MonsterNetworkView networkView = instance == null ? null : instance.GetComponent<MonsterNetworkView>();
        if (networkView == null)
        {
            if (instance != null)
            {
                Debug.LogWarning("Monster prefab has no MonsterNetworkView: " + prefab.name, this);
                Destroy(instance);
            }
            if (reportedMissingMonsterIds.Add(item.monsterId ?? string.Empty))
                Debug.LogWarning("[BATTLE_SYNC][MONSTER_RESOURCE_MISSING] monsterId=" + item.monsterId, this);
            instance = CreateMissingMonster(position, rotation);
            networkView = instance.GetComponent<MonsterNetworkView>();
        }
        else if (monsterCatalog.SelectionRingPrefab != null)
        {
            Transform monsterTransform = instance.transform;
            var selectionRing = Instantiate(monsterCatalog.SelectionRingPrefab, monsterTransform, false);
            selectionRing.gameObject.SetActive(false);
            selectionRing.SetFollowTarget(monsterTransform, monsterTransform, GroundRingSurfaceFilter.Allows);
            selectionRing.SetGroundScene(SceneManager.GetActiveScene());
            if (instance.TryGetComponent(out CapsuleCollider capsule))
            {
                Bounds bounds = capsule.bounds;
                selectionRing.EnsureMinimumRadius(Mathf.Max(bounds.extents.x, bounds.extents.z) + 0.15f);
            }
            networkView.SelectionRing = selectionRing.gameObject;
        }
        instance.name = "Monster_" + item.monsterInstanceId + "_" + item.monsterId;

        networkView.RebindAnimator();
        networkView.Initialize(BuildViewState(item));
        networkView.BootstrapState(
            snapshotRoutingMode == BattleSyncSnapshotRoutingMode.CoordinatorOwned
                && IsAttackState(item.state) ? "Flying" : item.state);
        MonsterClientEntry entry = new MonsterClientEntry
        {
            gameObject = instance,
            networkView = networkView,
            lastServerState = "",
            lastAiState = item.state
        };
        monsters[item.monsterInstanceId] = entry;
        return entry;
    }

    private static bool HasRequiredMonsterComponents(GameObject prefab)
    {
        if (prefab.GetComponent<MonsterNetworkView>() == null
            || prefab.GetComponent<MonsterViewRoot>() == null
            || prefab.GetComponent<MonsterTransformInterpolator>() == null)
            return false;

        foreach (MonoBehaviour component in prefab.GetComponentsInChildren<MonoBehaviour>(true))
            if (component is IMonsterAnimatorDriver)
                return true;
        return false;
    }

    private GameObject CreateMissingMonster(Vector3 position, Quaternion rotation)
    {
        GameObject root = new GameObject("MissingMonster");
        root.transform.SetParent(monsterRoot, false);
        root.transform.SetPositionAndRotation(position, rotation);
        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        marker.name = "MissingMonsterMarker";
        marker.transform.SetParent(root.transform, false);
        marker.transform.localPosition = Vector3.up;
        Collider collider = marker.GetComponent<Collider>();
        if (collider != null)
        {
            collider.enabled = false;
            Destroy(collider);
        }
        root.AddComponent<MonsterTransformInterpolator>();
        root.AddComponent<MissingMonsterAnimatorDriver>();
        root.AddComponent<MonsterViewRoot>();
        root.AddComponent<MonsterNetworkView>();
        return root;
    }

    private MonsterViewState BuildViewState(MonsterSnapshotItem item)
    {
        localStateSequence++;
        MonsterLocomotionMode locomotionMode = ResolveLocomotionMode(item.state);
        return new MonsterViewState
        {
            entityId = item.monsterInstanceId,
            stateSequence = localStateSequence,
            position = new Vector3(item.x, item.y, item.z),
            rotation = Quaternion.Euler(0f, item.rotY, 0f),
            velocity = Vector3.zero,
            moveSpeed = locomotionMode == MonsterLocomotionMode.Flying
                && !string.Equals(item.state,"Hover",StringComparison.OrdinalIgnoreCase) ? 1f : 0f,
            locomotionMode = locomotionMode,
            hp = item.hp,
            maxHp = item.maxHp
        };
    }

    private bool IsOlder(string monsterInstanceId, long revision, long serverTime)
    {
        if (!monsterVersions.TryGetValue(monsterInstanceId, out MonsterVersion current))
        {
            return false;
        }

        if (revision < current.revision)
        {
            return true;
        }

        return revision == current.revision && serverTime < current.serverTime;
    }

    private void CacheMonsterDefinition(MonsterSnapshotItem item)
    {
        if (!monsterDefinitions.TryGetValue(item.monsterInstanceId, out MonsterDefinition definition))
        {
            definition = new MonsterDefinition();
            monsterDefinitions[item.monsterInstanceId] = definition;
        }

        if (!string.IsNullOrEmpty(item.monsterId))
        {
            definition.monsterId = item.monsterId;
        }

        if (!string.IsNullOrEmpty(item.name))
        {
            definition.name = item.name;
        }
    }

    private void ApplyCachedDefinition(MonsterSnapshotItem item)
    {
        if (!monsterDefinitions.TryGetValue(item.monsterInstanceId, out MonsterDefinition definition))
        {
            return;
        }

        if (string.IsNullOrEmpty(item.monsterId))
        {
            item.monsterId = definition.monsterId;
        }

        if (string.IsNullOrEmpty(item.name))
        {
            item.name = definition.name;
        }
    }

    private GameObject FindPrefabById(string id)
    {
        return monsterCatalog != null && monsterCatalog.TryGet(id, out MonsterDefinitionAsset definition)
            ? definition.Prefab : null;
    }

    private bool IsExpectedScene(string sceneId)
    {
        return string.IsNullOrEmpty(expectedSceneId)
            || string.Equals(expectedSceneId, sceneId, StringComparison.Ordinal);
    }

    private bool IsActiveScene(string sceneId)
    {
        return IsExpectedScene(sceneId)
            && (string.IsNullOrEmpty(activeSceneId)
                || string.Equals(activeSceneId, sceneId, StringComparison.Ordinal));
    }

    private static MonsterLocomotionMode ResolveLocomotionMode(string state)
    {
        string normalizedState = string.IsNullOrEmpty(state) ? "" : state.Trim().ToLowerInvariant();
        switch (normalizedState)
        {
            case "idle":
            case "dead":
                return MonsterLocomotionMode.Grounded;
            case "takeoff":
            case "takingoff":
            case "take_off":
                return MonsterLocomotionMode.TakingOff;
            case "patrol":
            case "hover":
            case "fly":
            case "flying":
            case "chase":
            case "chasing":
            case "attack":
            case "return":
            case "returning":
                return MonsterLocomotionMode.Flying;
            case "land":
            case "landing":
                return MonsterLocomotionMode.Landing;
            default:
                return MonsterLocomotionMode.Grounded;
        }
    }

    private static bool IsAttackState(string state)
    {
        return string.Equals(state, "Attack", StringComparison.OrdinalIgnoreCase);
    }

    private void RemoveMissingMonsters()
    {
        removedMonsterIds.Clear();
        foreach (KeyValuePair<string, MonsterClientEntry> pair in monsters)
        {
            if (!snapshotMonsterIds.Contains(pair.Key))
            {
                removedMonsterIds.Add(pair.Key);
            }
        }

        for (int index = 0; index < removedMonsterIds.Count; index++)
        {
            string monsterInstanceId = removedMonsterIds[index];
            MonsterClientEntry entry = monsters[monsterInstanceId];
            monsters.Remove(monsterInstanceId);
            DisposeMonster(entry);
        }
    }

    private void ClearAllMonsters()
    {
        foreach (KeyValuePair<string, MonsterClientEntry> pair in monsters)
        {
            DisposeMonster(pair.Value);
        }

        monsters.Clear();
        snapshotMonsterIds.Clear();
    }

    private void DisposeMonster(MonsterClientEntry entry)
    {
        if (entry == null)
        {
            return;
        }

        if (entry.networkView != null)
        {
            entry.networkView.StopNetworkInterpolation();
            entry.networkView.ClearPendingSnapshots();
            entry.networkView.enabled = false;
        }

        if (entry.gameObject != null)
        {
            entry.gameObject.SetActive(false);
            Destroy(entry.gameObject);
        }
    }
}
