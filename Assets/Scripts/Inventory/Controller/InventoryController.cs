using System;
using System.Collections.Generic;
using Game.Items;

namespace Game.Inventory
{
    public sealed class InventoryController : IDisposable
    {
        private readonly InventoryStateStore stateStore;
        private readonly InventoryFilterService filterService;
        private readonly IInventoryDataSource dataSource;
        private readonly InventoryPanelView panelView;
        private readonly InventoryGridView gridView;
        private readonly ItemDefinitionTable itemDefinitions;
        private readonly HashSet<string> appliedGrantIds =
            new HashSet<string>(StringComparer.Ordinal);

        private bool initialized;
        private bool isOpen;

        public bool IsOpen => isOpen;
        public InventoryItemState SelectedItem { get; private set; }

        public event Action<InventorySnapshotData> SnapshotApplied;
        public event Action<InventoryDeltaData> DeltaApplied;
        public event Action<InventoryItemState> ItemSelected;
        public event Action<InventoryDataSourceError> ErrorReceived;
        public event Action InventoryMismatchDetected;

        public InventoryController(
            InventoryStateStore stateStore,
            InventoryFilterService filterService,
            IInventoryDataSource dataSource,
            InventoryPanelView panelView,
            ItemDefinitionTable itemDefinitions)
        {
            this.stateStore = stateStore
                ?? throw new ArgumentNullException(nameof(stateStore));
            this.filterService = filterService
                ?? throw new ArgumentNullException(nameof(filterService));
            this.dataSource = dataSource
                ?? throw new ArgumentNullException(nameof(dataSource));
            this.panelView = panelView
                ?? throw new ArgumentNullException(nameof(panelView));
            gridView = panelView.GridView
                ?? throw new ArgumentException(
                    "Inventory panel grid view is missing.",
                    nameof(panelView));
            this.itemDefinitions = itemDefinitions
                ?? throw new ArgumentNullException(nameof(itemDefinitions));
        }

        public void Initialize()
        {
            if (initialized)
            {
                return;
            }

            initialized = true;

            dataSource.SnapshotReceived += HandleSnapshotReceived;
            dataSource.DeltaReceived += HandleDeltaReceived;
            dataSource.ErrorReceived += HandleDataSourceError;

            panelView.CloseClicked += HandleCloseClicked;
            panelView.CategoryChanged += HandleCategoryChanged;
            gridView.SlotClicked += HandleSlotClicked;

            panelView.SetCategory(InventoryItemCategory.All);
            panelView.SetLoading(false);
        }

        public void Open()
        {
            EnsureInitialized();

            isOpen = true;
            panelView.Show();

            if (stateStore.HasSnapshot)
            {
                panelView.SetLoading(false);
                RefreshView();
                return;
            }

            RequestSnapshot(initialLoad: true);
        }

        public void Close()
        {
            if (!initialized)
            {
                return;
            }

            isOpen = false;
            panelView.Hide();
        }

        public void RequestSnapshot()
        {
            EnsureInitialized();
            RequestSnapshot(initialLoad: !stateStore.HasSnapshot);
        }

        public void ResetConnectionState()
        {
            appliedGrantIds.Clear();
            SelectedItem = null;
            stateStore.Reset();
            gridView.Clear();
            panelView.SetCategory(InventoryItemCategory.All);
            panelView.SetLoading(false);
        }

        public void Dispose()
        {
            if (!initialized)
            {
                return;
            }

            dataSource.SnapshotReceived -= HandleSnapshotReceived;
            dataSource.DeltaReceived -= HandleDeltaReceived;
            dataSource.ErrorReceived -= HandleDataSourceError;

            panelView.CloseClicked -= HandleCloseClicked;
            panelView.CategoryChanged -= HandleCategoryChanged;
            gridView.SlotClicked -= HandleSlotClicked;

            initialized = false;
            isOpen = false;
            SelectedItem = null;
        }

        private void HandleSnapshotReceived(InventorySnapshotData snapshot)
        {
            ApplySnapshot(snapshot);
        }

