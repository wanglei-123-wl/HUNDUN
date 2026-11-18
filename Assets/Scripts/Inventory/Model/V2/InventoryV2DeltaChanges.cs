using System;
using System.Collections.Generic;
using Game.Inventory.Protocol.V2;

namespace Game.Inventory.Model.V2
{
    public enum InventoryV2DeltaResult
    {
        Applied,
        Stale,
        NeedsSnapshot
    }

    public static class InventoryV2DeltaChanges
    {
        public static InventoryV2DeltaResult TryApplyDiscard(
            InventoryOwnershipSnapshot current, long revision,
            long stackId, int count, InventoryV2StackChangeDto[] changes,
            out InventoryOwnershipSnapshot next, out string error)
        {
            return TryApplyStackRemoval(current, revision, stackId, count, changes,
                out next, out error);
        }

        public static InventoryV2DeltaResult TryApplyWorldDrop(
            InventoryOwnershipSnapshot current, long revision,
            long stackId, int count, InventoryV2StackChangeDto[] changes,
            out InventoryOwnershipSnapshot next, out string error)
        {
            return TryApplyStackRemoval(current, revision, stackId, count, changes,
                out next, out error);
        }

        private static InventoryV2DeltaResult TryApplyStackRemoval(
            InventoryOwnershipSnapshot current, long revision,
            long stackId, int count, InventoryV2StackChangeDto[] changes,
            out InventoryOwnershipSnapshot next, out string error)
        {
            next = null;
            error = string.Empty;
            if (current == null)
                return Fail("No V2 ownership snapshot is available.", out error);
            if (revision <= current.InventoryRevision)
                return InventoryV2DeltaResult.Stale;
            InventoryStackState owned = null;
            foreach (InventoryStackState stack in current.Stacks)
                if (stack.StackId == stackId) { owned = stack; break; }
            if (owned == null || count <= 0 || count > owned.Count
                || changes == null || changes.Length != 1 || changes[0] == null
                || changes[0].stackId != stackId
                || changes[0].itemId != owned.ItemId
                || changes[0].isBound != owned.IsBound
                || changes[0].previousCount != owned.Count
                || changes[0].newCount != owned.Count - count)
                return Fail("Stack removal does not match the confirmed stack and count.", out error);
            return TryApplyPickup(current, revision, changes,
                Array.Empty<InventoryV2EquipmentDto>(),
                Array.Empty<InventoryV2WalletChangeDto>(), out next, out error);
        }

        public static InventoryV2DeltaResult TryApplyItemUse(
            InventoryOwnershipSnapshot current, long revision, long usedStackId,
            InventoryV2StackChangeDto[] changes,
            out InventoryOwnershipSnapshot next, out string error)
        {
            next = null;
            error = string.Empty;
            if (changes == null || changes.Length != 1 || changes[0] == null
                || changes[0].stackId != usedStackId
                || changes[0].previousCount <= 0
                || changes[0].newCount != changes[0].previousCount - 1)
                return Fail("Item use stack change is missing or invalid.", out error);
            return TryApplyPickup(current, revision, changes,
                Array.Empty<InventoryV2EquipmentDto>(),
                Array.Empty<InventoryV2WalletChangeDto>(), out next, out error);
        }

