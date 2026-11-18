using System;
using System.Collections.Generic;
using System.Text;
using Game.Friend.Protocol;

namespace Game.Friend.Model
{
    public sealed class FriendRequestQueue
    {
        private const double ResultTimeoutSeconds = 8d;
        private const int MaxWaitingRequests = 32;
        private readonly Queue<Func<long, FriendRequestMessage>> waiting =
            new Queue<Func<long, FriendRequestMessage>>();
        private long nextSequence = 1;
        private double deadline;
        private bool listQueued;

        public long ConnectionEpoch { get; private set; }
        public long CharacterDbId { get; private set; }
        public long NextSequence => nextSequence;
        public FriendRequestMessage Pending { get; private set; }
        public bool NeedsRecovery { get; private set; }
        public string LastErrorCode { get; private set; }
        public int WaitingCount => waiting.Count;
        public event Action<FriendRequestMessage, bool, string> Completed;

        public void Reset(long epoch)
        {
            ConnectionEpoch = epoch;
            CharacterDbId = 0;
            nextSequence = 1;
            Pending = null;
            deadline = 0;
            NeedsRecovery = false;
            LastErrorCode = null;
            waiting.Clear();
            listQueued = false;
        }

        public bool EnterWorld(long epoch, long characterDbId)
        {
            if (epoch <= 0 || epoch != ConnectionEpoch || characterDbId <= 0)
                return false;
            if (CharacterDbId > 0 && CharacterDbId != characterDbId)
            {
                MarkUncertain("friend_character_changed");
                return false;
            }
            CharacterDbId = characterDbId;
            return true;
        }

        public bool RequestList()
        {
            if (CharacterDbId <= 0 || NeedsRecovery || ConnectionEpoch <= 0)
                return false;
            if (Pending is FriendListRequest || listQueued) return true;
            if (!Enqueue(sequence => new FriendListRequest(sequence))) return false;
            listQueued = true;
            return true;
        }

        public bool Search(string keyword)
        {
            string normalized = keyword == null ? string.Empty : keyword.Trim();
            if (normalized.Length == 0 || Encoding.UTF8.GetByteCount(normalized) > 16)
                return false;
            foreach (char character in normalized)
                if (char.IsControl(character)) return false;
            return Enqueue(sequence => new FriendSearchRequest(sequence, normalized));
        }

        public bool Send(long target, string message)
        {
            if (target <= 0 || target == CharacterDbId) return false;
            return Enqueue(sequence =>
                new FriendSendRequest(sequence, target, message ?? string.Empty));
        }

        public bool Handle(string requestId, bool accept)
        {
            if (string.IsNullOrWhiteSpace(requestId)) return false;
            string id = requestId.Trim();
            return Enqueue(sequence => new FriendHandleRequest(sequence, id, accept));
        }

        private bool Enqueue(Func<long, FriendRequestMessage> factory)
        {
            if (CharacterDbId <= 0 || NeedsRecovery || ConnectionEpoch <= 0
                || waiting.Count >= MaxWaitingRequests)
                return false;
            waiting.Enqueue(factory);
            return true;
        }

        public bool TrySendNext(long epoch, double now,
            Func<FriendRequestMessage, bool> transmit)
        {
            if (epoch <= 0 || epoch != ConnectionEpoch || CharacterDbId <= 0
                || NeedsRecovery || Pending != null || waiting.Count == 0
                || transmit == null || nextSequence == long.MaxValue)
                return false;

            Pending = waiting.Dequeue()(nextSequence++);
            if (Pending is FriendListRequest) listQueued = false;
            deadline = now + ResultTimeoutSeconds;
            bool sent;
            try { sent = transmit(Pending); }
            catch (Exception) { sent = false; }
            if (!sent) MarkUncertain("friend_send_uncertain");
            return sent;
        }

        public bool Complete(long epoch, string requestMsgId, long sequence,
            bool success, string errorCode)
        {
            if (epoch != ConnectionEpoch || Pending == null
                || Pending.requestSequence != sequence
                || !string.Equals(Pending.msgId, requestMsgId, StringComparison.Ordinal))
                return false;

            FriendRequestMessage completed = Pending;
            Pending = null;
            deadline = 0;
            LastErrorCode = success ? null : errorCode;
            if (!success && (errorCode == "invalid_request"
                || errorCode == "duplicate_request" || errorCode == "sequence_gap"))
            {
                nextSequence = sequence;
                MarkUncertain(errorCode);
            }
            Completed?.Invoke(completed, success, errorCode);
            return true;
        }

        public bool ReceiveGateError(long epoch, string requestMsgId,
            string errorCode)
        {
            if (epoch != ConnectionEpoch || Pending == null
                || !string.Equals(Pending.msgId, requestMsgId, StringComparison.Ordinal))
                return false;
            if (errorCode == "not_authenticated")
                nextSequence = Pending.requestSequence;
            MarkUncertain(errorCode);
            return true;
        }

        public void CheckTimeout(long epoch, double now)
        {
            if (epoch == ConnectionEpoch && Pending != null
                && !NeedsRecovery && now >= deadline)
                MarkUncertain("friend_result_timeout");
        }

        public void MarkUncertain(string errorCode)
        {
            NeedsRecovery = true;
            LastErrorCode = errorCode ?? "friend_outcome_unknown";
            waiting.Clear();
            listQueued = false;
        }
    }
}
