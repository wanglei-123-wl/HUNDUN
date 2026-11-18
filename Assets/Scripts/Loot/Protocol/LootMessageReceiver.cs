using System;
using UnityEngine;

public sealed class LootMessageReceiver : IDisposable
{
    private readonly NetMessageDispatcher dispatcher;
    private bool initialized;

    public event Action<LootSnapshotMessageDto> SnapshotReceived;
    public event Action<LootUpsertMessageDto> UpsertReceived;
    public event Action<LootRemoveMessageDto> RemoveReceived;
    public event Action<LootPickupResultMessageDto> PickupResultReceived;
    public event Action<InventorySnapshotResultMessageDto> InventorySnapshotReceived;
    public event Action<NetErrorMessageDto> ProtocolErrorReceived;

    public LootMessageReceiver(NetMessageDispatcher dispatcher)
    {
        this.dispatcher = dispatcher
            ?? throw new ArgumentNullException(nameof(dispatcher));
    }

    public void Initialize(bool registerLootSnapshot = true,bool registerInventorySnapshot = true,
        bool registerReliableStructures = true)
    {
        if (initialized)
        {
            return;
        }

        if (registerLootSnapshot) dispatcher.Register(LootProtocolConstants.Snapshot, HandleSnapshot);
        if(registerReliableStructures)
        {
            dispatcher.Register(LootProtocolConstants.Upsert, HandleUpsert);
            dispatcher.Register(LootProtocolConstants.Remove, HandleRemove);
        }
        dispatcher.Register(LootProtocolConstants.PickupResult, HandlePickupResult);
        if (registerInventorySnapshot) dispatcher.Register(LootProtocolConstants.InventorySnapshotResult, HandleInventorySnapshotResult);
        dispatcher.Register(LootProtocolConstants.Error, HandleProtocolError);
        initialized = true;
    }

    public void Dispose()
    {
        if (initialized)
        {
            dispatcher.Unregister(LootProtocolConstants.Snapshot, HandleSnapshot);
            dispatcher.Unregister(LootProtocolConstants.Upsert, HandleUpsert);
            dispatcher.Unregister(LootProtocolConstants.Remove, HandleRemove);
            dispatcher.Unregister(LootProtocolConstants.PickupResult, HandlePickupResult);
            dispatcher.Unregister(LootProtocolConstants.InventorySnapshotResult, HandleInventorySnapshotResult);
            dispatcher.Unregister(LootProtocolConstants.Error, HandleProtocolError);
            initialized = false;
        }

        SnapshotReceived = null;
        UpsertReceived = null;
        RemoveReceived = null;
        PickupResultReceived = null;
        InventorySnapshotReceived = null;
        ProtocolErrorReceived = null;
    }

    private void HandleSnapshot(string json)
    {
        if (!TryParse(json, LootProtocolConstants.Snapshot, out LootSnapshotMessageDto message))
        {
            return;
        }

        InvokeSafely(SnapshotReceived, message, LootProtocolConstants.Snapshot);
    }

    private void HandleUpsert(string json)
    {
        if (!TryParse(json, LootProtocolConstants.Upsert, out LootUpsertMessageDto message)
            || message.reliableMessageId <= 0)
        {
            return;
        }

        InvokeSafely(UpsertReceived, message, LootProtocolConstants.Upsert);
    }

    private void HandleRemove(string json)
    {
        if (!TryParse(json, LootProtocolConstants.Remove, out LootRemoveMessageDto message)
            || message.reliableMessageId <= 0)
        {
            return;
        }

        InvokeSafely(RemoveReceived, message, LootProtocolConstants.Remove);
    }

    private void HandlePickupResult(string json)
    {
        if (!TryParse(
                json,
                LootProtocolConstants.PickupResult,
                out LootPickupResultMessageDto message))
        {
            return;
        }

        InvokeSafely(PickupResultReceived, message, LootProtocolConstants.PickupResult);
    }

    private void HandleInventorySnapshotResult(string json)
    {
        if (!TryParse(
                json,
                LootProtocolConstants.InventorySnapshotResult,
                out InventorySnapshotResultMessageDto message))
        {
            return;
        }

        InvokeSafely(
            InventorySnapshotReceived,
            message,
            LootProtocolConstants.InventorySnapshotResult);
    }

    private void HandleProtocolError(string json)
    {
        if (!TryParse(json, LootProtocolConstants.Error, out NetErrorMessageDto message)
            || !string.Equals(
                message.requestMsgId,
                LootProtocolConstants.Pickup,
                StringComparison.Ordinal))
        {
            return;
        }

        InvokeSafely(ProtocolErrorReceived, message, LootProtocolConstants.Error);
    }

    private static bool TryParse<T>(string json, string messageId, out T message)
        where T : class
    {
        message = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            Debug.LogWarning("Loot message parse failed, empty json. msgId=" + messageId);
            return false;
        }

        try
        {
            message = JsonUtility.FromJson<T>(json);
        }
        catch (Exception exception)
        {
            Debug.LogWarning(
                "Loot message parse failed. msgId=" + messageId
                + " error=" + exception.Message);
            return false;
        }

        if (message != null)
        {
            return true;
        }

        Debug.LogWarning("Loot message parse returned null. msgId=" + messageId);
        return false;
    }

    private static void InvokeSafely<T>(Action<T> handler, T message, string messageId)
    {
        if (handler == null)
        {
            return;
        }

        try
        {
            handler.Invoke(message);
        }
        catch (Exception exception)
        {
            Debug.LogError(
                "Loot message handler failed. msgId=" + messageId
                + " error=" + exception);
        }
    }
}
