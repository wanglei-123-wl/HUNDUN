using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Inventory.Presentation.V2
{
    [DisallowMultipleComponent]
    public sealed class InventoryDiscardConfirmationView : MonoBehaviour
    {
        private static InventoryDiscardConfirmationView openView;
        private TMP_Text title;
        private TMP_Text quantityText;
        private GameObject quantityControls;
        private Button confirmButton;
        private Button cancelButton;
        private Button backdropButton;
        private TMP_Text statusText;
        private string identity;
        private int maximum;
        private int quantity;
        private bool pending;

        public static bool BlocksGameplayInput =>
            openView != null && openView.isActiveAndEnabled;

        public event Action<string, int> Confirmed;

        public static InventoryDiscardConfirmationView Create(InventoryPanelView panel)
        {
            Canvas canvas = panel == null ? null : panel.GetComponentInParent<Canvas>(true);
            if (canvas == null)
            {
                Debug.LogWarning("Discard confirmation needs the inventory canvas.");
                return null;
            }

            var root = new GameObject("地面掉落确认弹窗", typeof(RectTransform),
                typeof(Canvas), typeof(GraphicRaycaster),
                typeof(InventoryDiscardConfirmationView));
            RectTransform rect = (RectTransform)root.transform;
            rect.SetParent(canvas.rootCanvas.transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            Canvas overlay = root.GetComponent<Canvas>();
            overlay.overrideSorting = true;
            overlay.sortingOrder = Mathf.Max(110, canvas.rootCanvas.sortingOrder + 1);
            InventoryDiscardConfirmationView view =
                root.GetComponent<InventoryDiscardConfirmationView>();
            view.Build(panel.GetComponentInChildren<TMP_Text>(true)?.font);
            root.SetActive(false);
            return view;
        }

        public void Show(string itemIdentity, string displayName, int availableCount)
        {
            if (string.IsNullOrEmpty(itemIdentity) || availableCount <= 0) return;
            identity = itemIdentity;
            maximum = availableCount;
            quantity = maximum;
            pending = false;
            title.text = "将 " + displayName + " 丢到地上？";
            quantityControls.SetActive(maximum > 1);
            statusText.text = string.Empty;
            cancelButton.interactable = backdropButton.interactable = true;
            UpdateQuantity();
            SetConfirmEnabled(false);
            gameObject.SetActive(true);
            openView = this;
        }

        public void SetConfirmEnabled(bool enabled)
        {
            if (confirmButton != null) confirmButton.interactable = enabled && !pending;
        }

        public void SetPending(bool value)
        {
            pending = value;
            confirmButton.interactable = !value;
            cancelButton.interactable = backdropButton.interactable = !value;
            quantityControls.SetActive(!value && maximum > 1);
            statusText.text = value ? "正在丢到地上..." : string.Empty;
        }

        public void ShowFailure()
        {
            SetPending(false);
            statusText.text = "丢到地上失败，物品未改变";
            SetConfirmEnabled(true);
        }

        public void Hide()
        {
            if (openView == this) openView = null;
            identity = null;
            pending = false;
            gameObject.SetActive(false);
        }

        public void Dispose()
        {
            if (openView == this) openView = null;
            if (this != null) Destroy(gameObject);
        }

        private void Update()
        {
            if (!pending && Input.GetKeyDown(KeyCode.Escape)) Hide();
        }

        private void OnDisable()
        {
            if (openView == this) openView = null;
        }

        private void Build(TMP_FontAsset font)
        {
            RectTransform backdrop = AddImage(transform, "遮罩",
                new Color(0f, 0f, 0f, .65f));
            Stretch(backdrop);
            backdropButton = backdrop.gameObject.AddComponent<Button>();
            backdropButton.onClick.AddListener(TryCancel);

            RectTransform dialog = AddImage(transform, "确认内容",
                new Color(.14f, .17f, .19f, 1f));
            dialog.anchorMin = dialog.anchorMax = new Vector2(.5f, .5f);
            float width = Mathf.Min(360f,
                ((RectTransform)transform.parent).rect.width - 32f);
            dialog.sizeDelta = new Vector2(Mathf.Max(260f, width), 224f);
            dialog.anchoredPosition = Vector2.zero;

            title = AddText(dialog, "物品名称", font, 20, TextAlignmentOptions.Center,
                new Vector2(0f, 72f),
                new Vector2(dialog.sizeDelta.x - 32f, 50f));
            title.enableAutoSizing = true;
            title.fontSizeMin = 13;
            title.fontSizeMax = 20;

            quantityControls = new GameObject("数量选择", typeof(RectTransform));
            RectTransform controls = (RectTransform)quantityControls.transform;
            controls.SetParent(dialog, false);
            controls.anchorMin = controls.anchorMax = new Vector2(.5f, .5f);
            controls.sizeDelta = new Vector2(260f, 44f);
            controls.anchoredPosition = new Vector2(0f, 8f);
            AddButton(controls, "减少数量", new Color(.27f, .31f, .33f, 1f),
                "−", () => ChangeQuantity(-1),
                new Vector2(-98f, 0f), new Vector2(42f, 40f), font);
            quantityText = AddText(controls, "当前数量", font, 17,
                TextAlignmentOptions.Center, new Vector2(-15f, 0f),
                new Vector2(100f, 40f));
            AddButton(controls, "增加数量", new Color(.27f, .31f, .33f, 1f),
                "+", () => ChangeQuantity(1),
                new Vector2(63f, 0f), new Vector2(42f, 40f), font);
            AddButton(controls, "全部数量", new Color(.27f, .31f, .33f, 1f),
                "全部", () => { quantity = maximum; UpdateQuantity(); },
                new Vector2(111f, 0f), new Vector2(48f, 40f), font);

            statusText = AddText(dialog, "请求状态", font, 14,
                TextAlignmentOptions.Center, new Vector2(0f, -32f),
                new Vector2(dialog.sizeDelta.x - 24f, 28f));
            cancelButton = AddButton(dialog, "取消掉落", new Color(.27f, .31f, .33f, 1f),
                "取消", TryCancel, new Vector2(-76f, -73f),
                new Vector2(112f, 44f), font);
            confirmButton = AddButton(dialog, "确认掉落",
                new Color(.65f, .25f, .22f, 1f), "丢到地上", Confirm,
                new Vector2(76f, -73f), new Vector2(112f, 44f), font);
        }

        private void Confirm()
        {
            if (pending || string.IsNullOrEmpty(identity)) return;
            Confirmed?.Invoke(identity, quantity);
        }

        private void TryCancel()
        {
            if (!pending) Hide();
        }

        private void ChangeQuantity(int step)
        {
            if (step > 0 && quantity < maximum) quantity++;
            if (step < 0 && quantity > 1) quantity--;
            UpdateQuantity();
        }

        private void UpdateQuantity()
        {
            if (quantityText != null) quantityText.text = quantity + " / " + maximum;
        }

        private static RectTransform AddImage(Transform parent, string name, Color color)
        {
            var gameObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            RectTransform rect = (RectTransform)gameObject.transform;
            rect.SetParent(parent, false);
            gameObject.GetComponent<Image>().color = color;
            return rect;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static TMP_Text AddText(Transform parent, string name,
            TMP_FontAsset font, int size, TextAlignmentOptions alignment,
            Vector2 position, Vector2 dimensions)
        {
            var gameObject = new GameObject(name, typeof(RectTransform),
                typeof(TextMeshProUGUI));
            RectTransform rect = (RectTransform)gameObject.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.sizeDelta = dimensions;
            rect.anchoredPosition = position;
            TMP_Text text = gameObject.GetComponent<TMP_Text>();
            if (font != null) text.font = font;
            text.fontSize = size;
            text.alignment = alignment;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }

        private static Button AddButton(Transform parent, string name, Color color,
            string label, Action action, Vector2 position = default,
            Vector2 dimensions = default, TMP_FontAsset font = null)
        {
            RectTransform rect = AddImage(parent, name, color);
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = dimensions == default ? new Vector2(1f, 1f) : dimensions;
            Button button = rect.gameObject.AddComponent<Button>();
            if (action != null) button.onClick.AddListener(() => action());
            if (label != null)
                AddText(rect, name + "文字", font, 17,
                    TextAlignmentOptions.Center, Vector2.zero, rect.sizeDelta).text = label;
            return button;
        }
    }
}
