using System;
using System.Collections.Generic;
using Game.Inventory.Protocol.V2;

namespace Game.Inventory.Model.V2
{
    public enum InventoryEquipmentOperationKind
    {
        Equip,
        Unequip,
        SlotMove
    }

    public sealed class InventoryEquipmentIntent
    {
        public InventoryEquipmentOperationKind Kind { get; }
        public string OperationName => Kind == InventoryEquipmentOperationKind.Equip
            ? "equip" : Kind == InventoryEquipmentOperationKind.Unequip
                ? "unequip" : Kind == InventoryEquipmentOperationKind.SlotMove
                    ? "slot_move" : string.Empty;
        public string SourceIdentity { get; }
        public int SourceSlotCode { get; }
        public int TargetSlotCode { get; }

        public InventoryEquipmentIntent(InventoryEquipmentOperationKind kind,
            string sourceIdentity, int sourceSlotCode, int targetSlotCode)
        {
            Kind = kind;
            SourceIdentity = sourceIdentity;
            SourceSlotCode = sourceSlotCode;
            TargetSlotCode = targetSlotCode;
        }
    }

    public static class InventoryEquipmentChanges
    {
        public static bool TryApplyDiscard(InventoryOwnershipSnapshot current,
            string itemInstanceId, InventoryV2OperationResultDto result,
            out InventoryOwnershipSnapshot next, out string error)
        {
            next = null;
            if (result == null || !result.success || result.operation != "discard"
                || result.equipmentItemChanges == null
                || result.equipmentItemChanges.Length != 0)
                return Fail("Equipment discard result needs a fresh snapshot.", out error);
            return TryApplyRemovedEquipment(current, itemInstanceId, result.characterDbId,
                result.inventoryRevision, result.equipmentRevision,
                result.removedItemInstanceIds, result.requiresSnapshot, out next, out error);
        }

        public static bool TryApplyWorldDrop(InventoryOwnershipSnapshot current,
            string itemInstanceId, InventoryV2WorldDropResultDto result,
            out InventoryOwnershipSnapshot next, out string error)
        {
            next = null;
            if (result == null || !result.success
                || (result.status != "active" && result.status != "pending_scene_sync"
                    && result.status != "expired")
                || (result.equipmentItemChanges != null
                    && result.equipmentItemChanges.Length != 0))
                return Fail("World drop result needs a fresh snapshot.", out error);
            return TryApplyRemovedEquipment(current, itemInstanceId, result.characterDbId,
                result.inventoryRevision,
                result.equipmentRevision == 0 && current != null
                    ? current.EquipmentRevision : result.equipmentRevision,
                result.removedItemInstanceIds, result.requiresSnapshot, out next, out error);
        }

        private static bool TryApplyRemovedEquipment(InventoryOwnershipSnapshot current,
            string itemInstanceId, long characterDbId, long inventoryRevision,
            long equipmentRevision, string[] removedItemInstanceIds,
            bool requiresSnapshot, out InventoryOwnershipSnapshot next, out string error)
        {
            next = null;
            if (current == null || requiresSnapshot
                || (characterDbId > 0 && characterDbId != current.CharacterDbId)
                || string.IsNullOrEmpty(itemInstanceId)
                || current.InventoryRevision == long.MaxValue
                || inventoryRevision != current.InventoryRevision + 1
                || equipmentRevision != current.EquipmentRevision
                || removedItemInstanceIds == null
                || removedItemInstanceIds.Length != 1
                || removedItemInstanceIds[0] != itemInstanceId)
                return Fail("Equipment removal needs a fresh snapshot.", out error);
            var equipment = new List<InventoryEquipmentState>(current.Equipment.Count);
            bool removed = false;
            foreach (InventoryEquipmentState item in current.Equipment)
            {
                if (item.ItemInstanceId == itemInstanceId)
                {
                    if (item.SlotCode != 0)
                        return Fail("Equipped items cannot be discarded.", out error);
                    removed = true;
                }
                else equipment.Add(item);
            }
            if (!removed)
                return Fail("Discarded equipment instance is not in the bag.", out error);

            var wallets = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, long> wallet in current.Wallets)
                wallets.Add(wallet.Key, wallet.Value);
            next = new InventoryOwnershipSnapshot(current.CharacterDbId,
                inventoryRevision, equipmentRevision,
                current.Capacity, current.CatalogVersion,
                InventoryV2Protocol.EquipmentCatalogVersion,
                new List<InventoryStackState>(current.Stacks), equipment, wallets,
                current.AttributeBonus);
            error = string.Empty;
            return true;
        }

