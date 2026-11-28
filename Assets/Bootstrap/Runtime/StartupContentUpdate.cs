using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using QiDao.Bootstrap.Contracts;
using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.EventSystems;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.UI;
using QiDao.Bootstrap.CodeUpdate;

public sealed class StartupContentUpdate : MonoBehaviour
{
    private TMP_Text statusText;
    private TMP_Text retryText;
    private RectTransform progressFill;
    private GameObject retryButton;
    private bool failed;
    private bool retryRequested;
    private IGameStartupContent gameContent;
    private CodeUpdateRuntime codeUpdate;
    private PinnedResourceCatalog pinnedCatalog;
    public bool IsReady { get; private set; }

    private void OnDestroy()
    {
        gameContent?.Dispose();
    }

    public IEnumerator Run()
    {
        IsReady = false;
        CreateView();

        while (true)
        {
            failed = false;
            retryRequested = false;
            retryButton.SetActive(false);
            SetProgress(0f);

            yield return UpdateGameContent();
            if (codeUpdate != null && codeUpdate.ExecutionHandedOff) { Application.Quit(); yield break; }
            if (!failed)
            {
                try { codeUpdate.MarkHealthy(); codeUpdate.MarkResourcesReady(); IsReady = true; }
                catch (System.Exception error)
                {
                    Debug.LogError("[CodeUpdate] Code startup confirmation failed: " + error);
                    Fail(StartupUiText.GameUpdateFailed);
                }
            }
            if (!failed)
            {
                SetStatus(StartupUiText.Ready);
                SetProgress(1f);
                yield break;
            }

            retryButton.SetActive(true);
            while (!retryRequested)
                yield return null;
            if (codeUpdate != null && codeUpdate.RequiresRestart)
            {
                try { codeUpdate.RestartForRecovery(); }
                catch (System.Exception error) { Debug.LogError("[CodeUpdate] Restart failed: " + error); }
                yield break;
            }
        }
    }

    private IEnumerator UpdateGameContent()
    {
        if (gameContent == null)
        {
            try { if (codeUpdate == null) codeUpdate = CodeUpdateRuntime.CreateDefault(); }
            catch (System.Exception error)
            {
                Debug.LogError("[StartupContentUpdate] " + error);
                Fail(StartupUiText.GameUpdateFailed);
            }
            if (failed) yield break;
            yield return codeUpdate.Run(SetStatus, SetProgress);
            if (codeUpdate.ExecutionHandedOff) yield break;
            if (!codeUpdate.Succeeded)
            {
                Debug.LogError("[CodeUpdate] " + codeUpdate.Error);
                Fail(StartupUiText.GameUpdateFailed);
                yield break;
            }
            try { gameContent = GameStartupServices.Create(); }
            catch (System.Exception error)
            {
                Debug.LogError("[CodeUpdate] Game startup services failed: " + error);
                Fail(StartupUiText.GameUpdateFailed);
            }
            if (failed) yield break;
        }
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
        StartupCatalogSynchronization catalogSync = null;
        if (CodeUpdateProtocol.HasResourceBinding(codeUpdate.SelectedRelease))
        {
            try
            {
                if (pinnedCatalog == null) pinnedCatalog = new PinnedResourceCatalog(codeUpdate.SelectedRelease,
                    codeUpdate.PublicationUri, codeUpdate.CacheRoot);
            }
            catch (System.Exception error)
            {
                Debug.LogError("[StartupContentUpdate] " + error);
                Fail(StartupUiText.CatalogCheckFailed);
            }
            if (failed) yield break;
            yield return pinnedCatalog.Run();
            if (!pinnedCatalog.Succeeded)
            {
                Debug.LogError("[StartupContentUpdate] " + pinnedCatalog.Error);
                if (pinnedCatalog.IntegrityFailure) codeUpdate.RequireResourceRecovery(pinnedCatalog.Error);
                Fail(StartupUiText.CatalogCheckFailed);
                yield break;
            }
            Debug.Log("[StartupContentUpdate] Pinned resources=" + codeUpdate.SelectedRelease.resourceReleaseId
                + " catalog=" + codeUpdate.SelectedRelease.resourceCatalogHash);
        }
        else
        {
            if (!Application.isEditor)
            {
                Fail(StartupUiText.CatalogCheckFailed);
                yield break;
            }
            catalogSync = new StartupCatalogSynchronization(Application.isEditor);
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
        }

        StartupResourceDownloadPlan plan = null;
        try
        {
            plan = StartupResourceDownloadPlan.Create(Addressables.ResourceLocators,
                gameContent.RequiredResourceAddresses);
            if (!Application.isEditor && plan.RemoteBundles.Count == 0)
                throw new System.InvalidOperationException("The Player catalog contains no remote resource bundles.");
        }
        catch (System.Exception error)
        {
            Debug.LogError("[StartupContentUpdate] Full download plan failed: " + error);
            Fail(StartupUiText.RequiredContentMissing);
        }
        if (failed) yield break;

        StartupResourceCacheLease resourceLease = null;
        try { resourceLease = new StartupResourceCacheLease(); }
        catch (System.Exception error)
        {
            Debug.LogError("[StartupContentUpdate] " + error);
            Fail(StartupUiText.CacheCheckFailed);
        }
        if (failed) yield break;
        using (resourceLease)
        {
        SetStatus(StartupUiText.CheckingCache);
        yield return resourceLease.Acquire();
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
        }

        yield return gameContent.PrepareContent(SetStatus);
        if (!gameContent.Succeeded)
        {
            Fail(gameContent.FailureMessage ?? StartupUiText.RequiredContentMissing);
            yield break;
        }

        SetStatus(StartupUiText.CheckingCatalog);
        if (pinnedCatalog != null)
        {
            yield return pinnedCatalog.ConfirmBeforeLogin();
            if (!pinnedCatalog.Succeeded)
            {
                Debug.LogError("[StartupContentUpdate] " + pinnedCatalog.Error);
                if (pinnedCatalog.IntegrityFailure) codeUpdate.RequireResourceRecovery(pinnedCatalog.Error);
                Fail(StartupUiText.CatalogChanged);
                yield break;
            }
        }
        else
        {
            yield return catalogSync.ConfirmBeforeLogin();
            if (!catalogSync.Succeeded)
            {
                Debug.LogWarning("[StartupContentUpdate] " + catalogSync.Error);
                Fail(catalogSync.FailureStage == "changed" ? StartupUiText.CatalogChanged : StartupUiText.CatalogCheckFailed);
                yield break;
            }
        }
        Debug.Log("[StartupContentUpdate] Startup resources match the confirmed server catalogs; login may open.");
    }

    private void ReleaseAttemptContent()
    {
        gameContent?.ReleaseAttemptContent();
    }

    public IEnumerator OpenLoginScene()
    {
        if (gameContent == null)
            throw new System.InvalidOperationException("Startup game content is unavailable.");
        return gameContent.OpenLoginScene();
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
