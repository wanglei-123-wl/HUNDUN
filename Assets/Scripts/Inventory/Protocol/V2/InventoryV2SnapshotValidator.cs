using System;
using System.Collections.Generic;
using Game.Inventory.Model.V2;

namespace Game.Inventory.Protocol.V2
{
    public static class InventoryV2SnapshotValidator
    {
        public static bool TryCreate(
            InventoryV2SnapshotResultDto message, out InventoryOwnershipSnapshot snapshot,
            out string error)
        {
            snapshot = null;
            if (message == null || message.msgId != InventoryV2Protocol.SnapshotResult
                || message.type != InventoryV2Protocol.SnapshotResult
                || !InventoryV2Protocol.SupportsSnapshotVersion(
                    message.equipmentProtocolVersion))
                return Fail("Invalid V2 inventory snapshot header.", out error);
            if (!message.success)
                return Fail("Inventory snapshot failed: " + (message.errorCode ?? message.message), out error);
            if (message.equipmentCatalogVersion <= 0 || message.requestSequence < 0
                || message.characterDbId <= 0 || message.capacity < 0
                || message.inventoryRevision < 0 || message.equipmentRevision < 0
                || message.serverTime <= 0 || message.stackItems == null
                || message.equipmentItems == null || message.wallets == null
                || message.attributeBonus == null)
                return Fail("V2 inventory snapshot is incomplete.", out error);

            var stacks = new List<InventoryStackState>(message.stackItems.Length);
            var equipment = new List<InventoryEquipmentState>(message.equipmentItems.Length);
            var wallets = new Dictionary<string, long>(StringComparer.Ordinal);
            var seenStacks = new HashSet<long>();
            var seenEquipment = new HashSet<string>(StringComparer.Ordinal);
            var occupiedSlots = new HashSet<int>();
            int bagCount = 0;
            foreach (InventoryV2StackDto item in message.stackItems)
            {
                if (item == null || item.stackId <= 0 || item.count <= 0
                    || string.IsNullOrWhiteSpace(item.itemId) || !seenStacks.Add(item.stackId))
                    return Fail("Invalid or duplicate stack identity.", out error);
                stacks.Add(new InventoryStackState(item.stackId, item.itemId, item.count, item.isBound, item.remainingUses));
                bagCount++;
            }
            foreach (InventoryV2EquipmentDto item in message.equipmentItems)
            {
                if (item == null || string.IsNullOrWhiteSpace(item.itemInstanceId)
                    || string.IsNullOrWhiteSpace(item.equipmentId)
                    || item.slotCode < 0 || item.slotCode > InventoryV2Protocol.EquipmentSlotCount
                    || !seenEquipment.Add(item.itemInstanceId)
                    || (item.slotCode > 0 && !occupiedSlots.Add(item.slotCode)))
                    return Fail("Invalid equipment identity or occupied slot.", out error);
                equipment.Add(new InventoryEquipmentState(
                    item.itemInstanceId, item.equipmentId, item.isBound, item.slotCode));
                if (item.slotCode == 0) bagCount++;
            }
            if (bagCount > message.capacity)
                return Fail("V2 inventory exceeds capacity.", out error);
            foreach (InventoryV2WalletDto wallet in message.wallets)
            {
                if (wallet == null || string.IsNullOrWhiteSpace(wallet.currencyId)
                    || wallet.balance < 0 || !wallets.TryAdd(wallet.currencyId, wallet.balance))
                    return Fail("Invalid or duplicate wallet.", out error);
            }

            snapshot = new InventoryOwnershipSnapshot(
                message.characterDbId, message.inventoryRevision, message.equipmentRevision,
                message.capacity, message.equipmentCatalogVersion,
                InventoryV2Protocol.EquipmentCatalogVersion, stacks, equipment, wallets,
                new InventoryAttributeBonusState(message.attributeBonus.maxHp,
                    message.attributeBonus.attack, message.attributeBonus.defense,
                    message.attributeBonus.maxMp, message.attributeBonus.critRateBps,
                    message.attributeBonus.critDamageBps, message.attributeBonus.magicAttack,
                    message.attributeBonus.magicDefense));
            error = string.Empty;
            return true;
        }

        private static bool Fail(string message, out string error)
        {
            error = message;
            return false;
        }
    }
}
