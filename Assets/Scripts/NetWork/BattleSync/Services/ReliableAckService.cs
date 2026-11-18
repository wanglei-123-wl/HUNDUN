using System;
using System.Collections.Generic;
using Game.Client.BattleSync.Protocol;

namespace Game.Client.BattleSync
{
    public readonly struct ReliableAckRequest
    {
        public long ConnectionEpoch { get; }
        public string SceneId { get; }
        public long ReliableMessageId { get; }
        public string SourceMessageId { get; }
        public string MessageIdentity { get; }
        public long RequestedAtMs { get; }
        public bool IsDuplicateDelivery { get; }

        public ReliableAckRequest(
            long connectionEpoch,
            string sceneId,
            long reliableMessageId,
            string sourceMessageId,
            string messageIdentity,
            long requestedAtMs,
            bool isDuplicateDelivery)
        {
            ConnectionEpoch = connectionEpoch;
            SceneId = sceneId ?? string.Empty;
            ReliableMessageId = reliableMessageId;
            SourceMessageId = sourceMessageId ?? string.Empty;
            MessageIdentity = messageIdentity ?? string.Empty;
            RequestedAtMs = requestedAtMs;
            IsDuplicateDelivery = isDuplicateDelivery;
        }
    }

    public enum ReliableAckRequestStatus
    {
        SentImmediately = 0,
        QueuedForRetry = 1,
        ExistingPendingRetrySucceeded = 2,
        ExistingPendingRetryFailed = 3,
        InvalidRequest = 4,
        ServiceNotActive = 5,
        SessionMismatch = 6,
        SceneMismatch = 7,
        TransportUnavailable = 8,
        SerializationFailed = 9,
        QueueFull = 10
    }

    public sealed class ReliableAckRequestResult
    {
        public ReliableAckRequestStatus Status { get; }
        public long ReliableMessageId { get; }
        public bool WasSent { get; }
        public bool IsPending { get; }
        public string Detail { get; }

        public ReliableAckRequestResult(
            ReliableAckRequestStatus status,
            long reliableMessageId,
            bool wasSent,
            bool isPending,
            string detail)
        {
            Status = status;
            ReliableMessageId = reliableMessageId;
            WasSent = wasSent;
            IsPending = isPending;
            Detail = detail ?? string.Empty;
        }
    }

    public sealed class PendingReliableAck
    {
        public long ConnectionEpoch { get; }
        public string SceneId { get; }
        public long ReliableMessageId { get; }
        public string SourceMessageId { get; }
        public string MessageIdentity { get; }
        public long FirstQueuedAtMs { get; }
        public long LastRequestedAtMs { get; internal set; }
        public long LastAttemptAtMs { get; internal set; }
        public long NextAttemptAtMs { get; internal set; }
        public int AttemptCount { get; internal set; }
        public string LastFailure { get; internal set; }

        internal PendingReliableAck(ReliableAckRequest request)
        {
            ConnectionEpoch = request.ConnectionEpoch;
            SceneId = request.SceneId;
            ReliableMessageId = request.ReliableMessageId;
            SourceMessageId = request.SourceMessageId;
            MessageIdentity = request.MessageIdentity;
            FirstQueuedAtMs = request.RequestedAtMs;
            LastRequestedAtMs = request.RequestedAtMs;
            LastAttemptAtMs = 0;
            NextAttemptAtMs = request.RequestedAtMs;
            AttemptCount = 0;
            LastFailure = string.Empty;
        }
    }

    public sealed class ReliableAckService
    {
        private const int DefaultQueueCapacity = 2048;
        private const long DefaultInitialRetryDelayMs = 250;
        private const long DefaultMaximumRetryDelayMs = 2000;

        private readonly IBattleSyncTransport transport;
        private readonly IBattleSyncJsonCodec jsonCodec;
        private readonly int queueCapacity;
        private readonly long initialRetryDelayMs;
        private readonly long maximumRetryDelayMs;
        private readonly Dictionary<long, PendingReliableAck> pendingByReliableId =
            new Dictionary<long, PendingReliableAck>();

        private bool isActive;
        private long activeConnectionEpoch;
        private string activeSceneId = string.Empty;

        public bool IsActive => isActive;
        public int PendingCount => pendingByReliableId.Count;

