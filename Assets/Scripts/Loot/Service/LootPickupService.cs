using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class LootPickupService
{
    private const int CanonicalLootIdLength = 36;
    private const int MaxRetryCount = 1;
    private const int RememberedGrantCount = 512;
    private const int RememberedFinalSequenceCount = 512;

    private readonly LootStateStore stateStore;
    private readonly LootNetworkSender networkSender;
    private readonly WorldLootViewManager viewManager;
    private readonly LootSyncService syncService;
    private readonly LootSystemSettings settings;
    private readonly Func<long> getClientTick;
    private readonly Func<float> getLocalTime;
    private readonly LootPickupSessionState session;
    private Dictionary<long, LootPickupRequestState> requestsBySequence => session.RequestsBySequence;
    private Dictionary<string, long> pendingSequenceByLootId => session.PendingSequenceByLootId;
    private HashSet<string> completedGrants => session.CompletedGrants;
    private Queue<string> completedGrantOrder => session.CompletedGrantOrder;
    private HashSet<long> completedFinalSequences => session.CompletedFinalSequences;
    private Queue<long> completedFinalSequenceOrder => session.CompletedFinalSequenceOrder;

    public event Action<LootPickupResultMessageDto> PickupPending;
    public event Action<LootPickupResultMessageDto> PickupSucceeded;
    public event Action<LootPickupResultMessageDto> PickupFailed;
    public event Action<LootPickupResultMessageDto> PickupResultRejected;
    public event Action<LootPickupRequestState> PickupTimedOut;
    public event Action InventoryReconciliationRequested;

    public LootPickupService(
        LootStateStore stateStore,
        LootNetworkSender networkSender,
        WorldLootViewManager viewManager,
        LootSyncService syncService,
        LootSystemSettings settings,
        Func<long> getClientTick)
        : this(stateStore, networkSender, viewManager, syncService, settings, getClientTick, () => Time.realtimeSinceStartup)
    {
    }

    public LootPickupService(
        LootStateStore stateStore,
        LootNetworkSender networkSender,
        WorldLootViewManager viewManager,
        LootSyncService syncService,
        LootSystemSettings settings,
        Func<long> getClientTick,
        Func<float> getLocalTime,
        LootPickupSessionState session = null)
    {
        this.stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        this.networkSender = networkSender ?? throw new ArgumentNullException(nameof(networkSender));
        this.viewManager = viewManager ?? throw new ArgumentNullException(nameof(viewManager));
        this.syncService = syncService ?? throw new ArgumentNullException(nameof(syncService));
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.getClientTick = getClientTick ?? throw new ArgumentNullException(nameof(getClientTick));
        this.getLocalTime = getLocalTime ?? throw new ArgumentNullException(nameof(getLocalTime));
        this.session = session ?? new LootPickupSessionState();
    }

    public long NextRequestSequence => session.LastConsumedSequence == long.MaxValue
        ? long.MaxValue : session.LastConsumedSequence + 1;
    public bool PickupSuspended => session.PickupSuspended;
    public int PendingCount => requestsBySequence.Count;

    public LootPickupRequestResult RequestPickup(string lootId)
    {
        if (session.PickupSuspended)
        {
            return LootPickupRequestResult.Suspended;
        }

        if (requestsBySequence.Count > 0 || pendingSequenceByLootId.ContainsKey(lootId))
        {
            return LootPickupRequestResult.AlreadyPending;
        }

        if (!stateStore.TryGet(lootId, out LootState loot))
        {
            return LootPickupRequestResult.LootNotFound;
        }

        if (!string.Equals(loot.SceneId, syncService.CurrentSceneId, StringComparison.Ordinal))
        {
            return LootPickupRequestResult.WrongScene;
        }

        if (!IsCanonicalLowercaseUuid(lootId))
        {
            return LootPickupRequestResult.InvalidLootId;
        }

        long clientTick = getClientTick();
        if (clientTick <= 0)
        {
            return LootPickupRequestResult.InvalidClientTick;
        }

        if (NextRequestSequence == long.MaxValue)
        {
            session.PickupSuspended = true;
            return LootPickupRequestResult.SequenceExhausted;
        }

        long requestSequence = NextRequestSequence;
        if (!networkSender.SendPickup(requestSequence, clientTick, lootId))
        {
            return LootPickupRequestResult.NetworkSendFailed;
        }

        LootPickupRequestState requestState = new LootPickupRequestState
        {
            RequestSequence = requestSequence,
            LootId = lootId,
            SendClientTick = clientTick,
            SendLocalTime = getLocalTime(),
            Pending = true,
            RetryCount = 0,
            Phase = LootPickupRequestPhase.RequestSent
        };

        requestsBySequence.Add(requestSequence, requestState);
        pendingSequenceByLootId.Add(lootId, requestSequence);
        viewManager.SetPickupPending(lootId, true);
        return LootPickupRequestResult.Sent;
    }

    public bool HandlePickupResult(LootPickupResultMessageDto message)
    {
        if (message == null || string.IsNullOrWhiteSpace(message.lootId))
        {
            PickupResultRejected?.Invoke(message);
            return false;
        }

        string status = message.status;
        if ((status == LootProtocolConstants.PickupStatuses.Succeeded
                && (!message.success || string.IsNullOrWhiteSpace(message.grantId)))
            || (status == LootProtocolConstants.PickupStatuses.Pending
                && message.success)
            || (status == LootProtocolConstants.PickupStatuses.Rejected
                && message.success)
            || (status != LootProtocolConstants.PickupStatuses.Pending
                && status != LootProtocolConstants.PickupStatuses.Succeeded
                && status != LootProtocolConstants.PickupStatuses.Rejected))
        {
            PickupResultRejected?.Invoke(message);
            return false;
        }
        if (status != LootProtocolConstants.PickupStatuses.Pending
            && completedFinalSequences.Contains(message.requestSequence))
            return true;
        if (!TryFindRequest(message, out LootPickupRequestState requestState))
        {
            PickupResultRejected?.Invoke(message);
            return false;
        }

        if (status == LootProtocolConstants.PickupStatuses.Succeeded
            && completedGrants.Contains(message.grantId))
        {
            PickupResultRejected?.Invoke(message);
            return false;
        }

        if (status == LootProtocolConstants.PickupStatuses.Pending)
        {
            session.ConfirmConsumed(requestState.RequestSequence);
            if (requestState.Phase == LootPickupRequestPhase.RequestSent)
            {
                requestState.Phase = LootPickupRequestPhase.Pending;
                requestState.RetryCount = 0;
                requestState.SendLocalTime = getLocalTime();
                PickupPending?.Invoke(message);
            }
            return true;
        }

        bool unconsumed = status == LootProtocolConstants.PickupStatuses.Rejected
            && IsUnconsumedResult(message.errorCode);
        if (unconsumed)
            session.PickupSuspended = true;
        else
            session.ConfirmConsumed(requestState.RequestSequence);
        RememberFinalSequence(requestState.RequestSequence);
        CompleteRequest(requestState);
        if (status == LootProtocolConstants.PickupStatuses.Succeeded)
        {
            RememberGrant(message.grantId);
            PickupSucceeded?.Invoke(message);
            return true;
        }

        if (status == LootProtocolConstants.PickupStatuses.Rejected)
        {
            PickupFailed?.Invoke(message);
            return true;
        }

        PickupResultRejected?.Invoke(message);
        return false;
    }

    public void HandleProtocolError(NetErrorMessageDto message)
    {
        if (message == null
            || !string.Equals(message.requestMsgId, LootProtocolConstants.Pickup, StringComparison.Ordinal)
            || requestsBySequence.Count == 0)
        {
            return;
        }

        LootPickupRequestState requestState = GetOnlyPendingRequest();
        if (requestState == null)
        {
            return;
        }

        // TCP errors do not confirm that the pickup sequence was consumed.
        session.PickupSuspended = true;
        CompleteRequest(requestState);
        PickupFailed?.Invoke(new LootPickupResultMessageDto
        {
            requestSequence = requestState.RequestSequence,
            success = false,
            status = LootProtocolConstants.PickupStatuses.Rejected,
            lootId = requestState.LootId,
            errorCode = message.errorCode,
            message = message.message,
            serverTime = message.serverTime
        });
    }

    public bool IsPending(string lootId)
    {
        return !string.IsNullOrWhiteSpace(lootId) && pendingSequenceByLootId.ContainsKey(lootId);
    }

    public bool TryGetPending(string lootId, out LootPickupRequestState requestState)
    {
        requestState = null;
        return !string.IsNullOrWhiteSpace(lootId)
            && pendingSequenceByLootId.TryGetValue(lootId, out long requestSequence)
            && requestsBySequence.TryGetValue(requestSequence, out requestState);
    }

    public void UpdateTimeouts()
    {
        float timeout = Mathf.Max(0f, settings.PickupRequestTimeout);
        if (timeout <= 0f || requestsBySequence.Count == 0)
        {
            return;
        }

        float localTime = getLocalTime();
        LootPickupRequestState requestState = GetOnlyPendingRequest();
        if (requestState == null
            || requestState.Phase == LootPickupRequestPhase.Reconciling
            || localTime - requestState.SendLocalTime < timeout)
        {
            return;
        }

        if ((requestState.Phase == LootPickupRequestPhase.RequestSent
                || requestState.Phase == LootPickupRequestPhase.Pending)
            && requestState.RetryCount < MaxRetryCount)
        {
            if (networkSender.SendPickup(
                requestState.RequestSequence, requestState.SendClientTick, requestState.LootId))
            {
                requestState.RetryCount++;
                requestState.SendLocalTime = localTime;
                return;
            }
        }

        requestState.Phase = LootPickupRequestPhase.Reconciling;
        PickupTimedOut?.Invoke(requestState);
        InventoryReconciliationRequested?.Invoke();
    }

    public void ResolveReconcilingRequests()
    {
        List<LootPickupRequestState> requests = new List<LootPickupRequestState>(requestsBySequence.Values);
        for (int index = 0; index < requests.Count; index++)
        {
            if (requests[index].Phase == LootPickupRequestPhase.Reconciling)
            {
                session.PickupSuspended = true;
                CompleteRequest(requests[index]);
            }
        }
    }

    public void SetPickupSuspended(bool suspended)
    {
        session.PickupSuspended = suspended;
    }

    public void ResetConnectionState()
    {
        ClearPendingRequests();
        session.Reset();
    }

    public void ClearPendingRequests()
    {
        foreach (LootPickupRequestState requestState in requestsBySequence.Values)
        {
            requestState.Pending = false;
            viewManager.SetPickupPending(requestState.LootId, false);
        }

        requestsBySequence.Clear();
        pendingSequenceByLootId.Clear();
    }

    private bool TryFindRequest(LootPickupResultMessageDto message, out LootPickupRequestState requestState)
    {
        requestState = null;
        return message.requestSequence > 0
            && requestsBySequence.TryGetValue(message.requestSequence, out requestState)
            && string.Equals(requestState.LootId, message.lootId, StringComparison.Ordinal);
    }

    private void RememberGrant(string grantId)
    {
        if (!completedGrants.Add(grantId)) return;
        completedGrantOrder.Enqueue(grantId);
        if (completedGrantOrder.Count > RememberedGrantCount)
            completedGrants.Remove(completedGrantOrder.Dequeue());
    }

    private void RememberFinalSequence(long sequence)
    {
        if (!completedFinalSequences.Add(sequence)) return;
        completedFinalSequenceOrder.Enqueue(sequence);
        if (completedFinalSequenceOrder.Count > RememberedFinalSequenceCount)
            completedFinalSequences.Remove(completedFinalSequenceOrder.Dequeue());
    }

    private LootPickupRequestState GetOnlyPendingRequest()
    {
        foreach (LootPickupRequestState requestState in requestsBySequence.Values)
        {
            return requestState;
        }

        return null;
    }

    private void CompleteRequest(LootPickupRequestState requestState)
    {
        requestState.Pending = false;
        requestsBySequence.Remove(requestState.RequestSequence);
        pendingSequenceByLootId.Remove(requestState.LootId);
        viewManager.SetPickupPending(requestState.LootId, false);
    }

    private static bool IsUnconsumedResult(string errorCode)
    {
        return errorCode == LootProtocolConstants.ErrorCodes.PickupSequenceGap
            || errorCode == LootProtocolConstants.ErrorCodes.PickupDuplicateOrStale
            || errorCode == LootProtocolConstants.ErrorCodes.PlayerNotInWorld
            || errorCode == LootProtocolConstants.ErrorCodes.InvalidLootPickup;
    }

    private static bool IsCanonicalLowercaseUuid(string lootId)
    {
        return !string.IsNullOrWhiteSpace(lootId)
            && lootId.Length == CanonicalLootIdLength
            && string.Equals(lootId, lootId.ToLowerInvariant(), StringComparison.Ordinal)
            && Guid.TryParseExact(lootId, "D", out Guid parsed)
            && parsed.ToString("D") == lootId
            && lootId[14] == '7';
    }
}
