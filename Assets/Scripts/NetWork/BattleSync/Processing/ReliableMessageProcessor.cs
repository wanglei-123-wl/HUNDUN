using System;
using System.Collections.Generic;

namespace Game.Client.BattleSync
{
    public readonly struct ReliableMessageContext
    {
        public long ConnectionEpoch { get; }
        public string SceneId { get; }
        public string MessageId { get; }
        public long ReliableMessageId { get; }
        public string MessageIdentity { get; }
        public long ReceivedAtMs { get; }
        public long ServerTime { get; }
        public long ServerTick { get; }

        public ReliableMessageContext(
            long connectionEpoch,
            string sceneId,
            string messageId,
            long reliableMessageId,
            string messageIdentity,
            long receivedAtMs,
            long serverTime,
            long serverTick)
        {
            ConnectionEpoch = connectionEpoch;
            SceneId = sceneId ?? string.Empty;
            MessageId = messageId ?? string.Empty;
            ReliableMessageId = reliableMessageId;
            MessageIdentity = messageIdentity ?? string.Empty;
            ReceivedAtMs = receivedAtMs;
            ServerTime = serverTime;
            ServerTick = serverTick;
        }
    }

    public enum ReliableMessageProcessStatus
    {
        AppliedAckSent = 0,
        AppliedAckQueued = 1,
        StaleAckSent = 2,
        StaleAckQueued = 3,
        DuplicateAckSent = 4,
        DuplicateAckQueued = 5,
        BusinessRetryableFailure = 6,
        BusinessInvalidMessage = 7,
        BusinessWrongScene = 8,
        InvalidContext = 9,
        ProcessorNotActive = 10,
        SessionMismatch = 11,
        SceneMismatch = 12,
        MessageIdentityConflict = 13,
        DeduplicationCapacityExceeded = 14,
        AckRejected = 15,
        BusinessException = 16
    }

    public sealed class ReliableMessageProcessResult
    {
        public ReliableMessageProcessStatus Status { get; }
        public long ReliableMessageId { get; }
        public string MessageId { get; }
        public bool BusinessInvoked { get; }
        public BattleSyncApplyResult BusinessResult { get; }
        public ReliableAckRequestStatus AckStatus { get; }
        public bool RequiresSnapshotRepair { get; }
        public string RepairReason { get; }
        public string Detail { get; }

        public bool IsBusinessConfirmed =>
            BusinessResult == BattleSyncApplyResult.Applied
            || BusinessResult == BattleSyncApplyResult.AlreadyApplied
            || BusinessResult == BattleSyncApplyResult.IgnoredStale;

        public ReliableMessageProcessResult(
            ReliableMessageProcessStatus status,
            long reliableMessageId,
            string messageId,
            bool businessInvoked,
            BattleSyncApplyResult businessResult,
            ReliableAckRequestStatus ackStatus,
            bool requiresSnapshotRepair,
            string repairReason,
            string detail)
        {
            Status = status;
            ReliableMessageId = reliableMessageId;
            MessageId = messageId ?? string.Empty;
            BusinessInvoked = businessInvoked;
            BusinessResult = businessResult;
            AckStatus = ackStatus;
            RequiresSnapshotRepair = requiresSnapshotRepair;
            RepairReason = repairReason ?? string.Empty;
            Detail = detail ?? string.Empty;
        }
    }

    public sealed class ReliableMessageRecord
    {
        public long ReliableMessageId { get; }
        public string MessageId { get; }
        public string MessageIdentity { get; }
        public string SceneId { get; }
        public long ConnectionEpoch { get; }
        public BattleSyncApplyResult ApplyResult { get; }
        public long FirstProcessedAtMs { get; }
        public long LastReceivedAtMs { get; internal set; }
        public int DuplicateCount { get; internal set; }

