using System;
using System.Collections.Generic;

namespace Game.Inventory.Protocol.V2
{
    public static class InventoryV2OperationValidator
    {
        public static bool TryValidate(InventoryV2OperationResultDto message, out string error)
        {
            if (message == null || message.msgId != InventoryV2Protocol.OperationResult
                || message.type != InventoryV2Protocol.OperationResult
                || (message.operation != "equip" && message.operation != "unequip"
                    && message.operation != "slot_move")
                || message.requestSequence <= 0 || message.characterDbId <= 0)
                return Fail("Invalid equipment operation result header.", out error);

            if (!message.success)
            {
                error = string.Empty;
                return true;
            }

            if (message.inventoryRevision < 0 || message.equipmentRevision < 0
                || message.serverTime <= 0 || message.attributeBonus == null
                || (!message.requiresSnapshot
                    && (message.equipmentItemChanges == null
                        || message.equipmentItemChanges.Length == 0)))
                return Fail("Equipment operation result is incomplete.", out error);

            var seenInstances = new HashSet<string>(StringComparer.Ordinal);
            var occupiedSlots = new HashSet<int>();
            foreach (InventoryV2EquipmentDto item
                in message.equipmentItemChanges ?? Array.Empty<InventoryV2EquipmentDto>())
            {
                if (item == null || string.IsNullOrWhiteSpace(item.itemInstanceId)
                    || string.IsNullOrWhiteSpace(item.equipmentId)
                    || item.slotCode < 0 || item.slotCode > InventoryV2Protocol.EquipmentSlotCount
                    || !seenInstances.Add(item.itemInstanceId)
                    || (item.slotCode > 0 && !occupiedSlots.Add(item.slotCode)))
                    return Fail("Equipment operation contains duplicate or invalid changes.", out error);
            }

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
