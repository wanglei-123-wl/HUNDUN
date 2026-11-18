using System;
using Game.Items;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Game.UI.ItemDetail
{
    [DisallowMultipleComponent]
    public sealed class ItemDetailHoverTrigger :
        MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] private ItemDetailHoverService hoverService;

        private Func<ItemDetailData> detailProvider;
        private ItemDetailData boundDetail;

        public bool HasDetailSource =>
            detailProvider != null || boundDetail != null;

        public void Bind(ItemDetailData detail)
        {
            if (hoverService != null)
                hoverService.HideIfDetailChanged(this, detail);
            detailProvider = null;
            boundDetail = detail;
        }

        public void Bind(Func<ItemDetailData> provider)
        {
            if (hoverService != null)
                hoverService.HideIfDetailChanged(this, provider);
            detailProvider = provider;
            boundDetail = null;
        }

        public void BindItem(ItemDefinitionTable itemDefinitions, string itemId)
        {
            Bind(() =>
            {
                if (itemDefinitions == null
                    || !itemDefinitions.TryGetDetail(itemId,
                        out ItemDetailData detail))
                    return null;
                return detail;
            });
        }

        public void BindEquipment(
            ItemDefinitionTable itemDefinitions,
            string equipmentId)
        {
            Bind(() =>
            {
                if (itemDefinitions == null
                    || !itemDefinitions.TryGetEquipmentDetail(equipmentId,
                        out ItemDetailData detail))
                    return null;
                return detail;
            });
        }

        public void ClearBinding()
        {
            detailProvider = null;
            boundDetail = null;
            if (hoverService != null)
                hoverService.Hide(this);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData == null)
                return;

            if (eventData.button == PointerEventData.InputButton.Left
                && eventData.clickCount == 1)
                ShowClickedDetail(eventData.position);

            ForwardParentClick(eventData);
        }

        private void ShowClickedDetail(Vector2 screenPosition)
        {
            if (hoverService == null || !hoverService.isActiveAndEnabled)
                hoverService = ItemDetailHoverService.Active;
            if (hoverService == null)
                return;

            ItemDetailData detail = detailProvider == null
                ? boundDetail : detailProvider();
            if (detail == null || !detail.IsValid)
            {
                hoverService.Hide(this);
                return;
            }

            hoverService.Show(this, detail, screenPosition);
        }

        private void ForwardParentClick(PointerEventData eventData)
        {
            // A child trigger otherwise replaces its parent's click handler.
            MonoBehaviour[] components = GetComponents<MonoBehaviour>();
            for (int index = 0; index < components.Length; index++)
            {
                if (components[index] != this
                    && components[index] is IPointerClickHandler)
                    return;
            }

            if (transform.parent != null)
                ExecuteEvents.ExecuteHierarchy<IPointerClickHandler>(
                    transform.parent.gameObject, eventData,
                    ExecuteEvents.pointerClickHandler);
        }

        public void NotifyServiceHidden(ItemDetailHoverService service)
        {
            if (hoverService == service)
                hoverService = null;
        }

        private void OnDisable()
        {
            if (hoverService != null)
                hoverService.Hide(this);
        }
    }
}
