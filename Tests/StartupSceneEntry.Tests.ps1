$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/StartupSceneController.cs') -Raw
$scene = Get-Content -LiteralPath (Join-Path $root 'Assets/Scenes/启动场景.unity') -Raw
$meta = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/StartupSceneController.cs.meta') -Raw
$sceneMeta = Get-Content -LiteralPath (Join-Path $root 'Assets/Scenes/启动场景.unity.meta') -Raw
$buildSettings = Get-Content -LiteralPath (Join-Path $root 'ProjectSettings/EditorBuildSettings.asset') -Raw
$gameSession = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/GameSession.cs') -Raw

$stubs = @'
namespace UnityEngine
{
    public class MonoBehaviour
    {
        public static StartupContentUpdate Updater;
        public static StartupResourceServerSettings Settings;
        public T GetComponent<T>() where T : class
        {
            return (typeof(T) == typeof(StartupContentUpdate)
                ? (object)Updater : Settings) as T;
        }
    }
    public static class Time { public static float realtimeSinceStartup; }
    public sealed class WaitForSecondsRealtime
    {
        public float waitTime;
        public WaitForSecondsRealtime(float seconds) { waitTime = seconds; }
    }
    public static class Debug
    {
        public static int Errors;
        public static void LogError(string message) { Errors++; }
    }
}
public sealed class StartupContentUpdate
{
    public int Runs;
    public System.Collections.IEnumerator Run()
    {
        Runs++;
        yield return null;
    }
}
public sealed class StartupResourceServerSettings
{
    public bool IsValid = true;
    public int Applies;
    public bool TryApply(out string error)
    {
        Applies++;
        error = IsValid ? null : "Invalid URL";
        return IsValid;
    }
}
namespace UnityEngine.ResourceManagement.ResourceProviders
{
    public class SceneInstance {}
}
namespace UnityEngine.ResourceManagement.AsyncOperations
{
    public enum AsyncOperationStatus { Succeeded, Failed }
    public struct AsyncOperationHandle<T>
    {
        public AsyncOperationStatus Status;
        public System.Exception OperationException;
    }
}
namespace UnityEngine.AddressableAssets
{
    public static class Addressables
    {
        public static int Releases;
        public static void Release<T>(
            UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<T> handle)
            { Releases++; }
    }
}
public static class RemoteGameContent
{
    public const string LoginSceneName = "SampleScene";
    public static int Loads;
    public static bool ThrowOnLoad;
    public static bool FailLoad;
    public static string LastScene;
    public static UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<
        UnityEngine.ResourceManagement.ResourceProviders.SceneInstance> LoadSceneAsync(string name)
    {
        if (ThrowOnLoad) throw new System.InvalidOperationException("Scene key missing");
        Loads++;
        LastScene = name;
        return new UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<
            UnityEngine.ResourceManagement.ResourceProviders.SceneInstance>
        {
            Status = FailLoad
                ? UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Failed
                : UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded,
            OperationException = FailLoad ? new System.InvalidOperationException("Download failed") : null
        };
    }
}
'@
Add-Type -TypeDefinition ($source + "`n" + $stubs) -IgnoreWarnings | Out-Null

$startup = [StartupSceneController]::new()
$start = $startup.GetType().GetMethod('Start', [System.Reflection.BindingFlags]'Instance,NonPublic')
if ($null -eq $start) { throw 'Startup scene entry method is missing.' }

[UnityEngine.MonoBehaviour]::Updater = $null
$missingUpdate = [System.Collections.IEnumerator]$start.Invoke($startup, @())
if ($missingUpdate.MoveNext() -or [UnityEngine.Debug]::Errors -ne 1 -or
    [RemoteGameContent]::Loads -ne 0) {
    throw 'Missing content updater must stop before any scene load.'
}
[UnityEngine.Debug]::Errors = 0

function Complete-ContentUpdate([System.Collections.IEnumerator]$routine) {
    if (-not $routine.MoveNext() -or $routine.Current -isnot [System.Collections.IEnumerator]) {
        throw 'Startup must begin content checking after applying the Inspector address.'
    }
    $update = [System.Collections.IEnumerator]$routine.Current
    if (-not $update.MoveNext() -or $update.MoveNext()) { throw 'Expected one update attempt.' }
}

[UnityEngine.MonoBehaviour]::Updater = [StartupContentUpdate]::new()
$missingSettings = [System.Collections.IEnumerator]$start.Invoke($startup, @())
if ($missingSettings.MoveNext() -or [UnityEngine.Debug]::Errors -ne 1 -or
    [UnityEngine.MonoBehaviour]::Updater.Runs -ne 0) {
    throw 'Missing Inspector settings must stop before content checking.'
}
[UnityEngine.Debug]::Errors = 0

[UnityEngine.MonoBehaviour]::Settings = [StartupResourceServerSettings]::new()
[UnityEngine.MonoBehaviour]::Settings.IsValid = $false
$invalidSettings = [System.Collections.IEnumerator]$start.Invoke($startup, @())
if ($invalidSettings.MoveNext() -or [UnityEngine.Debug]::Errors -ne 1 -or
    [UnityEngine.MonoBehaviour]::Updater.Runs -ne 0) {
    throw 'Invalid Inspector address must stop before content checking.'
}
[UnityEngine.Debug]::Errors = 0
[UnityEngine.MonoBehaviour]::Settings.IsValid = $true

