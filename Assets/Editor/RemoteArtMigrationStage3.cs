using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ResourceMigrationStage3;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;
using UnityEngine.SceneManagement;

// Only Addressables ownership changes. Source art, scenes, endpoints, builds
// and uploads are deliberately outside this phase's mutation methods.
[InitializeOnLoad]
public static class RemoteArtMigrationStage3
{
    private const string RequestPath = "Temp/RemoteArtMigrationStage3.v3.request.json";
    private static double nextPoll;
    private sealed class Root
    {
        public string Path, Guid, Address, Group, Bucket, ProposedGroup, ProposedBucket;
        public string[] Labels;
    }
    private sealed class Snapshot
    {
        public AddressableAssetSettings Settings;
        public List<Root> Roots;
        public Dictionary<string, string[]> Direct;
        public HashSet<string> Builtin, Required, Eligible;
        public JObject DependencyHashes, EntryBaseline, SchemaBaseline;
        public PackingPlan Plan;
        public string Fingerprint;
    }

    static RemoteArtMigrationStage3()
    {
        if (!Application.isBatchMode) EditorApplication.update += Poll;
    }

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 2;
        if (!File.Exists(RequestPath) || EditorApplication.isCompiling || EditorApplication.isUpdating
            || EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer) return;
        string id = null, claimed = null;
        try
        {
            var request = JObject.Parse(File.ReadAllText(RequestPath));
            id = (string)request["requestId"];
            Require(Guid.TryParseExact(id, "N", out _), "Invalid request ID.");
            double minutes = (request.Value<DateTime>("expiresAtUtc").ToUniversalTime() - DateTime.UtcNow).TotalMinutes;
            Require(minutes > 0 && minutes <= 30, "Expired request.");
            Require(string.Equals(Path.GetFullPath((string)request["projectRoot"]).TrimEnd('\\', '/'),
                Directory.GetCurrentDirectory().TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase), "Wrong project.");
            if (DirtyScenes().Length > 0)
            {
                Write(id, "status", new JObject { ["state"] = "waiting_for_saved_scenes", ["scenes"] = JArray.FromObject(DirtyScenes()) });
                return;
            }
            claimed = RequestPath + "." + id + ".running";
            File.Move(RequestPath, claimed);
            Write(id, "status", new JObject { ["state"] = "running" });
            var protectedFiles = (JObject)request["expectedHashes"];
            VerifyProtected(protectedFiles);
            Snapshot before = Capture();
            string action = (string)request["action"];
            JObject report = Describe(before);
            if (action == "apply")
            {
                Require(File.Exists((string)request["backupManifest"]), "Backup manifest missing.");
                Require((string)request["planFingerprint"] == before.Fingerprint, "Dependencies or groups changed after inspection; inspect again.");
                Require(before.Settings.FindGroup(PackingPlanner.RequiredSharedGroup) == null
                    && before.Settings.FindGroup(PackingPlanner.DeferredSharedGroup) == null, "Shared groups already exist; review before applying again.");
                Apply(before);
                Snapshot after = Capture();
                Verify(before, after);
                report["after"] = Describe(after);
                report["originalAssetDependencyHashesUnchanged"] = true;
                report["existingAddressesAndLabelsUnchanged"] = true;
                report["sourceAssetsMovedOrDeleted"] = false;
                report["predictedSharedCandidatesAfter"] = after.Plan.Promotions.Count;
            }
            else if (action == "verify")
            {
                string originalPath = Path.GetFullPath((string)request["originalReportPath"]);
                Require(originalPath.StartsWith(Path.GetFullPath("Build/ResourceMigrationStage3") + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase), "Original report is outside the inspection directory.");
                byte[] originalBytes = File.ReadAllBytes(originalPath);
                Require(Hash(originalBytes) == (string)request["originalReportSha256"], "Original inspection report changed.");
                var original = JObject.Parse(Encoding.UTF8.GetString(originalBytes));
                Verify(FromReport(original), before);
                report = (JObject)original.DeepClone();
                report["after"] = Describe(before);
                report["originalAssetDependencyHashesUnchanged"] = true;
                report["existingAddressesAndLabelsUnchanged"] = true;
                report["sourceAssetsMovedOrDeleted"] = false;
                report["predictedSharedCandidatesAfter"] = before.Plan.Promotions.Count;
                report["startupFontNormalization"] = VerifyStartupFont();
            }
            else Require(action == "inspect", "Unknown action.");
            VerifyProtected(protectedFiles);
            Require(DirtyScenes().Length == 0, "A scene became dirty; no scene has been saved.");
            report["requestId"] = id;
            report["action"] = action;
            report["backupManifest"] = (string)request["backupManifest"];
            report["protectedSourceFilesUnchanged"] = true;
            report["utc"] = DateTime.UtcNow.ToString("o");
            report["buildPerformed"] = false;
            report["published"] = false;
            report["verificationLimit"] = "Dependency ownership prediction only; actual bundle duplication, shader stripping, sizes and remote downloads require a fresh build in stage four.";
            Write(id, "report", report);
            Write(id, "status", new JObject { ["state"] = "succeeded" });
            Debug.Log("[RemoteArtMigrationStage3] " + action + " succeeded: Build/ResourceMigrationStage3/" + id + "/report.json");
        }
        catch (Exception error)
        {
            Debug.LogError("[RemoteArtMigrationStage3] " + error);
            if (Guid.TryParseExact(id, "N", out _)) Write(id, "status", new JObject { ["state"] = "failed", ["error"] = error.ToString() });
        }
        finally { if (claimed != null && File.Exists(claimed)) File.Delete(claimed); }
    }

    private static Snapshot Capture()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        Require(settings != null && settings.FindGroup("RemoteGameFonts") != null, "Complete the font phase first.");
        var roots = new List<Root>();
        var schemas = new JObject();
        foreach (var group in settings.groups.Where(group => group != null).OrderBy(group => group.Name, StringComparer.Ordinal))
        {
            var schema = group.GetSchema<BundledAssetGroupSchema>();
            if (schema == null || !schema.IncludeInBuild) continue;
            Require(!EditorUtility.IsDirty(group) && !EditorUtility.IsDirty(schema), "Unsaved group changes: " + group.Name);
            bool remote = schema.LoadPath.Id == settings.RemoteCatalogLoadPath.Id;
            schemas[group.Name] = new JObject { ["buildPath"] = schema.BuildPath.Id, ["loadPath"] = schema.LoadPath.Id,
                ["packing"] = schema.BundleMode.ToString(), ["include"] = schema.IncludeInBuild };
            if (!remote)
            {
                Require(group.entries.Count == 0, "Review unexpected local Addressables entries: " + group.Name);
                continue;
            }
            foreach (var entry in group.entries.OrderBy(entry => entry.guid, StringComparer.Ordinal))
            {
                Require(!AssetDatabase.IsValidFolder(entry.AssetPath), "Folder entries need separate review: " + entry.AssetPath);
                string proposed = WorldMapSceneCatalog.TryGetMapByAddress(entry.address, out _, out _)
                    || entry.address.StartsWith("game/map-traversal/", StringComparison.Ordinal) ? "RemoteWorldMaps" : group.Name;
                var target = settings.FindGroup(proposed);
                Require(target != null, "Target group missing: " + proposed);
                roots.Add(new Root { Path = entry.AssetPath, Guid = entry.guid, Address = entry.address, Group = group.Name,
                    Labels = entry.labels.OrderBy(label => label, StringComparer.Ordinal).ToArray(),
                    Bucket = Bucket(group.Name, schema, entry), ProposedGroup = proposed,
                    ProposedBucket = Bucket(proposed, target.GetSchema<BundledAssetGroupSchema>(), entry) });
            }
        }
        Require(roots.Select(root => root.Path).Distinct(StringComparer.Ordinal).Count() == roots.Count
            && roots.Select(root => root.Address).Distinct(StringComparer.Ordinal).Count() == roots.Count, "Duplicate remote paths or addresses.");
        var allPaths = AssetDatabase.GetAllAssetPaths().Where(path => path.StartsWith("Assets/", StringComparison.Ordinal)
            && !AssetDatabase.IsValidFolder(path)).ToArray();
        var builtinRoots = allPaths.Where(path => path.Contains("/Resources/")).ToList();
        builtinRoots.AddRange(EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path));
        foreach (string config in new[] { "ProjectSettings/GraphicsSettings.asset", "ProjectSettings/QualitySettings.asset", "ProjectSettings/ProjectSettings.asset" })
        foreach (Match match in Regex.Matches(File.ReadAllText(config), @"\bguid:\s*([0-9a-fA-F]{32})"))
        {
            string path = AssetDatabase.GUIDToAssetPath(match.Groups[1].Value);
            if (!string.IsNullOrEmpty(path)) builtinRoots.Add(path);
        }
        var builtin = new HashSet<string>(AssetDatabase.GetDependencies(builtinRoots.Distinct().ToArray(), true), StringComparer.Ordinal);
        Require(roots.All(root => !builtin.Contains(root.Path)), "A remote root is still forced into the player.");
        var direct = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var required = new HashSet<string>(StringComparer.Ordinal);
        var hashes = new JObject();
        var entries = new JObject();
        foreach (var root in roots)
        {
            Require(File.Exists(root.Path) && AssetDatabase.AssetPathToGUID(root.Path) == root.Guid, "Unresolved root: " + root.Path);
            string[] closure = AssetDatabase.GetDependencies(root.Path, true);
            if (root.Labels.Contains(RemoteGameContent.RequiredContentLabel) || root.Address == RemoteGameContent.BootstrapFontAddress
                || root.Group == "RemoteTrialOnly") required.UnionWith(closure);
            foreach (string path in closure)
                if (!direct.ContainsKey(path)) direct[path] = path.StartsWith("Assets/", StringComparison.Ordinal) || path.StartsWith("Packages/", StringComparison.Ordinal)
                    ? AssetDatabase.GetDependencies(path, false).Where(child => child != path).ToArray() : Array.Empty<string>();
            hashes[root.Path] = AssetDatabase.GetAssetDependencyHash(root.Path).ToString();
            entries[root.Guid] = new JObject { ["path"] = root.Path, ["address"] = root.Address,
                ["labels"] = JArray.FromObject(root.Labels), ["group"] = root.Group };
        }
        Require(roots.Count(root => root.Labels.Contains(RemoteGameContent.RequiredContentLabel)) == 5,
            "Startup required-content label must still contain its original five roots.");
        var eligible = new HashSet<string>(allPaths.Where(path => !builtin.Contains(path) && !roots.Any(root => root.Path == path)
            && path.IndexOf("/Editor/", StringComparison.OrdinalIgnoreCase) < 0
            && !Regex.IsMatch(path, @"\.(cs|asmdef|asmref|dll|unity|lighting)$", RegexOptions.IgnoreCase)
            && !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path))), StringComparer.Ordinal);
        PackingPlan plan = PackingPlanner.Create(roots.Select(root => new PackingRoot { Path = root.Path, Bucket = root.ProposedBucket }), direct, eligible, required);
        var fingerprintData = new JObject { ["entries"] = entries, ["schemas"] = schemas, ["dependencies"] = hashes,
            ["moves"] = JArray.FromObject(roots.Where(root => root.Group != root.ProposedGroup)
                .Select(root => new { root.Guid, root.ProposedGroup })), ["promotions"] = JArray.FromObject(plan.Promotions) };
        return new Snapshot { Settings = settings, Roots = roots, Direct = direct, Builtin = builtin, Eligible = eligible,
            Required = required, DependencyHashes = hashes, EntryBaseline = entries, SchemaBaseline = schemas, Plan = plan,
            Fingerprint = Hash(Encoding.UTF8.GetBytes(fingerprintData.ToString(Formatting.None))) };
    }

    private static string Bucket(string group, BundledAssetGroupSchema schema, AddressableAssetEntry entry)
    {
        string pack = schema.BundleMode == BundledAssetGroupSchema.BundlePackingMode.PackSeparately ? entry.guid
            : schema.BundleMode == BundledAssetGroupSchema.BundlePackingMode.PackTogetherByLabel
                ? string.Join("|", entry.labels.OrderBy(label => label, StringComparer.Ordinal)) : "together";
        return group + "/" + pack + (entry.AssetPath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) ? "/scenes" : "/assets");
    }

    private static JObject Describe(Snapshot snapshot)
    {
        var owners = PackingPlanner.Owners(snapshot.Roots.Select(root => new PackingRoot { Path = root.Path, Bucket = root.Bucket }), snapshot.Direct);
        return new JObject {
            ["planFingerprint"] = snapshot.Fingerprint,
            ["rootCount"] = snapshot.Roots.Count,
            ["rootMoves"] = JArray.FromObject(snapshot.Roots.Where(root => root.Group != root.ProposedGroup)
                .Select(root => new { root.Path, root.Guid, root.Address, From = root.Group, To = root.ProposedGroup, root.Labels })),
            ["sharedPromotions"] = JArray.FromObject(snapshot.Plan.Promotions.Select(item => new {
                item.Path, Guid = AssetDatabase.AssetPathToGUID(item.Path), item.Group, item.OriginalOwners,
                SourceFileBytes = new FileInfo(item.Path).Length,
                Address = "game/shared/" + AssetDatabase.AssetPathToGUID(item.Path) })),
            ["predictedSharedAssetsBeforePromotion"] = snapshot.Plan.Before.Count(pair => pair.Value.Count > 1 && snapshot.Eligible.Contains(pair.Key)),
            ["predictedSharedAssetsAfterPromotion"] = snapshot.Plan.After.Count(pair => pair.Value.Count > 1 && snapshot.Eligible.Contains(pair.Key)),
            ["bakeSettingsNotPromoted"] = JArray.FromObject(snapshot.Plan.Before.Keys.Where(path => path.EndsWith(".lighting", StringComparison.OrdinalIgnoreCase))),
            ["bakeSettingsReference"] = "https://docs.unity3d.com/cn/2022.3/ScriptReference/LightingSettings.html",
            ["retainedBuiltinOverlaps"] = JArray.FromObject(owners.Where(pair => snapshot.Builtin.Contains(pair.Key)
                && pair.Key.StartsWith("Assets/", StringComparison.Ordinal) && !pair.Key.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
                .OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => new { Path = pair.Key, Owners = pair.Value.OrderBy(owner => owner, StringComparer.Ordinal) })),
            ["remoteEntries"] = snapshot.EntryBaseline, ["groupSchemas"] = snapshot.SchemaBaseline,
            ["originalDependencyHashes"] = snapshot.DependencyHashes
        };
    }

    private static void Apply(Snapshot snapshot)
    {
        var settings = snapshot.Settings;
        Require(!EditorUtility.IsDirty(settings), "Save intended Addressables settings first.");
        var changed = new HashSet<UnityEngine.Object>();
        foreach (var root in snapshot.Roots.Where(root => root.Group != root.ProposedGroup))
        {
            changed.Add(settings.FindGroup(root.Group));
            var target = settings.FindGroup(root.ProposedGroup);
            settings.CreateOrMoveEntry(root.Guid, target);
            changed.Add(target);
        }
        foreach (var promotion in snapshot.Plan.Promotions)
        {
            var group = settings.FindGroup(promotion.Group);
            if (group == null)
            {
                group = settings.CreateGroup(promotion.Group, false, false, true, null,
                    typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
                var bundle = group.GetSchema<BundledAssetGroupSchema>();
                Require(bundle.BuildPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteBuildPath)
                    && bundle.LoadPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteLoadPath), "Remote shared paths unavailable.");
                bundle.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackTogether;
                bundle.Timeout = 10;
                changed.Add(bundle);
                changed.Add(group.GetSchema<ContentUpdateGroupSchema>());
            }
            string guid = AssetDatabase.AssetPathToGUID(promotion.Path);
            Require(settings.FindAssetEntry(guid) == null, "Promotion already owned: " + promotion.Path);
            var entry = settings.CreateOrMoveEntry(guid, group);
            entry.address = "game/shared/" + guid;
            changed.Add(group);
        }
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.BatchModification, null, true, true);
        changed.Add(settings);
        foreach (var asset in changed)
        {
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
        }
    }

    private static void Verify(Snapshot before, Snapshot after)
    {
        foreach (JProperty property in before.DependencyHashes.Properties())
            Require((string)after.DependencyHashes[property.Name] == (string)property.Value, "Source/imported dependencies changed: " + property.Name);
        foreach (var root in before.Roots)
        {
            Root actual = after.Roots.Single(item => item.Guid == root.Guid);
            Require(actual.Path == root.Path && actual.Address == root.Address && actual.Labels.SequenceEqual(root.Labels)
                && actual.Group == root.ProposedGroup, "Existing entry changed unexpectedly: " + root.Path);
        }
        foreach (var item in before.Plan.Promotions)
        {
            Root actual = after.Roots.Single(root => root.Path == item.Path);
            Require(actual.Group == item.Group && actual.Address == "game/shared/" + actual.Guid && actual.Labels.Length == 0,
                "Shared asset ownership is invalid: " + item.Path);
        }
        Require(after.Roots.Count == before.Roots.Count + before.Plan.Promotions.Count
            && after.Plan.Promotions.Count == 0 && after.Roots.All(root => root.Group == root.ProposedGroup), "Migration left unresolved ownership changes.");
        Require(after.Settings.FindGroup("RemoteWorldMaps").GetSchema<BundledAssetGroupSchema>().BundleMode
            == BundledAssetGroupSchema.BundlePackingMode.PackSeparately, "Map bundles must remain separate.");
        // A shared game bundle must not acquire an implicit dependency on a
        // deferred shared-map bundle (which would download unrelated future art).
        foreach (var game in after.Roots.Where(root => root.Group == PackingPlanner.RequiredSharedGroup))
        {
            var closure = new HashSet<string>(AssetDatabase.GetDependencies(game.Path, true), StringComparer.Ordinal);
            Require(!after.Roots.Any(root => root.Group == PackingPlanner.DeferredSharedGroup && closure.Contains(root.Path)),
                "Initial shared content depends on deferred shared-map content.");
        }
    }

    private static Snapshot FromReport(JObject report)
    {
        var moves = ((JArray)report["rootMoves"]).ToDictionary(item => (string)item["Guid"], item => (string)item["To"], StringComparer.Ordinal);
        return new Snapshot {
            Roots = ((JObject)report["remoteEntries"]).Properties().Select(item => new Root {
                Guid = item.Name, Path = (string)item.Value["path"], Address = (string)item.Value["address"],
                Group = (string)item.Value["group"], Labels = item.Value["labels"].ToObject<string[]>(),
                ProposedGroup = moves.TryGetValue(item.Name, out string group) ? group : (string)item.Value["group"]
            }).ToList(),
            DependencyHashes = (JObject)report["originalDependencyHashes"],
            Plan = new PackingPlan { Promotions = ((JArray)report["sharedPromotions"]).Select(item =>
                new SharedPromotion { Path = (string)item["Path"], Group = (string)item["Group"] }).ToList() }
        };
    }

    private static JObject VerifyStartupFont()
    {
        const string path = "Assets/Resources/Startup/Fonts/StartupChineseSDF.asset";
        var font = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(path);
        Require(font != null && TMPro.TMP_Settings.defaultFontAsset == font
            && font.atlasPopulationMode == TMPro.AtlasPopulationMode.Static && font.sourceFontFile == null,
            "Local startup font ownership changed.");
        Require(font.HasCharacters(StartupUiText.FontCharacters, out uint[] missing, false, false),
            "Startup glyphs are missing: " + string.Join(",", missing ?? Array.Empty<uint>()));
        Require(font.hashCode == TMPro.TMP_TextUtilities.GetSimpleHashCode(font.name)
            && font.materialHashCode == TMPro.TMP_TextUtilities.GetSimpleHashCode(font.name + " Atlas Material"),
            "TMP font lookup hashes are not normalized.");
        var expected = new Material(font.material) { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            TMPro.ShaderUtilities.UpdateShaderRatios(expected);
            foreach (string property in new[] { "_ScaleRatioA", "_ScaleRatioB", "_ScaleRatioC" })
                Require(Mathf.Approximately(font.material.GetFloat(property), expected.GetFloat(property)), "TMP material ratio changed: " + property);
        }
        finally { UnityEngine.Object.DestroyImmediate(expected); }
        return new JObject { ["glyphCoverageComplete"] = true, ["computedHashesMatchTMP"] = true,
            ["computedMaterialRatiosMatchTMP"] = true, ["staticLocalFontPreserved"] = true };
    }

    private static string[] DirtyScenes() => Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt)
        .Where(scene => scene.isDirty).Select(scene => string.IsNullOrEmpty(scene.path) ? scene.name : scene.path).ToArray();

    private static void VerifyProtected(JObject expected)
    {
        Require(expected != null && expected.Count > 0, "Protected-file baseline missing.");
        foreach (JProperty file in expected.Properties())
        {
            string path = Path.GetFullPath(file.Name);
            Require(path.StartsWith(Directory.GetCurrentDirectory() + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "Protected path escapes project.");
            Require(File.Exists(path) && Hash(File.ReadAllBytes(path)) == (string)file.Value, "Protected source changed: " + file.Name);
        }
    }
    private static string Hash(byte[] data)
    {
        using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static void Write(string id, string name, JObject data)
    {
        string directory = Path.Combine("Build", "ResourceMigrationStage3", id);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name + ".json"), data.ToString(Formatting.Indented));
    }
}
