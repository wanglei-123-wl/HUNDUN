using System;
using System.Collections.Generic;
using System.Linq;
using ResourceMigrationStage1;

namespace ResourceMigrationStage3
{
    public sealed class PackingRoot
    {
        public string Path;
        public string Bucket;
    }

    public sealed class SharedPromotion
    {
        public string Path;
        public string Group;
        public string[] OriginalOwners;
    }

    public sealed class PackingPlan
    {
        public List<SharedPromotion> Promotions = new List<SharedPromotion>();
        public Dictionary<string, HashSet<string>> Before;
        public Dictionary<string, HashSet<string>> After;
    }

    public static class PackingPlanner
    {
        public const string RequiredSharedGroup = "RemoteSharedGame";
        public const string DeferredSharedGroup = "RemoteSharedMaps";

        public static Dictionary<string, HashSet<string>> Owners(IEnumerable<PackingRoot> roots,
            IReadOnlyDictionary<string, string[]> dependencies)
        {
            var declared = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var root in roots)
            {
                if (!declared.TryGetValue(root.Path, out var buckets))
                    declared[root.Path] = buckets = new HashSet<string>(StringComparer.Ordinal);
                buckets.Add(root.Bucket);
            }
            var owners = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var root in roots)
            foreach (string path in OwnershipPolicy.OwnedDependencies(root.Path, root.Bucket, dependencies,
                declared, new HashSet<string>(StringComparer.Ordinal)))
            {
                if (!owners.TryGetValue(path, out var buckets))
                    owners[path] = buckets = new HashSet<string>(StringComparer.Ordinal);
                buckets.Add(root.Bucket);
            }
            return owners;
        }

        // Promote shared parents first. Their exclusive children stay implicit,
        // avoiding hundreds of needless entries for a single shared prefab.
        // Recalculate after each frontier to catch children also used directly elsewhere.
        public static PackingPlan Create(IEnumerable<PackingRoot> proposedRoots,
            IReadOnlyDictionary<string, string[]> dependencies, HashSet<string> eligible,
            HashSet<string> requiredClosure)
        {
            var roots = proposedRoots.Select(root => new PackingRoot { Path = root.Path, Bucket = root.Bucket }).ToList();
            if (roots.Select(root => root.Path).Distinct(StringComparer.Ordinal).Count() != roots.Count)
                throw new InvalidOperationException("One asset has multiple explicit owners.");
            var result = new PackingPlan { Before = Owners(roots, dependencies) };
            for (int iteration = 0; iteration <= eligible.Count; iteration++)
            {
                var owners = Owners(roots, dependencies);
                var candidates = new HashSet<string>(owners.Where(pair => pair.Value.Count > 1
                    && eligible.Contains(pair.Key)).Select(pair => pair.Key), StringComparer.Ordinal);
                if (candidates.Count == 0)
                {
                    result.After = owners;
                    return result;
                }
                var hasSharedParent = new HashSet<string>(StringComparer.Ordinal);
                foreach (string parent in candidates)
                    if (dependencies.TryGetValue(parent, out var children))
                        foreach (string child in children)
                            if (child != parent && candidates.Contains(child)) hasSharedParent.Add(child);
                string[] frontier = candidates.Where(path => !hasSharedParent.Contains(path))
                    .OrderBy(path => path, StringComparer.Ordinal).ToArray();
                // A cycle has no parent-free frontier. One stable boundary breaks it.
                if (frontier.Length == 0) frontier = new[] { candidates.OrderBy(path => path, StringComparer.Ordinal).First() };
                foreach (string path in frontier)
                {
                    string group = requiredClosure.Contains(path) ? RequiredSharedGroup : DeferredSharedGroup;
                    result.Promotions.Add(new SharedPromotion { Path = path, Group = group,
                        OriginalOwners = result.Before[path].OrderBy(owner => owner, StringComparer.Ordinal).ToArray() });
                    roots.Add(new PackingRoot { Path = path, Bucket = group + "/together/assets" });
                }
            }
            throw new InvalidOperationException("Shared dependency planning did not converge.");
        }
    }
}
