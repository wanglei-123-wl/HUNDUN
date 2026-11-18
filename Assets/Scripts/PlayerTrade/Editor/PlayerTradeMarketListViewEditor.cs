using UnityEditor;
using UnityEngine;

namespace Game.PlayerTrade.Presentation.Editor
{
    [CustomEditor(typeof(PlayerTradeMarketListView))]
    public sealed class PlayerTradeMarketListViewEditor : UnityEditor.Editor
    {
        private SerializedProperty contentRoot;
        private SerializedProperty listingPrefab;
        private SerializedProperty itemDefinitions;
        private SerializedProperty pageSize;
        private SerializedProperty sort;
        private SerializedProperty scrollRect;
        private SerializedProperty columns;
        private SerializedProperty preloadItemThreshold;
        private SerializedProperty horizontalSpacing;
        private SerializedProperty verticalSpacing;
        private SerializedProperty leftPadding;
        private SerializedProperty rightPadding;
        private SerializedProperty topPadding;
        private SerializedProperty bottomPadding;
        private SerializedProperty networkManager;
        private SerializedProperty messageDispatcher;

        private void OnEnable()
        {
            contentRoot = serializedObject.FindProperty("contentRoot");
            listingPrefab = serializedObject.FindProperty("listingPrefab");
            itemDefinitions = serializedObject.FindProperty("itemDefinitions");
            pageSize = serializedObject.FindProperty("pageSize");
            sort = serializedObject.FindProperty("sort");
            scrollRect = serializedObject.FindProperty("scrollRect");
            columns = serializedObject.FindProperty("columns");
            preloadItemThreshold = serializedObject.FindProperty(
                "preloadItemThreshold");
            horizontalSpacing = serializedObject.FindProperty("horizontalSpacing");
            verticalSpacing = serializedObject.FindProperty("verticalSpacing");
            leftPadding = serializedObject.FindProperty("leftPadding");
            rightPadding = serializedObject.FindProperty("rightPadding");
            topPadding = serializedObject.FindProperty("topPadding");
            bottomPadding = serializedObject.FindProperty("bottomPadding");
            networkManager = serializedObject.FindProperty("networkManager");
            messageDispatcher = serializedObject.FindProperty("messageDispatcher");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField("列表", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(contentRoot,
                new GUIContent("内容根节点"));
            EditorGUILayout.PropertyField(listingPrefab,
                new GUIContent("市场格子预制体"));
            EditorGUILayout.PropertyField(itemDefinitions,
                new GUIContent("物品总表"));
            EditorGUILayout.PropertyField(pageSize,
                new GUIContent("每页数量"));
            EditorGUILayout.PropertyField(sort,
                new GUIContent("排序方式"));

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("滚动加载", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(scrollRect,
                new GUIContent("滚动区域"));
            EditorGUILayout.PropertyField(columns,
                new GUIContent("每行格子数"));
            EditorGUILayout.PropertyField(preloadItemThreshold,
                new GUIContent("滚动多少个物品后加载"));

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("布局（可视化调整）",
                EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(horizontalSpacing,
                new GUIContent("横向间距"));
            EditorGUILayout.PropertyField(verticalSpacing,
                new GUIContent("纵向间距"));
            EditorGUILayout.PropertyField(leftPadding,
                new GUIContent("左边距"));
            EditorGUILayout.PropertyField(rightPadding,
                new GUIContent("右边距"));
            EditorGUILayout.PropertyField(topPadding,
                new GUIContent("上边距"));
            EditorGUILayout.PropertyField(bottomPadding,
                new GUIContent("下边距"));

            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("网络", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(networkManager,
                new GUIContent("网络管理器"));
            EditorGUILayout.PropertyField(messageDispatcher,
                new GUIContent("消息分发器"));

            serializedObject.ApplyModifiedProperties();
        }
    }
}
