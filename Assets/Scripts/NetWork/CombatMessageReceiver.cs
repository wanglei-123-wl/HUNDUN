using System;
using System.Collections.Generic;
using Game.Client.BattleSync;
using Game.Client.BattleSync.Protocol;
using UnityEngine;

public class CombatMessageReceiver : MonoBehaviour, ICombatBattleSyncModule, IBattleSyncSnapshotRoutingControl
{
    private const string CombatSnapshotMessageId = "combat_snapshot";
    private const string CombatStateUpsertMessageId = "combat_state_upsert";
    private const string CombatEventMessageId = "combat_event";

    [SerializeField] private CombatStateStore stateStore;
    [SerializeField, Min(32)] private int eventDeduplicationCapacity = 2048;

    private readonly HashSet<string> seenEventKeys = new HashSet<string>();
    private readonly Queue<string> seenEventOrder = new Queue<string>();
    private NetMessageDispatcher registeredDispatcher;
    private BattleSyncSnapshotRoutingMode snapshotRoutingMode = BattleSyncSnapshotRoutingMode.LegacySelfRegistered;
    private long activeConnectionEpoch;
    private string activeSceneId;

    public event Action<CombatEventMessage> CombatEventApplied;
    internal event Action<CombatEventMessage> MonsterHitConfirmed;
    public event Action<string,string> CombatRevisionGapDetected;

    private void Awake()
    {
        ResolveStateStore();
    }

    private void OnEnable()
    {
        EnsureMessageRegistration();
    }

    private void Update()
    {
        EnsureMessageRegistration();
    }

