using System;
using System.Globalization;
using Game.Cultivation.Protocol;
using Game.Cultivation.Runtime;
using Game.Inventory;
using Game.Inventory.Model.V2;
using Game.Inventory.Presentation.V2;
using Game.Items;
using Game.UI.Notice;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.Cultivation.Presentation
{
    [DisallowMultipleComponent]
    public sealed class CultivationMaterialDropTarget : MonoBehaviour,
        IDropHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        [SerializeField] private GameSession session;
        [SerializeField] private InventoryModuleRuntime inventory;
        [SerializeField] private Image materialIcon;
        [SerializeField] private TMP_Text totalTimeText;
        [SerializeField] private TMP_Text cultivationPreviewText;

        private NetworkManager network;
        private RefiningSessionRuntime refining;
        private RefiningQuantityConfirmationView quantityView;
        private string selectedIdentity;
        private string selectedItemId;
        private int selectedCount;
        private long selectedConfigurationRevision;
        private string pendingIdentity;
        private string pendingItemId;
        private long pendingConfigurationRevision;
        private string draggingIdentity;
        private RectTransform dragPreview;
        private Canvas dragCanvas;

        public int SelectedCount => selectedCount;
        public string SelectedItemId => selectedItemId;

        public void ClearSelectedMaterial()
        {
            if (quantityView != null)
                quantityView.Hide();
            pendingIdentity = null;
            pendingItemId = null;
            pendingConfigurationRevision = 0;
            CancelDrag();
            ClearSelection();
        }

        public bool TryGetSelection(out long stackId, out int count,
            out long configurationRevision)
        {
            stackId = 0;
            count = 0;
            configurationRevision = 0;
            if (selectedIdentity == null || selectedCount <= 0
                || !TryFindSelectedStack(inventory != null
                    ? inventory.CurrentSnapshot : null, selectedIdentity,
                    selectedItemId, out InventoryStackState stack)
                || stack.Count < selectedCount
                || !TryFindRule(selectedItemId, out RefiningRuleDto rule)
                || refining.Catalog.configurationRevision
                    != selectedConfigurationRevision
                || !RefiningPreviewCalculator.TryCalculate(rule,
                    selectedCount, out _, out _))
                return false;
            stackId = stack.StackId;
            count = selectedCount;
            configurationRevision = selectedConfigurationRevision;
            return true;
        }

        private void OnEnable()
        {
            if (session == null)
                session = FindObjectOfType<GameSession>();
            if (inventory == null)
                inventory = FindObjectOfType<InventoryModuleRuntime>();
            if (session != null)
                session.LocalWorldEntered += HandleWorldEntered;
            network = NetworkManager.Instance;
            if (network != null)
                network.OnDisconnected += HandleDisconnected;
            BindRefining();
            ClearSelection();
        }

        private void OnDisable()
        {
            if (session != null)
                session.LocalWorldEntered -= HandleWorldEntered;
            if (network != null)
                network.OnDisconnected -= HandleDisconnected;
            network = null;
            UnbindRefining();
            if (quantityView != null)
                quantityView.Hide();
            pendingIdentity = null;
            pendingItemId = null;
            pendingConfigurationRevision = 0;
            CancelDrag();
            ClearSelection();
        }

        private void OnDestroy()
        {
            if (quantityView == null)
                return;
            quantityView.Confirmed -= HandleQuantityConfirmed;
            quantityView.Dispose();
            quantityView = null;
        }

        private void Update()
        {
            BindRefining();
            if (selectedIdentity == null)
                return;
            if (!TryGetSelection(out _, out _, out _))
                ClearSelection();
        }

        public void OnDrop(PointerEventData eventData)
        {
            if (eventData == null || eventData.pointerDrag == null
                || inventory == null || session == null || materialIcon == null)
                return;

            InventoryBagDragSlot source =
                eventData.pointerDrag.GetComponent<InventoryBagDragSlot>();
            if (source == null || string.IsNullOrEmpty(source.DragIdentity))
                return;

            InventoryOwnershipSnapshot snapshot = inventory.CurrentSnapshot;
            if (!TryFindSelectedStack(snapshot, source.DragIdentity,
                source.ItemId, out InventoryStackState stack))
                return;

            BindRefining();
            if (refining != null && refining.Snapshot != null
                && refining.Snapshot.activeTask != null)
            {
                GameNoticeService.Show("已有炼化任务");
                return;
            }
            if (!TryFindRule(stack.ItemId, out RefiningRuleDto dropRule))
            {
                GameNoticeService.Show(refining == null
                    || refining.Catalog == null ? "炼化目录尚未同步" : "该物品不可炼化");
                if (refining != null && refining.Catalog == null)
                    refining.RequestCatalog();
                return;
            }
            ItemDefinitionTable definitions = inventory.ItemDefinitions;
            if (definitions == null
                || !definitions.TryGet(stack.ItemId, out ItemDefinition definition)
                || definition.Icon == null)
                return;

            if (quantityView == null)
            {
                Canvas canvas = GetComponentInParent<Canvas>(true);
                quantityView = RefiningQuantityConfirmationView.Create(canvas,
                    totalTimeText != null ? totalTimeText.font : null);
                if (quantityView != null)
                    quantityView.Confirmed += HandleQuantityConfirmed;
            }
            if (quantityView == null)
                return;
            pendingIdentity = stack.Identity;
            pendingItemId = stack.ItemId;
            pendingConfigurationRevision = refining.Catalog.configurationRevision;
            quantityView.Show(pendingIdentity,
                string.IsNullOrWhiteSpace(definition.DisplayName)
                    ? stack.ItemId : definition.DisplayName, stack.Count,
                amount => RefiningPreviewCalculator.TryCalculate(dropRule,
                    amount, out _, out _));
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (eventData == null
                || eventData.button != PointerEventData.InputButton.Left
                || materialIcon == null || materialIcon.sprite == null
                || selectedIdentity == null
                || !TryFindSelectedStack(inventory != null
                    ? inventory.CurrentSnapshot : null,
                    selectedIdentity, selectedItemId, out _)
                || !InventoryDragPreview.TryCreate(this, "拖拽炼化材料",
                    out dragPreview, out dragCanvas))
                return;

            draggingIdentity = selectedIdentity;
            dragPreview.sizeDelta = materialIcon.rectTransform.rect.size;
            Image previewImage = dragPreview.GetComponent<Image>();
            previewImage.sprite = materialIcon.sprite;
            previewImage.color = new Color(1f, 1f, 1f, 0.8f);
            OnDrag(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (draggingIdentity != null && eventData != null)
                InventoryDragPreview.Move(dragPreview, dragCanvas, eventData);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            GameObject hit = eventData != null
                ? eventData.pointerCurrentRaycast.gameObject : null;
            InventoryPanelView panel = hit != null
                ? hit.GetComponentInParent<InventoryPanelView>() : null;
            bool returnedToBag = draggingIdentity != null
                && selectedIdentity == draggingIdentity
                && panel != null && panel == InventoryPanelView.ActivePanel;
            CancelDrag();
            if (returnedToBag)
                ClearSelection();
        }

        private bool TryFindSelectedStack(InventoryOwnershipSnapshot snapshot,
            string identity, string itemId, out InventoryStackState found)
        {
            found = null;
            if (session == null || session.LocalCharacterDbId <= 0
                || snapshot == null
                || snapshot.CharacterDbId != session.LocalCharacterDbId)
                return false;

            foreach (InventoryStackState stack in snapshot.Stacks)
            {
                if (stack.Count > 0
                    && string.Equals(stack.Identity, identity, StringComparison.Ordinal)
                    && string.Equals(stack.ItemId, itemId, StringComparison.Ordinal))
                {
                    found = stack;
                    return true;
                }
            }
            return false;
        }

        private void HandleWorldEntered(long epoch, long characterDbId)
        {
            if (quantityView != null)
                quantityView.Hide();
            pendingIdentity = null;
            pendingItemId = null;
            pendingConfigurationRevision = 0;
            ClearSelection();
        }

        private void HandleDisconnected(string reason)
        {
            if (quantityView != null)
                quantityView.Hide();
            pendingIdentity = null;
            pendingItemId = null;
            pendingConfigurationRevision = 0;
            ClearSelection();
        }

        private void HandleQuantityConfirmed(string identity, int amount)
        {
            if (!string.Equals(identity, pendingIdentity, StringComparison.Ordinal)
                || !TryFindSelectedStack(inventory != null
                    ? inventory.CurrentSnapshot : null, identity,
                    pendingItemId, out InventoryStackState stack)
                || amount <= 0 || amount > stack.Count
                || !TryFindRule(stack.ItemId, out RefiningRuleDto rule)
                || refining.Catalog.configurationRevision
                    != pendingConfigurationRevision
                || !RefiningPreviewCalculator.TryCalculate(rule, amount,
                    out long seconds, out long cultivation))
            {
                quantityView.ShowError("数量或目录已变化，请重新拖入");
                return;
            }

            ItemDefinitionTable definitions = inventory.ItemDefinitions;
            if (definitions == null
                || !definitions.TryGet(stack.ItemId, out ItemDefinition definition)
                || definition.Icon == null)
            {
                quantityView.ShowError("物品图标不可用");
                return;
            }

            selectedIdentity = stack.Identity;
            selectedItemId = stack.ItemId;
            selectedCount = amount;
            selectedConfigurationRevision = refining.Catalog.configurationRevision;
            materialIcon.sprite = definition.Icon;
            materialIcon.preserveAspect = true;
            materialIcon.gameObject.SetActive(true);
            if (totalTimeText != null)
            {
                totalTimeText.enableAutoSizing = true;
                totalTimeText.fontSizeMin = 8;
                totalTimeText.fontSizeMax = 15;
                totalTimeText.text = RefiningPreviewCalculator.FormatDuration(seconds);
            }
            if (cultivationPreviewText != null)
            {
                cultivationPreviewText.enableAutoSizing = true;
                cultivationPreviewText.fontSizeMin = 8;
                cultivationPreviewText.fontSizeMax = 15;
                cultivationPreviewText.text = "预计 +"
                    + cultivation.ToString(CultureInfo.InvariantCulture);
            }
            quantityView.Hide();
            pendingIdentity = null;
            pendingItemId = null;
            pendingConfigurationRevision = 0;
        }

        private bool TryFindRule(string itemId, out RefiningRuleDto rule)
        {
            rule = null;
            RefiningResultDto catalog = refining != null ? refining.Catalog : null;
            if (catalog == null || !catalog.success
                || catalog.configurationRevision <= 0 || catalog.rules == null)
                return false;
            foreach (RefiningRuleDto entry in catalog.rules)
            {
                if (entry != null && string.Equals(entry.itemId, itemId,
                    StringComparison.Ordinal)
                    && RefiningPreviewCalculator.TryCalculate(entry, 1,
                        out _, out _))
                {
                    rule = entry;
                    return true;
                }
            }
            return false;
        }

        private void BindRefining()
        {
            RefiningSessionRuntime current = RefiningSessionRuntime.Instance;
            if (refining == current)
                return;
            UnbindRefining();
            refining = current;
            if (refining != null)
                refining.CatalogChanged += HandleCatalogChanged;
        }

        private void UnbindRefining()
        {
            if (refining != null)
                refining.CatalogChanged -= HandleCatalogChanged;
            refining = null;
        }

        private void HandleCatalogChanged(RefiningResultDto result)
        {
            if (selectedIdentity != null
                && !TryGetSelection(out _, out _, out _))
                ClearSelection();
        }

        private void ClearSelection()
        {
            selectedIdentity = null;
            selectedItemId = null;
            selectedCount = 0;
            selectedConfigurationRevision = 0;
            if (totalTimeText != null
                && (refining == null || refining.Snapshot == null
                    || refining.Snapshot.activeTask == null))
                totalTimeText.text = "00:00:00";
            if (cultivationPreviewText != null
                && (refining == null || refining.Snapshot == null
                    || refining.Snapshot.activeTask == null))
                cultivationPreviewText.text = "--";
            if (materialIcon == null)
                return;
            if (refining != null && refining.Snapshot != null
                && refining.Snapshot.activeTask != null)
                return;
            materialIcon.sprite = null;
            materialIcon.gameObject.SetActive(false);
        }

        private void CancelDrag()
        {
            if (dragPreview != null)
                Destroy(dragPreview.gameObject);
            dragPreview = null;
            dragCanvas = null;
            draggingIdentity = null;
        }
    }
}
