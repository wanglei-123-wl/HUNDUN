using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

public static class RemoteGameContent
{
    public const string LoginSceneName = "SampleScene";
    public const string WorldSceneName = "WorldShell";
    public const string DemoMapSceneName = "Demo 1 Map";
    public const string Map001SceneName = "Map001 Gameplay";
    public const string LoginSceneAddress = "game/scene/login";
    public const string WorldSceneAddress = "game/scene/world";
    public const string DemoMapSceneAddress = WorldMapSceneCatalog.DemoMapAddress;
    public const string Map001SceneAddress = WorldMapSceneCatalog.VillageMapAddress;
    public const string ItemTableAddress = "game/items/table";
    public const string BootstrapFontAddress = "bootstrap/font/chinese";
    public const string RequiredContentLabel = "required-game-content";

    private static readonly Dictionary<Scene, AsyncOperationHandle<SceneInstance>> loadedScenes =
        new Dictionary<Scene, AsyncOperationHandle<SceneInstance>>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        loadedScenes.Clear();
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
    }

    public static AsyncOperationHandle<SceneInstance> LoadSceneAsync(
        string sceneName, bool activateOnLoad = true)
    {
        string address = sceneName == LoginSceneName ? LoginSceneAddress
            : sceneName == WorldSceneName ? WorldSceneAddress
            : throw new ArgumentException("Unknown remote scene: " + sceneName, nameof(sceneName));

        return TrackScene(Addressables.LoadSceneAsync(
            address, LoadSceneMode.Single, activateOnLoad));
    }

    public static AsyncOperationHandle<SceneInstance> LoadMapSceneAsync(string address)
    {
        if (string.IsNullOrWhiteSpace(address))
            throw new ArgumentException("Map address is missing.", nameof(address));

        return TrackScene(Addressables.LoadSceneAsync(address, LoadSceneMode.Additive));
    }

    internal static AsyncOperationHandle<SceneInstance> RetainSceneAssets(Scene scene)
    {
        if (!loadedScenes.TryGetValue(scene, out var handle) || !handle.IsValid())
            return default;

        // Addressables releases the scene's own reference on unload.
        // Keep the extra reference for the loading view until it is destroyed.
        loadedScenes.Remove(scene);
        return Addressables.ResourceManager.Acquire(handle);
    }

    private static AsyncOperationHandle<SceneInstance> TrackScene(
        AsyncOperationHandle<SceneInstance> handle)
    {
        handle.Completed += result =>
        {
            if (result.Status == AsyncOperationStatus.Succeeded
                && result.Result.Scene.IsValid())
                loadedScenes[result.Result.Scene] = result;
        };
        return handle;
    }

    public static void LoadSceneOrLog(string sceneName, Action onFailure = null)
    {
        try
        {
            var handle = LoadSceneAsync(sceneName);
            handle.Completed += result =>
            {
                if (result.Status != AsyncOperationStatus.Succeeded)
                {
                    Debug.LogError("Remote scene failed to load: " + sceneName
                        + " " + result.OperationException);
                    Addressables.Release(result);
                    onFailure?.Invoke();
                }
            };
        }
        catch (Exception error)
        {
            Debug.LogError("Remote scene failed to start: " + sceneName + " " + error);
            onFailure?.Invoke();
        }
    }

    private static void OnSceneUnloaded(Scene scene)
    {
        if (!loadedScenes.TryGetValue(scene, out var handle))
            return;

        loadedScenes.Remove(scene);
        if (handle.IsValid())
            Addressables.Release(handle);
    }
}
