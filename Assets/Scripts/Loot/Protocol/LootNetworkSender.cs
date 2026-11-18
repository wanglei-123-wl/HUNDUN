using System;

public sealed class LootNetworkSender
{
    private const int MaxLootIdLength = 128;

    private readonly Func<LootPickupMessageDto, bool> sendPickupMessage;
    private readonly Func<BattleOutboxAckMessageDto, bool> sendAckMessage;
    private readonly Func<InventorySnapshotMessageDto, bool> sendInventorySnapshotMessage;

    public LootNetworkSender(NetworkManager networkManager)
    {
        if (networkManager == null)
        {
            throw new ArgumentNullException(nameof(networkManager));
        }

        sendPickupMessage = networkManager.Send;
        sendAckMessage = networkManager.Send;
        sendInventorySnapshotMessage = networkManager.Send;
    }

    public LootNetworkSender(
        Func<LootPickupMessageDto, bool> sendPickupMessage,
        Func<BattleOutboxAckMessageDto, bool> sendAckMessage)
        : this(sendPickupMessage, sendAckMessage, _ => false)
    {
    }

    public LootNetworkSender(
        Func<LootPickupMessageDto, bool> sendPickupMessage,
        Func<BattleOutboxAckMessageDto, bool> sendAckMessage,
        Func<InventorySnapshotMessageDto, bool> sendInventorySnapshotMessage)
    {
        this.sendPickupMessage = sendPickupMessage
            ?? throw new ArgumentNullException(nameof(sendPickupMessage));
        this.sendAckMessage = sendAckMessage
            ?? throw new ArgumentNullException(nameof(sendAckMessage));
        this.sendInventorySnapshotMessage = sendInventorySnapshotMessage
            ?? throw new ArgumentNullException(nameof(sendInventorySnapshotMessage));
    }

    public bool SendPickup(long requestSequence, long clientTick, string lootId)
    {
        if (requestSequence < 1
            || clientTick <= 0
            || string.IsNullOrWhiteSpace(lootId)
            || lootId.Length > MaxLootIdLength)
        {
            return false;
        }

        LootPickupMessageDto message = new LootPickupMessageDto
        {
            requestSequence = requestSequence,
            clientTick = clientTick,
            lootId = lootId
        };

        return sendPickupMessage(message);
    }

    public bool SendReliableAck(long reliableMessageId)
    {
        if (reliableMessageId <= 0)
        {
            return false;
        }

        BattleOutboxAckMessageDto message = new BattleOutboxAckMessageDto
        {
            reliableMessageId = reliableMessageId
        };

        return sendAckMessage(message);
    }

    public bool SendInventorySnapshotRequest()
    {
        return sendInventorySnapshotMessage(new InventorySnapshotMessageDto());
    }
}
