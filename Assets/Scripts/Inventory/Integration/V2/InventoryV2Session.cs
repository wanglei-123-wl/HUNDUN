using System;
using System.Collections.Generic;
using Game.Alchemy.Protocol;
using Game.Alchemy.Runtime;
using Game.Cultivation.Protocol;
using Game.Cultivation.Runtime;
using Game.Forging.Runtime;
using Game.Inventory.Model.V2;
using Game.Inventory.Presentation.V2;
using Game.Inventory.Protocol.V2;
using Game.Items;
using Game.PlayerTrade.Protocol;
using UnityEngine;

namespace Game.Inventory.Integration.V2
{
    public sealed class InventoryV2Session : IDisposable
    {
        private const float RequestTimeoutSeconds = 10f;
        private const float MinimumSnapshotRequestInterval = 1.2f;
        private readonly NetworkManager network;
        private readonly NetMessageDispatcher dispatcher;
        private readonly InventoryPanelView panel;
        private readonly InventoryBagLocalController bag;
        private readonly InventoryBagGridPresenter presenter;
        private readonly InventorySelectedItemDetailPanel selectedDetailPanel;
        private readonly InventoryEquipmentPanelView equipmentPanel;
        private readonly InventoryDiscardConfirmationView discardConfirmation;
        private readonly ItemDefinitionTable catalog;
        private readonly Func<long> clientTick;
        private readonly Func<long> combatRevision;
        private readonly Action<int, long> applyHealth;
        private readonly Func<long, InventoryV2EquipmentCharacterStatsDto, long, bool> applyEquipmentStats;
        private readonly Action playPotionAnimation;
        private readonly Action<int, int, long> applyMana;
        private readonly Action<long?, long?> applyJingjieBalance;
        private readonly Func<bool> progressionReady;
        private readonly Action requestAttributeRefresh;
        private readonly Action<string> showNotice;
        private readonly Func<long, bool> learnSkill;
        private CultivationSessionRuntime learnRuntime;
        private AlchemySessionRuntime alchemyLearnRuntime;
        private CultivationItemCatalogResultDto presentedItemCatalog;
        private 技能注册表 skillRegistry;
        private readonly Dictionary<string, float> itemCooldownDeadlines =
            new Dictionary<string, float>(StringComparer.Ordinal);
        private long characterDbId;
        private string sceneId;
        private string itemUseOperationSessionId;
        private string equipmentOperationSessionId;
        private long nextEquipmentOperationSequence;
        private string worldDropOperationSessionId;
        private long nextWorldDropOperationSequence;
        private long nextSequence;
        private long pendingSequence;
        private float requestStartedAt;
        private float lastRequestAt = float.NegativeInfinity;
        private bool snapshotRateLimitRetried;
        private InventoryOwnershipSnapshot snapshot;
        private InventoryEquipmentIntent pendingOperation;
        private long pendingOperationSequence;
        private int pendingUnequipTarget = -1;
        private float operationStartedAt;
        private bool refreshAfterPending;
        private long nextItemUseSequence;
        private long lastItemUseTick;
        private InventoryV2ItemUseRequestDto pendingItemUse;
        private string pendingItemUseCooldownKey;
        private long pendingItemUseCombatRevision;
        private float itemUseStartedAt;
        private bool pendingItemUseMayHaveConsumed;
        private long unresolvedItemUseCharacterDbId;
        private long nextInventoryOperationSequence;
        private InventoryV2DiscardRequestDto pendingDiscard;
        private float discardStartedAt;
        private bool discardRetried;
        private PendingWorldDrop pendingWorldDrop;
        private float worldDropStartedAt;
        private bool worldDropRetried;

        private sealed class PendingWorldDrop
        {
            public readonly NetMessageBase Request;
            public readonly long Sequence;
            public readonly long StackId;
            public readonly int Count;
            public readonly string ItemInstanceId;
            public bool IsStack => StackId > 0;

            public PendingWorldDrop(NetMessageBase request, long sequence,
                long stackId, int count, string itemInstanceId)
            {
                Request = request;
                Sequence = sequence;
                StackId = stackId;
                Count = count;
                ItemInstanceId = itemInstanceId;
            }
        }

        public bool IsOpen { get; private set; }
        public InventoryOwnershipSnapshot CurrentSnapshot => snapshot;

        public InventoryV2Session(NetworkManager network, NetMessageDispatcher dispatcher,
            InventoryPanelView panel, ItemDefinitionTable items, Transform equipmentRoot,
            Func<long> clientTick = null, Func<long> combatRevision = null,
            Action<int, long> applyHealth = null,
            Action<int, int, long> applyMana = null,
            Action requestAttributeRefresh = null,
            Action<string> showNotice = null,
            Action<long?, long?> applyJingjieBalance = null,
            Func<bool> progressionReady = null,
            InventorySelectedItemDetailPanel selectedDetailPanel = null,
            Func<long, bool> learnSkill = null,
            Action playPotionAnimation = null,
            Func<long, InventoryV2EquipmentCharacterStatsDto, long, bool> applyEquipmentStats = null)
        {
            this.network = network ?? throw new ArgumentNullException(nameof(network));
            this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
            this.panel = panel ?? throw new ArgumentNullException(nameof(panel));
            catalog = items;
            this.clientTick = clientTick;
            this.combatRevision = combatRevision;
            this.applyHealth = applyHealth;
            this.applyEquipmentStats = applyEquipmentStats;
            this.playPotionAnimation = playPotionAnimation;
            this.applyMana = applyMana;
            this.applyJingjieBalance = applyJingjieBalance;
            this.progressionReady = progressionReady;
            this.requestAttributeRefresh = requestAttributeRefresh;
            this.showNotice = showNotice;
            this.selectedDetailPanel = selectedDetailPanel;
            this.learnSkill = learnSkill;
            bag = new InventoryBagLocalController(new InventoryBagPlayerPrefsStorage());
            presenter = new InventoryBagGridPresenter(bag, panel, items);
            discardConfirmation = InventoryDiscardConfirmationView.Create(panel);
            presenter.WorldDropOffered += HandleWorldDropOffered;
            if (discardConfirmation != null)
                discardConfirmation.Confirmed += HandleStackWorldDropConfirmed;
            if (InventoryEquipmentPanelView.TryCreate(equipmentRoot, items,
                out equipmentPanel, out string bindingError))
            {
                equipmentPanel.EquipRequested += HandleEquipRequested;
                equipmentPanel.MoveRequested += HandleMoveRequested;
                presenter.UnequipRequested += HandleUnequipRequested;
            }
            else
                Debug.LogWarning("V2 equipment interaction is unavailable: " + bindingError);
            dispatcher.Register(InventoryV2Protocol.SnapshotResult, HandleSnapshot);
            dispatcher.Register(InventoryV2Protocol.OperationResult, HandleOperationResult);
            dispatcher.Register(InventoryV2Protocol.WorldDropResult, HandleWorldDropResult);
            dispatcher.Register(InventoryV2Protocol.ItemUseResult, HandleItemUseResult);
            dispatcher.Register(InventoryV2Protocol.InventoryOperationResult,
                HandleDiscardResult);
            dispatcher.Register("error", HandleServerError);
            presenter.StackDoubleClicked += HandleStackDoubleClicked;
            if (selectedDetailPanel != null)
            {
                selectedDetailPanel.LearnRequested += HandleLearnRequested;
                selectedDetailPanel.UseRequested += HandleUseRequested;
                selectedDetailPanel.SetActionResolver(ResolveSelectedItemActions);
                selectedDetailPanel.SetAttributesResolver(ResolveSelectedItemAttributes);
            }
            panel.CloseClicked += Close;
        }

        public void BeginSession(long activeCharacterDbId, string activeSceneId = null,
            string activeEquipmentOperationSessionId = null,
            string activeWorldDropOperationSessionId = null,
            string activeItemUseOperationSessionId = null)
        {
            Reset();
            characterDbId = activeCharacterDbId;
            sceneId = activeSceneId;
            equipmentOperationSessionId = activeEquipmentOperationSessionId;
            worldDropOperationSessionId = activeWorldDropOperationSessionId;
            itemUseOperationSessionId = activeItemUseOperationSessionId;
            if (unresolvedItemUseCharacterDbId != activeCharacterDbId)
                unresolvedItemUseCharacterDbId = 0;
            if (string.IsNullOrWhiteSpace(itemUseOperationSessionId))
                Debug.LogWarning(
                    "V2 item use is unavailable because enter_world_result did not provide "
                    + "itemUseOperationSessionId.");
        }

        public void Reset()
        {
            if (pendingItemUse != null && characterDbId > 0)
                unresolvedItemUseCharacterDbId = characterDbId;
            BindLearnRuntime(null);
            BindAlchemyLearnRuntime(null);
            presentedItemCatalog = null;
            equipmentOperationSessionId = null;
            nextEquipmentOperationSequence = 0;
            worldDropOperationSessionId = null;
            nextWorldDropOperationSequence = 0;
            itemUseOperationSessionId = null;
            CompleteSnapshotRequest();
            lastRequestAt = float.NegativeInfinity;
            pendingOperation = null;
            pendingOperationSequence = 0;
            pendingUnequipTarget = -1;
            refreshAfterPending = false;
            pendingItemUse = null;
            pendingItemUseCooldownKey = null;
            pendingItemUseMayHaveConsumed = false;
            nextItemUseSequence = 0;
            lastItemUseTick = 0;
            itemCooldownDeadlines.Clear();
            pendingDiscard = null;
            discardRetried = false;
            pendingWorldDrop = null;
            worldDropRetried = false;
            characterDbId = 0;
            sceneId = null;
            snapshot = null;
            IsOpen = false;
            if (discardConfirmation != null) discardConfirmation.Hide();
            bag.Reset();
            presenter.Clear();
            equipmentPanel?.SetPending(false);
            equipmentPanel?.Clear();
            panel.SetLoading(false);
            panel.Hide();
        }

        public void Open()
        {
            IsOpen = true;
            panel.Show();
            if (snapshot == null)
                RequestSnapshot();
            else
            {
                presenter.ApplySnapshot(snapshot);
                equipmentPanel?.Render(snapshot);
            }
        }

        public void Close()
        {
            if (pendingDiscard != null || pendingWorldDrop != null) return;
            IsOpen = false;
            if (discardConfirmation != null) discardConfirmation.Hide();
            panel.Hide();
        }

        public void RequestSnapshot()
        {
            if (pendingSequence != 0 || pendingOperation != null
                || (pendingItemUse != null && !pendingItemUseMayHaveConsumed)
                || pendingDiscard != null
                || pendingWorldDrop != null) return;
            if (characterDbId <= 0 || !network.IsConnected)
            {
                panel.SetLoading(false);
                Debug.LogWarning("V2 inventory snapshot requires an active character and Gate connection.");
                ShowNotice("背包暂不可用，请检查网络连接");
                return;
            }
            if (Time.unscaledTime - lastRequestAt < MinimumSnapshotRequestInterval)
            {
                refreshAfterPending = true;
                return;
            }
            long sequence = ++nextSequence;
            if (!network.Send(new InventoryV2SnapshotRequestDto(sequence)))
            {
                if (snapshotRateLimitRetried)
                {
                    CompleteSnapshotRequest();
                    refreshAfterPending = false;
                }
                panel.SetLoading(false);
                Debug.LogWarning("V2 inventory snapshot request could not be sent.");
                ShowNotice("背包请求发送失败，请稍后重试");
                return;
            }
            pendingSequence = sequence;
            refreshAfterPending = false;
            lastRequestAt = Time.unscaledTime;
            requestStartedAt = Time.unscaledTime;
            if (IsOpen && (!snapshotRateLimitRetried || snapshot == null)) panel.SetLoading(true);
            Debug.Log("[InventorySnapshot][TX] protocol=V2 requestSequence=" + sequence);
        }

