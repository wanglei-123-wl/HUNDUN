using Game.Items;
using Game.PlayerTrade.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Game.PlayerTrade.Presentation.Editor
{
    [CustomEditor(typeof(PlayerTradeClaimListView))]
    public sealed class PlayerTradeClaimListViewEditor : UnityEditor.Editor
    {
        private SerializedProperty contentRoot;
        private SerializedProperty claimPrefab;
        private SerializedProperty itemDefinitions;
        private SerializedProperty pageSize;
        private SerializedProperty scrollRect;
        private SerializedProperty verticalSpacing;
        private SerializedProperty topPadding;
        private SerializedProperty bottomPadding;
        private SerializedProperty leftPadding;
        private SerializedProperty rightPadding;
        private SerializedProperty networkManager;
        private SerializedProperty messageDispatcher;

        private void OnEnable()
        {
            contentRoot = serializedObject.FindProperty("contentRoot");
            claimPrefab = serializedObject.FindProperty("claimPrefab");
            itemDefinitions = serializedObject.FindProperty("itemDefinitions");
            pageSize = serializedObject.FindProperty("pageSize");
            scrollRect = serializedObject.FindProperty("scrollRect");
            verticalSpacing = serializedObject.FindProperty("verticalSpacing");
            topPadding = serializedObject.FindProperty("topPadding");
            bottomPadding = serializedObject.FindProperty("bottomPadding");
            leftPadding = serializedObject.FindProperty("leftPadding");
            rightPadding = serializedObject.FindProperty("rightPadding");
            networkManager = serializedObject.FindProperty("networkManager");
            messageDispatcher = serializedObject.FindProperty("messageDispatcher");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField("待领取列表", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(contentRoot,
                new GUIContent("内容根节点"));
            EditorGUILayout.PropertyField(claimPrefab,
                new GUIContent("待领取格子预制体"));
            EditorGUILayout.PropertyField(itemDefinitions,
                new GUIContent("物品总表"));
            EditorGUILayout.PropertyField(pageSize,
                new GUIContent("每页数量"));

            if (GUILayout.Button("自动绑定待领取预制体和物品总表"))
                AutoBind();

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("滚动区域", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(scrollRect,
                new GUIContent("滚动区域"));

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("布局（可视化调整）",
                EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(verticalSpacing,
                new GUIContent("格子上下间距"));
            EditorGUILayout.PropertyField(topPadding,
                new GUIContent("上边距"));
            EditorGUILayout.PropertyField(bottomPadding,
                new GUIContent("下边距"));
            EditorGUILayout.PropertyField(leftPadding,
                new GUIContent("左边距"));
            EditorGUILayout.PropertyField(rightPadding,
                new GUIContent("右边距"));

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("网络", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(networkManager,
                new GUIContent("网络管理器"));
            EditorGUILayout.PropertyField(messageDispatcher,
                new GUIContent("消息分发器"));

            serializedObject.ApplyModifiedProperties();
        }

        private void AutoBind()
        {
            PlayerTradeClaimListView view =
                (PlayerTradeClaimListView)target;

            if (claimPrefab.objectReferenceValue == null)
            {
                PlayerTradeClaimCardView prefab =
                    AssetDatabase.LoadAssetAtPath<PlayerTradeClaimCardView>(
                        "Assets/预制体/格子预制体/待领取预制体.prefab");
                if (prefab != null)
                    claimPrefab.objectReferenceValue = prefab;
            }

            if (itemDefinitions.objectReferenceValue == null)
            {
                ItemDefinitionTable table =
                    FindObjectOfType<ItemDefinitionTable>(true);
                if (table != null)
                    itemDefinitions.objectReferenceValue = table;
            }

            if (contentRoot.objectReferenceValue == null)
            {
                RectTransform rect = view.GetComponent<RectTransform>();
                if (rect != null)
                    contentRoot.objectReferenceValue = rect;
            }

            if (scrollRect.objectReferenceValue == null)
            {
                ScrollRect parentScroll =
                    view.GetComponentInParent<ScrollRect>(true);
                if (parentScroll != null)
                    scrollRect.objectReferenceValue = parentScroll;
            }

            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
        }
    }
}
