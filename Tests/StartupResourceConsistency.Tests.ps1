$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
$baseTest=Get-Content -Raw -LiteralPath (Join-Path $root 'Tests/StartupFullResourceDownload.Tests.ps1')
$baseHarness=[regex]::Match($baseTest,"(?s)@'\r?\n(.*?)\r?\n'@").Groups[1].Value
if(-not $baseHarness){throw 'The shared Addressables test harness was not found.'}
$baseHarness=$baseHarness.Replace('public static class Addressables {','public static partial class Addressables {')
$baseHarness=$baseHarness.Replace('public string Hash; public long BundleSize; public uint Crc;',
    'public string Hash, BundleName; public long BundleSize; public uint Crc; public int Timeout, RetryCount, RedirectLimit; public bool UseCrcForCachedBundle, ClearOtherCachedVersionsWhenLoaded;')
$baseHarness=$baseHarness.Replace('Downloads=Releases=0; DownloadFails=false;',
    'Downloads=Releases=0; DownloadFails=false; DownloadFailures.Clear(); UnityEngine.Caching.Clears=0;')
$baseHarness=$baseHarness.Replace('if(Active!=null){Active.Bytes=100;Active.Status=DownloadFails?',
    'if(Active!=null&&!Active.Done){Active.Bytes=100;Active.Status=(DownloadFailures.Count>0?DownloadFailures.Dequeue():DownloadFails)?')
