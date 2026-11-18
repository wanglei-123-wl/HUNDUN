using System;
using System.Collections.Generic;
using Game.Chat.Protocol;

namespace Game.Chat.Model
{
    public enum WorldChatHistoryStatus
    {
        Disconnected,
        WaitingForWorld,
        Receiving,
        Complete,
        Incomplete
    }

    public sealed class WorldChatTimeline
    {
        private const int MaxHistoryCount = 1000;
        private const int MaxBatchCount = 100;

        private readonly List<WorldChatMessage> history = new List<WorldChatMessage>();
        private readonly List<WorldChatMessage> pendingRealtime = new List<WorldChatMessage>();
        private readonly List<WorldChatMessage> messages = new List<WorldChatMessage>();
        private readonly HashSet<string> historyIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> pendingIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> displayedIds = new HashSet<string>(StringComparer.Ordinal);

        private double historyDeadline;
        private int nextBatchIndex;

        public long ConnectionEpoch { get; private set; }
        public WorldChatHistoryStatus Status { get; private set; }
        public string SyncId { get; private set; }
        public long SnapshotTime { get; private set; }
        public int TotalCount { get; private set; }
        public int ReceivedHistoryCount => history.Count;
        public string ErrorCode { get; private set; }
        public IReadOnlyList<WorldChatMessage> MessagesNewestFirst => messages.AsReadOnly();

        public event Action Changed;

        public void Reset(long epoch)
        {
            ConnectionEpoch = epoch;
            Status = epoch > 0
                ? WorldChatHistoryStatus.WaitingForWorld
                : WorldChatHistoryStatus.Disconnected;
            SyncId = null;
            SnapshotTime = 0;
            TotalCount = 0;
            ErrorCode = null;
            historyDeadline = 0;
            nextBatchIndex = 1;
            history.Clear();
            pendingRealtime.Clear();
            messages.Clear();
            historyIds.Clear();
            pendingIds.Clear();
            displayedIds.Clear();
            Changed?.Invoke();
        }

        public void EnterWorld(long epoch, double now, double timeoutSeconds)
        {
            if (epoch <= 0 || epoch != ConnectionEpoch
                || Status != WorldChatHistoryStatus.WaitingForWorld)
                return;

            Status = WorldChatHistoryStatus.Receiving;
            historyDeadline = now + Math.Max(1d, timeoutSeconds);
            Changed?.Invoke();
        }

        public void ReceiveRealtime(long epoch, WorldChatMessage message)
        {
            if (epoch <= 0 || epoch != ConnectionEpoch
                || Status == WorldChatHistoryStatus.Disconnected
                || message == null || message.msgId != "chat_message"
                || message.type != "chat_message" || message.channel != "world"
                || string.IsNullOrEmpty(message.messageId))
                return;

            if (Status == WorldChatHistoryStatus.WaitingForWorld
                || Status == WorldChatHistoryStatus.Receiving)
            {
                if (pendingIds.Add(message.messageId))
                    pendingRealtime.Add(message);
                return;
            }

            if (displayedIds.Add(message.messageId))
            {
                InsertByTime(message);
                Changed?.Invoke();
            }
        }

        public void ReceiveHistory(long epoch, WorldChatHistory batch)
        {
            if (epoch <= 0 || epoch != ConnectionEpoch
                || Status != WorldChatHistoryStatus.Receiving)
                return;

            if (batch == null || !batch.success)
            {
                MarkIncomplete(epoch, batch == null || string.IsNullOrEmpty(batch.errorCode)
                    ? "world_chat_history_unavailable" : batch.errorCode);
                return;
            }

            if (batch.msgId != "world_chat_history"
                || batch.type != "world_chat_history"
                || string.IsNullOrEmpty(batch.syncId)
                || batch.snapshotTime <= 0
                || batch.batchIndex <= 0
                || batch.totalCount < 0 || batch.totalCount > MaxHistoryCount
                || batch.messages == null || batch.messages.Length > MaxBatchCount)
            {
                MarkIncomplete(epoch, "world_chat_history_invalid");
                return;
            }

            if (SyncId == null)
            {
                if (batch.batchIndex != 1)
                {
                    MarkIncomplete(epoch, "world_chat_history_batch_gap");
                    return;
                }

                SyncId = batch.syncId;
                SnapshotTime = batch.snapshotTime;
                TotalCount = batch.totalCount;
            }
            else if (batch.syncId != SyncId
                || batch.snapshotTime != SnapshotTime
                || batch.totalCount != TotalCount)
            {
                MarkIncomplete(epoch, "world_chat_history_mismatch");
                return;
            }

            if (batch.batchIndex < nextBatchIndex) return;
            if (batch.batchIndex != nextBatchIndex)
            {
                MarkIncomplete(epoch, "world_chat_history_batch_gap");
                return;
            }

            if (history.Count + batch.messages.Length > TotalCount)
            {
                MarkIncomplete(epoch, "world_chat_history_count_mismatch");
                return;
            }

            var batchIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (WorldChatMessage message in batch.messages)
            {
                if (message == null || message.msgId != "chat_message"
                    || message.type != "chat_message" || message.channel != "world"
                    || string.IsNullOrEmpty(message.messageId)
                    || historyIds.Contains(message.messageId)
                    || !batchIds.Add(message.messageId))
                {
                    MarkIncomplete(epoch, "world_chat_history_invalid_message");
                    return;
                }
            }

            foreach (WorldChatMessage message in batch.messages)
            {
                history.Add(message);
                historyIds.Add(message.messageId);
            }

            nextBatchIndex++;
            if (batch.isLast)
            {
                if (history.Count != TotalCount)
                {
                    MarkIncomplete(epoch, "world_chat_history_count_mismatch");
                    return;
                }

                Status = WorldChatHistoryStatus.Complete;
                ReleaseRealtime();
            }

            Changed?.Invoke();
        }

        public void CheckTimeout(long epoch, double now)
        {
            if (epoch == ConnectionEpoch
                && Status == WorldChatHistoryStatus.Receiving
                && now >= historyDeadline)
                MarkIncomplete(epoch, "world_chat_history_timeout");
        }

        public void MarkIncomplete(long epoch, string errorCode)
        {
            if (epoch <= 0 || epoch != ConnectionEpoch
                || (Status != WorldChatHistoryStatus.Receiving
                    && Status != WorldChatHistoryStatus.WaitingForWorld))
                return;

            Status = WorldChatHistoryStatus.Incomplete;
            ErrorCode = errorCode ?? "world_chat_history_unavailable";
            ReleaseRealtime();
            Changed?.Invoke();
        }

        private void ReleaseRealtime()
        {
            foreach (WorldChatMessage message in history)
            {
                if (displayedIds.Add(message.messageId))
                    messages.Add(message);
            }

            foreach (WorldChatMessage message in pendingRealtime)
            {
                if (displayedIds.Add(message.messageId))
                    InsertByTime(message);
            }

            pendingRealtime.Clear();
            pendingIds.Clear();
        }

        private void InsertByTime(WorldChatMessage message)
        {
            int index = 0;
            while (index < messages.Count
                && messages[index].serverTime >= message.serverTime)
                index++;
            messages.Insert(index, message);
        }
    }
}
