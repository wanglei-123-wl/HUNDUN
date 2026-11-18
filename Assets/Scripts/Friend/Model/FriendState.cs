using System;
using System.Collections.Generic;
using Game.Friend.Protocol;

namespace Game.Friend.Model
{
    public sealed class FriendState
    {
        private const int MaxBufferedPushes = 512;
        private readonly Dictionary<long, FriendDto> friends = new Dictionary<long, FriendDto>();
        private readonly Dictionary<long, BlockDto> blocks = new Dictionary<long, BlockDto>();
        private readonly Dictionary<long, RecentContactDto> recentContacts =
            new Dictionary<long, RecentContactDto>();
        private readonly Dictionary<string, FriendRequestDto> received =
            new Dictionary<string, FriendRequestDto>(StringComparer.Ordinal);
        private readonly Dictionary<string, FriendRequestDto> sent =
            new Dictionary<string, FriendRequestDto>(StringComparer.Ordinal);
        private readonly Dictionary<long, string> sentRequestIds =
            new Dictionary<long, string>();
        private readonly Dictionary<string, FriendReceived> receivedNotices =
            new Dictionary<string, FriendReceived>(StringComparer.Ordinal);
        private readonly List<Action> bufferedPushes = new List<Action>();
        private long expectedListSequence;
        private bool bufferOverflowed;

        public long ConnectionEpoch { get; private set; }
        public long CharacterDbId { get; private set; }
        public bool HasSnapshot { get; private set; }
        public bool NeedsRefresh { get; private set; }
        public IReadOnlyDictionary<long, FriendDto> Friends => friends;
        public IReadOnlyDictionary<long, BlockDto> Blocks => blocks;
        public IReadOnlyDictionary<long, RecentContactDto> RecentContacts => recentContacts;
        public IReadOnlyDictionary<string, FriendRequestDto> PendingReceived => received;
        public IReadOnlyDictionary<string, FriendRequestDto> PendingSent => sent;
        public IReadOnlyDictionary<long, string> SentRequestIds => sentRequestIds;
        public IReadOnlyDictionary<string, FriendReceived> ReceivedNotices => receivedNotices;
        public event Action Changed;

        public void Reset(long epoch)
        {
            ConnectionEpoch = epoch;
            CharacterDbId = 0;
            expectedListSequence = 0;
            HasSnapshot = false;
            NeedsRefresh = false;
            bufferOverflowed = false;
            friends.Clear();
            blocks.Clear();
            recentContacts.Clear();
            received.Clear();
            sent.Clear();
            sentRequestIds.Clear();
            receivedNotices.Clear();
            bufferedPushes.Clear();
            Changed?.Invoke();
        }

        public bool EnterWorld(long epoch, long characterDbId)
        {
            if (epoch <= 0 || epoch != ConnectionEpoch || characterDbId <= 0)
                return false;
            if (CharacterDbId > 0 && CharacterDbId != characterDbId)
            {
                NeedsRefresh = true;
                return false;
            }
            CharacterDbId = characterDbId;
            return true;
        }

        public void BeginSnapshot(long epoch, long sequence)
        {
            if (epoch == ConnectionEpoch && sequence > 0)
                expectedListSequence = sequence;
        }

        public void AbortSnapshot(long epoch, long sequence)
        {
            if (epoch != ConnectionEpoch || sequence != expectedListSequence)
                return;
            expectedListSequence = 0;
            NeedsRefresh = true;
            if (!HasSnapshot) return;
            foreach (Action push in bufferedPushes) push();
            bufferedPushes.Clear();
            Changed?.Invoke();
        }

