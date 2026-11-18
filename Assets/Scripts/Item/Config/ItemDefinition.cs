using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Items
{
    public enum EquipmentCategoryCode
    {
        [InspectorName("非装备")]
        None = 0,
        [InspectorName("武器")]
        Weapon = 1,
        [InspectorName("头盔")]
        Helmet = 2,
        [InspectorName("衣服")]
        Armor = 3,
        [InspectorName("项链")]
        Necklace = 4,
        [InspectorName("戒指")]
        Ring = 5,
        [InspectorName("手镯")]
        Bracelet = 6,
        [InspectorName("靴子")]
        Boots = 7,
        [InspectorName("盾牌")]
        Shield = 8,
        [InspectorName("法宝")]
        Artifact = 9,
        [InspectorName("法阵")]
        Formation = 10
    }

    [Serializable]
    public sealed class ItemDefinition
    {
        public string ItemId;
        public string EquipmentId;
        public EquipmentCategoryCode EquipmentCategory;
        public string DisplayName;

        [TextArea]
        public string Description;

        public Sprite Icon;
        public string RemoteIconAddress;
        public ItemCategory Category = ItemCategory.Other;
        public string LinkedSkillId;
        public int Quality;
        public Color QualityColor = Color.white;
        public int MaxStack = 1;
        public bool IsCurrency;

        [Header("详情展示")]
        public ItemDetailCategory DisplayCategory = ItemDetailCategory.Auto;

        [TextArea(3, 8)]
        public string DetailText;

        public List<ItemDetailEntry> DetailEntries =
            new List<ItemDetailEntry>();

        public GameObject WorldPrefab;
        public AudioClip PickupSound;

        public bool IsValid => !string.IsNullOrWhiteSpace(ItemId);

        internal ItemDefinition WithIcon(Sprite icon)
        {
            var copy = (ItemDefinition)MemberwiseClone();
            copy.Icon = icon;
            return copy;
        }
    }
}
