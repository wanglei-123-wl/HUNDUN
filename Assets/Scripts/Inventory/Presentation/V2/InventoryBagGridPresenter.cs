using System;
using System.Collections.Generic;
using Game.Cultivation.Protocol;
using Game.Cultivation.Runtime;
using Game.Inventory.Integration.V2;
using Game.Inventory.Model.V2;
using Game.Items;
using UnityEngine;

namespace Game.Inventory.Presentation.V2
{
    public sealed class InventoryBagGridPresenter : IDisposable
    {
        private readonly InventoryBagLocalController controller;
        private readonly InventoryGridView grid;
        private readonly InventoryPanelView panel;
        private readonly ItemDefinitionTable items;
        private readonly List<InventoryBagDragSlot> dragSlots = new List<InventoryBagDragSlot>();
        private InventoryOwnershipSnapshot snapshot;

        public event Action<int, int, string> UnequipRequested;
        public event Action<string> StackDoubleClicked;
        public event Action<string> WorldDropOffered;

        public InventoryBagGridPresenter(InventoryBagLocalController controller,
            InventoryPanelView panel, ItemDefinitionTable items)
        {
            this.controller = controller ?? throw new ArgumentNullException(nameof(controller));
            this.panel = panel ?? throw new ArgumentNullException(nameof(panel));
            grid = panel.GridView ?? throw new ArgumentException("Bag grid is missing.", nameof(panel));
            this.items = items;
            controller.LayoutChanged += HandleLayoutChanged;
            panel.CategoryChanged += HandleCategoryChanged;
        }

        public void ApplySnapshot(InventoryOwnershipSnapshot next)
        {
            if (next == null) throw new ArgumentNullException(nameof(next));
            InventoryBagLayout layout = controller.PrepareSnapshot(next);
            InventoryOwnershipSnapshot previous = snapshot;
            InventoryBagLayout previousLayout = controller.Layout;
            snapshot = next;
            try { controller.ApplyPreparedLayout(layout, true); }
            catch
            {
                snapshot = previous;
                controller.RestoreLayout(previousLayout);
                if (previous == null || previousLayout == null) grid.Clear();
                throw;
            }
        }

        public InventoryBagLayout CurrentLayout => controller.Layout;
        internal InventoryOwnershipSnapshot PresentedSnapshot => snapshot;

        public void PresentSnapshot(InventoryOwnershipSnapshot next, InventoryBagLayout layout)
        {
            if (next == null) throw new ArgumentNullException(nameof(next));
            snapshot = next;
            controller.ApplyPreparedLayout(layout, false);
        }

        public InventoryBagLayout PrepareSnapshot(InventoryOwnershipSnapshot next) =>
            controller.PrepareSnapshot(next);

        public void RestoreSnapshot(InventoryOwnershipSnapshot previous, InventoryBagLayout layout)
        {
            snapshot = previous;
            controller.RestoreLayout(layout);
            if (previous == null || layout == null) grid.Clear();
        }

        public void PersistLayout() => controller.PersistLayout();

        public void RefreshCatalog()
        {
            if (snapshot != null && controller.Layout != null)
                Render(controller.Layout);
        }

        public void Clear()
        {
            snapshot = null;
            foreach (InventoryBagDragSlot slot in dragSlots)
                if (slot != null) slot.enabled = false;
            grid.Clear();
        }

        public void Dispose()
        {
            controller.LayoutChanged -= HandleLayoutChanged;
            panel.CategoryChanged -= HandleCategoryChanged;
            foreach (InventoryBagDragSlot slot in dragSlots)
                if (slot != null)
                {
                    slot.DropRequested -= HandleDropRequested;
                    slot.UnequipRequested -= HandleUnequipRequested;
                    slot.WorldDropOffered -= HandleWorldDropOffered;
                    slot.GetComponent<InventorySlotView>().V2DoubleClicked -= HandleDoubleClick;
                }
            Clear();
        }

        private void HandleLayoutChanged(InventoryBagLayout layout)
        {
            Render(layout);
        }

        private void HandleCategoryChanged(InventoryItemCategory category)
        {
            if (controller.Layout != null) Render(controller.Layout);
        }