        public static InventoryV2DeltaResult TryApplyPickup(
            InventoryOwnershipSnapshot current, long revision,
            InventoryV2StackChangeDto[] stackChanges,
            InventoryV2EquipmentDto[] equipmentChanges,
            InventoryV2WalletChangeDto[] walletChanges,
            out InventoryOwnershipSnapshot next, out string error)
        {
            next = null;
            error = string.Empty;
            if (current == null)
                return Fail("No V2 ownership snapshot is available.", out error);
            if (revision <= current.InventoryRevision)
                return InventoryV2DeltaResult.Stale;
            if (current.InventoryRevision == long.MaxValue
                || revision != current.InventoryRevision + 1)
                return Fail("Inventory revision has a gap.", out error);
            if (stackChanges == null || equipmentChanges == null || walletChanges == null
                || stackChanges.Length + equipmentChanges.Length + walletChanges.Length == 0)
                return Fail("Pickup changes are incomplete.", out error);

            var stacks = new Dictionary<long, InventoryStackState>();
            var equipment = new Dictionary<string, InventoryEquipmentState>(StringComparer.Ordinal);
            var wallets = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (InventoryStackState item in current.Stacks)
                stacks.Add(item.StackId, item);
            foreach (InventoryEquipmentState item in current.Equipment)
                equipment.Add(item.ItemInstanceId, item);
            foreach (KeyValuePair<string, long> wallet in current.Wallets)
                wallets.Add(wallet.Key, wallet.Value);

            var seenStacks = new HashSet<long>();
            foreach (InventoryV2StackChangeDto change in stackChanges)
            {
                if (change == null || change.stackId <= 0
                    || string.IsNullOrWhiteSpace(change.itemId)
                    || change.previousCount < 0 || change.newCount < 0
                    || !seenStacks.Add(change.stackId))
                    return Fail("Invalid or duplicate stack change.", out error);
                stacks.TryGetValue(change.stackId, out InventoryStackState previous);
                if ((previous?.Count ?? 0) != change.previousCount
                    || (previous != null && (previous.ItemId != change.itemId
                        || previous.IsBound != change.isBound))
                    || (previous == null && change.newCount == 0))
                    return Fail("Stack change does not match local ownership.", out error);
                if (change.newCount == 0)
                    stacks.Remove(change.stackId);
                else
                    stacks[change.stackId] = new InventoryStackState(change.stackId,
                        change.itemId, change.newCount, change.isBound);
            }

            var seenEquipment = new HashSet<string>(StringComparer.Ordinal);
            foreach (InventoryV2EquipmentDto change in equipmentChanges)
            {
                if (change == null || string.IsNullOrWhiteSpace(change.itemInstanceId)
                    || string.IsNullOrWhiteSpace(change.equipmentId)
                    || change.slotCode != 0 || !seenEquipment.Add(change.itemInstanceId))
                    return Fail("Pickup equipment change is invalid.", out error);
                if (equipment.TryGetValue(change.itemInstanceId, out InventoryEquipmentState previous)
                    && (previous.EquipmentId != change.equipmentId || previous.SlotCode != 0))
                    return Fail("Pickup equipment conflicts with local ownership.", out error);
                equipment[change.itemInstanceId] = new InventoryEquipmentState(
                    change.itemInstanceId, change.equipmentId, change.isBound, 0);
            }

            var seenWallets = new HashSet<string>(StringComparer.Ordinal);
            foreach (InventoryV2WalletChangeDto change in walletChanges)
            {
                if (change == null || string.IsNullOrWhiteSpace(change.currencyId)
                    || change.previousBalance < 0 || change.newBalance < 0
                    || !seenWallets.Add(change.currencyId))
                    return Fail("Invalid or duplicate wallet change.", out error);
                wallets.TryGetValue(change.currencyId, out long previous);
                if (previous != change.previousBalance)
                    return Fail("Wallet change does not match local balance.", out error);
                wallets[change.currencyId] = change.newBalance;
            }

            int bagCount = stacks.Count;
            foreach (InventoryEquipmentState item in equipment.Values)
                if (item.SlotCode == 0) bagCount++;
            if (bagCount > current.Capacity)
                return Fail("Pickup exceeds bag capacity.", out error);

            next = new InventoryOwnershipSnapshot(current.CharacterDbId, revision,
                current.EquipmentRevision, current.Capacity, current.CatalogVersion,
                InventoryV2Protocol.EquipmentCatalogVersion,
                new List<InventoryStackState>(stacks.Values),
                new List<InventoryEquipmentState>(equipment.Values),
                wallets, current.AttributeBonus);
            return InventoryV2DeltaResult.Applied;
        }

        private static InventoryV2DeltaResult Fail(string message, out string error)
        {
            error = message;
            return InventoryV2DeltaResult.NeedsSnapshot;
        }
    }
}
