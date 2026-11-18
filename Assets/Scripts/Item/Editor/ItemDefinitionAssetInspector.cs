using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Game.Items.Editor
{
    [CustomEditor(typeof(ItemDefinitionAsset))]
    internal sealed class ItemDefinitionAssetInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            SerializedProperty script = serializedObject.FindProperty("m_Script");
            if (script != null)
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.PropertyField(script, new GUIContent("脚本"));
            }

            SerializedProperty definition = serializedObject.FindProperty("definition");
            if (definition == null)
            {
                DrawDefaultInspector();
                return;
            }

            EditorGUILayout.LabelField("物品资料", EditorStyles.boldLabel);
            DrawField(definition, "ItemId", "物品ID", "必须与服务端的 itemId 完全一致");
            DrawField(definition, "EquipmentId", "装备ID", "仅装备填写，必须与服务端的 equipmentId 一致");
            DrawField(definition, "EquipmentCategory", "装备类别", "仅装备填写，非装备保持“非装备”");
            DrawField(definition, "DisplayName", "显示名称");
            DrawField(definition, "Description", "物品描述");
            DrawField(definition, "Icon", "物品图标");
            DrawField(definition, "RemoteIconAddress", "远程图标地址",
                "远程独占时填写地址，物品图标必须留空");
            DrawField(definition, "Category", "物品分类");
            DrawField(definition, "DisplayCategory", "详情展示分类",
                "自动时根据物品分类推导；需要特殊展示名称时再手动指定");
            DrawField(definition, "DetailText", "详情文本");
            DrawField(definition, "DetailEntries", "详情属性");
            ItemDefinitionAsset itemAsset = target as ItemDefinitionAsset;
            if (itemAsset != null && itemAsset.Definition != null
                && itemAsset.Definition.Category == ItemCategory.SkillBook)
                DrawLinkedSkillId(definition);
            DrawField(definition, "Quality", "品质等级");
            DrawField(definition, "QualityColor", "品质颜色");
            DrawField(definition, "MaxStack", "最大堆叠数");
            DrawField(definition, "IsCurrency", "是否货币");
            DrawField(definition, "WorldPrefab", "地面掉落预制体");
            DrawField(definition, "PickupSound", "拾取音效");

            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();
            if (GUILayout.Button("发布到物品总表"))
            {
                bool added = ItemCatalogEditor.TryRegister(
                    (ItemDefinitionAsset)target, out string message);
                EditorUtility.DisplayDialog(
                    added ? "物品总表" : "无法加入物品总表", message, "确定");
            }
        }

        private static void DrawField(SerializedProperty definition, string fieldName,
            string label, string tooltip = null)
        {
            SerializedProperty field = definition.FindPropertyRelative(fieldName);
            if (field != null)
                EditorGUILayout.PropertyField(field, new GUIContent(label, tooltip), true);
        }

        private static void DrawLinkedSkillId(SerializedProperty definition)
        {
            SerializedProperty field =
                definition.FindPropertyRelative("LinkedSkillId");
            if (field == null)
            {
                return;
            }

            List<string> skillIds = CollectRegisteredSkillIds();
            if (skillIds.Count == 0)
            {
                EditorGUILayout.PropertyField(field,
                    new GUIContent("关联技能ID",
                        "当前场景未找到有效技能注册表，暂时可手动填写"), true);
                return;
            }

            string current = field.stringValue == null
                ? string.Empty
                : field.stringValue.Trim();
            var options = new List<string> { "请选择技能" };
            options.AddRange(skillIds);

            int selectedIndex = string.IsNullOrEmpty(current)
                ? 0
                : skillIds.IndexOf(current) + 1;
            if (selectedIndex == 0 && !string.IsNullOrEmpty(current))
            {
                options.Add("当前值（未注册）: " + current);
                selectedIndex = options.Count - 1;
            }

            EditorGUI.BeginChangeCheck();
            int nextIndex = EditorGUILayout.Popup(
                new GUIContent("关联技能ID",
                    "从当前场景技能注册表中选择已注册技能"),
                selectedIndex, options.ToArray());
            if (EditorGUI.EndChangeCheck())
            {
                field.stringValue = nextIndex <= 0
                    ? string.Empty
                    : options[nextIndex];
            }
        }

        private static List<string> CollectRegisteredSkillIds()
        {
            var skillIds = new List<string>();
            技能注册表[] registries =
                UnityEngine.Object.FindObjectsOfType<技能注册表>(true);
            for (int index = 0; index < registries.Length; index++)
            {
                技能注册表 registry = registries[index];
                if (registry == null)
                {
                    continue;
                }

                string[] registeredIds = registry.获取有效技能ID();
                for (int idIndex = 0; idIndex < registeredIds.Length; idIndex++)
                {
                    string skillId = registeredIds[idIndex];
                    if (!string.IsNullOrWhiteSpace(skillId)
                        && !skillIds.Contains(skillId))
                    {
                        skillIds.Add(skillId);
                    }
                }
            }

            skillIds.Sort(StringComparer.Ordinal);
            return skillIds;
        }
    }
}
