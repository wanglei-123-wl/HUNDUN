using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

// Copied into an isolated editor only. This tool never builds resources/players.
public static class PackingMetadataMigration
{
    static string Hash(string path) { using(var sha=SHA256.Create()) using(var s=File.OpenRead(path))
        return BitConverter.ToString(sha.ComputeHash(s)).Replace("-","").ToLowerInvariant(); }
    static void Check(bool ok,string error) { if(!ok) throw new InvalidOperationException(error); }
    public static void Apply()
    {
        Check(Path.GetFullPath(Application.dataPath).StartsWith(@"C:\QiDaoBuild\",StringComparison.OrdinalIgnoreCase),"Use an isolated editor.");
        var args=Environment.GetCommandLineArgs();string request=args[Array.IndexOf(args,"-packing-request")+1];
        var plan=JObject.Parse(File.ReadAllText(request));string output=args[Array.IndexOf(args,"-packing-output")+1];
        Check((string)plan["status"]=="prepared" || (string)plan["status"]=="prepared-metadata-only-build-held","Invalid plan state.");
        Check(plan.Value<bool>("metadataOnlyMigration"),"Only metadata migration is supported.");
        Check(Hash((string)plan["preflightReport"])==(string)plan["preflightSha256"],"Preflight record changed.");
        string source=(string)plan["sourceRoot"];
        var settings=AddressableAssetSettingsDefaultObject.Settings;Check(settings!=null,"Missing settings.");
        // Check only importer metadata/GUIDs; this operation does not read or write image/model contents.
        foreach(JObject row in plan["entries"])
        {
            string path=(string)row["path"];
            Check(AssetDatabase.AssetPathToGUID(path)==(string)row["guid"],"GUID differs: "+path);
            Check(Hash(Path.Combine(source,path+".meta"))==(string)row["metaSha256"],"Source importer metadata differs: "+path);
            Check(Hash(path+".meta")== (string)row["metaSha256"],"Isolated importer metadata differs: "+path);
        }
        var old=new JArray();
        foreach(JObject original in plan["existingAddresses"])
        {
            string guid=AssetDatabase.AssetPathToGUID((string)original["path"]);var e=settings.FindAssetEntry(guid);
            Check(e!=null && e.address==(string)original["address"],"Existing address differs: "+original["path"]);
            old.Add(new JObject { ["guid"]=guid,["address"]=e.address,["labels"]=new JArray(e.labels.OrderBy(x=>x,StringComparer.Ordinal)) });
        }
        var importedBefore=new JObject();
        foreach(JObject row in plan["entries"]) importedBefore[(string)row["path"]]=AssetDatabase.GetAssetDependencyHash((string)row["path"]).ToString();
        var changed=new HashSet<UnityEngine.Object>();
        foreach(JObject definition in plan["newGroups"])
        {
            string name=(string)definition["name"];var group=settings.FindGroup(name);
            if(group==null) group=settings.CreateGroup(name,false,false,false,null,typeof(BundledAssetGroupSchema),typeof(ContentUpdateGroupSchema));
            var schema=group.GetSchema<BundledAssetGroupSchema>();
            Check(schema.BuildPath.SetVariableByName(settings,AddressableAssetSettings.kRemoteBuildPath)
                && schema.LoadPath.SetVariableByName(settings,AddressableAssetSettings.kRemoteLoadPath),"Missing remote profile variables.");
            schema.BundleMode=BundledAssetGroupSchema.BundlePackingMode.PackTogether;
            schema.InternalBundleIdMode=BundledAssetGroupSchema.BundleInternalIdMode.GroupGuid;
            schema.BundleNaming=BundledAssetGroupSchema.BundleNamingStyle.AppendHash;
            schema.Compression=BundledAssetGroupSchema.BundleCompressionMode.LZ4;
            schema.UseAssetBundleCache=true;schema.UseAssetBundleCrc=true;schema.UseAssetBundleCrcForCachedBundles=true;
            schema.AssetBundledCacheClearBehavior=BundledAssetGroupSchema.CacheClearBehavior.ClearWhenSpaceIsNeededInCache;
            schema.IncludeInBuild=true;schema.Timeout=10;
            group.GetSchema<ContentUpdateGroupSchema>().StaticContent=false;
            changed.Add(group);changed.Add(schema);changed.Add(group.GetSchema<ContentUpdateGroupSchema>());
        }
        foreach(JObject change in plan["existingSchemaOverrides"])
        {
            var group=settings.FindGroup((string)change["group"]);Check(group!=null,"Missing original group.");
            var schema=group.GetSchema<BundledAssetGroupSchema>();schema.BundleMode=BundledAssetGroupSchema.BundlePackingMode.PackSeparately;changed.Add(schema);
        }
        foreach(JObject row in plan["entries"])
        {
            string guid=(string)row["guid"];var target=settings.FindGroup((string)row["targetGroup"]);Check(target!=null,"Missing target group.");
            var current=settings.FindAssetEntry(guid);if(current!=null) changed.Add(current.parentGroup);
            var entry=settings.CreateOrMoveEntry(guid,target,false,false);entry.SetAddress((string)row["address"],false);changed.Add(target);
        }
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.BatchModification,null,true,true);changed.Add(settings);
        foreach(var asset in changed) { EditorUtility.SetDirty(asset);AssetDatabase.SaveAssetIfDirty(asset); }
        foreach(JObject original in old)
        {
            var e=settings.FindAssetEntry((string)original["guid"]);Check(e!=null && e.address==(string)original["address"],"Original address lost.");
            Check(e.labels.OrderBy(x=>x,StringComparer.Ordinal).SequenceEqual(original["labels"].Values<string>()),"Original labels lost.");
        }
        foreach(JProperty item in importedBefore.Properties())
            Check(AssetDatabase.GetAssetDependencyHash(item.Name).ToString()==(string)item.Value,"Imported content changed: "+item.Name);
        foreach(JObject row in plan["entries"])
        {
            var e=settings.FindAssetEntry((string)row["guid"]);
            Check(e.parentGroup.Name==(string)row["targetGroup"] && e.address==(string)row["address"],"Migration differs from plan.");
        }
        var report=new JObject { ["status"]="succeeded",["existingAddressesAndLabelsPreserved"]=old.Count,["plannedEntries"]=plan["entries"].Count(),
            ["newGroups"]=plan["newGroups"].Count(),["importedDependencyHashesUnchanged"]=true,["onlyAddressablesMetadataSaved"]=true,
            ["resourceBuildPerformed"]=false,["playerBuildPerformed"]=false,["resourceBuildReadinessVerified"]=plan["resourceBuildReadinessVerified"] };
        File.WriteAllText(output,report.ToString());Debug.Log("PACKING_METADATA_MIGRATION_VERIFIED");
    }
}
