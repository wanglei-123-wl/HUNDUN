using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class NetworkEndpointSettingsExporter
{
    private const string SourceScenePath = "Assets/Scenes/WorldShell.unity";
    private const string AssetPath = "Assets/Resources/NetworkEndpointSettings.asset";

    static NetworkEndpointSettingsExporter()
    {
        EditorSceneManager.sceneSaved += OnSceneSaved;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EditorApplication.delayCall += CreateInitialSettings;
    }

    private static void CreateInitialSettings()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += CreateInitialSettings;
            return;
        }

        if (AssetDatabase.LoadAssetAtPath<NetworkEndpointSettings>(AssetPath) == null
            && !TrySync(out string error))
        {
            Debug.LogError(error);
        }
    }

    private static void OnSceneSaved(Scene scene)
    {
        if (scene.path != SourceScenePath || EditorApplication.isPlaying)
            return;

        if (!TrySync(out string error))
        {
            Debug.LogError(error);
        }
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.ExitingEditMode)
        {
            return;
        }

        if (!TrySync(out string error))
        {
            Debug.LogError(error);
            EditorApplication.isPlaying = false;
        }
    }

    public static bool TrySync(out string error)
    {
        error = null;
        Scene originalActiveScene = SceneManager.GetActiveScene();
        Scene sourceScene = SceneManager.GetSceneByPath(SourceScenePath);
        bool closeSourceScene = !sourceScene.IsValid() || !sourceScene.isLoaded;
        if (closeSourceScene)
        {
            try
            {
                sourceScene = EditorSceneManager.OpenScene(
                    SourceScenePath, OpenSceneMode.Additive);
            }
            catch (Exception exception)
            {
                error = "Cannot open network configuration scene: " + exception.Message;
                return false;
            }
        }

        try
        {
            if (sourceScene.isDirty)
            {
                error = "Save WorldShell before playing or building; network settings are not saved.";
                return false;
            }

            NetworkManager source = null;
            int sourceCount = 0;
            foreach (GameObject root in sourceScene.GetRootGameObjects())
            {
                if (root.name != "NetworkSystem")
                {
                    continue;
                }

                foreach (NetworkManager candidate in root.GetComponents<NetworkManager>())
                {
                    source = candidate;
                    sourceCount++;
                }
            }

            if (sourceCount != 1 || source == null)
            {
                error = "WorldShell must have exactly one Network Manager on its NetworkSystem root.";
                return false;
            }

            if (!source.TryGetSelectedEndpointProfile(out NetworkEndpointProfile profile,
                    out error))
            {
                error = "WorldShell / NetworkSystem: " + error;
                return false;
            }

            NetworkEndpointSettings settings =
                AssetDatabase.LoadAssetAtPath<NetworkEndpointSettings>(AssetPath);
            if (settings == null)
            {
                if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                {
                    AssetDatabase.CreateFolder("Assets", "Resources");
                }

                settings = ScriptableObject.CreateInstance<NetworkEndpointSettings>();
                AssetDatabase.CreateAsset(settings, AssetPath);
            }

            if (settings.ServerHost != profile.TcpHost
                || settings.TcpPort != profile.TcpPort
                || settings.HttpPort != profile.HttpPort
                || settings.HttpBaseUrl != profile.HttpBaseUrl
                || settings.SelectedProfileName != profile.Name)
            {
                settings.SetFromScene(profile);
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
            }

            return true;
        }
        catch (Exception exception)
        {
            error = "Cannot generate network settings from WorldShell: "
                + exception.Message;
            return false;
        }
        finally
        {
            if (closeSourceScene)
            {
                try
                {
                    EditorSceneManager.CloseScene(sourceScene, true);
                }
                finally
                {
                    if (originalActiveScene.IsValid() && originalActiveScene.isLoaded)
                    {
                        SceneManager.SetActiveScene(originalActiveScene);
                    }
                }
            }
        }
    }
}

[CustomEditor(typeof(NetworkManager))]
public sealed class NetworkManagerEndpointEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        SerializedProperty profiles = serializedObject.FindProperty("endpointProfiles");
        SerializedProperty selection = serializedObject.FindProperty("selectedProfileName");

        using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
        {
            EditorGUILayout.PropertyField(profiles, true);
            if (profiles.arraySize == 0)
            {
                EditorGUILayout.HelpBox("运行前请先添加服务器环境。", MessageType.Warning);
            }
            else
            {
                string[] labels = new string[profiles.arraySize];
                int selectedIndex = -1;
                for (int i = 0; i < profiles.arraySize; i++)
                {
                    labels[i] = profiles.GetArrayElementAtIndex(i)
                        .FindPropertyRelative("name").stringValue;
                    if (labels[i] == selection.stringValue)
                        selectedIndex = i;
                }

                if (selectedIndex < 0)
                    EditorGUILayout.HelpBox("请选择列表中已有的服务器环境。", MessageType.Warning);

                EditorGUI.BeginChangeCheck();
                int newIndex = EditorGUILayout.Popup("当前环境",
                    Mathf.Max(0, selectedIndex), labels);
                if (EditorGUI.EndChangeCheck())
                    selection.stringValue = labels[newIndex];
            }
        }

        Editor.DrawPropertiesExcluding(serializedObject, "m_Script",
            "endpointProfiles", "selectedProfileName", "serverHost", "serverPort", "httpPort");
        serializedObject.ApplyModifiedProperties();

        if (EditorApplication.isPlaying)
        {
            NetworkEndpointSnapshot active = NetworkEndpointRuntime.Load();
            EditorGUILayout.HelpBox(active == null
                ? "当前没有有效的服务器环境。"
                : "正在使用的环境：" + active.SelectedProfileName
                    + "\nTCP: " + active.ServerHost + ":" + active.TcpPort
                    + "\nHTTP: " + active.HttpBaseUrl
                    + "\n如需切换环境，请先停止运行。",
                MessageType.Info);
        }
        else if (((NetworkManager)target).TryGetSelectedEndpointProfile(
            out NetworkEndpointProfile selected, out _))
        {
            EditorGUILayout.LabelField("TCP", selected.TcpHost + ":" + selected.TcpPort);
            EditorGUILayout.LabelField("HTTP", selected.HttpBaseUrl);
        }
    }
}

public sealed class NetworkEndpointSettingsBuildSync : IPreprocessBuildWithReport
{
    public int callbackOrder => 0;

    public void OnPreprocessBuild(BuildReport report)
    {
        if (!NetworkEndpointSettingsExporter.TrySync(out string error))
        {
            throw new BuildFailedException(error);
        }
    }
}
