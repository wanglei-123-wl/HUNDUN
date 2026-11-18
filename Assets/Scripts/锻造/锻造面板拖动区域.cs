using UnityEngine;
using UnityEngine.EventSystems;

namespace Game.Forging.Presentation
{
    [DisallowMultipleComponent]
    public sealed class 锻造面板拖动区域 : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [SerializeField] private RectTransform panel;

        private Vector2 pointerOffset;
        private bool dragging;

        public void OnBeginDrag(PointerEventData eventData)
        {
            dragging = false;
            if (eventData == null
                || eventData.button != PointerEventData.InputButton.Left
                || eventData.pointerPressRaycast.gameObject != gameObject
                || panel == null || !panel.gameObject.activeInHierarchy
                || !TryGetPointerPosition(eventData, out Vector2 pointer))
                return;

            pointerOffset = panel.anchoredPosition - pointer;
            dragging = true;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!dragging || eventData == null
                || eventData.button != PointerEventData.InputButton.Left
                || panel == null || !panel.gameObject.activeInHierarchy
                || !TryGetPointerPosition(eventData, out Vector2 pointer))
                return;

            panel.anchoredPosition = pointer + pointerOffset;
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
            pointer = default;
            RectTransform parent = panel.parent as RectTransform;
            return parent != null
                && RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parent, eventData.position, eventData.pressEventCamera,
                    out pointer);
        }
    }
}
