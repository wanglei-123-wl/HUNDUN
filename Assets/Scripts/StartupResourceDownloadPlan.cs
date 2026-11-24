using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;

// The current catalog, rather than a fixed label, owns the full download scope.
public sealed class StartupResourceDownloadPlan
{
    public IList<IResourceLocation> DownloadLocations { get; }
    public IReadOnlyList<IResourceLocation> RemoteBundles { get; }
    public int RequiredAddressCount { get; }

    private StartupResourceDownloadPlan(IResourceLocation[] bundles, int requiredAddressCount)
    {
        RemoteBundles = Array.AsReadOnly(bundles);
        RequiredAddressCount = requiredAddressCount;
        // GetDownloadSizeAsync sums dependencies, whereas DownloadDependenciesAsync
        // also accepts bundle locations directly. One dependency root makes both
        // APIs operate on exactly the same deduplicated set, including orphan keys.
        DownloadLocations = Array.AsReadOnly<IResourceLocation>(new IResourceLocation[]
        {
            new ResourceLocationBase("startup/all-published-resources", "startup/all-published-resources",
                "StartupResourceDownloadPlan", typeof(object), bundles)
        });
    }

    public static StartupResourceDownloadPlan Create(IEnumerable<IResourceLocator> resourceLocators)
    {
        if (resourceLocators == null) throw new ArgumentNullException(nameof(resourceLocators));
        IResourceLocator[] locators = resourceLocators.Where(locator => locator != null).ToArray();
        var required = new HashSet<string>(StringComparer.Ordinal)
        {
            RemoteGameContent.LoginSceneAddress, RemoteGameContent.WorldSceneAddress,
            RemoteGameContent.ItemTableAddress, RemoteGameContent.BootstrapFontAddress
        };
        required.UnionWith(WorldMapSceneCatalog.GetAllResourceAddresses());
        foreach (string address in required)
            if (!locators.Any(locator => locator.Locate(address, null, out var locations)
                && locations != null && locations.Count > 0))
                throw new InvalidOperationException("Required startup address is missing: " + address);

        var visited = new HashSet<IResourceLocation>();
        var bundles = new Dictionary<string, IResourceLocation>(StringComparer.Ordinal);
        var pending = new Stack<IResourceLocation>();
        foreach (IResourceLocator locator in locators)
        {
            foreach (object key in locator.Keys)
            {
                if (!locator.Locate(key, null, out var locations) || locations == null) continue;
                foreach (IResourceLocation location in locations)
                    if (location != null) pending.Push(location);
            }
        }
        while (pending.Count > 0)
        {
            IResourceLocation location = pending.Pop();
            if (!visited.Add(location)) continue;
            if (location.HasDependencies)
                foreach (IResourceLocation dependency in location.Dependencies)
                    if (dependency != null) pending.Push(dependency);

            if (!(location.Data is AssetBundleRequestOptions options)
                || !Uri.TryCreate(location.InternalId, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)) continue;
            if (location.ResourceType != typeof(IAssetBundleResource) || string.IsNullOrWhiteSpace(options.Hash))
                throw new InvalidOperationException("Remote bundle has invalid type or cache hash: " + location.InternalId);
            string identity = location.ProviderId + "\n" + location.InternalId;
            if (bundles.TryGetValue(identity, out var previous))
            {
                var old = (AssetBundleRequestOptions)previous.Data;
                if (old.Hash != options.Hash || old.BundleSize != options.BundleSize || old.Crc != options.Crc)
                    throw new InvalidOperationException("Conflicting remote bundle metadata: " + location.InternalId);
            }
            else bundles.Add(identity, location);
        }
        return new StartupResourceDownloadPlan(bundles.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Value).ToArray(), required.Count);
    }
}