        public void NotifyAuthoritativeInventoryChanged()
        {
            if (characterDbId <= 0) return;
            refreshAfterPending = true;
            if (pendingSequence == 0 && pendingOperation == null
                && (pendingItemUse == null || pendingItemUseMayHaveConsumed)
                && pendingDiscard == null
                && pendingWorldDrop == null)
                RequestSnapshot();
        }

        public void ApplyPickupDelta(long revision,
            InventoryV2StackChangeDto[] stackChanges,
            InventoryV2EquipmentDto[] equipmentChanges,
            InventoryV2WalletChangeDto[] walletChanges)
        {
            if (characterDbId <= 0) return;
            if (pendingOperation != null || pendingItemUse != null
                || pendingDiscard != null || pendingWorldDrop != null)
            {
                NotifyAuthoritativeInventoryChanged();
                return;
            }
            InventoryV2DeltaResult result = InventoryV2DeltaChanges.TryApplyPickup(
                snapshot, revision, stackChanges, equipmentChanges, walletChanges,
                out InventoryOwnershipSnapshot next, out string error);
            if (result == InventoryV2DeltaResult.Stale) return;
            if (result == InventoryV2DeltaResult.NeedsSnapshot)
            {
                Debug.LogWarning("[InventoryV2][REPAIR] pickup delta: " + error);
                NotifyAuthoritativeInventoryChanged();
                return;
            }
            try
            {
                presenter.ApplySnapshot(next);
                equipmentPanel?.Render(next);
                snapshot = next;
                Debug.Log("[InventoryV2][DELTA] source=loot_pickup_result"
                    + " inventoryRevision=" + revision
                    + " stackChanges=" + stackChanges.Length
                    + " equipmentChanges=" + equipmentChanges.Length
                    + " walletChanges=" + walletChanges.Length);
            }
            catch (Exception exception)
            {
                Debug.LogError("V2 pickup delta could not be displayed: " + exception.Message);
                NotifyAuthoritativeInventoryChanged();
            }
        }

        public bool ApplyJingjieBreakthroughInventoryChanges(
            long revision, bool requiresSnapshot,
            InventoryV2StackChangeDto[] stackChanges, out string error,
            string source = "jingjie_breakthrough_result",
            bool allowNewStacks = false,
            InventoryV2WalletDto[] walletBalances = null)
        {
            error = string.Empty;
            if (characterDbId <= 0)
            {
                error = "No active inventory session is available.";
                return false;
            }
            if (!requiresSnapshot && revision == 0
                && stackChanges != null && stackChanges.Length == 0
                && (walletBalances == null || walletBalances.Length == 0))
                return true;
            if (requiresSnapshot || snapshot == null || pendingSequence != 0
                || pendingOperation != null || pendingItemUse != null
                || pendingDiscard != null || pendingWorldDrop != null)
            {
                error = "Inventory snapshot or local operation is pending.";
                NotifyAuthoritativeInventoryChanged();
                return false;
            }

            if (revision <= snapshot.InventoryRevision)
                return true;
            if (stackChanges == null
                || (stackChanges.Length == 0
                    && (walletBalances == null || walletBalances.Length == 0)))
            {
                error = "Inventory mutation has no stack or wallet changes.";
                NotifyAuthoritativeInventoryChanged();
                return false;
            }

            var ownedStacks = new Dictionary<long, InventoryStackState>();
            foreach (InventoryStackState stack in snapshot.Stacks)
                ownedStacks.Add(stack.StackId, stack);
            var seen = new HashSet<long>();
            var finalChanges = new InventoryV2StackChangeDto[stackChanges.Length];
            for (int index = 0; index < stackChanges.Length; index++)
            {
                InventoryV2StackChangeDto change = stackChanges[index];
                if (change == null || change.stackId <= 0 || change.newCount < 0
                    || !seen.Add(change.stackId)
                    || (!ownedStacks.TryGetValue(change.stackId,
                        out InventoryStackState owned)
                        && (!allowNewStacks || change.newCount == 0
                            || string.IsNullOrWhiteSpace(change.itemId)))
                    || (owned != null && change.newCount > 0
                        && (change.itemId != owned.ItemId
                            || change.isBound != owned.IsBound)))
                {
                    error = "Breakthrough stack cannot be reconciled locally.";
                    NotifyAuthoritativeInventoryChanged();
                    return false;
                }

                // Alchemy uses absolute counts; previousCount is a placeholder.
                finalChanges[index] = new InventoryV2StackChangeDto
                {
                    stackId = change.stackId,
                    itemId = owned != null ? owned.ItemId : change.itemId,
                    previousCount = owned != null ? owned.Count : 0,
                    newCount = change.newCount,
                    isBound = owned != null ? owned.IsBound : change.isBound
                };
            }

            var finalWallets = walletBalances == null
                ? Array.Empty<InventoryV2WalletChangeDto>()
                : new InventoryV2WalletChangeDto[walletBalances.Length];
            var seenWallets = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < finalWallets.Length; index++)
            {
                InventoryV2WalletDto wallet = walletBalances[index];
                if (wallet == null || string.IsNullOrWhiteSpace(wallet.currencyId)
                    || wallet.balance < 0
                    || !seenWallets.Add(wallet.currencyId))
                {
                    error = "Forging wallet change is invalid or duplicated.";
                    NotifyAuthoritativeInventoryChanged();
                    return false;
                }
                snapshot.Wallets.TryGetValue(wallet.currencyId,
                    out long previousBalance);
                finalWallets[index] = new InventoryV2WalletChangeDto
                {
                    currencyId = wallet.currencyId,
                    previousBalance = previousBalance,
                    newBalance = wallet.balance
                };
            }

            InventoryV2DeltaResult result = InventoryV2DeltaChanges.TryApplyPickup(
                snapshot, revision, finalChanges,
                Array.Empty<InventoryV2EquipmentDto>(),
                finalWallets,
                out InventoryOwnershipSnapshot next, out error);
            if (result == InventoryV2DeltaResult.Stale)
                return true;
            if (result == InventoryV2DeltaResult.NeedsSnapshot)
            {
                NotifyAuthoritativeInventoryChanged();
                return false;
            }

            InventoryOwnershipSnapshot previous = snapshot;
            InventoryBagLayout previousLayout = presenter.CurrentLayout;
            try
            {
                InventoryBagLayout layout = presenter.PrepareSnapshot(next);
                presenter.PresentSnapshot(next, layout);
                equipmentPanel?.Render(next);
                snapshot = next;
                try { presenter.PersistLayout(); }
                catch (Exception storageError)
                {
                    Debug.LogWarning("V2 bag layout could not be saved: "
                        + storageError.Message);
                }
                Debug.Log("[InventoryV2][DELTA] source=" + source
                    + " inventoryRevision=" + revision
                    + " stackChanges=" + stackChanges.Length);
                return true;
            }
            catch (Exception exception)
            {
                error = "Breakthrough inventory changes could not be displayed: "
                    + exception.Message;
                snapshot = previous;
                try
                {
                    presenter.RestoreSnapshot(previous, previousLayout);
                    equipmentPanel?.Render(previous);
                }
                catch (Exception restoreError)
                {
                    presenter.Clear();
                    equipmentPanel?.Clear();
                    IsOpen = false;
                    panel.Hide();
                    Debug.LogError("V2 breakthrough inventory rollback failed: "
                        + restoreError.Message);
                }
                NotifyAuthoritativeInventoryChanged();
                return false;
            }
        }

        public bool ApplyForgingClaimInventoryChanges(long revision,
            InventoryV2StackChangeDto[] stackChanges,
            Game.Forging.Protocol.锻造装备变化[] equipmentChanges,
            InventoryV2WalletDto[] walletBalances, out string error)
        {
            error = string.Empty;
            if (characterDbId <= 0 || snapshot == null || pendingSequence != 0
                || pendingOperation != null || pendingItemUse != null
                || pendingDiscard != null || pendingWorldDrop != null)
            {
                error = "Inventory snapshot or local operation is pending.";
                NotifyAuthoritativeInventoryChanged();
                return false;
            }
            if (revision <= snapshot.InventoryRevision)
                return true;
            if (revision != snapshot.InventoryRevision + 1
                || stackChanges == null || walletBalances == null
                || equipmentChanges == null || equipmentChanges.Length != 1)
            {
                error = "Forging claim mutation is incomplete or out of order.";
                NotifyAuthoritativeInventoryChanged();
                return false;
            }

            var equipment = new InventoryV2EquipmentDto[equipmentChanges.Length];
            for (int i = 0; i < equipment.Length; i++)
            {
                Game.Forging.Protocol.锻造装备变化 change = equipmentChanges[i];
                if (change == null
                    || !Guid.TryParseExact(change.itemInstanceId, "D", out _)
                    || string.IsNullOrWhiteSpace(change.itemId)
                    || string.IsNullOrWhiteSpace(change.equipmentId)
                    || change.enhanceLevel < 0 || change.durability < 0
                    || catalog == null
                    || !catalog.TryGetByEquipmentId(change.equipmentId,
                        out ItemDefinition definition)
                    || !string.Equals(definition.ItemId, change.itemId,
                        StringComparison.Ordinal))
                {
                    error = "Forging equipment identity is not in the local catalog.";
                    NotifyAuthoritativeInventoryChanged();
                    return false;
                }
                foreach (InventoryEquipmentState owned in snapshot.Equipment)
                    if (string.Equals(owned.ItemInstanceId,
                        change.itemInstanceId, StringComparison.Ordinal))
                    {
                        error = "Forging equipment instance already exists.";
                        NotifyAuthoritativeInventoryChanged();
                        return false;
                    }
                equipment[i] = new InventoryV2EquipmentDto
                {
                    itemInstanceId = change.itemInstanceId,
                    equipmentId = change.equipmentId,
                    isBound = change.isBound,
                    slotCode = 0
                };
            }

            var ownedStacks = new Dictionary<long, InventoryStackState>();
            foreach (InventoryStackState stack in snapshot.Stacks)
                ownedStacks.Add(stack.StackId, stack);
            var stacks = new InventoryV2StackChangeDto[stackChanges.Length];
            for (int i = 0; i < stacks.Length; i++)
            {
                InventoryV2StackChangeDto change = stackChanges[i];
                if (change == null || change.stackId <= 0 || change.newCount < 0)
                {
                    error = "Invalid forging stack change.";
                    NotifyAuthoritativeInventoryChanged();
                    return false;
                }
                ownedStacks.TryGetValue(change.stackId, out InventoryStackState previous);
                if (previous == null && (change.newCount == 0
                    || string.IsNullOrWhiteSpace(change.itemId)))
                {
                    error = "Unknown forging stack change.";
                    NotifyAuthoritativeInventoryChanged();
                    return false;
                }
                stacks[i] = new InventoryV2StackChangeDto
                {
                    stackId = change.stackId,
                    itemId = previous != null ? previous.ItemId : change.itemId,
                    isBound = previous != null ? previous.IsBound : change.isBound,
                    previousCount = previous != null ? previous.Count : 0,
                    newCount = change.newCount
                };
                if (previous != null && change.newCount > 0
                    && (change.itemId != previous.ItemId
                        || change.isBound != previous.IsBound))
                {
                    error = "Forging stack identity changed.";
                    NotifyAuthoritativeInventoryChanged();
                    return false;
                }
            }
            var wallets = new InventoryV2WalletChangeDto[walletBalances.Length];
            for (int i = 0; i < wallets.Length; i++)
            {
                InventoryV2WalletDto wallet = walletBalances[i];
                if (wallet == null || string.IsNullOrWhiteSpace(wallet.currencyId)
                    || wallet.balance < 0)
                {
                    error = "Invalid forging wallet balance.";
                    NotifyAuthoritativeInventoryChanged();
                    return false;
                }
                snapshot.Wallets.TryGetValue(wallet.currencyId, out long previous);
                wallets[i] = new InventoryV2WalletChangeDto
                {
                    currencyId = wallet.currencyId,
                    previousBalance = previous,
                    newBalance = wallet.balance
                };
            }

            InventoryV2DeltaResult result = InventoryV2DeltaChanges.TryApplyPickup(
                snapshot, revision, stacks, equipment, wallets,
                out InventoryOwnershipSnapshot next, out error);
            if (result != InventoryV2DeltaResult.Applied)
            {
                NotifyAuthoritativeInventoryChanged();
                return false;
            }
            InventoryOwnershipSnapshot previousSnapshot = snapshot;
            InventoryBagLayout previousLayout = presenter.CurrentLayout;
            try
            {
                InventoryBagLayout layout = presenter.PrepareSnapshot(next);
                presenter.PresentSnapshot(next, layout);
                equipmentPanel?.Render(next);
                snapshot = next;
                try { presenter.PersistLayout(); }
                catch (Exception storageError)
                {
                    Debug.LogWarning("[Forging] Bag layout could not be saved: "
                        + storageError.Message);
                }
                return true;
            }
            catch (Exception exception)
            {
                error = "Forging equipment could not be displayed: "
                    + exception.Message;
                snapshot = previousSnapshot;
                try
                {
                    presenter.RestoreSnapshot(previousSnapshot, previousLayout);
                    equipmentPanel?.Render(previousSnapshot);
                }
                catch (Exception restoreError)
                {
                    presenter.Clear();
                    equipmentPanel?.Clear();
                    IsOpen = false;
                    panel.Hide();
                    Debug.LogError("[Forging] Bag rollback failed: "
                        + restoreError.Message);
                }
                NotifyAuthoritativeInventoryChanged();
                return false;
            }
        }

