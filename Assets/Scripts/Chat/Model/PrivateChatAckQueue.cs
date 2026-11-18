using System;
using System.Collections.Generic;
using Game.Chat.Protocol;

namespace Game.Chat.Model
{
    public sealed class PrivateChatAckQueue
    {
        public const int MaxBatchSize = 100;
        private const int MaxRecentBatches = 16;

        private readonly Queue<string> pending = new Queue<string>();
        private readonly HashSet<string> pendingIds =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly Queue<string[]> recentBatches = new Queue<string[]>();

        public long ConnectionEpoch { get; private set; }
        public long CharacterDbId { get; private set; }
        public int PendingCount => pending.Count;

        public void Reset(long epoch)
        {
            ConnectionEpoch = epoch;
            CharacterDbId = 0;
            pending.Clear();
            pendingIds.Clear();
            recentBatches.Clear();
        }

        public bool EnterWorld(long epoch, long characterDbId)
        {
            if (epoch <= 0 || epoch != ConnectionEpoch || characterDbId <= 0)
                return false;
            if (CharacterDbId > 0 && CharacterDbId != characterDbId)
                return false;
            CharacterDbId = characterDbId;
            return true;
        }

        public bool QueueReceived(long epoch, WorldChatMessage message)
        {
            if (epoch <= 0 || epoch != ConnectionEpoch || CharacterDbId <= 0
                || message == null || message.msgId != "chat_message"
                || message.type != "chat_message" || message.channel != "private"
                || message.targetCharacterDbId != CharacterDbId
                || message.senderCharacterDbId <= 0
                || message.senderCharacterDbId == CharacterDbId
                || !Guid.TryParseExact(message.messageId, "D", out _))
                return false;

            if (pendingIds.Add(message.messageId))
                pending.Enqueue(message.messageId);
            return true;
        }

        public bool TryGetBatch(long epoch, out string[] ids)
        {
            ids = null;
            if (epoch <= 0 || epoch != ConnectionEpoch || CharacterDbId <= 0
                || pending.Count == 0)
                return false;

            int count = Math.Min(pending.Count, MaxBatchSize);
            ids = new string[count];
            int index = 0;
            foreach (string id in pending)
            {
                ids[index++] = id;
                if (index == count) break;
            }
            return true;
        }

        public bool MarkSent(long epoch, string[] ids)
        {
            if (epoch <= 0 || epoch != ConnectionEpoch || ids == null
                || ids.Length == 0 || ids.Length > MaxBatchSize
                || ids.Length > pending.Count)
                return false;

            string[] front = pending.ToArray();
            for (int index = 0; index < ids.Length; index++)
                if (front[index] != ids[index]) return false;

            foreach (string id in ids)
            {
                pending.Dequeue();
                pendingIds.Remove(id);
            }
            recentBatches.Enqueue(ids);
            while (recentBatches.Count > MaxRecentBatches)
                recentBatches.Dequeue();
            return true;
        }

        public bool RetryRecent(long epoch)
        {
            if (epoch <= 0 || epoch != ConnectionEpoch || CharacterDbId <= 0
                || recentBatches.Count == 0)
                return false;

            var retry = new Queue<string>();
            foreach (string[] batch in recentBatches)
                foreach (string id in batch)
                    if (pendingIds.Add(id)) retry.Enqueue(id);
            recentBatches.Clear();
            foreach (string id in pending) retry.Enqueue(id);
            pending.Clear();
            foreach (string id in retry) pending.Enqueue(id);
            return true;
        }
    }
}
