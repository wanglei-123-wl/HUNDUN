using System;
using UnityEngine;

namespace Game.PlayerTrade.Protocol
{
    public static class PlayerTradeRequestSequence
    {
        [Serializable]
        private sealed class InFlightOperation
        {
            public string sessionId;
            public long requestSequence;
            public string clientRequestId;
            public string msgId;
            public string requestJson;
        }

        private const string PendingKeyPrefix = "player_trade_in_flight_";
        private static readonly object Sync = new object();
        private static string sessionId;
        private static long characterDbId;
        private static long nextSequence = 1;
        private static InFlightOperation inFlight;

        public static bool CanStart
        {
            get
            {
                lock (Sync)
                    return !string.IsNullOrEmpty(sessionId) && inFlight == null;
            }
        }

        public static void EnterWorld(string serverSessionId, long characterId)
        {
            lock (Sync)
            {
                if (string.Equals(sessionId, serverSessionId, StringComparison.Ordinal)
                    && characterDbId == characterId)
                    return;

                sessionId = Guid.TryParseExact(serverSessionId, "D", out _)
                    ? serverSessionId : null;
                characterDbId = characterId;
                nextSequence = 1;
                string saved = PlayerPrefs.GetString(PendingKeyPrefix + characterId, "");
                try
                {
                    inFlight = string.IsNullOrEmpty(saved)
                        ? null : JsonUtility.FromJson<InFlightOperation>(saved);
                }
                catch (Exception exception)
                {
                    inFlight = new InFlightOperation
                    {
                        clientRequestId = "unreadable_saved_request"
                    };
                    Debug.LogError("[PlayerTrade] Saved trade request cannot be "
                        + "read; trade writes remain disabled: " + exception.Message);
                }
                if (inFlight != null)
                    Debug.LogWarning("[PlayerTrade] Previous trade result is unknown. "
                        + "Refresh orders, claims, inventory and wallet before resolving it. "
                        + "clientRequestId=" + inFlight.clientRequestId);
                if (sessionId == null)
                    Debug.LogWarning("[PlayerTrade] enter_world_result has no valid "
                        + "playerTradeOperationSessionId; trade writes are disabled.");
            }
        }

        public static void Suspend()
        {
            lock (Sync)
            {
                sessionId = null;
                if (inFlight != null)
                    Debug.LogWarning("[PlayerTrade] Trade result unknown after leaving "
                        + "the session. clientRequestId=" + inFlight.clientRequestId);
            }
        }

        public static bool TryPrepare(PlayerTradeWriteRequestDto request)
        {
            if (request == null)
                return false;
            lock (Sync)
            {
                if (string.IsNullOrEmpty(sessionId) || inFlight != null)
                {
                    Debug.LogWarning(inFlight == null
                        ? "[PlayerTrade] Trade session is unavailable; enter world again."
                        : "[PlayerTrade] An earlier trade result is unknown or pending. "
                          + "Do not send a new operation. clientRequestId="
                          + inFlight.clientRequestId);
                    return false;
                }

                request.playerTradeOperationSessionId = sessionId;
                request.requestSequence = nextSequence++;
                request.clientRequestId = Guid.NewGuid().ToString("D");
                inFlight = new InFlightOperation
                {
                    sessionId = sessionId,
                    requestSequence = request.requestSequence,
                    clientRequestId = request.clientRequestId,
                    msgId = request.msgId,
                    requestJson = JsonUtility.ToJson(request)
                };
                PlayerPrefs.SetString(PendingKeyPrefix + characterDbId,
                    JsonUtility.ToJson(inFlight));
                PlayerPrefs.Save();
                return true;
            }
        }

        public static bool IsCurrent(PlayerTradeWriteRequestDto request,
            string responseSessionId, string responseClientRequestId)
        {
            if (request == null)
                return false;
            lock (Sync)
                return inFlight != null
                    && inFlight.requestSequence == request.requestSequence
                    && string.Equals(inFlight.msgId, request.msgId,
                        StringComparison.Ordinal)
                    && string.Equals(inFlight.clientRequestId,
                        request.clientRequestId, StringComparison.Ordinal)
                    && string.Equals(inFlight.sessionId,
                        request.playerTradeOperationSessionId, StringComparison.Ordinal)
                    && (string.IsNullOrEmpty(responseSessionId)
                        || string.Equals(responseSessionId, inFlight.sessionId,
                            StringComparison.Ordinal))
                    && (string.IsNullOrEmpty(responseClientRequestId)
                        || string.Equals(responseClientRequestId,
                            inFlight.clientRequestId, StringComparison.Ordinal));
        }

        public static void Complete(PlayerTradeWriteRequestDto request,
            string responseSessionId, string responseClientRequestId)
        {
            lock (Sync)
            {
                if (!IsCurrent(request, responseSessionId, responseClientRequestId))
                    return;
                PlayerPrefs.DeleteKey(PendingKeyPrefix + characterDbId);
                PlayerPrefs.Save();
                inFlight = null;
            }
        }

        // Only call after an external reconciliation proves this operation did not apply.
        public static bool ConfirmNotAppliedAfterReconciliation(
            string expectedClientRequestId)
        {
            lock (Sync)
            {
                if (string.IsNullOrEmpty(sessionId) || inFlight == null
                    || string.IsNullOrEmpty(expectedClientRequestId)
                    || !string.Equals(inFlight.clientRequestId,
                        expectedClientRequestId, StringComparison.Ordinal))
                    return false;

                PlayerPrefs.DeleteKey(PendingKeyPrefix + characterDbId);
                PlayerPrefs.Save();
                inFlight = null;
                return true;
            }
        }
    }
}
