using System;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

[CustomEditor(typeof(StartupResourceServerSettings))]
public sealed class StartupResourceServerSettingsEditor : Editor
{
    private SerializedProperty servers;
    private SerializedProperty selection;
    private ReorderableList serverList;

    private void OnEnable()
    {
        var settings = (StartupResourceServerSettings)target;
        serializedObject.Update();
        SerializedProperty legacy = serializedObject.FindProperty("resourceServerAddress");
        servers = serializedObject.FindProperty("resourceServers");
        selection = serializedObject.FindProperty("selectedServerId");
        if (!EditorApplication.isPlayingOrWillChangePlaymode
            && !string.IsNullOrWhiteSpace(legacy.stringValue))
        {
            Undo.RecordObject(settings, "迁移资源服务器配置");
            if (settings.MigrateLegacyAddress())
            {
                EditorUtility.SetDirty(settings);
                if (PrefabUtility.IsPartOfPrefabInstance(settings))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(settings);
            }
            serializedObject.Update();
        }

        serverList = new ReorderableList(serializedObject, servers, true, true, true, true);
        serverList.elementHeight = EditorGUIUtility.singleLineHeight * 2f + 10f;
        serverList.drawHeaderCallback = rect => EditorGUI.LabelField(rect, "资源服务器列表");
        serverList.drawElementCallback = DrawServer;
        serverList.onAddCallback = AddServer;
        serverList.onRemoveCallback = RemoveServer;
    }

    [UnityEditor.Callbacks.DidReloadScripts]
    private static void RefreshAfterReload()
    {
        EditorApplication.delayCall += () =>
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode
                && Selection.activeGameObject != null
                && Selection.activeGameObject.GetComponent<StartupResourceServerSettings>() != null)
                ActiveEditorTracker.sharedTracker.ForceRebuild();
        };
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        using (new EditorGUI.DisabledScope(true))
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));

        bool playing = EditorApplication.isPlayingOrWillChangePlaymode;
        using (new EditorGUI.DisabledScope(playing))
        {
            serverList.DoLayoutList();
            DrawSelection();
        }
        serializedObject.ApplyModifiedProperties();

        var settings = (StartupResourceServerSettings)target;
        if (settings.TryGetSelectedAddress(out string address, out string error))
        {
            EditorGUILayout.LabelField("当前指向地址");
            EditorGUILayout.SelectableLabel(string.IsNullOrWhiteSpace(address)
                    ? "使用 Addressables Profile 的内置地址" : address,
                EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
        }
        else
        {
            EditorGUILayout.HelpBox(error, MessageType.Error);
        }

        EditorGUILayout.HelpBox(playing
                ? "运行中使用启动时的选择。如需切换，请停止运行后选择资源服，再重新运行。"
                : "点击 + 添加名称和 HTTP/HTTPS 地址，在“当前资源服”中切换。地址留空时使用内置地址；保存场景后，打包的客户端将使用当前选择。",
            MessageType.Info);
    }

    private void DrawServer(Rect rect, int index, bool active, bool focused)
    {
        SerializedProperty server = servers.GetArrayElementAtIndex(index);
        rect.y += 3f;
        rect.height = EditorGUIUtility.singleLineHeight;
        float previousLabelWidth = EditorGUIUtility.labelWidth;
        EditorGUIUtility.labelWidth = 42f;
        EditorGUI.PropertyField(rect, server.FindPropertyRelative("name"), new GUIContent("名称"));
        rect.y += EditorGUIUtility.singleLineHeight + 3f;
        EditorGUI.PropertyField(rect, server.FindPropertyRelative("address"),
            new GUIContent("地址", "填写 HTTP/HTTPS 资源目录地址，例如以 /StandaloneWindows64 结尾。留空时使用内置地址。"));
        EditorGUIUtility.labelWidth = previousLabelWidth;
    }

    private void DrawSelection()
    {
        var labels = new string[servers.arraySize + 1];
        var ids = new string[labels.Length];
        labels[0] = "Addressables 内置地址";
        ids[0] = string.Empty;
        int selectedIndex = string.IsNullOrEmpty(selection.stringValue) ? 0 : -1;
        for (int i = 0; i < servers.arraySize; i++)
        {
            SerializedProperty server = servers.GetArrayElementAtIndex(i);
            string name = server.FindPropertyRelative("name").stringValue;
            labels[i + 1] = string.IsNullOrWhiteSpace(name) ? "未命名资源服 " + (i + 1) : name;
            ids[i + 1] = server.FindPropertyRelative("id").stringValue;
            if (ids[i + 1] == selection.stringValue)
                selectedIndex = i + 1;
        }
        int nextIndex = EditorGUILayout.Popup("当前资源服", selectedIndex, labels);
        if (nextIndex != selectedIndex && nextIndex >= 0)
            selection.stringValue = ids[nextIndex];
    }

    private void AddServer(ReorderableList list)
    {
        int index = servers.arraySize;
        servers.InsertArrayElementAtIndex(index);
        SerializedProperty server = servers.GetArrayElementAtIndex(index);
        string id = Guid.NewGuid().ToString("N");
        server.FindPropertyRelative("id").stringValue = id;
        server.FindPropertyRelative("name").stringValue = "资源服 " + (index + 1);
        server.FindPropertyRelative("address").stringValue = string.Empty;
        if (servers.arraySize == 1 && string.IsNullOrEmpty(selection.stringValue))
            selection.stringValue = id;
        list.index = index;
    }

    private void RemoveServer(ReorderableList list)
    {
        if (list.index < 0 || list.index >= servers.arraySize)
            return;
        string removedId = servers.GetArrayElementAtIndex(list.index)
            .FindPropertyRelative("id").stringValue;
        servers.DeleteArrayElementAtIndex(list.index);
        if (selection.stringValue == removedId)
            selection.stringValue = string.Empty;
        list.index = Mathf.Min(list.index, servers.arraySize - 1);
    }
}
