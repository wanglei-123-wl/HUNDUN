using System;
using Game.Friend.Model;
using Game.Friend.Protocol;
using Game.Friend.Transport;
using UnityEngine;

namespace Game.Friend.Runtime
{
    [DisallowMultipleComponent]
    public sealed class FriendSessionRuntime : MonoBehaviour
    {
        private readonly FriendRequestQueue requests = new FriendRequestQueue();
        private readonly FriendState state = new FriendState();
        private NetworkManager network;
        private NetMessageDispatcher dispatcher;
        private GameSession session;
        private FriendTransportInbox inbox;
        private long enteredEpoch;

        public static FriendSessionRuntime Instance { get; private set; }
        public FriendRequestQueue Requests => requests;
        public FriendState State => state;
        public event Action<FriendSearchResult> SearchCompleted;
        public event Action<FriendSendResult> SendCompleted;
        public event Action<FriendOperationResult> OperationCompleted;

        public static FriendSessionRuntime Ensure(NetworkManager network,
            NetMessageDispatcher dispatcher, GameSession session)
        {
            if (network == null || dispatcher == null || session == null)
                throw new ArgumentNullException(network == null ? nameof(network)
                    : dispatcher == null ? nameof(dispatcher) : nameof(session));

            FriendSessionRuntime runtime = network.GetComponent<FriendSessionRuntime>();
            if (runtime == null)
                runtime = network.gameObject.AddComponent<FriendSessionRuntime>();
            runtime.Initialize(network, dispatcher, session);
            Instance = runtime;
            return runtime;
        }

        public void DetachSession(GameSession owner)
        {
            if (!ReferenceEquals(session, owner)) return;
            session.LocalWorldEntered -= HandleWorldEntered;
            session = null;
        }

        private void Initialize(NetworkManager activeNetwork,
            NetMessageDispatcher activeDispatcher, GameSession activeSession)
        {
            if (!ReferenceEquals(network, activeNetwork)
                || !ReferenceEquals(dispatcher, activeDispatcher))
            {
                Unsubscribe();
                network = activeNetwork;
                dispatcher = activeDispatcher;
                requests.Reset(network.ConnectionEpoch);
                state.Reset(network.ConnectionEpoch);
                enteredEpoch = 0;
                network.OnConnectedWithEpoch += HandleConnected;
                network.OnDisconnectedWithEpoch += HandleDisconnected;
                inbox = new FriendTransportInbox(network, dispatcher, HandlePacket);
            }

            if (ReferenceEquals(session, activeSession)) return;
            if (session != null)
                session.LocalWorldEntered -= HandleWorldEntered;
            session = activeSession;
            session.LocalWorldEntered += HandleWorldEntered;
        }

        private void HandleConnected(long epoch)
        {
            enteredEpoch = 0;
            requests.Reset(epoch);
            state.Reset(epoch);
        }

        private void HandleDisconnected(long epoch, string reason)
        {
            if (epoch != requests.ConnectionEpoch) return;
            enteredEpoch = 0;
            requests.Reset(0);
            state.Reset(0);
        }

        private void HandleWorldEntered(long epoch, long characterDbId)
        {
            if (network == null || !network.IsConnected
                || epoch != network.ConnectionEpoch || enteredEpoch == epoch
                || !requests.EnterWorld(epoch, characterDbId)
                || !state.EnterWorld(epoch, characterDbId))
                return;

            enteredEpoch = epoch;
            RequestList();
        }

        public bool RequestList()
        {
            if (!CanSend() || !requests.RequestList()) return false;
            Pump();
            return true;
        }

        public bool Search(string keyword)
        {
            if (!CanSend() || !state.HasSnapshot || !requests.Search(keyword))
                return false;
            Pump();
            return true;
        }

        public bool SendRequest(long targetCharacterDbId, string message = "")
        {
            if (!CanSend() || !state.HasSnapshot
                || !requests.Send(targetCharacterDbId, message))
                return false;
            Pump();
            return true;
        }

        public bool HandleRequest(string requestId, bool accept)
        {
            if (!CanSend() || !state.HasSnapshot
                || !requests.Handle(requestId, accept))
                return false;
            Pump();
            return true;
        }

        private bool CanSend()
        {
            return network != null && network.IsConnected
                && enteredEpoch == network.ConnectionEpoch
                && enteredEpoch > 0 && state.CharacterDbId > 0;
        }

        private void Pump()
        {
            if (!CanSend()) return;
            requests.TrySendNext(network.ConnectionEpoch, Time.realtimeSinceStartupAsDouble,
                request =>
                {
                    if (request is FriendListRequest)
                        state.BeginSnapshot(network.ConnectionEpoch, request.requestSequence);
                    bool sent = network.Send(request);
                    if (!sent && request is FriendListRequest)
                        state.AbortSnapshot(network.ConnectionEpoch, request.requestSequence);
                    return sent;
                });
        }

        private void Update()
        {
            if (network == null) return;
            bool wasRecoverable = !requests.NeedsRecovery;
            requests.CheckTimeout(network.ConnectionEpoch, Time.realtimeSinceStartupAsDouble);
            if (wasRecoverable && requests.NeedsRecovery
                && requests.Pending is FriendListRequest)
                state.AbortSnapshot(network.ConnectionEpoch,
                    requests.Pending.requestSequence);
        }

