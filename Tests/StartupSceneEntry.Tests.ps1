$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Get-Content -LiteralPath (Join-Path $root 'Assets/Bootstrap/Runtime/StartupSceneController.cs') -Raw
$scene = Get-Content -LiteralPath (Join-Path $root 'Assets/Scenes/启动场景.unity') -Raw
$meta = Get-Content -LiteralPath (Join-Path $root 'Assets/Bootstrap/Runtime/StartupSceneController.cs.meta') -Raw
$sceneMeta = Get-Content -LiteralPath (Join-Path $root 'Assets/Scenes/启动场景.unity.meta') -Raw
$buildSettings = Get-Content -LiteralPath (Join-Path $root 'ProjectSettings/EditorBuildSettings.asset') -Raw
$gameSession = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/GameSession.cs') -Raw

$stubs = @'
namespace UnityEngine {
    public class MonoBehaviour {
        public GameObject gameObject = new GameObject();
        public static StartupContentUpdate Updater;
        public static StartupResourceServerSettings Settings;
        public T GetComponent<T>() where T:class => (typeof(T)==typeof(StartupContentUpdate)?(object)Updater:Settings) as T;
    }
    public class GameObject { public T AddComponent<T>() where T:new() => new T(); }
    public static class Application { public static bool isBatchMode; }
    public static class Time { public static float realtimeSinceStartup; }
    public sealed class WaitForSecondsRealtime { public float waitTime; public WaitForSecondsRealtime(float t){waitTime=t;} }
    public static class Debug { public static int Errors; public static void LogError(string m){Errors++;} }
}
namespace QiDao.Bootstrap.CodeUpdate { public class ResourceBindingAcceptanceRunner {} }
public sealed class StartupContentUpdate {
    public bool IsReady => true;
    public int Runs,Logins;
    public System.Collections.IEnumerator Run(){Runs++;yield return null;}
    public System.Collections.IEnumerator OpenLoginScene(){Logins++;yield return null;}
}
public sealed class StartupResourceServerSettings {
    public bool IsValid=true; public int Applies;
    public bool TryApply(out string error){Applies++;error=IsValid?null:"invalid";return IsValid;}
}
public static class StartupEntryBoundaryChecks {
    static int checks;
    static void Check(bool ok,string message){checks++;if(!ok)throw new System.Exception(message);}
    static System.Collections.IEnumerator Start(){return (System.Collections.IEnumerator)typeof(StartupSceneController)
        .GetMethod("Start",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)
        .Invoke(new StartupSceneController(),null);}
    public static int Run(){
        UnityEngine.MonoBehaviour.Updater=null;UnityEngine.MonoBehaviour.Settings=null;UnityEngine.Debug.Errors=0;
        Check(!Start().MoveNext()&&UnityEngine.Debug.Errors==1,"Missing updater must stop startup.");
        var updater=new StartupContentUpdate();UnityEngine.MonoBehaviour.Updater=updater;
        Check(!Start().MoveNext()&&updater.Runs==0,"Missing settings must stop before checking content.");
        var settings=new StartupResourceServerSettings{IsValid=false};UnityEngine.MonoBehaviour.Settings=settings;
        Check(!Start().MoveNext()&&updater.Runs==0,"Invalid endpoint must stop before checking content.");
        settings.IsValid=true;UnityEngine.Time.realtimeSinceStartup=0;
        var start=Start();Check(start.MoveNext()&&start.Current is System.Collections.IEnumerator,"Startup must yield content preparation.");
        var update=(System.Collections.IEnumerator)start.Current;Check(update.MoveNext()&&!update.MoveNext()&&updater.Runs==1,"Content preparation must run once.");
        UnityEngine.Time.realtimeSinceStartup=1;
        Check(start.MoveNext()&&start.Current is UnityEngine.WaitForSecondsRealtime,"Startup must retain its unscaled minimum delay.");
        Check(((UnityEngine.WaitForSecondsRealtime)start.Current).waitTime==3.5f&&updater.Logins==0,"Elapsed preparation must subtract from the delay; login must wait.");
        Check(start.MoveNext()&&start.Current is System.Collections.IEnumerator,"Login must be delegated through the updater boundary.");
        var login=(System.Collections.IEnumerator)start.Current;
        Check(login.MoveNext()&&!login.MoveNext()&&updater.Logins==1&&!start.MoveNext(),"Exactly one game login routine must execute.");
        UnityEngine.Time.realtimeSinceStartup=0;var slow=Start();slow.MoveNext();((System.Collections.IEnumerator)slow.Current).MoveNext();
        UnityEngine.Time.realtimeSinceStartup=8;
        Check(slow.MoveNext()&&slow.Current is System.Collections.IEnumerator,"Slow preparation must not add an unnecessary delay.");
        return checks;
    }
}
'@
Add-Type -TypeDefinition ($source + "`n" + $stubs)
$checks=[StartupEntryBoundaryChecks]::Run()
if($source -match 'RemoteGameContent|ItemDefinitionTable|GameFontService') {
    throw 'The startup scene must delegate business startup through the fixed contract.'
}
$selectedServerId = [regex]::Match($scene, '(?m)^  selectedServerId: ([a-f0-9]{32})\r?$').Groups[1].Value
$guid = [regex]::Match($meta, '(?m)^guid: ([a-f0-9]{32})\r?$').Groups[1].Value
$updateMeta = Get-Content -LiteralPath (Join-Path $root 'Assets/Bootstrap/Runtime/StartupContentUpdate.cs.meta') -Raw
$updateGuid = [regex]::Match($updateMeta, '(?m)^guid: ([a-f0-9]{32})\r?$').Groups[1].Value
$settingsMeta = Get-Content -LiteralPath (Join-Path $root 'Assets/Bootstrap/Runtime/StartupResourceServerSettings.cs.meta') -Raw
$settingsGuid = [regex]::Match($settingsMeta, '(?m)^guid: ([a-f0-9]{32})\r?$').Groups[1].Value
$sceneGuid = [regex]::Match($sceneMeta, '(?m)^guid: ([a-f0-9]{32})\r?$').Groups[1].Value
$buildScenes = [regex]::Matches($buildSettings,
    '(?m)^  - enabled: ([01])\r?\n    path: (.+)\r?\n    guid: ([a-f0-9]{32})\r?$')
