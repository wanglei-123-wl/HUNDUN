using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

// This phase changes only font ownership and startup text. It never builds,
// publishes, saves scenes, changes endpoints, or moves other game art.
[InitializeOnLoad]
public static class StartupFontMigrationStage2
{
    private const string RequestPath = "Temp/StartupFontMigrationStage2.v2.request.json";
    public const string LocalFontPath = "Assets/Resources/Startup/Fonts/StartupChineseSDF.asset";
    private const string FullFontPath = "Assets/字体/Fonts/NotoSansSC-Regular SDF.asset";
    private const string SourceFontPath = "Assets/字体/Fonts/NotoSansSC-Regular.ttf";
    private const string TmpSettingsPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";
    private const string OldLatinFolder = "Assets/TextMesh Pro/Resources/Fonts & Materials";
    private const string NewLatinFolder = "Assets/TextMesh Pro/GameFonts";
    private const string MobileShader = "Assets/TextMesh Pro/Shaders/TMP_SDF-Mobile.shader";
    private const string RemoteGroup = "RemoteGameFonts";
    private static double nextPoll;

    static StartupFontMigrationStage2()
    {
        if (!Application.isBatchMode) EditorApplication.update += Poll;
    }

    private static void Poll()
    {
        if (EditorApplication.timeSinceStartup < nextPoll) return;
        nextPoll = EditorApplication.timeSinceStartup + 2;
        if (!File.Exists(RequestPath) || EditorApplication.isCompiling || EditorApplication.isUpdating
            || EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer) return;
        string claimed = null, id = null;
        try
        {
            var request = JObject.Parse(File.ReadAllText(RequestPath));
            id = (string)request["requestId"];
            Require(Guid.TryParseExact(id, "N", out _), "Invalid request ID.");
            double remaining = (request.Value<DateTime>("expiresAtUtc").ToUniversalTime() - DateTime.UtcNow).TotalMinutes;
            Require(remaining > 0 && remaining <= 30, "Request expired.");
            Require(string.Equals(Path.GetFullPath((string)request["projectRoot"]).TrimEnd('\\', '/'),
                Directory.GetCurrentDirectory().TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase), "Wrong project.");
            var dirty = Enumerable.Range(0, SceneManager.sceneCount)
                .Select(SceneManager.GetSceneAt).Where(scene => scene.isDirty).Select(scene => scene.path).ToArray();
            if (dirty.Length > 0)
            {
                Write(id, "status", new JObject { ["state"] = "waiting_for_saved_scenes", ["scenes"] = JArray.FromObject(dirty) });
                return;
            }
            claimed = RequestPath + "." + id + ".running";
            File.Move(RequestPath, claimed);
            Write(id, "status", new JObject { ["state"] = "running" });
            var hashes = (JObject)request["expectedHashes"];
            Require(hashes != null && hashes.Count > 0, "A protected-file baseline is required.");
            VerifyHashes(hashes);
            string action = (string)request["action"];
            if (action == "apply")
            {
                Require(File.Exists((string)request["backupManifest"]), "Verified backup manifest is missing.");
                Apply();
            }
            else Require(action == "verify", "Unknown action.");

            JObject report = Verify();
            VerifyHashes(hashes);
            Require(Enumerable.Range(0, SceneManager.sceneCount).All(index => !SceneManager.GetSceneAt(index).isDirty),
                "Validation left an open scene dirty; no scene has been saved.");
            report["protectedFilesUnchanged"] = true;
            report["protectedFiles"] = hashes;
            report["requestId"] = id;
            report["action"] = action;
            report["backupManifest"] = (string)request["backupManifest"];
            report["utc"] = DateTime.UtcNow.ToString("o");
            Write(id, "report", report);
            Write(id, "status", new JObject { ["state"] = "succeeded" });
            Debug.Log("[StartupFontMigrationStage2] Succeeded: Build/ResourceMigrationStage2/" + id + "/report.json");
        }
        catch (Exception error)
        {
            Debug.LogError("[StartupFontMigrationStage2] " + error);
            if (Guid.TryParseExact(id, "N", out _))
                Write(id, "status", new JObject { ["state"] = "failed", ["error"] = error.ToString() });
        }
        finally
        {
            if (claimed != null && File.Exists(claimed)) File.Delete(claimed);
        }
    }

