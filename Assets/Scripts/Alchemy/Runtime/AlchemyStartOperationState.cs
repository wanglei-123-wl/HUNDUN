using System;
using Game.Alchemy.Protocol;
using UnityEngine;

namespace Game.Alchemy.Runtime
{
    public sealed class AlchemyStartOperationState
    {
        [Serializable]
        private sealed class Pending
        {
            public long characterDbId;
            public string sessionId;
            public long requestSequence;
            public string clientRequestId;
            public long configurationRevision;
            public long expectedInventoryRevision;
            public string recipeId;
            public AlchemySelectedMaterialDto[] materials;
            public long lastSentAt;
            public bool conflicted;
        }

        private const string KeyPrefix = "alchemy_start_in_flight_";
        private Pending pending;
        private long characterDbId;
        private string sessionId;
        private bool unreadable;
        private bool recoveryPending;

        public bool HasPending => pending != null || unreadable;
        public bool HasConflict => unreadable || (pending != null && pending.conflicted);
        public bool NeedsRecovery => pending != null && recoveryPending
            && !pending.conflicted;
        public long LastSentAt => pending?.lastSentAt ?? 0;

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
                pending = JsonUtility.FromJson<Pending>(saved);
                if (!Valid(pending) || pending.characterDbId != character)
                    throw new FormatException("Invalid saved alchemy request.");
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
                Debug.LogError("[Alchemy] Saved start request is unreadable; "
                    + "new starts are blocked: " + exception.Message);
            }
            return nextSequence;
        }

        public void Suspend()
        {
            sessionId = null;
            recoveryPending = false;
        }

        public bool Prepare(long sequence, long configurationRevision,
            long inventoryRevision, string recipeId,
            AlchemySelectedMaterialDto[] materials,
            out AlchemyStartRequestDto request)
        {
            request = null;
            if (HasPending || characterDbId <= 0 || sessionId == null
                || sequence <= 0 || configurationRevision < 0
                || inventoryRevision < 0 || string.IsNullOrWhiteSpace(recipeId)
                || !ValidMaterials(materials))
                return false;
            pending = new Pending
            {
                characterDbId = characterDbId,
                sessionId = sessionId,
                requestSequence = sequence,
                clientRequestId = Guid.NewGuid().ToString("D"),
                configurationRevision = configurationRevision,
                expectedInventoryRevision = inventoryRevision,
                recipeId = recipeId,
                materials = Copy(materials)
            };
            try { Save(); }
            catch (Exception exception)
            {
                pending = null;
                Debug.LogError("[Alchemy] Start cannot be saved: "
                    + exception.Message);
                return false;
            }
            request = CreateRequest();
            return true;
        }

        public bool Matches(string resultSession, long sequence,
            string requestId)
        {
            return pending != null && sessionId != null
                && string.Equals(sessionId, resultSession,
                    StringComparison.Ordinal)
                && string.Equals(pending.sessionId, resultSession,
                    StringComparison.Ordinal)
                && pending.requestSequence == sequence
                && string.Equals(pending.clientRequestId, requestId,
                    StringComparison.Ordinal);
        }

        public bool TryGetRetry(out AlchemyStartRequestDto request)
        {
            request = NeedsRecovery ? CreateRequest() : null;
            return request != null;
        }

        public bool TryGetPending(out AlchemyStartRequestDto request)
        {
            request = pending != null && characterDbId == pending.characterDbId
                ? CreateRequest() : null;
            return request != null;
        }

        public bool TryGetSameSession(out AlchemyStartRequestDto request)
        {
            request = pending != null && sessionId != null
                && !recoveryPending && !pending.conflicted
                && string.Equals(pending.sessionId, sessionId,
                    StringComparison.Ordinal)
                ? CreateRequest() : null;
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

        private AlchemyStartRequestDto CreateRequest()
        {
            return new AlchemyStartRequestDto(pending.sessionId,
                pending.requestSequence, pending.clientRequestId,
                pending.configurationRevision, pending.expectedInventoryRevision,
                pending.recipeId, Copy(pending.materials));
        }

        private void Save()
        {
            PlayerPrefs.SetString(KeyPrefix + characterDbId,
                JsonUtility.ToJson(pending));
            PlayerPrefs.Save();
        }

        private static bool Valid(Pending value)
        {
            return value != null && value.characterDbId > 0
                && Guid.TryParseExact(value.sessionId, "D", out _)
                && Guid.TryParseExact(value.clientRequestId, "D", out _)
                && value.requestSequence > 0
                && value.configurationRevision >= 0
                && value.expectedInventoryRevision >= 0
                && !string.IsNullOrWhiteSpace(value.recipeId)
                && ValidMaterials(value.materials);
        }

        private static bool ValidMaterials(AlchemySelectedMaterialDto[] materials)
        {
            if (materials == null || materials.Length == 0)
                return false;
            var seen = new System.Collections.Generic.HashSet<long>();
            foreach (AlchemySelectedMaterialDto entry in materials)
                if (entry == null || entry.stackId <= 0 || entry.count <= 0
                    || !seen.Add(entry.stackId))
                    return false;
            return true;
        }

        private static AlchemySelectedMaterialDto[] Copy(
            AlchemySelectedMaterialDto[] materials)
        {
            var copy = new AlchemySelectedMaterialDto[materials.Length];
            for (int i = 0; i < materials.Length; i++)
                copy[i] = new AlchemySelectedMaterialDto(materials[i].stackId,
                    materials[i].count);
            return copy;
        }
    }
}
