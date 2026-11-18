using System;
using Game.Inventory.Presentation.V2;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.Forging.Presentation
{
    public sealed class 锻造材料格子视图 : MonoBehaviour, IDropHandler,
        IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [SerializeField] private Image selectedIcon;
        [SerializeField] private TMP_Text quantityText;

        private RectTransform preview;
        private Canvas previewCanvas;
        private string draggingIdentity;

        public string Identity { get; private set; }
        public event Action<InventoryBagDragSlot> DropRequested;
        public event Action<string> RemoveRequested;
        public event Action<string, PointerEventData> ReturnRequested;

        public void Bind(string identity, int count, Sprite sprite)
        {
            Identity = identity;
            if (selectedIcon != null)
            {
                selectedIcon.sprite = sprite;
                selectedIcon.preserveAspect = true;
                selectedIcon.raycastTarget = false;
                selectedIcon.color = Color.white;
                selectedIcon.gameObject.SetActive(sprite != null);
            }
            if (quantityText != null)
            {
                quantityText.text = count.ToString();
                quantityText.enableAutoSizing = true;
                quantityText.fontSizeMin = 8f;
                quantityText.fontSizeMax = 15f;
                quantityText.raycastTarget = false;
            }
        }

        public void OnDrop(PointerEventData eventData)
        {
            InventoryBagDragSlot source = eventData?.pointerDrag != null
                ? eventData.pointerDrag.GetComponent<InventoryBagDragSlot>() : null;
            if (source != null && source.DragIdentity != null)
                DropRequested?.Invoke(source);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData != null
                && eventData.button == PointerEventData.InputButton.Right)
                RemoveRequested?.Invoke(Identity);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData == null
                || eventData.button != PointerEventData.InputButton.Left
                || Identity == null
                || !InventoryDragPreview.TryCreate(this, "拖拽锻造材料",
                    out preview, out previewCanvas))
                return;
            draggingIdentity = Identity;
            preview.sizeDelta = ((RectTransform)transform).rect.size;
            Image image = preview.GetComponent<Image>();
            image.sprite = selectedIcon != null ? selectedIcon.sprite : null;
            image.color = image.sprite != null
                ? new Color(1f, 1f, 1f, 0.8f)
                : new Color(1f, 1f, 1f, 0.4f);
            OnDrag(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (eventData != null)
                InventoryDragPreview.Move(preview, previewCanvas, eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            string identity = draggingIdentity;
            ClearDrag();
            if (identity != null)
                ReturnRequested?.Invoke(identity, eventData);
        }

        private void OnDisable()
        {
            ClearDrag();
        }

        private void ClearDrag()
        {
            if (preview != null)
                Destroy(preview.gameObject);
            preview = null;
            previewCanvas = null;
            draggingIdentity = null;
        }
    }
}
