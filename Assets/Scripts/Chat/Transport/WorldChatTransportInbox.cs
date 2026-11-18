using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Chat.Transport
{
    public sealed class WorldChatInboundPacket
    {
        public long ConnectionEpoch { get; }
        public string MsgId { get; }
        public string Json { get; }

        public WorldChatInboundPacket(long connectionEpoch, string msgId, string json)
        {
            ConnectionEpoch = connectionEpoch;
            MsgId = msgId;
            Json = json;
        }
    }

    public sealed class WorldChatTransportInbox : IDisposable
    {
        [Serializable]
        private sealed class ChatChannelHeader
        {
            public string channel = string.Empty;
        }

        private const int MaxUnclaimedPackets = 2048;

        private readonly NetworkManager network;
        private readonly NetMessageDispatcher dispatcher;
        private readonly Queue<WorldChatInboundPacket> unclaimed =
            new Queue<WorldChatInboundPacket>();

        private Action<WorldChatInboundPacket> receiver;
        private long connectionEpoch;
        private bool awaitingWorld;
        private bool disposed;

        public static WorldChatTransportInbox Instance { get; private set; }
        public long ConnectionEpoch => connectionEpoch;
        public long CharacterDbId { get; private set; }
        public bool IsInWorld { get; private set; }
        public bool HasBufferOverflow { get; private set; }
        public event Action<long, long> WorldEntered;
        public event Action<long> WorldEntryRejected;

        private WorldChatTransportInbox(NetworkManager network, NetMessageDispatcher dispatcher)
        {
            this.network = network;
            this.dispatcher = dispatcher;
            connectionEpoch = network.ConnectionEpoch;
            network.OnConnectedWithEpoch += HandleConnected;
            network.OnDisconnectedWithEpoch += HandleDisconnected;
            dispatcher.Register("enter_world_result", HandleEnterWorldResult);
            dispatcher.Register("chat_message", HandleChatMessage);
            dispatcher.Register("world_chat_history", HandleWorldChatHistory);
            dispatcher.Register("chat_send_result", HandleChatSendResult);
            dispatcher.Register("error", HandleServerError);
        }

        public static WorldChatTransportInbox Ensure(
            NetworkManager network, NetMessageDispatcher dispatcher)
        {
            if (network == null || dispatcher == null)
                throw new ArgumentNullException(
                    network == null ? nameof(network) : nameof(dispatcher));

            if (Instance != null
                && !Instance.disposed
                && ReferenceEquals(Instance.network, network)
                && ReferenceEquals(Instance.dispatcher, dispatcher))
                return Instance;

            Instance?.Dispose();
            Instance = new WorldChatTransportInbox(network, dispatcher);
            return Instance;
        }

        public void PrepareEnterWorld()
        {
            connectionEpoch = network.ConnectionEpoch;
            awaitingWorld = true;
            IsInWorld = false;
            CharacterDbId = 0;
            HasBufferOverflow = false;
            unclaimed.Clear();
        }

        public void Attach(Action<WorldChatInboundPacket> onPacket)
        {
            if (disposed) throw new ObjectDisposedException(nameof(WorldChatTransportInbox));
            if (onPacket == null) throw new ArgumentNullException(nameof(onPacket));
            if (receiver != null && receiver != onPacket)
                throw new InvalidOperationException("World chat already has a packet receiver.");

            receiver = onPacket;
            while (unclaimed.Count > 0)
                receiver(unclaimed.Dequeue());
        }

        public void Detach(Action<WorldChatInboundPacket> onPacket)
        {
            if (receiver == onPacket) receiver = null;
        }

        private void HandleConnected(long epoch)
        {
            if (disposed || epoch <= 0) return;
            connectionEpoch = epoch;
            IsInWorld = false;
            CharacterDbId = 0;
            HasBufferOverflow = false;
            unclaimed.Clear();
        }

        private void HandleDisconnected(long epoch, string reason)
        {
            if (disposed || epoch != connectionEpoch) return;
            awaitingWorld = false;
            IsInWorld = false;
            CharacterDbId = 0;
            unclaimed.Clear();
        }

        private void HandleEnterWorldResult(string json)
        {
            if (!CanReceive()) return;

            EnterWorldResult result;
            try { result = JsonUtility.FromJson<EnterWorldResult>(json); }
            catch (Exception ex)
            {
                Debug.LogWarning("[WorldChat] Invalid enter_world_result: " + ex.Message);
                return;
            }

            if (result == null || !result.success)
            {
                awaitingWorld = false;
                IsInWorld = false;
                CharacterDbId = 0;
                unclaimed.Clear();
                WorldEntryRejected?.Invoke(connectionEpoch);
                return;
            }

            long receivedCharacterDbId = result.characterDbId > 0
                ? result.characterDbId
                : result.self == null ? 0 : result.self.characterDbId;
            if (IsInWorld && CharacterDbId != receivedCharacterDbId)
            {
                awaitingWorld = false;
                IsInWorld = false;
                CharacterDbId = 0;
                unclaimed.Clear();
                WorldEntryRejected?.Invoke(connectionEpoch);
                Debug.LogWarning("[WorldChat] Character changed on an active connection.");
                return;
            }

            CharacterDbId = receivedCharacterDbId;
            IsInWorld = CharacterDbId > 0;
            if (IsInWorld)
                WorldEntered?.Invoke(connectionEpoch, CharacterDbId);
            else
            {
                awaitingWorld = false;
                unclaimed.Clear();
                WorldEntryRejected?.Invoke(connectionEpoch);
            }
        }

        private void HandleChatMessage(string json)
        {
            ChatChannelHeader header;
            try { header = JsonUtility.FromJson<ChatChannelHeader>(json); }
            catch (Exception ex)
            {
                Debug.LogWarning("[WorldChat] Invalid chat_message: " + ex.Message);
                return;
            }

            if (header == null || (header.channel != "world"
                && header.channel != "private")) return;
            Enqueue("chat_message", json);
        }

        private void HandleWorldChatHistory(string json)
        {
            Enqueue("world_chat_history", json);
        }

        private void HandleChatSendResult(string json)
        {
            Enqueue("chat_send_result", json);
        }

        private void HandleServerError(string json)
        {
            if (!CanReceive()) return;

            ServerErrorMessage error;
            try { error = JsonUtility.FromJson<ServerErrorMessage>(json); }
            catch (Exception ex)
            {
                Debug.LogWarning("[WorldChat] Invalid error response: " + ex.Message);
                return;
            }

            if (error != null && (error.requestMsgId == "chat_send"
                || error.requestMsgId == "chat_private_ack"))
                Enqueue("error", json);
        }

        private bool CanReceive()
        {
            return !disposed && awaitingWorld && network.IsConnected
                && connectionEpoch > 0
                && connectionEpoch == network.ConnectionEpoch;
        }

        private void Enqueue(string msgId, string json)
        {
            if (!CanReceive()) return;

            var packet = new WorldChatInboundPacket(connectionEpoch, msgId, json);
            if (receiver != null)
            {
                receiver(packet);
                return;
            }

            if (unclaimed.Count >= MaxUnclaimedPackets)
            {
                unclaimed.Dequeue();
                if (!HasBufferOverflow)
                    Debug.LogError("[WorldChat] Early message buffer overflowed.");
                HasBufferOverflow = true;
            }

            unclaimed.Enqueue(packet);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            dispatcher.Unregister("enter_world_result", HandleEnterWorldResult);
            dispatcher.Unregister("chat_message", HandleChatMessage);
            dispatcher.Unregister("world_chat_history", HandleWorldChatHistory);
            dispatcher.Unregister("chat_send_result", HandleChatSendResult);
            dispatcher.Unregister("error", HandleServerError);
            network.OnConnectedWithEpoch -= HandleConnected;
            network.OnDisconnectedWithEpoch -= HandleDisconnected;
            receiver = null;
            WorldEntered = null;
            WorldEntryRejected = null;
            unclaimed.Clear();
            if (ReferenceEquals(Instance, this)) Instance = null;
        }
    }
}
