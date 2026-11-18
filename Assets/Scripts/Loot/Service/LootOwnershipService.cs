using System;

public sealed class LootOwnershipService
{
    private readonly LootServerTimeService serverTimeService;

    public LootOwnershipService(LootServerTimeService serverTimeService)
    {
        this.serverTimeService = serverTimeService
            ?? throw new ArgumentNullException(nameof(serverTimeService));
    }

    public LootOwnershipState GetOwnershipState(
        LootState loot,
        string selfCharacterDbId)
    {
        if (loot == null)
        {
            throw new ArgumentNullException(nameof(loot));
        }

        if (string.IsNullOrWhiteSpace(loot.OwnerId))
        {
            return LootOwnershipState.Free;
        }

        if (!string.IsNullOrWhiteSpace(selfCharacterDbId)
            && string.Equals(loot.OwnerId, selfCharacterDbId, StringComparison.Ordinal))
        {
            return LootOwnershipState.OwnedBySelf;
        }

        if (serverTimeService.GetEstimatedServerTimeMs() >= loot.OwnerExpireAtMs)
        {
            return LootOwnershipState.Free;
        }

        return LootOwnershipState.ProtectedByOther;
    }
}
