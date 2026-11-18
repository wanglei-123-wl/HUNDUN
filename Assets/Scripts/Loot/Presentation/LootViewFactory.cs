using System;
using System.Collections.Generic;
using Game.Items;
using UnityEngine;

public sealed class LootViewFactory
{
    private readonly ItemDefinitionTable configTable;
    private readonly LootSystemSettings settings;
    private readonly LootViewPool viewPool;
    private readonly HashSet<string> missingConfigWarnings =
        new HashSet<string>(StringComparer.Ordinal);

    public LootViewFactory(
        ItemDefinitionTable configTable,
        LootSystemSettings settings,
        LootViewPool viewPool)
    {
        this.configTable = configTable;
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.viewPool = viewPool ?? throw new ArgumentNullException(nameof(viewPool));
    }

    public WorldLootView Create(LootState state)
    {
        if (state == null)
        {
            throw new ArgumentNullException(nameof(state));
        }

        ItemDefinition config = GetConfig(state.ItemId);
        WorldLootView view = viewPool.Rent(config);
        view.Bind(state, config);
        return view;
    }

    public void Refresh(WorldLootView view, LootState state)
    {
        if (view == null || state == null)
        {
            return;
        }

        view.Refresh(state, GetConfig(state.ItemId));
    }

    private ItemDefinition GetConfig(string itemId)
    {
        if (configTable != null && configTable.TryGet(itemId, out ItemDefinition config))
        {
            return config;
        }

        ItemDefinition fallback = configTable == null
            ? null
            : configTable.GetOrDefault(itemId);

        if (fallback == null)
        {
            fallback = new ItemDefinition
            {
                ItemId = itemId,
                DisplayName = settings.UnknownItemName,
                WorldPrefab = settings.DefaultLootPrefab,
                QualityColor = Color.white
            };
        }

        string warningKey = string.IsNullOrWhiteSpace(itemId) ? "<empty>" : itemId;
        if (missingConfigWarnings.Add(warningKey))
        {
            Debug.LogWarning("Item definition not found: " + warningKey);
        }

        return fallback;
    }
}
