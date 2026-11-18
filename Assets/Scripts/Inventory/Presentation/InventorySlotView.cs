using System;
using Game.Items;
using Game.UI.ItemDetail;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.Inventory
{
    [DisallowMultipleComponent]
    public sealed class InventorySlotView : MonoBehaviour, IPointerClickHandler
    {
        [Header("Interaction")]
        [SerializeField] private Button button;

        [Header("Visuals")]
        [SerializeField] private Image background;
        [SerializeField] private Image itemIcon;
        [SerializeField] private Image qualityFrame;
        [SerializeField] private GameObject selectedFrame;
        [SerializeField] private TMP_Text stackCountText;
        [SerializeField] private GameObject newMark;
        private ItemDetailHoverTrigger detailHoverTrigger;

        public int DisplayIndex { get; private set; } = -1;
        public InventoryItemState Item { get; private set; }
        public string V2Identity { get; private set; }
        public ItemDetailData BoundDetail { get; private set; }
        public Image ItemIcon => itemIcon;
        public bool HasItem => Item != null || V2Identity != null;
        public string ItemInstanceId => Item == null
            ? string.Empty
            : Item.ItemInstanceId;

        public event Action<InventorySlotView> Clicked;
        public event Action<InventorySlotView> V2DoubleClicked;

        public void OnPointerClick(PointerEventData eventData)
        {
            if (V2Identity != null && eventData.button == PointerEventData.InputButton.Left
                && eventData.clickCount == 2 && (button == null || button.interactable))
                V2DoubleClicked?.Invoke(this);
        }

        private void Awake()
        {
            if (button == null)
            {
                button = GetComponent<Button>();
            }

            detailHoverTrigger = GetComponent<ItemDetailHoverTrigger>();
            if (detailHoverTrigger == null
                && ItemDetailHoverService.IsConfiguredInScene(this))
            {
                detailHoverTrigger = gameObject.AddComponent<ItemDetailHoverTrigger>();
            }

            Clear();
        }

        private void OnEnable()
        {
            if (button != null)
            {
                button.onClick.AddListener(HandleButtonClicked);
            }
        }

        private void OnDisable()
        {
            if (button != null)
            {
                button.onClick.RemoveListener(HandleButtonClicked);
            }
        }

        public void Initialize(int displayIndex)
        {
            if (displayIndex < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(displayIndex),
                    "Display index cannot be negative.");
            }

            DisplayIndex = displayIndex;
        }

        public void Bind(
            InventoryItemState item,
            ItemDefinition definition)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            Item = item;
            V2Identity = null;
            BindDetail(ItemDetailDataResolver.FromItem(definition));

            Sprite icon = definition == null ? null : definition.Icon;
            if (itemIcon != null)
            {
                itemIcon.sprite = icon;
                itemIcon.gameObject.SetActive(icon != null);
            }

            if (qualityFrame != null)
            {
                qualityFrame.color = definition == null
                    ? Color.white
                    : definition.QualityColor;
                qualityFrame.gameObject.SetActive(true);
            }

            if (stackCountText != null)
            {
                bool showCount = item.Count > 1;
                stackCountText.text = showCount
                    ? item.Count.ToString()
                    : string.Empty;
                stackCountText.gameObject.SetActive(showCount);
            }

            SetSelected(false);
            SetNew(false);
            SetInteractable(true);
        }

        public void Clear()
        {
            Item = null;
            V2Identity = null;
            BoundDetail = null;
            if (detailHoverTrigger != null)
                detailHoverTrigger.ClearBinding();

            if (itemIcon != null)
            {
                itemIcon.sprite = null;
                itemIcon.gameObject.SetActive(false);
            }

            if (qualityFrame != null)
            {
                qualityFrame.color = Color.white;
                qualityFrame.gameObject.SetActive(false);
            }

            if (stackCountText != null)
            {
                stackCountText.text = string.Empty;
                stackCountText.gameObject.SetActive(false);
            }

            SetSelected(false);
            SetNew(false);
            SetInteractable(true);
        }

        public void BindV2(string identity, Sprite icon, int count,
            ItemDefinition definition, bool isPill = false)
        {
            if (string.IsNullOrWhiteSpace(identity))
                throw new ArgumentException("Bag identity is required.", nameof(identity));
            Item = null;
            V2Identity = identity;
            ItemDetailData detail = ItemDetailDataResolver.FromItem(definition);
            if (isPill && detail != null)
            {
                detail.Category = ItemDetailCategory.Pill;
                detail.CategoryLabel = ItemDetailCategoryResolver.GetLabel(
                    ItemDetailCategory.Pill);
            }
            BindDetail(detail);
            if (itemIcon != null)
            {
                itemIcon.sprite = icon;
                itemIcon.gameObject.SetActive(icon != null);
            }
            if (stackCountText != null)
            {
                stackCountText.text = count > 1 ? count.ToString() : string.Empty;
                stackCountText.gameObject.SetActive(count > 1);
            }
            SetSelected(false);
            SetInteractable(true);
        }

        private void BindDetail(ItemDetailData detail)
        {
            BoundDetail = detail;
            if (detailHoverTrigger == null)
                return;

            if (detail == null || !detail.IsValid)
                detailHoverTrigger.ClearBinding();
            else
                detailHoverTrigger.Bind(detail);
        }

        public void SetSelected(bool selected)
        {
            if (selectedFrame != null)
            {
                selectedFrame.SetActive(selected);
            }
        }

        public void SetNew(bool isNew)
        {
            if (newMark != null)
            {
                newMark.SetActive(isNew);
            }
        }

        public void SetInteractable(bool interactable)
        {
            if (button != null)
            {
                button.interactable = interactable;
            }
        }

        public void SetBackgroundColor(Color color)
        {
            if (background != null)
            {
                background.color = color;
            }
        }

        private void HandleButtonClicked()
        {
            if (!HasItem)
            {
                return;
            }

            Clicked?.Invoke(this);
        }
    }
}
