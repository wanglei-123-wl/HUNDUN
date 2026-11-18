using System;
using UnityEngine;

namespace Game.Cultivation.Protocol
{
    public static class RefiningRequestSequence
    {
        [Serializable]
        private sealed class PendingRequest
        {
            public long characterDbId;
            public string sessionId;
            public long requestSequence;
            public string clientRequestId;
            public long configurationRevision;
            public long expectedInventoryRevision;
            public long stackId;
            public int count;
            public long lastSentAtUnixMilliseconds;
            public bool conflicted;
        }

        private const string PendingKeyPrefix = "refining_start_in_flight_";
        private static readonly object Sync = new object();
        private static long characterDbId;
        private static string sessionId;
        private static long nextSequence = 1;
        private static PendingRequest pending;
        private static bool recoveryPending;

        public static bool HasPending
        {
            get { lock (Sync) return pending != null; }
        }

        public static bool HasConflict
        {
            get { lock (Sync) return pending != null && pending.conflicted; }
        }

        public static void EnterWorld(string serverSessionId, long characterId)
        {
            lock (Sync)
            {
                if (characterDbId == characterId
                    && string.Equals(sessionId, serverSessionId, StringComparison.Ordinal))
                    return;

                characterDbId = characterId > 0 ? characterId : 0;
                sessionId = Guid.TryParseExact(serverSessionId, "D", out _)
                    ? serverSessionId : null;
                nextSequence = 1;
                pending = null;
                recoveryPending = false;
                if (characterDbId <= 0)
                    return;

                string saved = PlayerPrefs.GetString(
                    PendingKeyPrefix + characterDbId, string.Empty);
                if (!string.IsNullOrEmpty(saved))
                {
                    try
                    {
                        pending = JsonUtility.FromJson<PendingRequest>(saved);
                        if (pending == null || pending.characterDbId != characterDbId
                            || pending.requestSequence <= 0
                            || !Guid.TryParseExact(pending.clientRequestId, "D", out _)
                            || !Guid.TryParseExact(pending.sessionId, "D", out _)
                            || pending.configurationRevision <= 0
                            || pending.expectedInventoryRevision < 0
                            || pending.stackId <= 0 || pending.count <= 0)
                            throw new FormatException("Invalid saved request.");
                    }
                    catch (Exception exception)
                    {
                        pending = new PendingRequest { characterDbId = characterDbId };
                        Debug.LogError("[Refining] Saved request is unreadable; "
                            + "new operations are blocked: " + exception.Message);
                    }
                }

                if (pending == null)
                    return;
                if (sessionId != null && !string.IsNullOrEmpty(pending.clientRequestId)
                    && !string.Equals(pending.sessionId, sessionId, StringComparison.Ordinal))
                {
                    pending.sessionId = sessionId;
                    pending.requestSequence = nextSequence++;
                    pending.lastSentAtUnixMilliseconds = 0;
                    SavePending();
                }
                else if (pending.requestSequence >= nextSequence
                    && pending.requestSequence < long.MaxValue)
                    nextSequence = pending.requestSequence + 1;

                recoveryPending = sessionId != null && !pending.conflicted
                    && !string.IsNullOrEmpty(pending.clientRequestId);
            }
        }

        public static void Suspend()
        {
            lock (Sync)
            {
                sessionId = null;
                recoveryPending = false;
            }
        }

        public static bool TryNextQuery(long activeCharacterId,
            string activeSessionId, string queryType, out RefiningQueryRequestDto request)
        {
            request = null;
            lock (Sync)
            {
                if (!IsCurrent(activeCharacterId, activeSessionId)
                    || (queryType != RefiningMessageIds.Catalog
                        && queryType != RefiningMessageIds.Snapshot)
                    || nextSequence <= 0 || nextSequence == long.MaxValue)
                    return false;
                request = new RefiningQueryRequestDto(queryType, nextSequence++);
                return true;
            }
        }

