using System;
using Game.Friend.Protocol;
using UnityEngine;

namespace Game.Friend.Transport
{
    public sealed class FriendTransportInbox : IDisposable
    {
        private static readonly string[] MessageIds =
        {
            "friend_list_result", "friend_search_result",
            "friend_request_send_result", "friend_operation_result",
            "friend_request_received", "friend_request_handled", "friend_list_delta"
        };

        private readonly NetworkManager network;
        private readonly NetMessageDispatcher dispatcher;
        private readonly Action<long, string, string> receiver;
        private bool disposed;

        public FriendTransportInbox(NetworkManager network,
            NetMessageDispatcher dispatcher, Action<long, string, string> receiver)
        {
            this.network = network ?? throw new ArgumentNullException(nameof(network));
            this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            this.receiver = receiver ?? throw new ArgumentNullException(nameof(receiver));
            foreach (string id in MessageIds)
                dispatcher.Register(id, Receive);
            dispatcher.Register("error", ReceiveError);
        }

        private void Receive(string json)
        {
            if (!CanReceive()) return;
            try
            {
                NetMessageBase header = JsonUtility.FromJson<NetMessageBase>(json);
                if (header != null && !string.IsNullOrEmpty(header.msgId))
                    receiver(network.ConnectionEpoch, header.msgId, json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Friend] Invalid packet header: " + ex.Message);
            }
        }

        private void ReceiveError(string json)
        {
            if (!CanReceive()) return;
            try
            {
                FriendGateError error = JsonUtility.FromJson<FriendGateError>(json);
                if (error != null && !string.IsNullOrEmpty(error.requestMsgId)
                    && (error.requestMsgId == "friend_list"
                        || error.requestMsgId == "friend_search"
                        || error.requestMsgId == "friend_request_send"
                        || error.requestMsgId == "friend_request_accept"
                        || error.requestMsgId == "friend_request_reject"
                        || error.requestMsgId == "friend_remove"
                        || error.requestMsgId == "block_add"
                        || error.requestMsgId == "block_remove"))
                    receiver(network.ConnectionEpoch, "error", json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Friend] Invalid Gate error: " + ex.Message);
            }
        }

        private bool CanReceive()
        {
            return !disposed && network.IsConnected && network.ConnectionEpoch > 0;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (string id in MessageIds)
                dispatcher.Unregister(id, Receive);
            dispatcher.Unregister("error", ReceiveError);
        }
    }
}
