using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.UI
{
    [DisallowMultipleComponent]
    public sealed class 提示面板交互 : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [SerializeField] private Button 提示按钮;
        [SerializeField] private RectTransform 提示面板;
        [SerializeField] private Button 提示面板返回按钮;

        private Vector2 pointerOffset;
        private bool dragging;

        private void OnEnable()
        {
            if (提示按钮 != null)
                提示按钮.onClick.AddListener(切换面板);
            if (提示面板返回按钮 != null)
                提示面板返回按钮.onClick.AddListener(关闭面板);
        }

        private void OnDisable()
        {
            if (提示按钮 != null)
                提示按钮.onClick.RemoveListener(切换面板);
            if (提示面板返回按钮 != null)
                提示面板返回按钮.onClick.RemoveListener(关闭面板);
            dragging = false;
        }

        private void 切换面板()
        {
            if (提示面板 == null)
                return;
            dragging = false;
            提示面板.gameObject.SetActive(!提示面板.gameObject.activeSelf);
        }

        private void 关闭面板()
        {
            dragging = false;
            if (提示面板 != null)
                提示面板.gameObject.SetActive(false);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            dragging = false;
            if (eventData == null
                || eventData.button != PointerEventData.InputButton.Left
                || 提示面板 == null || !提示面板.gameObject.activeInHierarchy)
                return;

            Transform pressed = eventData.pointerPressRaycast.gameObject?.transform;
            if (pressed == null || !pressed.IsChildOf(提示面板)
                || (提示面板返回按钮 != null
                    && pressed.IsChildOf(提示面板返回按钮.transform)))
                return;

            if (TryGetPointerPosition(eventData, out Vector2 pointer))
            {
                pointerOffset = 提示面板.anchoredPosition - pointer;
                dragging = true;
            }
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!dragging || eventData == null
                || eventData.button != PointerEventData.InputButton.Left
                || 提示面板 == null || !提示面板.gameObject.activeInHierarchy
                || !TryGetPointerPosition(eventData, out Vector2 pointer))
                return;

            提示面板.anchoredPosition = pointer + pointerOffset;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            dragging = false;
        }

        private bool TryGetPointerPosition(
            PointerEventData eventData, out Vector2 pointer)
        {
            pointer = default;
            RectTransform parent = 提示面板.parent as RectTransform;
            return parent != null
                && RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    parent, eventData.position, eventData.pressEventCamera,
                    out pointer);
        }
    }
}
