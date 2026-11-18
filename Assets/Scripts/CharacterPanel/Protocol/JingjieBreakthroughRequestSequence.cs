using System;
using UnityEngine;

namespace Game.CharacterPanel.Protocol
{
    public static class JingjieBreakthroughRequestSequence
    {
        [Serializable]
        private sealed class PendingRequest
        {
            public long characterDbId;
            public string sessionId;
            public long requestSequence;
            public string clientRequestId;
            public long lastSentAtUnixMilliseconds;
        }

        private const string PendingKeyPrefix = "jingjie_breakthrough_in_flight_";
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

        public static void EnterWorld(string serverSessionId, long characterId)
        {
            lock (Sync)
            {
                if (characterDbId == characterId
                    && string.Equals(sessionId, serverSessionId,
                        StringComparison.Ordinal))
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
                        if (pending == null
                            || pending.characterDbId != characterDbId
                            || pending.requestSequence <= 0
                            || !Guid.TryParseExact(pending.clientRequestId, "D", out _)
                            || !Guid.TryParseExact(pending.sessionId, "D", out _))
                            throw new FormatException("Invalid saved request.");
                    }
                    catch (Exception exception)
                    {
                        pending = new PendingRequest { characterDbId = characterDbId };
                        Debug.LogError("[Jingjie] Saved breakthrough request is "
                            + "unreadable; another write is blocked: "
                            + exception.Message);
                    }
                }

                if (pending != null)
                {
                    if (sessionId != null && !string.Equals(pending.sessionId,
                        sessionId, StringComparison.Ordinal))
                    {
                        pending.sessionId = sessionId;
                        pending.requestSequence = nextSequence++;
                        pending.lastSentAtUnixMilliseconds = 0;
                        SavePending();
                    }
                    else if (pending.requestSequence < long.MaxValue)
                        nextSequence = pending.requestSequence + 1;
                    recoveryPending = sessionId != null
                        && !string.IsNullOrWhiteSpace(pending.clientRequestId);
                    Debug.LogWarning("[Jingjie] Previous breakthrough outcome "
                        + "is unresolved. Do not start another operation.");
                }
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

        public static bool TryGetRecoveryRequest(out JingjieBreakthroughRequestDto request)
        {
            request = null;
            lock (Sync)
            {
                if (!recoveryPending || pending == null || sessionId == null
                    || !string.Equals(pending.sessionId, sessionId,
                        StringComparison.Ordinal)
                    || pending.requestSequence <= 0
                    || !Guid.TryParseExact(pending.clientRequestId, "D", out _))
                    return false;
                request = new JingjieBreakthroughRequestDto(sessionId,
                    pending.requestSequence, pending.clientRequestId);
                return true;
            }
        }

        public static void MarkRecoverySent(long nowUnixMilliseconds)
        {
            lock (Sync)
            {
                if (pending == null || sessionId == null || !recoveryPending)
                    return;
                recoveryPending = false;
                pending.lastSentAtUnixMilliseconds = nowUnixMilliseconds;
                SavePending();
            }
        }

        public static bool Matches(long activeCharacterDbId, string resultSessionId,
            long requestSequence, string clientRequestId)
        {
            lock (Sync)
                return pending != null && sessionId != null
                    && characterDbId == activeCharacterDbId
                    && pending.characterDbId == activeCharacterDbId
                    && requestSequence > 0
                    && pending.requestSequence == requestSequence
                    && string.Equals(sessionId, resultSessionId,
                        StringComparison.Ordinal)
                    && string.Equals(pending.sessionId, resultSessionId,
                        StringComparison.Ordinal)
                    && string.Equals(pending.clientRequestId, clientRequestId,
                        StringComparison.Ordinal);
        }

        public static bool TryPrepare(out JingjieBreakthroughRequestDto request)
        {
            request = null;
            lock (Sync)
            {
                if (characterDbId <= 0 || sessionId == null
                    || pending != null || nextSequence <= 0
                    || nextSequence == long.MaxValue)
                    return false;

                request = new JingjieBreakthroughRequestDto(sessionId,
                    nextSequence++, Guid.NewGuid().ToString("D"));
                pending = new PendingRequest
                {
                    characterDbId = characterDbId,
                    sessionId = sessionId,
                    requestSequence = request.requestSequence,
                    clientRequestId = request.clientRequestId,
                    lastSentAtUnixMilliseconds =
                        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };
                SavePending();
                return true;
            }
        }

        public static bool IsTimedOut(long nowUnixMilliseconds,
            long timeoutMilliseconds)
        {
            lock (Sync)
                return pending != null && sessionId != null && !recoveryPending
                    && string.Equals(sessionId, pending.sessionId,
                        StringComparison.Ordinal)
                    && timeoutMilliseconds > 0
                    && pending.lastSentAtUnixMilliseconds > 0
                    && nowUnixMilliseconds - pending.lastSentAtUnixMilliseconds
                        >= timeoutMilliseconds;
        }

        public static bool TryGetSameSessionRetry(long nowUnixMilliseconds,
            long timeoutMilliseconds, out string requestJson)
        {
            requestJson = null;
            lock (Sync)
            {
                if (pending == null || sessionId == null
                    || recoveryPending
                    || !string.Equals(sessionId, pending.sessionId,
                        StringComparison.Ordinal)
                    || !IsTimedOut(nowUnixMilliseconds, timeoutMilliseconds))
                    return false;

                requestJson = JsonUtility.ToJson(
                    new JingjieBreakthroughRequestDto(pending.sessionId,
                        pending.requestSequence, pending.clientRequestId));
                return true;
            }
        }

        public static void MarkRetrySent(long nowUnixMilliseconds)
        {
            lock (Sync)
            {
                if (pending == null || sessionId == null
                    || !string.Equals(sessionId, pending.sessionId,
                        StringComparison.Ordinal))
                    return;
                pending.lastSentAtUnixMilliseconds = nowUnixMilliseconds;
                SavePending();
            }
        }

        // Call only after the server result is matched and fully reconciled.
        public static bool Complete(string resultSessionId, long requestSequence,
            string clientRequestId)
        {
            lock (Sync)
            {
                if (pending == null || sessionId == null
                    || !string.Equals(resultSessionId, sessionId,
                        StringComparison.Ordinal)
                    || pending.requestSequence != requestSequence
                    || !string.Equals(pending.sessionId, sessionId,
                        StringComparison.Ordinal)
                    || !string.Equals(pending.clientRequestId, clientRequestId,
                        StringComparison.Ordinal))
                    return false;
                pending = null;
                recoveryPending = false;
                PlayerPrefs.DeleteKey(PendingKeyPrefix + characterDbId);
                PlayerPrefs.Save();
                return true;
            }
        }

        private static void SavePending()
        {
            PlayerPrefs.SetString(PendingKeyPrefix + characterDbId,
                JsonUtility.ToJson(pending));
            PlayerPrefs.Save();
        }
    }
}