    private static void Apply()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        Require(settings != null && settings.FindGroup(RemoteGroup) == null, "Font migration already exists or settings are missing.");
        Require(!File.Exists(LocalFontPath) && !AssetDatabase.IsValidFolder(NewLatinFolder), "Refusing to overwrite existing font assets.");
        var local = settings.FindGroup("BootstrapShared");
        var template = settings.FindGroup("RemoteWorldMaps");
        var tmp = AssetDatabase.LoadAssetAtPath<TMP_Settings>(TmpSettingsPath);
        var full = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FullFontPath);
        var source = AssetDatabase.LoadAssetAtPath<Font>(SourceFontPath);
        Require(local != null && template != null && tmp != null && full != null && source != null, "Font inputs are missing.");
        Require(local.entries.Count == 2 && local.entries.Any(entry => entry.AssetPath == FullFontPath)
            && local.entries.Any(entry => entry.AssetPath == MobileShader), "Review changed local font ownership first.");
        Require(TMP_Settings.defaultFontAsset == full && TMP_Settings.fallbackFontAssets.All(font => font == full), "Review changed TMP defaults first.");
        Require(AssetDatabase.IsValidFolder(OldLatinFolder), "Legacy TMP font folder is missing.");
        foreach (var asset in new UnityEngine.Object[] { settings, local, tmp, full, template })
            Require(!EditorUtility.IsDirty(asset), "Save the intended asset changes before migration: " + asset.name);

        var small = TMP_FontAsset.CreateFontAsset(source, 48, 5, GlyphRenderMode.SDFAA, 1024, 1024,
            AtlasPopulationMode.Dynamic, false);
        Require(small != null, "Cannot create startup font.");
        try
        {
            Require(small.TryAddCharacters(StartupUiText.FontCharacters, out string missing),
                "Startup font has missing glyphs: " + missing);
            small.name = "StartupChineseSDF";
            small.atlasPopulationMode = AtlasPopulationMode.Static;
            // Clear even editor-only source references so dependency audits cannot
            // mistake the static font for a dependency on the complete source TTF.
            var serialized = new SerializedObject(small);
            serialized.FindProperty("m_SourceFontFile_EditorRef").objectReferenceValue = null;
            serialized.FindProperty("m_SourceFontFileGUID").stringValue = "";
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EnsureFolder("Assets/Resources/Startup/Fonts");
            AssetDatabase.CreateAsset(small, LocalFontPath);
            small.material.name = "StartupChineseSDF Material";
            AssetDatabase.AddObjectToAsset(small.material, small);
            foreach (Texture2D atlas in small.atlasTextures)
            {
                atlas.name = "StartupChineseSDF Atlas";
                AssetDatabase.AddObjectToAsset(atlas, small);
                EditorUtility.SetDirty(atlas);
            }
            EditorUtility.SetDirty(small);
            AssetDatabase.SaveAssetIfDirty(small);
        }
        catch
        {
            if (!AssetDatabase.Contains(small)) UnityEngine.Object.DestroyImmediate(small);
            throw;
        }

        // Native MoveAsset keeps every original GUID, including material presets.
        string moveError = AssetDatabase.MoveAsset(OldLatinFolder, NewLatinFolder);
        Require(string.IsNullOrEmpty(moveError), moveError);
        var tmpSerialized = new SerializedObject(tmp);
        tmpSerialized.FindProperty("m_defaultFontAsset").objectReferenceValue = small;
        tmpSerialized.FindProperty("m_fallbackFontAssets").arraySize = 0;
        tmpSerialized.FindProperty("m_defaultFontAssetPath").stringValue = "Startup/Fonts/";
        tmpSerialized.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssetIfDirty(tmp);

        var remote = settings.CreateGroup(RemoteGroup, false, false, true, null,
            typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
        var bundle = remote.GetSchema<BundledAssetGroupSchema>();
        Require(bundle.BuildPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteBuildPath)
            && bundle.LoadPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteLoadPath), "Remote font paths unavailable.");
        bundle.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackTogether;
        bundle.Timeout = 10;
        var entry = settings.CreateOrMoveEntry(AssetDatabase.AssetPathToGUID(FullFontPath), remote);
        entry.address = RemoteGameContent.BootstrapFontAddress;
        settings.RemoveAssetEntry(AssetDatabase.AssetPathToGUID(MobileShader));
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.BatchModification, remote, true, true);
        foreach (var asset in new UnityEngine.Object[] { local, remote, bundle, remote.GetSchema<ContentUpdateGroupSchema>(), settings })
        {
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssetIfDirty(asset);
        }
    }

    private static JObject Verify()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        var small = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(LocalFontPath);
        var full = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FullFontPath);
        Require(small != null && full != null, "Missing font assets.");
        Require(TMP_Settings.defaultFontAsset == small && TMP_Settings.fallbackFontAssets.Count == 0, "Saved TMP defaults must reference only the startup font.");
        Require(small.atlasPopulationMode == AtlasPopulationMode.Static && small.sourceFontFile == null,
            "Startup font must be static and independent of the source font.");
        Require(small.HasCharacters(StartupUiText.FontCharacters, out uint[] missing, false, false),
            "Startup glyph coverage failed: " + string.Join(",", missing ?? Array.Empty<uint>()));
        string[] localDependencies = AssetDatabase.GetDependencies(LocalFontPath, true);
        Require(!localDependencies.Contains(FullFontPath) && !localDependencies.Contains(SourceFontPath), "Startup font pulls in the complete font.");
        Require(settings.FindGroup("BootstrapShared").entries.Count == 0, "Obsolete local font bundle entries remain.");
        var entry = settings.FindAssetEntry(AssetDatabase.AssetPathToGUID(FullFontPath));
        Require(entry != null && entry.parentGroup.Name == RemoteGroup
            && entry.address == RemoteGameContent.BootstrapFontAddress, "Remote game font address changed.");
        var schema = entry.parentGroup.GetSchema<BundledAssetGroupSchema>();
        Require(schema.IncludeInBuild && schema.LoadPath.Id == settings.RemoteCatalogLoadPath.Id, "Game font is not remotely loaded.");

        var builtinRoots = AssetDatabase.GetAllAssetPaths().Where(path => !AssetDatabase.IsValidFolder(path)
            && path.StartsWith("Assets/", StringComparison.Ordinal) && path.Contains("/Resources/")).ToList();
        builtinRoots.AddRange(EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path));
        foreach (string config in new[] { "ProjectSettings/GraphicsSettings.asset", "ProjectSettings/QualitySettings.asset", "ProjectSettings/ProjectSettings.asset" })
        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(config), @"\bguid:\s*([0-9a-fA-F]{32})"))
        {
            string path = AssetDatabase.GUIDToAssetPath(match.Groups[1].Value);
            if (!string.IsNullOrEmpty(path)) builtinRoots.Add(path);
        }
        string[] forced = AssetDatabase.GetDependencies(builtinRoots.Distinct().ToArray(), true);
        Require(!forced.Contains(FullFontPath) && !forced.Contains(SourceFontPath), "A built-in root still pulls in the complete Chinese font.");
        Require(!forced.Any(path => path.StartsWith(NewLatinFolder + "/", StringComparison.Ordinal)), "A built-in root still forces the game Latin font into the player.");

        JObject runtimeTests = CheckRuntime(full, small);
        return new JObject
        {
            ["localFont"] = LocalFontPath, ["localFontGuid"] = AssetDatabase.AssetPathToGUID(LocalFontPath),
            ["localFontSourceFileBytes"] = new FileInfo(LocalFontPath).Length,
            ["localFontCharacters"] = small.characterTable.Count, ["requiredCharacters"] = StartupUiText.FontCharacters.Length,
            ["missingCharacters"] = new JArray(), ["localFontAtlasCount"] = small.atlasTextureCount,
            ["localFontAtlasWidth"] = small.atlasWidth, ["localFontAtlasHeight"] = small.atlasHeight,
            ["localFontDependencies"] = JArray.FromObject(localDependencies),
            ["remoteFontGroup"] = RemoteGroup, ["remoteFontAddress"] = entry.address,
            ["remoteFontGuid"] = entry.guid, ["remoteFontSourceFileBytes"] = new FileInfo(FullFontPath).Length,
            ["latinFontsMovedTo"] = NewLatinFolder,
            ["latinFontAssets"] = JArray.FromObject(AssetDatabase.FindAssets("", new[] { NewLatinFolder })
                .Select(guid => new { guid, path = AssetDatabase.GUIDToAssetPath(guid) })),
            ["builtinRoots"] = JArray.FromObject(builtinRoots.Distinct().OrderBy(path => path)),
            ["completeChineseFontForcedBuiltin"] = false,
            ["runtimeTests"] = runtimeTests,
            ["validationLimit"] = "Native glyph, dependency, retry UI and font-lifetime checks; remote bundles, release player and clean-machine tests belong to later phases."
        };
    }

    private static JObject CheckRuntime(TMP_FontAsset full, TMP_FontAsset small)
    {
        const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
        var handleField = typeof(GameFontService).GetField("fontHandle", flags);
        Require(!((AsyncOperationHandle<TMP_FontAsset>)handleField.GetValue(null)).IsValid(), "Stop the active font session before validation.");
        int fallbackCount = TMP_Settings.fallbackFontAssets.Count;
        GameObject host = null;
        var preview = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        try
        {
            // Exercise failure without contacting the resource server.
            var exceptionHandler = UnityEngine.ResourceManagement.ResourceManager.ExceptionHandler;
            try
            {
                UnityEngine.ResourceManagement.ResourceManager.ExceptionHandler = null;
                handleField.SetValue(null, Addressables.ResourceManager.CreateCompletedOperation<TMP_FontAsset>(null, "Simulated offline font failure"));
                Drain(GameFontService.Load());
            }
            finally { UnityEngine.ResourceManagement.ResourceManager.ExceptionHandler = exceptionHandler; }
            Require(!GameFontService.IsReady && !((AsyncOperationHandle<TMP_FontAsset>)handleField.GetValue(null)).IsValid()
                && TMP_Settings.fallbackFontAssets.Count == fallbackCount, "Failed font load was retained instead of allowing retry.");
            handleField.SetValue(null, Addressables.ResourceManager.CreateCompletedOperation(full, (string)null));
            Drain(GameFontService.Load());
            Drain(GameFontService.Load());
            Require(GameFontService.IsReady && TMP_Settings.fallbackFontAssets.Count == fallbackCount + 1,
                "Game fallback registration must be idempotent.");

            // Create only temporary objects in a preview scene; never save or load a user's scene.
            host = new GameObject("Stage2 font validation") { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(host, preview);
            var updater = host.AddComponent<StartupContentUpdate>();
            IEnumerator run = updater.Run();
            Require(run.MoveNext(), "Updater did not create its local view.");
            var status = host.GetComponentsInChildren<TMP_Text>(true).First(text => text.gameObject.name == "更新状态");
            var retry = host.GetComponentInChildren<Button>(true);
            Require(status.font == small && retry != null, "Startup view depends on the remote font.");
            typeof(StartupContentUpdate).GetMethod("Fail", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(updater, new object[] { StartupUiText.InitializationFailed });
            Require(run.MoveNext() && retry.gameObject.activeSelf && status.text == StartupUiText.InitializationFailed + StartupUiText.RetrySuffix,
                "Offline failure did not expose the local Chinese retry view.");
            retry.onClick.Invoke();
            Require(run.MoveNext() && !retry.gameObject.activeSelf, "Retry did not restart the updater.");
            UnityEngine.Object.DestroyImmediate(host);
            host = null;
            Require(GameFontService.IsReady, "Destroying the startup object released the game font.");

            uint probe = full.characterTable.Select(character => character.unicode)
                .First(code => code >= 0x4e00 && code <= 0x9fff && !small.HasCharacter((int)code));
            Require(small.HasCharacters(char.ConvertFromUtf32((int)probe), out uint[] missing, true, false),
                "Runtime-created UI cannot resolve game Chinese through the registered fallback.");
            GameFontService.Release();
            Require(!GameFontService.IsReady && TMP_Settings.fallbackFontAssets.Count == fallbackCount,
                "Session cleanup retained a font handle or fallback reference.");
            return new JObject { ["failedLoadReleasesHandle"] = true, ["retryRestartsUpdater"] = true,
                ["offlineChineseUsesLocalFont"] = true, ["startupDestroyKeepsGameFont"] = true,
                ["fallbackRegistrationIsIdempotent"] = true, ["dynamicUiResolvesChinese"] = true,
                ["sessionCleanupReleasesFont"] = true, ["fallbackProbeUnicode"] = probe };
        }
        finally
        {
            if (host != null) UnityEngine.Object.DestroyImmediate(host);
            GameFontService.Release();
            UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(preview);
        }
    }

    private static void Drain(IEnumerator operation)
    {
        int steps = 0;
        while (operation.MoveNext()) Require(++steps < 5, "Completed font operation did not finish.");
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = path.Substring(0, path.LastIndexOf('/'));
        EnsureFolder(parent);
        Require(!string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, Path.GetFileName(path))), "Cannot create " + path);
    }

    private static void VerifyHashes(JObject hashes)
    {
        foreach (var file in hashes.Properties())
        {
            string path = Path.GetFullPath(file.Name);
            Require(path.StartsWith(Directory.GetCurrentDirectory() + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "Protected path escapes project.");
            using (var sha = SHA256.Create())
                Require(BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant() == (string)file.Value,
                    "Protected file changed: " + file.Name);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Write(string id, string name, JObject data)
    {
        string directory = Path.Combine("Build", "ResourceMigrationStage2", id);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name + ".json"), data.ToString(Formatting.Indented));
    }
}
