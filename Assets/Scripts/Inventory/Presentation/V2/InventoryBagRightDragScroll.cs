using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.Inventory.Presentation.V2
{
    [RequireComponent(typeof(ScrollRect))]
    public sealed class InventoryBagRightDragScroll : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private ScrollRect scrollRect;
        private bool dragging;
        private int pointerId;
        private float startPointerY;
        private float startScrollPosition;

        private void Awake()
        {
            scrollRect = GetComponent<ScrollRect>();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData == null || eventData.button != PointerEventData.InputButton.Right
                || scrollRect == null)
                return;

            dragging = false;
            if (!scrollRect.IsActive() || !scrollRect.vertical || scrollRect.content == null)
                return;

            if (!TryGetPointerY(eventData, out startPointerY))
                return;

            scrollRect.StopMovement();
            startScrollPosition = scrollRect.verticalNormalizedPosition;
            pointerId = eventData.pointerId;
            dragging = true;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!dragging || eventData == null
                || eventData.button != PointerEventData.InputButton.Right
                || eventData.pointerId != pointerId || scrollRect == null
                || !scrollRect.IsActive() || scrollRect.content == null)
                return;

            RectTransform viewport = GetViewport();
            if (viewport == null || !TryGetPointerY(eventData, out float pointerY))
                return;

            Bounds contentBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(
                viewport, scrollRect.content);
            float scrollableHeight = contentBounds.size.y - viewport.rect.height;
            if (scrollableHeight <= 0f)
                return;

            scrollRect.verticalNormalizedPosition = Mathf.Clamp01(
                startScrollPosition - (pointerY - startPointerY) / scrollableHeight);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (eventData != null && eventData.button == PointerEventData.InputButton.Right
                && eventData.pointerId == pointerId)
                dragging = false;
        }

        private RectTransform GetViewport()
        {
            return scrollRect == null ? null
                : scrollRect.viewport != null
                    ? scrollRect.viewport
                    : scrollRect.GetComponent<RectTransform>();
        }

        private bool TryGetPointerY(PointerEventData eventData, out float pointerY)
        {
            pointerY = 0f;
            RectTransform viewport = GetViewport();
            if (viewport == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                viewport, eventData.position, eventData.pressEventCamera, out Vector2 localPoint))
                return false;

            pointerY = localPoint.y;
            return true;
        }

        private void OnDisable()
        {
            dragging = false;
        }
    }
}
