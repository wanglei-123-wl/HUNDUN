public sealed class LootPickupRequestState
{
    public long RequestSequence;
    public string LootId;
    public long SendClientTick;
    public float SendLocalTime;
    public bool Pending;
    public int RetryCount;
    public LootPickupRequestPhase Phase;
}