        internal ReliableMessageRecord(
            long reliableMessageId,
            string messageId,
            string messageIdentity,
            string sceneId,
            long connectionEpoch,
            BattleSyncApplyResult applyResult,
            long firstProcessedAtMs)
        {
            ReliableMessageId = reliableMessageId;
            MessageId = messageId ?? string.Empty;
            MessageIdentity = messageIdentity ?? string.Empty;
            SceneId = sceneId ?? string.Empty;
            ConnectionEpoch = connectionEpoch;
            ApplyResult = applyResult;
            FirstProcessedAtMs = firstProcessedAtMs;
            LastReceivedAtMs = firstProcessedAtMs;
            DuplicateCount = 0;
        }
    }

    public sealed class ReliableMessageProcessor
    {
        private const int DefaultRecordCapacity = 8192;

        private readonly int recordCapacity;
        private readonly ReliableAckService ackService;
        private readonly Dictionary<long, ReliableMessageRecord> recordsByReliableId =
            new Dictionary<long, ReliableMessageRecord>();
        private readonly Queue<long> recordOrder = new Queue<long>();

        private bool isActive;
        private long activeConnectionEpoch;
        private string activeSceneId = string.Empty;

        public bool IsActive => isActive;
        public long ActiveConnectionEpoch => activeConnectionEpoch;
        public string ActiveSceneId => activeSceneId;
        public int ProcessedRecordCount => recordsByReliableId.Count;

        public ReliableMessageProcessor(
            ReliableAckService ackService,
            int recordCapacity = DefaultRecordCapacity)
        {
            this.ackService = ackService
                ?? throw new ArgumentNullException(nameof(ackService));

            if (recordCapacity < 128)
            {
                throw new ArgumentOutOfRangeException(nameof(recordCapacity));
            }

            this.recordCapacity = recordCapacity;
        }

        public void BeginSession(long connectionEpoch, string sceneId)
        {
            if (connectionEpoch <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(connectionEpoch));
            }

            if (string.IsNullOrWhiteSpace(sceneId))
            {
                throw new ArgumentException("Scene ID is required.", nameof(sceneId));
            }

            recordsByReliableId.Clear();
            recordOrder.Clear();
            activeConnectionEpoch = connectionEpoch;
            activeSceneId = sceneId;
            isActive = true;
        }

        public bool EndSession(long connectionEpoch)
        {
            if (!isActive || connectionEpoch != activeConnectionEpoch)
            {
                return false;
            }

            recordsByReliableId.Clear();
            recordOrder.Clear();
            activeConnectionEpoch = 0;
            activeSceneId = string.Empty;
            isActive = false;
            return true;
        }

