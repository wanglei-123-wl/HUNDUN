using System;
using Game.Items;
using Game.Inventory.Protocol.V2;
using Game.UI.ItemDetail;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.Inventory.Presentation.V2
{
    [DisallowMultipleComponent]
    public sealed class InventoryEquipmentSlotView : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
    {
        private Image itemIcon;
        private Image unknownPlaceholder;
        private TMP_Text unknownMark;
        private GameObject emptySlotLabel;
        private RectTransform ghost;
        private Canvas dragCanvas;
        private ItemDetailHoverTrigger detailHoverTrigger;
        private string identity;
        private string dragIdentity;
        private bool pending;

        public int SlotCode { get; private set; }
        public string DragIdentity => dragIdentity;
        public event Action<string, int> EquipRequested;
        public event Action<int, int, string> MoveRequested;

        public void Configure(int slotCode, Image icon, GameObject label)
        {
            if (slotCode < 1 || slotCode > InventoryV2Protocol.EquipmentSlotCount || icon == null)
                throw new ArgumentException("Equipment slot binding is invalid.");
            SlotCode = slotCode;
            itemIcon = icon;
            emptySlotLabel = label;
            detailHoverTrigger = GetComponent<ItemDetailHoverTrigger>();
            if (detailHoverTrigger == null
                && ItemDetailHoverService.IsConfiguredInScene(this))
                detailHoverTrigger = gameObject.AddComponent<ItemDetailHoverTrigger>();
            if (unknownMark == null)
            {
                var placeholder = new GameObject("未知装备占位图",
                    typeof(RectTransform), typeof(Image));
                RectTransform placeholderRect = (RectTransform)placeholder.transform;
                RectTransform iconRect = itemIcon.rectTransform;
                placeholderRect.SetParent(iconRect.parent, false);
                placeholderRect.anchorMin = iconRect.anchorMin;
                placeholderRect.anchorMax = iconRect.anchorMax;
                placeholderRect.pivot = iconRect.pivot;
                placeholderRect.sizeDelta = iconRect.sizeDelta;
                placeholderRect.anchoredPosition = iconRect.anchoredPosition;
                placeholderRect.SetSiblingIndex(iconRect.GetSiblingIndex() + 1);
                unknownPlaceholder = placeholder.GetComponent<Image>();
                unknownPlaceholder.color = new Color(.22f, .27f, .28f, .95f);
                unknownPlaceholder.raycastTarget = false;

                var marker = new GameObject("未知装备标记",
                    typeof(RectTransform), typeof(TextMeshProUGUI));
                var rect = (RectTransform)marker.transform;
                rect.SetParent(placeholderRect, false);
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                unknownMark = marker.GetComponent<TextMeshProUGUI>();
                unknownMark.text = "?";
                unknownMark.fontSize = 24;
                unknownMark.alignment = TextAlignmentOptions.Center;
                unknownMark.raycastTarget = false;
            }
            Clear();
        }

        public void SetEquipped(string itemIdentity, Sprite sprite)
        {
            SetEquipped(itemIdentity, sprite, null);
        }

        public void SetEquipped(string itemIdentity, Sprite sprite,
            ItemDetailData detail)
        {
            identity = itemIdentity;
            if (detailHoverTrigger != null)
            {
                if (detail == null || !detail.IsValid)
                    detailHoverTrigger.ClearBinding();
                else
                    detailHoverTrigger.Bind(detail);
            }
            itemIcon.sprite = sprite;
            itemIcon.color = Color.white;
            itemIcon.raycastTarget = false;
            itemIcon.gameObject.SetActive(sprite != null);
            unknownPlaceholder.gameObject.SetActive(itemIdentity != null && sprite == null);
            unknownMark.gameObject.SetActive(itemIdentity != null && sprite == null);
            if (emptySlotLabel != null)
                emptySlotLabel.SetActive(itemIdentity == null);
        }

        public void SetPending(bool value)
        {
            pending = value;
        }

        public void Clear()
        {
            identity = null;
            if (detailHoverTrigger != null)
                detailHoverTrigger.ClearBinding();
            if (itemIcon != null)
            {
                itemIcon.sprite = null;
                itemIcon.gameObject.SetActive(false);
            }
            if (unknownMark != null) unknownMark.gameObject.SetActive(false);
            if (unknownPlaceholder != null) unknownPlaceholder.gameObject.SetActive(false);
            if (emptySlotLabel != null) emptySlotLabel.SetActive(true);
            CancelPreview();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (pending || identity == null) return;
            if (!InventoryDragPreview.TryCreate(this, "拖拽装备",
                out ghost, out dragCanvas)) return;
            dragIdentity = identity;
            ghost.sizeDelta = ((RectTransform)transform).rect.size;
            Image image = ghost.GetComponent<Image>();
            image.sprite = itemIcon.sprite != null
                ? itemIcon.sprite : GetComponent<Image>()?.sprite;
            image.color = itemIcon.sprite != null
                ? new Color(1f, 1f, 1f, .8f)
                : new Color(.25f, .5f, .5f, .8f);
            OnDrag(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            InventoryDragPreview.Move(ghost, dragCanvas, eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            CancelPreview();
        }

        public void OnDrop(PointerEventData eventData)
        {
            if (pending || eventData.pointerDrag == null) return;
            InventoryBagDragSlot bag = eventData.pointerDrag.GetComponent<InventoryBagDragSlot>();
            if (bag != null && bag.DragIdentity != null)
            {
                EquipRequested?.Invoke(bag.DragIdentity, SlotCode);
                return;
            }
            InventoryEquipmentSlotView source =
                eventData.pointerDrag.GetComponent<InventoryEquipmentSlotView>();
            if (source != null && source != this && source.dragIdentity != null)
                MoveRequested?.Invoke(source.SlotCode, SlotCode, source.dragIdentity);
        }

        private void OnDisable()
        {
            CancelPreview();
        }

        private void CancelPreview()
        {
            if (ghost != null) Destroy(ghost.gameObject);
            ghost = null;
            dragCanvas = null;
            dragIdentity = null;
        }
    }
}
