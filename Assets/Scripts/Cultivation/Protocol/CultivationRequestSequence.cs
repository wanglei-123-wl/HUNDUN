using System;
using UnityEngine;

namespace Game.Cultivation.Protocol
{
    public static class CultivationRequestSequence
    {
        [Serializable]
        private sealed class PendingRequest
        {
            public long characterDbId;
            public string sessionId;
            public long requestSequence;
            public string clientRequestId;
            public string requestType;
            public long configurationRevision;
            public long expectedInventoryRevision;
            public long stackId;
            public string techniqueId;
            public bool conflicted;
        }

        private const string PendingKeyPrefix = "cultivation_write_in_flight_";
        private static readonly object Sync = new object();
        private static long characterDbId;
        private static string sessionId;
        private static long nextSequence = 1;
        private static PendingRequest pending;

        public static bool HasPending { get { lock (Sync) return pending != null; } }
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
                            || !Guid.TryParseExact(pending.sessionId, "D", out _)
                            || !Guid.TryParseExact(pending.clientRequestId, "D", out _)
                            || pending.configurationRevision <= 0
                            || pending.expectedInventoryRevision < 0
                            || (pending.requestType == CultivationMessageIds.Learn
                                ? pending.stackId <= 0 || !string.IsNullOrEmpty(pending.techniqueId)
                                : pending.requestType != CultivationMessageIds.Start
                                    || string.IsNullOrWhiteSpace(pending.techniqueId)
                                    || pending.stackId != 0))
                            throw new FormatException("Invalid saved cultivation request.");
                    }
                    catch (Exception exception)
                    {
                        pending = new PendingRequest
                        {
                            characterDbId = characterDbId,
                            conflicted = true
                        };
                        Debug.LogError("[Cultivation] Saved request is unreadable; "
                            + "new writes are blocked: " + exception.Message);
                    }
                }
                if (pending == null)
                    return;
                if (sessionId != null && !pending.conflicted
                    && !string.Equals(pending.sessionId, sessionId,
                        StringComparison.Ordinal))
                {
                    pending.sessionId = sessionId;
                    pending.requestSequence = nextSequence++;
                    SavePending();
                }
                else if (pending.requestSequence >= nextSequence
                    && pending.requestSequence < long.MaxValue)
                    nextSequence = pending.requestSequence + 1;
            }
        }

        public static void Suspend()
        {
            lock (Sync) sessionId = null;
        }

        public static bool TryNextQuery(long characterId, string activeSessionId,
            string queryType, out CultivationQueryRequestDto request)
        {
            request = null;
            lock (Sync)
            {
                if (!IsCurrent(characterId, activeSessionId)
                    || (queryType != CultivationMessageIds.Catalog
                        && queryType != CultivationMessageIds.Snapshot)
                    || nextSequence <= 0 || nextSequence == long.MaxValue)
                    return false;
                request = new CultivationQueryRequestDto(queryType, nextSequence++);
                return true;
            }
        }

        public static bool TryPrepareLearn(long characterId, string activeSessionId,
            long configuration, long inventory, long stack,
            out CultivationLearnRequestDto request)
        {
            request = null;
            lock (Sync)
            {
                if (!CanPrepare(characterId, activeSessionId, configuration, inventory)
                    || stack <= 0)
                    return false;
                PendingRequest value = CreatePending(CultivationMessageIds.Learn,
                    configuration, inventory);
                value.stackId = stack;
                pending = value;
                SavePending();
                request = new CultivationLearnRequestDto(sessionId, value.requestSequence,
                    value.clientRequestId, configuration, inventory, stack);
                return true;
            }
        }

        public static bool TryPrepareStart(long characterId, string activeSessionId,
            long configuration, long inventory, string technique,
            out CultivationStartRequestDto request)
        {
            request = null;
            lock (Sync)
            {
                if (!CanPrepare(characterId, activeSessionId, configuration, inventory)
                    || string.IsNullOrWhiteSpace(technique))
                    return false;
                PendingRequest value = CreatePending(CultivationMessageIds.Start,
                    configuration, inventory);
                value.techniqueId = technique;
                pending = value;
                SavePending();
                request = new CultivationStartRequestDto(sessionId, value.requestSequence,
                    value.clientRequestId, configuration, inventory, technique);
                return true;
            }
        }

        public static bool TryGetPending(out NetMessageBase request)
        {
            request = null;
            lock (Sync)
            {
                if (pending == null || pending.conflicted || sessionId == null
                    || !string.Equals(pending.sessionId, sessionId,
                        StringComparison.Ordinal))
                    return false;
                request = pending.requestType == CultivationMessageIds.Learn
                    ? (NetMessageBase)new CultivationLearnRequestDto(sessionId,
                        pending.requestSequence, pending.clientRequestId,
                        pending.configurationRevision, pending.expectedInventoryRevision,
                        pending.stackId)
                    : new CultivationStartRequestDto(sessionId, pending.requestSequence,
                        pending.clientRequestId, pending.configurationRevision,
                        pending.expectedInventoryRevision, pending.techniqueId);
                return true;
            }
        }

        public static bool Matches(long characterId, string resultSessionId,
            string requestType, long sequence, string requestId)
        {
            lock (Sync)
                return IsCurrent(characterId, resultSessionId) && pending != null
                    && pending.characterDbId == characterId
                    && pending.requestType == requestType
                    && pending.requestSequence == sequence && sequence > 0
                    && string.Equals(pending.clientRequestId, requestId,
                        StringComparison.Ordinal);
        }

        public static bool Complete(long characterId, string resultSessionId,
            string requestType, long sequence, string requestId)
        {
            lock (Sync)
            {
                if (!Matches(characterId, resultSessionId, requestType, sequence,
                    requestId))
                    return false;
                pending = null;
                PlayerPrefs.DeleteKey(PendingKeyPrefix + characterDbId);
                PlayerPrefs.Save();
                return true;
            }
        }

        public static void MarkConflict()
        {
            lock (Sync)
            {
                if (pending == null || sessionId == null)
                    return;
                pending.conflicted = true;
                SavePending();
            }
        }

        private static bool IsCurrent(long characterId, string activeSessionId)
        {
            return characterDbId > 0 && characterDbId == characterId
                && sessionId != null
                && string.Equals(sessionId, activeSessionId, StringComparison.Ordinal);
        }

        private static bool CanPrepare(long characterId, string activeSessionId,
            long configuration, long inventory)
        {
            return IsCurrent(characterId, activeSessionId) && pending == null
                && configuration > 0 && inventory >= 0
                && nextSequence > 0 && nextSequence < long.MaxValue;
        }

        private static PendingRequest CreatePending(string requestType,
            long configuration, long inventory)
        {
            return new PendingRequest
            {
                characterDbId = characterDbId,
                sessionId = sessionId,
                requestSequence = nextSequence++,
                clientRequestId = Guid.NewGuid().ToString("D"),
                requestType = requestType,
                configurationRevision = configuration,
                expectedInventoryRevision = inventory
            };
        }

        private static void SavePending()
        {
            PlayerPrefs.SetString(PendingKeyPrefix + characterDbId,
                JsonUtility.ToJson(pending));
            PlayerPrefs.Save();
        }
    }
}
