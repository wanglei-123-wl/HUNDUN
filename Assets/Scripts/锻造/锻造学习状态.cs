using System;
using Game.Forging.Protocol;
using UnityEngine;

namespace Game.Forging.Runtime
{
    public sealed class 锻造学习状态
    {
        [Serializable]
        private sealed class 待确认学习
        {
            public long characterDbId;
            public string sessionId;
            public long requestSequence;
            public string clientRequestId;
            public long expectedInventoryRevision;
            public long stackId;
            public long lastSentAt;
            public bool conflicted;
        }

        private const string KeyPrefix = "forging_learn_in_flight_";
        private 待确认学习 pending;
        private long characterDbId;
        private string sessionId;
        private bool unreadable;
        private bool recoveryPending;

        public bool HasPending => pending != null || unreadable;
        public bool HasConflict => unreadable || pending != null && pending.conflicted;
        public bool NeedsRecovery => pending != null && recoveryPending
            && !pending.conflicted;
        public long LastSentAt => pending?.lastSentAt ?? 0;
        public long ExpectedInventoryRevision =>
            pending?.expectedInventoryRevision ?? -1;
        public long StackId => pending?.stackId ?? 0;

        public long EnterWorld(long character, string currentSession,
            long nextSequence)
        {
            Suspend();
            characterDbId = character;
            sessionId = currentSession;
            pending = null;
            unreadable = false;
            string saved = PlayerPrefs.GetString(KeyPrefix + character, "");
            if (string.IsNullOrEmpty(saved))
                return nextSequence;
            try
            {
                pending = JsonUtility.FromJson<待确认学习>(saved);
                if (!Valid(pending) || pending.characterDbId != character)
                    throw new FormatException("Invalid saved forging learn.");
                if (!string.Equals(pending.sessionId, currentSession,
                    StringComparison.Ordinal))
                {
                    pending.sessionId = currentSession;
                    pending.requestSequence = nextSequence++;
                    pending.lastSentAt = 0;
                    Save();
                }
                else if (pending.requestSequence >= nextSequence
                    && pending.requestSequence < long.MaxValue)
                    nextSequence = pending.requestSequence + 1;
                recoveryPending = !pending.conflicted;
            }
            catch (Exception exception)
            {
                pending = null;
                unreadable = true;
                Debug.LogError("[Forging] Saved learn is unreadable; "
                    + "new learning is blocked: " + exception.Message);
            }
            return nextSequence;
        }

        public void Suspend()
        {
            sessionId = null;
            recoveryPending = false;
        }

        public bool Prepare(long sequence, long inventoryRevision, long stackId,
            out 锻造学习请求 request)
        {
            request = null;
            if (HasPending || characterDbId <= 0 || sessionId == null
                || sequence <= 0 || inventoryRevision < 0 || stackId <= 0)
                return false;
            pending = new 待确认学习
            {
                characterDbId = characterDbId,
                sessionId = sessionId,
                requestSequence = sequence,
                clientRequestId = Guid.NewGuid().ToString("D"),
                expectedInventoryRevision = inventoryRevision,
                stackId = stackId
            };
            try { Save(); }
            catch (Exception exception)
            {
                pending = null;
                Debug.LogError("[Forging] Learn cannot be saved: "
                    + exception.Message);
                return false;
            }
            request = CreateRequest();
            return true;
        }

        public bool Matches(string resultSession, long sequence, string requestId)
        {
            return pending != null && sessionId != null
                && string.Equals(sessionId, resultSession, StringComparison.Ordinal)
                && string.Equals(pending.sessionId, resultSession,
                    StringComparison.Ordinal)
                && pending.requestSequence == sequence
                && string.Equals(pending.clientRequestId, requestId,
                    StringComparison.Ordinal);
        }

        public bool TryGetRetry(out 锻造学习请求 request)
        {
            request = NeedsRecovery ? CreateRequest() : null;
            return request != null;
        }

        public bool TryGetSameSession(out 锻造学习请求 request)
        {
            request = pending != null && sessionId != null
                && !recoveryPending && !pending.conflicted
                && string.Equals(pending.sessionId, sessionId,
                    StringComparison.Ordinal) ? CreateRequest() : null;
            return request != null;
        }

        public void MarkSent(long unixMilliseconds)
        {
            if (pending == null)
                return;
            recoveryPending = false;
            pending.lastSentAt = unixMilliseconds;
            Save();
        }

        public void MarkConflict()
        {
            if (pending == null)
                return;
            pending.conflicted = true;
            recoveryPending = false;
            Save();
        }

        public void Complete()
        {
            if (pending == null)
                return;
            PlayerPrefs.DeleteKey(KeyPrefix + characterDbId);
            PlayerPrefs.Save();
            pending = null;
            recoveryPending = false;
        }

        private 锻造学习请求 CreateRequest()
        {
            return new 锻造学习请求(pending.sessionId,
                pending.requestSequence, pending.clientRequestId,
                pending.expectedInventoryRevision, pending.stackId);
        }

        private void Save()
        {
            PlayerPrefs.SetString(KeyPrefix + characterDbId,
                JsonUtility.ToJson(pending));
            PlayerPrefs.Save();
        }

        private static bool Valid(待确认学习 value)
        {
            return value != null && value.characterDbId > 0
                && Guid.TryParseExact(value.sessionId, "D", out _)
                && Guid.TryParseExact(value.clientRequestId, "D", out _)
                && value.requestSequence > 0
                && value.expectedInventoryRevision >= 0 && value.stackId > 0;
        }
    }
}