        public ReliableMessageProcessResult Process(
            ReliableMessageContext context,
            Func<BattleSyncApplyResult> applyBusiness)
        {
            if (!isActive)
            {
                return CreateResult(
                    ReliableMessageProcessStatus.ProcessorNotActive,
                    context,
                    false,
                    BattleSyncApplyResult.InvalidMessage,
                    ReliableAckRequestStatus.ServiceNotActive,
                    false,
                    string.Empty,
                    "Reliable message processor is not active.");
            }

            if (!IsValid(context) || applyBusiness == null)
            {
                return CreateResult(
                    ReliableMessageProcessStatus.InvalidContext,
                    context,
                    false,
                    BattleSyncApplyResult.InvalidMessage,
                    ReliableAckRequestStatus.InvalidRequest,
                    false,
                    string.Empty,
                    "Reliable message context or business delegate is invalid.");
            }

            if (context.ConnectionEpoch != activeConnectionEpoch)
            {
                return CreateResult(
                    ReliableMessageProcessStatus.SessionMismatch,
                    context,
                    false,
                    BattleSyncApplyResult.InvalidMessage,
                    ReliableAckRequestStatus.SessionMismatch,
                    false,
                    string.Empty,
                    "Reliable message belongs to another connection epoch.");
            }

            if (!string.Equals(context.SceneId, activeSceneId, StringComparison.Ordinal))
            {
                return CreateResult(
                    ReliableMessageProcessStatus.SceneMismatch,
                    context,
                    false,
                    BattleSyncApplyResult.IgnoredWrongScene,
                    ReliableAckRequestStatus.SceneMismatch,
                    false,
                    string.Empty,
                    "Reliable message belongs to another scene.");
            }

            if (recordsByReliableId.TryGetValue(
                    context.ReliableMessageId,
                    out ReliableMessageRecord record))
            {
                return ProcessDuplicate(context, record);
            }

            if (!EnsureRecordCapacity())
            {
                return CreateResult(
                    ReliableMessageProcessStatus.DeduplicationCapacityExceeded,
                    context,
                    false,
                    BattleSyncApplyResult.RetryableFailure,
                    ReliableAckRequestStatus.QueueFull,
                    false,
                    string.Empty,
                    "Reliable message record capacity is exhausted.");
            }

            BattleSyncApplyResult applyResult;
            try
            {
                applyResult = applyBusiness();
            }
            catch (Exception exception)
            {
                return CreateResult(
                    ReliableMessageProcessStatus.BusinessException,
                    context,
                    true,
                    BattleSyncApplyResult.RetryableFailure,
                    ReliableAckRequestStatus.InvalidRequest,
                    false,
                    string.Empty,
                    exception.Message);
            }

            if (applyResult == BattleSyncApplyResult.RetryableFailure)
            {
                return CreateBusinessFailure(
                    ReliableMessageProcessStatus.BusinessRetryableFailure,
                    context,
                    applyResult,
                    "Business requested a retry.");
            }

            if (applyResult == BattleSyncApplyResult.InvalidMessage)
            {
                return CreateBusinessFailure(
                    ReliableMessageProcessStatus.BusinessInvalidMessage,
                    context,
                    applyResult,
                    "Business rejected the reliable message as invalid.");
            }

            if (applyResult == BattleSyncApplyResult.IgnoredWrongScene)
            {
                return CreateBusinessFailure(
                    ReliableMessageProcessStatus.BusinessWrongScene,
                    context,
                    applyResult,
                    "Business rejected the reliable message scene.");
            }

            ReliableMessageRecord newRecord = new ReliableMessageRecord(
                context.ReliableMessageId,
                context.MessageId,
                context.MessageIdentity,
                context.SceneId,
                context.ConnectionEpoch,
                applyResult,
                context.ReceivedAtMs);

            recordsByReliableId.Add(context.ReliableMessageId, newRecord);
            recordOrder.Enqueue(context.ReliableMessageId);
            return RequestAck(context, applyResult, true, false);
        }

        private ReliableMessageProcessResult ProcessDuplicate(
            ReliableMessageContext context,
            ReliableMessageRecord record)
        {
            bool identityMatches =
                record.ConnectionEpoch == context.ConnectionEpoch
                && string.Equals(record.SceneId, context.SceneId, StringComparison.Ordinal)
                && string.Equals(record.MessageId, context.MessageId, StringComparison.Ordinal)
                && string.Equals(record.MessageIdentity, context.MessageIdentity, StringComparison.Ordinal);

            if (!identityMatches)
            {
                return CreateResult(
                    ReliableMessageProcessStatus.MessageIdentityConflict,
                    context,
                    false,
                    BattleSyncApplyResult.InvalidMessage,
                    ReliableAckRequestStatus.InvalidRequest,
                    true,
                    BattleSyncProtocolConstants.RepairReasons.SnapshotMismatch,
                    "Reliable message ID was reused with a different identity.");
            }

            record.DuplicateCount++;
            record.LastReceivedAtMs = context.ReceivedAtMs;
            return RequestAck(context, record.ApplyResult, false, true);
        }

