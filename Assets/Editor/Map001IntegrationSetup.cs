using System;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class Map001IntegrationSetup
{
    private const string SourcePath = "Assets/Map001/map001.unity";
    private const string GameplayPath = "Assets/Scenes/Map001 Gameplay.unity";
    private const string ShellPath = "Assets/Scenes/WorldShell.unity";

    [MenuItem("Tools/World/Create Map001 Gameplay Scene")]
    public static void Create()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[Map001] Leave Play mode before creating the gameplay scene.");
            return;
        }

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(GameplayPath) != null)
        {
            Debug.LogError("[Map001] Gameplay scene already exists; it was not overwritten.");
            return;
        }

        try
        {
            Scene source = SceneManager.GetSceneByPath(SourcePath);
            if (source.IsValid() && source.isLoaded)
            {
                if (!EditorSceneManager.SaveScene(source, GameplayPath, true))
                    throw new InvalidOperationException("Could not save the Map001 scene copy.");
            }
            else if (!AssetDatabase.CopyAsset(SourcePath, GameplayPath))
            {
                throw new InvalidOperationException("Could not copy the Map001 scene.");
            }

            AssetDatabase.Refresh();
            Scene map = EditorSceneManager.OpenScene(GameplayPath, OpenSceneMode.Additive);
            try
            {
                GameObject camera = null;
                bool hasLight = false;
                bool hasVolume = false;
                foreach (GameObject root in map.GetRootGameObjects())
                {
                    if (root.name == "Main Camera")
                        camera = root;
                    else if (root.name == "Directional Light")
                        hasLight = true;
                    else if (root.name == "Global Volume")
                        hasVolume = true;
                }

                if (camera == null || !hasLight || !hasVolume)
                    throw new InvalidOperationException("Map001 camera, light, or volume layout changed.");

                UnityEngine.Object.DestroyImmediate(camera);
                if (!EditorSceneManager.SaveScene(map))
                    throw new InvalidOperationException("Could not save the integrated Map001 scene.");
            }
            finally
            {
                EditorSceneManager.CloseScene(map, true);
            }

            var settings = AddressableAssetSettingsDefaultObject.Settings;
            var group = settings == null ? null : settings.FindGroup("RemoteGameContent");
            if (group == null)
                throw new InvalidOperationException("RemoteGameContent Addressables group is missing.");

            var entry = settings.CreateOrMoveEntry(
                AssetDatabase.AssetPathToGUID(GameplayPath), group);
            entry.address = RemoteGameContent.Map001SceneAddress;
            entry.SetLabel(RemoteGameContent.RequiredContentLabel, true);
            AssetDatabase.SaveAssets();

            Debug.Log("[Map001] Gameplay copy and remote scene entry are ready.");
        }
        catch (Exception error)
        {
            Debug.LogError("[Map001] Integration failed; inspect the copy before retrying. " + error);
        }
    }

    [MenuItem("Tools/World/Validate Map001 And Shared UI")]
    public static void Validate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        Scene shell = default;
        Scene map = default;
        try
        {
            shell = EditorSceneManager.OpenScene(ShellPath, OpenSceneMode.Additive);
            map = EditorSceneManager.OpenScene(GameplayPath, OpenSceneMode.Additive);
            int canvasCount = 0;
            int eventSystemCount = 0;
            int cameraCount = 0;
            foreach (GameObject root in shell.GetRootGameObjects())
            {
                canvasCount += root.GetComponentsInChildren<Canvas>(true).Length;
                eventSystemCount += root.GetComponentsInChildren<EventSystem>(true).Length;
                cameraCount += root.GetComponentsInChildren<Camera>(true).Length;
            }

            int mapRenderers = 0;
            foreach (GameObject root in map.GetRootGameObjects())
            {
                mapRenderers += root.GetComponentsInChildren<Renderer>(true).Length;
                cameraCount += root.GetComponentsInChildren<Camera>(true).Length;
                eventSystemCount += root.GetComponentsInChildren<EventSystem>(true).Length;
            }

            if (canvasCount == 0 || eventSystemCount != 1 || cameraCount != 1
                || mapRenderers == 0)
                throw new InvalidOperationException("Map, camera, or shared UI is incomplete.");

            GameSession session = null;
            foreach (GameObject root in shell.GetRootGameObjects())
            {
                session = root.GetComponentInChildren<GameSession>(true);
                if (session != null)
                    break;
            }

            EditorSceneManager.CloseScene(map, true);
            map = default;
            if (!shell.isLoaded || session == null || !session.gameObject.scene.Equals(shell))
                throw new InvalidOperationException("Shared UI was lost after Map001 unloaded.");

            Debug.Log("[Map001] Shared UI survives Map001 unload; only the gameplay camera remains.");
        }
        catch (Exception error)
        {
            Debug.LogError("[Map001] Validation failed: " + error);
        }
        finally
        {
            if (map.IsValid() && map.isLoaded)
                EditorSceneManager.CloseScene(map, true);
            if (shell.IsValid() && shell.isLoaded)
                EditorSceneManager.CloseScene(shell, true);
        }
    }
}
