using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Game.Items;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.EventSystems;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UI;

public sealed class StartupContentUpdate : MonoBehaviour
{
    private TMP_Text statusText;
    private TMP_Text retryText;
    private RectTransform progressFill;
    private GameObject retryButton;
    private bool failed;
    private bool retryRequested;
    private AsyncOperationHandle<TMP_FontAsset> fontHandle;
    private AsyncOperationHandle<ItemDefinitionTable> itemTableHandle;

    private void OnDestroy()
    {
        if (itemTableHandle.IsValid())
            Addressables.Release(itemTableHandle);
        if (fontHandle.IsValid())
            Addressables.Release(fontHandle);
    }

    public IEnumerator Run()
    {
        RemoteItemIconCache.Clear();
        CreateView();

        while (true)
        {
            failed = false;
            retryRequested = false;
            retryButton.SetActive(false);
            SetProgress(0f);

            yield return UpdateTrialContent();
            if (!failed)
            {
                SetStatus("资源已就绪");
                SetProgress(1f);
                yield break;
            }

            retryButton.SetActive(true);
            while (!retryRequested)
                yield return null;
        }
    }

    private IEnumerator UpdateTrialContent()
    {
        RemoteItemIconCache.Clear();
        SetStatus("Checking resources");
        AsyncOperationHandle<UnityEngine.AddressableAssets.ResourceLocators.IResourceLocator> initialize =
            Addressables.InitializeAsync(false);
        yield return initialize;
        bool initialized = initialize.Status == AsyncOperationStatus.Succeeded;
        Addressables.Release(initialize);
        if (!initialized)
        {
            Fail("Resource catalog initialization failed");
            yield break;
        }

        SetStatus("Checking catalog");
        var check = Addressables.CheckForCatalogUpdates(false);
        yield return check;
        bool checkedCatalog = check.Status == AsyncOperationStatus.Succeeded;
        string[] catalogs = checkedCatalog ? check.Result.ToArray() : null;
        Addressables.Release(check);
        if (!checkedCatalog)
        {
            Fail("检查远程目录失败");
            yield break;
        }

        if (catalogs.Length > 0)
        {
            SetStatus("Updating catalog");
            var update = Addressables.UpdateCatalogs(catalogs, false);
            yield return update;
            bool updated = update.Status == AsyncOperationStatus.Succeeded;
            Addressables.Release(update);
            if (!updated)
            {
                Fail("更新资源目录失败");
                yield break;
            }
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        GrassRenderDiagnostics.LogCatalog(catalogs);
#endif
        if (!fontHandle.IsValid())
            fontHandle = Addressables.LoadAssetAsync<TMP_FontAsset>(
                RemoteGameContent.BootstrapFontAddress);
        yield return fontHandle;
        if (fontHandle.Status != AsyncOperationStatus.Succeeded || fontHandle.Result == null)
        {
            Addressables.Release(fontHandle);
            fontHandle = default;
            Fail("Bootstrap font unavailable");
            yield break;
        }
        statusText.font = fontHandle.Result;
        retryText.font = fontHandle.Result;

        string[] requiredAddresses =
        {
            RemoteGameContent.LoginSceneAddress,
            RemoteGameContent.WorldSceneAddress,
            RemoteGameContent.DemoMapSceneAddress,
            RemoteGameContent.Map001SceneAddress,
            RemoteGameContent.ItemTableAddress
        };
        var gameLocations = Addressables.LoadResourceLocationsAsync(
            RemoteGameContent.RequiredContentLabel);
        yield return gameLocations;
        bool hasGameContent = gameLocations.Status == AsyncOperationStatus.Succeeded
            && gameLocations.Result != null
            && gameLocations.Result.Count == requiredAddresses.Length;
        Addressables.Release(gameLocations);
        if (!hasGameContent)
        {
            Fail("登录、公共游戏场景、两张地图或物品总表未登记到远程目录");
            yield break;
        }

        foreach (string address in requiredAddresses)
        {
            var locations = Addressables.LoadResourceLocationsAsync(address);
            yield return locations;
            bool available = locations.Status == AsyncOperationStatus.Succeeded
                && locations.Result != null && locations.Result.Count > 0;
            Addressables.Release(locations);
            if (!available)
            {
                Fail("必需的远程资源不存在: " + address);
                yield break;
            }
        }

        SetStatus("计算物品目录下载量");
        var tableSize = Addressables.GetDownloadSizeAsync(RemoteGameContent.ItemTableAddress);
        yield return tableSize;
        bool tableSized = tableSize.Status == AsyncOperationStatus.Succeeded;
        long tableBytes = tableSized ? tableSize.Result : 0L;
        Addressables.Release(tableSize);
        if (!tableSized)
        {
            Fail("物品目录不存在");
            yield break;
        }

        if (tableBytes > 0L)
        {
            var tableDownload = Addressables.DownloadDependenciesAsync(
                RemoteGameContent.ItemTableAddress, false);
            while (!tableDownload.IsDone)
            {
                SetStatus("下载物品目录 " + FormatBytes(tableDownload.GetDownloadStatus().DownloadedBytes)
                    + " / " + FormatBytes(tableBytes));
                yield return null;
            }
            bool tableDownloaded = tableDownload.Status == AsyncOperationStatus.Succeeded;
            Addressables.Release(tableDownload);
            if (!tableDownloaded)
            {
                Fail("物品目录下载失败");
                yield break;
            }
        }

        if (itemTableHandle.IsValid())
            Addressables.Release(itemTableHandle);
        itemTableHandle = Addressables.LoadAssetAsync<ItemDefinitionTable>(
            RemoteGameContent.ItemTableAddress);
        yield return itemTableHandle;
        if (itemTableHandle.Status != AsyncOperationStatus.Succeeded
            || itemTableHandle.Result == null)
        {
            Fail("物品总表加载失败");
            yield break;
        }

        ItemDefinitionTable requiredItemIcons = itemTableHandle.Result;
        List<string> addresses = requiredItemIcons.GetRequiredRemoteIconAddresses();
        if (addresses.Count == 0)
        {
            Fail("必需的远程图标地址未配置");
            yield break;
        }

        var keys = new List<object> { RemoteGameContent.RequiredContentLabel };
        keys.AddRange(addresses);
        IEnumerable downloadKeys = keys;
        SetStatus("计算资源下载量");
        var size = Addressables.GetDownloadSizeAsync(downloadKeys);
        yield return size;
        bool sized = size.Status == AsyncOperationStatus.Succeeded;
        long bytes = sized ? size.Result : 0L;
        Addressables.Release(size);
        if (!sized)
        {
            Fail("计算图标资源下载量失败");
            yield break;
        }

        if (bytes > 0L)
        {
            var download = Addressables.DownloadDependenciesAsync(
                downloadKeys, Addressables.MergeMode.Union, false);
            while (!download.IsDone)
            {
                long downloaded = download.GetDownloadStatus().DownloadedBytes;
                SetStatus("下载资源 " + FormatBytes(downloaded) + " / " + FormatBytes(bytes));
                SetProgress((float)((double)downloaded / bytes));
                yield return null;
            }

            bool downloadedSuccessfully = download.Status == AsyncOperationStatus.Succeeded;
            Addressables.Release(download);
            if (!downloadedSuccessfully)
            {
                Fail("资源下载失败");
                yield break;
            }
        }

        SetStatus("加载图标");
        yield return RemoteItemIconCache.Load(addresses);
        if (!requiredItemIcons.AreRequiredRemoteIconsLoaded(out string missingItemId))
            Fail("必需的远程图标加载失败: " + missingItemId);
    }

    private void Fail(string message)
    {
        failed = true;
        SetStatus(message + "，请重试");
        Debug.LogWarning("[StartupContentUpdate] " + message);
    }

    private void SetStatus(string message)
    {
        statusText.text = message;
    }

    private void SetProgress(float progress)
    {
        progressFill.anchorMax = new Vector2(Mathf.Clamp01(progress), 1f);
    }

    private static string FormatBytes(long bytes)
    {
        double size = bytes;
        string[] units = { "B", "KB", "MB", "GB" };
        int unit = 0;
        while (size >= 1024d && unit < units.Length - 1)
        {
            size /= 1024d;
            unit++;
        }
        return size.ToString(unit == 0 ? "0" : "0.0", CultureInfo.InvariantCulture)
            + " " + units[unit];
    }

    private void CreateView()
    {
        var canvasObject = new GameObject("资源更新显示", typeof(RectTransform),
            typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform panel = CreateRect("状态栏", canvasObject.transform,
            new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f),
            new Vector2(0f, 158f));
        panel.pivot = new Vector2(0.5f, 0f);
        panel.gameObject.AddComponent<Image>().color = new Color(0.06f, 0.08f, 0.09f, 0.88f);

        RectTransform status = CreateRect("更新状态", panel,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -42f),
            new Vector2(980f, 50f));
        statusText = status.gameObject.AddComponent<TextMeshProUGUI>();
        statusText.font = TMP_Settings.defaultFontAsset;
        statusText.fontSize = 26f;
        statusText.color = Color.white;
        statusText.alignment = TextAlignmentOptions.Center;
        statusText.raycastTarget = false;

        RectTransform track = CreateRect("下载进度背景", panel,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -82f),
            new Vector2(820f, 12f));
        track.gameObject.AddComponent<Image>().color = new Color(0.28f, 0.31f, 0.31f, 1f);
        progressFill = CreateRect("下载进度", track,
            new Vector2(0f, 0f), new Vector2(0f, 1f), Vector2.zero, Vector2.zero);
        progressFill.gameObject.AddComponent<Image>().color = new Color(0.88f, 0.69f, 0.34f, 1f);

        retryButton = CreateButton("重试", panel, new Vector2(0f, -122f),
            () => retryRequested = true);

        if (FindObjectOfType<EventSystem>() == null)
        {
            new GameObject("资源更新输入", typeof(EventSystem),
                typeof(StandaloneInputModule));
        }
    }

    private GameObject CreateButton(string label, Transform parent, Vector2 position,
        UnityEngine.Events.UnityAction action)
    {
        RectTransform rect = CreateRect(label, parent,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), position,
            new Vector2(156f, 42f));
        rect.gameObject.AddComponent<Image>().color = new Color(0.2f, 0.27f, 0.29f, 1f);
        rect.gameObject.AddComponent<Button>().onClick.AddListener(action);
        RectTransform textRect = CreateRect("文字", rect, Vector2.zero, Vector2.one,
            Vector2.zero, Vector2.zero);
        var text = textRect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = statusText.font;
        text.text = label;
        text.fontSize = 22f;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        retryText = text;
        return rect.gameObject;
    }

    private static RectTransform CreateRect(string name, Transform parent,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 position, Vector2 size)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }
}
