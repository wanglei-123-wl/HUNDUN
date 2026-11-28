using System;
using System.Collections;
using System.Collections.Generic;
using Game.Items;
using QiDao.Bootstrap.Contracts;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;

namespace QiDao.GameLogic
{
    public sealed class GameStartupContent : IGameStartupContent
    {
        private AsyncOperationHandle<ItemDefinitionTable> itemTableHandle;
        public bool Succeeded { get; private set; }
        public string FailureMessage { get; private set; }
        public IEnumerable<string> RequiredResourceAddresses => GetRequiredResourceAddresses();

        public static IEnumerable<string> GetRequiredResourceAddresses()
        {
            yield return RemoteGameContent.LoginSceneAddress;
            yield return RemoteGameContent.WorldSceneAddress;
            yield return RemoteGameContent.ItemTableAddress;
            yield return RemoteGameContent.BootstrapFontAddress;
            foreach (string address in WorldMapSceneCatalog.GetAllResourceAddresses())
                yield return address;
        }

        public IEnumerator PrepareContent(Action<string> reportStatus)
        {
            Succeeded = false;
            FailureMessage = null;
            reportStatus?.Invoke(StartupUiText.CalculatingFont);
            yield return GameFontService.Load();
            if (!GameFontService.IsReady)
            {
                FailureMessage = StartupUiText.FontLoadFailed;
                yield break;
            }
            reportStatus?.Invoke(StartupUiText.CalculatingTable);
            itemTableHandle = Addressables.LoadAssetAsync<ItemDefinitionTable>(RemoteGameContent.ItemTableAddress);
            yield return itemTableHandle;
            if (itemTableHandle.Status != AsyncOperationStatus.Succeeded || itemTableHandle.Result == null)
            {
                FailureMessage = StartupUiText.ItemTableLoadFailed;
                yield break;
            }
            ItemDefinitionTable requiredItemIcons = itemTableHandle.Result;
            List<string> addresses = requiredItemIcons.GetRequiredRemoteIconAddresses();
            if (addresses.Count == 0)
            {
                FailureMessage = StartupUiText.RequiredIconsMissing;
                yield break;
            }
            reportStatus?.Invoke(StartupUiText.LoadingIcons);
            yield return RemoteItemIconCache.Load(addresses);
            if (!requiredItemIcons.AreRequiredRemoteIconsLoaded(out string missingItemId))
            {
                FailureMessage = StartupUiText.RequiredIconLoadFailed;
                Debug.LogWarning("[StartupContentUpdate] Missing item icon: " + missingItemId);
                yield break;
            }
            Succeeded = true;
        }

        public void ReleaseAttemptContent()
        {
            RemoteItemIconCache.Clear();
            GameFontService.Release();
            Dispose();
        }

        public void Dispose()
        {
            // The game font and icon session must survive the startup scene.
            if (itemTableHandle.IsValid()) Addressables.Release(itemTableHandle);
            itemTableHandle = default;
        }

        public IEnumerator OpenLoginScene()
        {
            GameRuntimeInitializers.BeforeLoginSceneLoad();
            AsyncOperationHandle<SceneInstance> login;
            try { login = RemoteGameContent.LoadSceneAsync(RemoteGameContent.LoginSceneName); }
            catch (Exception error)
            {
                Debug.LogError("Startup scene cannot open login scene: " + error);
                yield break;
            }
            yield return login;
            if (login.Status != AsyncOperationStatus.Succeeded)
            {
                Debug.LogError("Startup scene failed to load login scene: " + login.OperationException);
                Addressables.Release(login);
            }
        }
    }
}
