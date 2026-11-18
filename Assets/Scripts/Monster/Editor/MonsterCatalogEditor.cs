using System.Collections.Generic;
using Game.Monsters.Config;
using UnityEditor;
using UnityEngine;

namespace Game.Monsters.Editor
{
    internal static class MonsterCatalogEditor
    {
        private const string CatalogPath = "Assets/Data/Monsters/怪物总表.asset";

        internal static bool TryRegister(MonsterDefinitionAsset asset, out string message)
        {
            if (asset == null || string.IsNullOrEmpty(AssetDatabase.GetAssetPath(asset)))
            {
                message = "请先将怪物配置保存到项目 Assets 中。";
                return false;
            }

            MonsterCatalog catalog = LoadCatalog(out message);
            if (catalog == null)
                return false;

            var entries = new List<MonsterDefinitionAsset>(catalog.MonsterAssets);
            bool alreadyRegistered = entries.Contains(asset);
            if (!alreadyRegistered)
                entries.Add(asset);

            if (!MonsterCatalogValidation.TryValidate(entries, out string report))
            {
                message = "怪物配置校验失败：\n" + report;
                return false;
            }

            if (!alreadyRegistered)
            {
                var serialized = new SerializedObject(catalog);
                serialized.Update();
                SerializedProperty references = serialized.FindProperty("monsterAssets");
                if (references == null)
                {
                    message = "怪物总表缺少怪物配置列表。";
                    return false;
                }

                Undo.RecordObject(catalog, "发布怪物到总表");
                references.arraySize++;
                references.GetArrayElementAtIndex(references.arraySize - 1)
                    .objectReferenceValue = asset;
                serialized.ApplyModifiedProperties();
                AssetDatabase.SaveAssetIfDirty(catalog);
            }

            AssetDatabase.SaveAssetIfDirty(asset);
            catalog.RefreshLookup();
            message = alreadyRegistered
                ? "已更新怪物总表中的配置：" + asset.Id
                : "已发布到怪物总表：" + asset.Id;
            return true;
        }

        [MenuItem("工具/怪物/校验怪物总表")]
        private static void ValidateCatalog()
        {
            MonsterCatalog catalog = LoadCatalog(out string error);
            if (catalog == null)
            {
                EditorUtility.DisplayDialog("怪物总表", error, "确定");
                return;
            }

            bool valid = MonsterCatalogValidation.TryValidate(
                catalog.MonsterAssets, out string report);
            if (valid)
                catalog.RefreshLookup();
            EditorUtility.DisplayDialog("怪物总表",
                valid ? "怪物总表校验通过。" : "怪物总表校验失败：\n" + report,
                "确定");
        }

        [MenuItem("工具/怪物/定位怪物总表")]
        private static void LocateCatalog()
        {
            MonsterCatalog catalog = LoadCatalog(out string error);
            if (catalog == null)
            {
                EditorUtility.DisplayDialog("怪物总表", error, "确定");
                return;
            }

            Selection.activeObject = catalog;
            EditorGUIUtility.PingObject(catalog);
        }

        internal static MonsterCatalog LoadCatalog(out string error)
        {
            MonsterCatalog catalog = AssetDatabase.LoadAssetAtPath<MonsterCatalog>(CatalogPath);
            error = catalog == null ? "找不到怪物总表：" + CatalogPath : string.Empty;
            return catalog;
        }
    }

    [CustomEditor(typeof(MonsterCatalog))]
    internal sealed class MonsterCatalogInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            SerializedProperty script = serializedObject.FindProperty("m_Script");
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.PropertyField(script, new GUIContent("脚本"));

            EditorGUILayout.PropertyField(serializedObject.FindProperty("selectionRingPrefab"),
                new GUIContent("共用选中光圈", "怪物生成时挂载，初始停用。"));
            SerializedProperty entries = serializedObject.FindProperty("monsterAssets");
            EditorGUILayout.LabelField("已发布怪物", EditorStyles.boldLabel);
            for (int index = 0; index < entries.arraySize; index++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.PropertyField(entries.GetArrayElementAtIndex(index),
                        new GUIContent("怪物配置 " + (index + 1)));
                    if (GUILayout.Button(new GUIContent("-", "从总表移除"), GUILayout.Width(26)))
                    {
                        entries.DeleteArrayElementAtIndex(index);
                        if (index < entries.arraySize &&
                            entries.GetArrayElementAtIndex(index).objectReferenceValue == null)
                            entries.DeleteArrayElementAtIndex(index);
                        break;
                    }
                }
            }

            if (GUILayout.Button("添加怪物配置"))
            {
                entries.arraySize++;
                entries.GetArrayElementAtIndex(entries.arraySize - 1).objectReferenceValue = null;
            }
            if (serializedObject.ApplyModifiedProperties())
                ((MonsterCatalog)target).RefreshLookup();

            if (GUILayout.Button("校验怪物总表"))
            {
                bool valid = MonsterCatalogValidation.TryValidate(
                    ((MonsterCatalog)target).MonsterAssets, out string report);
                EditorUtility.DisplayDialog("怪物总表",
                    valid ? "怪物总表校验通过。" : "怪物总表校验失败：\n" + report,
                    "确定");
            }
        }
    }
}
