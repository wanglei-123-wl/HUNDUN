using System;
using UnityEngine;

public sealed class LootErrorHandler
{
    private readonly LootStateStore stateStore;
    private readonly WorldLootViewManager viewManager;
    private readonly LootPickupService pickupService;
    private readonly LootFeedbackPresenter feedbackPresenter;
    private readonly LootErrorTextProvider errorTextProvider;

    public event Action WorldResyncRequested;
    public event Action PlayerWorldStateCheckRequested;
    public event Action<string> MoveCloserRequested;

    public LootErrorHandler(
        LootStateStore stateStore,
        WorldLootViewManager viewManager,
        LootPickupService pickupService,
        LootFeedbackPresenter feedbackPresenter,
        LootErrorTextProvider errorTextProvider)
    {
        this.stateStore = stateStore
            ?? throw new ArgumentNullException(nameof(stateStore));
        this.viewManager = viewManager
            ?? throw new ArgumentNullException(nameof(viewManager));
        this.pickupService = pickupService
            ?? throw new ArgumentNullException(nameof(pickupService));
        this.feedbackPresenter = feedbackPresenter
            ?? throw new ArgumentNullException(nameof(feedbackPresenter));
        this.errorTextProvider = errorTextProvider
            ?? throw new ArgumentNullException(nameof(errorTextProvider));
    }

    public void HandlePickupFailure(LootPickupResultMessageDto message)
    {
        if (message == null
            || !string.Equals(
                message.status,
                LootProtocolConstants.PickupStatuses.Rejected,
                StringComparison.Ordinal))
        {
            return;
        }

        string errorCode = message.errorCode;
        switch (errorCode)
        {
            case LootProtocolConstants.ErrorCodes.PickupDuplicateOrStale:
            case LootProtocolConstants.ErrorCodes.PickupSequenceGap:
                if (errorCode == LootProtocolConstants.ErrorCodes.PickupDuplicateOrStale)
                    Debug.LogWarning(
                        "Loot pickup request was duplicate or stale. sequence="
                        + message.requestSequence);
                pickupService.SetPickupSuspended(true);
                pickupService.ClearPendingRequests();
                feedbackPresenter.ShowError(errorTextProvider.GetMessage(errorCode));
                WorldResyncRequested?.Invoke();
                return;

            case LootProtocolConstants.ErrorCodes.PlayerNotInWorld:
                pickupService.SetPickupSuspended(true);
                pickupService.ClearPendingRequests();
                feedbackPresenter.ShowError(errorTextProvider.GetMessage(errorCode));
                PlayerWorldStateCheckRequested?.Invoke();
                return;

            case LootProtocolConstants.ErrorCodes.PlayerDead:
                feedbackPresenter.ShowError(errorTextProvider.GetMessage(errorCode));
                return;

            case LootProtocolConstants.ErrorCodes.LootNotFound:
                RemoveLocalLoot(message.lootId, LootRemoveReason.Unknown);
                feedbackPresenter.ShowError(errorTextProvider.GetMessage(errorCode));
                return;

            case LootProtocolConstants.ErrorCodes.LootAlreadyPicked:
                feedbackPresenter.ShowError(errorTextProvider.GetMessage(errorCode));
                return;

            case LootProtocolConstants.ErrorCodes.LootExpired:
                RemoveLocalLoot(message.lootId, LootRemoveReason.Expired);
                feedbackPresenter.ShowError(errorTextProvider.GetMessage(errorCode));
                return;

            case LootProtocolConstants.ErrorCodes.LootNotOwner:
                feedbackPresenter.ShowError(errorTextProvider.GetMessage(errorCode));
                return;

            case LootProtocolConstants.ErrorCodes.LootTooFar:
                feedbackPresenter.ShowError(errorTextProvider.GetMessage(errorCode));
                MoveCloserRequested?.Invoke(message.lootId);
                return;

            case LootProtocolConstants.ErrorCodes.ServerBusy:
            case LootProtocolConstants.ErrorCodes.RewardNotAvailable:
            case LootProtocolConstants.ErrorCodes.InventoryFull:
            case LootProtocolConstants.ErrorCodes.AlreadyClaimed:
            case LootProtocolConstants.ErrorCodes.WalletLimitExceeded:
            case LootProtocolConstants.ErrorCodes.CharacterNotFound:
            case LootProtocolConstants.ErrorCodes.ItemNotFound:
            case LootProtocolConstants.ErrorCodes.ItemNotGrantable:
            case LootProtocolConstants.ErrorCodes.RewardSourceConflict:
            case LootProtocolConstants.ErrorCodes.GrantIdConflict:
            case LootProtocolConstants.ErrorCodes.InvalidGrantRequest:
            case LootProtocolConstants.ErrorCodes.InvalidGrantSource:
            case LootProtocolConstants.ErrorCodes.DatabaseError:
            case LootProtocolConstants.ErrorCodes.DatabaseUnavailable:
            case LootProtocolConstants.ErrorCodes.DatabaseOperationCancelled:
            case LootProtocolConstants.ErrorCodes.GrantNotFound:
                feedbackPresenter.ShowError(errorTextProvider.GetMessage(errorCode));
                return;

            case LootProtocolConstants.ErrorCodes.InvalidLootPickup:
                pickupService.SetPickupSuspended(true);
                feedbackPresenter.ShowError(errorTextProvider.GetMessage(errorCode));
                Debug.LogError(
                    "Invalid loot pickup response. sequence=" + message.requestSequence
                    + " lootId=" + message.lootId);
                return;

            default:
                feedbackPresenter.ShowError(pickupService.PickupSuspended
                    ? "拾取状态未确认，请重新进入游戏"
                    : errorTextProvider.GetMessage(errorCode));
                Debug.LogWarning("Unknown loot pickup error code: " + errorCode);
                return;
        }
    }

    public void HandleSceneSnapshotApplied(
        string sceneId,
        System.Collections.Generic.IReadOnlyCollection<LootState> loots)
    {
    }

    public void ResetState()
    {
    }

    private void RemoveLocalLoot(string lootId, LootRemoveReason reason)
    {
        if (string.IsNullOrWhiteSpace(lootId))
        {
            return;
        }

        if (stateStore.TryGet(lootId, out LootState state))
        {
            stateStore.Remove(lootId, state.AoiRevision);
        }

        viewManager.Remove(lootId, reason);
    }
}
