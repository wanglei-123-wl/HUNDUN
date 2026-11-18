using System;
using Game.CharacterPanel.Protocol;
using Game.Inventory;
using UnityEngine;

namespace Game.CharacterPanel.Runtime
{
    [DisallowMultipleComponent]
    public sealed class JingjieBreakthroughRuntime : MonoBehaviour
    {
        [SerializeField, Min(1f)] private float requestTimeoutSeconds = 10f;

        private NetworkManager network;
        private NetMessageDispatcher dispatcher;
        private GameSession session;
        private long enteredEpoch;
        private bool timeoutReported;
        private int retryAttempts;
        private float nextRecoveryAt;

        public static JingjieBreakthroughRuntime Instance { get; private set; }
        public event Action RequestTimedOut;
        public event Action<JingjieBreakthroughResultDto> ResultConfirmed;
        public event Action<string> RequestRejected;
        public bool HasUnresolvedOperation =>
            JingjieBreakthroughRequestSequence.HasPending;

        public static JingjieBreakthroughRuntime Ensure(NetworkManager network,
            GameSession session)
        {
            if (network == null || session == null)
                throw new ArgumentNullException(network == null
                    ? nameof(network) : nameof(session));

            JingjieBreakthroughRuntime runtime =
                network.GetComponent<JingjieBreakthroughRuntime>();
            if (runtime == null)
                runtime = network.gameObject.AddComponent<JingjieBreakthroughRuntime>();
            runtime.Initialize(network, session);
            Instance = runtime;
            return runtime;
        }

        private void Initialize(NetworkManager activeNetwork,
            GameSession activeSession)
        {
            NetMessageDispatcher activeDispatcher = NetMessageDispatcher.Instance;
            if (dispatcher != activeDispatcher)
            {
                if (dispatcher != null)
                {
                    dispatcher.Unregister(JingjieBreakthroughMessageIds.Result,
                        HandleResult);
                    dispatcher.Unregister("error", HandleServerError);
                }
                dispatcher = activeDispatcher;
                if (dispatcher != null)
                {
                    dispatcher.Register(JingjieBreakthroughMessageIds.Result,
                        HandleResult);
                    dispatcher.Register("error", HandleServerError);
                }
            }
            if (network != activeNetwork)
            {
                if (network != null)
                    network.OnDisconnectedWithEpoch -= HandleDisconnected;
                network = activeNetwork;
                network.OnDisconnectedWithEpoch += HandleDisconnected;
                enteredEpoch = 0;
                JingjieBreakthroughRequestSequence.Suspend();
            }
            session = activeSession;
        }

        public void BeginSession(GameSession owner, long characterDbId,
            string operationSessionId)
        {
            if (session != owner || network == null || !network.IsConnected
                || characterDbId <= 0)
                return;

            JingjieBreakthroughRequestSequence.EnterWorld(
                operationSessionId, characterDbId);
            enteredEpoch = network.ConnectionEpoch;
            timeoutReported = false;
            retryAttempts = 0;
            nextRecoveryAt = 0f;
        }

        public void SuspendSession(GameSession owner)
        {
            if (session != owner)
                return;
            enteredEpoch = 0;
            timeoutReported = false;
            retryAttempts = 0;
            JingjieBreakthroughRequestSequence.Suspend();
        }

        public bool TryBreakthrough()
        {
            if (!CanSend()
                || !JingjieBreakthroughRequestSequence.TryPrepare(
                    out JingjieBreakthroughRequestDto request))
                return false;

            timeoutReported = false;
            retryAttempts = 0;
            if (network.Send(request))
                return true;

            // A failed network write has an unknown server outcome.
            Debug.LogWarning("[Jingjie] Breakthrough send was not confirmed; "
                + "the request ID remains pending.");
            return false;
        }

        public bool TryRetryTimedOut()
        {
            if (!CanSend())
                return false;

            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long timeout = (long)(requestTimeoutSeconds * 1000f);
            if (!JingjieBreakthroughRequestSequence.TryGetSameSessionRetry(
                now, timeout, out string requestJson))
                return false;

            if (!network.SendJson(requestJson))
                return false;

            JingjieBreakthroughRequestSequence.MarkRetrySent(now);
            timeoutReported = false;
            return true;
        }

        private bool CanSend()
        {
            return session != null && network != null && network.IsConnected
                && enteredEpoch > 0 && enteredEpoch == network.ConnectionEpoch
                && session.IsWorldReady
                && session.LocalCharacterDbId > 0
                && !string.IsNullOrWhiteSpace(
                    session.JingjieOperationSessionId);
        }

