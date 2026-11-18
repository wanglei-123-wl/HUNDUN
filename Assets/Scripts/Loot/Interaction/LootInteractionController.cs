using System;
using UnityEngine;

public sealed class LootInteractionController
{
    private readonly LootStateStore stateStore;
    private readonly LootSyncService syncService;
    private readonly LootPickupService pickupService;
    private readonly LootOwnershipService ownershipService;
    private readonly LootPickupRangeChecker rangeChecker;
    private readonly Func<string> getSelfCharacterDbId;

    public event Action<LootState> MoveCloserRequested;

    public LootInteractionController(
        LootStateStore stateStore,
        LootSyncService syncService,
        LootPickupService pickupService,
        LootOwnershipService ownershipService,
        LootPickupRangeChecker rangeChecker,
        Func<string> getSelfCharacterDbId)
    {
        this.stateStore = stateStore
            ?? throw new ArgumentNullException(nameof(stateStore));
        this.syncService = syncService
            ?? throw new ArgumentNullException(nameof(syncService));
        this.pickupService = pickupService
            ?? throw new ArgumentNullException(nameof(pickupService));
        this.ownershipService = ownershipService
            ?? throw new ArgumentNullException(nameof(ownershipService));
        this.rangeChecker = rangeChecker
            ?? throw new ArgumentNullException(nameof(rangeChecker));
        this.getSelfCharacterDbId = getSelfCharacterDbId
            ?? throw new ArgumentNullException(nameof(getSelfCharacterDbId));
    }

    public LootInteractionResult TryHandleClick(
        WorldLootView view,
        Vector3 playerPosition)
    {
        if (view == null || string.IsNullOrWhiteSpace(view.LootId))
        {
            return LootInteractionResult.InvalidTarget;
        }

        return TryInteract(view.LootId, playerPosition);
    }

    public LootInteractionResult TryHandleAutoPickup(
        string lootId,
        Vector3 playerPosition)
    {
        return TryInteract(lootId, playerPosition);
    }

    public LootInteractionResult TryInteract(
        string lootId,
        Vector3 playerPosition)
    {
        if (string.IsNullOrWhiteSpace(lootId))
        {
            return LootInteractionResult.InvalidTarget;
        }

        if (!stateStore.TryGet(lootId, out LootState loot))
        {
            return LootInteractionResult.LootNotFound;
        }

        if (!string.Equals(
                loot.SceneId,
                syncService.CurrentSceneId,
                StringComparison.Ordinal))
        {
            return LootInteractionResult.WrongScene;
        }

        if (pickupService.IsPending(lootId))
        {
            return LootInteractionResult.AlreadyPending;
        }

        LootOwnershipState ownershipState = ownershipService.GetOwnershipState(
            loot,
            getSelfCharacterDbId());

        if (ownershipState == LootOwnershipState.ProtectedByOther)
        {
            return LootInteractionResult.ProtectedByOther;
        }

        if (!rangeChecker.IsWithinRequestRange(playerPosition, loot.Position))
        {
            MoveCloserRequested?.Invoke(loot);
            return LootInteractionResult.MoveCloserRequested;
        }

        LootPickupRequestResult requestResult = pickupService.RequestPickup(lootId);
        switch (requestResult)
        {
            case LootPickupRequestResult.Sent:
                return LootInteractionResult.PickupRequested;
            case LootPickupRequestResult.LootNotFound:
                return LootInteractionResult.LootNotFound;
            case LootPickupRequestResult.WrongScene:
                return LootInteractionResult.WrongScene;
            case LootPickupRequestResult.AlreadyPending:
                return LootInteractionResult.AlreadyPending;
            default:
                return LootInteractionResult.PickupRejected;
        }
    }
}
