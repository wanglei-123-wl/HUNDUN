using UnityEngine;
using UnityEngine.EventSystems;

namespace Game.Cultivation.Presentation
{
    [DisallowMultipleComponent]
    public sealed class 修炼界面拖动区域 : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [SerializeField] private RectTransform[] movableParts;

        private Vector2 startPointer;
        private Vector2[] startPositions;
        private bool dragging;

        public void OnBeginDrag(PointerEventData eventData)
        {
            dragging = false;
            if (eventData == null
                || eventData.button != PointerEventData.InputButton.Left
                || eventData.pointerPressRaycast.gameObject != gameObject
                || movableParts == null || movableParts.Length == 0
                || !TryGetPointerPosition(eventData, out startPointer))
                return;

            Transform parent = transform.parent;
            var positions = new Vector2[movableParts.Length];
            for (int i = 0; i < movableParts.Length; i++)
            {
                RectTransform part = movableParts[i];
                if (part == null || part.parent != parent)
                    return;
                positions[i] = part.anchoredPosition;
            }
            startPositions = positions;
            dragging = true;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!dragging || eventData == null
                || eventData.button != PointerEventData.InputButton.Left
                || !TryGetPointerPosition(eventData, out Vector2 pointer))
                return;

            Vector2 delta = pointer - startPointer;
            for (int i = 0; i < movableParts.Length; i++)
                if (movableParts[i] != null)
                    movableParts[i].anchoredPosition = startPositions[i] + delta;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            dragging = false;
            startPositions = null;
        }

        private void OnDisable()
        {
            dragging = false;
            startPositions = null;
        }

        private bool TryGetPointerPosition(
            PointerEventData eventData, out Vector2 pointer)
        {
            pointer = default;
            RectTransform parent = transform.parent as RectTransform;
            return parent != null
                && RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parent, eventData.position, eventData.pressEventCamera,
                    out pointer);
        }
    }
}
