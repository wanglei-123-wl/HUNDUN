using System;
using System.Collections.Generic;
using Game.Items;
using UnityEngine;

namespace Game.Inventory
{
    [DisallowMultipleComponent]
    public sealed class InventoryGridView : MonoBehaviour
    {
        [SerializeField] private RectTransform contentRoot;
        [SerializeField] private InventorySlotView slotPrefab;

        private readonly List<InventorySlotView> slotViews =
            new List<InventorySlotView>();

        private int activeCapacity;
        private string selectedItemInstanceId;
        private string selectedV2Identity;

        public int ActiveCapacity => activeCapacity;
        public int CreatedSlotCount => slotViews.Count;
        public string SelectedItemInstanceId => selectedItemInstanceId;
        public InventorySlotView SelectedV2Slot
        {
            get
            {
                if (string.IsNullOrEmpty(selectedV2Identity))
                    return null;
                for (int index = 0; index < activeCapacity; index++)
                    if (string.Equals(selectedV2Identity, slotViews[index].V2Identity,
                            StringComparison.Ordinal))
                        return slotViews[index];
                return null;
            }
        }

        public event Action<InventorySlotView> SlotClicked;
        public event Action<InventorySlotView> V2SelectionChanged;

        private void Awake()
        {
            if (contentRoot == null)
            {
                contentRoot = GetComponent<RectTransform>();
            }
        }

        private void OnDestroy()
        {
            for (int index = 0; index < slotViews.Count; index++)
            {
                InventorySlotView slotView = slotViews[index];
                if (slotView != null)
                {
                    slotView.Clicked -= HandleSlotClicked;
                }
            }
        }

        public void EnsureCapacity(int capacity)
        {
            capacity = Math.Max(0, capacity);

            if (contentRoot == null)
            {
                throw new InvalidOperationException(
                    "Inventory grid content root is missing.");
            }

            if (slotPrefab == null)
            {
                throw new InvalidOperationException(
                    "Inventory slot prefab is missing.");
            }

            while (slotViews.Count < capacity)
            {
                CreateSlot(slotViews.Count);
            }

            activeCapacity = capacity;

            for (int index = 0; index < slotViews.Count; index++)
            {
                bool active = index < activeCapacity;
                InventorySlotView slotView = slotViews[index];
                slotView.gameObject.SetActive(active);

                if (!active)
                {
                    slotView.Clear();
                }
            }
        }

        public void RefreshServerSlots(
            int capacity,
            IReadOnlyList<InventoryItemState> items,
            ItemDefinitionTable itemDefinitions)
        {
            EnsureCapacity(capacity);
            ClearVisibleSlots(clearSelection: false);

            bool selectionFound =
                BindItemsToServerSlots(items, itemDefinitions);

            if (!selectionFound)
            {
                selectedItemInstanceId = null;
            }
        }

        public void RefreshCompact(
            int capacity,
            IReadOnlyList<InventoryItemState> items,
            ItemDefinitionTable itemDefinitions)
        {
            EnsureCapacity(capacity);
            ClearVisibleSlots(clearSelection: false);

            bool selectionFound = BindItemsCompact(items, itemDefinitions);

            if (!selectionFound)
            {
                selectedItemInstanceId = null;
            }
        }

        public InventorySlotView GetSlot(int displayIndex)
        {
            if (displayIndex < 0 || displayIndex >= activeCapacity)
            {
                return null;
            }

            return slotViews[displayIndex];
        }

        public void Clear()
        {
            ClearVisibleSlots(clearSelection: true);
        }

        public void ClearSelection()
        {
            selectedItemInstanceId = null;
            selectedV2Identity = null;

            for (int index = 0; index < activeCapacity; index++)
            {
                slotViews[index].SetSelected(false);
            }
            V2SelectionChanged?.Invoke(null);
        }

