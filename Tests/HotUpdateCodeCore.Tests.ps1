$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$harness = @'
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Diagnostics;
using QiDao.Bootstrap.CodeUpdate;
public static class HotCodeCoreChecks {
    static int checks;
    static void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
    static void Reject(Action action,string message){bool rejected=false;try{action();}catch{rejected=true;}Check(rejected,message);}
    static CodeCacheState Parse(string s){var p=s.Split('|');return new CodeCacheState{activeRelease=p[0],previousRelease=p[1],highestSequence=long.Parse(p[2]),readyRelease=p[3],previousReadyRelease=p[4],readySequence=long.Parse(p[5])};}
    static string Write(CodeCacheState s)=>s.activeRelease+"|"+s.previousRelease+"|"+s.highestSequence+"|"+s.readyRelease+"|"+s.previousReadyRelease+"|"+s.readySequence;
    static CodeRelease Release(string id,long sequence,byte[] bytes)=>new CodeRelease {
        schemaVersion=1,releaseId=id,sequence=sequence,target=CodeUpdateProtocol.Target,
        unityVersion="2022.3.62f1",bootstrapApiVersion=1,assemblyName=CodeUpdateProtocol.AssemblyName,
        entryType=CodeUpdateProtocol.EntryType,codePath="releases/"+id+"/QiDao.GameLogic.dll.bytes",
        codeBytes=bytes.Length,codeSha256=CodeUpdateProtocol.Hash(bytes)
    };
    public static int Run(string directory){
        var bytes=Encoding.UTF8.GetBytes("a verified game DLL fixture");
        var a=Release(new string('a',32),1,bytes);var b=Release(new string('b',32),2,bytes);
        using(var key=RSA.Create(2048))using(var wrong=RSA.Create(2048)){
            byte[] payload=Encoding.UTF8.GetBytes("immutable signed release metadata");
            var envelope=new SignedCodeEnvelope{payload=Convert.ToBase64String(payload),signature=Convert.ToBase64String(key.SignData(payload,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1))};
            Check(CodeUpdateProtocol.VerifyEnvelope(envelope,key.ToXmlString(false)).SequenceEqual(payload),"A valid publisher signature must verify exact payload bytes.");
            Reject(()=>CodeUpdateProtocol.VerifyEnvelope(envelope,wrong.ToXmlString(false)),"Wrong publisher key must fail.");
            envelope.payload=Convert.ToBase64String(Encoding.UTF8.GetBytes("modified metadata"));
            Reject(()=>CodeUpdateProtocol.VerifyEnvelope(envelope,key.ToXmlString(false)),"Tampered payload must fail.");
            var config=new CodeUpdateConfiguration{manifestUrl="http://127.0.0.1/current.json",publicKeyXml=key.ToXmlString(false),bootstrapApiVersion=1,unityVersion="2022.3.62f1",maxCodeBytes=1000,requestTimeoutSeconds=10};
            CodeUpdateProtocol.Validate(a,config);checks++;
            a.resourceReleaseId=new string('d',32);a.resourceCatalogHash=new string('1',32);a.resourceCatalogSha256=new string('2',64);
            a.resourceManifestSha256=new string('3',64);a.resourceCatalogBytes=100;a.resourceManifestBytes=200;
            a.scriptTypeHash=new string('4',32);a.resourceBuildRoot="http://47.93.78.106:18761/client-hotupdate/windows-mono-v1/resources/"+a.resourceReleaseId;
            CodeUpdateProtocol.ValidateResourceBinding(a);checks++;
            a.resourceBuildRoot="http://47.93.78.106:18761/StandaloneWindows64";
            Reject(()=>CodeUpdateProtocol.ValidateResourceBinding(a),"Legacy resources must never be accepted as a migrated code/resource binding.");
            a.resourceBuildRoot="http://47.93.78.106:18761/client-hotupdate/windows-mono-v1/resources/"+a.resourceReleaseId;
            a.resourceCatalogSha256="wrong";Reject(()=>CodeUpdateProtocol.ValidateResourceBinding(a),"A resource binding needs a complete authenticated catalog hash.");a.resourceCatalogSha256=new string('2',64);
            Check(CodeUpdateProtocol.ResourceUri(new Uri(config.manifestUrl),a,"catalog_0.1.json").AbsolutePath=="/resources/"+a.resourceReleaseId+"/catalog_0.1.json","Resource URI must pin an immutable release, independently of latest.");
            Reject(()=>CodeUpdateProtocol.ResourceUri(new Uri(config.manifestUrl),a,"../../outside"),"Resource control paths must be restricted.");
            a.resourceReleaseId=null;
            a.bootstrapApiVersion=2;Reject(()=>CodeUpdateProtocol.Validate(a,config),"An incompatible base API must fail.");a.bootstrapApiVersion=1;
            a.target="another-runtime";Reject(()=>CodeUpdateProtocol.Validate(a,config),"Wrong runtime target must fail.");a.target=CodeUpdateProtocol.Target;
            a.codePath="../outside.dll";Reject(()=>CodeUpdateProtocol.Validate(a,config),"Manifest path traversal must fail.");a.codePath="releases/"+a.releaseId+"/QiDao.GameLogic.dll.bytes";
            a.assemblyName="OtherCode";Reject(()=>CodeUpdateProtocol.Validate(a,config),"Wrong game assembly must fail.");a.assemblyName=CodeUpdateProtocol.AssemblyName;
            a.codeBytes=1001;Reject(()=>CodeUpdateProtocol.Validate(a,config),"Oversized DLL must fail.");a.codeBytes=bytes.Length;
            Reject(()=>CodeUpdateProtocol.VerifyCodeBytes(a,new byte[]{1}),"Truncated code must fail.");
            var corrupt=bytes.ToArray();corrupt[0]^=1;
            Reject(()=>CodeUpdateProtocol.VerifyCodeBytes(a,corrupt),"Same-length corrupted code must fail.");
        }
        var cache=new CodeUpdateCache(directory,Parse,Write);
        cache.Store(a,"signed-manifest-a",bytes);
        Reject(()=>cache.Store(a,"tampered-manifest",new byte[]{1}),"A partial download must never replace a valid cached version.");
        Check(File.ReadAllBytes(cache.CodePath(a)).SequenceEqual(bytes)&&File.ReadAllText(cache.EnvelopePath(a.releaseId))=="signed-manifest-a","Failed replacement must preserve valid code and manifest.");
        cache.MarkHealthy(a);cache.MarkHealthy(b);
        Check(cache.ReadState().activeRelease==b.releaseId&&cache.ReadState().previousRelease==a.releaseId,"Keep the last healthy release for recovery.");
        cache.MarkResourcesReady(a);
        Check(cache.ReadState().readyRelease==a.releaseId&&cache.ReadState().activeRelease==b.releaseId,"Code initialization alone must not commit a new resource pair.");
        cache.MarkResourcesReady(b);
        Check(cache.ReadState().readyRelease==b.releaseId&&cache.ReadState().previousReadyRelease==a.releaseId,"Keep the previous completely verified code/resource pair.");
        cache.MarkResourcesReady(a);
        Check(cache.ReadState().readyRelease==b.releaseId&&cache.ReadState().readySequence==2,"An older concurrent client must not downgrade the ready resource pair.");
        cache.MarkHealthy(a);
        Check(cache.ReadState().activeRelease==b.releaseId&&cache.ReadState().highestSequence==2,"A slower older concurrent client cannot downgrade shared state.");
        cache.MarkHealthy(a,true);
        Check(cache.ReadState().activeRelease==a.releaseId&&cache.ReadState().highestSequence==2,"Recovery may restore healthy old code without lowering replay protection.");
        Reject(()=>cache.ReleaseDirectory("../../outside"),"Cache paths must reject traversal.");
        using(var process=Process.GetCurrentProcess()){
            string journal=cache.BeginStart(b,"live-process");
            cache.RecoverAbandonedStarts(s=>new CodeStartJournal{releaseId=b.releaseId,processId=process.Id,processStartTicks=process.StartTime.ToUniversalTime().Ticks});
            Check(!cache.IsFailed(b.releaseId)&&File.Exists(journal),"Another live client startup must never be treated as a crash.");
            cache.EndStart(journal);
        }
        string failedId=new string('c',32);cache.BeginStart(Release(failedId,3,bytes),"dead-process");
        cache.RecoverAbandonedStarts(s=>new CodeStartJournal{releaseId=failedId,processId=int.MaxValue,processStartTicks=0});
        Check(cache.IsFailed(failedId),"An abandoned unconfirmed code start must be quarantined.");
        cache.MarkHealthy(b);File.WriteAllText(Path.Combine(directory,"state.json"),"broken");
        Check(cache.ReadState().activeRelease==a.releaseId,"Interrupted state writes must recover the valid backup.");
        Check(!Directory.GetFiles(directory,"*.partial-*",SearchOption.AllDirectories).Any(),"No partial files may remain after completed writes.");
        return checks;
    }
}
'@
$temporary = Join-Path $root ('Temp/HotUpdateCodeCore-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
$source = Join-Path $temporary 'Harness.cs'
try {
    [IO.File]::WriteAllText($source,$harness,[Text.UTF8Encoding]::new($false))
    Add-Type -Path @($source,
        (Join-Path $root 'Assets/Bootstrap/Runtime/CodeUpdate/CodeUpdateProtocol.cs'),
        (Join-Path $root 'Assets/Bootstrap/Runtime/CodeUpdate/CodeUpdateCache.cs'))
    $checks=[HotCodeCoreChecks]::Run((Join-Path $temporary 'Cache'))
    Write-Output "Signed code verification, compatibility, atomic cache, replay floor, multi-client journals and crash recovery passed ($checks checks)."
} finally {
    $absolute=[IO.Path]::GetFullPath($temporary)
    $allowed=[IO.Path]::GetFullPath((Join-Path $root 'Temp')) + [IO.Path]::DirectorySeparatorChar
    if(!$absolute.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe test cleanup path.'}
    Remove-Item -LiteralPath $absolute -Recurse -Force
}
