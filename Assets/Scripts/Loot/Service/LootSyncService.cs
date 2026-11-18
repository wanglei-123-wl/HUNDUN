using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class LootSyncService
{
    private readonly LootStateStore stateStore;
    private readonly LootServerTimeService serverTimeService;

    public string CurrentSceneId { get; private set; }

    public event Action<string, IReadOnlyCollection<LootState>> SceneSnapshotApplied;
    public event Action<LootState> LootUpsertApplied;
    public event Action<string, LootRemoveReason> LootRemoved;

    public LootSyncService(
        LootStateStore stateStore,
        LootServerTimeService serverTimeService)
    {
        this.stateStore = stateStore
            ?? throw new ArgumentNullException(nameof(stateStore));
        this.serverTimeService = serverTimeService
            ?? throw new ArgumentNullException(nameof(serverTimeService));
    }

    public void SetCurrentScene(string sceneId)
    {
        CurrentSceneId = sceneId;
    }

    public LootSyncResult ApplySnapshot(LootSnapshotMessageDto message)
    {
        if (message == null || string.IsNullOrWhiteSpace(message.sceneId))
        {
            return LootSyncResult.IgnoredInvalidData;
        }

        serverTimeService.Synchronize(message.serverTime);

        if (!IsCurrentScene(message.sceneId))
        {
            return LootSyncResult.IgnoredScene;
        }

        List<LootState> snapshot = new List<LootState>();
        if (message.loots != null)
        {
            for (int index = 0; index < message.loots.Length; index++)
            {
                LootDataDto dto = message.loots[index];
                if (TryCreateState(dto, message.sceneId, 0, out LootState state))
                {
                    snapshot.Add(state);
                }
            }
        }

        stateStore.ReplaceSceneSnapshot(message.sceneId, snapshot);
        SceneSnapshotApplied?.Invoke(message.sceneId, stateStore.GetByScene(message.sceneId));
        return LootSyncResult.Applied;
    }

    public LootSyncResult ApplyUpsert(LootUpsertMessageDto message)
    {
        if (message == null || message.loot == null)
        {
            return LootSyncResult.IgnoredInvalidData;
        }

        serverTimeService.Synchronize(message.serverTime);

        string sceneId = string.IsNullOrWhiteSpace(message.sceneId)
            ? message.loot.sceneId
            : message.sceneId;

        if (!IsCurrentScene(sceneId))
        {
            return LootSyncResult.IgnoredScene;
        }

        long aoiRevision = message.aoiRevision != 0
            ? message.aoiRevision
            : message.loot.aoiRevision;

        if (!TryCreateState(message.loot, sceneId, aoiRevision, out LootState state))
        {
            return LootSyncResult.IgnoredInvalidData;
        }

        if (!stateStore.Upsert(state))
        {
            return LootSyncResult.IgnoredOldRevision;
        }

        LootUpsertApplied?.Invoke(state);
        return LootSyncResult.Applied;
    }

    public LootSyncResult ApplyRemove(LootRemoveMessageDto message)
    {
        if (message == null || string.IsNullOrWhiteSpace(message.lootId))
        {
            return LootSyncResult.IgnoredInvalidData;
        }

        serverTimeService.Synchronize(message.serverTime);

        if (!IsCurrentScene(message.sceneId))
        {
            return LootSyncResult.IgnoredScene;
        }

        if (stateStore.TryGetLatestRevision(message.lootId, out long latestRevision)
            && latestRevision > message.aoiRevision)
        {
            return LootSyncResult.IgnoredOldRevision;
        }

        bool removed = stateStore.Remove(message.lootId, message.aoiRevision);
        LootRemoveReason reason = ParseRemoveReason(message.reason);
        LootRemoved?.Invoke(message.lootId, reason);
        return removed ? LootSyncResult.Applied : LootSyncResult.IgnoredNotFound;
    }

    public void ClearCurrentScene()
    {
        if (!string.IsNullOrWhiteSpace(CurrentSceneId))
        {
            stateStore.ClearScene(CurrentSceneId);
        }

        CurrentSceneId = null;
    }

    private bool IsCurrentScene(string sceneId)
    {
        return !string.IsNullOrWhiteSpace(CurrentSceneId)
            && string.Equals(CurrentSceneId, sceneId, StringComparison.Ordinal);
    }

    private static bool TryCreateState(
        LootDataDto dto,
        string fallbackSceneId,
        long overrideAoiRevision,
        out LootState state)
    {
        state = null;

        if (dto == null || string.IsNullOrWhiteSpace(dto.lootId))
        {
            return false;
        }

        string sceneId = string.IsNullOrWhiteSpace(dto.sceneId)
            ? fallbackSceneId
            : dto.sceneId;

        if (string.IsNullOrWhiteSpace(sceneId))
        {
            return false;
        }

        long aoiRevision = overrideAoiRevision != 0
            ? overrideAoiRevision
            : dto.aoiRevision;

        state = new LootState(
            dto.lootId,
            sceneId,
            dto.itemId,
            dto.count,
            new Vector3(dto.x, dto.y, dto.z),
            dto.ownerType,
            dto.ownerId,
            dto.ownerExpireAtMs,
            dto.expireAtMs,
            aoiRevision,
            dto.state);

        return true;
    }

    private static LootRemoveReason ParseRemoveReason(string reason)
    {
        switch (reason)
        {
            case LootProtocolConstants.RemoveReasons.OutOfView:
                return LootRemoveReason.OutOfView;
            case LootProtocolConstants.RemoveReasons.Picked:
                return LootRemoveReason.Picked;
            case LootProtocolConstants.RemoveReasons.Expired:
                return LootRemoveReason.Expired;
            case LootProtocolConstants.RemoveReasons.ClaimedByOther:
                return LootRemoveReason.ClaimedByOther;
            case LootProtocolConstants.RemoveReasons.GrantRejected:
                return LootRemoveReason.GrantRejected;
            default:
                Debug.LogWarning("Unknown loot remove reason: " + reason);
                return LootRemoveReason.Unknown;
        }
    }
}
