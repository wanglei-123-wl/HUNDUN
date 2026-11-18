using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public static class WorldSceneSplitSetup
{
    private const string ShellPath = "Assets/Scenes/WorldShell.unity";
    private const string MapPath = "Assets/Scenes/Demo 1 Map.unity";

    [MenuItem("Tools/World/Validate Shared UI Lifetime")]
    public static void ValidateLifetime()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        Scene shell = default;
        Scene map = default;
        try
        {
            shell = EditorSceneManager.OpenScene(ShellPath, OpenSceneMode.Additive);
            map = EditorSceneManager.OpenScene(MapPath, OpenSceneMode.Additive);

            GameSession session = FindSession(shell);
            int canvasCount = 0;
            int eventSystemCount = 0;
            foreach (GameObject root in shell.GetRootGameObjects())
            {
                canvasCount += root.GetComponentsInChildren<Canvas>(true).Length;
                eventSystemCount += root.GetComponentsInChildren<EventSystem>(true).Length;
            }

            if (canvasCount == 0 || eventSystemCount != 1)
                throw new InvalidOperationException("Shared UI or EventSystem is missing.");

            EditorSceneManager.CloseScene(map, true);
            map = default;

            if (!shell.isLoaded || session == null || !session.gameObject.scene.Equals(shell))
                throw new InvalidOperationException("Shared UI scene was lost with the map.");

            Debug.Log("[WorldSplit] Lifetime validation passed: the UI and GameSession survive map unload.");
        }
        catch (Exception error)
        {
            Debug.LogError("[WorldSplit] Lifetime validation failed: " + error);
        }
        finally
        {
            if (map.IsValid() && map.isLoaded)
                EditorSceneManager.CloseScene(map, true);
            if (shell.IsValid() && shell.isLoaded)
                EditorSceneManager.CloseScene(shell, true);
        }
    }

    private static GameSession FindSession(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            GameSession session = root.GetComponentInChildren<GameSession>(true);
            if (session != null)
                return session;
        }

        throw new InvalidOperationException("GameSession is missing from the shared UI scene.");
    }
}
