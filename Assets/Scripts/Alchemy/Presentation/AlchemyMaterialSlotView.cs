using System;
using System.Globalization;
using Game.Inventory.Presentation.V2;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.Alchemy.Presentation
{
    [DisallowMultipleComponent]
    public sealed class AlchemyMaterialSlotView : MonoBehaviour,
        IDropHandler, IPointerClickHandler, IBeginDragHandler, IDragHandler,
        IEndDragHandler
    {
        [SerializeField] private Image selectedIcon;
        [SerializeField] private TMP_Text quantityText;
        private RectTransform dragPreview;
        private Canvas dragCanvas;
        private string draggingIdentity;

        public string Identity { get; private set; }
        public event Action<InventoryBagDragSlot> DropRequested;
        public event Action<string> RemoveRequested;
        public event Action<string, PointerEventData> ReturnToBagRequested;

        public void Bind(string identity, int selected, Sprite icon)
        {
            Identity = identity;
            if (selectedIcon != null)
            {
                selectedIcon.sprite = selected > 0 ? icon : null;
                selectedIcon.preserveAspect = true;
                selectedIcon.raycastTarget = false;
                selectedIcon.gameObject.SetActive(selectedIcon.sprite != null);
            }
            if (quantityText != null)
            {
                quantityText.rectTransform.sizeDelta = new Vector2(44f, 15f);
                quantityText.rectTransform.anchoredPosition =
                    new Vector2(0f, -17f);
                quantityText.enableAutoSizing = true;
                quantityText.fontSizeMin = 8f;
                quantityText.fontSizeMax = 15f;
                quantityText.enableWordWrapping = false;
                quantityText.text = selected > 0
                    ? selected.ToString(CultureInfo.InvariantCulture)
                    : string.Empty;
                quantityText.color = Color.white;
                quantityText.raycastTarget = false;
            }
        }

        public void OnDrop(PointerEventData eventData)
        {
            InventoryBagDragSlot source = eventData?.pointerDrag != null
                ? eventData.pointerDrag.GetComponent<InventoryBagDragSlot>() : null;
            if (source != null && !string.IsNullOrEmpty(source.DragIdentity)
                && !string.IsNullOrEmpty(Identity))
                DropRequested?.Invoke(source);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData != null
                && eventData.button == PointerEventData.InputButton.Right
                && !string.IsNullOrEmpty(Identity))
                RemoveRequested?.Invoke(Identity);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData == null
                || eventData.button != PointerEventData.InputButton.Left
                || string.IsNullOrEmpty(Identity)
                || !InventoryDragPreview.TryCreate(this, "拖拽炼丹材料",
                    out dragPreview, out dragCanvas))
                return;
            draggingIdentity = Identity;
            dragPreview.sizeDelta = ((RectTransform)transform).rect.size;
            Image image = dragPreview.GetComponent<Image>();
            image.sprite = selectedIcon != null ? selectedIcon.sprite : null;
            image.color = image.sprite != null
                ? new Color(1f, 1f, 1f, .8f) : new Color(1f, 1f, 1f, .4f);
            OnDrag(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData != null)
                InventoryDragPreview.Move(dragPreview, dragCanvas, eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            string identity = draggingIdentity;
            ClearDrag();
            if (identity != null && eventData != null)
                ReturnToBagRequested?.Invoke(identity, eventData);
        }

        private void OnDisable()
        {
            ClearDrag();
        }

        private void ClearDrag()
        {
            if (dragPreview != null)
                Destroy(dragPreview.gameObject);
            dragPreview = null;
            dragCanvas = null;
            draggingIdentity = null;
        }
    }
}
