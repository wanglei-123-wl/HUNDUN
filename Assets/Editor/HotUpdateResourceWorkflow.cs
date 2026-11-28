using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEditor.Build;
using UnityEngine;

public static class HotUpdateResourceWorkflow
{
    static void Require(bool value,string message) { if(!value) throw new BuildFailedException(message); }
    public static void VerifyInputs(JObject request)
    {
        if(request["workflowProtocol"]==null) return; // Historical acceptance requests remain readable.
        Require(request.Value<int>("workflowProtocol")==1,"Unsupported resource workflow.");
        string file=(string)request["sourceSnapshotPath"];
        Require(ResourceReleaseFiles.Hash(file)==(string)request["sourceSnapshotSha256"],"Source verification report changed.");
        var snapshot=JObject.Parse(File.ReadAllText(file));
        Require((string)snapshot["kind"]=="QiDaoResourceInputVerification" && (string)snapshot["status"]=="succeeded","Source reads were not verified.");
        foreach(JProperty item in ((JObject)snapshot["files"]).Properties())
        {
            string relative=item.Name;
            Require(!relative.Contains("\\") && !relative.Split('/').Any(x=>x==".." || x==".")
                && new[]{"Assets/","Packages/","ProjectSettings/"}.Any(relative.StartsWith),"Invalid source input path.");
            Require(File.Exists(relative) && new FileInfo(relative).Length==item.Value.Value<long>("bytes")
                && ResourceReleaseFiles.Hash(relative)==(string)item.Value["sha256"],"Isolated build input differs: "+relative);
        }
        if(request.Value<bool>("acceptanceOnly"))
            Require(string.Equals(Path.GetFullPath(Application.dataPath),@"C:\QiDaoBuild\HotUpdateMigration-Stage2\Assets",StringComparison.OrdinalIgnoreCase),"Acceptance workflow is restricted to the fixture project.");
    }
    public static JObject Capture(AddressableAssetSettings settings,string typeHash)
    {
        var groups=new JObject();var entries=new JObject();
        foreach(var group in settings.groups.Where(g=>g!=null))
        {
            var s=group.GetSchema<BundledAssetGroupSchema>();if(s==null || !s.IncludeInBuild) continue;
            groups[group.Guid]=new JObject { ["name"]=group.Name,["mode"]=s.BundleMode.ToString(),["compression"]=s.Compression.ToString(),
                ["internalBundleId"]=s.InternalBundleIdMode.ToString(),["internalAssetId"]=s.InternalIdNamingMode.ToString(),
                ["bundleNaming"]=s.BundleNaming.ToString(),["cache"]=s.UseAssetBundleCache,["crc"]=s.UseAssetBundleCrc,
                ["includeAddress"]=s.IncludeAddressInCatalog,["includeGuid"]=s.IncludeGUIDInCatalog,["includeLabels"]=s.IncludeLabelsInCatalog,
                ["forceUniqueProvider"]=s.ForceUniqueProvider,["assetLoadMode"]=s.AssetLoadMode.ToString(),
                ["cachedCrc"]=s.UseAssetBundleCrcForCachedBundles,["cacheClear"]=s.AssetBundledCacheClearBehavior.ToString(),
                ["staticContent"]=group.GetSchema<ContentUpdateGroupSchema>()?.StaticContent ?? false };
            foreach(var e in group.entries)
            {
                var value=new JObject { ["group"]=group.Guid,["address"]=e.address };
                if(s.BundleMode==BundledAssetGroupSchema.BundlePackingMode.PackTogetherByLabel) value["labels"]=new JArray(e.labels.OrderBy(x=>x,StringComparer.Ordinal));
                entries[e.guid]=value;
            }
        }
        return new JObject { ["schemaVersion"]=1,["unityVersion"]=Application.unityVersion,
            ["addressablesVersion"]=UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(AddressableAssetSettings).Assembly).version,
            ["scriptTypeHash"]=typeHash,["uniqueBundleIds"]=settings.UniqueBundleIds,["playerVersion"]=settings.OverridePlayerVersion,
            ["buildSettings"]=new JObject { ["contiguousBundles"]=settings.ContiguousBundles,["nonRecursiveBuilding"]=settings.NonRecursiveBuilding,
                ["disableVisibleSubAssets"]=settings.DisableVisibleSubAssetRepresentations,["ignoreUnsupportedFiles"]=settings.IgnoreUnsupportedFilesInBuild,
                ["shaderBundleNaming"]=settings.ShaderBundleNaming.ToString(),["monoScriptBundleNaming"]=settings.MonoScriptBundleNaming.ToString() },
            ["groups"]=groups,["entries"]=entries };
    }
    public static void VerifyUpdate(JObject request,JObject current)
    {
        string contract=(string)request["baselineLayoutContract"];
        Require(ResourceReleaseFiles.Hash(contract)==(string)request["baselineLayoutContractSha256"],"Baseline layout contract changed.");
        Require(ResourceReleaseFiles.Hash((string)request["baselineContentState"])==(string)request["baselineContentStateSha256"],"Original baseline content state changed.");
        var old=JObject.Parse(File.ReadAllText(contract));
        foreach(string key in new[]{"unityVersion","addressablesVersion","scriptTypeHash","uniqueBundleIds","playerVersion","buildSettings"})
            Require(JToken.DeepEquals(old[key],current[key]),"Resource baseline compatibility changed ("+key+"); create a fresh resource baseline.");
        foreach(JProperty group in ((JObject)old["groups"]).Properties())
            Require(JToken.DeepEquals(group.Value,current["groups"][group.Name]),"Existing group identity/settings changed; create a baseline: "+group.Name);
        foreach(JProperty entry in ((JObject)old["entries"]).Properties())
        {
            var next=current["entries"][entry.Name];
            if(next==null && string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(entry.Name))) continue;
            Require(JToken.DeepEquals(entry.Value,next),"Existing address/group changed; create a baseline: "+entry.Name);
        }
    }
}