[UnityEngine.Time]::realtimeSinceStartup = 0
[RemoteGameContent]::ThrowOnLoad = $true
$routine = [System.Collections.IEnumerator]$start.Invoke($startup, @())
Complete-ContentUpdate $routine
if (-not $routine.MoveNext() -or
    $routine.Current -isnot [UnityEngine.WaitForSecondsRealtime] -or
    $routine.Current.waitTime -ne 4.5) {
    throw 'Startup must wait the remaining 4.5 unscaled seconds.'
}
if ([RemoteGameContent]::Loads -ne 0 -or [UnityEngine.Debug]::Errors -ne 0) {
    throw 'Startup must not load or report an error before the delay.'
}
$routine.MoveNext() | Out-Null
if ([RemoteGameContent]::Loads -ne 0 -or [UnityEngine.Debug]::Errors -ne 1) {
    throw 'Missing login scene must not start a load and must log one error.'
}

[RemoteGameContent]::ThrowOnLoad = $false
[UnityEngine.Time]::realtimeSinceStartup = 2
$routine = [System.Collections.IEnumerator]$start.Invoke($startup, @())
Complete-ContentUpdate $routine
[UnityEngine.Time]::realtimeSinceStartup = 3
if (-not $routine.MoveNext() -or
    $routine.Current -isnot [UnityEngine.WaitForSecondsRealtime] -or
    $routine.Current.waitTime -ne 3.5) {
    throw 'The minimum delay must subtract elapsed update time.'
}
if ([RemoteGameContent]::Loads -ne 0) {
    throw 'Available login scene must not load before the delay.'
}
if (-not $routine.MoveNext() -or $routine.Current.GetType().Name -ne 'AsyncOperationHandle`1') {
    throw 'Available login scene must yield the remote scene handle.'
}
if ([RemoteGameContent]::Loads -ne 1 -or
    [RemoteGameContent]::LastScene -ne 'SampleScene' -or
    [UnityEngine.Debug]::Errors -ne 1) {
    throw 'Available login scene must be loaded asynchronously by name.'
}
if ($routine.MoveNext()) { throw 'Successful remote scene load should finish startup.' }

[RemoteGameContent]::FailLoad = $true
[UnityEngine.Time]::realtimeSinceStartup = 10
$routine = [System.Collections.IEnumerator]$start.Invoke($startup, @())
Complete-ContentUpdate $routine
[UnityEngine.Time]::realtimeSinceStartup = 16
$routine.MoveNext() | Out-Null
$routine.MoveNext() | Out-Null
$routine.MoveNext() | Out-Null
if ([UnityEngine.Debug]::Errors -ne 2 -or
    [UnityEngine.AddressableAssets.Addressables]::Releases -ne 1) {
    throw 'Failed async load must log an error.'
}
if ([UnityEngine.MonoBehaviour]::Updater.Runs -ne 3 -or
    [UnityEngine.MonoBehaviour]::Settings.Applies -ne 4) {
    throw 'Inspector address must apply before each content update.'
}

$guid = [regex]::Match($meta, '(?m)^guid: ([a-f0-9]{32})$').Groups[1].Value
$updateMeta = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/StartupContentUpdate.cs.meta') -Raw
$updateGuid = [regex]::Match($updateMeta, '(?m)^guid: ([a-f0-9]{32})$').Groups[1].Value
$settingsMeta = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/StartupResourceServerSettings.cs.meta') -Raw
$settingsGuid = [regex]::Match($settingsMeta, '(?m)^guid: ([a-f0-9]{32})$').Groups[1].Value
$sceneGuid = [regex]::Match($sceneMeta, '(?m)^guid: ([a-f0-9]{32})$').Groups[1].Value
$buildScenes = [regex]::Matches($buildSettings,
    '(?m)^  - enabled: ([01])\r?\n    path: (.+)\r?\n    guid: ([a-f0-9]{32})$')
$buildScenePaths = @($buildScenes | ForEach-Object {
    [regex]::Unescape($_.Groups[2].Value.Trim('"'))
})
if ([string]::IsNullOrEmpty($guid) -or
    [string]::IsNullOrEmpty($sceneGuid) -or
    $scene -notmatch "m_Script: \{fileID: 11500000, guid: $guid, type: 3\}" -or
    $scene -notmatch "m_Script: \{fileID: 11500000, guid: $updateGuid, type: 3\}" -or
    $scene -notmatch "m_Script: \{fileID: 11500000, guid: $settingsGuid, type: 3\}" -or
    $scene -notmatch '(?m)^  resourceServerAddress: http://127\.0\.0\.1:18761/StandaloneWindows64$' -or
    $scene -notmatch '(?s)--- !u!1 &754758762.*?m_Component:.*?fileID: 754758764' -or
    $buildScenes.Count -ne 1 -or
    @($buildScenes | Where-Object { $_.Groups[1].Value -ne '1' }).Count -ne 0 -or
    $buildScenePaths[0] -ne 'Assets/Scenes/启动场景.unity' -or
    $buildScenes[0].Groups[3].Value -ne $sceneGuid -or
    $gameSession -notmatch 'RemoteGameContent\.LoadSceneOrLog\(') {
    throw 'Startup binding, build scene order, or return-to-login fallback is invalid.'
}

Write-Output 'Startup scene entry and Inspector resource address binding tests passed.'
