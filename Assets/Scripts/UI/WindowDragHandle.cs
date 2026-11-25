using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.UI
{
    [DisallowMultipleComponent]
    public class WindowDragHandle : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [SerializeField, InspectorName("拖动的界面")] protected RectTransform panelRoot;

        private RectTransform dragSpace;
        private RectTransform[] draggedParts;
        private Vector2 startPointer;
        private Vector2[] startPositions;
        private bool dragging;

        public void OnBeginDrag(PointerEventData eventData)
        {
            ResetDrag();
            GameObject pressed = eventData?.pointerPressRaycast.gameObject;
            if (eventData == null
                || eventData.button != PointerEventData.InputButton.Left
                || pressed == null || !pressed.transform.IsChildOf(transform)
                || pressed.GetComponentInParent<Selectable>() != null)
                return;

            // Existing scene instances can resolve their immediate window parent.
            if (panelRoot == null) panelRoot = transform.parent as RectTransform;
            if (panelRoot == null || !transform.IsChildOf(panelRoot)) return;

            Canvas canvas = panelRoot.GetComponent<Canvas>();
            bool canvasRoot = canvas != null && canvas.isRootCanvas
                && canvas.renderMode != RenderMode.WorldSpace;
            dragSpace = canvasRoot ? panelRoot : panelRoot.parent as RectTransform;
            if (dragSpace == null || !TryGetPointerPosition(eventData, out startPointer)) return;

            // Unity drives a screen-space Canvas root. Its children move together;
            // an ordinary window container can move as a single RectTransform.
            int count = canvasRoot ? panelRoot.childCount : 1;
            if (count == 0) return;
            var parts = new RectTransform[count];
            var positions = new Vector2[count];
            for (int index = 0; index < count; index++)
            {
                RectTransform part = canvasRoot ? panelRoot.GetChild(index) as RectTransform : panelRoot;
                if (part == null) return;
                parts[index] = part;
                positions[index] = part.anchoredPosition;
            }
            draggedParts = parts;
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
            for (int index = 0; index < draggedParts.Length; index++)
                if (draggedParts[index] != null)
                    draggedParts[index].anchoredPosition = startPositions[index] + delta;
        }

        public void OnEndDrag(PointerEventData eventData) => ResetDrag();

        protected void OnDisable() => ResetDrag();

        private void ResetDrag()
        {
            dragging = false;
            dragSpace = null;
            draggedParts = null;
            startPositions = null;
        }

        private bool TryGetPointerPosition(PointerEventData eventData, out Vector2 pointer)
        {
            pointer = default;
            return dragSpace != null
                && RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    dragSpace, eventData.position, eventData.pressEventCamera, out pointer);
        }
    }
}