$buildScenePaths = @($buildScenes | ForEach-Object {
    [regex]::Unescape($_.Groups[2].Value.Trim().Trim('"'))
})
if ([string]::IsNullOrEmpty($guid) -or
    [string]::IsNullOrEmpty($sceneGuid) -or
    $scene -notmatch "m_Script: \{fileID: 11500000, guid: $guid, type: 3\}" -or
    $scene -notmatch "m_Script: \{fileID: 11500000, guid: $updateGuid, type: 3\}" -or
    $scene -notmatch "m_Script: \{fileID: 11500000, guid: $settingsGuid, type: 3\}" -or
    [string]::IsNullOrEmpty($selectedServerId) -or
    $scene -notmatch ('(?m)^  - id: ' + $selectedServerId + '\r?$') -or
    $scene -notmatch '(?s)--- !u!1 &754758762.*?m_Component:.*?fileID: 754758764' -or
    $buildScenes.Count -ne 1 -or
    @($buildScenes | Where-Object { $_.Groups[1].Value -ne '1' }).Count -ne 0 -or
    $buildScenePaths[0] -ne 'Assets/Scenes/启动场景.unity' -or
    $buildScenes[0].Groups[3].Value -ne $sceneGuid -or
    $gameSession -notmatch 'RemoteGameContent\.LoadSceneOrLog\(') {
    throw 'Startup binding, build scene order, or return-to-login fallback is invalid.'
}

Write-Output 'Startup scene entry and Inspector resource address binding tests passed.'
