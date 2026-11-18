using UnityEngine;
using UnityEngine.EventSystems;

namespace Game.UI.ItemDetail
{
    [DisallowMultipleComponent]
    public sealed class ItemDetailPanelDragHandle :
        MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler,
        IPointerClickHandler
    {
        private ItemDetailPanelController panel;
        private Vector2 pointerOffset;
        private bool dragging;

        public void Initialize(ItemDetailPanelController owner)
        {
            panel = owner;
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData == null || panel == null || !panel.IsVisible)
                return;

            if (eventData.button == PointerEventData.InputButton.Right)
                panel.RequestClose();
            else if (eventData.button == PointerEventData.InputButton.Left)
                panel.transform.SetAsLastSibling();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            dragging = false;
            if (eventData == null
                || eventData.button != PointerEventData.InputButton.Left
                || panel == null || !panel.IsVisible
                || !TryGetPointerPosition(eventData, out Vector2 pointer))
                return;

            pointerOffset = (Vector2)panel.PanelRect.localPosition - pointer;
            dragging = true;
            panel.transform.SetAsLastSibling();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!dragging || eventData == null
                || eventData.button != PointerEventData.InputButton.Left
                || panel == null || !panel.IsVisible
                || !TryGetPointerPosition(eventData, out Vector2 pointer))
                return;

            panel.SetPanelLocalPosition(pointer + pointerOffset);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            dragging = false;
        }

        private void OnDisable()
        {
            dragging = false;
        }

        private bool TryGetPointerPosition(
            PointerEventData eventData, out Vector2 pointer)
        {
            RectTransform parent = panel.PanelRect == null
                ? null : panel.PanelRect.parent as RectTransform;
            pointer = default;
            return parent != null
                && RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parent, eventData.position, eventData.pressEventCamera,
                    out pointer);
        }
    }
}