        public bool ApplySnapshot(InventorySnapshotData snapshot)
        {
            if (snapshot == null)
            {
                ReportClientError(
                    "inventory_snapshot_result",
                    "client_invalid_snapshot",
                    "Inventory snapshot payload is null.",
                    0L);
                return false;
            }

            try
            {
                stateStore.ReplaceSnapshot(
                    snapshot.Capacity,
                    snapshot.Revision,
                    snapshot.Items,
                    snapshot.Wallets);
            }
            catch (Exception exception)
            {
                ReportClientError(
                    "inventory_snapshot_result",
                    "client_invalid_snapshot",
                    exception.Message,
                    snapshot.ServerTime);
                return false;
            }

            panelView.SetLoading(false);
            RefreshSelectedItem();

            if (isOpen)
            {
                RefreshView();
            }

            SnapshotApplied?.Invoke(snapshot);
            return true;
        }

        private void HandleDeltaReceived(InventoryDeltaData delta)
        {
            if (delta == null)
            {
                return;
            }

            if (appliedGrantIds.Contains(delta.GrantId))
            {
                return;
            }

            if (!stateStore.HasSnapshot)
            {
                RequestSnapshot(initialLoad: true);
                return;
            }

            if (delta.Revision <= stateStore.Revision)
            {
                appliedGrantIds.Add(delta.GrantId);
                return;
            }

            if (delta.Revision != stateStore.Revision + 1L
                || !PreviousValuesMatch(delta))
            {
                InventoryMismatchDetected?.Invoke();
                RequestSnapshot(initialLoad: false);
                return;
            }

            try
            {
                ValidateDelta(delta);
                ApplyDelta(delta);
            }
            catch (Exception exception)
            {
                ReportClientError(
                    "loot_pickup_result",
                    "client_invalid_inventory_delta",
                    exception.Message,
                    delta.ServerTime);
                InventoryMismatchDetected?.Invoke();
                RequestSnapshot(initialLoad: false);
                return;
            }

            appliedGrantIds.Add(delta.GrantId);
            RefreshSelectedItem();

            if (isOpen)
            {
                RefreshView();
            }

            DeltaApplied?.Invoke(delta);
        }

        private bool PreviousValuesMatch(InventoryDeltaData delta)
        {
            for (int index = 0; index < delta.SlotChanges.Count; index++)
            {
                InventorySlotChangeData change = delta.SlotChanges[index];
                int localCount = stateStore.TryGetItemBySlotIndex(
                    change.SlotIndex,
                    out InventoryItemState localItem)
                        ? localItem.Count
                        : 0;

                if (localCount != change.PreviousCount)
                {
                    return false;
                }
            }

            for (int index = 0; index < delta.WalletChanges.Count; index++)
            {
                InventoryWalletChangeData change = delta.WalletChanges[index];
                if (stateStore.GetWalletBalance(change.CurrencyId)
                    != change.PreviousBalance)
                {
                    return false;
                }
            }

            return true;
        }

        private void ValidateDelta(InventoryDeltaData delta)
        {
            HashSet<int> changedSlots = new HashSet<int>();
            for (int index = 0; index < delta.SlotChanges.Count; index++)
            {
                InventorySlotChangeData change = delta.SlotChanges[index];
                if (change.SlotIndex >= stateStore.Capacity)
                {
                    throw new InvalidOperationException(
                        "Inventory delta slot index exceeds capacity.");
                }

                if (!changedSlots.Add(change.SlotIndex))
                {
                    throw new InvalidOperationException(
                        "Inventory delta contains duplicate slot changes.");
                }
            }

            HashSet<string> changedWallets =
                new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < delta.WalletChanges.Count; index++)
            {
                InventoryWalletChangeData change = delta.WalletChanges[index];
                if (!changedWallets.Add(change.CurrencyId))
                {
                    throw new InvalidOperationException(
                        "Inventory delta contains duplicate wallet changes.");
                }
            }
        }

