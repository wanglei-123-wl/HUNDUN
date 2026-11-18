using System;
using System.Collections.Generic;

public sealed class LootPickupSessionState
{
    // Keeps pickup request state isolated from the world loot presentation.
    public long LastConsumedSequence { get; private set; }
    public bool PickupSuspended { get; set; }
    public readonly Dictionary<long, LootPickupRequestState> RequestsBySequence =
        new Dictionary<long, LootPickupRequestState>();
    public readonly Dictionary<string, long> PendingSequenceByLootId =
        new Dictionary<string, long>(StringComparer.Ordinal);
    public readonly HashSet<string> CompletedGrants = new HashSet<string>(StringComparer.Ordinal);
    public readonly Queue<string> CompletedGrantOrder = new Queue<string>();
    public readonly HashSet<long> CompletedFinalSequences = new HashSet<long>();
    public readonly Queue<long> CompletedFinalSequenceOrder = new Queue<long>();

    public void ConfirmConsumed(long sequence)
    {
        if (sequence > LastConsumedSequence)
            LastConsumedSequence = sequence;
    }

    public void Reset()
    {
        foreach (LootPickupRequestState request in RequestsBySequence.Values)
            request.Pending = false;
        RequestsBySequence.Clear();
        PendingSequenceByLootId.Clear();
        CompletedGrants.Clear();
        CompletedGrantOrder.Clear();
        CompletedFinalSequences.Clear();
        CompletedFinalSequenceOrder.Clear();
        LastConsumedSequence = 0;
        PickupSuspended = false;
    }
}
