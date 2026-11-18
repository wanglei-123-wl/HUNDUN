using TMPro;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.EventSystems;
using UnityEngine.Events;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.UI;

public sealed class WorldEntryLoadingView : MonoBehaviour
{
    public const float WorldSceneLoadedProgress = 0.65f;
    public const float TargetMapLoadedProgress = 0.8f;
    private const float ProgressSmoothTime = 0.18f;

    [SerializeField] private Image progressFill;
    [SerializeField] private TMP_Text progressText;

    private AsyncOperationHandle<SceneInstance> sourceSceneAssets;
    private GameObject failurePanel;
    private TMP_Text failureText;
    private Button retryButton;
    private Button backButton;
    private TMP_Text backButtonText;
    private float targetProgress;
    private float progressVelocity;
    private string progressDescription;
    private int displayedPercentage = -1;

    public static WorldEntryLoadingView ActiveWorldEntry { get; private set; }
    internal float DisplayedProgress { get; private set; }

    public void Show(string description)
    {
        transform.SetAsLastSibling();
        gameObject.SetActive(true);
        Report(0f, description, immediate: true);
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    public void Report(float progress, string description, bool immediate = false)
    {
        if (failurePanel != null && failurePanel.activeSelf)
        {
            failurePanel.SetActive(false);
            retryButton.onClick.RemoveAllListeners();
            backButton.onClick.RemoveAllListeners();
        }
        targetProgress = Mathf.Clamp01(progress);
        if (immediate)
        {
            DisplayedProgress = targetProgress;
            progressVelocity = 0f;
        }

        if (progressFill != null)
            progressFill.enabled = true;
        if (progressText != null)
            progressText.enabled = true;

        bool descriptionChanged = progressDescription != description;
        progressDescription = description;
        RenderProgress(descriptionChanged);
    }

    private void Update()
    {
        if (DisplayedProgress == targetProgress || Time.unscaledDeltaTime <= 0f
            || (failurePanel != null && failurePanel.activeSelf))
            return;

        DisplayedProgress = Mathf.SmoothDamp(DisplayedProgress, targetProgress, ref progressVelocity,
            ProgressSmoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
        if (Mathf.Abs(DisplayedProgress - targetProgress) < 0.0005f)
        {
            DisplayedProgress = targetProgress;
            progressVelocity = 0f;
        }
        RenderProgress();
    }

    private void RenderProgress(bool descriptionChanged = false)
    {
        if (progressFill != null)
            progressFill.fillAmount = DisplayedProgress;
        if (progressText == null)
            return;

        int percentage = DisplayedProgress >= 1f && targetProgress >= 1f
            ? 100
            : Mathf.Min(99, Mathf.RoundToInt(DisplayedProgress * 100f));
        if (!descriptionChanged && percentage == displayedPercentage)
            return;

        displayedPercentage = percentage;
        progressText.text = $"{progressDescription}  {percentage}%";
    }

    internal void ShowFailure(string message, UnityAction retry, UnityAction goBack = null,
        string backLabel = "返回选角")
    {
        transform.SetAsLastSibling();
        gameObject.SetActive(true);
        if (failurePanel == null)
            CreateFailurePanel();
        if (progressFill != null)
            progressFill.enabled = false;
        if (progressText != null)
            progressText.enabled = false;

        failureText.text = message;
        backButtonText.text = backLabel;
        ((RectTransform)retryButton.transform).anchoredPosition = new Vector2(goBack == null ? 0f : -90f, -48f);
        BindFailureButton(retryButton, retry);
        BindFailureButton(backButton, goBack);
        failurePanel.SetActive(true);
        if (EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(null);
    }

    internal void ReturnToLoginScene(string sceneName)
    {
        Report(0f, "正在返回登录界面", immediate: true);
        RemoteGameContent.LoadSceneOrLog(sceneName, () =>
        {
            if (this != null)
                ShowFailure("登录界面资源加载失败，请重试。", () => ReturnToLoginScene(sceneName));
        });
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    public void PersistForWorldEntry()
    {
        sourceSceneAssets = RemoteGameContent.RetainSceneAssets(gameObject.scene);
        Canvas sourceCanvas = GetComponentInParent<Canvas>();
        CanvasScaler sourceScaler = GetComponentInParent<CanvasScaler>();
        transform.SetParent(null, false);

        Canvas canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue;
        if (sourceCanvas != null)
            canvas.additionalShaderChannels = sourceCanvas.additionalShaderChannels;
        gameObject.AddComponent<GraphicRaycaster>();
        if (sourceScaler != null)
        {
            CanvasScaler scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = sourceScaler.uiScaleMode;
            scaler.scaleFactor = sourceScaler.scaleFactor;
            scaler.referencePixelsPerUnit = sourceScaler.referencePixelsPerUnit;
            scaler.referenceResolution = sourceScaler.referenceResolution;
            scaler.screenMatchMode = sourceScaler.screenMatchMode;
            scaler.matchWidthOrHeight = sourceScaler.matchWidthOrHeight;
        }

        DontDestroyOnLoad(gameObject);
        ActiveWorldEntry = this;
    }

    public static void CloseWorldEntry()
    {
        WorldEntryLoadingView view = ActiveWorldEntry;
        ActiveWorldEntry = null;
        if (view == null)
            return;

        view.Hide();
        Destroy(view.gameObject);
    }

    private void CreateFailurePanel()
    {
        RectTransform panel = CreateFailureRect("进场失败", transform, Vector2.zero, new Vector2(620f, 180f));
        failurePanel = panel.gameObject;
        failurePanel.AddComponent<Image>().color = new Color(0.08f, 0.11f, 0.14f, 1f);
        failureText = CreateFailureLabel("原因", panel, new Vector2(0f, 34f), new Vector2(556f, 76f));
        failureText.enableAutoSizing = true;
        failureText.fontSizeMin = 16f;
        failureText.fontSizeMax = 24f;
        retryButton = CreateFailureButton("重试", panel, -90f, out _);
        backButton = CreateFailureButton("返回选角", panel, 90f, out backButtonText);
    }

    private Button CreateFailureButton(string label, Transform parent, float x, out TMP_Text text)
    {
        RectTransform rect = CreateFailureRect(label, parent, new Vector2(x, -48f), new Vector2(152f, 44f));
        Image background = rect.gameObject.AddComponent<Image>();
        background.color = new Color(0.23f, 0.33f, 0.37f, 1f);
        text = CreateFailureLabel("文字", rect, Vector2.zero, rect.sizeDelta);
        text.text = label;
        Button button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        return button;
    }

    private TMP_Text CreateFailureLabel(string label, Transform parent, Vector2 position, Vector2 size)
    {
        RectTransform rect = CreateFailureRect(label, parent, position, size);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        if (progressText != null)
            text.font = progressText.font;
        text.fontSize = 22f;
        text.color = Color.white;
        text.alignment = TextAlignmentOptions.Center;
        text.richText = false;
        text.raycastTarget = false;
        return text;
    }

    private static RectTransform CreateFailureRect(string label, Transform parent, Vector2 position, Vector2 size)
    {
        var rect = new GameObject(label, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.gameObject.layer = parent.gameObject.layer;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        return rect;
    }

    private void BindFailureButton(Button button, UnityAction action)
    {
        button.onClick.RemoveAllListeners();
        button.gameObject.SetActive(action != null);
        button.interactable = true;
        if (action == null)
            return;

        button.onClick.AddListener(() =>
        {
            retryButton.interactable = false;
            backButton.interactable = false;
            action();
        });
    }

    private void OnDestroy()
    {
        if (ActiveWorldEntry == this)
            ActiveWorldEntry = null;
        if (sourceSceneAssets.IsValid())
            Addressables.Release(sourceSceneAssets);
    }
}
