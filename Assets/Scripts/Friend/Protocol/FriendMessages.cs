using System;

namespace Game.Friend.Protocol
{
    [Serializable]
    public abstract class FriendRequestMessage : NetMessageBase
    {
        public long requestSequence;

        protected FriendRequestMessage(string id, long sequence)
        {
            msgId = type = id;
            requestSequence = sequence;
        }
    }

    [Serializable]
    public sealed class FriendListRequest : FriendRequestMessage
    {
        public FriendListRequest(long sequence) : base("friend_list", sequence) { }
    }

    [Serializable]
    public sealed class FriendSearchRequest : FriendRequestMessage
    {
        public string keyword;

        public FriendSearchRequest(long sequence, string value)
            : base("friend_search", sequence) { keyword = value; }
    }

    [Serializable]
    public sealed class FriendSendRequest : FriendRequestMessage
    {
        public long targetCharacterDbId;
        public string message;

        public FriendSendRequest(long sequence, long target, string text)
            : base("friend_request_send", sequence)
        {
            targetCharacterDbId = target;
            message = text;
        }
    }

    [Serializable]
    public sealed class FriendHandleRequest : FriendRequestMessage
    {
        public string requestId;

        public FriendHandleRequest(long sequence, string id, bool accept)
            : base(accept ? "friend_request_accept" : "friend_request_reject", sequence)
        {
            requestId = id;
        }
    }

    [Serializable]
    public sealed class FriendDto
    {
        public long characterDbId;
        public string characterId;
        public string nickname;
        public string roleId;
        public int level;
        public string sceneId;
        public bool online;
    }

    [Serializable]
    public sealed class BlockDto
    {
        public long characterDbId;
        public string characterId;
        public string nickname;
        public string reason;
        public long createdAt;
    }

    [Serializable]
    public sealed class RecentContactDto
    {
        public long characterDbId;
        public string characterId;
        public string nickname;
        public string lastChannel;
        public long lastContactAt;
    }

    [Serializable]
    public sealed class FriendRequestDto
    {
        public string requestId;
        public long fromCharacterDbId;
        public string fromCharacterId;
        public string fromNickname;
        public long toCharacterDbId;
        public string toCharacterId;
        public string toNickname;
        public string message;
        public string status;
        public long createdAt;
        public long expireAt;
        public long handledAt;
    }

    [Serializable]
    public sealed class FriendListResult : NetMessageBase
    {
        public long requestSequence;
        public FriendDto[] friends;
        public BlockDto[] blocks;
        public RecentContactDto[] recentContacts;
        public FriendRequestDto[] pendingReceivedRequests;
        public FriendRequestDto[] pendingSentRequests;
        public long serverTime;
    }

    [Serializable]
    public sealed class FriendSearchPlayer
    {
        public long characterDbId;
        public string characterId;
        public string nickname;
        public string roleId;
        public int level;
        public bool online;
    }

    [Serializable]
    public sealed class FriendSearchResult : NetMessageBase
    {
        public long requestSequence;
        public bool success;
        public FriendSearchPlayer[] players;
        public bool truncated;
        public string errorCode;
        public string message;
        public long serverTime;
    }

    [Serializable]
    public sealed class FriendSendResult : NetMessageBase
    {
        public long requestSequence;
        public bool success;
        public string requestId;
        public string errorCode;
        public string message;
        public long serverTime;
    }

    [Serializable]
    public sealed class FriendOperationResult : NetMessageBase
    {
        public string requestMsgId;
        public long requestSequence;
        public bool success;
        public string errorCode;
        public string message;
        public long serverTime;
    }

    [Serializable]
    public sealed class FriendReceived : NetMessageBase
    {
        public string requestId;
        public long fromCharacterDbId;
        public string fromCharacterId;
        public string fromNickname;
        public string message;
        public long expireAt;
        public long serverTime;
    }

    [Serializable]
    public sealed class FriendHandled : NetMessageBase
    {
        public string requestId;
        public string status;
        public long fromCharacterDbId;
        public long toCharacterDbId;
        public long serverTime;
    }

    [Serializable]
    public sealed class FriendListDelta : NetMessageBase
    {
        public string changeType;
        public FriendDto friend;
        public long targetCharacterDbId;
        public long serverTime;
    }

    [Serializable]
    public sealed class FriendGateError
    {
        public string msgId;
        public string requestMsgId;
        public string errorCode;
        public string message;
        public long serverTime;
    }
}