        public bool ApplyTradeInventoryChanges(
            long revision,
            bool requiresSnapshot,
            PlayerTradeInventoryChangeDto[] changes,
            out string error)
        {
            error = string.Empty;
            if (characterDbId <= 0 || snapshot == null)
            {
                error = "No V2 ownership snapshot is available.";
                NotifyAuthoritativeInventoryChanged();
                return false;
            }
            if (requiresSnapshot)
            {
                error = "Trade response requires an inventory snapshot.";
                NotifyAuthoritativeInventoryChanged();
                return false;
            }
            if (revision <= snapshot.InventoryRevision)
                return true;
            if (snapshot.InventoryRevision == long.MaxValue
                || revision != snapshot.InventoryRevision + 1
                || changes == null || changes.Length == 0)
            {
                error = "Trade inventory revision or changes are not consecutive.";
                NotifyAuthoritativeInventoryChanged();
                return false;
            }

            var stacks = new Dictionary<long, InventoryStackState>();
            var equipment = new Dictionary<string, InventoryEquipmentState>(
                StringComparer.Ordinal);
            var wallets = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (InventoryStackState item in snapshot.Stacks)
                stacks.Add(item.StackId, item);
            foreach (InventoryEquipmentState item in snapshot.Equipment)
                equipment.Add(item.ItemInstanceId, item);
            foreach (KeyValuePair<string, long> wallet in snapshot.Wallets)
                wallets.Add(wallet.Key, wallet.Value);

            var seenStacks = new HashSet<long>();
            var seenEquipment = new HashSet<string>(StringComparer.Ordinal);
            var seenWallets = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < changes.Length; index++)
            {
                PlayerTradeInventoryChangeDto change = changes[index];
                if (change == null || string.IsNullOrWhiteSpace(change.type))
                {
                    error = "Trade inventory change is empty.";
                    NotifyAuthoritativeInventoryChanged();
                    return false;
                }

                switch (change.type)
                {
                    case "stack_upsert":
                        if (change.stackId <= 0
                            || string.IsNullOrWhiteSpace(change.itemId)
                            || change.count <= 0
                            || !seenStacks.Add(change.stackId))
                        {
                            error = "Trade stack upsert is invalid.";
                            NotifyAuthoritativeInventoryChanged();
                            return false;
                        }
                        if (stacks.TryGetValue(change.stackId,
                                out InventoryStackState previousStack)
                            && (!string.Equals(previousStack.ItemId, change.itemId,
                                StringComparison.Ordinal)
                                || previousStack.IsBound != change.isBound))
                        {
                            error = "Trade stack upsert conflicts with local ownership.";
                            NotifyAuthoritativeInventoryChanged();
                            return false;
                        }
                        stacks[change.stackId] = new InventoryStackState(
                            change.stackId, change.itemId, change.count, change.isBound, change.remainingUses);
                        break;

                    case "stack_remove":
                        if (change.stackId <= 0 || !seenStacks.Add(change.stackId)
                            || !stacks.Remove(change.stackId))
                        {
                            error = "Trade stack removal does not match local ownership.";
                            NotifyAuthoritativeInventoryChanged();
                            return false;
                        }
                        break;

                    case "equipment_upsert":
                        if (string.IsNullOrWhiteSpace(change.itemInstanceId)
                            || string.IsNullOrWhiteSpace(change.equipmentId)
                            || change.slotCode < 0
                            || change.slotCode > InventoryV2Protocol.EquipmentSlotCount
                            || !seenEquipment.Add(change.itemInstanceId))
                        {
                            error = "Trade equipment upsert is invalid.";
                            NotifyAuthoritativeInventoryChanged();
                            return false;
                        }
                        if (equipment.TryGetValue(change.itemInstanceId,
                                out InventoryEquipmentState previousEquipment)
                            && !string.Equals(previousEquipment.EquipmentId,
                                change.equipmentId, StringComparison.Ordinal))
                        {
                            error = "Trade equipment upsert conflicts with local ownership.";
                            NotifyAuthoritativeInventoryChanged();
                            return false;
                        }
                        foreach (InventoryEquipmentState item in equipment.Values)
                        {
                            if (item.ItemInstanceId != change.itemInstanceId
                                && item.SlotCode > 0
                                && item.SlotCode == change.slotCode)
                            {
                                error = "Trade equipment upsert occupies an existing slot.";
                                NotifyAuthoritativeInventoryChanged();
                                return false;
                            }
                        }
                        equipment[change.itemInstanceId] =
                            new InventoryEquipmentState(change.itemInstanceId,
                                change.equipmentId, change.isBound, change.slotCode);
                        break;

                    case "equipment_remove":
                        if (string.IsNullOrWhiteSpace(change.itemInstanceId)
                            || !seenEquipment.Add(change.itemInstanceId)
                            || !equipment.Remove(change.itemInstanceId))
                        {
                            error = "Trade equipment removal does not match local ownership.";
                            NotifyAuthoritativeInventoryChanged();
                            return false;
                        }
                        break;

                    case "wallet_set":
                        if (string.IsNullOrWhiteSpace(change.currencyId)
                            || change.balance < 0
                            || !seenWallets.Add(change.currencyId))
                        {
                            error = "Trade wallet change is invalid.";
                            NotifyAuthoritativeInventoryChanged();
                            return false;
                        }
                        wallets[change.currencyId] = change.balance;
                        break;

                    default:
                        error = "Unsupported trade inventory change: " + change.type;
                        NotifyAuthoritativeInventoryChanged();
                        return false;
                }
            }

            int bagCount = stacks.Count;
            foreach (InventoryEquipmentState item in equipment.Values)
                if (item.SlotCode == 0)
                    bagCount++;
            if (bagCount > snapshot.Capacity)
            {
                error = "Trade inventory changes exceed bag capacity.";
                NotifyAuthoritativeInventoryChanged();
                return false;
            }

