using System;
using System.Collections.Generic;
using UnityEngine;
using Game.Client.BattleSync;

[DefaultExecutionOrder(-100)]
public class NetMessageDispatcher
    : MonoBehaviour,
      Game.Client.BattleSync.IBattleSyncMessageRouter
{
    public static NetMessageDispatcher Instance { get; private set; }

    private readonly Dictionary<string, Action<string>> handlers = new Dictionary<string, Action<string>>();
    private readonly Queue<string> deferredMessages = new Queue<string>();
    private const int MaxDeferredMessages = 512;
    private NetworkManager subscribedNetworkManager;
    private bool deferringWorldMessages;
    private bool deferringMapTransferMessages;

    public event Action DeferredDispatchOverflow;
    public bool HasDeferredWorldOverflow { get; private set; }
    public bool HasNetworkSubscription =>
        subscribedNetworkManager != null
        && subscribedNetworkManager == NetworkManager.Instance;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            enabled = false;
            Destroy(this);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnEnable()
    {
        EnsureNetworkSubscription();
    }

    private void Update()
    {
        EnsureNetworkSubscription();
    }

    private void OnDisable()
    {
        RemoveNetworkSubscription();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void EnsureNetworkSubscription()
    {
        NetworkManager currentNetworkManager = NetworkManager.Instance;

        if (subscribedNetworkManager == currentNetworkManager)
        {
            return;
        }

        RemoveNetworkSubscription();

        if (currentNetworkManager == null)
        {
            return;
        }

        currentNetworkManager.OnMessageReceived += Dispatch;
        subscribedNetworkManager = currentNetworkManager;
    }

    private void RemoveNetworkSubscription()
    {
        if (subscribedNetworkManager == null)
        {
            return;
        }

        subscribedNetworkManager.OnMessageReceived -= Dispatch;
        subscribedNetworkManager = null;
    }

    public void Register(string msgId, Action<string> handler)
    {
        if (string.IsNullOrEmpty(msgId) || handler == null)
        {
            return;
        }

        if (!handlers.ContainsKey(msgId))
        {
            handlers[msgId] = null;
        }

        handlers[msgId] += handler;
    }

    public void Unregister(string msgId, Action<string> handler)
    {
        if (string.IsNullOrEmpty(msgId) || handler == null)
        {
            return;
        }

        if (!handlers.ContainsKey(msgId))
        {
            return;
        }

        handlers[msgId] -= handler;

        if (handlers[msgId] == null)
        {
            handlers.Remove(msgId);
        }
    }

    public void BeginDeferredWorldMessages()
    {
        deferredMessages.Clear();
        deferringWorldMessages = true;
        deferringMapTransferMessages = false;
        HasDeferredWorldOverflow = false;
    }

    public void BeginDeferredMapTransferMessages()
    {
        deferredMessages.Clear();
        deferringWorldMessages = true;
        deferringMapTransferMessages = true;
        HasDeferredWorldOverflow = false;
    }

    public void ClearDeferredWorldMessages()
    {
        deferringWorldMessages = false;
        deferringMapTransferMessages = false;
        deferredMessages.Clear();
        HasDeferredWorldOverflow = false;
    }

    public void ReplayDeferredWorldMessages()
    {
        deferringWorldMessages = false;
        deferringMapTransferMessages = false;
        while (deferredMessages.Count > 0)
        {
            if (NetworkManager.Instance == null || !NetworkManager.Instance.IsConnected)
            {
                deferredMessages.Clear();
                return;
            }

            Dispatch(deferredMessages.Dequeue());
        }
    }

    public void Dispatch(string json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return;
        }

        NetMessageHeader header;

        try
        {
            header = JsonUtility.FromJson<NetMessageHeader>(json);
        }
        catch (Exception ex)
        {
            BattleSyncDiagnosticsLogger.LogDispatch(
                "parse_failed",
                "<unknown>",
                "<unknown>",
                0,
                false,
                false,
                deferredMessages.Count);
            Debug.LogWarning("Dispatch failed, bad json: " + ex.Message);
            return;
        }

        if (header == null || string.IsNullOrEmpty(header.msgId))
        {
            BattleSyncDiagnosticsLogger.LogDispatch(
                "missing_msg_id",
                header == null ? "<unknown>" : header.msgId,
                header == null ? "<unknown>" : header.type,
                header == null ? 0 : header.reliableMessageId,
                false,
                false,
                deferredMessages.Count);
            Debug.LogWarning("Dispatch failed, missing msgId: " + json);
            return;
        }

        bool isWorldChat = (header.msgId == "chat_message" && header.channel == "world")
            || header.msgId == "world_chat_history"
            || header.msgId == "chat_send_result";
        bool isSessionControl = header.msgId == "enter_world_result"
            || header.msgId == "heartbeat_result" || header.msgId == "account_kicked"
            || (header.msgId == "error"
                && (header.requestMsgId == "heartbeat" || header.requestMsgId == "enter_world"));
        if (deferringWorldMessages
            && !isSessionControl
            && !isWorldChat
            && (!deferringMapTransferMessages
                || IsMapTransferWorldMessage(header.msgId)))
        {
            if (deferredMessages.Count >= MaxDeferredMessages)
            {
                BattleSyncDiagnosticsLogger.LogDispatch(
                    "deferred_overflow",
                    header.msgId,
                    header.type,
                    header.reliableMessageId,
                    false,
                    true,
                    deferredMessages.Count);
                ClearDeferredWorldMessages();
                HasDeferredWorldOverflow = true;
                DeferredDispatchOverflow?.Invoke();
                return;
            }

            deferredMessages.Enqueue(json);
            BattleSyncDiagnosticsLogger.LogDispatch(
                "deferred",
                header.msgId,
                header.type,
                header.reliableMessageId,
                true,
                true,
                deferredMessages.Count);
            return;
        }

        if (header.msgId == "inventory_snapshot_result")
        {
            try
            {
                InventorySnapshotLogHeader snapshot =
                    JsonUtility.FromJson<InventorySnapshotLogHeader>(json);
                Debug.Log("[InventorySnapshot][RX] type=" + (snapshot.type ?? "<missing>")
                    + " protocolVersion=" + snapshot.equipmentProtocolVersion
                    + " catalogVersion=" + snapshot.equipmentCatalogVersion
                    + " requestSequence=" + snapshot.requestSequence
                    + " success=" + snapshot.success
                    + " capacity=" + snapshot.capacity
                    + " errorCode=" + (string.IsNullOrEmpty(snapshot.errorCode)
                        ? "<empty>" : snapshot.errorCode));
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[InventorySnapshot][RX] header parse failed: " + ex.Message);
            }
        }

        if (!handlers.TryGetValue(header.msgId, out Action<string> handler) || handler == null)
        {
            BattleSyncDiagnosticsLogger.LogDispatch(
                "no_handler",
                header.msgId,
                header.type,
                header.reliableMessageId,
                false,
                false,
                deferredMessages.Count);
            Debug.LogWarning("No handler registered for msgId: " + header.msgId
                + (header.reliableMessageId > 0
                    ? " reliableMessageId=" + header.reliableMessageId + " (not acknowledged)"
                    : string.Empty));
            return;
        }

        BattleSyncDiagnosticsLogger.LogDispatch(
            "handler",
            header.msgId,
            header.type,
            header.reliableMessageId,
            true,
            false,
            deferredMessages.Count);

        try
        {
            handler.Invoke(json);
        }
        catch (Exception exception)
        {
            BattleSyncDiagnosticsLogger.LogDispatchException(
                header.msgId,
                header.reliableMessageId,
                exception.Message);
            throw;
        }
    }

    private static bool IsMapTransferWorldMessage(string msgId)
    {
        switch (msgId)
        {
            case "world_snapshot":
            case "monster_snapshot":
            case "loot_snapshot":
            case "combat_snapshot":
            case "state_frame":
            case "player_enter_broadcast":
            case "player_leave_broadcast":
            case "monster_enter":
            case "monster_upsert":
            case "monster_remove":
            case "loot_upsert":
            case "loot_remove":
            case "combat_event":
            case "combat_event_batch":
            case "player_authoritative_state":
            case "movement_control":
            case "movement_control_result":
            case "movement_correction":
            case "player_movement_event":
            case "player_state_broadcast":
            case "player_state_frame":
            case "monster_state":
            case "monster_state_frame":
            case "combat_state_upsert":
            case "skill_cast_event":
                return true;
            default:
                return false;
        }
    }

    [Serializable]
    private class NetMessageHeader
    {
        public string msgId;
        public string type;
        public string channel;
        public string requestMsgId;
        public long reliableMessageId;
    }

    [Serializable]
    private class InventorySnapshotLogHeader
    {
        public string type;
        public int equipmentProtocolVersion;
        public int equipmentCatalogVersion;
        public long requestSequence;
        public bool success;
        public int capacity;
        public string errorCode;
    }
}
