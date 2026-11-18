using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Inventory.Config.V2
{
    [Serializable]
    public sealed class InventoryEquipmentDefinition
    {
        public string equipmentId;
        public string itemId;
        public string displayName;
        public int categoryCode;
        public string subType;
        public int quality;
        public int requiredLevel;
        public string requiredRoleId;
        public string iconKey;
        public Sprite icon;
        public int maxHpBonus;
        public int attackBonus;
        public int defenseBonus;
    }

    [CreateAssetMenu(fileName = "InventoryEquipmentCatalog",
        menuName = "Game/Inventory/Equipment Catalog V2")]
    public sealed class InventoryEquipmentCatalog : ScriptableObject
    {
        [SerializeField] private int equipmentCatalogVersion = 2;
        [SerializeField] private List<InventoryEquipmentDefinition> definitions =
            new List<InventoryEquipmentDefinition>();

        public int Version => equipmentCatalogVersion;

        public bool TryGet(string equipmentId, out InventoryEquipmentDefinition definition)
        {
            if (!string.IsNullOrWhiteSpace(equipmentId))
            {
                foreach (InventoryEquipmentDefinition candidate in definitions)
                {
                    if (candidate != null && string.Equals(
                        candidate.equipmentId, equipmentId, StringComparison.Ordinal))
                    {
                        definition = candidate;
                        return true;
                    }
                }
            }
            definition = null;
            return false;
        }
    }
}
