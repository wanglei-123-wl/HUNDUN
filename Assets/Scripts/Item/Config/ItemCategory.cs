using UnityEngine;

namespace Game.Items
{
    public enum ItemCategory
    {
        [InspectorName("其他")]
        Other = 0,
        [InspectorName("装备")]
        Equipment = 1,
        [InspectorName("消耗品")]
        Consumable = 2,
        [InspectorName("材料")]
        Material = 3,
        [InspectorName("货币")]
        Currency = 4,
        [InspectorName("任务物品")]
        Quest = 5,
        [InspectorName("技能书")]
        SkillBook = 6,
        [InspectorName("锻造图纸")]
        ForgingBlueprint = 7
    }
}
