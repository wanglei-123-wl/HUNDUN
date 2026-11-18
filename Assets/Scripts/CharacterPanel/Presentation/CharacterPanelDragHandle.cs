using UnityEngine;
using UnityEngine.EventSystems;

namespace Game.CharacterPanel.Presentation
{
    [DisallowMultipleComponent]
    public sealed class CharacterPanelDragHandle : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [SerializeField] private RectTransform panel;
        [SerializeField] private RectTransform secondaryHandle;

        private Vector2 pointerOffset;
        private bool dragging;

        public void OnBeginDrag(PointerEventData eventData)
        {
            dragging = false;
            if (eventData == null
                || eventData.button != PointerEventData.InputButton.Left
                || panel == null || !panel.gameObject.activeInHierarchy)
                return;

            Transform pressed = eventData.pointerPressRaycast.gameObject?.transform;
            if (pressed == null || (pressed != transform && pressed != secondaryHandle))
                return;

            if (!TryGetPointerPosition(eventData, out Vector2 pointer))
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