        private void HandleResult(string json)
        {
            if (!CanSend())
                return;
            JingjieBreakthroughResultDto result;
            try { result = JsonUtility.FromJson<JingjieBreakthroughResultDto>(json); }
            catch (Exception exception)
            {
                Debug.LogWarning("[Jingjie] Invalid breakthrough result: "
                    + exception.Message);
                return;
            }

            if (result == null
                || result.msgId != JingjieBreakthroughMessageIds.Result
                || result.type != JingjieBreakthroughMessageIds.Result
                || !JingjieBreakthroughRequestSequence.Matches(
                    session.LocalCharacterDbId, result.jingjieOperationSessionId,
                    result.requestSequence, result.clientRequestId)
                || result.characterDbId != session.LocalCharacterDbId)
                return;

            if (result.success && !result.idempotentReplay
                && (string.IsNullOrWhiteSpace(result.jingjieId)
                || string.IsNullOrWhiteSpace(result.jingjieName)
                || result.jingjieStage < 0 || result.jingjieExp < 0
                || result.requiredJingjieExp < 0 || result.totalJingjieExp < 0
                || !JingjieNextRealmFields.IsValid(result.hasNextJingjie,
                    result.nextJingjieId, result.nextJingjieStage,
                    result.nextJingjieName)))
            {
                session.QueueProgressionSnapshotRefresh();
                Debug.LogWarning("[Jingjie] Success result has invalid realm fields; "
                    + "the operation remains unresolved.");
                return;
            }

            if (result.success)
            {
                InventoryModuleRuntime inventory =
                    FindObjectOfType<InventoryModuleRuntime>();
                if (inventory == null || !inventory.TryInitialize())
                {
                    Debug.LogWarning("[Jingjie] Inventory module is not ready; "
                        + "the operation remains unresolved.");
                    return;
                }

                if (result.idempotentReplay || result.requiresInventorySnapshot)
                    inventory.RequestReconciliation();
                else if (!inventory.ApplyJingjieBreakthroughInventoryChanges(
                    result.inventoryRevision, false, result.stackItemChanges))
                    inventory.RequestReconciliation();
            }

            session.ReconcileJingjieBreakthrough(result);
            if (!result.success && (result.errorCode == "jingjie_operation_busy"
                || result.errorCode == "jingjie_persist_failed"
                || result.errorCode == "jingjie_operation_conflict"))
            {
                if (result.errorCode == "jingjie_operation_conflict")
                    timeoutReported = true;
                else
                {
                    JingjieBreakthroughRequestSequence.MarkRetrySent(
                        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                    timeoutReported = false;
                }
                RequestRejected?.Invoke(result.errorCode);
                return;
            }

            if (!JingjieBreakthroughRequestSequence.Complete(
                result.jingjieOperationSessionId, result.requestSequence,
                result.clientRequestId))
                return;
            timeoutReported = false;
            ResultConfirmed?.Invoke(result);
        }

        private void HandleServerError(string json)
        {
            if (!CanSend() || !JingjieBreakthroughRequestSequence.HasPending)
                return;
            ServerErrorMessage error;
            try { error = JsonUtility.FromJson<ServerErrorMessage>(json); }
            catch (Exception) { return; }
            if (error == null || error.requestMsgId != JingjieBreakthroughMessageIds.Request)
                return;

            timeoutReported = true;
            session.RequestProgressionSnapshot();
            RequestRejected?.Invoke(error.errorCode);
            Debug.LogWarning("[Jingjie] Gate rejected breakthrough: "
                + (error.errorCode ?? error.message));
        }

        private void Update()
        {
            if (!CanSend() || !HasUnresolvedOperation)
                return;
            if (Time.unscaledTime >= nextRecoveryAt
                && JingjieBreakthroughRequestSequence.TryGetRecoveryRequest(
                    out JingjieBreakthroughRequestDto recovered))
            {
                nextRecoveryAt = Time.unscaledTime + 1f;
                if (network.Send(recovered))
                {
                    JingjieBreakthroughRequestSequence.MarkRecoverySent(
                        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                    timeoutReported = false;
                }
                return;
            }
            if (timeoutReported)
                return;
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (!JingjieBreakthroughRequestSequence.IsTimedOut(now,
                    (long)(requestTimeoutSeconds * 1000f)))
                return;
            if (retryAttempts < 2 && TryRetryTimedOut())
            {
                retryAttempts++;
                return;
            }
            timeoutReported = true;
            RequestTimedOut?.Invoke();
        }

        private void HandleDisconnected(long epoch, string reason)
        {
            if (enteredEpoch != epoch)
                return;
            enteredEpoch = 0;
            timeoutReported = false;
            retryAttempts = 0;
            JingjieBreakthroughRequestSequence.Suspend();
        }

        private void OnDestroy()
        {
            if (network != null)
                network.OnDisconnectedWithEpoch -= HandleDisconnected;
            if (dispatcher != null)
            {
                dispatcher.Unregister(JingjieBreakthroughMessageIds.Result,
                    HandleResult);
                dispatcher.Unregister("error", HandleServerError);
            }
            if (Instance != this)
                return;
            Instance = null;
            JingjieBreakthroughRequestSequence.Suspend();
        }
    }
}
