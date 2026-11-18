using System;
using System.Globalization;
using Game.Chat.Protocol;

namespace Game.Chat.Model
{
    public sealed class ChatSendSession
    {
        private const double WorldCooldownSeconds = 5d;
        private const double PrivateCooldownSeconds = 0.8d;
        private const double ResultTimeoutSeconds = 15d;
        private const int MaxContentLength = 100;

        private long nextSequence = 1;
        private double worldCooldownUntil;
        private double privateCooldownUntil;
        private double resultDeadline;
        private bool deliveryUnknown;
        private string pendingChannel;
        private long pendingTargetCharacterDbId;

        public long ConnectionEpoch { get; private set; }
        public long CharacterDbId { get; private set; }
        public long PendingSequence { get; private set; }
        public long NextSequence => nextSequence;
        public string PendingChannel => pendingChannel;
        public long PendingTargetCharacterDbId => pendingTargetCharacterDbId;
        public bool IsInWorld { get; private set; }
        public bool DeliveryUnknown => deliveryUnknown;
        public string LastErrorCode { get; private set; }
        public string LastServerMessage { get; private set; }
        public WorldChatSendResult LastResult { get; private set; }

        public event Action Changed;

        public void Reset(long epoch)
        {
            ConnectionEpoch = epoch;
            CharacterDbId = 0;
            IsInWorld = false;
            nextSequence = 1;
            PendingSequence = 0;
            worldCooldownUntil = 0;
            privateCooldownUntil = 0;
            resultDeadline = 0;
            deliveryUnknown = false;
            pendingChannel = null;
            pendingTargetCharacterDbId = 0;
            LastErrorCode = null;
            LastServerMessage = null;
            LastResult = null;
            Changed?.Invoke();
        }

        public void EnterWorld(long epoch, long characterDbId)
        {
            if (epoch <= 0 || epoch != ConnectionEpoch || characterDbId <= 0)
                return;

            CharacterDbId = characterDbId;
            IsInWorld = true;
            Changed?.Invoke();
        }

        public double RemainingCooldown(double now)
        {
            return RemainingCooldown("world", now);
        }

        public double RemainingCooldown(string channel, double now)
        {
            double cooldownUntil = channel == "private"
                ? privateCooldownUntil : worldCooldownUntil;
            return Math.Max(0d, cooldownUntil - now);
        }

        public bool TrySendWorld(long epoch, string content, double now,
            Func<WorldChatSendRequest, bool> transmit, out string errorCode)
        {
            return TrySendCore(epoch, "world", 0, content, now,
                transmit == null ? null
                    : new Func<long, string, bool>((sequence, message) =>
                        transmit(new WorldChatSendRequest(sequence, message))),
                out errorCode);
        }

        public bool TrySendPrivate(long epoch, long targetCharacterDbId,
            string content, double now, Func<PrivateChatSendRequest, bool> transmit,
            out string errorCode)
        {
            return TrySendCore(epoch, "private", targetCharacterDbId, content, now,
                transmit == null ? null
                    : new Func<long, string, bool>((sequence, message) =>
                        transmit(new PrivateChatSendRequest(
                            sequence, targetCharacterDbId, message))),
                out errorCode);
        }

