$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$messages = Get-Content -LiteralPath (Join-Path $root 'Assets/Bootstrap/Contracts/StartupUiText.cs') -Raw
Add-Type -TypeDefinition $messages
$required = @(
    [StartupUiText]::InitializationFailed + [StartupUiText]::RetrySuffix,
    [StartupUiText]::FontDownloadFailed + [StartupUiText]::RetrySuffix,
    [StartupUiText]::DownloadingFont + '1.0 MB / 25.0 MB',
    [StartupUiText]::RemoteAssetMissing + 'game/scene/login',
    [StartupUiText]::Retry
)
foreach ($message in $required) {
    foreach ($character in $message.ToCharArray()) {
        if (-not [StartupUiText]::FontCharacters.Contains($character.ToString())) {
            throw "The startup font corpus misses U+$('{0:X4}' -f [int]$character)."
        }
    }
}
$updater = Get-Content -LiteralPath (Join-Path $root 'Assets/Bootstrap/Runtime/StartupContentUpdate.cs') -Raw
$updater += Get-Content -Raw -LiteralPath (Join-Path $root 'Assets/Scripts/HotUpdate/GameStartupContent.cs')
$service = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/GameFontService.cs') -Raw
if ($updater -match 'AsyncOperationHandle<TMP_FontAsset>|statusText.font\s*=\s*fontHandle|retryText.font\s*=\s*fontHandle' -or
    $updater -notmatch 'statusText.font = TMP_Settings.defaultFontAsset' -or
    $updater -notmatch 'GameFontService.Load\(\)' -or
    $updater -notmatch 'if \(!GameFontService.IsReady\)' -or
    $updater -match '(?:Fail|SetStatus)\("[^"\r\n]*[\u4e00-\u9fff]') {
    throw 'Startup messages must use the local corpus; the font session must outlive the updater.'
}
if ($service -notmatch 'SubsystemRegistration' -or
    $service -notmatch 'Application.quitting \+= Release' -or
    $service -notmatch 'registeredFallbacks.Remove\(registeredFont\)' -or
    $service -notmatch 'Addressables.Release\(fontHandle\)') {
    throw 'Font session cleanup must support shutdown and Play mode with domain reload disabled.'
}
if ($updater -notmatch 'GameFontService.Release\(\)' -or
    $updater.IndexOf('ReleaseAttemptContent();') -gt $updater.IndexOf('yield return catalogSync.Run()')) {
    throw 'A retry that updates the catalog must release the previous font bundle first.'
}
$fontChecks = @'
namespace UnityEngine {
    public enum RuntimeInitializeLoadType { SubsystemRegistration }
    public sealed class RuntimeInitializeOnLoadMethodAttribute : System.Attribute {
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType kind) { }
    }
    public static class Application {
        public static event System.Action quitting;
        public static int Subscribers => quitting == null ? 0 : quitting.GetInvocationList().Length;
        public static void Quit() { quitting?.Invoke(); }
    }
}
namespace TMPro {
    public sealed class TMP_FontAsset { }
    public static class TMP_Settings {
        public static readonly System.Collections.Generic.List<TMP_FontAsset> fallbackFontAssets =
            new System.Collections.Generic.List<TMP_FontAsset>();
    }
}
namespace UnityEngine.ResourceManagement.AsyncOperations {
    public enum AsyncOperationStatus { None, Succeeded, Failed }
    public sealed class TestOperation<T> {
        public bool Valid = true;
        public T Result;
        public AsyncOperationStatus Status;
    }
    public struct AsyncOperationHandle<T> {
        public TestOperation<T> Operation;
        public bool IsValid() => Operation != null && Operation.Valid;
        public T Result => Operation.Result;
        public AsyncOperationStatus Status => Operation.Status;
    }
}
namespace UnityEngine.AddressableAssets {
    public static class Addressables {
        public static TMPro.TMP_FontAsset NextFont;
        public static bool Fail;
        public static int Loads, Releases;
        public static UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<T> LoadAssetAsync<T>(string address) {
            Loads++;
            return new UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<T> {
                Operation = new UnityEngine.ResourceManagement.AsyncOperations.TestOperation<T> {
                    Result = (T)(object)NextFont,
                    Status = Fail ? UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Failed
                        : UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded
                }
            };
        }
        public static void Release<T>(UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<T> handle) {
            if (!handle.IsValid()) throw new System.Exception("Double release");
            Releases++;
            handle.Operation.Valid = false;
        }
    }
}
public static class RemoteGameContent { public const string BootstrapFontAddress = "bootstrap/font/chinese"; }
public static class FontSessionBehaviorChecks {
    private static void Check(bool ok, string message) { if (!ok) throw new System.Exception(message); }
    private static void Drain(System.Collections.IEnumerator load) { while (load.MoveNext()) { } }
    public static void Run() {
        var reset = typeof(GameFontService).GetMethod("Reset", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        reset.Invoke(null, null);
        var original = new TMPro.TMP_FontAsset();
        TMPro.TMP_Settings.fallbackFontAssets.Add(original);
        UnityEngine.AddressableAssets.Addressables.Fail = true;
        Drain(GameFontService.Load());
        Check(!GameFontService.IsReady && UnityEngine.AddressableAssets.Addressables.Releases == 1,
            "Failed font requests must release their handle and allow retry.");
        var first = new TMPro.TMP_FontAsset();
        UnityEngine.AddressableAssets.Addressables.NextFont = first;
        UnityEngine.AddressableAssets.Addressables.Fail = false;
        Drain(GameFontService.Load());
        Drain(GameFontService.Load());
        Check(GameFontService.IsReady && UnityEngine.AddressableAssets.Addressables.Loads == 2
            && TMPro.TMP_Settings.fallbackFontAssets.Count == 2,
            "Retries and repeat callers must share one retained game font.");
        GameFontService.Release();
        var second = new TMPro.TMP_FontAsset();
        UnityEngine.AddressableAssets.Addressables.NextFont = second;
        Drain(GameFontService.Load());
        Check(!TMPro.TMP_Settings.fallbackFontAssets.Contains(first) && TMPro.TMP_Settings.fallbackFontAssets.Contains(second),
            "Catalog replacement must remove the previous font fallback.");
        UnityEngine.Application.Quit();
        Check(!GameFontService.IsReady && TMPro.TMP_Settings.fallbackFontAssets.Count == 1
            && TMPro.TMP_Settings.fallbackFontAssets[0] == original,
            "Shutdown must release only the fallback owned by this service.");
        Drain(GameFontService.Load());
        reset.Invoke(null, null);
        reset.Invoke(null, null);
        Check(!GameFontService.IsReady && UnityEngine.Application.Subscribers == 1
            && TMPro.TMP_Settings.fallbackFontAssets.Count == 1,
            "Repeated Play sessions without domain reload must clear stale state and register shutdown once.");
    }
}
'@
Add-Type -TypeDefinition ($service + "`n" + $fontChecks)
[FontSessionBehaviorChecks]::Run()
Write-Output 'Startup message coverage, failed-load retry, shared font ownership, catalog replacement, shutdown and domain-reload reset tests passed. Native Unity checks are required for generated glyphs.'
