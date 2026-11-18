using System;
using Game.Chat.Model;
using Game.Chat.Protocol;
using Game.Chat.Storage;
using Game.Chat.Transport;
using UnityEngine;

namespace Game.Chat.Runtime
{
    [DisallowMultipleComponent]
    public sealed class WorldChatSessionRuntime : MonoBehaviour
    {
        [SerializeField, Min(1f)] private float historyTimeoutSeconds = 15f;

        private readonly WorldChatTimeline timeline = new WorldChatTimeline();
        private readonly ChatSendSession sender = new ChatSendSession();
        private readonly PrivateChatConversationStore privateConversations =
            new PrivateChatConversationStore();
        private readonly PrivateChatAckQueue privateAcks = new PrivateChatAckQueue();
        private NetworkManager network;
        private WorldChatTransportInbox inbox;
        private double nextAckAttemptAt;
        private int ackFailures;
        private bool ackWaitingForWorld;

        public static WorldChatSessionRuntime Instance { get; private set; }
        public WorldChatTimeline Timeline => timeline;
        public ChatSendSession Sender => sender;
        public PrivateChatConversationStore PrivateConversations =>
            privateConversations;
        public event Action<WorldChatInboundPacket> OperationPacketReceived;
        public event Action<WorldChatMessage> PrivateMessageReceived;
        public event Action<WorldChatSendResult> SendResultReceived;

        private void Awake()
        {
            Instance = this;
        }

        public static WorldChatSessionRuntime Ensure(
            NetworkManager network, WorldChatTransportInbox inbox)
        {
            if (network == null || inbox == null)
                throw new ArgumentNullException(network == null
                    ? nameof(network) : nameof(inbox));

            WorldChatSessionRuntime runtime =
                network.GetComponent<WorldChatSessionRuntime>();
            if (runtime == null)
                runtime = network.gameObject.AddComponent<WorldChatSessionRuntime>();
            runtime.Initialize(network, inbox);
            Instance = runtime;
            return runtime;
        }

        public void BeginSession()
        {
            if (network == null || inbox == null) return;
            timeline.Reset(network.ConnectionEpoch);
            sender.Reset(network.ConnectionEpoch);
            ResetPrivateAcks(network.ConnectionEpoch);
            ResetPrivateConversations(network.ConnectionEpoch);
            if (inbox.IsInWorld)
            {
                timeline.EnterWorld(inbox.ConnectionEpoch, Time.realtimeSinceStartup,
                    historyTimeoutSeconds);
                sender.EnterWorld(inbox.ConnectionEpoch, inbox.CharacterDbId);
                OpenPrivateConversations(inbox.ConnectionEpoch, inbox.CharacterDbId);
            }
            if (inbox.HasBufferOverflow)
                timeline.MarkIncomplete(inbox.ConnectionEpoch, "world_chat_buffer_overflow");
        }

        private void Initialize(NetworkManager activeNetwork, WorldChatTransportInbox activeInbox)
        {
            if (ReferenceEquals(network, activeNetwork) && ReferenceEquals(inbox, activeInbox))
                return;

            Unsubscribe();
            network = activeNetwork;
            inbox = activeInbox;
            timeline.Reset(network.ConnectionEpoch);
            sender.Reset(network.ConnectionEpoch);
            ResetPrivateAcks(network.ConnectionEpoch);
            ResetPrivateConversations(network.ConnectionEpoch);
            network.OnConnectedWithEpoch += HandleConnected;
            network.OnDisconnectedWithEpoch += HandleDisconnected;
            inbox.WorldEntered += HandleWorldEntered;
            inbox.WorldEntryRejected += HandleWorldEntryRejected;

            if (inbox.IsInWorld)
            {
                timeline.EnterWorld(inbox.ConnectionEpoch, Time.realtimeSinceStartup,
                    historyTimeoutSeconds);
                sender.EnterWorld(inbox.ConnectionEpoch, inbox.CharacterDbId);
                OpenPrivateConversations(inbox.ConnectionEpoch, inbox.CharacterDbId);
            }
            if (inbox.HasBufferOverflow)
                timeline.MarkIncomplete(inbox.ConnectionEpoch, "world_chat_buffer_overflow");
            inbox.Attach(HandlePacket);
        }

        private void Update()
        {
            if (network != null && network.IsConnected)
            {
                timeline.CheckTimeout(network.ConnectionEpoch, Time.realtimeSinceStartup);
                sender.CheckTimeout(network.ConnectionEpoch,
                    Time.realtimeSinceStartupAsDouble);
                FlushPrivateAcks();
            }
        }

        public bool TrySendWorldMessage(string content, out string errorCode)
        {
            if (network == null || inbox == null || !network.IsConnected
                || !inbox.IsInWorld || inbox.ConnectionEpoch != network.ConnectionEpoch)
            {
                errorCode = "player_not_in_world";
                return false;
            }

            return sender.TrySendWorld(network.ConnectionEpoch, content,
                Time.realtimeSinceStartupAsDouble,
                request => network.Send(request), out errorCode);
        }

