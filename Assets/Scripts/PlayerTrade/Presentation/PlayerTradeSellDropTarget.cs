using System;
using Game.Inventory.Presentation.V2;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.PlayerTrade.Presentation
{
    [DisallowMultipleComponent]
    public sealed class PlayerTradeSellDropTarget : MonoBehaviour, IDropHandler
    {
        private Action<InventoryBagDragSlot> itemDropped;

        public void SetDropHandler(Action<InventoryBagDragSlot> handler)
        {
            itemDropped = handler;
            EnsureRaycastTarget();
        }

        public void OnDrop(PointerEventData eventData)
        {
            if (eventData == null || eventData.pointerDrag == null)
                return;

            InventoryBagDragSlot source =
                eventData.pointerDrag.GetComponent<InventoryBagDragSlot>();
            if (source == null || string.IsNullOrWhiteSpace(source.DragIdentity))
                return;

            itemDropped?.Invoke(source);
        }

        private void Awake()
        {
            EnsureRaycastTarget();
        }

        private void EnsureRaycastTarget()
        {
            Graphic graphic = GetComponent<Graphic>();
            if (graphic != null)
            {
                graphic.raycastTarget = true;
                return;
            }

            Image image = gameObject.AddComponent<Image>();
            image.color = new Color(0f, 0f, 0f, 0f);
            image.raycastTarget = true;
        }
    }
}
