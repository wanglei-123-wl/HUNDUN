using System;
using System.Collections.Generic;

namespace ResourceMigrationStage1
{
    public sealed class OwnershipFacts
    {
        public string Path;
        public bool Startup;
        public bool GlobalInfrastructure;
        public bool BuiltIn;
        public bool LocalAddressable;
        public bool RemoteExplicit;
        public int RemoteBundleOwners;
    }

    public static class OwnershipPolicy
    {
        public static string Classify(OwnershipFacts facts)
        {
            if (facts.Path.IndexOf("/Editor/", StringComparison.OrdinalIgnoreCase) >= 0)
                return "editor_only";
            if (facts.Path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                || facts.Path.EndsWith(".asmdef", StringComparison.OrdinalIgnoreCase)
                || facts.Path.EndsWith(".asmref", StringComparison.OrdinalIgnoreCase))
                return "program_source";
            if (facts.RemoteExplicit && facts.BuiltIn)
                return "review_remote_root_forced_into_player";
            if (facts.BuiltIn && facts.LocalAddressable)
                return "review_duplicate_local_packing_paths";
            if (facts.Startup)
                return "keep_startup_local";
            if (facts.GlobalInfrastructure || facts.Path == "Assets/Resources/NetworkEndpointSettings.asset")
                return "keep_infrastructure_local";
            if (facts.BuiltIn)
                return "review_forced_builtin_content";
            if (facts.RemoteExplicit)
                return "remote_explicit_root";
            if (facts.RemoteBundleOwners > 1)
                return "review_remote_shared_dependency";
            if (facts.RemoteBundleOwners == 1)
                return "remote_game_dependency";
            if (facts.LocalAddressable)
                return "review_local_addressable_content";
            return "pending_review_not_proven_unused";
        }

        // An explicitly addressable dependency belongs to its own packing bucket.
        // Its children must not also be attributed to the referencing bucket.
        public static HashSet<string> OwnedDependencies(string root, string bucket,
            IReadOnlyDictionary<string, string[]> directDependencies,
            IReadOnlyDictionary<string, HashSet<string>> explicitBuckets,
            HashSet<string> boundaries)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Stack<string>();
            pending.Push(root);
            while (pending.Count > 0)
            {
                string path = pending.Pop();
                if (!visited.Add(path)) continue;
                if (path != root && explicitBuckets.TryGetValue(path, out var declared)
                    && !declared.Contains(bucket))
                {
                    boundaries.Add(path);
                    continue;
                }
                result.Add(path);
                if (directDependencies.TryGetValue(path, out string[] children))
                    foreach (string child in children) pending.Push(child);
            }
            return result;
        }
    }
}
