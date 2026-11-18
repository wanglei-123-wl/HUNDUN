using System;
using System.Collections.Generic;

public sealed class WorldLootViewManager
{
    private readonly Dictionary<string, WorldLootView> viewsByLootId =
        new Dictionary<string, WorldLootView>(StringComparer.Ordinal);
    private readonly HashSet<WorldLootView> removingViews =
        new HashSet<WorldLootView>();

    private readonly LootViewFactory viewFactory;
    private readonly LootViewPool viewPool;
    private readonly LootOwnershipService ownershipService;
    private readonly Func<string> getSelfCharacterDbId;

    public int ViewCount => viewsByLootId.Count;

    public WorldLootViewManager(
        LootViewFactory viewFactory,
        LootViewPool viewPool,
        LootOwnershipService ownershipService,
        Func<string> getSelfCharacterDbId)
    {
        this.viewFactory = viewFactory
            ?? throw new ArgumentNullException(nameof(viewFactory));
        this.viewPool = viewPool
            ?? throw new ArgumentNullException(nameof(viewPool));
        this.ownershipService = ownershipService
            ?? throw new ArgumentNullException(nameof(ownershipService));
        this.getSelfCharacterDbId = getSelfCharacterDbId
            ?? throw new ArgumentNullException(nameof(getSelfCharacterDbId));
    }

    public bool TryGetView(string lootId, out WorldLootView view)
    {
        if (string.IsNullOrWhiteSpace(lootId))
        {
            view = null;
            return false;
        }

        return viewsByLootId.TryGetValue(lootId, out view);
    }

    public void CreateOrUpdate(LootState state)
    {
        if (state == null || string.IsNullOrWhiteSpace(state.LootId))
        {
            return;
        }

        if (viewsByLootId.TryGetValue(state.LootId, out WorldLootView existingView))
        {
            viewFactory.Refresh(existingView, state);
            ApplyOwnership(existingView, state);
            return;
        }

        WorldLootView view = viewFactory.Create(state);
        viewsByLootId.Add(state.LootId, view);
        ApplyOwnership(view, state);
    }

    public void Remove(string lootId, LootRemoveReason reason)
    {
        if (string.IsNullOrWhiteSpace(lootId)
            || !viewsByLootId.TryGetValue(lootId, out WorldLootView view))
        {
            return;
        }

        viewsByLootId.Remove(lootId);
        removingViews.Add(view);
        view.PlayRemove(reason, () =>
        {
            if (removingViews.Remove(view))
            {
                viewPool.Return(view);
            }
        });
    }

    public void RebuildScene(
        string sceneId,
        IReadOnlyCollection<LootState> loots)
    {
        if (string.IsNullOrWhiteSpace(sceneId))
        {
            return;
        }

        ClearScene(sceneId);

        if (loots == null)
        {
            return;
        }

        foreach (LootState loot in loots)
        {
            if (loot != null
                && string.Equals(loot.SceneId, sceneId, StringComparison.Ordinal))
            {
                CreateOrUpdate(loot);
            }
        }
    }

    public void SetPickupPending(string lootId, bool pending)
    {
        if (TryGetView(lootId, out WorldLootView view))
        {
            view.SetPickupPending(pending);
        }
    }

    public void RefreshOwnershipStates(IReadOnlyCollection<LootState> loots)
    {
        if (loots == null)
        {
            return;
        }

        foreach (LootState loot in loots)
        {
            if (loot != null
                && viewsByLootId.TryGetValue(loot.LootId, out WorldLootView view))
            {
                ApplyOwnership(view, loot);
            }
        }
    }

    public void ClearScene(string sceneId)
    {
        if (string.IsNullOrWhiteSpace(sceneId))
        {
            return;
        }

        List<string> lootIds = new List<string>();
        foreach (KeyValuePair<string, WorldLootView> pair in viewsByLootId)
        {
            if (pair.Value != null
                && string.Equals(pair.Value.SceneId, sceneId, StringComparison.Ordinal))
            {
                lootIds.Add(pair.Key);
            }
        }

        for (int index = 0; index < lootIds.Count; index++)
        {
            string lootId = lootIds[index];
            WorldLootView view = viewsByLootId[lootId];
            viewsByLootId.Remove(lootId);
            viewPool.Return(view);
        }

        List<WorldLootView> removingSceneViews = new List<WorldLootView>();
        foreach (WorldLootView view in removingViews)
        {
            if (view != null
                && string.Equals(view.SceneId, sceneId, StringComparison.Ordinal))
            {
                removingSceneViews.Add(view);
            }
        }

        for (int index = 0; index < removingSceneViews.Count; index++)
        {
            WorldLootView view = removingSceneViews[index];
            removingViews.Remove(view);
            viewPool.Return(view);
        }
    }

    public void ClearAll()
    {
        foreach (WorldLootView view in viewsByLootId.Values)
        {
            viewPool.Return(view);
        }

        viewsByLootId.Clear();

        foreach (WorldLootView view in removingViews)
        {
            viewPool.Return(view);
        }

        removingViews.Clear();
    }

    private void ApplyOwnership(WorldLootView view, LootState state)
    {
        LootOwnershipState ownershipState = ownershipService.GetOwnershipState(
            state,
            getSelfCharacterDbId());
        view.SetOwnershipState(ownershipState);
    }
}
