using System;
using UnityEngine;

public sealed class LootPickupRangeChecker
{
    private readonly LootSystemSettings settings;

    public LootPickupRangeChecker(LootSystemSettings settings)
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    public bool IsWithinRequestRange(
        Vector3 playerPosition,
        Vector3 lootPosition)
    {
        float horizontalDistance = Mathf.Max(0f, settings.PickupHorizontalDistance);
        float verticalDistance = Mathf.Max(0f, settings.PickupVerticalDistance);
        float deltaX = playerPosition.x - lootPosition.x;
        float deltaZ = playerPosition.z - lootPosition.z;
        float horizontalDistanceSquared = deltaX * deltaX + deltaZ * deltaZ;

        return horizontalDistanceSquared <= horizontalDistance * horizontalDistance
            && Mathf.Abs(playerPosition.y - lootPosition.y) <= verticalDistance;
    }
}
