using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ResourceMigrationStage1;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;
using UnityEngine.SceneManagement;

// Read-only: imports are performed by Unity itself; this tool never saves, moves,
// creates, regroups or deletes project assets, and never builds or publishes.
[InitializeOnLoad]
public static class ResourceMigrationStage1Audit
{
    private const string RequestPath = "Temp/ResourceMigrationStage1Audit.request.json";
    private const string StartupScene = "Assets/Scenes/启动场景.unity";
    private static bool running;
    private static double nextPoll;

    private sealed class Root
    {
        public string Path, Kind, Group, Address, Bucket, RecommendedGroup;
        public bool Remote;
        public string[] Dependencies;
    }

    static ResourceMigrationStage1Audit()
    {
        if (!Application.isBatchMode) EditorApplication.update += Poll;
    }

    [MenuItem("Tools/Resource Update/Audit Resource Migration Stage 1")]
    public static void AuditFromMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[ResourceMigrationStage1] Stop Play mode before auditing.");
            return;
        }
        if (DirtyScenes().Count > 0)
        {
            Debug.LogWarning("[ResourceMigrationStage1] Save the scenes to include in this disk-based audit first.");
            return;
        }
        Run(Guid.NewGuid().ToString("N"), null);
    }

    private static void Poll()
    {
        if (running || EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 2;
        if (!File.Exists(RequestPath) || EditorApplication.isCompiling || EditorApplication.isUpdating
            || EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer) return;
        string claimed = null;
        string id = null;
        try
        {
            JObject request = JObject.Parse(File.ReadAllText(RequestPath));
            id = (string)request["requestId"];
            if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidDataException("Invalid audit request ID.");
            string project = Path.GetFullPath((string)request["projectRoot"] ?? "").TrimEnd('\\', '/');
            double remaining = (request.Value<DateTime>("expiresAtUtc").ToUniversalTime() - DateTime.UtcNow).TotalMinutes;
            if (!string.Equals(project, Directory.GetCurrentDirectory().TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)
                || remaining <= 0 || remaining > 30)
                throw new InvalidDataException("Audit request expired or belongs to another project.");
            JArray dirty = DirtyScenes();
            if (dirty.Count > 0)
            {
                WriteStatus(id, "waiting_for_saved_scenes", new JObject { ["dirtyScenes"] = dirty });
                return;
            }
            claimed = RequestPath + "." + id + ".running";
            File.Move(RequestPath, claimed);
            Run(id, (string)request["backupManifest"]);
        }
        catch (Exception error)
        {
            Debug.LogError("[ResourceMigrationStage1] " + error);
            if (Guid.TryParseExact(id, "N", out _))
                WriteStatus(id, "failed", new JObject { ["error"] = error.ToString() });
        }
        finally
        {
            // Only remove the one-time request claimed by this invocation.
            if (claimed != null && File.Exists(claimed)) File.Delete(claimed);
        }
    }

    private static void Run(string id, string backupManifest)
    {
        if (running) return;
        running = true;
        try
        {
            string output = OutputDirectory(id);
            if (File.Exists(Path.Combine(output, "report.json")))
                throw new IOException("Refusing to overwrite a completed audit.");
            WriteStatus(id, "running", null);
            DateTime started = DateTime.UtcNow;
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) throw new InvalidDataException("Addressables settings are missing.");
            string[] allAssets = AssetDatabase.GetAllAssetPaths()
                .Where(p => p.StartsWith("Assets/", StringComparison.Ordinal)
                    && !AssetDatabase.IsValidFolder(p) && !p.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                .OrderBy(p => p, StringComparer.Ordinal).ToArray();
            var roots = new List<Root>();
            var warnings = new JArray();
            var errors = new JArray();
            var groups = new JArray();
            foreach (var scene in EditorBuildSettings.scenes.Where(s => s.enabled))
                roots.Add(new Root { Path = scene.path, Kind = "builtin_scene", Group = "Player" });
            foreach (string path in allAssets.Where(p => p.IndexOf("/Resources/", StringComparison.OrdinalIgnoreCase) >= 0))
                roots.Add(new Root { Path = path, Kind = "builtin_resources", Group = "Resources" });
            foreach (string source in new[] { "ProjectSettings/GraphicsSettings.asset", "ProjectSettings/QualitySettings.asset", "ProjectSettings/ProjectSettings.asset" })
            foreach (Match match in Regex.Matches(File.ReadAllText(source), @"\bguid:\s*([0-9a-fA-F]{32})"))
            {
                string path = AssetDatabase.GUIDToAssetPath(match.Groups[1].Value);
                if (!string.IsNullOrEmpty(path)
                    && (path.StartsWith("Assets/", StringComparison.Ordinal) || path.StartsWith("Packages/", StringComparison.Ordinal))
                    && !roots.Any(r => r.Kind == "builtin_global" && r.Path == path))
                    roots.Add(new Root { Path = path, Kind = "builtin_global", Group = source });
            }
            foreach (AddressableAssetGroup group in settings.groups.Where(g => g != null))
            {
                BundledAssetGroupSchema schema = group.GetSchema<BundledAssetGroupSchema>();
                if (schema == null || !schema.IncludeInBuild) continue;
                string loadPath = schema.LoadPath.GetValue(settings);
                bool remote = schema.LoadPath.Id == settings.RemoteCatalogLoadPath.Id
                    || (Uri.TryCreate(loadPath, UriKind.Absolute, out Uri uri)
                        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps));
                groups.Add(new JObject { ["name"] = group.Name, ["remote"] = remote,
                    ["loadPath"] = loadPath, ["packingMode"] = schema.BundleMode.ToString() });
                foreach (AddressableAssetEntry entry in group.entries)
                {
                    var expanded = new List<AddressableAssetEntry>();
                    entry.GatherAllAssets(expanded, true, true, false);
                    foreach (AddressableAssetEntry child in expanded)
                    {
                        string path = child.AssetPath;
                        if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path))
                        {
                            errors.Add("Unresolved Addressables entry: " + group.Name + "/" + child.address);
                            continue;
                        }
                        string packingKey = schema.BundleMode == BundledAssetGroupSchema.BundlePackingMode.PackSeparately
                            ? entry.guid
                            : schema.BundleMode == BundledAssetGroupSchema.BundlePackingMode.PackTogetherByLabel
                                ? string.Join("|", child.labels.OrderBy(l => l, StringComparer.Ordinal)) : "together";
                        string bucket = group.Name + "/" + packingKey + (path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) ? "/scenes" : "/assets");
                        string recommended = remote && (child.address.StartsWith("game/map-traversal/", StringComparison.Ordinal)
                            || WorldMapSceneCatalog.TryGetMapByAddress(child.address, out _, out _)) ? "RemoteWorldMaps" : group.Name;
                        roots.Add(new Root { Path = path, Kind = remote ? "remote_addressable" : "local_addressable",
                            Group = group.Name, Address = child.address, Bucket = bucket, Remote = remote, RecommendedGroup = recommended });
                    }
                }
            }

            string[] protectedPaths = allAssets.Where(p => p.StartsWith("Assets/AddressableAssetsData/", StringComparison.Ordinal)
                || p.IndexOf("/Resources/", StringComparison.OrdinalIgnoreCase) >= 0)
                .Concat(roots.Select(r => r.Path)).Concat(new[] { "ProjectSettings/EditorBuildSettings.asset", "ProjectSettings/GraphicsSettings.asset", "ProjectSettings/QualitySettings.asset", "ProjectSettings/ProjectSettings.asset" })
                .Where(File.Exists).Distinct(StringComparer.Ordinal).ToArray();
            JObject before = Fingerprint(protectedPaths);
            var rootHashesBefore = new JObject();
            foreach (string path in roots.Select(r => r.Path).Distinct(StringComparer.Ordinal))
                rootHashesBefore[path] = AssetDatabase.GetAssetDependencyHash(path).ToString();
            var direct = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (Root root in roots)
            {
                root.Dependencies = AssetDatabase.GetDependencies(root.Path, true)
                    .Append(root.Path).Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal).ToArray();
                foreach (string path in root.Dependencies)
                    if (!direct.ContainsKey(path))
                        direct[path] = path.StartsWith("Assets/", StringComparison.Ordinal) || path.StartsWith("Packages/", StringComparison.Ordinal)
                            ? AssetDatabase.GetDependencies(path, false).Where(p => p != path).ToArray()
                            : Array.Empty<string>();
            }
            var explicitBuckets = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (Root root in roots.Where(r => r.Bucket != null))
            {
                if (!explicitBuckets.TryGetValue(root.Path, out var buckets)) explicitBuckets[root.Path] = buckets = new HashSet<string>(StringComparer.Ordinal);
                buckets.Add(root.Bucket);
            }
            var owned = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            var bucketGroups = new Dictionary<string, Root>(StringComparer.Ordinal);
            var rootReport = new JArray();
            foreach (Root root in roots)
            {
                var boundaries = new HashSet<string>(StringComparer.Ordinal);
                if (root.Bucket != null)
                {
                    if (!owned.TryGetValue(root.Bucket, out var paths)) owned[root.Bucket] = paths = new HashSet<string>(StringComparer.Ordinal);
                    paths.UnionWith(OwnershipPolicy.OwnedDependencies(root.Path, root.Bucket, direct, explicitBuckets, boundaries));
                    bucketGroups[root.Bucket] = root;
                }
                rootReport.Add(new JObject { ["path"] = root.Path, ["kind"] = root.Kind, ["group"] = root.Group,
                    ["address"] = root.Address, ["packingBucket"] = root.Bucket, ["recommendedGroup"] = root.RecommendedGroup,
                    ["dependencies"] = new JArray(root.Dependencies), ["explicitDependencyBoundaries"] = new JArray(boundaries.OrderBy(p => p, StringComparer.Ordinal)) });
            }

            var startup = DependenciesOf(roots.Where(r => r.Kind == "builtin_scene"));
            var builtin = DependenciesOf(roots.Where(r => r.Kind.StartsWith("builtin_", StringComparison.Ordinal)));
            var global = DependenciesOf(roots.Where(r => r.Kind == "builtin_global"));
            var localOwned = new HashSet<string>(owned.Where(kv => !bucketGroups[kv.Key].Remote).SelectMany(kv => kv.Value), StringComparer.Ordinal);
            var remoteExplicit = new HashSet<string>(roots.Where(r => r.Remote).Select(r => r.Path), StringComparer.Ordinal);
            var remoteOwners = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var bucket in owned.Where(kv => bucketGroups[kv.Key].Remote))
            foreach (string path in bucket.Value)
            {
                if (!remoteOwners.TryGetValue(path, out var owners)) remoteOwners[path] = owners = new HashSet<string>(StringComparer.Ordinal);
                owners.Add(bucket.Key);
            }
            var rows = new JArray();
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < allAssets.Length; i++)
            {
                string path = allAssets[i];
                if (i % 250 == 0 && EditorUtility.DisplayCancelableProgressBar("资源迁移阶段一检查", path, (float)i / allAssets.Length))
                    throw new OperationCanceledException("Audit cancelled by the user.");
                remoteOwners.TryGetValue(path, out var owners);
                string assignment = OwnershipPolicy.Classify(new OwnershipFacts { Path = path,
                    Startup = startup.Contains(path), GlobalInfrastructure = global.Contains(path), BuiltIn = builtin.Contains(path),
                    LocalAddressable = localOwned.Contains(path), RemoteExplicit = remoteExplicit.Contains(path), RemoteBundleOwners = owners?.Count ?? 0 });
                counts[assignment] = counts.TryGetValue(assignment, out int count) ? count + 1 : 1;
                rows.Add(new JObject { ["path"] = path, ["guid"] = AssetDatabase.AssetPathToGUID(path),
                    ["assetType"] = AssetDatabase.GetMainAssetTypeAtPath(path)?.FullName,
                    ["sourceFileBytes"] = File.Exists(path) ? new FileInfo(path).Length : 0,
                    ["proposedAssignment"] = assignment, ["forcedBuiltin"] = builtin.Contains(path),
                    ["startupDependency"] = startup.Contains(path), ["localAddressableDependency"] = localOwned.Contains(path),
                    ["remotePackingOwners"] = new JArray((owners ?? new HashSet<string>()).OrderBy(o => o, StringComparer.Ordinal)) });
            }
            JObject after = Fingerprint(protectedPaths);
            var changed = new JArray(before.Properties().Where(p => (string)p.Value != (string)after[p.Name]).Select(p => p.Name));
            var changedDependencyHashes = new JArray();
            foreach (JProperty property in rootHashesBefore.Properties())
                if ((string)property.Value != AssetDatabase.GetAssetDependencyHash(property.Name).ToString()) changedDependencyHashes.Add(property.Name);
            if (changed.Count > 0 || changedDependencyHashes.Count > 0)
                errors.Add("Source or imported dependencies changed during the audit; create a new snapshot.");
            JArray dirtyAfter = DirtyScenes();
            if (dirtyAfter.Count > 0) errors.Add("A scene became dirty during the audit; save intended changes and rerun.");
            var shaderSites = new JArray();
            var loadSites = new JArray();
            foreach (string path in allAssets.Where(p => p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) && p.IndexOf("/Editor/", StringComparison.OrdinalIgnoreCase) < 0))
            {
                string[] lines = File.ReadAllLines(path);
                for (int line = 0; line < lines.Length; line++)
                {
                    if (Regex.IsMatch(lines[line], @"Resources\.(Load|LoadAsync|LoadAll)|Addressables\.(Load\w*|Instantiate\w*|Download\w*)|Shader\.Find|AssetBundle\.\w+"))
                        loadSites.Add(new JObject { ["path"] = path, ["line"] = line + 1, ["code"] = lines[line].Trim() });
                    foreach (Match match in Regex.Matches(lines[line], "Shader\\.Find\\(\"([^\"]+)\"\\)"))
                    {
                        Shader shader = Shader.Find(match.Groups[1].Value);
                        shaderSites.Add(new JObject { ["path"] = path, ["line"] = line + 1,
                            ["shaderName"] = match.Groups[1].Value, ["editorResolvedPath"] = shader == null ? null : AssetDatabase.GetAssetPath(shader) });
                    }
                }
            }
            warnings.Add("Packing ownership is predicted from explicit boundaries and group schemas; only a successful Build Layout can confirm actual duplicated bundle bytes.");
            warnings.Add("Pending-review assets are not proven unused. String-based loading sites and dynamic shaders require review before migration.");
            warnings.Add("Source file bytes are not player-build or compressed download sizes.");
            var report = new JObject { ["requestId"] = id, ["status"] = errors.Count == 0 ? "succeeded" : "requires_review",
                ["startedAtUtc"] = started.ToString("O"), ["finishedAtUtc"] = DateTime.UtcNow.ToString("O"),
                ["projectRoot"] = Directory.GetCurrentDirectory(), ["unityVersion"] = Application.unityVersion,
                ["buildTarget"] = EditorUserBuildSettings.activeBuildTarget.ToString(),
                ["method"] = "Unity AssetDatabase.GetDependencies (recursive and direct), GUID and imported dependency hashes",
                ["backupManifest"] = backupManifest, ["activeProfile"] = settings.profileSettings.GetProfileName(settings.activeProfileId),
                ["savedStartupSelection"] = ReadSavedStartupSelection(),
                ["groups"] = groups, ["assetCount"] = rows.Count, ["rootCount"] = roots.Count,
                ["assignmentCounts"] = JObject.FromObject(counts), ["rootsFile"] = "roots.json", ["assetsFile"] = "assets.json",
                ["runtimeLoadSites"] = loadSites, ["dynamicShaderSites"] = shaderSites,
                ["protectedSourceHashesBefore"] = before, ["protectedSourceHashesAfter"] = after,
                ["rootDependencyHashes"] = rootHashesBefore, ["changedSourcesDuringAudit"] = changed,
                ["changedDependenciesDuringAudit"] = changedDependencyHashes, ["dirtyScenesAfterAudit"] = dirtyAfter,
                ["assetMutationPerformed"] = false, ["addressablesBuilt"] = false, ["playerBuilt"] = false, ["published"] = false,
                ["warnings"] = warnings, ["errors"] = errors };
            WriteJson(Path.Combine(output, "roots.json"), rootReport);
            WriteJson(Path.Combine(output, "assets.json"), rows);
            WriteJson(Path.Combine(output, "report.json"), report);
            WriteStatus(id, (string)report["status"], new JObject { ["report"] = Path.Combine(output, "report.json") });
            Debug.Log("[ResourceMigrationStage1] Read-only Unity dependency audit finished: " + Path.Combine(output, "report.json"));
        }
        catch (Exception error)
        {
            WriteStatus(id, error is OperationCanceledException ? "cancelled" : "failed", new JObject { ["error"] = error.ToString() });
            Debug.LogError("[ResourceMigrationStage1] " + error);
        }
        finally { EditorUtility.ClearProgressBar(); running = false; }
    }

    private static HashSet<string> DependenciesOf(IEnumerable<Root> roots) =>
        new HashSet<string>(roots.SelectMany(r => r.Dependencies), StringComparer.Ordinal);

    private static JObject ReadSavedStartupSelection()
    {
        string text = File.ReadAllText(StartupScene);
        string selected = Regex.Match(text, @"(?m)^  selectedServerId:\s*([^\r\n]*)").Groups[1].Value.Trim();
        var entries = new JArray();
        JObject current = null;
        foreach (Match match in Regex.Matches(text, @"  - id: ([^\r\n]+)\r?\n    name: ([^\r\n]+)\r?\n    address: ([^\r\n]+)"))
        {
            string name = match.Groups[2].Value.Trim();
            if (name.StartsWith("\"", StringComparison.Ordinal)) name = JsonConvert.DeserializeObject<string>(name);
            var entry = new JObject { ["id"] = match.Groups[1].Value.Trim(), ["name"] = name, ["address"] = match.Groups[3].Value.Trim() };
            entries.Add(entry);
            if ((string)entry["id"] == selected) current = entry;
        }
        return new JObject { ["scene"] = StartupScene, ["selectedId"] = selected, ["selected"] = current, ["entries"] = entries };
    }

    private static JObject Fingerprint(IEnumerable<string> paths)
    {
        var hashes = new JObject();
        using (var sha = SHA256.Create())
            foreach (string path in paths.OrderBy(p => p, StringComparer.Ordinal))
                using (var stream = File.OpenRead(path))
                    hashes[path] = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        return hashes;
    }

    private static JArray DirtyScenes()
    {
        var result = new JArray();
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (scene.isDirty) result.Add(string.IsNullOrEmpty(scene.path) ? scene.name : scene.path);
        }
        return result;
    }

    private static string OutputDirectory(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new InvalidDataException("Invalid audit output ID.");
        return Path.Combine(Directory.GetCurrentDirectory(), "Build/ResourceMigrationStage1", id);
    }

    private static void WriteStatus(string id, string status, JObject details)
    {
        var record = details ?? new JObject();
        record["requestId"] = id;
        record["status"] = status;
        record["updatedAtUtc"] = DateTime.UtcNow.ToString("O");
        WriteJson(Path.Combine(OutputDirectory(id), "status.json"), record);
    }

    private static void WriteJson(string path, JToken value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, value.ToString(Formatting.Indented));
    }
}