    private void OnDisable()
    {
        RemoveMessageRegistration();
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

        if (snapshotRoutingMode == BattleSyncSnapshotRoutingMode.LegacySelfRegistered)
        {
            currentDispatcher.Register(CombatSnapshotMessageId, HandleCombatSnapshot);
            currentDispatcher.Register(CombatStateUpsertMessageId, HandleCombatStateUpsert);
            currentDispatcher.Register(CombatEventMessageId, HandleCombatEvent);
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
        activeConnectionEpoch=context.ConnectionEpoch; activeSceneId=context.SceneId; ClearSeenEvents();
        ResolveStateStore();
        stateStore?.BeginScene(context.SceneId);
        return BattleSyncApplyResult.Applied;
    }

    public void EndSession(long connectionEpoch)
    {
        if (connectionEpoch != activeConnectionEpoch) return;
        activeConnectionEpoch=0; activeSceneId=string.Empty; ClearSeenEvents();
        ResolveStateStore();
        stateStore?.Clear();
    }

    public BattleSyncApplyResult ApplyCombatSnapshot(CombatSnapshotMessageDto message)
    {
        if (message == null) return BattleSyncApplyResult.InvalidMessage;
        if (!string.Equals(activeSceneId,message.sceneId,StringComparison.Ordinal)) return BattleSyncApplyResult.IgnoredWrongScene;
        ResolveStateStore(); if (stateStore == null) return BattleSyncApplyResult.RetryableFailure;
        CombatStateDto[] source=message.states??Array.Empty<CombatStateDto>();
        CombatStateMessage[] values=new CombatStateMessage[source.Length];
        for(int i=0;i<source.Length;i++){var s=source[i];values[i]=new CombatStateMessage{entity=new CombatEntityMessage{entityType=s.entity.entityType,entityId=s.entity.entityId},hp=s.hp,maxHp=s.maxHp,lifeState=s.lifeState,combatRevision=s.combatRevision,spawnGeneration=s.spawnGeneration};}
        return stateStore.TryReplaceSnapshot(message.sceneId,values,message.serverTick)
            ? BattleSyncApplyResult.Applied : BattleSyncApplyResult.InvalidMessage;
    }

    public BattleSyncApplyResult ApplyCombatEvent(CombatEventMessageDto message)
    {
        if(message==null)return BattleSyncApplyResult.InvalidMessage;
        if(!string.Equals(activeSceneId,message.sceneId,StringComparison.Ordinal))return BattleSyncApplyResult.IgnoredWrongScene;
        ResolveStateStore();if(stateStore==null)return BattleSyncApplyResult.RetryableFailure;
        if(IsEventSeen(message.sceneId,message.eventId))return BattleSyncApplyResult.AlreadyApplied;
        CombatEntityDto monsterEntity = string.Equals(message.eventType,"attack",StringComparison.Ordinal)
            ? message.source : message.target;
        if (monsterEntity != null && monsterEntity.entityType == "monster")
        {
            if (!stateStore.TryGetState("monster", monsterEntity.entityId, out CombatStateData monster))
            {
                CombatRevisionGapDetected?.Invoke("monster", monsterEntity.entityId);
                return string.Equals(message.eventType,"attack",StringComparison.Ordinal)
                    ? BattleSyncApplyResult.IgnoredStale : BattleSyncApplyResult.RetryableFailure;
            }
            if (monster.GenerationServerTick > 0 && message.serverTick > 0
                && message.serverTick < monster.GenerationServerTick)
            {
                MarkEventSeen(message.sceneId,message.eventId);
                return BattleSyncApplyResult.IgnoredStale;
            }
        }
        if(string.Equals(message.eventType,"attack",StringComparison.Ordinal))
        {
            CombatEventApplied?.Invoke(ToCombatEventMessage(message));
            MarkEventSeen(message.sceneId,message.eventId);
            return BattleSyncApplyResult.Applied;
        }
        if(message.target==null)return BattleSyncApplyResult.InvalidMessage;
        stateStore.TryGetState(message.target.entityType,message.target.entityId,out CombatStateData current);
        if(current!=null&&message.combatRevision<=current.CombatRevision)
        {
            MarkEventSeen(message.sceneId,message.eventId);
            if(ShouldPublishStaleLifeCycleEvent(message,current))
                CombatEventApplied?.Invoke(ToCombatEventMessage(message));
            // A state update can carry this HP revision before the damage event arrives.
            if (current.Hp > 0 && IsMonsterHit(message.eventType, message.damage, message.lethal, message.hpAfter,
                    message.source?.entityType, message.target.entityType))
                MonsterHitConfirmed?.Invoke(ToCombatEventMessage(message));
            return BattleSyncApplyResult.IgnoredStale;
        }
        bool revisionGap=current!=null&&message.combatRevision>current.CombatRevision
            &&message.combatRevision-current.CombatRevision>1;
        bool isDeath = string.Equals(message.eventType, "death", StringComparison.OrdinalIgnoreCase);
        int hpAfter = isDeath ? 0 : message.hpAfter;
        string lifeState = isDeath || message.lethal || hpAfter <= 0 ? "Dead" : "Alive";
        CombatEntityMessage target=new CombatEntityMessage{entityType=message.target.entityType,entityId=message.target.entityId};
        if(!stateStore.MergeEventHealth(message.sceneId,target,hpAfter,message.maxHp,lifeState,
            message.combatRevision,message.serverTick))return BattleSyncApplyResult.RetryableFailure;
        MarkEventSeen(message.sceneId,message.eventId);
        if(revisionGap)CombatRevisionGapDetected?.Invoke(message.target.entityType,message.target.entityId);
        CombatEventMessage appliedEvent = ToCombatEventMessage(message);
        CombatEventApplied?.Invoke(appliedEvent);
        NotifyMonsterHit(appliedEvent);
        return BattleSyncApplyResult.Applied;
    }

    private void NotifyMonsterHit(CombatEventMessage message)
    {
        if (IsMonsterHit(message.eventType, message.damage, message.lethal, message.hpAfter,
                message.source?.entityType, message.target?.entityType))
            MonsterHitConfirmed?.Invoke(message);
    }

    private static bool IsMonsterHit(string eventType, int damage, bool lethal, int hpAfter,
        string sourceType, string targetType) =>
        eventType == "damage" && damage > 0 && !lethal && hpAfter > 0
        && sourceType == "player" && targetType == "monster";

    private static bool IsLifeCycleEvent(string eventType)
    {
        return string.Equals(eventType,"death",StringComparison.OrdinalIgnoreCase)
            ||string.Equals(eventType,"respawn",StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldPublishStaleLifeCycleEvent(
        CombatEventMessageDto message,CombatStateData current)
    {
        if(message==null||current==null||!IsLifeCycleEvent(message.eventType))return false;
        bool currentIsDead=current.Hp<=0
            ||string.Equals(current.LifeState,"Dead",StringComparison.OrdinalIgnoreCase);
        if(string.Equals(message.eventType,"death",StringComparison.OrdinalIgnoreCase))
            return currentIsDead&&current.CombatRevision==message.combatRevision;
        return !currentIsDead&&current.CombatRevision>=message.combatRevision;
    }

    private static CombatEventMessage ToCombatEventMessage(CombatEventMessageDto message)
    {
        CombatEntityMessage source=message.source==null?null:new CombatEntityMessage{
            entityType=message.source.entityType,entityId=message.source.entityId};
        CombatEntityMessage target=message.target==null?null:new CombatEntityMessage{
            entityType=message.target.entityType,entityId=message.target.entityId};
        return new CombatEventMessage{sceneId=message.sceneId,eventId=message.eventId,
            eventType=message.eventType,actionId=message.actionId,
            actionSequence=message.actionSequence,source=source,target=target,damage=message.damage,
            critical=message.critical,hpAfter=message.hpAfter,maxHp=message.maxHp,lethal=message.lethal,
            killerType=message.killerType,killerId=message.killerId,killerName=message.killerName,
            respawnAvailableAt=message.respawnAvailableAt,deathKickAt=message.deathKickAt,
            combatRevision=message.combatRevision,serverTick=message.serverTick,serverTime=message.serverTime};
    }

    private void RemoveMessageRegistration()
    {
        if (registeredDispatcher == null)
        {
            return;
        }

        registeredDispatcher.Unregister(CombatSnapshotMessageId, HandleCombatSnapshot);
        registeredDispatcher.Unregister(CombatStateUpsertMessageId, HandleCombatStateUpsert);
        registeredDispatcher.Unregister(CombatEventMessageId, HandleCombatEvent);
        registeredDispatcher = null;
    }

    private void HandleCombatSnapshot(string json)
    {
        CombatSnapshotMessage message = ParseMessage<CombatSnapshotMessage>(json);
        if (message == null || string.IsNullOrEmpty(message.sceneId))
        {
            return;
        }

        ResolveStateStore();
        if (stateStore == null)
        {
            return;
        }

        ClearSeenEvents();
        stateStore.ReplaceSnapshot(message.sceneId, message.states);
    }

    private void HandleCombatStateUpsert(string json)
    {
        CombatStateUpsertMessage message = ParseMessage<CombatStateUpsertMessage>(json);
        if (message == null || message.states == null)
        {
            return;
        }

        ResolveStateStore();
        if (stateStore == null)
        {
            return;
        }

        for (int index = 0; index < message.states.Length; index++)
        {
            stateStore.MergeState(message.sceneId, message.states[index]);
        }
    }

    private void HandleCombatEvent(string json)
    {
        CombatEventMessage message = ParseMessage<CombatEventMessage>(json);
        if (message == null)
        {
            return;
        }

        ResolveStateStore();
        if (stateStore == null)
        {
            return;
        }

        string eventSceneId = string.IsNullOrEmpty(message.sceneId)
            ? stateStore.ActiveSceneId
            : message.sceneId;

        if (!TryMarkEventSeen(eventSceneId, message.eventId))
        {
            return;
        }

        message.sceneId = eventSceneId;

        if (string.Equals(message.eventType, "attack", StringComparison.Ordinal))
        {
            CombatEventApplied?.Invoke(message);
            return;
        }

        CombatEntityMessage affectedEntity;
        string lifeState;

        switch (message.eventType)
        {
            case "damage":
                affectedEntity = message.target;
                lifeState = message.lethal || message.hpAfter <= 0 ? "Dead" : "Alive";
                break;
            case "death":
                affectedEntity = message.target;
                lifeState = "Dead";
                break;
            case "respawn":
                affectedEntity = IsValidEntity(message.target) ? message.target : message.source;
                lifeState = "Alive";
                break;
            default:
                return;
        }

        bool applied = stateStore.MergeEventHealth(
            eventSceneId,
            affectedEntity,
            message.hpAfter,
            message.maxHp,
            lifeState,
            message.combatRevision);

        if (applied)
        {
            CombatEventApplied?.Invoke(message);
        }
        if (string.Equals(eventSceneId, stateStore.ActiveSceneId, StringComparison.Ordinal))
            NotifyMonsterHit(message);
    }

    private bool TryMarkEventSeen(string sceneId, long eventId)
    {
        if (string.IsNullOrEmpty(sceneId))
        {
            return false;
        }

        if (eventId <= 0)
        {
            return true;
        }

        if (IsEventSeen(sceneId, eventId))
        {
            return false;
        }

        MarkEventSeen(sceneId, eventId);
        return true;
    }

    private bool IsEventSeen(string sceneId, long eventId)
    {
        if (string.IsNullOrEmpty(sceneId) || eventId <= 0)
        {
            return false;
        }

        return seenEventKeys.Contains(sceneId + "\u001f" + eventId);
    }

    private void MarkEventSeen(string sceneId, long eventId)
    {
        if (string.IsNullOrEmpty(sceneId) || eventId <= 0)
        {
            return;
        }

        string eventKey = sceneId + "\u001f" + eventId;
        if (!seenEventKeys.Add(eventKey))
        {
            return;
        }

        seenEventOrder.Enqueue(eventKey);
        int capacity = Mathf.Max(32, eventDeduplicationCapacity);
        while (seenEventOrder.Count > capacity)
        {
            seenEventKeys.Remove(seenEventOrder.Dequeue());
        }
    }

    private void ClearSeenEvents()
    {
        seenEventKeys.Clear();
        seenEventOrder.Clear();
    }

    private void ResolveStateStore()
    {
        if (stateStore == null)
        {
            stateStore = GetComponent<CombatStateStore>();
        }

        if (stateStore == null)
        {
            stateStore = FindObjectOfType<CombatStateStore>();
        }

        if (stateStore == null)
        {
            Debug.LogError("CombatMessageReceiver requires a CombatStateStore.", this);
        }
    }

    private static bool IsValidEntity(CombatEntityMessage entity)
    {
        return entity != null
            && !string.IsNullOrEmpty(entity.entityType)
            && !string.IsNullOrEmpty(entity.entityId);
    }

    private static T ParseMessage<T>(string json) where T : class
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        try
        {
            return JsonUtility.FromJson<T>(json);
        }
        catch (Exception exception)
        {
            Debug.LogWarning(typeof(T).Name + " parse failed: " + exception.Message);
            return null;
        }
    }
}