        public static bool TryPrepareStart(long activeCharacterId,
            string activeSessionId, long configurationRevision,
            long expectedInventoryRevision, long stackId, int count,
            out RefiningStartRequestDto request)
        {
            request = null;
            lock (Sync)
            {
                if (!IsCurrent(activeCharacterId, activeSessionId)
                    || pending != null || configurationRevision <= 0
                    || expectedInventoryRevision < 0 || stackId <= 0
                    || count <= 0 || nextSequence <= 0
                    || nextSequence == long.MaxValue)
                    return false;
                request = new RefiningStartRequestDto(sessionId, nextSequence++,
                    Guid.NewGuid().ToString("D"), configurationRevision,
                    expectedInventoryRevision, stackId, count);
                pending = new PendingRequest
                {
                    characterDbId = characterDbId,
                    sessionId = sessionId,
                    requestSequence = request.requestSequence,
                    clientRequestId = request.clientRequestId,
                    configurationRevision = configurationRevision,
                    expectedInventoryRevision = expectedInventoryRevision,
                    stackId = stackId,
                    count = count,
                    lastSentAtUnixMilliseconds =
                        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };
                SavePending();
                return true;
            }
        }

        public static bool Matches(long activeCharacterId, string resultSessionId,
            long sequence, string requestId)
        {
            lock (Sync)
                return IsCurrent(activeCharacterId, resultSessionId)
                    && pending != null
                    && pending.characterDbId == activeCharacterId
                    && pending.requestSequence == sequence && sequence > 0
                    && string.Equals(pending.clientRequestId, requestId,
                        StringComparison.Ordinal);
        }

        public static bool TryGetRecoveryRequest(out RefiningStartRequestDto request)
        {
            request = null;
            lock (Sync)
            {
                if (!recoveryPending || pending == null || sessionId == null
                    || pending.conflicted
                    || !string.Equals(pending.sessionId, sessionId,
                        StringComparison.Ordinal)
                    || !Guid.TryParseExact(pending.clientRequestId, "D", out _))
                    return false;
                request = CreateRequest();
                return true;
            }
        }

        public static bool TryGetSameSessionRetry(long nowUnixMilliseconds,
            long timeoutMilliseconds, out RefiningStartRequestDto request)
        {
            request = null;
            lock (Sync)
            {
                if (pending == null || sessionId == null || recoveryPending
                    || pending.conflicted
                    || !string.Equals(pending.sessionId, sessionId,
                        StringComparison.Ordinal)
                    || timeoutMilliseconds <= 0
                    || pending.lastSentAtUnixMilliseconds <= 0
                    || nowUnixMilliseconds - pending.lastSentAtUnixMilliseconds
                        < timeoutMilliseconds)
                    return false;
                request = CreateRequest();
                return true;
            }
        }

        public static void MarkSent(long nowUnixMilliseconds)
        {
            lock (Sync)
            {
                if (pending == null || sessionId == null
                    || !string.Equals(pending.sessionId, sessionId,
                        StringComparison.Ordinal))
                    return;
                recoveryPending = false;
                pending.lastSentAtUnixMilliseconds = nowUnixMilliseconds;
                SavePending();
            }
        }

        public static void MarkConflict()
        {
            lock (Sync)
            {
                if (pending == null || sessionId == null
                    || !string.Equals(pending.sessionId, sessionId,
                        StringComparison.Ordinal))
                    return;
                pending.conflicted = true;
                recoveryPending = false;
                SavePending();
            }
        }

        public static bool Complete(long activeCharacterId, string resultSessionId,
            long sequence, string requestId)
        {
            lock (Sync)
            {
                if (!Matches(activeCharacterId, resultSessionId, sequence, requestId))
                    return false;
                pending = null;
                recoveryPending = false;
                PlayerPrefs.DeleteKey(PendingKeyPrefix + characterDbId);
                PlayerPrefs.Save();
                return true;
            }
        }

        private static bool IsCurrent(long activeCharacterId, string activeSessionId)
        {
            return characterDbId > 0 && characterDbId == activeCharacterId
                && sessionId != null
                && string.Equals(sessionId, activeSessionId,
                    StringComparison.Ordinal);
        }

        private static RefiningStartRequestDto CreateRequest()
        {
            return new RefiningStartRequestDto(pending.sessionId,
                pending.requestSequence, pending.clientRequestId,
                pending.configurationRevision, pending.expectedInventoryRevision,
                pending.stackId, pending.count);
        }

        private static void SavePending()
        {
            PlayerPrefs.SetString(PendingKeyPrefix + characterDbId,
                JsonUtility.ToJson(pending));
            PlayerPrefs.Save();
        }
    }
}
