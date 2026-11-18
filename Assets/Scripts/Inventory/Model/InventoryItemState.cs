using System;
using System.Globalization;

namespace Game.Inventory
{
    public sealed class InventoryItemState
    {
        public long SlotId { get; }
        public int SlotIndex { get; }
        public string ItemId { get; }
        public int Count { get; }
        public bool IsBound { get; }

        public string ItemInstanceId => SlotId.ToString(CultureInfo.InvariantCulture);

        public InventoryItemState(
            long slotId,
            int slotIndex,
            string itemId,
            int count,
            bool isBound)
        {
            if (slotId <= 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(slotId), "Slot ID must be positive.");
            }

            if (slotIndex < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(slotIndex), "Slot index cannot be negative.");
            }

            if (string.IsNullOrWhiteSpace(itemId))
            {
                throw new ArgumentException("Item ID cannot be empty.", nameof(itemId));
            }

            if (count <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count), "Item count must be positive.");
            }

            SlotId = slotId;
            SlotIndex = slotIndex;
            ItemId = itemId;
            Count = count;
            IsBound = isBound;
        }

        public InventoryItemState WithCount(int count)
        {
            return new InventoryItemState(
                SlotId,
                SlotIndex,
                ItemId,
                count,
                IsBound);
        }

        public InventoryItemState WithSlotIndex(int slotIndex)
        {
            return new InventoryItemState(
                SlotId,
                slotIndex,
                ItemId,
                Count,
                IsBound);
        }
    }
}
