using System;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Inventory
{
    [DisallowMultipleComponent]
    public sealed class InventoryPanelView : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private InventoryGridView gridView;
        [SerializeField] private Button closeButton;

        [Header("Category Buttons")]
        [SerializeField] private Button allButton;
        [SerializeField] private Button equipmentButton;
        [SerializeField] private Button consumableButton;
        [SerializeField] private Button pillButton;
        [SerializeField] private Button otherButton;

        [Header("Optional State")]
        [SerializeField] private GameObject loadingRoot;

        private bool isLoading;

        public static InventoryPanelView ActivePanel { get; private set; }
        public InventoryGridView GridView => gridView;
        public InventoryItemCategory CurrentCategory { get; private set; } =
            InventoryItemCategory.All;
        public bool IsLoading => isLoading;

        public event Action CloseClicked;
        public event Action<InventoryItemCategory> CategoryChanged;

        private void Awake()
        {
            SetLoading(false);
            RefreshCategoryButtons();
        }

        private void OnEnable()
        {
            ActivePanel = this;
            AddButtonListeners();
            RefreshCategoryButtons();
        }

        private void OnDisable()
        {
            if (ActivePanel == this) ActivePanel = null;
            RemoveButtonListeners();
        }

        public void Show()
        {
            gameObject.SetActive(true);
        }

        public void Hide()
        {
            gameObject.SetActive(false);
        }

        public void SetCategory(InventoryItemCategory category)
        {
            SetCategory(category, notify: false);
        }

        public void SetCategory(
            InventoryItemCategory category,
            bool notify)
        {
            InventoryItemCategory normalizedCategory =
                NormalizeSupportedCategory(category);
            bool changed = CurrentCategory != normalizedCategory;

            CurrentCategory = normalizedCategory;
            RefreshCategoryButtons();

            if (notify && changed)
            {
                CategoryChanged?.Invoke(CurrentCategory);
            }
        }

        public void SetLoading(bool loading)
        {
            isLoading = loading;

            if (loadingRoot != null)
            {
                loadingRoot.SetActive(loading);
            }

            RefreshCategoryButtons();
        }

        private void AddButtonListeners()
        {
            if (closeButton != null)
            {
                closeButton.onClick.AddListener(HandleCloseClicked);
            }

            if (allButton != null)
            {
                allButton.onClick.AddListener(HandleAllClicked);
            }

            if (equipmentButton != null)
            {
                equipmentButton.onClick.AddListener(HandleEquipmentClicked);
            }

            if (consumableButton != null)
            {
                consumableButton.onClick.AddListener(HandleConsumableClicked);
            }

            if (pillButton != null)
            {
                pillButton.onClick.AddListener(HandlePillClicked);
            }

            if (otherButton != null)
            {
                otherButton.onClick.AddListener(HandleOtherClicked);
            }
        }

        private void RemoveButtonListeners()
        {
            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(HandleCloseClicked);
            }

            if (allButton != null)
            {
                allButton.onClick.RemoveListener(HandleAllClicked);
            }

            if (equipmentButton != null)
            {
                equipmentButton.onClick.RemoveListener(HandleEquipmentClicked);
            }

            if (consumableButton != null)
            {
                consumableButton.onClick.RemoveListener(HandleConsumableClicked);
            }

            if (pillButton != null)
            {
                pillButton.onClick.RemoveListener(HandlePillClicked);
            }

            if (otherButton != null)
            {
                otherButton.onClick.RemoveListener(HandleOtherClicked);
            }
        }

        private void HandleCloseClicked()
        {
            CloseClicked?.Invoke();
        }

        private void HandleAllClicked()
        {
            SetCategory(InventoryItemCategory.All, notify: true);
        }

        private void HandleEquipmentClicked()
        {
            SetCategory(InventoryItemCategory.Equipment, notify: true);
        }

        private void HandleConsumableClicked()
        {
            SetCategory(InventoryItemCategory.Consumable, notify: true);
        }

        private void HandlePillClicked()
        {
            SetCategory(InventoryItemCategory.Pill, notify: true);
        }

        private void HandleOtherClicked()
        {
            SetCategory(InventoryItemCategory.Other, notify: true);
        }

        private void RefreshCategoryButtons()
        {
            RefreshCategoryButton(allButton);
            RefreshCategoryButton(equipmentButton);
            RefreshCategoryButton(consumableButton);
            RefreshCategoryButton(pillButton);
            RefreshCategoryButton(otherButton);
        }

        private void RefreshCategoryButton(Button button)
        {
            if (button == null)
            {
                return;
            }

            button.interactable = !isLoading;
        }

        private static InventoryItemCategory NormalizeSupportedCategory(
            InventoryItemCategory category)
        {
            switch (category)
            {
                case InventoryItemCategory.Equipment:
                case InventoryItemCategory.Consumable:
                case InventoryItemCategory.Pill:
                case InventoryItemCategory.Other:
                    return category;
                default:
                    return InventoryItemCategory.All;
            }
        }
    }
}
