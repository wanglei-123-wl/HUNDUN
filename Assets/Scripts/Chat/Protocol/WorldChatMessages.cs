using System;

namespace Game.Chat.Protocol
{
    [Serializable]
    public sealed class WorldChatSendRequest : NetMessageBase
    {
        public long requestSequence;
        public string channel;
        public string content;

        public WorldChatSendRequest(long sequence, string message)
        {
            msgId = type = "chat_send";
            requestSequence = sequence;
            channel = "world";
            content = message;
        }
    }

    [Serializable]
    public sealed class PrivateChatSendRequest : NetMessageBase
    {
        public long requestSequence;
        public string channel;
        public long targetCharacterDbId;
        public string content;

        public PrivateChatSendRequest(long sequence, long target, string message)
        {
            msgId = type = "chat_send";
            requestSequence = sequence;
            channel = "private";
            targetCharacterDbId = target;
            content = message;
        }
    }

    [Serializable]
    public sealed class PrivateChatAckRequest : NetMessageBase
    {
        public string[] messageIds;

        public PrivateChatAckRequest(string[] ids)
        {
            msgId = type = "chat_private_ack";
            messageIds = ids;
        }
    }

    [Serializable]
    public sealed class WorldChatSendResult : NetMessageBase
    {
        public long requestSequence;
        public bool success;
        public string errorCode;
        public string message;
        public long serverTime;
    }

    [Serializable]
    public sealed class WorldChatMessage : NetMessageBase
    {
        public string messageId;
        public string channel;
        public long senderCharacterDbId;
        public string senderName;
        public long targetCharacterDbId;
        public string targetName;
        public string guildId;
        public string content;
        public long serverTime;
    }

    [Serializable]
    public sealed class WorldChatHistory : NetMessageBase
    {
        public string syncId;
        public long snapshotTime;
        public int batchIndex;
        public int totalCount;
        public WorldChatMessage[] messages;
        public bool isLast;
        public bool success;
        public string errorCode;
    }
}