        public bool TrySendPrivateMessage(long targetCharacterDbId,
            string content, out string errorCode)
        {
            if (network == null || inbox == null || !network.IsConnected
                || !inbox.IsInWorld || inbox.ConnectionEpoch != network.ConnectionEpoch)
            {
                errorCode = "player_not_in_world";
                return false;
            }
            if (!privateConversations.IsPersistent)
            {
                errorCode = "private_chat_local_store_unavailable";
                return false;
            }

            return sender.TrySendPrivate(network.ConnectionEpoch,
                targetCharacterDbId, content,
                Time.realtimeSinceStartupAsDouble,
                request => network.Send(request), out errorCode);
        }

        private void HandleConnected(long epoch)
        {
            timeline.Reset(epoch);
            sender.Reset(epoch);
            ResetPrivateAcks(epoch);
            ResetPrivateConversations(epoch);
        }

        private void HandleDisconnected(long epoch, string reason)
        {
            if (timeline.ConnectionEpoch == epoch)
                timeline.Reset(0);
            if (sender.ConnectionEpoch == epoch)
                sender.Reset(0);
            if (privateAcks.ConnectionEpoch == epoch)
                ResetPrivateAcks(0);
            if (privateConversations.ConnectionEpoch == epoch)
                ResetPrivateConversations(0);
        }

        private void HandleWorldEntered(long epoch, long characterDbId)
        {
            timeline.EnterWorld(epoch, Time.realtimeSinceStartup, historyTimeoutSeconds);
            sender.EnterWorld(epoch, characterDbId);
            OpenPrivateConversations(epoch, characterDbId);
        }

        private void OpenPrivateConversations(long epoch, long characterDbId)
        {
            string accountId = AuthSession.Instance == null
                ? null : AuthSession.Instance.UserId;
            try
            {
                var storage = new PrivateChatFileStorage(
                    Application.persistentDataPath, network.ServerHost,
                    network.ServerPort, accountId, characterDbId);
                if (privateConversations.EnterWorld(
                    epoch, characterDbId, storage, false))
                {
                    privateAcks.EnterWorld(epoch, characterDbId);
                    ackWaitingForWorld = false;
                    try { privateConversations.PublishChanged(); }
                    catch (Exception ex)
                    {
                        Debug.LogWarning("[Chat] Private history view update failed: "
                            + ex.GetType().Name);
                    }
                    return;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Chat] Private chat storage unavailable: "
                    + ex.GetType().Name);
            }
            Debug.LogWarning("[Chat] Private chat history could not be opened.");
        }

        private void HandleWorldEntryRejected(long epoch)
        {
            if (timeline.ConnectionEpoch == epoch)
                timeline.Reset(0);
            if (sender.ConnectionEpoch == epoch)
                sender.Reset(0);
            if (privateAcks.ConnectionEpoch == epoch)
                ResetPrivateAcks(0);
            if (privateConversations.ConnectionEpoch == epoch)
                ResetPrivateConversations(0);
        }