            InventoryOwnershipSnapshot next = new InventoryOwnershipSnapshot(
                snapshot.CharacterDbId, revision, snapshot.EquipmentRevision,
                snapshot.Capacity, snapshot.CatalogVersion,
                InventoryV2Protocol.EquipmentCatalogVersion,
                new List<InventoryStackState>(stacks.Values),
                new List<InventoryEquipmentState>(equipment.Values),
                wallets, snapshot.AttributeBonus);
            try
            {
                presenter.ApplySnapshot(next);
                equipmentPanel?.Render(next);
                snapshot = next;
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                NotifyAuthoritativeInventoryChanged();
                return false;
            }
        }

        public void Tick()
        {
            CultivationItemCatalogResultDto currentCatalog =
                CultivationSessionRuntime.Instance?.ItemCatalog;
            if (!ReferenceEquals(presentedItemCatalog, currentCatalog))
            {
                presentedItemCatalog = currentCatalog;
                presenter.RefreshCatalog();
            }
            if (pendingWorldDrop != null
                && Time.unscaledTime - worldDropStartedAt >= RequestTimeoutSeconds)
            {
                if (!worldDropRetried && network.IsConnected)
                {
                    worldDropRetried = true;
                    worldDropStartedAt = Time.unscaledTime;
                    bool sent = pendingWorldDrop.IsStack
                        ? network.Send((InventoryV2StackWorldDropRequestDto)pendingWorldDrop.Request)
                        : network.Send((InventoryV2WorldDropRequestDto)pendingWorldDrop.Request);
                    if (!sent)
                        Debug.LogWarning("World drop retry could not be sent.");
                }
                else
                {
                    pendingWorldDrop = null;
                    equipmentPanel?.SetPending(false);
                    discardConfirmation?.Hide();
                    Debug.LogWarning("World drop timed out; refreshing ownership.");
                    ShowNotice("地面掉落请求超时，正在刷新背包");
                    NotifyAuthoritativeInventoryChanged();
                }
            }
            if (pendingDiscard != null
                && Time.unscaledTime - discardStartedAt >= RequestTimeoutSeconds)
            {
                if (!discardRetried && network.IsConnected)
                {
                    discardRetried = true;
                    discardStartedAt = Time.unscaledTime;
                    if (!network.Send(pendingDiscard))
                        Debug.LogWarning("V2 inventory discard retry could not be sent.");
                }
                else
                {
                    pendingDiscard = null;
                    if (discardConfirmation != null) discardConfirmation.Hide();
                    Debug.LogWarning("V2 inventory discard timed out; refreshing ownership.");
                    ShowNotice("丢弃请求超时，请稍后重试");
                    NotifyAuthoritativeInventoryChanged();
                }
            }
            if (pendingItemUse != null
                && Time.unscaledTime - itemUseStartedAt >= RequestTimeoutSeconds)
            {
                if (network.IsConnected)
                {
                    itemUseStartedAt = Time.unscaledTime;
                    if (!network.Send(pendingItemUse))
                        Debug.LogWarning("V2 item use retry could not be sent.");
                }
                else
                    itemUseStartedAt = Time.unscaledTime;
            }
            if (pendingOperation != null
                && Time.unscaledTime - operationStartedAt >= RequestTimeoutSeconds)
            {
                pendingOperation = null;
                pendingOperationSequence = 0;
                pendingUnequipTarget = -1;
                equipmentPanel?.SetPending(false);
                Debug.LogWarning("V2 equipment operation timed out; refreshing ownership.");
                ShowNotice("装备操作超时，请稍后重试");
                RequestSnapshot();
            }
            if (pendingSequence != 0
                && Time.unscaledTime - requestStartedAt >= RequestTimeoutSeconds)
            {
                CompleteSnapshotRequest();
                panel.SetLoading(false);
                Debug.LogWarning("V2 inventory snapshot request timed out.");
                ShowNotice("背包加载超时，请稍后重试");
            }
            if (refreshAfterPending && pendingSequence == 0
                && pendingOperation == null
                && (pendingItemUse == null || pendingItemUseMayHaveConsumed)
                && pendingDiscard == null && pendingWorldDrop == null
                && Time.unscaledTime - lastRequestAt >= MinimumSnapshotRequestInterval)
                RequestSnapshot();
        }

        public void Dispose()
        {
            dispatcher.Unregister(InventoryV2Protocol.SnapshotResult, HandleSnapshot);
            dispatcher.Unregister(InventoryV2Protocol.OperationResult, HandleOperationResult);
            dispatcher.Unregister(InventoryV2Protocol.WorldDropResult, HandleWorldDropResult);
            dispatcher.Unregister(InventoryV2Protocol.ItemUseResult, HandleItemUseResult);
            dispatcher.Unregister(InventoryV2Protocol.InventoryOperationResult,
                HandleDiscardResult);
            dispatcher.Unregister("error", HandleServerError);
            presenter.StackDoubleClicked -= HandleStackDoubleClicked;
            if (selectedDetailPanel != null)
            {
                selectedDetailPanel.LearnRequested -= HandleLearnRequested;
                selectedDetailPanel.UseRequested -= HandleUseRequested;
                selectedDetailPanel.SetActionResolver(null);
                selectedDetailPanel.SetAttributesResolver(null);
            }
            presenter.WorldDropOffered -= HandleWorldDropOffered;
            if (discardConfirmation != null)
                discardConfirmation.Confirmed -= HandleStackWorldDropConfirmed;
            panel.CloseClicked -= Close;
            presenter.UnequipRequested -= HandleUnequipRequested;
            if (equipmentPanel != null)
            {
                equipmentPanel.EquipRequested -= HandleEquipRequested;
                equipmentPanel.MoveRequested -= HandleMoveRequested;
            }
            Reset();
            presenter.Dispose();
            equipmentPanel?.Dispose();
            if (discardConfirmation != null) discardConfirmation.Dispose();
        }

        private void HandleSnapshot(string json)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.Log("[InventorySnapshot][RAW] " + json);
