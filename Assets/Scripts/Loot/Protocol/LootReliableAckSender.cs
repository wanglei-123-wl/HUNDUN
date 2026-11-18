using System;

public sealed class LootReliableAckSender
{
    private readonly LootNetworkSender networkSender;

    public LootReliableAckSender(LootNetworkSender networkSender)
    {
        this.networkSender = networkSender
            ?? throw new ArgumentNullException(nameof(networkSender));
    }

    public bool SendAck(long reliableMessageId)
    {
        return networkSender.SendReliableAck(reliableMessageId);
    }
}