$extra=@'
namespace UnityEngine {
    public struct Hash128 {
        public string Value;
        public static Hash128 Parse(string value)=>new Hash128{Value=value};
    }
    public static class Caching {
        public static int Clears;
        public static string LastName,LastHash;
        public static bool ClearCachedVersion(string name,Hash128 hash){Clears++;LastName=name;LastHash=hash.Value;return true;}
    }
}
namespace UnityEngine.Networking {
    public sealed class DownloadHandler { public string text; }
    public sealed class UnityWebRequest:System.IDisposable {
        public enum Result { Success,ConnectionError,ProtocolError }
        public sealed class Reply { public string Text; public long Code=200; public Result Outcome=Result.Success; }
        public static readonly System.Collections.Generic.Queue<Reply> Replies=new System.Collections.Generic.Queue<Reply>();
        public static readonly System.Collections.Generic.List<string> Urls=new System.Collections.Generic.List<string>();
        public static int Disposals,NoCacheHeaders;
        public string url,error; public long responseCode; public Result result; public int timeout; public DownloadHandler downloadHandler;
        public static UnityWebRequest Get(string url)=>new UnityWebRequest {url=url};
        public void SetRequestHeader(string name,string value){if(name=="Cache-Control"&&value.Contains("no-cache"))NoCacheHeaders++;}
        public object SendWebRequest(){Urls.Add(url);var r=Replies.Dequeue();responseCode=r.Code;result=r.Outcome;downloadHandler=new DownloadHandler {text=r.Text};return new object();}
        public void Dispose(){Disposals++;}
        public static void Reset(){Replies.Clear();Urls.Clear();Disposals=NoCacheHeaders=0;}
    }
}
namespace UnityEngine.AddressableAssets {
    public sealed class ResourceLocatorInfo {
        public IResourceLocator Locator; public string LocalHash; public IResourceLocation HashLocation; public bool CanUpdateContent=true;
    }
    public sealed class TestResourceManager {
        public System.Action<UnityEngine.Networking.UnityWebRequest> WebRequestOverride;
        public string TransformInternalId(IResourceLocation location)=>location.InternalId.Replace("original.test","selected.test");
    }
    public static partial class Addressables {
        public static readonly System.Collections.Generic.Queue<bool> DownloadFailures=new System.Collections.Generic.Queue<bool>();
        public static readonly TestResourceManager ResourceManager=new TestResourceManager();
        public static readonly System.Collections.Generic.List<IResourceLocator> ResourceLocators=new System.Collections.Generic.List<IResourceLocator>();
        public static readonly System.Collections.Generic.List<ResourceLocatorInfo> Infos=new System.Collections.Generic.List<ResourceLocatorInfo>();
        public static System.Collections.Generic.List<string> CheckResult;public static bool CheckFails,UpdateFails;public static string UpdatedHash;
        public static int Checks,Updates;
        public static ResourceLocatorInfo GetLocatorInfo(IResourceLocator locator)=>Infos.Find(info=>info.Locator==locator);
        public static AsyncOperationHandle<System.Collections.Generic.List<string>> CheckForCatalogUpdates(bool autoRelease){
            Checks++;return new AsyncOperationHandle<System.Collections.Generic.List<string>>{State=new State<System.Collections.Generic.List<string>>{
                Done=true,Result=CheckResult,Status=CheckFails?AsyncOperationStatus.Failed:AsyncOperationStatus.Succeeded}};
        }
        public static AsyncOperationHandle<System.Collections.Generic.List<IResourceLocator>> UpdateCatalogs(bool clean,System.Collections.Generic.IEnumerable<string> ids,bool autoRelease){
            if(clean||autoRelease)throw new System.Exception("Catalog update must retain valid caches and owned handles");
            Updates++;if(!UpdateFails)foreach(var info in Infos)info.LocalHash=UpdatedHash;
            return new AsyncOperationHandle<System.Collections.Generic.List<IResourceLocator>>{State=new State<System.Collections.Generic.List<IResourceLocator>>{
                Done=true,Result=ResourceLocators.ToList(),Status=UpdateFails?AsyncOperationStatus.Failed:AsyncOperationStatus.Succeeded}};
        }
        public static void ResetCatalog(string hash){
            Reset();UnityEngine.Networking.UnityWebRequest.Reset();Infos.Clear();ResourceLocators.Clear();
            Checks=Updates=0;CheckFails=UpdateFails=false;CheckResult=new System.Collections.Generic.List<string>();UpdatedHash=null;
            var locator=new TestLocator();ResourceLocators.Add(locator);Infos.Add(new ResourceLocatorInfo{Locator=locator,LocalHash=hash,
                HashLocation=new ResourceLocationBase("remote-hash","https://original.test/catalog.hash","text",typeof(string))});
        }
    }
}
public static class StartupConsistencyBehaviorChecks {
    private const string A="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",B="bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private static int checks;
    static void Check(bool ok,string message){checks++;if(!ok)throw new System.Exception(message);}
    static void Reply(string hash,long code=200,UnityEngine.Networking.UnityWebRequest.Result result=UnityEngine.Networking.UnityWebRequest.Result.Success)=>
        UnityEngine.Networking.UnityWebRequest.Replies.Enqueue(new UnityEngine.Networking.UnityWebRequest.Reply{Text=hash,Code=code,Outcome=result});
    static void Drain(System.Collections.IEnumerator run){int budget=0;while(run.MoveNext()){
        if(++budget>100)throw new System.Exception("coroutine did not finish");
        if(run.Current is System.Collections.IEnumerator child)Drain(child);Addressables.Tick();}}
    static StartupCatalogSynchronization Sync(){var s=new StartupCatalogSynchronization();Drain(s.Run());return s;}
    static StartupResourceDownloadPlan CachePlan(out AssetBundleRequestOptions original){
        original=new AssetBundleRequestOptions{Hash=A,BundleSize=10,BundleName="map-test",Crc=456,Timeout=10,RetryCount=1,
            UseCrcForCachedBundle=false,ClearOtherCachedVersionsWhenLoaded=true};
        var bundle=new ResourceLocationBase("bundle","https://resources.test/map.bundle","bundles",typeof(IAssetBundleResource)){Data=original};
        var locator=new TestLocator();
        foreach(string key in new[]{RemoteGameContent.LoginSceneAddress,RemoteGameContent.WorldSceneAddress,RemoteGameContent.ItemTableAddress,RemoteGameContent.BootstrapFontAddress}
            .Concat(WorldMapSceneCatalog.GetAllResourceAddresses()))
            locator.Add(key,new ResourceLocationBase(key,key,"assets",typeof(object),bundle));
        return StartupResourceDownloadPlan.Create(new[]{locator});
    }
    public static int Run(){
        checks=0;
        Addressables.ResetCatalog(A);Reply(A);var unchanged=Sync();
        Check(unchanged.Succeeded&&Addressables.Checks==1&&Addressables.Updates==0&&Addressables.Downloads==0,"unchanged startup must check online without replacing the catalog or downloading bundles");
        var firstUrl=UnityEngine.Networking.UnityWebRequest.Urls.Single();
        Check(firstUrl.StartsWith("https://selected.test/")&&firstUrl.Contains("startup_check=")&&UnityEngine.Networking.UnityWebRequest.NoCacheHeaders==1,"fresh hash probes must honor the selected endpoint and bypass HTTP caches");
        Reply(A);Drain(unchanged.ConfirmBeforeLogin());
        Check(unchanged.Succeeded&&UnityEngine.Networking.UnityWebRequest.Urls.Last()!=firstUrl,"login confirmation must issue a fresh version request");

        Addressables.ResetCatalog(A);Addressables.CheckResult.Add("test");Addressables.UpdatedHash=B;Reply(B);var updated=Sync();
        Check(updated.Succeeded&&Addressables.Updates==1&&Addressables.Releases==2,"a new version must replace the catalog, retain bundle caches and release operation handles");
        Reply(A);Drain(updated.ConfirmBeforeLogin());
        Check(!updated.Succeeded&&updated.FailureStage=="changed","publication changing during download must block login");

        Addressables.ResetCatalog(A);Addressables.CheckFails=true;var disconnected=Sync();
        Check(!disconnected.Succeeded&&Addressables.Updates==0&&UnityEngine.Networking.UnityWebRequest.Urls.Count==0,"failed online checks must not silently use offline content");
        Addressables.ResetCatalog(A);Addressables.CheckResult=null;Check(!Sync().Succeeded,"a null success result must not be treated as no update");
        Addressables.ResetCatalog(A);Addressables.CheckResult.Add(null);Check(!Sync().Succeeded,"partial invalid catalog results must be rejected");
        Addressables.ResetCatalog(A);Addressables.CheckResult.Add("test");Addressables.UpdateFails=true;Check(!Sync().Succeeded,"catalog replacement failures must block content loading");
        foreach(string body in new[]{"", "<html>proxy error</html>", "012345", new string('g',32)}){
            Addressables.ResetCatalog(A);Reply(body);Check(!Sync().Succeeded,"invalid hash response was accepted");
        }
        Addressables.ResetCatalog(A);Reply(A,502,UnityEngine.Networking.UnityWebRequest.Result.ProtocolError);Check(!Sync().Succeeded,"HTTP proxy errors must block startup even if local data is complete");
        Addressables.ResetCatalog(A);Reply(B);Check(!Sync().Succeeded,"a mismatching fresh hash must block stale content");
        Addressables.ResetCatalog(A);Reply(A);Check(Sync().Succeeded,"retry after a network failure must perform a new successful check");
        Addressables.ResetCatalog(A);Addressables.ResourceLocators.Clear();Addressables.Infos.Clear();Check(!Sync().Succeeded,"a Player with no updatable catalog must not skip online validation");
        var editor=new StartupCatalogSynchronization(true);Drain(editor.Run());Check(editor.Succeeded&&editor.SkippedEditorAssetDatabase,"only explicitly allowed editor AssetDatabase mode may skip remote checks");
        Check(StartupCatalogSynchronization.TryNormalizeHash("  "+A.ToUpperInvariant()+"\n",out string normalized)&&normalized==A,"hash normalization must support whitespace and uppercase");

        var plan=CachePlan(out var original);
        var verification=StartupResourceCacheVerification.CreateVerificationLocation(plan.RemoteBundles[0]);
        var options=(AssetBundleRequestOptions)verification.Data;
        Check(options.UseCrcForCachedBundle&&!options.ClearOtherCachedVersionsWhenLoaded&&verification.Dependencies.Count==0,"cache verification must enable CRC and isolate each bundle without purging other versions");
        Check(!object.ReferenceEquals(options,original)&&!original.UseCrcForCachedBundle&&original.ClearOtherCachedVersionsWhenLoaded,"cache checks must not mutate catalog metadata");
        Addressables.Reset(0);var cache=new StartupResourceCacheVerification(plan,null);Drain(cache.Run());
        Check(cache.Succeeded&&Addressables.Downloads==1&&Addressables.Releases==2,"warm cache verification must read the CRC-checking location and release owned handles");
        Check(((AssetBundleRequestOptions)Addressables.LastDownload[0].Data).UseCrcForCachedBundle,"the actual provider request must verify cached bytes");
        Addressables.Reset();Addressables.DownloadFails=true;var broken=new StartupResourceCacheVerification(plan,null);Drain(broken.Run());
        Check(!broken.Succeeded&&Addressables.Releases==2&&Addressables.Downloads==2,"unrecoverable cache validation must stop after one bounded retry and release both handles");
        Addressables.Reset(0);Addressables.DownloadFailures.Enqueue(true);Addressables.DownloadFailures.Enqueue(false);
        var repaired=new StartupResourceCacheVerification(plan,null);Drain(repaired.Run());
        Check(repaired.Succeeded&&Addressables.Downloads==2&&Addressables.Releases==3&&UnityEngine.Caching.Clears==1,
            "a native cache handler failure must recover after releasing the first request and retrying only once");
        Check(UnityEngine.Caching.LastName=="map-test"&&UnityEngine.Caching.LastHash==A,
            "cache repair may clear only the failed bundle name and version, preserving other resources");
        Addressables.Reset(8);var missing=new StartupResourceCacheVerification(plan,null);Drain(missing.Run());Check(!missing.Succeeded,"an evicted cache entry after verification must block login");
        original.Crc=0;bool rejected=false;try{StartupResourceCacheVerification.CreateVerificationLocation(plan.RemoteBundles[0]);}catch(System.InvalidOperationException){rejected=true;}
        Check(rejected,"bundles without CRC metadata must not be reported as verified");
        return checks;
    }
}
'@
$temporary=Join-Path $root ('Temp/ConsistencyTests-'+[guid]::NewGuid().ToString('N')+'.cs')
try {
    [IO.File]::WriteAllText($temporary,$baseHarness+[Environment]::NewLine+$extra,[Text.UTF8Encoding]::new($false))
    Add-Type -Path @($temporary,(Join-Path $root 'Assets/Scripts/WorldMapSceneCatalog.cs'),
        (Join-Path $root 'Assets/Scripts/StartupResourceDownloadPlan.cs'),
        (Join-Path $root 'Assets/Scripts/StartupResourceDownloadOperation.cs'),
        (Join-Path $root 'Assets/Scripts/StartupCatalogSynchronization.cs'),
        (Join-Path $root 'Assets/Scripts/StartupResourceCacheVerification.cs'))
    $checks=[StartupConsistencyBehaviorChecks]::Run()
    Write-Output "Startup version synchronization and cache verification behaviors passed ($checks checks)."
} finally {if(Test-Path -LiteralPath $temporary){Remove-Item -LiteralPath $temporary -Force}}