        private void HandlePacket(WorldChatInboundPacket packet)
        {
            if (packet.ConnectionEpoch != timeline.ConnectionEpoch) return;

            try
            {
                switch (packet.MsgId)
                {
                    case "chat_message":
                        WorldChatMessage message =
                            JsonUtility.FromJson<WorldChatMessage>(packet.Json);
                        if (message == null || message.msgId != "chat_message"
                            || message.type != "chat_message")
                            break;
                        if (message.channel == "world")
                            timeline.ReceiveRealtime(packet.ConnectionEpoch, message);
                        else if (message.channel == "private"
                            && !string.IsNullOrEmpty(message.messageId)
                            && message.senderCharacterDbId > 0
                            && message.targetCharacterDbId > 0
                            && (message.senderCharacterDbId == sender.CharacterDbId
                                || message.targetCharacterDbId == sender.CharacterDbId))
                        {
                            bool isReceiver = message.targetCharacterDbId
                                == sender.CharacterDbId
                                && message.senderCharacterDbId != sender.CharacterDbId;
                            if (isReceiver
                                && !Guid.TryParseExact(message.messageId, "D", out _))
                            {
                                Debug.LogWarning("[Chat] Private message has an invalid ID.");
                                break;
                            }
                            PrivateChatReceiveResult stored =
                                privateConversations.TryReceive(
                                    packet.ConnectionEpoch, message,
                                    out long partnerId, false);
                            bool queued = isReceiver
                                && (stored == PrivateChatReceiveResult.Added
                                    || stored == PrivateChatReceiveResult.Duplicate)
                                && privateConversations.IsPersistent
                                && privateAcks.QueueReceived(
                                    packet.ConnectionEpoch, message);
                            if (queued && privateAcks.PendingCount == 1
                                && nextAckAttemptAt <= Time.realtimeSinceStartupAsDouble)
                                nextAckAttemptAt =
                                    Time.realtimeSinceStartupAsDouble + 0.05d;
                            Debug.Log("[Chat] Private message id=" + message.messageId
                                + " sender=" + message.senderCharacterDbId
                                + " target=" + message.targetCharacterDbId
                                + " local=" + sender.CharacterDbId
                                + " store=" + stored + " ackQueued=" + queued);
                            if (stored == PrivateChatReceiveResult.StorageFailed)
                                Debug.LogWarning("[Chat] Private message could not be "
                                    + "stored; messageId=" + message.messageId);
                            if (stored == PrivateChatReceiveResult.Added)
                            {
                                try
                                {
                                    privateConversations.PublishChanged(partnerId);
                                    PrivateMessageReceived?.Invoke(message);
                                }
                                catch (Exception ex)
                                {
                                    Debug.LogWarning("[Chat] Private chat view update "
                                        + "failed: " + ex.GetType().Name);
                                }
                            }
                        }
                        break;
                    case "world_chat_history":
                        timeline.ReceiveHistory(packet.ConnectionEpoch,
                            JsonUtility.FromJson<WorldChatHistory>(packet.Json));
                        break;
                    case "chat_send_result":
                        WorldChatSendResult result =
                            JsonUtility.FromJson<WorldChatSendResult>(packet.Json);
                        if (sender.ReceiveResult(packet.ConnectionEpoch, result,
                            Time.realtimeSinceStartupAsDouble))
                            SendResultReceived?.Invoke(result);
                        OperationPacketReceived?.Invoke(packet);
                        break;
                    case "error":
                        ServerErrorMessage error =
                            JsonUtility.FromJson<ServerErrorMessage>(packet.Json);
                        if (error != null)
                        {
                            if (error.requestMsgId == "chat_send")
                                sender.ReceiveGateError(packet.ConnectionEpoch,
                                    error.errorCode, error.message);
                            else if (error.requestMsgId == "chat_private_ack")
                                HandlePrivateAckError(packet.ConnectionEpoch,
                                    error.errorCode);
                        }
                        OperationPacketReceived?.Invoke(packet);
                        break;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[WorldChat] Invalid " + packet.MsgId + ": " + ex.Message);
                if (packet.MsgId == "world_chat_history")
                    timeline.MarkIncomplete(packet.ConnectionEpoch,
                        "world_chat_history_invalid");
            }
        }

        private void ResetPrivateAcks(long epoch)
        {
            privateAcks.Reset(epoch);
            nextAckAttemptAt = 0;
            ackFailures = 0;
            ackWaitingForWorld = false;
        }

        private void ResetPrivateConversations(long epoch)
        {
            privateConversations.Reset(epoch, false);
            try { privateConversations.PublishChanged(); }
            catch (Exception ex)
            {
                Debug.LogWarning("[Chat] Private chat view reset failed: "
                    + ex.GetType().Name);
            }
        }

        private void FlushPrivateAcks()
        {
            if (network == null || inbox == null || !network.IsConnected
                || !inbox.IsInWorld || ackWaitingForWorld
                || !privateConversations.IsPersistent
                || inbox.ConnectionEpoch != network.ConnectionEpoch
                || inbox.CharacterDbId != privateAcks.CharacterDbId
                || Time.realtimeSinceStartupAsDouble < nextAckAttemptAt
                || !privateAcks.TryGetBatch(network.ConnectionEpoch, out string[] ids))
                return;

            bool sent;
            try { sent = network.Send(new PrivateChatAckRequest(ids)); }
            catch (Exception ex)
            {
                Debug.LogWarning("[Chat] Private ACK send failed: "
                    + ex.GetType().Name);
                sent = false;
            }

            if (sent)
            {
                privateAcks.MarkSent(network.ConnectionEpoch, ids);
                ackFailures = 0;
                nextAckAttemptAt = Time.realtimeSinceStartupAsDouble + 0.05d;
                Debug.Log("[Chat] Private ACK sent: count=" + ids.Length);
            }
            else
            {
                DelayPrivateAck();
                Debug.LogWarning("[Chat] Private ACK transport unavailable.");
            }
        }

        private void HandlePrivateAckError(long epoch, string errorCode)
        {
            if (epoch != privateAcks.ConnectionEpoch) return;
            privateAcks.RetryRecent(epoch);
            if (errorCode == "not_authenticated"
                || errorCode == "player_not_in_world")
                ackWaitingForWorld = true;
            DelayPrivateAck();
            Debug.LogWarning("[Chat] Private ACK rejected: "
                + (errorCode ?? "<unknown>"));
        }

        private void DelayPrivateAck()
        {
            ackFailures = Math.Min(ackFailures + 1, 5);
            nextAckAttemptAt = Time.realtimeSinceStartupAsDouble
                + Math.Min(30d, Math.Pow(2d, ackFailures));
        }

        private void Unsubscribe()
        {
            if (inbox != null)
            {
                inbox.Detach(HandlePacket);
                inbox.WorldEntered -= HandleWorldEntered;
                inbox.WorldEntryRejected -= HandleWorldEntryRejected;
            }
            if (network != null)
            {
                network.OnConnectedWithEpoch -= HandleConnected;
                network.OnDisconnectedWithEpoch -= HandleDisconnected;
            }
        }

        private void OnDestroy()
        {
            Unsubscribe();
            if (ReferenceEquals(Instance, this)) Instance = null;
        }
    }
}