        public bool ApplySnapshot(long epoch, FriendListResult snapshot)
        {
            if (epoch != ConnectionEpoch || snapshot == null
                || snapshot.msgId != "friend_list_result"
                || snapshot.type != "friend_list_result"
                || snapshot.requestSequence <= 0
                || snapshot.requestSequence != expectedListSequence
                || snapshot.friends == null || snapshot.blocks == null
                || snapshot.recentContacts == null
                || snapshot.pendingReceivedRequests == null
                || snapshot.pendingSentRequests == null)
                return false;

            friends.Clear();
            blocks.Clear();
            recentContacts.Clear();
            received.Clear();
            sent.Clear();
            sentRequestIds.Clear();
            receivedNotices.Clear();
            foreach (FriendDto item in snapshot.friends)
                if (item != null && item.characterDbId > 0)
                    friends[item.characterDbId] = item;
            foreach (BlockDto item in snapshot.blocks)
                if (item != null && item.characterDbId > 0)
                    blocks[item.characterDbId] = item;
            foreach (RecentContactDto item in snapshot.recentContacts)
                if (item != null && item.characterDbId > 0)
                    recentContacts[item.characterDbId] = item;
            foreach (FriendRequestDto item in snapshot.pendingReceivedRequests)
                if (item != null && !string.IsNullOrEmpty(item.requestId))
                    received[item.requestId] = item;
            foreach (FriendRequestDto item in snapshot.pendingSentRequests)
                if (item != null && !string.IsNullOrEmpty(item.requestId))
                    sent[item.requestId] = item;

            expectedListSequence = 0;
            HasSnapshot = true;
            NeedsRefresh = bufferOverflowed;
            bufferOverflowed = false;
            foreach (Action push in bufferedPushes) push();
            bufferedPushes.Clear();
            Changed?.Invoke();
            return true;
        }

        public void RecordSentRequest(long epoch, long targetCharacterDbId,
            string requestId)
        {
            if (epoch != ConnectionEpoch || CharacterDbId <= 0
                || targetCharacterDbId <= 0 || targetCharacterDbId == CharacterDbId
                || string.IsNullOrWhiteSpace(requestId))
                return;
            sentRequestIds[targetCharacterDbId] = requestId;
        }

        public void Receive(long epoch, FriendReceived notification)
        {
            if (notification == null || notification.msgId != "friend_request_received"
                || notification.type != "friend_request_received"
                || string.IsNullOrEmpty(notification.requestId)) return;
            BufferOrApply(epoch, () =>
            {
                if (!received.ContainsKey(notification.requestId))
                    receivedNotices[notification.requestId] = notification;
            });
        }

        public void Handle(long epoch, FriendHandled notification)
        {
            if (notification == null || notification.msgId != "friend_request_handled"
                || notification.type != "friend_request_handled"
                || string.IsNullOrEmpty(notification.requestId)
                || (notification.status != "accepted" && notification.status != "rejected"))
                return;
            BufferOrApply(epoch, () =>
            {
                received.Remove(notification.requestId);
                sent.Remove(notification.requestId);
                receivedNotices.Remove(notification.requestId);
                long resolvedTarget = 0;
                foreach (KeyValuePair<long, string> entry in sentRequestIds)
                    if (entry.Value == notification.requestId)
                    {
                        resolvedTarget = entry.Key;
                        break;
                    }
                if (resolvedTarget > 0) sentRequestIds.Remove(resolvedTarget);
            });
        }

        public void ApplyDelta(long epoch, FriendListDelta delta)
        {
            if (delta == null || delta.msgId != "friend_list_delta"
                || delta.type != "friend_list_delta") return;
            if (delta.changeType == "add"
                && (delta.friend == null || delta.friend.characterDbId <= 0))
                return;
            if (delta.changeType == "remove" && delta.targetCharacterDbId <= 0)
                return;
            if (delta.changeType != "add" && delta.changeType != "remove")
                return;

            BufferOrApply(epoch, () =>
            {
                if (delta.changeType == "add")
                {
                    friends[delta.friend.characterDbId] = delta.friend;
                    sentRequestIds.Remove(delta.friend.characterDbId);
                }
                else
                    friends.Remove(delta.targetCharacterDbId);
            });
        }

        private void BufferOrApply(long epoch, Action push)
        {
            if (epoch <= 0 || epoch != ConnectionEpoch) return;
            if (!HasSnapshot || expectedListSequence > 0)
            {
                if (bufferedPushes.Count < MaxBufferedPushes)
                    bufferedPushes.Add(push);
                else
                {
                    bufferOverflowed = true;
                    NeedsRefresh = true;
                }
                return;
            }
            push();
            Changed?.Invoke();
        }
    }
}
