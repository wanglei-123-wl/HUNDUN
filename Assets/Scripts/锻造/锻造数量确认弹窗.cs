using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Forging.Presentation
{
    [DisallowMultipleComponent]
    public sealed class 锻造数量确认弹窗 : MonoBehaviour
    {
        private TMP_Text title;
        private TMP_Text hint;
        private TMP_InputField input;
        private Button confirmButton;
        private string identity;
        private int maximum;

        public event Action<string, int> Confirmed;
        public int LastEscapeFrame { get; private set; } = -1;

        public static 锻造数量确认弹窗 Create(Canvas canvas, TMP_FontAsset font)
        {
            if (canvas == null)
                return null;
            var root = new GameObject("锻造数量确认弹窗", typeof(RectTransform),
                typeof(Canvas), typeof(GraphicRaycaster),
                typeof(锻造数量确认弹窗));
            RectTransform rect = (RectTransform)root.transform;
            rect.SetParent(canvas.rootCanvas.transform, false);
            Stretch(rect);
            Canvas overlay = root.GetComponent<Canvas>();
            overlay.overrideSorting = true;
            overlay.sortingOrder = Mathf.Max(111, canvas.rootCanvas.sortingOrder + 2);
            锻造数量确认弹窗 dialog = root.GetComponent<锻造数量确认弹窗>();
            dialog.Build(font);
            root.SetActive(false);
            return dialog;
        }

        public void Show(string stackIdentity, string displayName, int available,
            int suggested)
        {
            if (string.IsNullOrEmpty(stackIdentity) || available <= 0)
                return;
            identity = stackIdentity;
            maximum = available;
            title.text = "选择 " + displayName + " 数量";
            SetQuantity(Mathf.Clamp(suggested, 1, maximum));
            gameObject.SetActive(true);
            GameplayUiInputGuard.RegisterModal(gameObject);
        }

        public void ShowError(string message)
        {
            hint.text = message;
            confirmButton.interactable = false;
        }

        public void Hide()
        {
            GameplayUiInputGuard.UnregisterModal(gameObject);
            identity = null;
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
            {
                LastEscapeFrame = Time.frameCount;
                Hide();
            }
        }

        private void Build(TMP_FontAsset font)
        {
            RectTransform shade = AddImage(transform, "遮罩",
                new Color(0f, 0f, 0f, 0.65f));
            Stretch(shade);
            shade.gameObject.AddComponent<Button>().onClick.AddListener(Hide);

            RectTransform card = AddImage(transform, "数量确认",
                new Color(0.1f, 0.2f, 0.25f, 1f));
            card.sizeDelta = new Vector2(340f, 210f);
            title = AddText(card, "材料名称", font, 20f,
                new Vector2(0f, 71f), new Vector2(308f, 42f));
            title.enableAutoSizing = true;
            title.fontSizeMin = 12f;
            title.fontSizeMax = 20f;

            RectTransform field = AddImage(card, "数量输入背景",
                new Color(0.94f, 0.97f, 0.98f, 1f));
            field.anchoredPosition = new Vector2(0f, 16f);
            field.sizeDelta = new Vector2(110f, 42f);
            RectTransform viewport = AddImage(field, "数量输入区域", Color.clear);
            Stretch(viewport);
            viewport.offsetMin = new Vector2(7f, 0f);
            viewport.offsetMax = new Vector2(-7f, 0f);
            viewport.gameObject.AddComponent<RectMask2D>();
            TMP_Text value = AddText(viewport, "数量文字", font, 18f,
                Vector2.zero, Vector2.zero);
            Stretch(value.rectTransform);
            value.color = new Color(0.07f, 0.2f, 0.3f);
            input = field.gameObject.AddComponent<TMP_InputField>();
            input.targetGraphic = field.GetComponent<Image>();
            input.textViewport = viewport;
            input.textComponent = (TextMeshProUGUI)value;
            input.contentType = TMP_InputField.ContentType.IntegerNumber;
            input.characterLimit = 10;
            input.onValueChanged.AddListener(_ => RefreshValidity());

            AddButton(card, "减少数量", "-", new Vector2(-86f, 16f),
                new Vector2(42f, 42f), font, () => Change(-1));
            AddButton(card, "增加数量", "+", new Vector2(86f, 16f),
                new Vector2(42f, 42f), font, () => Change(1));
            AddButton(card, "全部数量", "全部", new Vector2(139f, 16f),
                new Vector2(49f, 42f), font, () => SetQuantity(maximum));

            hint = AddText(card, "可选数量", font, 14f,
                new Vector2(0f, -26f), new Vector2(300f, 26f));
            AddButton(card, "取消选择", "取消", new Vector2(-75f, -72f),
                new Vector2(116f, 40f), font, Hide);
            confirmButton = AddButton(card, "确认数量", "确认",
                new Vector2(75f, -72f), new Vector2(116f, 40f), font, Confirm);
        }

        private void Confirm()
        {
            if (identity != null && TryRead(out int amount))
                Confirmed?.Invoke(identity, amount);
        }

        private bool TryRead(out int amount)
        {
            return int.TryParse(input.text, NumberStyles.None,
                    CultureInfo.InvariantCulture, out amount)
                && amount > 0 && amount <= maximum;
        }

        private void RefreshValidity()
        {
            confirmButton.interactable = TryRead(out _);
            hint.text = "最多 " + maximum.ToString(CultureInfo.InvariantCulture);
        }

        private void Change(int delta)
        {
            int current = TryRead(out int value) ? value : 1;
            SetQuantity(Mathf.Clamp(current + delta, 1, maximum));
        }

        private void SetQuantity(int amount)
        {
            input.SetTextWithoutNotify(
                amount.ToString(CultureInfo.InvariantCulture));
            RefreshValidity();
        }

        private static RectTransform AddImage(Transform parent, string name,
            Color color)
        {
            var rect = new GameObject(name, typeof(RectTransform), typeof(Image))
                .GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.GetComponent<Image>().color = color;
            return rect;
        }

        private static TMP_Text AddText(Transform parent, string name,
            TMP_FontAsset font, float size, Vector2 position, Vector2 dimensions)
        {
            var rect = new GameObject(name, typeof(RectTransform),
                typeof(TextMeshProUGUI)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = dimensions;
            TMP_Text text = rect.GetComponent<TMP_Text>();
            text.font = font;
            text.fontSize = size;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            return text;
        }

        private static Button AddButton(Transform parent, string name,
            string label, Vector2 position, Vector2 size, TMP_FontAsset font,
            Action action)
        {
            RectTransform rect = AddImage(parent, name,
                new Color(0.19f, 0.38f, 0.48f, 1f));
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            Button button = rect.gameObject.AddComponent<Button>();
            button.onClick.AddListener(() => action());
            AddText(rect, name + "文字", font, 16f, Vector2.zero, size).text =
                label;
            return button;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
