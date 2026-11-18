using System;
using System.Collections.Generic;
using Game.Items;
using UnityEngine;

public sealed class LootViewPool
{
    private readonly LootSystemSettings settings;
    private readonly Transform root;
    private readonly Dictionary<GameObject, Stack<WorldLootView>> pooledByPrefab =
        new Dictionary<GameObject, Stack<WorldLootView>>();
    private readonly Stack<WorldLootView> fallbackPool = new Stack<WorldLootView>();
    private readonly Dictionary<WorldLootView, GameObject> sourcePrefabByView =
        new Dictionary<WorldLootView, GameObject>();
    private bool cleared;

    public LootViewPool(LootSystemSettings settings, Transform root = null)
    {
        this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
        this.root = root;
    }

    public WorldLootView Rent(ItemDefinition config)
    {
        if (cleared)
        {
            throw new InvalidOperationException("Loot view pool has been cleared.");
        }

        GameObject prefab = config != null && config.WorldPrefab != null
            ? config.WorldPrefab
            : settings.DefaultLootPrefab;

        WorldLootView view = prefab == null
            ? RentFallback()
            : RentConfigured(prefab);

        view.gameObject.SetActive(true);
        return view;
    }

    public void Return(WorldLootView view)
    {
        if (view == null)
        {
            return;
        }

        if (cleared)
        {
            sourcePrefabByView.Remove(view);
            UnityEngine.Object.Destroy(view.gameObject);
            return;
        }

        view.ResetView();
        view.gameObject.SetActive(false);

        if (!sourcePrefabByView.TryGetValue(view, out GameObject prefab))
        {
            UnityEngine.Object.Destroy(view.gameObject);
            return;
        }

        if (prefab == null)
        {
            fallbackPool.Push(view);
            return;
        }

        if (!pooledByPrefab.TryGetValue(prefab, out Stack<WorldLootView> pool))
        {
            pool = new Stack<WorldLootView>();
            pooledByPrefab.Add(prefab, pool);
        }

        pool.Push(view);
    }

    public void Clear()
    {
        if (cleared)
        {
            return;
        }

        cleared = true;
        List<WorldLootView> allViews =
            new List<WorldLootView>(sourcePrefabByView.Keys);
        for (int index = 0; index < allViews.Count; index++)
        {
            WorldLootView view = allViews[index];
            if (view != null)
            {
                UnityEngine.Object.Destroy(view.gameObject);
            }
        }

        pooledByPrefab.Clear();
        fallbackPool.Clear();
        sourcePrefabByView.Clear();
    }

    private WorldLootView RentConfigured(GameObject prefab)
    {
        if (pooledByPrefab.TryGetValue(prefab, out Stack<WorldLootView> pool))
        {
            while (pool.Count > 0)
            {
                WorldLootView pooledView = pool.Pop();
                if (pooledView != null)
                {
                    return pooledView;
                }
            }
        }

        GameObject instance = UnityEngine.Object.Instantiate(prefab, root);
        WorldLootView view = EnsureViewAndCollider(instance);
        sourcePrefabByView[view] = prefab;
        return view;
    }

    private WorldLootView RentFallback()
    {
        while (fallbackPool.Count > 0)
        {
            WorldLootView pooledView = fallbackPool.Pop();
            if (pooledView != null)
            {
                return pooledView;
            }
        }

        GameObject instance = GameObject.CreatePrimitive(PrimitiveType.Cube);
        instance.name = "WorldLootFallback";
        if (root != null)
        {
            instance.transform.SetParent(root, false);
        }

        WorldLootView view = EnsureViewAndCollider(instance);
        sourcePrefabByView[view] = null;
        return view;
    }

    private static WorldLootView EnsureViewAndCollider(GameObject instance)
    {
        WorldLootView view = instance.GetComponent<WorldLootView>();
        if (view == null)
        {
            view = instance.AddComponent<WorldLootView>();
            Debug.LogWarning("Loot prefab is missing WorldLootView: " + instance.name);
        }

        if (instance.GetComponentInChildren<Collider>(true) == null)
        {
            instance.AddComponent<SphereCollider>();
            Debug.LogWarning("Loot prefab is missing Collider: " + instance.name);
        }

        return view;
    }

    private void DestroyPool(Stack<WorldLootView> pool)
    {
        while (pool.Count > 0)
        {
            WorldLootView view = pool.Pop();
            if (view == null)
            {
                continue;
            }

            sourcePrefabByView.Remove(view);
            UnityEngine.Object.Destroy(view.gameObject);
        }
    }
}
