using UnityEngine;
using UnityEngine.UI;

namespace Game.Inventory
{
    [DisallowMultipleComponent]
    [AddComponentMenu("背包/导航栏选中效果")]
    public sealed class InventoryCategorySelectionView : MonoBehaviour
    {
        [SerializeField, InspectorName("背包面板")]
        private InventoryPanelView panel;
        [SerializeField, InspectorName("背包滚动区域")]
        private ScrollRect bagScroll;

        [SerializeField, InspectorName("全部按钮")]
        private Button allButton;
        [SerializeField, InspectorName("全部选中效果")]
        private GameObject allSelection;
        [SerializeField, InspectorName("装备按钮")]
        private Button equipmentButton;
        [SerializeField, InspectorName("装备选中效果")]
        private GameObject equipmentSelection;
        [SerializeField, InspectorName("消耗品按钮")]
        private Button consumableButton;
        [SerializeField, InspectorName("消耗品选中效果")]
        private GameObject consumableSelection;
        [SerializeField, InspectorName("丹药按钮")]
        private Button pillButton;
        [SerializeField, InspectorName("丹药选中效果")]
        private GameObject pillSelection;
        [SerializeField, InspectorName("其他按钮")]
        private Button otherButton;
        [SerializeField, InspectorName("其他选中效果")]
        private GameObject otherSelection;

        private bool resetScrollPending;

        private void OnEnable()
        {
            if (allButton != null)
                allButton.onClick.AddListener(HandleAllClicked);
            if (equipmentButton != null)
                equipmentButton.onClick.AddListener(HandleEquipmentClicked);
            if (consumableButton != null)
                consumableButton.onClick.AddListener(HandleConsumableClicked);
            if (pillButton != null)
                pillButton.onClick.AddListener(HandlePillClicked);
            if (otherButton != null)
                otherButton.onClick.AddListener(HandleOtherClicked);
            if (panel != null)
                panel.CategoryChanged += HandleCategoryChanged;

            HandleCategoryChanged(panel != null
                ? panel.CurrentCategory : InventoryItemCategory.All);
        }

        private void OnDisable()
        {
            resetScrollPending = false;
            if (allButton != null)
                allButton.onClick.RemoveListener(HandleAllClicked);
            if (equipmentButton != null)
                equipmentButton.onClick.RemoveListener(HandleEquipmentClicked);
            if (consumableButton != null)
                consumableButton.onClick.RemoveListener(HandleConsumableClicked);
            if (pillButton != null)
                pillButton.onClick.RemoveListener(HandlePillClicked);
            if (otherButton != null)
                otherButton.onClick.RemoveListener(HandleOtherClicked);
            if (panel != null)
                panel.CategoryChanged -= HandleCategoryChanged;
        }

        private void LateUpdate()
        {
            if (!resetScrollPending || bagScroll == null)
                return;

            resetScrollPending = false;
            Canvas.ForceUpdateCanvases();
            bagScroll.StopMovement();
            bagScroll.verticalNormalizedPosition = 1f;
        }

        private void HandleAllClicked() => SelectAndResetScroll(0);
        private void HandleEquipmentClicked() => SelectAndResetScroll(1);
        private void HandleConsumableClicked() => SelectAndResetScroll(2);
        private void HandlePillClicked() => SelectAndResetScroll(3);
        private void HandleOtherClicked() => SelectAndResetScroll(4);

        private void SelectAndResetScroll(int index)
        {
            SetSelected(index);
            resetScrollPending = true;
        }

        private void HandleCategoryChanged(InventoryItemCategory category)
        {
            switch (category)
            {
                case InventoryItemCategory.Equipment:
                    SetSelected(1);
                    break;
                case InventoryItemCategory.Consumable:
                    SetSelected(2);
                    break;
                case InventoryItemCategory.Pill:
                    SetSelected(3);
                    break;
                case InventoryItemCategory.Other:
                    SetSelected(4);
                    break;
                default:
                    SetSelected(0);
                    break;
            }
        }

        private void SetSelected(int index)
        {
            if (allSelection != null)
                allSelection.SetActive(index == 0);
            if (equipmentSelection != null)
                equipmentSelection.SetActive(index == 1);
            if (consumableSelection != null)
                consumableSelection.SetActive(index == 2);
            if (pillSelection != null)
                pillSelection.SetActive(index == 3);
            if (otherSelection != null)
                otherSelection.SetActive(index == 4);
        }
    }
}
