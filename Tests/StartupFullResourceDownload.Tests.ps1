$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$temporary = Join-Path $root ('Temp/FullStartupTests-' + [guid]::NewGuid().ToString('N') + '.cs')
$harness = @'
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;
namespace UnityEngine.ResourceManagement.ResourceLocations {
    public interface IResourceLocation {
        string InternalId { get; } string ProviderId { get; } Type ResourceType { get; }
        object Data { get; set; } IList<IResourceLocation> Dependencies { get; } bool HasDependencies { get; }
    }
    public sealed class ResourceLocationBase : IResourceLocation {
        public string InternalId { get; } public string ProviderId { get; } public Type ResourceType { get; }
        public object Data { get; set; } public IList<IResourceLocation> Dependencies { get; }
        public bool HasDependencies => Dependencies.Count > 0;
        public ResourceLocationBase(string key,string id,string provider,Type type,params IResourceLocation[] deps) {
            if(string.IsNullOrEmpty(provider))throw new ArgumentNullException("providerId");
            InternalId=id; ProviderId=provider; ResourceType=type; Dependencies=new List<IResourceLocation>(deps);
        }
    }
}
namespace UnityEngine.ResourceManagement.ResourceProviders {
    public interface IAssetBundleResource { }
    public sealed class AssetBundleRequestOptions { public string Hash; public long BundleSize; public uint Crc; }
}
namespace UnityEngine.AddressableAssets.ResourceLocators {
    public interface IResourceLocator {
        string LocatorId { get; } IEnumerable<object> Keys { get; }
        bool Locate(object key, Type type, out IList<IResourceLocation> locations);
    }
}
namespace UnityEngine.ResourceManagement.AsyncOperations {
    public enum AsyncOperationStatus { None,Succeeded,Failed }
    public sealed class State<T> { public bool Valid=true,Done; public T Result; public long Bytes; public AsyncOperationStatus Status; }
    public struct DownloadStatus { public long DownloadedBytes; }
    public struct AsyncOperationHandle<T> {
        public State<T> State; public bool IsValid()=>State!=null&&State.Valid;
        public T Result=>State.Result; public AsyncOperationStatus Status=>State.Status;
        public Exception OperationException=>State.Status==AsyncOperationStatus.Failed?new Exception("injected failure"):null;
    }
    public struct AsyncOperationHandle {
        public State<object> State; public bool IsValid()=>State!=null&&State.Valid; public bool IsDone=>State.Done;
        public AsyncOperationStatus Status=>State.Status;
        public Exception OperationException=>State.Status==AsyncOperationStatus.Failed?new Exception("injected failure"):null;
        public DownloadStatus GetDownloadStatus()=>new DownloadStatus {DownloadedBytes=State.Bytes};
    }
}
namespace UnityEngine.AddressableAssets {
    public static class Addressables {
        public static Queue<long?> Sizes; public static int Downloads,Releases; public static bool DownloadFails;
        public static State<object> Active; public static IList<IResourceLocation> LastDownload;
        public static void Reset(params long?[] sizes) { Sizes=new Queue<long?>(sizes); Downloads=Releases=0; DownloadFails=false; Active=null; LastDownload=null; }
        public static AsyncOperationHandle<long> GetDownloadSizeAsync(IEnumerable keys) {
            long? value=Sizes.Dequeue(); return new AsyncOperationHandle<long>{State=new State<long>{Done=true,
                Result=value??0,Status=value.HasValue?AsyncOperationStatus.Succeeded:AsyncOperationStatus.Failed}};
        }
        public static AsyncOperationHandle DownloadDependenciesAsync(IList<IResourceLocation> locations,bool autoRelease) {
            if(autoRelease)throw new Exception("The gate must retain handles until inspected");
            Downloads++;LastDownload=locations;Active=new State<object>();return new AsyncOperationHandle{State=Active};
        }
        public static void Tick(){if(Active!=null){Active.Bytes=100;Active.Status=DownloadFails?AsyncOperationStatus.Failed:AsyncOperationStatus.Succeeded;Active.Done=true;}}
        public static void Release<T>(AsyncOperationHandle<T> h){if(!h.IsValid())throw new Exception("double release");h.State.Valid=false;Releases++;}
        public static void Release(AsyncOperationHandle h){if(!h.IsValid())throw new Exception("double release");h.State.Valid=false;Releases++;}
    }
}
public static class RemoteGameContent {
    public const string LoginSceneAddress="game/scene/login", WorldSceneAddress="game/scene/world",
        ItemTableAddress="game/items/table", BootstrapFontAddress="bootstrap/font/chinese";
}
public sealed class TestLocator:IResourceLocator {
    public string LocatorId=>"test";public readonly Dictionary<object,IList<IResourceLocation>> Entries=new Dictionary<object,IList<IResourceLocation>>();
    public IEnumerable<object> Keys=>Entries.Keys;
    public bool Locate(object key,Type type,out IList<IResourceLocation> locations)=>Entries.TryGetValue(key,out locations);
    public void Add(string key,IResourceLocation value)=>Entries[key]=new List<IResourceLocation>{value};
}
public static class FullStartupBehaviorChecks {
    private static int count;
    static void Check(bool value,string message){count++;if(!value)throw new Exception(message);}
    static ResourceLocationBase Bundle(string name,string hash="v1")=>new ResourceLocationBase(name,"https://resources.test/"+name,"bundles",typeof(IAssetBundleResource))
        {Data=new AssetBundleRequestOptions{Hash=hash,BundleSize=123,Crc=456}};
    static ResourceLocationBase Asset(string name,params IResourceLocation[] deps)=>new ResourceLocationBase(name,name,"assets",typeof(object),deps);
    static TestLocator Catalog(IResourceLocation common) {
        var locator=new TestLocator();
        var required=new[]{RemoteGameContent.LoginSceneAddress,RemoteGameContent.WorldSceneAddress,RemoteGameContent.ItemTableAddress,RemoteGameContent.BootstrapFontAddress}
            .Concat(WorldMapSceneCatalog.GetAllResourceAddresses());
        foreach(string key in required)locator.Add(key,Asset(key,common));return locator;
    }
    static void Drain(IEnumerator routine){int steps=0;while(routine.MoveNext()){Check(++steps<30,"download coroutine did not terminate");Addressables.Tick();}}
    static bool Rejected(TestLocator locator) {try{StartupResourceDownloadPlan.Create(new[]{locator});return false;}catch(InvalidOperationException){return true;}}
    public static int Run() {
        count=0;
        var shared=Bundle("shared.bundle");var map=Bundle("map.bundle");map.Dependencies.Add(shared);
        var locator=Catalog(map);locator.Add("alias",Asset("alias",Bundle("shared.bundle")));
        var future=Bundle("future-unlabelled.bundle");locator.Add("new-content",Asset("new-content",future));
        var direct=Bundle("direct-key.bundle");locator.Add("direct-key",direct);
        var local=new ResourceLocationBase("local","{RuntimePath}/local.bundle","bundles",typeof(IAssetBundleResource)){Data=new AssetBundleRequestOptions{Hash="local",BundleSize=1}};
        locator.Add("local",local);shared.Dependencies.Add(map);
        var plan=StartupResourceDownloadPlan.Create(new[]{locator});
        Check(plan.RemoteBundles.Count==4,"aliases, shared dependencies, cycles and future content must produce exactly four remote bundles");
        Check(plan.DownloadLocations.Count==1&&plan.DownloadLocations[0].Dependencies.Count==4,"size and download must share the complete dependency root, including direct bundle keys");
        Check(plan.RemoteBundles.Contains(future)&&plan.RemoteBundles.Contains(direct),"unlabelled resources and direct bundle keys were omitted");
        var all=WorldMapSceneCatalog.GetAllResourceAddresses().ToArray();
        Check(all.Contains("game/scene/world005")&&all.Contains("game/map-traversal/map_005"),"Yaoling map scene and traversal data must be required");
        foreach(string address in all){var missing=Catalog(map);missing.Entries.Remove(address);Check(Rejected(missing),"Missing required map address was accepted: "+address);}
        var conflict=Catalog(map);conflict.Add("conflict",Asset("conflict",Bundle("shared.bundle","v2")));
        Check(Rejected(conflict),"conflicting metadata for the same bundle URL must fail closed");

        Addressables.Reset(0,0);var warm=new StartupResourceDownloadOperation(plan.DownloadLocations,null);Drain(warm.Run());
        Check(warm.Succeeded&&Addressables.Downloads==0&&Addressables.Releases==2,"unchanged cached resources must not redownload on restart");
        Addressables.Reset(100,0);var progress=new List<long>();var cold=new StartupResourceDownloadOperation(plan.DownloadLocations,(done,total)=>progress.Add(done));
        Drain(cold.Run());Check(cold.Succeeded&&Addressables.Downloads==1&&Addressables.Releases==3,"cold start must download and verify all dependencies, then release all handles");
        Check(object.ReferenceEquals(Addressables.LastDownload,plan.DownloadLocations)&&progress.Last()==100,"download scope and byte progress must match the plan");
        Addressables.Reset((long?)null);var failedSize=new StartupResourceDownloadOperation(plan.DownloadLocations,null);Drain(failedSize.Run());
        Check(!failedSize.Succeeded&&Addressables.Downloads==0&&Addressables.Releases==1,"size failure must block completion");
        Addressables.Reset(100);Addressables.DownloadFails=true;var failed=new StartupResourceDownloadOperation(plan.DownloadLocations,null);Drain(failed.Run());
        Check(!failed.Succeeded&&Addressables.Releases==2,"download failure must block completion and release handles");
        Addressables.Reset(100,7);var incomplete=new StartupResourceDownloadOperation(plan.DownloadLocations,null);Drain(incomplete.Run());
        Check(!incomplete.Succeeded&&incomplete.Error.Contains("incomplete"),"a success response with missing cache bytes must not open login");
        Addressables.Reset(100,0);var retry=new StartupResourceDownloadOperation(plan.DownloadLocations,null);Drain(retry.Run());
        Check(retry.Succeeded,"a failed attempt must allow a fresh successful retry");
        Addressables.Reset(100);var cancelled=new StartupResourceDownloadOperation(plan.DownloadLocations,null);var run=cancelled.Run();
        run.MoveNext();run.MoveNext();((IDisposable)run).Dispose();
        Check(!cancelled.Succeeded&&Addressables.Releases==2,"cancelling in-flight download must release its owned handles");
        return count;
    }
}
'@
try {
    [IO.File]::WriteAllText($temporary,$harness,[Text.UTF8Encoding]::new($false))
    Add-Type -Path @($temporary,(Join-Path $root 'Assets/Scripts/WorldMapSceneCatalog.cs'),
        (Join-Path $root 'Assets/Scripts/StartupResourceDownloadPlan.cs'),
        (Join-Path $root 'Assets/Scripts/StartupResourceDownloadOperation.cs'))
    $assertions=[FullStartupBehaviorChecks]::Run()
    Write-Output "Full startup download behavior passed ($assertions assertions)."
} finally {
    if(Test-Path -LiteralPath $temporary){Remove-Item -LiteralPath $temporary -Force}
}