        private bool TrySendCore(long epoch, string channel,
            long targetCharacterDbId, string content, double now,
            Func<long, string, bool> transmit, out string errorCode)
        {
            errorCode = null;
            if (epoch <= 0 || epoch != ConnectionEpoch || !IsInWorld)
                errorCode = "player_not_in_world";
            else if (deliveryUnknown || nextSequence <= 0 || nextSequence == long.MaxValue)
                errorCode = "chat_send_outcome_unknown";
            else if (PendingSequence > 0)
                errorCode = "chat_send_pending";
            else if (channel == "private" && targetCharacterDbId <= 0)
                errorCode = "target_required";
            else if (channel == "private" && targetCharacterDbId == CharacterDbId)
                errorCode = "cannot_chat_self";
            else if (RemainingCooldown(channel, now) > 0)
                errorCode = "rate_limited";
            else if (string.IsNullOrWhiteSpace(content))
                errorCode = "content_empty";
            else if (content.Trim().Length > MaxContentLength)
                errorCode = "content_too_long";
            else if (transmit == null)
                errorCode = "chat_transport_unavailable";

            if (errorCode != null)
                return false;

            long sequence = nextSequence++;
            PendingSequence = sequence;
            pendingChannel = channel;
            pendingTargetCharacterDbId = targetCharacterDbId;
            resultDeadline = now + ResultTimeoutSeconds;
            SetCooldown(channel, now + (channel == "private"
                ? PrivateCooldownSeconds : WorldCooldownSeconds));
            LastErrorCode = null;
            LastServerMessage = null;
            LastResult = null;

            bool sent;
            try { sent = transmit(sequence, content.Trim()); }
            catch (Exception) { sent = false; }

            if (!sent)
            {
                PendingSequence = 0;
                pendingChannel = null;
                pendingTargetCharacterDbId = 0;
                deliveryUnknown = true;
                errorCode = LastErrorCode = "chat_send_outcome_unknown";
            }

            Changed?.Invoke();
            return sent;
        }

        public bool ReceiveResult(long epoch, WorldChatSendResult result, double now)
        {
            if (epoch != ConnectionEpoch || PendingSequence <= 0
                || result == null || result.msgId != "chat_send_result"
                || result.type != "chat_send_result"
                || result.requestSequence != PendingSequence)
                return false;

            string channel = pendingChannel;
            PendingSequence = 0;
            pendingChannel = null;
            pendingTargetCharacterDbId = 0;
            resultDeadline = 0;
            LastResult = result;
            LastErrorCode = result.success ? null : result.errorCode;
            LastServerMessage = result.message;

            if (!result.success)
            {
                if (result.errorCode == "rate_limited")
                    SetCooldown(channel, now + RetryAfterSeconds(
                        result.message, channel == "private"
                            ? PrivateCooldownSeconds : WorldCooldownSeconds));
                else if (result.errorCode == "player_not_in_world")
                    IsInWorld = false;
                else if (result.errorCode == "invalid_chat_request"
                    || result.errorCode == "chat_duplicate_request"
                    || result.errorCode == "chat_sequence_gap")
                    deliveryUnknown = true;
            }

            Changed?.Invoke();
            return true;
        }

        public bool ReceiveGateError(long epoch, string errorCode, string message)
        {
            if (epoch != ConnectionEpoch || PendingSequence <= 0)
                return false;

            if (errorCode == "not_authenticated")
            {
                nextSequence = PendingSequence;
                IsInWorld = false;
                ClearCooldowns();
            }
            else
            {
                deliveryUnknown = true;
            }

            PendingSequence = 0;
            pendingChannel = null;
            pendingTargetCharacterDbId = 0;
            resultDeadline = 0;
            LastErrorCode = errorCode;
            LastServerMessage = message;
            Changed?.Invoke();
            return true;
        }

        public void CheckTimeout(long epoch, double now)
        {
            if (epoch != ConnectionEpoch || PendingSequence <= 0
                || now < resultDeadline)
                return;

            PendingSequence = 0;
            pendingChannel = null;
            pendingTargetCharacterDbId = 0;
            deliveryUnknown = true;
            LastErrorCode = "chat_send_result_timeout";
            Changed?.Invoke();
        }

        private void SetCooldown(string channel, double until)
        {
            if (channel == "private")
                privateCooldownUntil = until;
            else
                worldCooldownUntil = until;
        }

        private void ClearCooldowns()
        {
            worldCooldownUntil = 0;
            privateCooldownUntil = 0;
        }

        private static double RetryAfterSeconds(string message, double fallback)
        {
            const string key = "retryAfterMs=";
            int start = message == null
                ? -1 : message.IndexOf(key, StringComparison.Ordinal);
            if (start < 0) return fallback;

            start += key.Length;
            int end = start;
            while (end < message.Length && message[end] >= '0'
                && message[end] <= '9')
                end++;

            if (!long.TryParse(message.Substring(start, end - start),
                    NumberStyles.None, CultureInfo.InvariantCulture,
                    out long milliseconds))
                return fallback;

            return milliseconds / 1000d;
        }
    }
}