        private ReliableMessageProcessResult RequestAck(
            ReliableMessageContext context,
            BattleSyncApplyResult applyResult,
            bool businessInvoked,
            bool isDuplicateDelivery)
        {
            ReliableAckRequest request = new ReliableAckRequest(
                context.ConnectionEpoch,
                context.SceneId,
                context.ReliableMessageId,
                context.MessageId,
                context.MessageIdentity,
                context.ReceivedAtMs,
                isDuplicateDelivery);

            ReliableAckRequestResult ackResult = ackService.RequestAck(request);
            BattleSyncDiagnosticsLogger.LogAckRequest(request, ackResult);
            ReliableMessageProcessStatus status;

            if (ackResult.Status == ReliableAckRequestStatus.SentImmediately
                || ackResult.Status == ReliableAckRequestStatus.ExistingPendingRetrySucceeded)
            {
                status = isDuplicateDelivery
                    ? ReliableMessageProcessStatus.DuplicateAckSent
                    : applyResult == BattleSyncApplyResult.IgnoredStale
                        ? ReliableMessageProcessStatus.StaleAckSent
                        : ReliableMessageProcessStatus.AppliedAckSent;
            }
            else if (ackResult.Status == ReliableAckRequestStatus.QueuedForRetry
                || ackResult.Status == ReliableAckRequestStatus.ExistingPendingRetryFailed)
            {
                status = isDuplicateDelivery
                    ? ReliableMessageProcessStatus.DuplicateAckQueued
                    : applyResult == BattleSyncApplyResult.IgnoredStale
                        ? ReliableMessageProcessStatus.StaleAckQueued
                        : ReliableMessageProcessStatus.AppliedAckQueued;
            }
            else
            {
                status = ReliableMessageProcessStatus.AckRejected;
            }

            return CreateResult(
                status,
                context,
                businessInvoked,
                applyResult,
                ackResult.Status,
                false,
                string.Empty,
                ackResult.Detail);
        }

        private bool EnsureRecordCapacity()
        {
            if (recordsByReliableId.Count < recordCapacity)
            {
                return true;
            }

            int candidates = recordOrder.Count;
            for (int index = 0; index < candidates; index++)
            {
                long reliableMessageId = recordOrder.Dequeue();
                if (!recordsByReliableId.ContainsKey(reliableMessageId))
                {
                    continue;
                }

                if (ackService.IsAckPending(reliableMessageId))
                {
                    recordOrder.Enqueue(reliableMessageId);
                    continue;
                }

                recordsByReliableId.Remove(reliableMessageId);
                return true;
            }

            return recordsByReliableId.Count < recordCapacity;
        }

        private static bool IsValid(ReliableMessageContext context)
        {
            return context.ConnectionEpoch > 0
                && !string.IsNullOrWhiteSpace(context.SceneId)
                && !string.IsNullOrWhiteSpace(context.MessageId)
                && context.ReliableMessageId > 0
                && !string.IsNullOrWhiteSpace(context.MessageIdentity)
                && context.MessageIdentity.Length <= 512
                && context.ReceivedAtMs >= 0
                && context.ServerTime >= 0
                && context.ServerTick >= 0;
        }

        private static ReliableMessageProcessResult CreateBusinessFailure(
            ReliableMessageProcessStatus status,
            ReliableMessageContext context,
            BattleSyncApplyResult applyResult,
            string detail)
        {
            return CreateResult(
                status,
                context,
                true,
                applyResult,
                ReliableAckRequestStatus.InvalidRequest,
                false,
                string.Empty,
                detail);
        }

        private static ReliableMessageProcessResult CreateResult(
            ReliableMessageProcessStatus status,
            ReliableMessageContext context,
            bool businessInvoked,
            BattleSyncApplyResult businessResult,
            ReliableAckRequestStatus ackStatus,
            bool requiresSnapshotRepair,
            string repairReason,
            string detail)
        {
            return new ReliableMessageProcessResult(
                status,
                context.ReliableMessageId,
                context.MessageId,
                businessInvoked,
                businessResult,
                ackStatus,
                requiresSnapshotRepair,
                repairReason,
                detail);
        }
    }
}
