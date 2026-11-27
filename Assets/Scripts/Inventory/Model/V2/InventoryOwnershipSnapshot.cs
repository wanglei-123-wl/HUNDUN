using System;
using System.Collections.Generic;

namespace Game.Inventory.Model.V2
{
    public sealed class InventoryAttributeBonusState
    {
        public int MaxHp { get; }
        public int Attack { get; }
        public int Defense { get; }
        public int MaxMp { get; }
        public int CritRateBps { get; }
        public int CritDamageBps { get; }
        public int MagicAttack { get; }
        public int MagicDefense { get; }

        public InventoryAttributeBonusState(int maxHp, int attack, int defense,
            int maxMp = 0, int critRateBps = 0, int critDamageBps = 0,
            int magicAttack = 0, int magicDefense = 0)
        {
            MaxHp = maxHp;
            Attack = attack;
            Defense = defense;
            MaxMp = maxMp;
            CritRateBps = critRateBps;
            CritDamageBps = critDamageBps;
            MagicAttack = magicAttack;
            MagicDefense = magicDefense;
        }
    }

    public sealed class InventoryStackState
    {
        public long StackId { get; }
        public string ItemId { get; }
        public int Count { get; }
        public bool IsBound { get; }
        public int RemainingUses { get; }
        public int DisplayQuantity => RemainingUses > 0 ? RemainingUses : Count;
        public string Identity => "s:" + StackId.ToString(System.Globalization.CultureInfo.InvariantCulture);

        public InventoryStackState(long stackId, string itemId, int count, bool isBound, int remainingUses = 0)
        {
            StackId = stackId;
            ItemId = itemId;
            Count = count;
            IsBound = isBound;
            RemainingUses = IsTeleportStone(itemId) && remainingUses == 0 ? 50 : remainingUses;
        }

        internal static bool IsTeleportStone(string itemId) =>
            itemId == "huicheng_stone_01" || itemId == "suiji_stone_01";
    }

    public sealed class InventoryEquipmentState
    {
        public string ItemInstanceId { get; }
        public string EquipmentId { get; }
        public bool IsBound { get; }
        public int SlotCode { get; }
        public string Identity => "e:" + ItemInstanceId;

        public InventoryEquipmentState(string itemInstanceId, string equipmentId, bool isBound, int slotCode)
        {
            ItemInstanceId = itemInstanceId;
            EquipmentId = equipmentId;
            IsBound = isBound;
            SlotCode = slotCode;
        }
    }

    public sealed class InventoryOwnershipSnapshot
    {
        public long CharacterDbId { get; }
        public long InventoryRevision { get; }
        public long EquipmentRevision { get; }
        public int Capacity { get; }
        public int CatalogVersion { get; }
        public bool CatalogMatchesClient { get; }
        public IReadOnlyList<InventoryStackState> Stacks { get; }
        public IReadOnlyList<InventoryEquipmentState> Equipment { get; }
        public IReadOnlyDictionary<string, long> Wallets { get; }
        public InventoryAttributeBonusState AttributeBonus { get; }

        internal InventoryOwnershipSnapshot(
            long characterDbId, long inventoryRevision, long equipmentRevision,
            int capacity, int catalogVersion, int clientCatalogVersion,
            List<InventoryStackState> stacks, List<InventoryEquipmentState> equipment,
            Dictionary<string, long> wallets, InventoryAttributeBonusState attributeBonus)
        {
            CharacterDbId = characterDbId;
            InventoryRevision = inventoryRevision;
            EquipmentRevision = equipmentRevision;
            Capacity = capacity;
            CatalogVersion = catalogVersion;
            CatalogMatchesClient = catalogVersion == clientCatalogVersion
                || (clientCatalogVersion == 4 && catalogVersion == 2);
            Stacks = stacks.AsReadOnly();
            Equipment = equipment.AsReadOnly();
            Wallets = new System.Collections.ObjectModel.ReadOnlyDictionary<string, long>(wallets);
            AttributeBonus = attributeBonus ?? throw new ArgumentNullException(nameof(attributeBonus));
        }

        public bool TryGetItemCount(string itemId, out long count)
        {
            count = 0;
            if (string.IsNullOrWhiteSpace(itemId))
                return false;
            foreach (InventoryStackState stack in Stacks)
                if (string.Equals(stack.ItemId, itemId, StringComparison.Ordinal))
                    count += stack.Count;
            return true;
        }
    }
}
