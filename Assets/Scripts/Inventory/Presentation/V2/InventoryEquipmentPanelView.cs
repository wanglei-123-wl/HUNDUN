using System;
using Game.Inventory.Model.V2;
using Game.Inventory.Protocol.V2;
using Game.Items;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Inventory.Presentation.V2
{
    public sealed class InventoryEquipmentPanelView : IDisposable
    {
        private static readonly string[] SlotLabels =
        {
            null, "武器", "头盔", "护甲", "项链",
            "戒指", "戒指", "手镯", "手镯",
            "鞋子", "盾牌", "法宝", "法阵"
        };

        private readonly InventoryEquipmentSlotView[] slots =
            new InventoryEquipmentSlotView[InventoryV2Protocol.EquipmentSlotCount + 1];
        private readonly ItemDefinitionTable catalog;

        public event Action<string, int> EquipRequested;
        public event Action<int, int, string> MoveRequested;

        private InventoryEquipmentPanelView(ItemDefinitionTable catalog)
        {
            this.catalog = catalog;
        }

        public static bool TryCreate(Transform explicitRoot,
            ItemDefinitionTable catalog,
            out InventoryEquipmentPanelView view, out string error)
        {
            view = null;
            Transform root = explicitRoot;
            if (root == null)
            {
                foreach (RectTransform candidate in
                    UnityEngine.Object.FindObjectsOfType<RectTransform>(true))
                {
                    if (candidate.name != "装备栏" || !candidate.gameObject.scene.IsValid())
                        continue;
                    if (root != null)
                    {
                        error = "Multiple equipment roots named 装备栏 were found.";
                        return false;
                    }
                    root = candidate;
                }
            }
            if (root == null)
            {
                error = "Equipment root 装备栏 was not found.";
                return false;
            }

            var result = new InventoryEquipmentPanelView(catalog);
            for (int code = 1; code <= InventoryV2Protocol.EquipmentSlotCount; code++)
            {
                Transform child = root.Find("装备框按钮" + code);
                if (child == null) child = root.Find(SlotLabels[code] + code);
                if (child == null && code > 8) continue;
                if (child == null || child.GetComponent<Button>() == null)
                {
                    error = "Equipment button binding is missing for slot " + code;
                    result.Dispose();
                    return false;
                }
                Image icon = null;
                foreach (Image image in child.GetComponentsInChildren<Image>(true))
                {
                    if (image.name != "装备图标显示板"
                        && !image.name.StartsWith("装备图标显示板 (", StringComparison.Ordinal))
                        continue;
                    if (icon != null)
                    {
                        error = "Equipment icon binding is ambiguous for slot " + code;
                        result.Dispose();
                        return false;
                    }
                    icon = image;
                }
                if (icon == null)
                {
                    error = "Equipment icon binding is missing for slot " + code;
                    result.Dispose();
                    return false;
                }
                Transform label = null;
                foreach (Transform candidate in child)
                {
                    if (candidate == icon.transform || candidate.name == "未知装备占位图")
                        continue;
                    if (label != null)
                    {
                        error = "Equipment empty label binding is ambiguous for slot " + code;
                        result.Dispose();
                        return false;
                    }
                    label = candidate;
                }
                InventoryEquipmentSlotView slot =
                    child.GetComponent<InventoryEquipmentSlotView>();
                if (slot == null) slot = child.gameObject.AddComponent<InventoryEquipmentSlotView>();
                slot.Configure(code, icon, label != null ? label.gameObject : null);
                slot.EquipRequested += result.ForwardEquip;
                slot.MoveRequested += result.ForwardMove;
                result.slots[code] = slot;
            }
            error = string.Empty;
            view = result;
            return true;
        }

        public void Render(InventoryOwnershipSnapshot snapshot)
        {
            Clear();
            if (snapshot == null) return;
            foreach (InventoryEquipmentState item in snapshot.Equipment)
            {
                if (item.SlotCode == 0) continue;
                if (item.SlotCode > InventoryV2Protocol.EquipmentSlotCount
                    || slots[item.SlotCode] == null) continue;
                Sprite icon = null;
                ItemDetailData detail = null;
                if (catalog != null
                    && catalog.TryGetByEquipmentId(item.EquipmentId, out ItemDefinition definition))
                {
                    icon = definition.Icon;
                    detail = ItemDetailDataResolver.FromItem(definition);
                }
                slots[item.SlotCode].SetEquipped(item.Identity, icon, detail);
            }
        }

        public void SetPending(bool value)
        {
            for (int code = 1; code <= InventoryV2Protocol.EquipmentSlotCount; code++)
                if (slots[code] != null) slots[code].SetPending(value);
        }

        public void Clear()
        {
            for (int code = 1; code <= InventoryV2Protocol.EquipmentSlotCount; code++)
                if (slots[code] != null) slots[code].Clear();
        }

        public void Dispose()
        {
            for (int code = 1; code <= InventoryV2Protocol.EquipmentSlotCount; code++)
            {
                InventoryEquipmentSlotView slot = slots[code];
                if (slot == null) continue;
                slot.EquipRequested -= ForwardEquip;
                slot.MoveRequested -= ForwardMove;
                slot.Clear();
                slots[code] = null;
            }
        }

        private void ForwardEquip(string identity, int code)
        {
            EquipRequested?.Invoke(identity, code);
        }

        private void ForwardMove(int source, int target, string identity)
        {
            MoveRequested?.Invoke(source, target, identity);
        }
    }
}
