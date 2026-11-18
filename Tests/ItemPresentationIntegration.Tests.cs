using System;
using System.Collections.Generic;
using System.Reflection;
using Game.Inventory;
using Game.Items;

internal static class ItemPresentationIntegrationTests
{
    public static void Run()
    {
        var icon = new UnityEngine.Sprite();
        var legacyIcon = new UnityEngine.Sprite();
        var fresh = new ItemDefinition
        {
            ItemId = "server_item_sample",
            DisplayName = "Sample item",
            Description = "Sample description",
            Icon = icon
        };
        var legacy = new ItemDefinition
        {
            ItemId = "hp_potion_01",
            DisplayName = "Legacy potion",
            Icon = legacyIcon
        };
        var fallback = new ItemDefinition();
        var asset = new ItemDefinitionAsset();
        SetField(asset, "definition", fresh);
        var table = new ItemDefinitionTable();
        SetField(table, "items", new List<ItemDefinition> { legacy });
        SetField(table, "itemAssets", new List<ItemDefinitionAsset> { asset });
        SetField(table, "defaultConfig", fallback);

        var bagSlot = new InventorySlotView();
        var bindSlot = typeof(InventoryGridView).GetMethod(
            "BindSlot", BindingFlags.Static | BindingFlags.NonPublic);
        BindBag("server_item_sample", bagSlot, table, bindSlot);
        Check(ReferenceEquals(bagSlot.Definition, fresh)
              && ReferenceEquals(bagSlot.Definition.Icon, icon)
              && bagSlot.Definition.Description == "Sample description",
            "Bag binding must receive the new asset's icon and description.");
        BindBag("hp_potion_01", bagSlot, table, bindSlot);
        Check(ReferenceEquals(bagSlot.Definition, legacy)
              && ReferenceEquals(bagSlot.Definition.Icon, legacyIcon),
            "Existing bag items must still use the legacy entry.");
        BindBag("missing", bagSlot, table, bindSlot);
        Check(ReferenceEquals(bagSlot.Definition, fallback),
            "Unknown bag items must retain the existing default definition.");

        var pool = new LootViewPool();
        var loot = new LootViewFactory(table, new LootSystemSettings(), pool);
        WorldLootView view = loot.Create(new LootState("server_item_sample"));
        Check(ReferenceEquals(pool.LastRented, fresh)
              && ReferenceEquals(view.Definition, fresh)
              && ReferenceEquals(view.Definition.Icon, icon)
              && view.Definition.DisplayName == "Sample item",
            "Ground loot must receive the new asset's icon and name.");
        loot.Refresh(view, new LootState("hp_potion_01"));
        Check(ReferenceEquals(view.Definition, legacy),
            "Existing ground loot must still use the legacy entry.");
        loot.Refresh(view, new LootState("missing"));
        Check(ReferenceEquals(view.Definition, fallback),
            "Unknown ground loot must retain the existing fallback.");
    }

    private static void BindBag(string itemId, InventorySlotView slot,
        ItemDefinitionTable table, MethodInfo bind)
    {
        bind.Invoke(null, new object[]
        {
            slot, new InventoryItemState(1, 0, itemId, 1, false), table
        });
    }

    private static void SetField(object target, string name, object value)
    {
        target.GetType().GetField(name,
            BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}

namespace Game.Inventory
{
    public sealed class InventorySlotView
    {
        public UnityEngine.GameObject gameObject = new UnityEngine.GameObject();
        public string name;
        public InventoryItemState Item { get; private set; }
        public ItemDefinition Definition { get; private set; }
        public bool HasItem => Item != null;
        public string ItemInstanceId => Item == null ? "" : Item.ItemInstanceId;
        public event Action<InventorySlotView> Clicked;
        public void Initialize(int index) { }
        public void Bind(InventoryItemState item, ItemDefinition definition)
        {
            Item = item;
            Definition = definition;
        }
        public void Clear() { Item = null; Definition = null; }
        public void SetSelected(bool value) { }
    }
}

public sealed class LootState
{
    public string ItemId { get; }
    public LootState(string id) { ItemId = id; }
}

public sealed class LootSystemSettings
{
    public string UnknownItemName = "Unknown";
    public UnityEngine.GameObject DefaultLootPrefab;
}

public sealed class LootViewPool
{
    public ItemDefinition LastRented { get; private set; }
    public WorldLootView Rent(ItemDefinition definition)
    {
        LastRented = definition;
        return new WorldLootView();
    }
}

public sealed class WorldLootView
{
    public ItemDefinition Definition { get; private set; }
    public void Bind(LootState state, ItemDefinition definition)
    {
        Definition = definition;
    }
    public void Refresh(LootState state, ItemDefinition definition)
    {
        Definition = definition;
    }
}