        private void ApplyDelta(InventoryDeltaData delta)
        {
            Dictionary<int,InventoryItemState> nextItems=new Dictionary<int,InventoryItemState>();
            foreach(KeyValuePair<int,InventoryItemState> pair in stateStore.ItemsBySlotIndex)
                nextItems.Add(pair.Key,pair.Value);
            Dictionary<string,long> nextWallets=new Dictionary<string,long>(StringComparer.Ordinal);
            foreach(KeyValuePair<string,long> pair in stateStore.WalletsByCurrencyId)
                nextWallets.Add(pair.Key,pair.Value);
            for (int index = 0; index < delta.SlotChanges.Count; index++)
            {
                InventorySlotChangeData change = delta.SlotChanges[index];
                if (change.NewCount <= 0)
                {
                    nextItems.Remove(change.SlotIndex);
                    continue;
                }

                nextItems[change.SlotIndex]=new InventoryItemState(change.SlotId,change.SlotIndex,
                    change.ItemId,change.NewCount,change.IsBound);
            }

            for (int index = 0; index < delta.WalletChanges.Count; index++)
            {
                InventoryWalletChangeData change = delta.WalletChanges[index];
                if(change.NewBalance<0)throw new InvalidOperationException("Inventory wallet balance cannot be negative.");
                nextWallets[change.CurrencyId]=change.NewBalance;
            }
            stateStore.ReplaceSnapshot(stateStore.Capacity,delta.Revision,nextItems.Values,nextWallets);
        }

        private void HandleCategoryChanged(InventoryItemCategory category)
        {
            if (isOpen)
            {
                RefreshView();
            }
        }

        private void HandleSlotClicked(InventorySlotView slotView)
        {
            SelectedItem = slotView == null ? null : slotView.Item;
            ItemSelected?.Invoke(SelectedItem);
        }

        private void HandleCloseClicked()
        {
            Close();
        }

        private void HandleDataSourceError(InventoryDataSourceError error)
        {
            panelView.SetLoading(false);

            if (!stateStore.HasSnapshot)
            {
                stateStore.MarkLoadFailed();
            }

            ErrorReceived?.Invoke(error);
        }

        private void RefreshView()
        {
            List<InventoryItemState> allItems =
                stateStore.GetItemsInSlotOrder();
            InventoryItemCategory category = panelView.CurrentCategory;

            if (category == InventoryItemCategory.All)
            {
                gridView.RefreshServerSlots(
                    stateStore.Capacity,
                    allItems,
                    itemDefinitions);
                return;
            }

            List<InventoryItemState> filteredItems =
                filterService.FilterAndSort(
                    allItems,
                    category,
                    itemDefinitions);
            gridView.RefreshCompact(
                stateStore.Capacity,
                filteredItems,
                itemDefinitions);
        }

        private void RefreshSelectedItem()
        {
            if (SelectedItem == null)
            {
                return;
            }

            if (stateStore.TryGetItemBySlotId(
                SelectedItem.SlotId,
                out InventoryItemState refreshedItem))
            {
                SelectedItem = refreshedItem;
                return;
            }

            SelectedItem = null;
            gridView.ClearSelection();
        }

        private void RequestSnapshot(bool initialLoad)
        {
            if (dataSource.IsSnapshotRequestPending)
            {
                return;
            }

            if (initialLoad)
            {
                stateStore.BeginLoading();
            }

            if (isOpen)
            {
                panelView.SetLoading(true);
            }

            try
            {
                dataSource.RequestSnapshot();
            }
            catch (Exception exception)
            {
                panelView.SetLoading(false);
                ReportClientError(
                    "inventory_snapshot",
                    "client_snapshot_request_failed",
                    exception.Message,
                    0L);
            }
        }

        private void ReportClientError(
            string requestType,
            string errorCode,
            string message,
            long serverTime)
        {
            ErrorReceived?.Invoke(
                new InventoryDataSourceError(
                    requestType,
                    errorCode,
                    message,
                    serverTime));
        }

        private void EnsureInitialized()
        {
            if (!initialized)
            {
                Initialize();
            }
        }
    }
}
