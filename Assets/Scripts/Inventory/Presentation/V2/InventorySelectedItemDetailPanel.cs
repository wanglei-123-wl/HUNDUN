using System;
using System.Text;
using Game.Items;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Inventory.Presentation.V2
{
    [DisallowMultipleComponent]
    public sealed class InventorySelectedItemDetailPanel : MonoBehaviour
    {
        [SerializeField] private InventoryGridView grid;
        [SerializeField] private GameObject detailPanel;
        [SerializeField] private Image itemIcon;
        [SerializeField] private TMP_Text itemName;
        [SerializeField] private TMP_Text description;
        [SerializeField] private ScrollRect descriptionScroll;
        [SerializeField] private TMP_Text attributes;
        [SerializeField] private ScrollRect attributesScroll;
        [SerializeField] private Button learnButton;
        [SerializeField] private Button useButton;
        [SerializeField] private Button sellButton;

        private Func<string, (bool CanLearn, bool CanUse)> resolveActions;
        private Func<string, string> resolveAttributes;

        public event Action<string> LearnRequested;
        public event Action<string> UseRequested;
        public event Action<string> SellRequested;

        internal void SetActionResolver(Func<string, (bool CanLearn, bool CanUse)> resolver)
        {
            resolveActions = resolver;
            RefreshActions(grid?.SelectedV2Slot?.V2Identity);
        }

        internal void SetAttributesResolver(Func<string, string> resolver)
        {
            resolveAttributes = resolver;
        }

        private void OnEnable()
        {
            if (learnButton != null)
                learnButton.onClick.AddListener(HandleLearnClicked);
            if (useButton != null)
                useButton.onClick.AddListener(HandleUseClicked);
            if (sellButton != null)
                sellButton.onClick.AddListener(HandleSellClicked);
            if (grid != null)
                grid.V2SelectionChanged += HandleSelectionChanged;
            HandleSelectionChanged(grid?.SelectedV2Slot);
        }

        private void OnDisable()
        {
            if (learnButton != null)
                learnButton.onClick.RemoveListener(HandleLearnClicked);
            if (useButton != null)
                useButton.onClick.RemoveListener(HandleUseClicked);
            if (sellButton != null)
                sellButton.onClick.RemoveListener(HandleSellClicked);
            if (grid != null)
            {
                grid.V2SelectionChanged -= HandleSelectionChanged;
                grid.ClearSelection();
            }
            HideDetail();
        }

        private void HandleLearnClicked()
        {
            string identity = grid?.SelectedV2Slot?.V2Identity;
            if (!string.IsNullOrEmpty(identity))
                LearnRequested?.Invoke(identity);
        }

        private void HandleUseClicked()
        {
            string identity = grid?.SelectedV2Slot?.V2Identity;
            if (!string.IsNullOrEmpty(identity))
                UseRequested?.Invoke(identity);
        }

        private void HandleSellClicked()
        {
            string identity = grid?.SelectedV2Slot?.V2Identity;
            if (!string.IsNullOrEmpty(identity))
                SellRequested?.Invoke(identity);
        }

        private void HandleSelectionChanged(InventorySlotView slot)
        {
            if (slot == null || string.IsNullOrEmpty(slot.V2Identity))
            {
                HideDetail();
                return;
            }

            ItemDetailData detail = slot.BoundDetail;
            if (itemIcon != null)
            {
                itemIcon.sprite = detail?.Icon;
                itemIcon.gameObject.SetActive(detail?.Icon != null);
            }
            if (itemName != null)
                itemName.text = detail?.DisplayName ?? string.Empty;
            if (description != null)
                description.text = detail?.Description ?? string.Empty;
            if (attributes != null)
                attributes.text = resolveAttributes != null
                    ? resolveAttributes(slot.V2Identity) : BuildAttributes(detail);

            RefreshActions(slot.V2Identity);
            if (detailPanel != null)
                detailPanel.SetActive(true);
            Canvas.ForceUpdateCanvases();
            FitScroll(descriptionScroll, description);
            FitScroll(attributesScroll, attributes);
        }

        private void HideDetail()
        {
            if (itemIcon != null)
            {
                itemIcon.sprite = null;
                itemIcon.gameObject.SetActive(false);
            }
            if (itemName != null) itemName.text = string.Empty;
            if (description != null) description.text = string.Empty;
            if (attributes != null) attributes.text = string.Empty;
            RefreshActions(null);
            if (detailPanel != null) detailPanel.SetActive(false);
        }

        private void RefreshActions(string identity)
        {
            bool hasItem = !string.IsNullOrEmpty(identity);
            var (canLearn, canUse) = hasItem && resolveActions != null
                ? resolveActions(identity) : default;
            if (learnButton != null) learnButton.gameObject.SetActive(canLearn);
            if (useButton != null) useButton.gameObject.SetActive(canUse);
            if (sellButton != null) sellButton.gameObject.SetActive(hasItem);
        }

        private static string BuildAttributes(ItemDetailData detail)
        {
            if (detail == null)
                return string.Empty;

            var text = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(detail.DetailText))
                text.Append(detail.DetailText.Trim());
            if (detail.Entries != null)
            {
                foreach (ItemDetailEntry entry in detail.Entries)
                {
                    if (entry == null || !entry.IsValid)
                        continue;
                    if (text.Length > 0) text.AppendLine();
                    if (!string.IsNullOrWhiteSpace(entry.Label))
                        text.Append(entry.Label.Trim()).Append("：");
                    if (!string.IsNullOrWhiteSpace(entry.Value))
                        text.Append(entry.Value.Trim());
                }
            }
            return text.ToString();
        }

        private static void FitScroll(ScrollRect scroll, TMP_Text text)
        {
            if (scroll == null || scroll.content == null || text == null)
                return;

            RectTransform textRect = text.rectTransform;
            float height = Mathf.Max(1f, Mathf.Ceil(text.preferredHeight));
            textRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
            RectTransform viewport = scroll.viewport != null
                ? scroll.viewport : (RectTransform)scroll.transform;
            scroll.content.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Vertical,
                Mathf.Max(viewport.rect.height, height - textRect.anchoredPosition.y));
            scroll.StopMovement();
            scroll.verticalNormalizedPosition = 1f;
        }
    }
}
