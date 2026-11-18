using System;
using System.Collections.Generic;

namespace Game.Inventory
{
    public sealed class InventoryStateStore
    {
        private readonly Dictionary<int, InventoryItemState> itemsBySlotIndex =
            new Dictionary<int, InventoryItemState>();
        private readonly Dictionary<long, InventoryItemState> itemsBySlotId =
            new Dictionary<long, InventoryItemState>();
        private readonly Dictionary<string, long> walletsByCurrencyId =
            new Dictionary<string, long>(StringComparer.Ordinal);

        public int Capacity { get; private set; }
        public long Revision { get; private set; }
        public InventoryLoadState LoadState { get; private set; }
        public bool HasSnapshot => LoadState == InventoryLoadState.Ready;

        public IReadOnlyDictionary<int, InventoryItemState> ItemsBySlotIndex =>
            itemsBySlotIndex;
        public IReadOnlyDictionary<string, long> WalletsByCurrencyId =>
            walletsByCurrencyId;

        public InventoryStateStore()
        {
            Capacity = 0;
            LoadState = InventoryLoadState.Uninitialized;
        }

        public void BeginLoading()
        {
            LoadState = InventoryLoadState.Loading;
        }

        public void MarkLoadFailed()
        {
            LoadState = InventoryLoadState.Failed;
        }

        public void ReplaceSnapshot(
            int capacity,
            long revision,
            IEnumerable<InventoryItemState> items,
            IReadOnlyDictionary<string, long> wallets)
        {
            capacity = Math.Max(0, capacity);

            if (revision < 0L)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(revision),
                    "Inventory revision cannot be negative.");
            }

            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }

            Dictionary<int, InventoryItemState> newItemsBySlotIndex =
                new Dictionary<int, InventoryItemState>();
            Dictionary<long, InventoryItemState> newItemsBySlotId =
                new Dictionary<long, InventoryItemState>();
            Dictionary<string, long> newWalletsByCurrencyId =
                new Dictionary<string, long>(StringComparer.Ordinal);

            foreach (InventoryItemState item in items)
            {
                ValidateItem(item, capacity);

                if (newItemsBySlotIndex.ContainsKey(item.SlotIndex))
                {
                    throw new InvalidOperationException(
                        "Inventory snapshot contains duplicate slotIndex: "
                        + item.SlotIndex);
                }

                if (newItemsBySlotId.ContainsKey(item.SlotId))
                {
                    throw new InvalidOperationException(
                        "Inventory snapshot contains duplicate slotId: "
                        + item.SlotId);
                }

                newItemsBySlotIndex.Add(item.SlotIndex, item);
                newItemsBySlotId.Add(item.SlotId, item);
            }

            if (wallets != null)
            {
                foreach (KeyValuePair<string, long> wallet in wallets)
                {
                    ValidateWallet(wallet.Key, wallet.Value);
                    newWalletsByCurrencyId.Add(wallet.Key, wallet.Value);
                }
            }

            itemsBySlotIndex.Clear();
            itemsBySlotId.Clear();
            walletsByCurrencyId.Clear();

            CopyTo(newItemsBySlotIndex, itemsBySlotIndex);
            CopyTo(newItemsBySlotId, itemsBySlotId);
            CopyTo(newWalletsByCurrencyId, walletsByCurrencyId);

            Capacity = capacity;
            Revision = revision;
            LoadState = InventoryLoadState.Ready;
        }

        public bool TryGetItemBySlotIndex(
            int slotIndex,
            out InventoryItemState item)
        {
            return itemsBySlotIndex.TryGetValue(slotIndex, out item);
        }

        public bool TryGetItemBySlotId(
            long slotId,
            out InventoryItemState item)
        {
            return itemsBySlotId.TryGetValue(slotId, out item);
        }

        public long GetWalletBalance(string currencyId)
        {
            if (string.IsNullOrWhiteSpace(currencyId))
            {
                return 0L;
            }

            return walletsByCurrencyId.TryGetValue(currencyId, out long balance)
                ? balance
                : 0L;
        }

        public List<InventoryItemState> GetItemsInSlotOrder()
        {
            List<InventoryItemState> items =
                new List<InventoryItemState>(itemsBySlotIndex.Values);
            items.Sort(CompareSlotIndex);
            return items;
        }

        public void UpsertAuthoritativeItem(InventoryItemState item)
        {
            ValidateItem(item, Capacity);

            if (itemsBySlotIndex.TryGetValue(
                item.SlotIndex,
                out InventoryItemState itemAtTargetSlot))
            {
                itemsBySlotId.Remove(itemAtTargetSlot.SlotId);
            }

            if (itemsBySlotId.TryGetValue(
                item.SlotId,
                out InventoryItemState previousItem))
            {
                itemsBySlotIndex.Remove(previousItem.SlotIndex);
            }

            itemsBySlotIndex[item.SlotIndex] = item;
            itemsBySlotId[item.SlotId] = item;
        }

        public bool RemoveAuthoritativeItem(int slotIndex)
        {
            if (!itemsBySlotIndex.TryGetValue(
                slotIndex,
                out InventoryItemState item))
            {
                return false;
            }

            itemsBySlotIndex.Remove(slotIndex);
            itemsBySlotId.Remove(item.SlotId);
            return true;
        }

        public void SetAuthoritativeWalletBalance(
            string currencyId,
            long balance)
        {
            ValidateWallet(currencyId, balance);
            walletsByCurrencyId[currencyId] = balance;
        }

        public void SetAuthoritativeRevision(long revision)
        {
            if (revision < Revision)
            {
                throw new InvalidOperationException(
                    "Inventory revision cannot move backwards.");
            }

            Revision = revision;
        }

        public void Reset()
        {
            itemsBySlotIndex.Clear();
            itemsBySlotId.Clear();
            walletsByCurrencyId.Clear();
            Capacity = 0;
            Revision = 0L;
            LoadState = InventoryLoadState.Uninitialized;
        }

        private static void ValidateItem(
            InventoryItemState item,
            int capacity)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            if (item.SlotIndex >= capacity)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(item),
                    "Item slot index exceeds inventory capacity.");
            }
        }

        private static void ValidateWallet(
            string currencyId,
            long balance)
        {
            if (string.IsNullOrWhiteSpace(currencyId))
            {
                throw new ArgumentException(
                    "Currency ID cannot be empty.",
                    nameof(currencyId));
            }

            if (balance < 0L)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(balance),
                    "Wallet balance cannot be negative.");
            }
        }

        private static int CompareSlotIndex(
            InventoryItemState left,
            InventoryItemState right)
        {
            return left.SlotIndex.CompareTo(right.SlotIndex);
        }

        private static void CopyTo<TKey, TValue>(
            Dictionary<TKey, TValue> source,
            Dictionary<TKey, TValue> destination)
        {
            foreach (KeyValuePair<TKey, TValue> pair in source)
            {
                destination.Add(pair.Key, pair.Value);
            }
        }
    }
}
