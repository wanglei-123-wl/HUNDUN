using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;

// Resolves both resources without loading or downloading them. Cache checks
// happen before the Gate reservation, so a missing bundle cannot freeze input.
public static class MapTransferResourcePreflight
{
    public static IResourceLocation[] Resolve(string sceneId, string mapId,
        IEnumerable<IResourceLocator> locators)
    {
        if (!WorldMapSceneCatalog.TryGetAddress(sceneId, mapId, out string scene)
            || !WorldMapSceneCatalog.TryGetTraversalAddress(sceneId, mapId, out string traversal))
            throw new InvalidOperationException("Unknown transfer destination.");
        if (locators == null) throw new ArgumentNullException(nameof(locators));
        var catalogs = new List<IResourceLocator>(locators);
        var all = new List<IResourceLocation> { Find(scene, typeof(SceneInstance), catalogs),
            Find(traversal, typeof(TextAsset), catalogs) };
        var seen = new HashSet<IResourceLocation>(all);
        // GetDownloadSizeAsync sums immediate dependencies. Include every
        // parent so shared bundles nested below the scene bundle are counted.
        for (int i = 0; i < all.Count; i++)
        {
            var dependencies = all[i].Dependencies;
            if (dependencies == null) continue;
            foreach (var dependency in dependencies)
            {
                if (dependency == null) throw new InvalidOperationException("Invalid map dependency.");
                if (seen.Add(dependency)) all.Add(dependency);
            }
        }
        return all.ToArray();
    }

    private static IResourceLocation Find(string address, Type type, List<IResourceLocator> locators)
    {
        IResourceLocation found = null;
        foreach (var locator in locators)
        {
            if (locator == null || !locator.Locate(address, type, out var locations) || locations == null)
                continue;
            foreach (var location in locations)
            {
                if (location == null || location.ResourceType == null
                    || !type.IsAssignableFrom(location.ResourceType)) continue;
                if (found != null && (found.InternalId != location.InternalId
                    || found.ProviderId != location.ProviderId || found.ResourceType != location.ResourceType))
                    throw new InvalidOperationException("Ambiguous map resource: " + address);
                found = location;
            }
        }
        if (found == null) throw new InvalidOperationException("Map resource not found: " + address);
        return found;
    }
}