        public ReliableAckService(
            IBattleSyncTransport transport,
            IBattleSyncJsonCodec jsonCodec,
            int queueCapacity = DefaultQueueCapacity,
            long initialRetryDelayMs = DefaultInitialRetryDelayMs,
            long maximumRetryDelayMs = DefaultMaximumRetryDelayMs)
        {
            this.transport = transport
                ?? throw new ArgumentNullException(nameof(transport));
            this.jsonCodec = jsonCodec
                ?? throw new ArgumentNullException(nameof(jsonCodec));

            if (queueCapacity < 32)
            {
                throw new ArgumentOutOfRangeException(nameof(queueCapacity));
            }

            if (initialRetryDelayMs < 50)
            {
                throw new ArgumentOutOfRangeException(nameof(initialRetryDelayMs));
            }

            if (maximumRetryDelayMs < initialRetryDelayMs)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumRetryDelayMs));
            }

            this.queueCapacity = queueCapacity;
            this.initialRetryDelayMs = initialRetryDelayMs;
            this.maximumRetryDelayMs = maximumRetryDelayMs;
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

            pendingByReliableId.Clear();
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

            pendingByReliableId.Clear();
            activeConnectionEpoch = 0;
            activeSceneId = string.Empty;
            isActive = false;
            return true;
        }

        public ReliableAckRequestResult RequestAck(ReliableAckRequest request)
        {
            if (!IsValid(request))
            {
                return CreateResult(
                    ReliableAckRequestStatus.InvalidRequest,
                    request.ReliableMessageId,
                    false,
                    false,
                    "ACK request is invalid.");
            }

            if (!isActive)
            {
                return CreateResult(
                    ReliableAckRequestStatus.ServiceNotActive,
                    request.ReliableMessageId,
                    false,
                    false,
                    "ACK service is not active.");
            }

            if (request.ConnectionEpoch != activeConnectionEpoch)
            {
                return CreateResult(
                    ReliableAckRequestStatus.SessionMismatch,
                    request.ReliableMessageId,
                    false,
                    false,
                    "ACK request belongs to another connection epoch.");
            }

            if (!string.Equals(request.SceneId, activeSceneId, StringComparison.Ordinal))
            {
                return CreateResult(
                    ReliableAckRequestStatus.SceneMismatch,
                    request.ReliableMessageId,
                    false,
                    false,
                    "ACK request belongs to another scene.");
            }

            if (pendingByReliableId.TryGetValue(
                    request.ReliableMessageId,
                    out PendingReliableAck existing))
            {
                return HandleExistingPending(request, existing);
            }

            PendingReliableAck pending = new PendingReliableAck(request);
            bool sent = TrySendAck(
                pending,
                request.RequestedAtMs,
                out AckSendFailure failure,
                out string error);

            if (sent)
            {
                return CreateResult(
                    ReliableAckRequestStatus.SentImmediately,
                    request.ReliableMessageId,
                    true,
                    false,
                    "ACK sent immediately.");
            }

            if (failure == AckSendFailure.Serialization)
            {
                return CreateResult(
                    ReliableAckRequestStatus.SerializationFailed,
                    request.ReliableMessageId,
                    false,
                    false,
                    error);
            }

            if (pendingByReliableId.Count >= queueCapacity)
            {
                return CreateResult(
                    ReliableAckRequestStatus.QueueFull,
                    request.ReliableMessageId,
                    false,
                    false,
                    "Pending ACK queue is full. " + error);
            }

            pendingByReliableId.Add(request.ReliableMessageId, pending);
            return CreateResult(
                ReliableAckRequestStatus.QueuedForRetry,
                request.ReliableMessageId,
                false,
                true,
                error);
        }

        public void Tick(long nowMs)
        {
            if (!isActive || nowMs < 0 || pendingByReliableId.Count == 0
                || !transport.IsConnected)
            {
                return;
            }

            List<long> dueReliableIds = new List<long>();
            foreach (KeyValuePair<long, PendingReliableAck> pair in pendingByReliableId)
            {
                PendingReliableAck pending = pair.Value;
                if (pending.ConnectionEpoch == activeConnectionEpoch
                    && string.Equals(pending.SceneId, activeSceneId, StringComparison.Ordinal)
                    && nowMs >= pending.NextAttemptAtMs)
                {
                    dueReliableIds.Add(pair.Key);
                }
            }

            foreach (long reliableMessageId in dueReliableIds)
            {
                if (!pendingByReliableId.TryGetValue(
                        reliableMessageId,
                        out PendingReliableAck pending))
                {
                    continue;
                }

                if (TrySendAck(pending, nowMs, out _, out _))
                {
                    pendingByReliableId.Remove(reliableMessageId);
                }
            }
        }

        public bool IsAckPending(long reliableMessageId)
        {
            return reliableMessageId > 0
                && pendingByReliableId.ContainsKey(reliableMessageId);
        }

        public bool TryGetPending(
            long reliableMessageId,
            out PendingReliableAck pending)
        {
            return pendingByReliableId.TryGetValue(reliableMessageId, out pending);
        }

        private ReliableAckRequestResult HandleExistingPending(
            ReliableAckRequest request,
            PendingReliableAck pending)
        {
            if (pending.ConnectionEpoch != request.ConnectionEpoch
                || !string.Equals(pending.SceneId, request.SceneId, StringComparison.Ordinal)
                || !string.Equals(pending.SourceMessageId, request.SourceMessageId, StringComparison.Ordinal)
                || !string.Equals(pending.MessageIdentity, request.MessageIdentity, StringComparison.Ordinal))
            {
                return CreateResult(
                    ReliableAckRequestStatus.InvalidRequest,
                    request.ReliableMessageId,
                    false,
                    true,
                    "Pending ACK identity conflicts with the request.");
            }

            pending.LastRequestedAtMs = request.RequestedAtMs;
            if (!request.IsDuplicateDelivery)
            {
                return CreateResult(
                    ReliableAckRequestStatus.ExistingPendingRetryFailed,
                    request.ReliableMessageId,
                    false,
                    true,
                    "ACK is already pending.");
            }

            if (TrySendAck(pending, request.RequestedAtMs, out _, out string error))
            {
                pendingByReliableId.Remove(request.ReliableMessageId);
                return CreateResult(
                    ReliableAckRequestStatus.ExistingPendingRetrySucceeded,
                    request.ReliableMessageId,
                    true,
                    false,
                    "Pending ACK sent after duplicate delivery.");
            }

            return CreateResult(
                ReliableAckRequestStatus.ExistingPendingRetryFailed,
                request.ReliableMessageId,
                false,
                true,
                error);
        }

        private bool TrySendAck(
            PendingReliableAck pending,
            long nowMs,
            out AckSendFailure failure,
            out string error)
        {
            pending.AttemptCount++;
            pending.LastAttemptAtMs = nowMs;

            if (!transport.IsConnected
                || transport.ConnectionEpoch != pending.ConnectionEpoch
                || pending.ConnectionEpoch != activeConnectionEpoch)
            {
                failure = AckSendFailure.Transport;
                error = "Transport is unavailable for the ACK connection epoch.";
                ScheduleRetry(pending, nowMs, error);
                BattleSyncDiagnosticsLogger.LogAckAttempt(
                    pending,
                    false,
                    "transport_unavailable",
                    error);
                return false;
            }

            BattleOutboxAckDto ack = new BattleOutboxAckDto
            {
                reliableMessageId = pending.ReliableMessageId
            };

            if (!jsonCodec.TrySerialize(ack, out string json, out error))
            {
                failure = AckSendFailure.Serialization;
                error = string.IsNullOrWhiteSpace(error)
                    ? "ACK serialization failed."
                    : error;
                ScheduleRetry(pending, nowMs, error);
                BattleSyncDiagnosticsLogger.LogAckAttempt(
                    pending,
                    false,
                    "serialization_failed",
                    error);
                return false;
            }

            if (!transport.SendJson(json))
            {
                failure = AckSendFailure.Transport;
                error = "Transport rejected the ACK payload.";
                ScheduleRetry(pending, nowMs, error);
                BattleSyncDiagnosticsLogger.LogAckAttempt(
                    pending,
                    false,
                    "transport_rejected",
                    error);
                return false;
            }

            failure = AckSendFailure.None;
            error = string.Empty;
            pending.LastFailure = string.Empty;
            BattleSyncDiagnosticsLogger.LogAckAttempt(
                pending,
                true,
                pending.AttemptCount == 1 ? "sent" : "retry_sent",
                string.Empty);
            return true;
        }

        private void ScheduleRetry(
            PendingReliableAck pending,
            long nowMs,
            string error)
        {
            pending.LastFailure = error ?? string.Empty;
            pending.NextAttemptAtMs =
                nowMs + CalculateRetryDelayMs(pending.AttemptCount);
        }

        private long CalculateRetryDelayMs(int attemptCount)
        {
            long delay = initialRetryDelayMs;
            int remainingDoublings = Math.Max(0, attemptCount - 1);

            while (remainingDoublings > 0 && delay < maximumRetryDelayMs)
            {
                if (delay > maximumRetryDelayMs / 2)
                {
                    return maximumRetryDelayMs;
                }

                delay *= 2;
                remainingDoublings--;
            }

            return Math.Min(delay, maximumRetryDelayMs);
        }

        private static bool IsValid(ReliableAckRequest request)
        {
            return request.ConnectionEpoch > 0
                && !string.IsNullOrWhiteSpace(request.SceneId)
                && request.ReliableMessageId > 0
                && !string.IsNullOrWhiteSpace(request.SourceMessageId)
                && !string.IsNullOrWhiteSpace(request.MessageIdentity)
                && request.MessageIdentity.Length <= 512
                && request.RequestedAtMs >= 0;
        }

        private static ReliableAckRequestResult CreateResult(
            ReliableAckRequestStatus status,
            long reliableMessageId,
            bool wasSent,
            bool isPending,
            string detail)
        {
            return new ReliableAckRequestResult(
                status,
                reliableMessageId,
                wasSent,
                isPending,
                detail);
        }

        private enum AckSendFailure
        {
            None = 0,
            Transport = 1,
            Serialization = 2
        }
    }
}
