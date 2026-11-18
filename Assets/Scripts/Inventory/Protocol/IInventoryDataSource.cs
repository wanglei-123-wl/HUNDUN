using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Game.Inventory
{
    public interface IInventoryDataSource
    {
        bool IsSnapshotRequestPending { get; }

        event Action<InventorySnapshotData> SnapshotReceived;
        event Action<InventoryDeltaData> DeltaReceived;
        event Action<InventoryDataSourceError> ErrorReceived;

        void RequestSnapshot();
    }

    public sealed class InventorySnapshotData
    {
        public int Capacity { get; }
        public long Revision { get; }
        public IReadOnlyList<InventoryItemState> Items { get; }
        public IReadOnlyDictionary<string, long> Wallets { get; }
        public long ServerTime { get; }

        public InventorySnapshotData(
            int capacity,
            long revision,
            IEnumerable<InventoryItemState> items,
            IReadOnlyDictionary<string, long> wallets,
            long serverTime)
        {
            if (revision < 0L)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(revision),
                    "Inventory revision cannot be negative.");
            }

            Capacity = Math.Max(0, capacity);
            Revision = revision;
            Items = CopyItems(items);
            Wallets = CopyWallets(wallets);
            ServerTime = serverTime;
        }

        private static IReadOnlyList<InventoryItemState> CopyItems(
            IEnumerable<InventoryItemState> items)
        {
            List<InventoryItemState> copiedItems =
                new List<InventoryItemState>();

            if (items != null)
            {
                foreach (InventoryItemState item in items)
                {
                    if (item != null)
                    {
                        copiedItems.Add(item);
                    }
                }
            }

            return copiedItems.AsReadOnly();
        }

        private static IReadOnlyDictionary<string, long> CopyWallets(
            IReadOnlyDictionary<string, long> wallets)
        {
            Dictionary<string, long> copiedWallets =
                new Dictionary<string, long>(StringComparer.Ordinal);

            if (wallets != null)
            {
                foreach (KeyValuePair<string, long> wallet in wallets)
                {
                    if (string.IsNullOrWhiteSpace(wallet.Key)
                        || wallet.Value < 0L)
                    {
                        continue;
                    }

                    copiedWallets[wallet.Key] = wallet.Value;
                }
            }

            return new ReadOnlyDictionary<string, long>(copiedWallets);
        }
    }

    public sealed class InventorySlotChangeData
    {
        public long SlotId { get; }
        public int SlotIndex { get; }
        public string ItemId { get; }
        public int PreviousCount { get; }
        public int NewCount { get; }
        public bool IsBound { get; }

        public InventorySlotChangeData(
            long slotId,
            int slotIndex,
            string itemId,
            int previousCount,
            int newCount,
            bool isBound)
        {
            if (slotId <= 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(slotId));
            }

            if (slotIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(slotIndex));
            }

            if (string.IsNullOrWhiteSpace(itemId))
            {
                throw new ArgumentException(
                    "Item ID cannot be empty.",
                    nameof(itemId));
            }

            if (previousCount < 0 || newCount < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(newCount),
                    "Inventory counts cannot be negative.");
            }

            SlotId = slotId;
            SlotIndex = slotIndex;
            ItemId = itemId;
            PreviousCount = previousCount;
            NewCount = newCount;
            IsBound = isBound;
        }
    }

    public sealed class InventoryWalletChangeData
    {
        public string CurrencyId { get; }
        public long PreviousBalance { get; }
        public long NewBalance { get; }

        public InventoryWalletChangeData(
            string currencyId,
            long previousBalance,
            long newBalance)
        {
            if (string.IsNullOrWhiteSpace(currencyId))
            {
                throw new ArgumentException(
                    "Currency ID cannot be empty.",
                    nameof(currencyId));
            }

            if (previousBalance < 0L || newBalance < 0L)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(newBalance),
                    "Wallet balances cannot be negative.");
            }

            CurrencyId = currencyId;
            PreviousBalance = previousBalance;
            NewBalance = newBalance;
        }
    }

    public sealed class InventoryDeltaData
    {
        public string GrantId { get; }
        public long Revision { get; }
        public IReadOnlyList<InventorySlotChangeData> SlotChanges { get; }
        public IReadOnlyList<InventoryWalletChangeData> WalletChanges { get; }
        public long ServerTime { get; }

        public InventoryDeltaData(
            string grantId,
            long revision,
            IEnumerable<InventorySlotChangeData> slotChanges,
            IEnumerable<InventoryWalletChangeData> walletChanges,
            long serverTime)
        {
            if (string.IsNullOrWhiteSpace(grantId))
            {
                throw new ArgumentException(
                    "Grant ID cannot be empty for a succeeded inventory delta.",
                    nameof(grantId));
            }

            if (revision <= 0L)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(revision),
                    "Inventory delta revision must be positive.");
            }

            GrantId = grantId;
            Revision = revision;
            SlotChanges = CopyChanges(slotChanges);
            WalletChanges = CopyChanges(walletChanges);
            ServerTime = serverTime;
        }

        private static IReadOnlyList<T> CopyChanges<T>(IEnumerable<T> changes)
        {
            List<T> copiedChanges = new List<T>();
            if (changes != null)
            {
                foreach (T change in changes)
                {
                    if (change != null)
                    {
                        copiedChanges.Add(change);
                    }
                }
            }

            return copiedChanges.AsReadOnly();
        }
    }

    public sealed class InventoryDataSourceError
    {
        public string RequestType { get; }
        public string ErrorCode { get; }
        public string Message { get; }
        public long ServerTime { get; }

        public InventoryDataSourceError(
            string requestType,
            string errorCode,
            string message,
            long serverTime)
        {
            RequestType = requestType ?? string.Empty;
            ErrorCode = errorCode ?? string.Empty;
            Message = message ?? string.Empty;
            ServerTime = serverTime;
        }
    }
}
