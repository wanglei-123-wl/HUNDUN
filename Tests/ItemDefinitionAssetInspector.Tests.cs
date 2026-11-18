using System;
using System.Linq;
using System.Reflection;
using Game.Items;
using Game.Items.Editor;
using UnityEditor;
using UnityEngine;

internal static class ItemDefinitionAssetInspectorTests
{
    public static void Run()
    {
        var definition = new ItemDefinition
        {
            ItemId = "server_item",
            DisplayName = "Original name"
        };
        var asset = new ItemDefinitionAsset();
        typeof(ItemDefinitionAsset).GetField("definition",
            BindingFlags.Instance | BindingFlags.NonPublic).SetValue(asset, definition);
        EditorGUILayout.Labels.Clear();

        var inspector = new ItemDefinitionAssetInspector
        {
            target = asset,
            serializedObject = new SerializedObject(asset)
        };
        inspector.OnInspectorGUI();

        string[] expected =
        {
            "脚本", "物品资料", "物品ID", "装备ID", "装备类别",
            "显示名称", "物品描述",
            "物品图标", "远程图标地址", "物品分类", "详情展示分类", "详情文本", "详情属性",
            "品质等级", "品质颜色",
            "最大堆叠数", "是否货币", "地面掉落预制体", "拾取音效"
        };
        Check(EditorGUILayout.Labels.SequenceEqual(expected),
            "Every item authoring field must have a Chinese Inspector label.");
        Check(definition.ItemId == "server_item"
              && definition.DisplayName == "Original name",
            "Drawing the Inspector must not rewrite serialized item data.");

        ItemCategory[] values = (ItemCategory[])Enum.GetValues(typeof(ItemCategory));
        string[] categoryNames =
        {
            "其他", "装备", "消耗品", "材料", "货币", "任务物品", "技能书"
        };
        Check(values.Length == categoryNames.Length,
            "Category member count must stay unchanged.");
        for (int index = 0; index < values.Length; index++)
        {
            FieldInfo field = typeof(ItemCategory).GetField(values[index].ToString());
            var label = field.GetCustomAttribute<InspectorNameAttribute>();
            Check((int)values[index] == index && label?.DisplayName == categoryNames[index],
                "Category numeric values must remain intact with Chinese labels.");
        }

        definition.Category = ItemCategory.SkillBook;
        EditorGUILayout.Labels.Clear();
        inspector.OnInspectorGUI();
        Check(EditorGUILayout.Labels.Contains("关联技能ID"),
            "Skill books must expose the linked skill ID field.");

        EquipmentCategoryCode[] equipmentValues =
            (EquipmentCategoryCode[])Enum.GetValues(typeof(EquipmentCategoryCode));
        string[] equipmentNames =
        {
            "非装备", "武器", "头盔", "衣服", "项链", "戒指", "手镯", "靴子"
        };
        Check(equipmentValues.Length == equipmentNames.Length,
            "Equipment category labels must cover every authoring option.");
        for (int index = 0; index < equipmentValues.Length; index++)
        {
            FieldInfo field = typeof(EquipmentCategoryCode)
                .GetField(equipmentValues[index].ToString());
            var label = field.GetCustomAttribute<InspectorNameAttribute>();
            Check((int)equipmentValues[index] == index
                  && label?.DisplayName == equipmentNames[index],
                "Equipment category codes must keep their Chinese labels.");
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
