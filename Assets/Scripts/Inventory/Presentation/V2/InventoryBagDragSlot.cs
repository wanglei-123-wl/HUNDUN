using System;
using System.Collections.Generic;
using Game.Inventory.Model.V2;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.Inventory.Presentation.V2
{
    [DisallowMultipleComponent]
    public sealed class InventoryBagDragSlot : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
    {
        private int displayIndex = -1;
        private string identity;
        private string itemId;
        private string displayName;
        private string dragIdentity;
        private Image icon;
        private RectTransform ghost;
        private Canvas dragCanvas;
        private RectTransform bagBounds;
        private InventoryBagRightDragScroll rightDragScroll;
        private bool dropHandled;
        private readonly List<RaycastResult> raycastResults = new List<RaycastResult>();

        public string DragIdentity => dragIdentity;
        public string ItemId => itemId;
        public string DisplayName => displayName;
        public Image ItemIcon => icon;
        public event Action<int, int, string> DropRequested;
        public event Action<int, int, string> UnequipRequested;
        public event Action<string> WorldDropOffered;

        public void Bind(int index, string itemIdentity, string boundItemId,
            string boundDisplayName, Image itemIcon, RectTransform bounds)
        {
            if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
            displayIndex = index;
            identity = itemIdentity;
            itemId = boundItemId;
            displayName = boundDisplayName;
            icon = itemIcon;
            bagBounds = bounds;
            rightDragScroll = GetComponentInParent<InventoryBagRightDragScroll>();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData != null && eventData.button == PointerEventData.InputButton.Right)
            {
                rightDragScroll?.OnBeginDrag(eventData);
                return;
            }

            dropHandled = false;
            if (identity == null || eventData == null
                || eventData.button != PointerEventData.InputButton.Left) return;
            if (!InventoryDragPreview.TryCreate(this, "拖拽图标",
                out ghost, out dragCanvas)) return;
            dragIdentity = identity;

            ghost.sizeDelta = icon != null
                ? icon.rectTransform.rect.size
                : ((RectTransform)transform).rect.size;
            Image previewImage = ghost.GetComponent<Image>();
            previewImage.sprite = icon != null && icon.sprite != null
                ? icon.sprite : GetComponent<Image>()?.sprite;
            previewImage.color = new Color(1f, 1f, 1f, 0.8f);
            OnDrag(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData != null && eventData.button == PointerEventData.InputButton.Right)
            {
                rightDragScroll?.OnDrag(eventData);
                return;
            }

            InventoryDragPreview.Move(ghost, dragCanvas, eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (eventData != null && eventData.button == PointerEventData.InputButton.Right)
            {
                rightDragScroll?.OnEndDrag(eventData);
                return;
            }

            string releasedIdentity = dragIdentity;
            bool insideBag = bagBounds == null || eventData == null
                || RectTransformUtility.RectangleContainsScreenPoint(
                    bagBounds, eventData.position, eventData.pressEventCamera);
            bool offer = InventoryWorldDropRules.ShouldOffer(
                true, releasedIdentity != null, insideBag,
                IsOverUi(eventData), dropHandled);
            if (ghost != null) Destroy(ghost.gameObject);
            ghost = null;
            dragCanvas = null;
            dragIdentity = null;
            dropHandled = false;
            if (offer) WorldDropOffered?.Invoke(releasedIdentity);
        }

        public void OnDrop(PointerEventData eventData)
        {
            if (eventData.pointerDrag == null || displayIndex < 0) return;
            InventoryEquipmentSlotView equipment =
                eventData.pointerDrag.GetComponent<InventoryEquipmentSlotView>();
            if (equipment != null && equipment.DragIdentity != null)
            {
                UnequipRequested?.Invoke(equipment.SlotCode, displayIndex, equipment.DragIdentity);
                return;
            }
            InventoryBagDragSlot source = eventData.pointerDrag == null
                ? null : eventData.pointerDrag.GetComponent<InventoryBagDragSlot>();
            if (source == null || source == this || source.dragIdentity == null
                || displayIndex < 0 || source.transform.parent != transform.parent)
                return;
            source.dropHandled = true;
            DropRequested?.Invoke(source.displayIndex, displayIndex, source.dragIdentity);
        }

        private bool IsOverUi(PointerEventData eventData)
        {
            if (eventData == null || EventSystem.current == null) return true;
            raycastResults.Clear();
            EventSystem.current.RaycastAll(eventData, raycastResults);
            foreach (RaycastResult result in raycastResults)
            {
                GameObject hit = result.gameObject;
                if (hit != null && hit.GetComponentInParent<Canvas>() != null
                    && hit.GetComponent<Graphic>() != null)
                    return true;
            }
            return false;
        }

        private void OnDisable()
        {
            if (ghost != null) Destroy(ghost.gameObject);
            ghost = null;
            dragCanvas = null;
            dragIdentity = null;
            dropHandled = false;
            raycastResults.Clear();
        }
    }
}
