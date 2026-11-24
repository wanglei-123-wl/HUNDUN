using Game.Minimap;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class MapTransferStatusView : MonoBehaviour
{
    [SerializeField] private TMP_Text label;
    [SerializeField, Min(3.5f)] private float minimumVisibleSeconds = 3.5f;
    private string destinationName;
    private float shownAt;
    private float dismissAt = float.PositiveInfinity;
    private bool started;
    private bool runtimeCreated;

    public bool IsVisible => gameObject.activeInHierarchy;
    public bool MinimumDisplayElapsed => started
        && Time.realtimeSinceStartup - shownAt >= Mathf.Max(3.5f, minimumVisibleSeconds);

    public static MapTransferStatusView Create(Transform owner)
    {
        foreach (var existing in FindObjectsOfType<MapTransferStatusView>(true))
        {
            if (existing.gameObject.scene == owner.gameObject.scene)
                return existing;
        }

        var root = new GameObject("地图传送状态", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        root.transform.SetParent(owner, false);
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 30000;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        var box = new GameObject("状态", typeof(RectTransform), typeof(Image));
        box.transform.SetParent(root.transform, false);
        var rect = box.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 110f);
        rect.sizeDelta = new Vector2(650f, 58f);
        var background = box.GetComponent<Image>();
        background.color = new Color(0.04f, 0.05f, 0.06f, 0.85f);
        background.raycastTarget = false;
        var text = new GameObject("文字", typeof(RectTransform), typeof(TextMeshProUGUI));
        text.transform.SetParent(box.transform, false);
        var textRect = text.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(12f, 4f);
        textRect.offsetMax = new Vector2(-12f, -4f);
        var view = root.AddComponent<MapTransferStatusView>();
        view.runtimeCreated = true;
        view.label = text.GetComponent<TMP_Text>();
        view.label.alignment = TextAlignmentOptions.Center;
        view.label.fontSize = 24f;
        view.label.color = Color.white;
        view.label.raycastTarget = false;
        return view;
    }

    public void BeginDestination(string sceneId, string mapId)
    {
        var minimap = FindObjectOfType<MinimapPresenter>(true);
        destinationName = minimap?.GetMapDisplayName(sceneId, mapId);
        if (string.IsNullOrWhiteSpace(destinationName)) destinationName = "目标地图";
        started = false;
        dismissAt = float.PositiveInfinity;
    }

    public void Show(string message)
    {
        if (!started)
        {
            shownAt = Time.realtimeSinceStartup;
            started = true;
        }
        dismissAt = float.PositiveInfinity;
        label.richText = false;
        label.text = "正在前往" + (destinationName ?? "目标地图") + "……\n" + message;
        gameObject.SetActive(true);
        GameplayUiInputGuard.RegisterModal(gameObject);
    }

    public void ShowFailure(string message, bool recovering)
    {
        Show(message);
        // 可恢复的失败留出阅读时间；重连时一直遮挡，直到会话重新就绪。
        dismissAt = recovering ? float.PositiveInfinity
            : Time.realtimeSinceStartup + Mathf.Max(3.5f, minimumVisibleSeconds);
    }

    private void Update()
    {
        if (Time.realtimeSinceStartup >= dismissAt && MinimumDisplayElapsed) Hide();
    }

    public void Hide()
    {
        GameplayUiInputGuard.UnregisterModal(gameObject);
        gameObject.SetActive(false);
        started = false;
    }

    private void OnDisable() => GameplayUiInputGuard.UnregisterModal(gameObject);

    public void Release()
    {
        Hide();
        if (runtimeCreated) Destroy(gameObject);
    }
}
