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
    private AsyncOperationHandle<ItemDefinitionTable> itemTableHandle;

    private void OnDestroy()
    {
        if (itemTableHandle.IsValid())
            Addressables.Release(itemTableHandle);
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

            yield return UpdateGameContent();
            if (!failed)
            {
                SetStatus(StartupUiText.Ready);
                SetProgress(1f);
                yield break;
            }

            retryButton.SetActive(true);
            while (!retryRequested)
                yield return null;
        }
    }

    private IEnumerator UpdateGameContent()
    {
        // Release references from a previous attempt before a catalog replacement.
        // These are in-memory handles, not the downloaded files in the disk cache.
        ReleaseAttemptContent();
        SetStatus(StartupUiText.CheckingResources);
        var initialize = Addressables.InitializeAsync(false);
        bool initialized;
        try
        {
            yield return initialize;
            initialized = initialize.Status == AsyncOperationStatus.Succeeded;
        }
        finally { if (initialize.IsValid()) Addressables.Release(initialize); }
        if (!initialized)
        {
            Fail(StartupUiText.InitializationFailed);
            yield break;
        }

        SetStatus(StartupUiText.CheckingCatalog);
        var catalogSync = new StartupCatalogSynchronization(Application.isEditor);
        yield return catalogSync.Run();
        if (!catalogSync.Succeeded)
        {
            Debug.LogError("[StartupContentUpdate] " + catalogSync.Error);
            Fail(catalogSync.FailureStage == "changed" ? StartupUiText.CatalogChanged
                : catalogSync.FailureStage == "update" ? StartupUiText.CatalogUpdateFailed : StartupUiText.CatalogCheckFailed);
            yield break;
        }
        Debug.Log("[StartupContentUpdate] Catalog check complete; remoteCatalogs=" + catalogSync.CatalogCount
            + " editorAssetDatabase=" + catalogSync.SkippedEditorAssetDatabase);

        StartupResourceDownloadPlan plan = null;
        try
        {
            plan = StartupResourceDownloadPlan.Create(Addressables.ResourceLocators);
            if (!Application.isEditor && plan.RemoteBundles.Count == 0)
                throw new System.InvalidOperationException("The Player catalog contains no remote resource bundles.");
        }
        catch (System.Exception error)
        {
            Debug.LogError("[StartupContentUpdate] Full download plan failed: " + error);
            Fail(StartupUiText.RequiredContentMissing);
        }
        if (failed) yield break;

        SetStatus(StartupUiText.CalculatingResources);
        var allResources = new StartupResourceDownloadOperation(plan.DownloadLocations, (downloaded, total) =>
        {
            SetStatus(StartupUiText.DownloadingResources + FormatBytes(downloaded) + " / " + FormatBytes(total));
            SetProgress(total > 0 ? (float)((double)downloaded / total) : 1f);
        });
        Debug.Log("[StartupContentUpdate] Full download plan: remoteBundles=" + plan.RemoteBundles.Count
            + " requiredAddresses=" + plan.RequiredAddressCount);
        yield return allResources.Run();
        if (!allResources.Succeeded)
        {
            Debug.LogError("[StartupContentUpdate] " + allResources.Error);
            Fail(StartupUiText.ResourcesDownloadFailed);
            yield break;
        }

        var cacheVerification = new StartupResourceCacheVerification(plan, (checkedBundles, total) =>
        {
            SetStatus(StartupUiText.CheckingCache + " " + checkedBundles + " / " + total);
            SetProgress(total > 0 ? (float)checkedBundles / total : 1f);
        });
        yield return cacheVerification.Run();
        if (!cacheVerification.Succeeded)
        {
            Debug.LogError("[StartupContentUpdate] " + cacheVerification.Error);
            Fail(StartupUiText.CacheCheckFailed);
            yield break;
        }
        Debug.Log("[StartupContentUpdate] Full resource cache verified; downloadedBytes=" + allResources.PendingBytes);

        SetStatus(StartupUiText.CalculatingFont);
        yield return GameFontService.Load();
        if (!GameFontService.IsReady)
        {
            Fail(StartupUiText.FontLoadFailed);
            yield break;
        }
        SetStatus(StartupUiText.CalculatingTable);
        itemTableHandle = Addressables.LoadAssetAsync<ItemDefinitionTable>(RemoteGameContent.ItemTableAddress);
        yield return itemTableHandle;
        if (itemTableHandle.Status != AsyncOperationStatus.Succeeded || itemTableHandle.Result == null)
        {
            Fail(StartupUiText.ItemTableLoadFailed);
            yield break;
        }
        ItemDefinitionTable requiredItemIcons = itemTableHandle.Result;
        List<string> addresses = requiredItemIcons.GetRequiredRemoteIconAddresses();
        if (addresses.Count == 0)
        {
            Fail(StartupUiText.RequiredIconsMissing);
            yield break;
        }
        SetStatus(StartupUiText.LoadingIcons);
        yield return RemoteItemIconCache.Load(addresses);
        if (!requiredItemIcons.AreRequiredRemoteIconsLoaded(out string missingItemId))
        {
            Fail(StartupUiText.RequiredIconLoadFailed);
            Debug.LogWarning("[StartupContentUpdate] Missing item icon: " + missingItemId);
            yield break;
        }

        SetStatus(StartupUiText.CheckingCatalog);
        yield return catalogSync.ConfirmBeforeLogin();
        if (!catalogSync.Succeeded)
        {
            Debug.LogWarning("[StartupContentUpdate] " + catalogSync.Error);
            Fail(catalogSync.FailureStage == "changed" ? StartupUiText.CatalogChanged : StartupUiText.CatalogCheckFailed);
            yield break;
        }
        Debug.Log("[StartupContentUpdate] Startup resources match the confirmed server catalogs; login may open.");
    }

    private void ReleaseAttemptContent()
    {
        RemoteItemIconCache.Clear();
        GameFontService.Release();
        if (itemTableHandle.IsValid()) Addressables.Release(itemTableHandle);
        itemTableHandle = default;
    }

    private void Fail(string message)
    {
        failed = true;
        SetStatus(message + StartupUiText.RetrySuffix);
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
        canvasObject.transform.SetParent(transform, false);
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

        retryButton = CreateButton(StartupUiText.Retry, panel, new Vector2(0f, -122f),
            () => retryRequested = true);

        if (FindObjectOfType<EventSystem>() == null)
        {
            var input = new GameObject("资源更新输入", typeof(EventSystem),
                typeof(StandaloneInputModule));
            input.transform.SetParent(transform, false);
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
