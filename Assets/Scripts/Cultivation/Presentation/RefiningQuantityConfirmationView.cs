using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Cultivation.Presentation
{
    [DisallowMultipleComponent]
    public sealed class RefiningQuantityConfirmationView : MonoBehaviour
    {
        private TMP_Text title;
        private TMP_Text status;
        private TMP_InputField quantityInput;
        private Button confirmButton;
        private string identity;
        private int maximum;
        private Func<int, bool> isAllowed;

        public event Action<string, int> Confirmed;

        public static RefiningQuantityConfirmationView Create(Canvas canvas,
            TMP_FontAsset font)
        {
            if (canvas == null)
                return null;
            var root = new GameObject("炼化数量确认弹窗", typeof(RectTransform),
                typeof(Canvas), typeof(GraphicRaycaster),
                typeof(RefiningQuantityConfirmationView));
            RectTransform rect = (RectTransform)root.transform;
            rect.SetParent(canvas.rootCanvas.transform, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            Canvas overlay = root.GetComponent<Canvas>();
            overlay.overrideSorting = true;
            overlay.sortingOrder = Mathf.Max(111, canvas.rootCanvas.sortingOrder + 2);
            RefiningQuantityConfirmationView view =
                root.GetComponent<RefiningQuantityConfirmationView>();
            view.Build(font);
            root.SetActive(false);
            return view;
        }

        public void Show(string stackIdentity, string displayName,
            int availableCount, Func<int, bool> canConfirm)
        {
            if (string.IsNullOrEmpty(stackIdentity) || availableCount <= 0)
                return;
            identity = stackIdentity;
            maximum = availableCount;
            isAllowed = canConfirm;
            title.text = "炼化 " + displayName + "？";
            status.text = "最多 " + maximum.ToString(CultureInfo.InvariantCulture);
            quantityInput.SetTextWithoutNotify(
                maximum.ToString(CultureInfo.InvariantCulture));
            UpdateValidity();
            gameObject.SetActive(true);
            GameplayUiInputGuard.RegisterModal(gameObject);
        }

        public void ShowError(string message)
        {
            if (status != null)
                status.text = message;
            if (confirmButton != null)
                confirmButton.interactable = false;
        }

        public void Hide()
        {
            GameplayUiInputGuard.UnregisterModal(gameObject);
            identity = null;
            isAllowed = null;
            gameObject.SetActive(false);
        }

        public void Dispose()
        {
            GameplayUiInputGuard.UnregisterModal(gameObject);
            if (this != null)
                Destroy(gameObject);
        }

        private void OnDisable()
        {
            GameplayUiInputGuard.UnregisterModal(gameObject);
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
                Hide();
        }

        private void Build(TMP_FontAsset font)
        {
            RectTransform backdrop = AddImage(transform, "遮罩",
                new Color(0f, 0f, 0f, .65f));
            Stretch(backdrop);
            backdrop.gameObject.AddComponent<Button>().onClick.AddListener(Hide);

            RectTransform dialog = AddImage(transform, "确认内容",
                new Color(.14f, .17f, .19f, 1f));
            dialog.anchorMin = dialog.anchorMax = new Vector2(.5f, .5f);
            float width = Mathf.Min(360f,
                ((RectTransform)transform.parent).rect.width - 32f);
            dialog.sizeDelta = new Vector2(Mathf.Max(260f, width), 224f);
            dialog.anchoredPosition = Vector2.zero;

            title = AddText(dialog, "物品名称", font, 20,
                new Vector2(0f, 72f),
                new Vector2(dialog.sizeDelta.x - 32f, 50f));
            title.enableAutoSizing = true;
            title.fontSizeMin = 13;
            title.fontSizeMax = 20;

            RectTransform controls = new GameObject("数量选择",
                typeof(RectTransform)).GetComponent<RectTransform>();
            controls.SetParent(dialog, false);
            controls.anchorMin = controls.anchorMax = new Vector2(.5f, .5f);
            controls.sizeDelta = new Vector2(260f, 44f);
            controls.anchoredPosition = new Vector2(0f, 8f);

            AddButton(controls, "减少数量", new Color(.27f, .31f, .33f, 1f),
                "-", () => ChangeQuantity(-1), new Vector2(-105f, 0f),
                new Vector2(38f, 40f), font);
            RectTransform inputBackground = AddImage(controls, "填写数量",
                new Color(.07f, .1f, .11f, 1f));
            inputBackground.anchorMin = inputBackground.anchorMax =
                new Vector2(.5f, .5f);
            inputBackground.anchoredPosition = new Vector2(-25f, 0f);
            inputBackground.sizeDelta = new Vector2(90f, 40f);

            RectTransform viewport = new GameObject("输入区域", typeof(RectTransform),
                typeof(RectMask2D)).GetComponent<RectTransform>();
            viewport.SetParent(inputBackground, false);
            Stretch(viewport);
            viewport.offsetMin = new Vector2(6f, 2f);
            viewport.offsetMax = new Vector2(-6f, -2f);
            TMP_Text inputText = AddText(viewport, "数量", font, 17,
                Vector2.zero, new Vector2(78f, 36f));
            Stretch(inputText.rectTransform);
            inputText.alignment = TextAlignmentOptions.Center;
            inputText.raycastTarget = false;

            quantityInput = inputBackground.gameObject.AddComponent<TMP_InputField>();
            quantityInput.textViewport = viewport;
            quantityInput.textComponent = (TextMeshProUGUI)inputText;
            quantityInput.targetGraphic = inputBackground.GetComponent<Image>();
            quantityInput.lineType = TMP_InputField.LineType.SingleLine;
            quantityInput.contentType = TMP_InputField.ContentType.IntegerNumber;
            quantityInput.characterLimit = 10;
            quantityInput.onValueChanged.AddListener(_ => UpdateValidity());

            AddButton(controls, "增加数量", new Color(.27f, .31f, .33f, 1f),
                "+", () => ChangeQuantity(1), new Vector2(48f, 0f),
                new Vector2(38f, 40f), font);
            AddButton(controls, "全部数量", new Color(.27f, .31f, .33f, 1f),
                "全部", () => SetQuantity(maximum), new Vector2(103f, 0f),
                new Vector2(48f, 40f), font);

            status = AddText(dialog, "数量范围", font, 14,
                new Vector2(0f, -32f),
                new Vector2(dialog.sizeDelta.x - 24f, 28f));
            AddButton(dialog, "取消炼化选择", new Color(.27f, .31f, .33f, 1f),
                "取消", Hide, new Vector2(-76f, -73f),
                new Vector2(112f, 44f), font);
            confirmButton = AddButton(dialog, "确认炼化数量",
                new Color(.25f, .53f, .48f, 1f), "确认数量", Confirm,
                new Vector2(76f, -73f), new Vector2(112f, 44f), font);
        }

        private void Confirm()
        {
            if (!CanConfirm(out int amount) || string.IsNullOrEmpty(identity))
                return;
            Confirmed?.Invoke(identity, amount);
        }

        private bool CanConfirm(out int amount)
        {
            return TryReadQuantity(out amount)
                && (isAllowed == null || isAllowed(amount));
        }

        private bool TryReadQuantity(out int amount)
        {
            return int.TryParse(quantityInput.text, NumberStyles.None,
                    CultureInfo.InvariantCulture, out amount)
                && amount >= 1 && amount <= maximum;
        }

        private void UpdateValidity()
        {
            bool inRange = TryReadQuantity(out int amount);
            bool withinRule = inRange && (isAllowed == null || isAllowed(amount));
            if (confirmButton != null)
                confirmButton.interactable = withinRule;
            if (status != null)
                status.text = inRange && !withinRule
                    ? "数量超出本次炼化上限"
                    : "最多 " + maximum.ToString(CultureInfo.InvariantCulture);
        }

        private void ChangeQuantity(int delta)
        {
            int current = TryReadQuantity(out int amount) ? amount : 1;
            if (delta > 0 && current < maximum)
                current++;
            else if (delta < 0 && current > 1)
                current--;
            SetQuantity(current);
        }

        private void SetQuantity(int amount)
        {
            quantityInput.SetTextWithoutNotify(
                amount.ToString(CultureInfo.InvariantCulture));
            UpdateValidity();
        }

        private static RectTransform AddImage(Transform parent, string name, Color color)
        {
            var item = new GameObject(name, typeof(RectTransform), typeof(Image));
            RectTransform rect = (RectTransform)item.transform;
            rect.SetParent(parent, false);
            item.GetComponent<Image>().color = color;
            return rect;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static TMP_Text AddText(Transform parent, string name,
            TMP_FontAsset font, int size, Vector2 position, Vector2 dimensions)
        {
            RectTransform rect = new GameObject(name, typeof(RectTransform),
                typeof(TextMeshProUGUI)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.sizeDelta = dimensions;
            rect.anchoredPosition = position;
            TMP_Text text = rect.GetComponent<TMP_Text>();
            if (font != null)
                text.font = font;
            text.fontSize = size;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }

        private static Button AddButton(Transform parent, string name, Color color,
            string label, Action action, Vector2 position, Vector2 dimensions,
            TMP_FontAsset font)
        {
            RectTransform rect = AddImage(parent, name, color);
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = dimensions;
            Button button = rect.gameObject.AddComponent<Button>();
            button.onClick.AddListener(() => action());
            AddText(rect, name + "文字", font, 17, Vector2.zero, dimensions).text =
                label;
            return button;
        }
    }
}