        private void Render(InventoryBagLayout layout)
        {
            if (snapshot == null || layout.CharacterDbId != snapshot.CharacterDbId) return;
            grid.EnsureCapacity(layout.Capacity);
            var stacks = new Dictionary<string, InventoryStackState>(StringComparer.Ordinal);
            var equipment = new Dictionary<string, InventoryEquipmentState>(StringComparer.Ordinal);
            foreach (InventoryStackState stack in snapshot.Stacks) stacks.Add(stack.Identity, stack);
            foreach (InventoryEquipmentState item in snapshot.Equipment)
                if (item.SlotCode == 0) equipment.Add(item.Identity, item);

            bool showAll = panel.CurrentCategory == InventoryItemCategory.All;
            int[] displayOrder = showAll ? null : InventoryBagDisplayOrder.Filter(
                layout, identity => MatchesCategory(identity, stacks, equipment));
            for (int index = 0; index < layout.Capacity; index++)
            {
                InventorySlotView view = grid.GetSlot(index);
                InventoryBagDragSlot drag = view.GetComponent<InventoryBagDragSlot>();
                if (drag == null)
                {
                    drag = view.gameObject.AddComponent<InventoryBagDragSlot>();
                    drag.DropRequested += HandleDropRequested;
                    drag.UnequipRequested += HandleUnequipRequested;
                    drag.WorldDropOffered += HandleWorldDropOffered;
                    view.V2DoubleClicked += HandleDoubleClick;
                    dragSlots.Add(drag);
                }

                int layoutIndex = showAll ? index : displayOrder[index];
                string identity = layoutIndex < layout.Capacity
                    ? layout.GetIdentity(layoutIndex) : null;
                bool visible = BindVisibleItem(view, identity, stacks, equipment,
                    out string itemId, out string displayName);
                if (!visible) view.Clear();
                bool allowEquipmentDrag = panel.CurrentCategory == InventoryItemCategory.Equipment;
                bool allowConsumableDrag =
                    panel.CurrentCategory == InventoryItemCategory.Consumable;
                drag.enabled = showAll || allowEquipmentDrag || allowConsumableDrag;
                drag.Bind(layoutIndex, visible ? identity : null,
                    visible ? itemId : null,
                    visible ? displayName : null,
                    view.ItemIcon,
                    panel.GetComponent<RectTransform>());
            }
            grid.RestoreV2Selection();
        }

        private bool MatchesCategory(
            string identity,
            Dictionary<string, InventoryStackState> stacks,
            Dictionary<string, InventoryEquipmentState> equipment)
        {
            if (panel.CurrentCategory == InventoryItemCategory.Pill
                || panel.CurrentCategory == InventoryItemCategory.Other)
                return false;
            if (panel.CurrentCategory == InventoryItemCategory.Equipment)
                return equipment.ContainsKey(identity);
            if (panel.CurrentCategory != InventoryItemCategory.Consumable
                || !stacks.TryGetValue(identity, out InventoryStackState stack))
                return false;

            CultivationItemDefinitionDto serverItem = null;
            CultivationSessionRuntime.Instance?.TryGetItem(stack.ItemId,
                out serverItem);
            return serverItem?.kind == "consumable"
                || serverItem?.kind == "pill";
        }

        private bool BindVisibleItem(InventorySlotView view, string identity,
            Dictionary<string, InventoryStackState> stacks,
            Dictionary<string, InventoryEquipmentState> equipment,
            out string itemId, out string displayName)
        {
            itemId = null;
            displayName = null;
            if (identity == null) return false;
            if (stacks.TryGetValue(identity, out InventoryStackState stack))
            {
                itemId = stack.ItemId;
                ItemDefinition itemDefinition = null;
                bool known = items != null && items.TryGet(stack.ItemId, out itemDefinition);
                displayName = known ? itemDefinition.DisplayName : null;
                CultivationItemDefinitionDto serverItem = null;
                CultivationSessionRuntime.Instance?.TryGetItem(stack.ItemId,
                    out serverItem);
                bool isPill = serverItem?.kind == "pill";
                if (panel.CurrentCategory != InventoryItemCategory.All
                    && (panel.CurrentCategory != InventoryItemCategory.Consumable
                        || (serverItem?.kind != "consumable" && !isPill)))
                    return false;
                view.BindV2(identity, known ? itemDefinition.Icon : null, stack.Count,
                    known ? itemDefinition : null, isPill);
                return true;
            }
            if (!equipment.TryGetValue(identity, out InventoryEquipmentState item)
                || panel.CurrentCategory == InventoryItemCategory.Consumable)
                return false;
            ItemDefinition equipmentDefinition = null;
            bool knownEquipment = items != null
                && items.TryGetByEquipmentId(item.EquipmentId, out equipmentDefinition);
            view.BindV2(identity, knownEquipment ? equipmentDefinition.Icon : null,
                1, knownEquipment ? equipmentDefinition : null);
            return true;
        }

        private void HandleDropRequested(int source, int target, string identity)
        {
            if (panel.CurrentCategory == InventoryItemCategory.All)
                controller.TryDrop(source, target, identity);
        }

        private void HandleDoubleClick(InventorySlotView view)
        {
            if (snapshot == null || view == null || view.V2Identity == null) return;
            foreach (InventoryStackState stack in snapshot.Stacks)
            {
                if (stack.Identity != view.V2Identity || stack.Count <= 0) continue;
                StackDoubleClicked?.Invoke(view.V2Identity);
                return;
            }
        }

        private void HandleWorldDropOffered(string identity)
        {
            if (snapshot == null || string.IsNullOrEmpty(identity)) return;
            WorldDropOffered?.Invoke(identity);
        }

        private void HandleUnequipRequested(
            int sourceSlotCode, int targetBagIndex, string identity)
        {
            if (panel.CurrentCategory == InventoryItemCategory.Pill
                || panel.CurrentCategory == InventoryItemCategory.Other
                || controller.Layout == null || targetBagIndex < 0
                || targetBagIndex >= controller.Layout.Capacity)
                return;
            UnequipRequested?.Invoke(sourceSlotCode, targetBagIndex, identity);
        }
    }
}