        private void HandlePacket(long epoch, string msgId, string json)
        {
            if (network == null || epoch <= 0 || epoch != network.ConnectionEpoch
                || epoch != requests.ConnectionEpoch)
                return;

            try
            {
                switch (msgId)
                {
                    case "friend_list_result":
                    {
                        FriendListResult result = JsonUtility.FromJson<FriendListResult>(json);
                        if (!MatchesPending<FriendListRequest>(result?.requestSequence))
                            return;
                        if (!state.ApplySnapshot(epoch, result))
                        {
                            requests.MarkUncertain("friend_list_invalid");
                            state.AbortSnapshot(epoch, requests.Pending.requestSequence);
                            return;
                        }
                        requests.Complete(epoch, "friend_list", result.requestSequence, true, null);
                        if (state.NeedsRefresh) requests.RequestList();
                        Pump();
                        break;
                    }
                    case "friend_search_result":
                    {
                        FriendSearchResult result = JsonUtility.FromJson<FriendSearchResult>(json);
                        if (!MatchesPending<FriendSearchRequest>(result?.requestSequence))
                            return;
                        if (!Valid(result?.msgId, result?.type, msgId)
                            || result.success && result.players == null)
                        {
                            requests.MarkUncertain("friend_search_invalid");
                            return;
                        }
                        if (!requests.Complete(epoch, "friend_search",
                                result.requestSequence, result.success, result.errorCode))
                            return;
                        SearchCompleted?.Invoke(result);
                        Pump();
                        break;
                    }
                    case "friend_request_send_result":
                    {
                        FriendSendResult result = JsonUtility.FromJson<FriendSendResult>(json);
                        if (!MatchesPending<FriendSendRequest>(result?.requestSequence))
                            return;
                        FriendSendRequest sentRequest = (FriendSendRequest)requests.Pending;
                        if (!Valid(result?.msgId, result?.type, msgId)
                            || result.success
                                && !Guid.TryParseExact(result.requestId, "D", out _))
                        {
                            requests.MarkUncertain("friend_send_invalid");
                            return;
                        }
                        if (!requests.Complete(epoch, "friend_request_send",
                                result.requestSequence, result.success, result.errorCode))
                            return;
                        if (result.success)
                            state.RecordSentRequest(epoch,
                                sentRequest.targetCharacterDbId, result.requestId);
                        SendCompleted?.Invoke(result);
                        Pump();
                        break;
                    }
                    case "friend_operation_result":
                    {
                        FriendOperationResult result =
                            JsonUtility.FromJson<FriendOperationResult>(json);
                        if (result == null || requests.Pending == null
                            || result.requestSequence != requests.Pending.requestSequence
                            || result.requestMsgId != requests.Pending.msgId)
                            return;
                        if (!Valid(result.msgId, result.type, msgId)
                            || (requests.Pending is FriendListRequest && result.success))
                        {
                            requests.MarkUncertain("friend_operation_invalid");
                            return;
                        }
                        bool wasList = requests.Pending is FriendListRequest;
                        if (!requests.Complete(epoch, result.requestMsgId,
                                result.requestSequence, result.success, result.errorCode))
                            return;
                        if (wasList) state.AbortSnapshot(epoch, result.requestSequence);
                        OperationCompleted?.Invoke(result);
                        Pump();
                        break;
                    }
                    case "friend_request_received":
                        state.Receive(epoch, JsonUtility.FromJson<FriendReceived>(json));
                        break;
                    case "friend_request_handled":
                        state.Handle(epoch, JsonUtility.FromJson<FriendHandled>(json));
                        break;
                    case "friend_list_delta":
                        state.ApplyDelta(epoch, JsonUtility.FromJson<FriendListDelta>(json));
                        break;
                    case "error":
                    {
                        FriendGateError error = JsonUtility.FromJson<FriendGateError>(json);
                        FriendRequestMessage pending = requests.Pending;
                        if (pending != null && error != null
                            && requests.ReceiveGateError(epoch, error.requestMsgId,
                                error.errorCode)
                            && pending is FriendListRequest)
                            state.AbortSnapshot(epoch, pending.requestSequence);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Friend] Invalid " + msgId + ": " + ex.Message);
                if (msgId == "friend_list_result" || msgId == "friend_search_result"
                    || msgId == "friend_request_send_result"
                    || msgId == "friend_operation_result")
                {
                    requests.MarkUncertain("friend_response_invalid");
                    if (requests.Pending is FriendListRequest)
                        state.AbortSnapshot(epoch, requests.Pending.requestSequence);
                }
            }
        }

        private bool MatchesPending<T>(long? sequence) where T : FriendRequestMessage
        {
            return sequence.HasValue && sequence.Value > 0
                && requests.Pending is T
                && requests.Pending.requestSequence == sequence.Value;
        }

        private static bool Valid(string id, string type, string expected)
        {
            return id == expected && type == expected;
        }

        private void Unsubscribe()
        {
            if (session != null)
                session.LocalWorldEntered -= HandleWorldEntered;
            session = null;
            inbox?.Dispose();
            inbox = null;
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
