using Game.Monsters.Config;
using UnityEditor;
using UnityEngine;

namespace Game.Monsters.Editor
{
    [CustomEditor(typeof(MonsterDefinitionAsset))]
    internal sealed class MonsterDefinitionAssetInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            SerializedProperty script = serializedObject.FindProperty("m_Script");
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.PropertyField(script, new GUIContent("脚本"));

            EditorGUILayout.LabelField("怪物资料", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("id"),
                new GUIContent("怪物ID", "必须与服务端发送的 prefabId 一致；找不到时会尝试 monsterId。"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("displayName"),
                new GUIContent("显示名称", "怪物的本地显示名称，当前阶段只保存配置。"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("prefab"),
                new GUIContent("怪物预制体"));
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space();
            if (GUILayout.Button("发布到怪物总表"))
            {
                bool published = MonsterCatalogEditor.TryRegister(
                    (MonsterDefinitionAsset)target, out string message);
                EditorUtility.DisplayDialog(published ? "怪物总表" : "无法发布怪物配置",
                    message, "确定");
            }
        }
    }
}