        public static bool TryApply(InventoryOwnershipSnapshot current,
            InventoryEquipmentIntent intent, InventoryV2OperationResultDto result,
            out InventoryOwnershipSnapshot next, out string error)
        {
            next = null;
            if (current == null || intent == null
                || !InventoryV2OperationValidator.TryValidate(result, out error)
                || !result.success || result.requiresSnapshot
                || result.characterDbId != current.CharacterDbId
                || result.operation != intent.OperationName)
                return Fail("Equipment response needs a fresh snapshot.", out error);

            long expectedInventory = current.InventoryRevision
                + (intent.Kind == InventoryEquipmentOperationKind.SlotMove ? 0 : 1);
            if (result.inventoryRevision != expectedInventory
                || result.equipmentRevision != current.EquipmentRevision + 1)
                return Fail("Equipment response revision is not consecutive.", out error);

            InventoryEquipmentState source = FindIdentity(current, intent.SourceIdentity);
            int expectedSourceSlot = intent.Kind == InventoryEquipmentOperationKind.Equip
                ? 0 : intent.SourceSlotCode;
            if (source == null || source.SlotCode != expectedSourceSlot)
                return Fail("Equipment source no longer matches ownership.", out error);

            InventoryEquipmentState target = null;
            int newSourceSlot;
            int newTargetSlot;
            switch (intent.Kind)
            {
                case InventoryEquipmentOperationKind.Equip:
                    if (!IsEquipmentSlot(intent.TargetSlotCode))
                        return Fail("Equipment target slot is invalid.", out error);
                    target = FindSlot(current, intent.TargetSlotCode);
                    newSourceSlot = intent.TargetSlotCode;
                    newTargetSlot = 0;
                    break;
                case InventoryEquipmentOperationKind.Unequip:
                    if (!IsEquipmentSlot(intent.SourceSlotCode)
                        || intent.TargetSlotCode != 0)
                        return Fail("Unequip source slot is invalid.", out error);
                    newSourceSlot = 0;
                    newTargetSlot = 0;
                    break;
                case InventoryEquipmentOperationKind.SlotMove:
                    if (!IsEquipmentSlot(intent.SourceSlotCode)
                        || !IsEquipmentSlot(intent.TargetSlotCode)
                        || intent.SourceSlotCode == intent.TargetSlotCode)
                        return Fail("Equipment move slots are invalid.", out error);
                    target = FindSlot(current, intent.TargetSlotCode);
                    newSourceSlot = intent.TargetSlotCode;
                    newTargetSlot = intent.SourceSlotCode;
                    break;
                default:
                    return Fail("Unknown equipment operation.", out error);
            }

            InventoryV2EquipmentDto[] changes = result.equipmentItemChanges;
            if (changes == null || changes.Length != (target == null ? 1 : 2))
                return Fail("Equipment response is not an atomic change set.", out error);
            var byInstance = new Dictionary<string, InventoryV2EquipmentDto>(StringComparer.Ordinal);
            foreach (InventoryV2EquipmentDto change in changes)
                byInstance.Add(change.itemInstanceId, change);
            if (!Matches(byInstance, source, newSourceSlot)
                || (target != null && !Matches(byInstance, target, newTargetSlot)))
                return Fail("Equipment response changed unexpected instances.", out error);

            var equipment = new List<InventoryEquipmentState>(current.Equipment.Count);
            var occupied = new HashSet<int>();
            int bagCount = current.Stacks.Count;
            foreach (InventoryEquipmentState item in current.Equipment)
            {
                InventoryEquipmentState updated = byInstance.TryGetValue(
                    item.ItemInstanceId, out InventoryV2EquipmentDto change)
                    ? new InventoryEquipmentState(item.ItemInstanceId, item.EquipmentId,
                        change.isBound, change.slotCode) : item;
                if (updated.SlotCode == 0) bagCount++;
                else if (!occupied.Add(updated.SlotCode))
                    return Fail("Equipment response assigned a slot twice.", out error);
                equipment.Add(updated);
            }
            if (bagCount > current.Capacity)
                return Fail("Equipment response exceeded bag capacity.", out error);

            var wallets = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, long> wallet in current.Wallets)
                wallets.Add(wallet.Key, wallet.Value);
            next = new InventoryOwnershipSnapshot(current.CharacterDbId,
                result.inventoryRevision, result.equipmentRevision,
                current.Capacity, current.CatalogVersion,
                InventoryV2Protocol.EquipmentCatalogVersion,
                new List<InventoryStackState>(current.Stacks), equipment, wallets,
                new InventoryAttributeBonusState(result.attributeBonus.maxHp,
                    result.attributeBonus.attack, result.attributeBonus.defense,
                    result.attributeBonus.maxMp, result.attributeBonus.critRateBps,
                    result.attributeBonus.critDamageBps, result.attributeBonus.magicAttack,
                    result.attributeBonus.magicDefense));
            error = string.Empty;
            return true;
        }

        private static bool IsEquipmentSlot(int code)
        {
            return code >= 1 && code <= InventoryV2Protocol.EquipmentSlotCount;
        }

        private static InventoryEquipmentState FindIdentity(
            InventoryOwnershipSnapshot snapshot, string identity)
        {
            foreach (InventoryEquipmentState item in snapshot.Equipment)
                if (string.Equals(item.Identity, identity, StringComparison.Ordinal))
                    return item;
            return null;
        }

        private static InventoryEquipmentState FindSlot(
            InventoryOwnershipSnapshot snapshot, int slotCode)
        {
            foreach (InventoryEquipmentState item in snapshot.Equipment)
                if (item.SlotCode == slotCode) return item;
            return null;
        }

        private static bool Matches(
            Dictionary<string, InventoryV2EquipmentDto> changes,
            InventoryEquipmentState item, int slotCode)
        {
            return changes.TryGetValue(item.ItemInstanceId, out InventoryV2EquipmentDto change)
                && change.slotCode == slotCode
                && string.Equals(change.equipmentId, item.EquipmentId, StringComparison.Ordinal);
        }

        private static bool Fail(string message, out string error)
        {
            error = message;
            return false;
        }
    }
}
