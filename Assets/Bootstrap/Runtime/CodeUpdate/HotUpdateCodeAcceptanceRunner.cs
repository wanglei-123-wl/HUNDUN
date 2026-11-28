#if QIDAO_HOTUPDATE_PROBE
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace QiDao.Bootstrap.CodeUpdate
{
    public sealed class HotUpdateCodeAcceptanceRunner : MonoBehaviour
    {
        private IEnumerator Start()
        {
            string reportPath = Argument("-hotupdate-probe-report");
            var report = new ProbeReport();
            report.preloadedGameAssembly = AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == CodeUpdateProtocol.AssemblyName);
            CodeUpdateRuntime runtime = null;
            try
            {
                var config = JsonUtility.FromJson<CodeUpdateConfiguration>(Resources.Load<TextAsset>("Startup/HotUpdateBootstrap").text);
                config.manifestUrl = Argument("-hotupdate-probe-url");
                runtime = new CodeUpdateRuntime(config, Argument("-hotupdate-probe-cache"));
            }
            catch (Exception error) { report.error = error.ToString(); }
            if (runtime != null)
            {
                yield return runtime.Run(null, null);
                report.codeLoaded = runtime.Succeeded;
                report.codeStartupConfirmed = runtime.CodeStartupConfirmed;
                report.requiresRestart = runtime.RequiresRestart;
                report.recovered = runtime.UsedRecovery;
                report.releaseId = runtime.SelectedRelease?.releaseId;
                if (!runtime.Succeeded) report.error = runtime.Error;
                else
                {
                    try
                    {
                        if (report.preloadedGameAssembly) throw new InvalidOperationException("Base player contained the game assembly.");
                        if (!report.codeStartupConfirmed) throw new InvalidOperationException("Code startup is still pending before resource loading.");
                        Assembly assembly = runtime.GameAssembly;
                        report.assemblyLocation = assembly.Location;
                        Type probe = assembly.GetType("QiDao.GameLogic.HotCodeProbe", true);
                        report.version = (string)probe.GetProperty("Version").GetValue(null);
                        probe.GetMethod("CompleteGameSceneInitializers").Invoke(null, null);
                        var added = new GameObject("DynamicallyLoadedGameComponent");
                        var component = added.AddComponent(probe);
                        report.addedComponentResult = (string)probe.GetMethod("InvokeGameMethod").Invoke(component, null);
                        var bundle = AssetBundle.LoadFromFile(Argument("-hotupdate-probe-bundle"));
                        if (bundle == null) throw new InvalidDataException("Acceptance asset bundle failed to load.");
                        var prefab = bundle.LoadAsset<GameObject>("HotCodeProbePrefab");
                        var instance = Instantiate(prefab);
                        var restored = instance.GetComponent(probe);
                        if (restored == null) throw new InvalidOperationException("Hot game MonoBehaviour was not restored from the prefab.");
                        report.prefabResult = (string)probe.GetMethod("InvokeGameMethod").Invoke(restored, null);
                        report.awakeVersion = (string)probe.GetProperty("ExecutedVersion").GetValue(restored);
                        var data = bundle.LoadAsset("HotCodeProbeData");
                        if (data == null || data.GetType().Assembly != assembly)
                            throw new InvalidOperationException("Hot game ScriptableObject was not restored.");
                        report.scriptableObjectResult = (string)data.GetType().GetMethod("ReadFromGameCode").Invoke(data, null);
                        if (report.addedComponentResult != "game-" + report.version + "-42"
                            || report.prefabResult != report.addedComponentResult || report.awakeVersion != report.version
                            || report.scriptableObjectResult != report.version + "-73")
                            throw new InvalidOperationException("The player executed stale or incorrect game code.");
                        runtime.MarkHealthy();
                        report.succeeded = true;
                        Destroy(instance);
                        Destroy(added);
                        bundle.Unload(false);
                    }
                    catch (Exception error) { report.error = error.ToString(); }
                }
            }
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
            File.WriteAllText(reportPath, JsonUtility.ToJson(report, true));
            Application.Quit(report.succeeded ? 0 : 1);
        }

        private static string Argument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, name);
            if (index < 0 || index + 1 >= args.Length) throw new ArgumentException("Missing " + name);
            return args[index + 1];
        }

        [Serializable]
        private sealed class ProbeReport
        {
            public bool succeeded, preloadedGameAssembly, codeLoaded, codeStartupConfirmed, requiresRestart, recovered;
            public string error, releaseId, version, assemblyLocation, addedComponentResult, prefabResult, awakeVersion, scriptableObjectResult;
        }
    }
}
#endif
