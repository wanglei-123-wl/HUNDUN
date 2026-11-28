using System;
using System.Collections;
using System.IO;
using System.Linq;
using QiDao.Bootstrap.Contracts;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.SceneManagement;

namespace QiDao.Bootstrap.CodeUpdate
{
    public sealed class ResourceBindingAcceptanceRunner : MonoBehaviour
    {
        private readonly Result report = new Result();
        private IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            string root = Arg("-resource-probe-cache");
            var config = JsonUtility.FromJson<CodeUpdateConfiguration>(Resources.Load<TextAsset>("Startup/HotUpdateBootstrap").text);
            if (!StartupResourceEndpoint.TryApply(string.Empty, out string endpointError))
            { Finish("Empty bootstrap catalog lost its default server: " + endpointError); yield break; }
            string server = new Uri(Arg("-resource-probe-url")).GetLeftPart(UriPartial.Authority) + "/StandaloneWindows64";
            if (!StartupResourceEndpoint.TryApply(server, out endpointError)
                || StartupResourceEndpoint.ResolveCodeManifestAddress(config.manifestUrl)
                    != new Uri(new Uri(Arg("-resource-probe-url")), CodeUpdateProtocol.ChannelPath + "/current.json").AbsoluteUri)
            { Finish("Server selection did not route the separate code channel: " + endpointError); yield break; }
            config.manifestUrl = Arg("-resource-probe-url");
            var code = new CodeUpdateRuntime(config, Path.Combine(root, "Code"));
            yield return code.Run(null, null);
            if (code.ExecutionHandedOff) { yield return code.WaitForExecutionChild(); yield break; }
            report.codeLoaded = code.Succeeded;
            report.recovered = code.UsedRecovery;
            if (!code.Succeeded) { Finish(code.Error); yield break; }
            Directory.CreateDirectory(Path.Combine(root, "Bundles"));
            var cache = Caching.AddCache(Path.Combine(root, "Bundles"));
            if (!cache.valid) throw new InvalidOperationException("Isolated Unity cache could not be created.");
            Caching.currentCacheForWriting = cache;
            report.preloadedVerifiedCode = !string.IsNullOrEmpty(code.GameAssembly.Location);
            report.activationId = code.SelectedRelease.releaseId;
            report.resourceId = code.SelectedRelease.resourceReleaseId;
            report.codeSha256 = code.SelectedRelease.codeSha256;
            var initialize = Addressables.InitializeAsync(false);
            yield return initialize;
            bool initialized = initialize.Status == AsyncOperationStatus.Succeeded;
            Addressables.Release(initialize);
            if (!initialized) { Finish("Addressables initialization failed."); yield break; }
            var pin = new PinnedResourceCatalog(code.SelectedRelease, code.PublicationUri, code.CacheRoot);
            yield return pin.Run();
            report.pinned = pin.Succeeded;
            if (!pin.Succeeded)
            {
                if (pin.IntegrityFailure) code.RequireResourceRecovery(pin.Error);
                Finish(pin.Error); yield break;
            }
            bool full = CodeUpdateRuntime.HasArgument("-resource-probe-full");
            using (var services = GameStartupServices.Create())
            {
                var required = full ? services.RequiredResourceAddresses.ToArray() : new[] { "fixture/prefab", "fixture/data" };
                var plan = StartupResourceDownloadPlan.Create(new[] { pin.Locator }, required);
                report.bundleCount = plan.RemoteBundles.Count;
                using (var lease = new StartupResourceCacheLease())
                {
                yield return lease.Acquire();
                var download = new StartupResourceDownloadOperation(plan.DownloadLocations, null);
                yield return download.Run();
                report.downloadBytes = download.PendingBytes;
                if (!download.Succeeded) { Finish(download.Error); yield break; }
                var verification = new StartupResourceCacheVerification(plan, null);
                yield return verification.Run();
                report.cacheVerified = verification.Succeeded;
                if (!verification.Succeeded) { Finish(verification.Error); yield break; }
                }
                if (full)
                {
                    yield return services.PrepareContent(null);
                    if (!services.Succeeded)
                    {
                        var inspect = Addressables.LoadAssetAsync<UnityEngine.Object>("game/items/table");
                        yield return inspect;
                        if (inspect.Status == AsyncOperationStatus.Succeeded && inspect.Result != null)
                        {
                            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
                            foreach (var field in inspect.Result.GetType().GetFields(flags))
                            {
                                var value = field.GetValue(inspect.Result);
                                if (value is System.Collections.ICollection collection)
                                    Debug.Log("[ResourceProbe] Table field " + field.Name + " count=" + collection.Count);
                                if (field.Name == "itemAssets" && value is System.Collections.IList assets)
                                    foreach (var item in assets.Cast<object>().Take(2))
                                    {
                                        if (item == null) { Debug.Log("[ResourceProbe] Null item asset"); continue; }
                                        var definition = item.GetType().GetField("definition", flags)?.GetValue(item);
                                        Debug.Log("[ResourceProbe] Item=" + item + " definition=" + (definition == null ? "null" : JsonUtility.ToJson(definition)));
                                    }
                            }
                            Debug.Log("[ResourceProbe] Table JSON=" + JsonUtility.ToJson(inspect.Result).Substring(0, Math.Min(500, JsonUtility.ToJson(inspect.Result).Length)));
                        }
                        if (inspect.IsValid()) Addressables.Release(inspect);
                        Finish(services.FailureMessage); yield break;
                    }
                    report.gameContentPrepared = true;
                    foreach (string address in required.Where(a => a.StartsWith("game/map-traversal/", StringComparison.Ordinal)).Distinct())
                    {
                        string mapId = address.Substring("game/map-traversal/".Length);
                        string sceneId = null;
                        var catalog = code.GameAssembly.GetType("WorldMapSceneCatalog", true);
                        foreach (string sceneAddress in required.Where(a => a.StartsWith("game/scene/", StringComparison.Ordinal)))
                        {
                            object[] parameters = { sceneAddress, null, null };
                            if ((bool)catalog.GetMethod("TryGetMapByAddress").Invoke(null, parameters) && (string)parameters[2] == mapId)
                                sceneId = (string)parameters[1];
                        }
                        var map = Addressables.LoadAssetAsync<TextAsset>(address);
                        yield return map;
                        byte[] archive = map.Status == AsyncOperationStatus.Succeeded && map.Result != null ? map.Result.bytes : null;
                        if (map.IsValid()) Addressables.Release(map);
                        if (archive == null || sceneId == null) { Finish("Map traversal data did not load: " + address); yield break; }
                        var loader = code.GameAssembly.GetType("Game.Client.MapTraversal.MapTraversalPackage", true).GetMethod("Load");
                        var parse = System.Threading.Tasks.Task.Run(() => loader.Invoke(null,
                            new object[] { archive, sceneId, mapId, System.Threading.CancellationToken.None }));
                        while (!parse.IsCompleted) yield return null;
                        if (parse.IsFaulted || parse.Result == null || parse.Result.GetType().Assembly != code.GameAssembly)
                        { Finish("Map traversal package did not deserialize: " + address + " " + parse.Exception); yield break; }
                        report.mapDataCount++;
                    }
                }
                else
                {
                    var prefabHandle = Addressables.LoadAssetAsync<GameObject>("fixture/prefab");
                    yield return prefabHandle;
                    var dataHandle = Addressables.LoadAssetAsync<UnityEngine.Object>("fixture/data");
                    yield return dataHandle;
                    if (prefabHandle.Status != AsyncOperationStatus.Succeeded || dataHandle.Status != AsyncOperationStatus.Succeeded)
                    { Finish("Fixture assets failed."); yield break; }
                    var instance = Instantiate(prefabHandle.Result);
                    var type = code.GameAssembly.GetType("QiDao.GameLogic.HotCodeProbe", true);
                    var component = instance.GetComponent(type);
                    if (component == null) { Finish("Pinned prefab lost its hot component."); yield break; }
                    report.codeVersion = (string)type.GetProperty("Version").GetValue(null);
                    report.marker = (int)dataHandle.Result.GetType().GetField("marker").GetValue(dataHandle.Result);
                    object nested = dataHandle.Result.GetType().GetField("nested").GetValue(dataHandle.Result);
                    report.nestedDataRestored = nested != null
                        && (string)nested.GetType().GetField("text").GetValue(nested) == "nested-code-data"
                        && ((System.Collections.IList)nested.GetType().GetField("values").GetValue(nested)).Count == 2;
                    if (!report.nestedDataRestored) { Finish("Nested hot-code serializable data was lost."); yield break; }
                    report.fixtureAssetsLoaded = true;
                    Destroy(instance);
                    Addressables.Release(prefabHandle);
                    Addressables.Release(dataHandle);
                }
                yield return pin.ConfirmBeforeLogin();
                if (!pin.Succeeded) { Finish(pin.Error); yield break; }
                code.MarkResourcesReady();
                report.readyPairCommitted = true;
                if (full)
                {
                    yield return services.OpenLoginScene();
                    report.loginScene = SceneManager.GetActiveScene().name;
                    if (report.loginScene != "SampleScene") { Finish("Login scene did not open."); yield break; }
                }
            }
            report.succeeded = true;
            Finish(null);
        }

        private void Finish(string error)
        {
            report.error = error;
            File.WriteAllText(Arg("-resource-probe-report"), JsonUtility.ToJson(report, true));
            Application.Quit(report.succeeded ? 0 : 1);
        }
        private static string Arg(string name)
        {
            string[] args = Environment.GetCommandLineArgs(); int i = Array.IndexOf(args, name);
            if (i < 0 || i + 1 >= args.Length) throw new ArgumentException(name);
            return args[i + 1];
        }
        [Serializable]
        private sealed class Result
        {
            public bool succeeded, codeLoaded, recovered, pinned, cacheVerified, fixtureAssetsLoaded, gameContentPrepared, readyPairCommitted, preloadedVerifiedCode, nestedDataRestored;
            public string error, activationId, resourceId, codeSha256, codeVersion, loginScene;
            public int marker, bundleCount, mapDataCount;
            public long downloadBytes;
        }
    }
}
