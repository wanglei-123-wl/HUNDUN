using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.Inventory.Presentation.V2
{
    internal static class InventoryDragPreview
    {
        private const string OverlayName = "拖拽显示层";

        public static bool TryCreate(Component source, string name,
            out RectTransform preview, out Canvas overlay)
        {
            preview = null;
            overlay = null;
            foreach (Canvas canvas in Object.FindObjectsOfType<Canvas>())
            {
                if (canvas.name != OverlayName || canvas.rootCanvas != canvas
                    || canvas.gameObject.scene != source.gameObject.scene)
                    continue;

                if (overlay != null)
                {
                    Debug.LogWarning("Multiple active drag display canvases were found.");
                    overlay = null;
                    return false;
                }
                overlay = canvas;
            }

            if (overlay == null)
            {
                Debug.LogWarning("Drag display canvas was not found in the current scene.");
                return false;
            }

            var gameObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            preview = (RectTransform)gameObject.transform;
            preview.SetParent(overlay.transform, false);
            preview.SetAsLastSibling();
            Image image = gameObject.GetComponent<Image>();
            image.preserveAspect = true;
            image.raycastTarget = false;
            return true;
        }

        public static void Move(RectTransform preview, Canvas overlay,
            PointerEventData eventData)
        {
            if (preview == null || overlay == null) return;
            Camera camera = overlay.renderMode == RenderMode.ScreenSpaceOverlay
                ? null : overlay.worldCamera;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                (RectTransform)overlay.transform, eventData.position, camera, out Vector2 point))
                preview.anchoredPosition = point;
        }
    }
}
