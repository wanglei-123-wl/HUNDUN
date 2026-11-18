using System;
using System.Collections.Generic;
using Game.Chat.Protocol;

namespace Game.Chat.Model
{
    public enum PrivateChatReceiveResult
    {
        Invalid,
        Added,
        Duplicate,
        StorageFailed
    }

    public sealed class PrivateChatConversationStore
    {
        private readonly Dictionary<long, List<WorldChatMessage>> messagesByPartner =
            new Dictionary<long, List<WorldChatMessage>>();
        private readonly Dictionary<string, WorldChatMessage> messagesById =
            new Dictionary<string, WorldChatMessage>(StringComparer.Ordinal);
        private IPrivateChatMessageStorage storage;

        public long ConnectionEpoch { get; private set; }
        public long CharacterDbId { get; private set; }
        public bool IsPersistent => storage != null && CharacterDbId > 0;
        public IReadOnlyCollection<long> ConversationIds =>
            messagesByPartner.Keys;

        public event Action<long> ConversationChanged;
        public event Action Changed;

        public void Reset(long epoch, bool notifyObservers = true)
        {
            ConnectionEpoch = epoch;
            CharacterDbId = 0;
            storage = null;
            messagesByPartner.Clear();
            messagesById.Clear();
            if (notifyObservers) PublishChanged();
        }

        public bool EnterWorld(long epoch, long characterDbId)
        {
            return EnterWorld(epoch, characterDbId, null);
        }

        public bool EnterWorld(long epoch, long characterDbId,
            IPrivateChatMessageStorage persistentStorage,
            bool notifyObservers = true)
        {
            if (epoch <= 0 || epoch != ConnectionEpoch || characterDbId <= 0)
                return false;
            if (CharacterDbId > 0 && CharacterDbId != characterDbId)
                return false;

            if (persistentStorage != null)
            {
                if (!persistentStorage.TryLoad(out List<WorldChatMessage> saved)
                    || saved == null)
                    return false;

                var restored = new Dictionary<long, List<WorldChatMessage>>();
                var restoredIds = new Dictionary<string, WorldChatMessage>(
                    StringComparer.Ordinal);
                foreach (WorldChatMessage message in saved)
                {
                    if (!TryGetPartner(message, characterDbId, out long partnerId)
                        || restoredIds.ContainsKey(message.messageId))
                        return false;
                    restoredIds.Add(message.messageId, message);
                    if (!restored.TryGetValue(partnerId, out List<WorldChatMessage> list))
                    {
                        list = new List<WorldChatMessage>();
                        restored.Add(partnerId, list);
                    }
                    list.Add(message);
                }
                messagesByPartner.Clear();
                messagesById.Clear();
                foreach (KeyValuePair<long, List<WorldChatMessage>> entry in restored)
                {
                    entry.Value.Sort(CompareMessages);
                    messagesByPartner.Add(entry.Key, entry.Value);
                }
                foreach (KeyValuePair<string, WorldChatMessage> entry in restoredIds)
                    messagesById.Add(entry.Key, entry.Value);
            }
            CharacterDbId = characterDbId;
            storage = persistentStorage;
            if (notifyObservers) PublishChanged();
            return true;
        }

        public bool Receive(long epoch, WorldChatMessage message,
            out long partnerCharacterDbId)
        {
            return TryReceive(epoch, message, out partnerCharacterDbId)
                == PrivateChatReceiveResult.Added;
        }

        public PrivateChatReceiveResult TryReceive(long epoch,
            WorldChatMessage message, out long partnerCharacterDbId,
            bool notifyObservers = true)
        {
            partnerCharacterDbId = 0;
            if (epoch <= 0 || epoch != ConnectionEpoch
                || CharacterDbId <= 0
                || !TryGetPartner(message, CharacterDbId,
                    out partnerCharacterDbId))
                return PrivateChatReceiveResult.Invalid;

            if (messagesById.TryGetValue(message.messageId,
                out WorldChatMessage existing))
            {
                if (!SameMessage(existing, message))
                    return PrivateChatReceiveResult.Invalid;
                return storage == null || storage.TryWrite(partnerCharacterDbId, message)
                    ? PrivateChatReceiveResult.Duplicate
                    : PrivateChatReceiveResult.StorageFailed;
            }
            if (storage != null
                && !storage.TryWrite(partnerCharacterDbId, message))
                return PrivateChatReceiveResult.StorageFailed;

            if (!messagesByPartner.TryGetValue(partnerCharacterDbId,
                out List<WorldChatMessage> messages))
            {
                messages = new List<WorldChatMessage>();
                messagesByPartner.Add(partnerCharacterDbId, messages);
            }

            int index = messages.BinarySearch(message,
                Comparer<WorldChatMessage>.Create(CompareMessages));
            messages.Insert(index < 0 ? ~index : index, message);
            messagesById.Add(message.messageId, message);
            if (notifyObservers) PublishChanged(partnerCharacterDbId);
            return PrivateChatReceiveResult.Added;
        }

        public void PublishChanged(long partnerCharacterDbId = 0)
        {
            if (partnerCharacterDbId > 0)
                ConversationChanged?.Invoke(partnerCharacterDbId);
            Changed?.Invoke();
        }

        private static bool SameMessage(WorldChatMessage first,
            WorldChatMessage second)
        {
            return first.senderCharacterDbId == second.senderCharacterDbId
                && first.targetCharacterDbId == second.targetCharacterDbId
                && first.content == second.content
                && first.serverTime == second.serverTime;
        }

        private static bool TryGetPartner(WorldChatMessage message,
            long characterDbId, out long partnerId)
        {
            partnerId = 0;
            if (message == null || message.msgId != "chat_message"
                || message.type != "chat_message"
                || message.channel != "private"
                || string.IsNullOrWhiteSpace(message.messageId)
                || message.senderCharacterDbId <= 0
                || message.targetCharacterDbId <= 0
                || (message.senderCharacterDbId != characterDbId
                    && message.targetCharacterDbId != characterDbId))
                return false;
            partnerId = message.senderCharacterDbId == characterDbId
                ? message.targetCharacterDbId : message.senderCharacterDbId;
            return partnerId > 0 && partnerId != characterDbId;
        }

        private static int CompareMessages(WorldChatMessage first,
            WorldChatMessage second)
        {
            int byTime = first.serverTime.CompareTo(second.serverTime);
            return byTime != 0 ? byTime
                : string.CompareOrdinal(first.messageId, second.messageId);
        }

        public IReadOnlyList<WorldChatMessage> GetMessages(
            long partnerCharacterDbId)
        {
            if (partnerCharacterDbId <= 0
                || !messagesByPartner.TryGetValue(partnerCharacterDbId,
                    out List<WorldChatMessage> messages))
                return Array.Empty<WorldChatMessage>();
            return messages.AsReadOnly();
        }
    }
}
