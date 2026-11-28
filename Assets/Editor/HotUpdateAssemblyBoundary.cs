using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Player;
using UnityEditor.Build.Reporting;
using UnityEditor.Compilation;
using UnityEngine;

public sealed class HotUpdateAssemblyBoundary : IPreprocessBuildWithReport
{
    public int callbackOrder => -1000;
    public void OnPreprocessBuild(BuildReport report) => VerifyCompiledBoundary();

    [MenuItem("Tools/Hot Update/Verify Assembly Boundary")]
    public static void VerifyCompiledBoundary()
    {
        var assemblies = CompilationPipeline.GetAssemblies(AssembliesType.Player);
        string projectPrefix = Path.GetDirectoryName(Application.dataPath).Replace('\\', '/') + "/";
        foreach (var assembly in assemblies)
        {
            string[] sources = assembly.sourceFiles.Select(p => p.Replace('\\', '/'))
                .Select(p => p.StartsWith(projectPrefix, StringComparison.OrdinalIgnoreCase) ? p.Substring(projectPrefix.Length) : p)
                .Where(p => p.StartsWith("Assets/", StringComparison.Ordinal)).ToArray();
            if (sources.Length == 0) continue;
            if (assembly.name != "QiDao.GameLogic" && assembly.name != "QiDao.Bootstrap"
                && assembly.name != "QiDao.Contracts" && assembly.name != "EZhex1991.EZSoftBone")
                throw new BuildFailedException("Unclassified player assembly: " + assembly.name);
            if (sources.Any(p => p.Split('/').Any(part => part.Equals("Editor", StringComparison.OrdinalIgnoreCase))))
                throw new BuildFailedException("Editor source in player assembly: " + assembly.name);
            if (assembly.name == "QiDao.Bootstrap" && sources.Any(p => !p.StartsWith("Assets/Bootstrap/Runtime/", StringComparison.Ordinal)))
                throw new BuildFailedException("Business code was placed in the fixed bootstrap assembly.");
            if (assembly.name == "QiDao.Contracts" && sources.Any(p => !p.StartsWith("Assets/Bootstrap/Contracts/", StringComparison.Ordinal)))
                throw new BuildFailedException("Business code was placed in fixed contracts.");
            if ((assembly.name == "QiDao.Bootstrap" || assembly.name == "QiDao.Contracts")
                && assembly.allReferences.Any(p => Path.GetFileNameWithoutExtension(p) == "QiDao.GameLogic"))
                throw new BuildFailedException("Bootstrap cannot depend on the game assembly.");
        }
        foreach (string required in new[] { "QiDao.GameLogic", "QiDao.Bootstrap", "QiDao.Contracts" })
            if (!assemblies.Any(a => a.name == required)) throw new BuildFailedException("Assembly missing: " + required);
    }

    // Code-only validation project: compile both Editor and Windows Player code
    // and resolve the original script GUIDs, without building or changing bundles.
    public static void VerifyStage1()
    {
        string output = GetArgument("-hotUpdateStage1Output");
        string inventory = GetArgument("-hotUpdateStage1Inventory");
        var report = new JObject();
        try
        {
            VerifyCompiledBoundary();
            var sourceReport = JObject.Parse(File.ReadAllText(inventory));
            if ((string)sourceReport["status"] != "succeeded")
                throw new InvalidOperationException("Source assembly inventory did not pass.");
            var owners = ((JArray)sourceReport["scripts"]).Cast<JObject>()
                .ToDictionary(s => (string)s["guid"], s => (string)s["assembly"]);
            var bindings = new JArray();
            foreach (JObject binding in (JArray)sourceReport["assetScriptBindings"])
            {
                string path = AssetDatabase.GUIDToAssetPath((string)binding["guid"]);
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                Type type = script == null ? null : script.GetClass();
                if (type == null) throw new InvalidOperationException("Asset script does not resolve: " + binding["path"]);
                if (type.Assembly.GetName().Name != owners[(string)binding["guid"]])
                    throw new InvalidOperationException("Asset script has the wrong assembly: " + path);
                bindings.Add(new JObject { ["path"] = path, ["type"] = type.FullName,
                    ["assembly"] = type.Assembly.GetName().Name, ["guid"] = binding["guid"] });
            }
            report["assetScriptBindings"] = bindings;
            report["playerSourceOwnership"] = new JArray(CompilationPipeline.GetAssemblies(AssembliesType.Player)
                .Where(a => a.name.StartsWith("QiDao.", StringComparison.Ordinal))
                .Select(a => new JObject { ["assembly"] = a.name, ["sources"] = new JArray(a.sourceFiles) }));
            report["editorAssemblyReferences"] = new JObject
            {
                ["bootstrap"] = new JArray(typeof(StartupContentUpdate).Assembly.GetReferencedAssemblies().Select(a => a.Name)),
                ["contracts"] = new JArray(typeof(QiDao.Bootstrap.Contracts.GameStartupServices).Assembly.GetReferencedAssemblies().Select(a => a.Name))
            };
            string playerOutput = Path.Combine(Path.GetDirectoryName(output), "PlayerAssemblies");
            Directory.CreateDirectory(playerOutput);
            var result = PlayerBuildInterface.CompilePlayerScripts(new ScriptCompilationSettings
            {
                target = BuildTarget.StandaloneWindows64, group = BuildTargetGroup.Standalone,
                options = ScriptCompilationOptions.None
            }, playerOutput);
            if (result.assemblies == null || result.assemblies.Count == 0)
                throw new InvalidOperationException("Windows Player script compilation failed.");
            report["playerAssemblies"] = new JArray(result.assemblies);
            report["status"] = "succeeded";
        }
        catch (Exception error)
        {
            report["status"] = "failed";
            report["error"] = error.ToString();
            throw;
        }
        finally
        {
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, report.ToString());
        }
    }

    private static string GetArgument(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, name);
        if (index < 0 || index + 1 >= args.Length) throw new ArgumentException("Missing " + name);
        return args[index + 1];
    }
}