#endif
            InventoryV2SnapshotResultDto header;
            try { header = JsonUtility.FromJson<InventoryV2SnapshotResultDto>(json); }
            catch (Exception) { return; }
            if (header == null || header.requestSequence < 0) return;
            bool requested = header.requestSequence > 0
                && header.requestSequence == pendingSequence;
            if (header.requestSequence > 0 && !requested) return;

            InventorySnapshotProtocol protocol =
                InventoryV2Codec.ReadSnapshot(json, out InventoryOwnershipSnapshot next, out string error);
            if (protocol != InventorySnapshotProtocol.V2)
            {
                if (requested)
                {
                    CompleteSnapshotRequest();
                    panel.SetLoading(false);
                    ShowResultNotice(header.message, "背包加载失败，请稍后重试");
                }
                Debug.LogWarning("V2 inventory snapshot rejected: " + error);
                RefreshIfNeeded();
                return;
            }
            if (requested && !header.success)
            {
                CompleteSnapshotRequest();
                panel.SetLoading(false);
                ShowResultNotice(header.message, "背包加载失败，请稍后重试");
                RefreshIfNeeded();
                return;
            }
            if (next.CharacterDbId != characterDbId)
            {
                if (requested)
                {
                    CompleteSnapshotRequest();
                    panel.SetLoading(false);
                    ShowNotice("背包角色信息不匹配，请重新连接");
                }
                Debug.LogWarning("V2 inventory snapshot belongs to another character.");
                RefreshIfNeeded();
                return;
            }
            if (!string.IsNullOrEmpty(header.sceneId)
                && !string.IsNullOrEmpty(sceneId)
                && !string.Equals(header.sceneId, sceneId, StringComparison.Ordinal))
            {
                if (requested)
                {
                    CompleteSnapshotRequest();
                    panel.SetLoading(false);
                    ShowNotice("背包场景信息不匹配，请重新连接");
                }
                Debug.LogWarning("V2 inventory snapshot belongs to another scene.");
                RefreshIfNeeded();
                return;
            }
            if (snapshot != null && (next.InventoryRevision < snapshot.InventoryRevision
                || next.EquipmentRevision < snapshot.EquipmentRevision))
            {
                if (requested)
                {
                    CompleteSnapshotRequest();
                    panel.SetLoading(false);
                    ShowNotice("背包数据已变化，请稍后刷新");
                }
                Debug.LogWarning("V2 inventory snapshot is older than the local snapshot.");
                RefreshIfNeeded();
                return;
            }

            InventoryOwnershipSnapshot previous = snapshot;
            InventoryBagLayout previousLayout = presenter.CurrentLayout;
            try
            {
                InventoryBagLayout layout = presenter.PrepareSnapshot(next);
                presenter.PresentSnapshot(next, layout);
                equipmentPanel?.Render(next);
                panel.SetLoading(false);
                snapshot = next;
                if (pendingSequence != 0)
                    CompleteSnapshotRequest();
                try { presenter.PersistLayout(); }
                catch (Exception storageError)
                {
                    Debug.LogWarning("V2 bag layout could not be saved: " + storageError.Message);
                }
                Debug.Log("[InventoryV2][RX] requestSequence=" + header.requestSequence
                    + " capacity=" + next.Capacity
                    + " inventoryRevision=" + next.InventoryRevision
                    + " equipmentRevision=" + next.EquipmentRevision
                    + " reason=" + (header.reason ?? string.Empty));
                if (header.equipmentProtocolVersion == InventoryV2Protocol.EquipmentProtocolVersion)
                    LogV3Snapshot(next);
            }
            catch (Exception exception)
            {
                snapshot = previous;
                try
                {
                    presenter.RestoreSnapshot(previous, previousLayout);
                    equipmentPanel?.Render(previous);
                }
                catch (Exception restoreError)
                {
                    presenter.Clear();
                    equipmentPanel?.Clear();
                    IsOpen = false;
                    panel.Hide();
                    Debug.LogError("V2 inventory view rollback failed: " + restoreError.Message);
                }
                if (requested)
                {
                    CompleteSnapshotRequest();
                    panel.SetLoading(false);
                }
                Debug.LogError("V2 inventory snapshot could not be displayed: " + exception.Message);
            }
            RefreshIfNeeded();
        }

        private void LogV3Snapshot(InventoryOwnershipSnapshot current)
        {
            var unknownStacks = new SortedSet<string>(StringComparer.Ordinal);
            var unknownEquipment = new SortedSet<string>(StringComparer.Ordinal);
            int bagEquipment = 0;
            int equippedEquipment = 0;
            bool catalogMatches = catalog != null
                && current.CatalogMatchesClient
                && InventoryV2Protocol.IsCatalogCompatible(
                    catalog.EquipmentCatalogVersion, current.CatalogVersion);
            foreach (InventoryStackState item in current.Stacks)
            {
                if (catalog == null || !catalog.TryGet(item.ItemId, out ItemDefinition _))
                    unknownStacks.Add(item.ItemId);
            }
            foreach (InventoryEquipmentState item in current.Equipment)
            {
                if (item.SlotCode == 0) bagEquipment++;
                else equippedEquipment++;
                if (catalog == null || !catalog.TryGetByEquipmentId(
                    item.EquipmentId, out ItemDefinition _))
                    unknownEquipment.Add(item.EquipmentId);
            }
            Debug.Log("[InventoryV3][RX] protocol=3 success=true"
                + " capacity=" + current.Capacity
                + " stackItems=" + current.Stacks.Count
                + " equipmentItems=" + current.Equipment.Count
                + " bagEquipment=" + bagEquipment
                + " equippedEquipment=" + equippedEquipment
                + " catalogMatchesClient=" + catalogMatches
                + " unknownStackItemIds=[" + string.Join(",", unknownStacks) + "]"
                + " unknownEquipmentIds=[" + string.Join(",", unknownEquipment) + "]"
                + " applied=true");
        }

        private void HandleEquipRequested(string identity, int targetSlotCode)
        {
            InventoryEquipmentState source = FindEquipment(identity);
            if (source == null || source.SlotCode != 0
                || !IsCompatible(source, targetSlotCode)) return;
            var intent = new InventoryEquipmentIntent(
                InventoryEquipmentOperationKind.Equip, identity, 0, targetSlotCode);
            SendOperation(intent, sequence => new InventoryV2EquipRequestDto(
                equipmentOperationSessionId, sequence, source.ItemInstanceId, targetSlotCode));
        }

        public bool TryUseStack(long stackId)
        {
            return stackId > 0 && TryUseStackInternal(stackId, null);
        }

        public bool CanUseItem(string itemId)
        {
            CultivationSessionRuntime items = CultivationSessionRuntime.Instance;
            return items != null && items.TryGetItem(itemId, out var item)
                && IsUsableItem(item);
        }

        private (bool CanLearn, bool CanUse) ResolveSelectedItemActions(string identity)
        {
            InventoryOwnershipSnapshot presented = presenter.PresentedSnapshot;
            if (presented == null)
                return default;

            foreach (InventoryStackState stack in presented.Stacks)
            {
                if (stack.Identity != identity || stack.Count <= 0)
                    continue;

                CultivationItemDefinitionDto item = null;
                CultivationSessionRuntime.Instance?.TryGetItem(stack.ItemId, out item);
                bool canLearn = IsForgingBlueprint(stack.ItemId)
                    || item?.IsSkillBook == true
                    || (item?.kind == "techniquebook" && !string.IsNullOrWhiteSpace(item.learnTechniqueId))
                    || (item?.kind == "recipebook" && !string.IsNullOrWhiteSpace(item.learnRecipeId));
                return (canLearn, IsUsableItem(item));
            }
            return default;
        }

        private string ResolveSelectedItemAttributes(string identity)
        {
            InventoryOwnershipSnapshot presented = presenter.PresentedSnapshot;
            CultivationSessionRuntime directory = CultivationSessionRuntime.Instance;
            if (presented == null || presented.CharacterDbId != characterDbId || directory == null)
                return string.Empty;

            foreach (InventoryStackState stack in presented.Stacks)
                if (stack.Identity == identity && directory.TryGetItem(stack.ItemId, out var stackDefinition))
                    return stack.RemainingUses > 0
                        ? "剩余次数 " + stack.RemainingUses + "/50"
                        : InventoryItemAttributeText.Format(stackDefinition, FindLearningTarget(stackDefinition, directory));

            foreach (InventoryEquipmentState equipment in presented.Equipment)
                if (equipment.SlotCode == 0 && equipment.Identity == identity
                    && directory.TryGetItemByEquipmentId(equipment.EquipmentId, out var equipmentDefinition))
                    return InventoryItemAttributeText.Format(equipmentDefinition);

            return string.Empty;
        }

        private string FindLearningTarget(CultivationItemDefinitionDto item, CultivationSessionRuntime directory)
        {
            switch (item.kind)
            {
                case "skillbook" when !string.IsNullOrWhiteSpace(item.learnSkillId):
                    if (skillRegistry == null)
                        skillRegistry = UnityEngine.Object.FindObjectOfType<技能注册表>();
                    return skillRegistry != null && skillRegistry.TryGet(item.learnSkillId, out var skill)
                        ? skill.技能名称 : null;
                case "techniquebook" when !string.IsNullOrWhiteSpace(item.learnTechniqueId):
                    return directory.IsConfigurationReady && directory.Catalog.techniques != null
                        ? Array.Find(directory.Catalog.techniques,
                            technique => technique?.techniqueId == item.learnTechniqueId)?.name : null;
                case "recipebook" when !string.IsNullOrWhiteSpace(item.learnRecipeId):
                    AlchemySessionRuntime alchemy = AlchemySessionRuntime.Instance;
                    return alchemy != null && alchemy.IsCatalogCurrent && alchemy.Catalog != null
                        ? Array.Find(alchemy.Catalog, recipe => recipe?.recipeId == item.learnRecipeId)?.name : null;
                case "forgingblueprint" when !string.IsNullOrWhiteSpace(item.learnForgingRecipeId):
                    锻造会话运行时 forging = 锻造会话运行时.Instance;
                    return forging != null && forging.IsCatalogCurrent && forging.Catalog != null
                        ? Array.Find(forging.Catalog, recipe => recipe?.recipeId == item.learnForgingRecipeId)?.name : null;
                default:
                    return null;
            }
        }

        private bool IsForgingBlueprint(string itemId)
        {
            return catalog != null && catalog.TryGet(itemId, out ItemDefinition item)
                && item.Category == ItemCategory.ForgingBlueprint;
        }

        private static bool IsUsableItem(CultivationItemDefinitionDto item)
        {
            return item?.kind == "consumable"
                || (item?.kind == "pill" && item.HasUsablePillEffect);
        }

        private void HandleStackDoubleClicked(string identity)
        {
            HandleStackActivation(identity, allowLearn: true, allowUse: true);
        }

        private void HandleLearnRequested(string identity)
        {
            HandleStackActivation(identity, allowLearn: true, allowUse: false, allowSkillLearn: true);
        }

        private void HandleUseRequested(string identity)
        {
            HandleStackActivation(identity, allowLearn: false, allowUse: true);
        }

        private void HandleStackActivation(
            string identity, bool allowLearn, bool allowUse, bool allowSkillLearn = false)
        {
            if (snapshot == null || string.IsNullOrEmpty(identity)
                || characterDbId <= 0 || !network.IsConnected)
                return;
            foreach (InventoryStackState stack in snapshot.Stacks)
            {
                if (stack.Identity != identity || stack.Count <= 0)
                    continue;
                CultivationSessionRuntime cultivation = CultivationSessionRuntime.Instance;
                CultivationItemDefinitionDto item = null;
                bool hasCatalogItem = cultivation != null
                    && cultivation.TryGetItem(stack.ItemId, out item);
                if (item?.kind == "skillbook")
                {
                    if (allowSkillLearn && item.IsSkillBook)
                        ShowNotice(learnSkill?.Invoke(stack.StackId) == true
                            ? "正在确认技能学习结果" : "当前无法学习该技能，请稍后重试");
                    return;
                }
                if (allowLearn && IsForgingBlueprint(stack.ItemId))
                {
                    Game.Forging.Runtime.锻造会话运行时 forging =
                        Game.Forging.Runtime.锻造会话运行时.Instance;
                    if (forging == null)
                        Debug.LogWarning("[Forging][Learn] rejected reason=runtime_missing"
                            + " stackId=" + stack.StackId
                            + " inventoryRevision=" + snapshot.InventoryRevision);
                    if (forging == null || !forging.TryLearn(stack.StackId, snapshot))
                        ShowNotice(forging != null && forging.HasUnresolvedLearn
                            ? "图谱学习结果待确认"
                            : "当前无法学习该图谱，请确认背包与锻造会话");
                    else
                        ShowNotice("正在确认图谱学习结果");
                    return;
                }
                if (allowLearn && hasCatalogItem && item.kind == "techniquebook")
                {
                    BindLearnRuntime(cultivation);
                    if (!cultivation.TryResolveTechniqueBook(stack.ItemId, out _, out _))
                    {
                        Debug.LogWarning("[Cultivation][BookLookup] itemId="
                            + stack.ItemId + " mapping not ready configurationReady="
                            + cultivation.IsConfigurationReady
                            + " techniqueCatalogPresent=" + (cultivation.Catalog != null)
                            + " techniqueCount=" + (cultivation.Catalog?.techniques?.Length ?? 0)
                            + " snapshotAccepted=" + cultivation.HasAcceptedSnapshot
                            + " learnTechniqueId="
                            + item.learnTechniqueId);
                        cultivation.Refresh();
                        ShowNotice("功法目录待同步，请稍后重试");
                    }
                    else if (!cultivation.TryLearn(stack.StackId))
                        ShowNotice(cultivation.HasUnresolvedOperation
                            ? "功法学习结果待确认" : "当前无法学习该功法");
                    return;
                }
                if (allowLearn && hasCatalogItem && item.kind == "recipebook")
                {
                    AlchemySessionRuntime alchemy = AlchemySessionRuntime.Instance;
                    BindAlchemyLearnRuntime(alchemy);
                    if (alchemy == null || !alchemy.IsCatalogCurrent
                        || !alchemy.IsSnapshotCurrent
                        || string.IsNullOrWhiteSpace(item.learnRecipeId))
                    {
                        alchemy?.EnsureLearningData();
                        ShowNotice("丹方目录或学习状态待同步，请稍后重试");
                    }
                    else if (!alchemy.TryLearn(stack.StackId, stack.ItemId,
                        snapshot))
                        ShowNotice(alchemy.HasUnresolvedLearn
                            ? "丹方学习结果待确认"
                            : "丹方已学会或当前无法学习");
                    else
                        ShowNotice("正在确认丹方学习结果");
                    return;
                }
                if (allowLearn && (!hasCatalogItem || (item.kind != "pill"
                        && item.kind != "consumable"))
                    && catalog != null && catalog.TryGet(stack.ItemId,
                        out ItemDefinition localBook)
                    && localBook.DisplayCategory == ItemDetailCategory.Cultivation)
                {
                    Debug.LogWarning("[Cultivation][BookLookup] itemId="
                        + stack.ItemId + " catalogItemKnown=" + hasCatalogItem
                        + " serverKind=" + (item?.kind ?? "<missing>")
                        + " runtimePresent=" + (cultivation != null)
                        + " sessionReady="
                        + (cultivation != null && cultivation.IsSessionReady)
                        + " itemCatalogPresent=" + (cultivation?.ItemCatalog != null)
                        + " catalogItemCount="
                        + (cultivation?.ItemCatalog?.items?.Length ?? 0)
                        + " techniqueCatalogPresent=" + (cultivation?.Catalog != null)
                        + " techniqueCount="
                        + (cultivation?.Catalog?.techniques?.Length ?? 0)
                        + " snapshotAccepted="
                        + (cultivation != null && cultivation.HasAcceptedSnapshot)
                        + " configurationReady="
                        + (cultivation != null && cultivation.IsConfigurationReady));
                    cultivation?.Refresh();
                    ShowNotice(hasCatalogItem ? "该物品不是功法书"
                        : cultivation?.ItemCatalog != null
                            ? "该书不在有效物品目录中，当前不可学习"
                            : "功法书目录待同步，请稍后重试");
                    return;
                }
                if (allowUse && ((hasCatalogItem && (item.kind == "pill"
                        || item.kind == "consumable"))
                    || (catalog != null && catalog.TryGet(stack.ItemId,
                        out ItemDefinition definition)
                        && definition.Category == ItemCategory.Consumable)))
                    TryUseStackInternal(stack.StackId, identity);
                return;
            }
        }

        private void BindLearnRuntime(CultivationSessionRuntime current)
        {
            if (learnRuntime == current)
                return;
            if (learnRuntime != null)
            {
                learnRuntime.LearnResultReceived -= HandleLearnResult;
                learnRuntime.LearnRejected -= HandleLearnRejected;
            }
            learnRuntime = current;
            if (learnRuntime != null)
            {
                learnRuntime.LearnResultReceived += HandleLearnResult;
                learnRuntime.LearnRejected += HandleLearnRejected;
            }
        }

        private void HandleLearnResult(CultivationWriteResultDto result)
        {
            if (result != null && result.success)
                ShowNotice("功法学习成功");
        }

        private void HandleLearnRejected(string errorCode)
        {
            ShowNotice(errorCode == "cultivation_already_learned"
                ? "该功法已经学会"
                : errorCode == "cultivation_learning_requirement"
                    ? "不满足功法书的职业要求"
                    : errorCode == "cultivation_persist_failed"
                        ? "学习结果待确认，正在同步"
                        : errorCode == "inventory_revision_conflict"
                            ? "背包已变化，请重新确认功法书"
                            : "功法学习失败，请刷新后重试");
        }

        private void BindAlchemyLearnRuntime(AlchemySessionRuntime current)
        {
            if (alchemyLearnRuntime == current)
                return;
            if (alchemyLearnRuntime != null)
            {
                alchemyLearnRuntime.LearnResultReceived -= HandleAlchemyLearnResult;
                alchemyLearnRuntime.LearnTimedOut -= HandleAlchemyLearnTimeout;
            }
            alchemyLearnRuntime = current;
            if (alchemyLearnRuntime != null)
            {
                alchemyLearnRuntime.LearnResultReceived += HandleAlchemyLearnResult;
                alchemyLearnRuntime.LearnTimedOut += HandleAlchemyLearnTimeout;
            }
        }

        private void HandleAlchemyLearnResult(AlchemyResponseDto result)
        {
            ShowNotice(!string.IsNullOrWhiteSpace(result.message)
                ? result.message
                : result.success ? "丹方学习成功"
                : result.errorCode == "alchemy_already_learned"
                    ? "该丹方已经学会"
                    : "丹方学习失败，请刷新后重试");
        }

        private void HandleAlchemyLearnTimeout()
        {
            ShowNotice("丹方学习结果待确认，请等待同步或重新进场恢复");
        }

        private bool TryUseStackInternal(long stackId, string identity)
        {
            if (snapshot == null || characterDbId <= 0 || !network.IsConnected
                || string.IsNullOrWhiteSpace(itemUseOperationSessionId)
                || pendingSequence != 0 || pendingOperation != null || pendingItemUse != null
                || pendingDiscard != null || pendingWorldDrop != null
                || clientTick == null || combatRevision == null) return false;
            if (unresolvedItemUseCharacterDbId == characterDbId)
            {
                if (progressionReady == null || !progressionReady())
                {
                    ShowNotice("物品使用结果待同步，请稍后重试");
                    return false;
                }
                unresolvedItemUseCharacterDbId = 0;
            }
            foreach (InventoryStackState stack in snapshot.Stacks)
            {
                if ((stackId > 0 ? stack.StackId != stackId : stack.Identity != identity)
                    || stack.Count <= 0) continue;
                if (!CanUseItem(stack.ItemId))
                {
                    CultivationSessionRuntime items = CultivationSessionRuntime.Instance;
                    if (items == null || !items.TryGetItem(stack.ItemId,
                        out CultivationItemDefinitionDto serverItem))
                        ShowNotice("物品目录待同步，请稍后重试");
                    else
                        ShowNotice(serverItem.kind == "pill"
                            ? "该丹药尚无可用功效，暂不可服用"
                            : "该物品当前不可使用");
                    return false;
                }
                string cooldownKey = GetItemUseCooldownKey(stack.ItemId);
                if (itemCooldownDeadlines.TryGetValue(cooldownKey, out float cooldownDeadline)
                    && Time.unscaledTime < cooldownDeadline)
                {
                    Debug.Log("V2 item use is still cooling down: " + stack.ItemId);
                    ShowNotice("物品冷却中");
                    return false;
                }
                long tick = Math.Max(Math.Max(1L, clientTick()), lastItemUseTick + 1);
                long expectedCombatRevision = combatRevision();
                long sequence = nextItemUseSequence + 1;
                var request = new InventoryV2ItemUseRequestDto(
                    itemUseOperationSessionId, sequence, snapshot.InventoryRevision,
                    stack.StackId, tick);
                if (InventoryStackState.IsTeleportStone(stack.ItemId))
                {
                    if (UnityEngine.Object.FindObjectOfType<GameSession>()?.TryUseTeleportStone(request) != true)
                    {
                        ShowNotice("当前无法传送，请稍后重试");
                        return false;
                    }
                    nextItemUseSequence = sequence;
                    lastItemUseTick = tick;
                    return true;
                }
                if (!network.Send(request))
                {
                    Debug.LogWarning("V2 item use request could not be sent.");
                    ShowNotice("物品使用请求发送失败，请稍后重试");
                    return false;
                }
                nextItemUseSequence = sequence;
                lastItemUseTick = tick;
                pendingItemUse = request;
                pendingItemUseCooldownKey = cooldownKey;
                pendingItemUseCombatRevision = expectedCombatRevision;
                itemUseStartedAt = Time.unscaledTime;
                Debug.Log("[ItemUse][TX] requestSequence=" + sequence
                    + " sessionId=" + itemUseOperationSessionId
                    + " stackId=" + stack.StackId
                    + " itemId=" + stack.ItemId
                    + " inventoryRevision=" + snapshot.InventoryRevision
                    + " clientTick=" + tick);
                return true;
            }
            return false;
        }

        private void HandleWorldDropOffered(string identity)
        {
            if (!IsOpen || snapshot == null
                || pendingSequence != 0 || pendingOperation != null
                || pendingItemUse != null || pendingDiscard != null
                || pendingWorldDrop != null) return;
            foreach (InventoryStackState stack in snapshot.Stacks)
            {
                if (stack.Identity != identity || stack.Count <= 0) continue;
                if (!Guid.TryParseExact(worldDropOperationSessionId, "D", out _))
                {
                    ShowNotice("地面掉落暂不可用，请重新进入场景");
                    return;
                }
                if (discardConfirmation == null) return;
                string name = catalog != null
                    && catalog.TryGet(stack.ItemId, out ItemDefinition definition)
                    ? definition.DisplayName : "物品";
                discardConfirmation.Show(identity, name, stack.Count);
                discardConfirmation.SetConfirmEnabled(true);
                return;
            }
            foreach (InventoryEquipmentState equipment in snapshot.Equipment)
            {
                if (equipment.Identity != identity || equipment.SlotCode != 0) continue;
                SendWorldDrop(equipment);
                return;
            }
        }

        private void SendWorldDrop(InventoryEquipmentState equipment)
        {
            if (equipment.IsBound)
            {
                ShowNotice("绑定装备不能丢到地上");
                return;
            }
            if (!Guid.TryParseExact(worldDropOperationSessionId, "D", out _))
            {
                Debug.LogWarning("Equipment world drop requires the current enter-world session ID.");
                ShowNotice("装备地面掉落暂不可用，请重新进入场景");
                return;
            }
            var request = new InventoryV2WorldDropRequestDto(worldDropOperationSessionId,
                ++nextWorldDropOperationSequence, equipment.ItemInstanceId);
            if (!network.Send(request))
            {
                nextWorldDropOperationSequence--;
                Debug.LogWarning("Equipment world drop request could not be sent.");
                ShowNotice("装备地面掉落请求发送失败，请稍后重试");
                return;
            }
            pendingWorldDrop = new PendingWorldDrop(request, request.requestSequence,
                0, 0, equipment.ItemInstanceId);
            worldDropStartedAt = Time.unscaledTime;
            worldDropRetried = false;
            equipmentPanel?.SetPending(true);
            Debug.Log("[WorldDrop][TX] requestSequence=" + request.requestSequence
                + " itemInstanceId=" + request.itemInstanceId);
        }

        private void HandleStackWorldDropConfirmed(string identity, int count)
        {
            if (snapshot == null || characterDbId <= 0 || !network.IsConnected
                || !Guid.TryParseExact(worldDropOperationSessionId, "D", out _)
                || pendingSequence != 0 || pendingOperation != null
                || pendingItemUse != null || pendingDiscard != null
                || pendingWorldDrop != null)
            {
                discardConfirmation?.ShowFailure();
                ShowNotice("地面掉落暂不可用，请稍后重试");
                return;
            }
            foreach (InventoryStackState stack in snapshot.Stacks)
            {
                if (stack.Identity != identity) continue;
                if (count <= 0 || count > stack.Count)
                {
                    discardConfirmation?.ShowFailure();
                    ShowNotice("掉落数量无效");
                    return;
                }
                var request = new InventoryV2StackWorldDropRequestDto(
                    worldDropOperationSessionId, ++nextWorldDropOperationSequence,
                    snapshot.InventoryRevision, stack.StackId, count);
                if (!network.Send(request))
                {
                    nextWorldDropOperationSequence--;
                    discardConfirmation?.ShowFailure();
                    ShowNotice("地面掉落请求发送失败，请稍后重试");
                    return;
                }
                pendingWorldDrop = new PendingWorldDrop(request, request.requestSequence,
                    stack.StackId, count, null);
                worldDropStartedAt = Time.unscaledTime;
                worldDropRetried = false;
                discardConfirmation?.SetPending(true);
                Debug.Log("[WorldDrop][TX] requestSequence=" + request.requestSequence
                    + " stackId=" + request.stackId + " count=" + request.count);
                return;
            }
            discardConfirmation?.Hide();
        }

        private void HandlePermanentDiscardConfirmed(string identity, int count)
        {
            if (snapshot == null || characterDbId <= 0 || !network.IsConnected
                || pendingSequence != 0 || pendingOperation != null
                || pendingItemUse != null || pendingDiscard != null
                || pendingWorldDrop != null)
            {
                discardConfirmation?.ShowFailure();
                ShowNotice("丢弃暂不可用，请稍后重试");
                return;
            }
            foreach (InventoryStackState stack in snapshot.Stacks)
            {
                if (stack.Identity != identity) continue;
                if (count <= 0 || count > stack.Count)
                {
                    discardConfirmation?.ShowFailure();
                    ShowNotice("丢弃数量无效");
                    return;
                }
                long sequence = Math.Max(nextInventoryOperationSequence + 1,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                nextInventoryOperationSequence = sequence;
                var request = new InventoryV2DiscardRequestDto(
                    sequence, snapshot.InventoryRevision, stack.StackId, count);
                pendingDiscard = request;
                discardRetried = false;
                discardStartedAt = Time.unscaledTime;
                discardConfirmation?.SetPending(true);
                if (!network.Send(request))
                {
                    pendingDiscard = null;
                    discardConfirmation?.ShowFailure();
                    Debug.LogWarning("V2 inventory discard request could not be sent.");
                    ShowNotice("丢弃请求发送失败，请稍后重试");
                }
                return;
            }
            discardConfirmation?.Hide();
        }

        private void HandleWorldDropResult(string json)
        {
            InventoryV2WorldDropResultDto result;
            try { result = JsonUtility.FromJson<InventoryV2WorldDropResultDto>(json); }
            catch (Exception) { return; }
            if (pendingWorldDrop == null || result == null
                || result.requestSequence != pendingWorldDrop.Sequence
                || result.worldDropOperationSessionId != worldDropOperationSessionId
                || result.assetKind != (pendingWorldDrop.IsStack ? "stack" : "equipment")
                || (result.characterDbId > 0 && result.characterDbId != characterDbId)) return;

            PendingWorldDrop request = pendingWorldDrop;
            pendingWorldDrop = null;
            equipmentPanel?.SetPending(false);
            Debug.Log("[WorldDrop][RX] requestSequence=" + result.requestSequence
                + " assetKind=" + result.assetKind + " success=" + result.success
                + " status=" + (result.status ?? "")
                + " inventoryRevision=" + result.inventoryRevision);
            if (!result.success)
            {
                Debug.LogWarning("World drop failed: "
                    + result.errorCode + " " + result.message);
                ShowResultNotice(result.message, "地面掉落失败，物品未改变");
                bool repair = result.requiresSnapshot || NeedsSnapshot(result.errorCode)
                    || result.inventoryRevision > snapshot.InventoryRevision;
                if (repair)
                {
                    discardConfirmation?.Hide();
                    NotifyAuthoritativeInventoryChanged();
                }
                else
                {
                    RefreshIfNeeded();
                    if (request.IsStack)
                        discardConfirmation?.ShowFailure();
                }
                return;
            }
            discardConfirmation?.Hide();
            if (result.status != "active" && result.status != "pending_scene_sync"
                && result.status != "expired")
            {
                Debug.LogWarning("World drop returned an unsupported status: "
                    + result.status);
                ShowNotice("地面掉落结果异常，正在刷新背包");
                NotifyAuthoritativeInventoryChanged();
                return;
            }
            InventoryOwnershipSnapshot next = null;
            string error = string.Empty;
            bool applied;
            if (request.IsStack)
            {
                applied = (result.removedItemInstanceIds == null
                        || result.removedItemInstanceIds.Length == 0)
                    && (result.equipmentItemChanges == null
                        || result.equipmentItemChanges.Length == 0)
                    && !result.requiresSnapshot
                    && InventoryV2DeltaChanges.TryApplyWorldDrop(snapshot,
                        result.inventoryRevision, request.StackId, request.Count,
                        result.stackItemChanges, out next, out error)
                        == InventoryV2DeltaResult.Applied;
                if (!applied)
                {
                    next = null;
                    if (string.IsNullOrEmpty(error))
                        error = "Stack world drop delta is incomplete or out of date.";
                }
            }
            else
            {
                applied = (result.stackItemChanges == null
                        || result.stackItemChanges.Length == 0)
                    && InventoryEquipmentChanges.TryApplyWorldDrop(snapshot,
                        request.ItemInstanceId, result, out next, out error);
                if (!applied)
                {
                    next = null;
                    if (string.IsNullOrEmpty(error))
                        error = "Equipment world drop delta is incomplete or out of date.";
                }
            }
            if (!applied)
            {
                Debug.LogWarning("World drop needs snapshot: " + error);
                ShowNotice("地面掉落结果未完整同步，正在刷新背包");
                NotifyAuthoritativeInventoryChanged();
                return;
            }
            try
            {
                presenter.ApplySnapshot(next);
                equipmentPanel?.Render(next);
                snapshot = next;
                RefreshIfNeeded();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("World drop display failed: " + exception.Message);
                ShowNotice("地面掉落结果显示失败，正在刷新背包");
                NotifyAuthoritativeInventoryChanged();
                return;
            }
            ShowNotice(result.status == "expired"
                ? "物品已离开背包，但地面掉落已过期" : "物品已丢到地上");
        }

        private void HandleDiscardResult(string json)
        {
            InventoryV2DiscardResultDto result;
            try { result = JsonUtility.FromJson<InventoryV2DiscardResultDto>(json); }
            catch (Exception) { return; }
            if (pendingDiscard == null || result == null
                || result.requestSequence != pendingDiscard.requestSequence
                || result.operationType != "discard") return;

            InventoryV2DiscardRequestDto request = pendingDiscard;
            pendingDiscard = null;
            if (!result.success)
            {
                Debug.LogWarning("V2 inventory discard failed: "
                    + result.errorCode + " " + result.message);
                ShowResultNotice(result.message, "丢弃失败，请稍后重试");
                if (result.requiresSnapshot
                    || result.inventoryRevision > snapshot.InventoryRevision
                    || result.serverInventoryRevision > snapshot.InventoryRevision)
                {
                    discardConfirmation?.Hide();
                    NotifyAuthoritativeInventoryChanged();
                }
                else
                {
                    discardConfirmation?.ShowFailure();
                    RefreshIfNeeded();
                }
                return;
            }
            if (result.requiresSnapshot
                || (result.serverInventoryRevision > 0
                    && result.serverInventoryRevision != result.inventoryRevision))
            {
                discardConfirmation?.Hide();
                NotifyAuthoritativeInventoryChanged();
                return;
            }
            InventoryV2DeltaResult applied = InventoryV2DeltaChanges.TryApplyDiscard(
                snapshot, result.inventoryRevision, request.stackId, request.count,
                result.stackItemChanges, out InventoryOwnershipSnapshot next, out string error);
            if (applied != InventoryV2DeltaResult.Applied)
            {
                Debug.LogWarning("V2 inventory discard needs snapshot: " + error);
                ShowNotice("丢弃结果未完整同步，请刷新背包");
                discardConfirmation?.Hide();
                NotifyAuthoritativeInventoryChanged();
                return;
            }
            try
            {
                presenter.ApplySnapshot(next);
                snapshot = next;
                discardConfirmation?.Hide();
                RefreshIfNeeded();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("V2 inventory discard display failed: " + exception.Message);
                ShowNotice("丢弃结果显示失败，请刷新背包");
                discardConfirmation?.Hide();
                NotifyAuthoritativeInventoryChanged();
                return;
            }
            ShowNotice("物品已丢弃");
        }

        private void HandleItemUseResult(string json)
        {
            InventoryV2ItemUseResultDto result;
            try { result = JsonUtility.FromJson<InventoryV2ItemUseResultDto>(json); }
            catch (Exception) { return; }
            if (pendingItemUse == null || result == null
                || result.requestSequence != pendingItemUse.requestSequence
                || !string.Equals(result.itemUseOperationSessionId,
                    pendingItemUse.itemUseOperationSessionId,
                    StringComparison.Ordinal)) return;

            long usedStackId = pendingItemUse.stackId;
            long expectedCombatRevision = pendingItemUseCombatRevision;
            long? totalJingjieExp = null;
            bool validBalances = CultivationCatalogWireState.TryReadOptionalNonNegativeLong(
                    json, "jingjieExp", out long? jingjieExp)
                && CultivationCatalogWireState.TryReadOptionalNonNegativeLong(
                    json, "totalJingjieExp", out totalJingjieExp);
            if (!validBalances)
            {
                Debug.LogWarning("V2 item use contains invalid jingjie balance.");
                jingjieExp = null;
                totalJingjieExp = null;
            }
            bool effectPending = string.Equals(result.status, "effect_pending",
                StringComparison.Ordinal);
            if (jingjieExp != null || totalJingjieExp != null || effectPending
                || result.gainedJingjieExp > 0 || pendingItemUseMayHaveConsumed)
                applyJingjieBalance?.Invoke(jingjieExp, totalJingjieExp);
            ApplyItemUseCooldown(pendingItemUseCooldownKey, result);
            if (effectPending)
            {
                pendingItemUseMayHaveConsumed = true;
                itemUseStartedAt = Time.unscaledTime;
                NotifyAuthoritativeInventoryChanged();
                ShowNotice("物品效果待确认，正在同步");
                return;
            }
            bool mayHaveConsumed = pendingItemUseMayHaveConsumed;
            pendingItemUse = null;
            pendingItemUseCooldownKey = null;
            pendingItemUseMayHaveConsumed = false;
            if (!result.success)
            {
                Debug.LogWarning("V2 item use failed: " + result.errorCode + " " + result.message);
                ShowResultNotice(result.message, "物品使用失败，请稍后重试");
                if (result.requiresInventorySnapshot || result.requiresSnapshot
                    || mayHaveConsumed
                    || (snapshot != null
                        && result.inventoryRevision > snapshot.InventoryRevision))
                    NotifyAuthoritativeInventoryChanged();
                else RefreshIfNeeded();
                return;
            }

            if (ContainsPotionEffect(result.effects))
                playPotionAnimation?.Invoke();

            InventoryOwnershipSnapshot next = null;
            string error = null;
            InventoryV2DeltaResult applied =
                result.requiresInventorySnapshot || result.requiresSnapshot
                    ? InventoryV2DeltaResult.NeedsSnapshot
                    : InventoryV2DeltaChanges.TryApplyItemUse(
                        snapshot, result.inventoryRevision, usedStackId,
                        result.stackItemChanges, out next, out error);
            if (applied == InventoryV2DeltaResult.Applied)
            {
                try
                {
                    presenter.ApplySnapshot(next);
                    snapshot = next;
                    equipmentPanel?.Render(next);
                }
                catch (Exception exception)
                {
                    Debug.LogWarning("V2 item use display failed: " + exception.Message);
                    ShowNotice("物品使用结果显示失败，请刷新背包");
                    NotifyAuthoritativeInventoryChanged();
                    return;
                }
                ShowNotice("物品使用成功");
            }
            else if (applied == InventoryV2DeltaResult.NeedsSnapshot)
            {
                Debug.LogWarning("V2 item use needs snapshot: " + error);
                ShowNotice("物品使用结果未完整同步，请刷新背包");
                NotifyAuthoritativeInventoryChanged();
            }
            if (applyHealth != null && result.currentHp >= 0
                && json.IndexOf("\"currentHp\"", StringComparison.Ordinal) >= 0)
                applyHealth(result.currentHp, expectedCombatRevision);
            ApplyItemUseManaIfPresent(result, json);
            PublishItemUseEffects(result.effects);
            RefreshIfNeeded();
        }

        private void HandleServerError(string json)
        {
            ServerErrorMessage error;
            try { error = JsonUtility.FromJson<ServerErrorMessage>(json); }
            catch (Exception) { return; }
            if (error != null && pendingSequence != 0 && error.requestSequence == pendingSequence
                && error.requestMsgId == InventoryV2Protocol.Snapshot
                && error.errorCode == "inventory_snapshot_rate_limited")
            {
                HandleSnapshotRateLimit();
                return;
            }
            if (error != null && MovementReferenceError.DidNotExecute(error.errorCode)
                && pendingWorldDrop != null && error.requestSequence == pendingWorldDrop.Sequence
                && error.requestMsgId == (pendingWorldDrop.IsStack
                    ? InventoryV2Protocol.InventoryWorldDrop : InventoryV2Protocol.EquipmentWorldDrop))
            {
                nextWorldDropOperationSequence = pendingWorldDrop.Sequence - 1;
                pendingWorldDrop = null;
                discardConfirmation?.ShowFailure();
                equipmentPanel?.SetPending(false);
                ShowNotice("移动状态已变化，请重新丢弃物品");
                return;
            }
            if (error == null
                || !string.Equals(error.requestMsgId, InventoryV2Protocol.ItemUse,
                    StringComparison.Ordinal)
                || pendingItemUse == null
                || (error.expectedRequestSequence > 0
                    && error.expectedRequestSequence != pendingItemUse.requestSequence))
                return;

            string errorCode = error.errorCode ?? string.Empty;
            long failedSequence = pendingItemUse.requestSequence;
            bool mayHaveConsumed = pendingItemUseMayHaveConsumed;
            pendingItemUse = null;
            pendingItemUseCooldownKey = null;
            pendingItemUseMayHaveConsumed = false;

            if (errorCode == "item_use_operation_session_invalid")
                itemUseOperationSessionId = null;

            Debug.LogWarning("[ItemUse][RX] rejected requestSequence="
                + failedSequence + " errorCode=" + errorCode
                + " message=" + (error.message ?? string.Empty));
            ShowResultNotice(error.message, errorCode == "item_not_consumable"
                ? "该物品暂不可使用" : "物品使用失败，请稍后重试");

            if (errorCode == "item_use_revision_conflict" || mayHaveConsumed)
                NotifyAuthoritativeInventoryChanged();
            else
                RefreshIfNeeded();
            if (mayHaveConsumed)
                applyJingjieBalance?.Invoke(null, null);
        }

        private void HandleSnapshotRateLimit()
        {
            lastRequestAt = Time.unscaledTime;
            if (snapshotRateLimitRetried)
            {
                CompleteSnapshotRequest();
                refreshAfterPending = false;
                panel.SetLoading(false);
                ShowNotice("背包刷新过于频繁，请稍后重试");
                return;
            }

            pendingSequence = 0;
            snapshotRateLimitRetried = true;
            refreshAfterPending = true;
            if (snapshot != null) panel.SetLoading(false);
        }

        private void CompleteSnapshotRequest()
        {
            pendingSequence = 0;
            snapshotRateLimitRetried = false;
        }

        private void ApplyItemUseManaIfPresent(
            InventoryV2ItemUseResultDto result, string json)
        {
            if (applyMana == null || result == null || string.IsNullOrEmpty(json)
                || result.currentMp < 0 || result.maxMp < 0
                || result.currentMp > result.maxMp
                || json.IndexOf("\"currentMp\"", StringComparison.Ordinal) < 0
                || json.IndexOf("\"maxMp\"", StringComparison.Ordinal) < 0)
                return;
            applyMana(result.currentMp, result.maxMp, result.serverTime);
        }

        private void ApplyItemUseCooldown(string cooldownKey,
            InventoryV2ItemUseResultDto result)
        {
            if (string.IsNullOrWhiteSpace(cooldownKey) || result == null
                || result.cooldownAvailableAt <= 0)
                return;

            long referenceTime = result.serverTime > 0
                ? result.serverTime
                : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            double remainingSeconds = Math.Max(0d,
                (result.cooldownAvailableAt - referenceTime) / 1000d);
            itemCooldownDeadlines[cooldownKey] =
                Time.unscaledTime + (float)remainingSeconds;
        }

        private string GetItemUseCooldownKey(string itemId)
        {
            CultivationSessionRuntime items = CultivationSessionRuntime.Instance;
            if (items != null && items.TryGetItem(itemId,
                    out CultivationItemDefinitionDto definition)
                && !string.IsNullOrWhiteSpace(definition.cooldownGroup))
                return "group:" + definition.cooldownGroup;
            return "item:" + itemId;
        }

        private static void PublishItemUseEffects(InventoryV2ItemUseEffectDto[] effects)
        {
            if (effects == null) return;
            for (int index = 0; index < effects.Length; index++)
            {
                InventoryV2ItemUseEffectDto effect = effects[index];
                if (effect == null || string.IsNullOrWhiteSpace(effect.type)) continue;
                Debug.Log("[ItemUseEffect] type=" + effect.type + " amount=" + effect.amount);
            }
        }

        private static bool ContainsPotionEffect(InventoryV2ItemUseEffectDto[] effects)
        {
            if (effects == null) return false;
            foreach (InventoryV2ItemUseEffectDto effect in effects)
            {
                switch (effect?.type)
                {
                    case "restore_hp":
                    case "restore_mp":
                    case "restore_hp_max_bps":
                    case "restore_mp_max_bps":
                        return true;
                }
            }
            return false;
        }

        private void HandleUnequipRequested(
            int sourceSlotCode, int targetBagIndex, string identity)
        {
            InventoryEquipmentState source = FindEquipment(identity);
            if (source == null || source.SlotCode != sourceSlotCode
                || targetBagIndex < 0 || targetBagIndex >= snapshot.Capacity) return;
            var intent = new InventoryEquipmentIntent(
                InventoryEquipmentOperationKind.Unequip, identity, sourceSlotCode, 0);
            if (SendOperation(intent, sequence =>
                new InventoryV2UnequipRequestDto(
                    equipmentOperationSessionId, sequence, sourceSlotCode)))
                pendingUnequipTarget = targetBagIndex;
        }

        private void HandleMoveRequested(int sourceSlotCode, int targetSlotCode, string identity)
        {
            InventoryEquipmentState source = FindEquipment(identity);
            if (source == null || source.SlotCode != sourceSlotCode
                || sourceSlotCode == targetSlotCode
                || !IsCompatible(source, targetSlotCode)) return;
            InventoryEquipmentState displaced = FindEquipped(targetSlotCode);
            if (displaced != null && !IsCompatible(displaced, sourceSlotCode)) return;
            var intent = new InventoryEquipmentIntent(
                InventoryEquipmentOperationKind.SlotMove, identity,
                sourceSlotCode, targetSlotCode);
            SendOperation(intent, sequence => new InventoryV2SlotMoveRequestDto(
                equipmentOperationSessionId, sequence, sourceSlotCode, targetSlotCode));
        }

        private bool SendOperation<T>(InventoryEquipmentIntent intent, Func<long, T> createRequest)
        {
            if (snapshot == null || characterDbId <= 0 || !network.IsConnected
                || pendingSequence != 0 || pendingOperation != null
                || pendingItemUse != null || pendingDiscard != null
                || pendingWorldDrop != null)
            {
                ShowNotice("装备操作暂不可用，请稍后重试");
                return false;
            }
            if (!HasEquipmentOperationSession()) return false;
            long sequence = ++nextEquipmentOperationSequence;
            if (!network.Send(createRequest(sequence)))
            {
                Debug.LogWarning("V2 equipment operation could not be sent.");
                ShowNotice("装备操作请求发送失败，请稍后重试");
                return false;
            }
            pendingOperation = intent;
            pendingOperationSequence = sequence;
            operationStartedAt = Time.unscaledTime;
            equipmentPanel?.SetPending(true);
            return true;
        }

        private bool HasEquipmentOperationSession()
        {
            if (Guid.TryParseExact(equipmentOperationSessionId, "D", out _))
                return true;
            Debug.LogWarning("Equipment operation requires the current enter-world session ID.");
            ShowNotice("装备操作暂不可用，请重新进入场景");
            return false;
        }

        private void HandleOperationResult(string json)
        {
            if (pendingOperation == null) return;
            InventoryV2OperationResultDto header;
            try { header = JsonUtility.FromJson<InventoryV2OperationResultDto>(json); }
            catch (Exception) { return; }
            if (header == null || header.requestSequence != pendingOperationSequence) return;
            if (!string.IsNullOrEmpty(header.equipmentOperationSessionId)
                && header.equipmentOperationSessionId != equipmentOperationSessionId) return;

            InventoryEquipmentIntent intent = pendingOperation;
            int unequipTarget = pendingUnequipTarget;
            pendingOperation = null;
            pendingOperationSequence = 0;
            pendingUnequipTarget = -1;
            equipmentPanel?.SetPending(false);
            if (!InventoryV2Codec.TryReadOperation(json,
                out InventoryV2OperationResultDto result, out string error)
                || result.characterDbId != characterDbId
                || result.operation != intent.OperationName)
            {
                Debug.LogWarning("V2 equipment response is invalid: " + error);
                ShowNotice("装备结果异常，请刷新背包");
                RequestSnapshot();
                return;
            }
            if (!result.success)
            {
                Debug.LogWarning("V2 equipment operation failed: " + result.errorCode
                    + " " + result.message);
                ShowResultNotice(result.message,
                    result.errorCode == "equipment_inventory_full"
                        ? "背包已满，无法卸下装备"
                        : result.errorCode == "equipment_slot_mismatch"
                            ? "装备类型与槽位不匹配"
                            : "装备操作失败，请稍后重试");
                if (NeedsSnapshot(result.errorCode) || result.requiresSnapshot)
                    RequestSnapshot();
                else RefreshIfNeeded();
                return;
            }
            if (!InventoryEquipmentChanges.TryApply(snapshot, intent, result,
                out InventoryOwnershipSnapshot next, out error))
            {
                Debug.LogWarning("V2 equipment change rejected: " + error);
                ShowNotice("装备结果未完整同步，请刷新背包");
                RequestSnapshot();
                return;
            }
            try
            {
                presenter.ApplySnapshot(next);
                if (intent.Kind == InventoryEquipmentOperationKind.Unequip
                    && unequipTarget >= 0 && bag.Layout != null)
                {
                    for (int index = 0; index < bag.Layout.Capacity; index++)
                    {
                        if (!string.Equals(bag.Layout.GetIdentity(index),
                            intent.SourceIdentity, StringComparison.Ordinal)) continue;
                        bag.TryDrop(index, unequipTarget, intent.SourceIdentity);
                        break;
                    }
                }
                equipmentPanel?.Render(next);
                snapshot = next;
                bool appliedStats = result.characterStats != null
                    && string.Equals(result.equipmentOperationSessionId, equipmentOperationSessionId,
                        StringComparison.Ordinal)
                    && applyEquipmentStats?.Invoke(result.characterDbId, result.characterStats, result.serverTime) == true;
                RefreshIfNeeded();
                if (!appliedStats)
                    requestAttributeRefresh?.Invoke();
                ShowNotice(intent.Kind == InventoryEquipmentOperationKind.Equip
                    ? "装备成功"
                    : intent.Kind == InventoryEquipmentOperationKind.Unequip
                        ? "卸下成功" : "装备位置已调整");
            }
            catch (Exception exception)
            {
                Debug.LogError("V2 equipment change could not be displayed: " + exception.Message);
                ShowNotice("装备结果显示失败，请刷新背包");
                RequestSnapshot();
            }
        }

        private InventoryEquipmentState FindEquipment(string identity)
        {
            if (snapshot == null) return null;
            foreach (InventoryEquipmentState item in snapshot.Equipment)
                if (string.Equals(item.Identity, identity, StringComparison.Ordinal))
                    return item;
            return null;
        }

        private InventoryEquipmentState FindEquipped(int slotCode)
        {
            if (snapshot == null) return null;
            foreach (InventoryEquipmentState item in snapshot.Equipment)
                if (item.SlotCode == slotCode) return item;
            return null;
        }

        private bool IsCompatible(InventoryEquipmentState item, int slotCode)
        {
            if (slotCode < 1 || slotCode > InventoryV2Protocol.EquipmentSlotCount)
                return false;
            // Unknown catalog data is never used to infer a category; the Gate validates the explicit target.
            if (equipmentPanel == null) return false;
            if (catalog == null || !snapshot.CatalogMatchesClient
                || !InventoryV2Protocol.IsCatalogCompatible(
                    catalog.EquipmentCatalogVersion, snapshot.CatalogVersion)) return true;
            return !catalog.TryGetByEquipmentId(item.EquipmentId,
                       out ItemDefinition definition)
                || (int)definition.EquipmentCategory == ExpectedCategory(slotCode);
        }

        private static int ExpectedCategory(int slotCode)
        {
            switch (slotCode)
            {
                case 1: return 1;
                case 2: return 2;
                case 3: return 3;
                case 4: return 4;
                case 5:
                case 6: return 5;
                case 7:
                case 8: return 6;
                case 9: return 7;
                case 10: return 8;
                case 11: return 9;
                case 12: return 10;
                default: return 0;
            }
        }

        private static bool NeedsSnapshot(string errorCode)
        {
            return errorCode == "equipment_item_not_found"
                || errorCode == "equipment_item_not_in_inventory"
                || errorCode == "equipment_slot_empty"
                || errorCode == "equipment_operation_conflict"
                || errorCode == "equipment_persist_failed";
        }

        private void RefreshIfNeeded()
        {
            if (refreshAfterPending && pendingSequence == 0
                && pendingOperation == null
                && (pendingItemUse == null || pendingItemUseMayHaveConsumed)
                && pendingDiscard == null && pendingWorldDrop == null)
                RequestSnapshot();
        }

        private void ShowNotice(string message)
        {
            if (!string.IsNullOrWhiteSpace(message))
                showNotice?.Invoke(message);
        }

        private void ShowResultNotice(string serverMessage, string fallback)
        {
            ShowNotice(string.IsNullOrWhiteSpace(serverMessage)
                ? fallback : serverMessage);
        }
    }
}