        public void RestoreV2Selection()
        {
            InventorySlotView selectedSlot = null;
            for (int index = 0; index < activeCapacity; index++)
            {
                InventorySlotView slotView = slotViews[index];
                bool selected = !string.IsNullOrEmpty(selectedV2Identity)
                    && string.Equals(selectedV2Identity, slotView.V2Identity,
                        StringComparison.Ordinal);
                slotView.SetSelected(selected);
                if (selected) selectedSlot = slotView;
            }

            if (selectedSlot == null) selectedV2Identity = null;
            V2SelectionChanged?.Invoke(selectedSlot);
        }

        private void CreateSlot(int displayIndex)
        {
            InventorySlotView slotView = Instantiate(
                slotPrefab,
                contentRoot,
                false);
            slotView.name = "InventorySlot_" + displayIndex.ToString("D2");
            slotView.Initialize(displayIndex);
            slotView.Clicked += HandleSlotClicked;
            slotViews.Add(slotView);
        }

        private bool BindItemsToServerSlots(
            IReadOnlyList<InventoryItemState> items,
            ItemDefinitionTable itemDefinitions)
        {
            bool selectionFound = false;
            if (items == null)
            {
                return false;
            }

            for (int index = 0; index < items.Count; index++)
            {
                InventoryItemState item = items[index];
                if (item == null
                    || item.SlotIndex < 0
                    || item.SlotIndex >= activeCapacity)
                {
                    continue;
                }

                InventorySlotView slotView = slotViews[item.SlotIndex];
                BindSlot(slotView, item, itemDefinitions);
                selectionFound |= RestoreSelection(slotView);
            }

            return selectionFound;
        }

        private bool BindItemsCompact(
            IReadOnlyList<InventoryItemState> items,
            ItemDefinitionTable itemDefinitions)
        {
            bool selectionFound = false;
            int displayIndex = 0;

            if (items == null)
            {
                return false;
            }

            for (int index = 0;
                index < items.Count && displayIndex < activeCapacity;
                index++)
            {
                InventoryItemState item = items[index];
                if (item == null)
                {
                    continue;
                }

                InventorySlotView slotView = slotViews[displayIndex];
                BindSlot(slotView, item, itemDefinitions);
                selectionFound |= RestoreSelection(slotView);
                displayIndex++;
            }

            return selectionFound;
        }

        private static void BindSlot(
            InventorySlotView slotView,
            InventoryItemState item,
            ItemDefinitionTable itemDefinitions)
        {
            ItemDefinition definition = itemDefinitions == null
                ? null
                : itemDefinitions.GetOrDefault(item.ItemId);
            slotView.Bind(item, definition);
        }

        private bool RestoreSelection(InventorySlotView slotView)
        {
            bool selected = !string.IsNullOrWhiteSpace(selectedItemInstanceId)
                && string.Equals(
                    selectedItemInstanceId,
                    slotView.ItemInstanceId,
                    StringComparison.Ordinal);
            slotView.SetSelected(selected);
            return selected;
        }

        private void ClearVisibleSlots(bool clearSelection)
        {
            for (int index = 0; index < activeCapacity; index++)
            {
                slotViews[index].Clear();
            }

            if (clearSelection)
            {
                selectedItemInstanceId = null;
                selectedV2Identity = null;
                V2SelectionChanged?.Invoke(null);
            }
        }

        private void HandleSlotClicked(InventorySlotView clickedSlot)
        {
            if (clickedSlot == null || !clickedSlot.HasItem)
            {
                return;
            }

            for (int index = 0; index < activeCapacity; index++)
            {
                InventorySlotView slotView = slotViews[index];
                slotView.SetSelected(slotView == clickedSlot);
            }

            selectedItemInstanceId = clickedSlot.Item == null
                ? null : clickedSlot.ItemInstanceId;
            selectedV2Identity = clickedSlot.V2Identity;
            V2SelectionChanged?.Invoke(clickedSlot.V2Identity == null
                ? null : clickedSlot);
            if (clickedSlot.Item != null)
                SlotClicked?.Invoke(clickedSlot);
        }
    }
}
