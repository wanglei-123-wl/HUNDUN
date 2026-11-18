using System;
using System.Text;
#if UNITY_EDITOR
using System.Collections;
#endif
using Game.Items;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.UI.ItemDetail
{
    [DisallowMultipleComponent]
    public sealed class ItemDetailPanelController : MonoBehaviour
    {
        [Header("显示根节点")]
        [SerializeField] private GameObject visualRoot;

        [Header("内容绑定")]
        [SerializeField] private Image itemIcon;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text categoryText;
        [SerializeField] private TMP_Text descriptionText;
        [SerializeField] private TMP_Text detailText;
        [SerializeField] private ScrollRect descriptionScroll;
        [SerializeField] private ScrollRect detailScroll;
        [SerializeField] private RectTransform panelRect;

        [Header("位置")]
        [SerializeField] private Vector2 screenOffset = Vector2.zero;

        private bool initialized;
        private CanvasGroup visualCanvasGroup;
        private readonly Vector3[] panelCorners = new Vector3[4];

        public ItemDetailData CurrentDetail { get; private set; }
        public event Action<ItemDetailPanelController> CloseRequested;
        public bool IsVisible =>
            visualRoot != null && visualRoot.activeInHierarchy
            && visualCanvasGroup != null && visualCanvasGroup.alpha > 0f;
        internal RectTransform PanelRect => panelRect;

        internal void RequestClose()
        {
            CloseRequested?.Invoke(this);
        }

        private void Awake()
        {
            ResolveReferences();
            ConfigurePointerPassthrough();
            initialized = ValidateReferences();
            Hide();
        }

        public void Show(ItemDetailData detail)
        {
            Show(detail, Input.mousePosition);
        }

        public void Show(ItemDetailData detail, Vector2 screenPosition)
        {
            if (!gameObject.activeSelf)
                gameObject.SetActive(true);

            if (!initialized)
            {
                ResolveReferences();
                ConfigurePointerPassthrough();
                initialized = ValidateReferences();
            }

            if (!initialized || detail == null || !detail.IsValid)
            {
                Hide();
                return;
            }

            CurrentDetail = detail;
            SetVisible(false);
            ApplyDetail(detail);
            Canvas.ForceUpdateCanvases();
            FitScrollContent(descriptionScroll, descriptionText);
            FitScrollContent(detailScroll, detailText);
            ResetScroll(descriptionScroll);
            ResetScroll(detailScroll);
            SetScreenPosition(screenPosition);
            SetVisible(true);
#if UNITY_EDITOR
            StartCoroutine(LogDescriptionState(detail));
#endif
        }

        public void Hide()
        {
            CurrentDetail = null;
            SetVisible(false);
            ClearDetail();
        }

        public void SetScreenPosition(Vector2 screenPosition)
        {
            if (visualRoot == null || panelRect == null)
                return;

            Canvas.ForceUpdateCanvases();
            Canvas canvas = panelRect.GetComponentInParent<Canvas>();
            Camera eventCamera = null;
            if (canvas != null
                && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            {
                eventCamera = canvas.worldCamera;
            }

            RectTransform parentRect = panelRect.parent as RectTransform;
            Vector3 targetWorldPosition;
            if (parentRect == null
                || !RectTransformUtility.ScreenPointToWorldPointInRectangle(
                    parentRect,
                    screenPosition + screenOffset,
                    eventCamera,
                    out targetWorldPosition))
            {
                targetWorldPosition = new Vector3(
                    screenPosition.x + screenOffset.x,
                    screenPosition.y + screenOffset.y,
                    panelRect.position.z);
            }

            Vector3[] corners = new Vector3[4];
            panelRect.GetWorldCorners(corners);

            // Position once on entry: the cursor is the panel's lower-left
            // corner, and the panel does not follow the cursor afterward.
            Vector3 delta = targetWorldPosition - corners[0];
            panelRect.position += delta;
            ClampToCanvas();
        }

        internal void SetPanelLocalPosition(Vector2 position)
        {
            if (panelRect == null)
                return;

            Vector3 current = panelRect.localPosition;
            panelRect.localPosition = new Vector3(position.x, position.y, current.z);
            ClampToCanvas();
        }

        private void ClampToCanvas()
        {
            RectTransform parent = panelRect == null
                ? null : panelRect.parent as RectTransform;
            if (parent == null)
                return;

            panelRect.GetWorldCorners(panelCorners);
            float minX = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float minY = float.PositiveInfinity;
            float maxY = float.NegativeInfinity;
            for (int index = 0; index < panelCorners.Length; index++)
            {
                Vector3 corner = parent.InverseTransformPoint(panelCorners[index]);
                minX = Mathf.Min(minX, corner.x);
                maxX = Mathf.Max(maxX, corner.x);
                minY = Mathf.Min(minY, corner.y);
                maxY = Mathf.Max(maxY, corner.y);
            }

            Rect bounds = parent.rect;
            float x = maxX - minX <= bounds.width
                ? Mathf.Clamp(0f, bounds.xMin - minX, bounds.xMax - maxX)
                : bounds.center.x - (minX + maxX) * 0.5f;
            float y = maxY - minY <= bounds.height
                ? Mathf.Clamp(0f, bounds.yMin - minY, bounds.yMax - maxY)
                : bounds.center.y - (minY + maxY) * 0.5f;
            panelRect.localPosition += new Vector3(x, y, 0f);
        }

        private void ApplyDetail(ItemDetailData detail)
        {
            if (itemIcon != null)
            {
                itemIcon.sprite = detail.Icon;
                itemIcon.gameObject.SetActive(detail.Icon != null);
            }

            if (nameText != null)
                nameText.text = detail.DisplayName ?? string.Empty;

            if (categoryText != null)
                categoryText.text = FormatCategoryLabel(
                    detail.CategoryLabel);

            if (descriptionText != null)
                descriptionText.text = detail.Description ?? string.Empty;

            if (detailText != null)
            {
                detailText.text = BuildDetailText(detail);
                detailText.gameObject.SetActive(
                    !string.IsNullOrWhiteSpace(detailText.text));
            }
        }

        private string BuildDetailText(ItemDetailData detail)
        {
            var builder = new StringBuilder();
            if (categoryText == null)
            {
                builder.Append(FormatCategoryLabel(detail.CategoryLabel));
            }

            if (!string.IsNullOrWhiteSpace(detail.DetailText))
            {
                AppendLineIfNeeded(builder);
                builder.Append(detail.DetailText.Trim());
            }

            if (detail.Entries != null)
            {
                for (int index = 0; index < detail.Entries.Count; index++)
                {
                    ItemDetailEntry entry = detail.Entries[index];
                    if (entry == null || !entry.IsValid)
                        continue;

                    AppendLineIfNeeded(builder);
                    if (!string.IsNullOrWhiteSpace(entry.Label))
                        builder.Append(entry.Label.Trim()).Append("：");
                    if (!string.IsNullOrWhiteSpace(entry.Value))
                        builder.Append(entry.Value.Trim());
                }
            }

            return builder.ToString();
        }

        private string FormatCategoryLabel(string categoryLabel)
        {
            string value = categoryLabel == null
                ? string.Empty
                : categoryLabel.Trim();
            return string.IsNullOrEmpty(value)
                ? "种类："
                : "种类：" + value;
        }

        private void ClearDetail()
        {
            if (itemIcon != null)
            {
                itemIcon.sprite = null;
                itemIcon.gameObject.SetActive(false);
            }

            if (nameText != null)
                nameText.text = string.Empty;
            if (categoryText != null)
                categoryText.text = string.Empty;
            if (descriptionText != null)
                descriptionText.text = string.Empty;
            if (detailText != null)
            {
                detailText.text = string.Empty;
                detailText.gameObject.SetActive(false);
            }
        }

        private void SetVisible(bool visible)
        {
            if (visualRoot == null)
                return;

            if (visualCanvasGroup != null)
            {
                visualCanvasGroup.alpha = visible ? 1f : 0f;
                visualCanvasGroup.interactable = visible;
                visualCanvasGroup.blocksRaycasts = visible;
            }
            visualRoot.transform.localScale = Vector3.one;
            if (!visualRoot.activeSelf)
                visualRoot.SetActive(true);
        }

        private void ConfigurePointerPassthrough()
        {
            if (visualRoot == null)
                return;

            visualCanvasGroup = visualRoot.GetComponent<CanvasGroup>();
            if (visualCanvasGroup == null)
                visualCanvasGroup = visualRoot.AddComponent<CanvasGroup>();

            visualCanvasGroup.interactable = false;
            visualCanvasGroup.blocksRaycasts = false;
            EnsureDragHandle();
        }

        private void EnsureDragHandle()
        {
            if (panelRect == null)
                return;

            RectTransform handle = panelRect.Find("拖动区域") as RectTransform;
            if (handle == null)
            {
                var handleObject = new GameObject(
                    "拖动区域", typeof(RectTransform), typeof(Image));
                handle = (RectTransform)handleObject.transform;
                handle.SetParent(panelRect, false);
                handle.anchorMin = new Vector2(0f, 1f);
                handle.anchorMax = Vector2.one;
                handle.pivot = new Vector2(0.5f, 1f);
                handle.sizeDelta = new Vector2(0f, 60f);
                handle.anchoredPosition = Vector2.zero;
            }

            Image image = handle.GetComponent<Image>();
            if (image == null)
                image = handle.gameObject.AddComponent<Image>();
            image.color = Color.clear;
            image.raycastTarget = true;
            image.canvasRenderer.cullTransparentMesh = false;
            handle.SetAsLastSibling();

            ItemDetailPanelDragHandle dragHandle =
                handle.GetComponent<ItemDetailPanelDragHandle>();
            if (dragHandle == null)
                dragHandle = handle.gameObject.AddComponent<ItemDetailPanelDragHandle>();
            dragHandle.Initialize(this);
        }

        private static void FitScrollContent(ScrollRect scroll, TMP_Text text)
        {
            if (scroll == null || scroll.content == null || text == null)
                return;

            RectTransform textRect = text.rectTransform;
            textRect.anchorMin = new Vector2(textRect.anchorMin.x, 1f);
            textRect.anchorMax = new Vector2(textRect.anchorMax.x, 1f);
            textRect.pivot = new Vector2(textRect.pivot.x, 1f);
            textRect.anchoredPosition = new Vector2(
                textRect.anchoredPosition.x, -5f);
            float height = text.gameObject.activeSelf
                ? Mathf.Ceil(text.preferredHeight) : 0f;
            textRect.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Vertical, Mathf.Max(1f, height));

            RectTransform viewport = scroll.viewport != null
                ? scroll.viewport : (RectTransform)scroll.transform;
            float topInset = Mathf.Max(0f, -textRect.anchoredPosition.y);
            scroll.content.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Vertical,
                Mathf.Max(viewport.rect.height, height + 2f * topInset));
        }

        private static void ResetScroll(ScrollRect scroll)
        {
            if (scroll == null)
                return;

            scroll.StopMovement();
            scroll.horizontalNormalizedPosition = 0f;
            scroll.verticalNormalizedPosition = 1f;
        }

#if UNITY_EDITOR
        private IEnumerator LogDescriptionState(ItemDetailData detail)
        {
            yield return null;
            if (CurrentDetail != detail || descriptionText == null)
                yield break;

            RectTransform textRect = descriptionText.rectTransform;
            RectTransform viewport = descriptionScroll != null
                ? descriptionScroll.viewport : null;
            Bounds bounds = viewport != null
                ? RectTransformUtility.CalculateRelativeRectTransformBounds(
                    viewport, textRect)
                : default;
            Debug.Log(
                "[ItemDetail][Presentation] item=" + detail.Identity
                + " sourceLength=" + (detail.Description?.Length ?? 0)
                + " textLength=" + descriptionText.text.Length
                + " active=" + descriptionText.gameObject.activeInHierarchy
                + " culled=" + descriptionText.canvasRenderer.cull
                + " textY=" + bounds.center.y.ToString("F1")
                + " textHeight=" + bounds.size.y.ToString("F1")
                + " viewportHeight=" + (viewport != null
                    ? viewport.rect.height.ToString("F1") : "missing")
                + " contentY=" + (descriptionScroll != null
                    && descriptionScroll.content != null
                    ? descriptionScroll.content.anchoredPosition.y.ToString("F1")
                    : "missing")
                + " alpha=" + (visualCanvasGroup != null
                    ? visualCanvasGroup.alpha.ToString("F1") : "missing"),
                this);
        }
#endif

        private void ResolveReferences()
        {
            if (visualRoot == null)
            {
                Transform canvas = FindChild<Transform>("Canvas");
                visualRoot = canvas == null ? null : canvas.gameObject;
            }

            if (itemIcon == null)
                itemIcon = FindChild<Image>("图标");
            if (nameText == null)
                nameText = FindChild<TMP_Text>("名称");
            if (categoryText == null)
                categoryText = FindChild<TMP_Text>(
                    "种类", "类别", "类型", "物品类型");
            if (descriptionText == null)
                descriptionText = FindChild<TMP_Text>("介绍文本框");
            if (detailText == null)
                detailText = FindChild<TMP_Text>("介绍文本框 (1)");
            if (descriptionScroll == null)
                descriptionScroll = FindChild<ScrollRect>("介绍滚动区域");
            if (detailScroll == null)
                detailScroll = FindChild<ScrollRect>("详情滚动区域");
            if (panelRect == null)
                panelRect = FindChild<RectTransform>("背景");
        }

        private bool ValidateReferences()
        {
            if (visualRoot == null)
            {
                Debug.LogError(
                    "[ItemDetail] 查看详情预制体缺少 Canvas 根节点。",
                    this);
                return false;
            }

            if (nameText == null || descriptionText == null
                || detailText == null)
            {
                Debug.LogError(
                    "[ItemDetail] 查看详情预制体缺少名称、介绍或详情文本绑定。",
                    this);
                return false;
            }

            return true;
        }

        private void AppendLineIfNeeded(StringBuilder builder)
        {
            if (builder.Length > 0)
                builder.AppendLine();
        }

        private T FindChild<T>(params string[] childNames)
            where T : Component
        {
            if (childNames == null)
                return null;

            T[] components = GetComponentsInChildren<T>(true);
            for (int index = 0; index < components.Length; index++)
            {
                T component = components[index];
                if (component == null)
                    continue;

                for (int nameIndex = 0; nameIndex < childNames.Length;
                    nameIndex++)
                {
                    if (string.Equals(component.gameObject.name,
                        childNames[nameIndex], StringComparison.Ordinal))
                        return component;
                }
            }

            return null;
        }
    }
}
