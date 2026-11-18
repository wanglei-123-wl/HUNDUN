$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$url = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/StartupResourceUrl.cs') -Raw
$endpoint = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/StartupResourceEndpoint.cs') -Raw
$settings = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/StartupResourceServerSettings.cs') -Raw
$stubs = @'
namespace UnityEngine
{
    public class MonoBehaviour {}
    public sealed class SerializeField : System.Attribute {}
    public sealed class InspectorName : System.Attribute
    {
        public InspectorName(string name) {}
    }
    public sealed class Tooltip : System.Attribute
    {
        public Tooltip(string text) {}
    }
    public enum RuntimeInitializeLoadType { SubsystemRegistration }
    public sealed class RuntimeInitializeOnLoadMethodAttribute : System.Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType loadType) {}
    }
    public static class Debug { public static void LogWarning(string message) {} }
    public static class JsonUtility
    {
        public static T FromJson<T>(string value) { return default(T); }
    }
}
namespace UnityEngine.ResourceManagement.ResourceLocations
{
    public interface IResourceLocation { string InternalId { get; } }
    public sealed class TestLocation : IResourceLocation
    {
        public string InternalId { get; set; }
    }
}
namespace UnityEngine.AddressableAssets.ResourceLocators
{
    public sealed class ResourceLocationData
    {
        public string[] Keys;
        public string InternalId;
    }
}
namespace UnityEngine.AddressableAssets.Initialization
{
    public sealed class ResourceManagerRuntimeData
    {
        public UnityEngine.AddressableAssets.ResourceLocators.ResourceLocationData[] CatalogLocations;
    }
}
namespace UnityEngine.AddressableAssets
{
    public static class Addressables
    {
        public static string RuntimePath = "";
        public static System.Func<
            UnityEngine.ResourceManagement.ResourceLocations.IResourceLocation, string>
            InternalIdTransformFunc;
    }
}
'@
$classStart = $endpoint.IndexOf('internal static class StartupResourceEndpoint')
$settingsStart = $settings.IndexOf('public sealed class StartupResourceServerSettings')
$usings = $endpoint.Substring(0, $classStart) -replace '(?m)^using System;\r?\n', ''
Add-Type -TypeDefinition ($usings +
    $url + "`n" + $endpoint.Substring($classStart) + "`n" +
    $settings.Substring($settingsStart) + "`n" + $stubs) -IgnoreWarnings | Out-Null

$type = [StartupResourceUrl].Assembly.GetType('StartupResourceEndpoint')
$binding = [System.Reflection.BindingFlags]'NonPublic,Static'
$type.GetField('builtAddress', $binding).SetValue(
    $null, 'http://127.0.0.1:18761/StandaloneWindows64')
$built = 'http://127.0.0.1:18761/StandaloneWindows64'
$selected = 'https://cdn.example.test/game/StandaloneWindows64'

$component = [StartupResourceServerSettings]::new()
$field = $component.GetType().GetField('resourceServerAddress',
    [System.Reflection.BindingFlags]'NonPublic,Instance')
$field.SetValue($component, $selected)
$errorMessage = ''
if (-not $component.TryApply([ref]$errorMessage)) {
    throw "Valid Inspector address was rejected: $errorMessage"
}
$transform = [UnityEngine.AddressableAssets.Addressables]::InternalIdTransformFunc
if ($null -eq $transform) { throw 'Addressables URL transform was not installed.' }
foreach ($name in @('catalog_0.1.hash', 'catalog_0.1.json', 'scenes.bundle')) {
    $location = [UnityEngine.ResourceManagement.ResourceLocations.TestLocation]::new()
    $location.InternalId = "$built/$name"
    if ($transform.Invoke($location) -ne "$selected/$name") {
        throw "Remote file did not use the Inspector address: $name"
    }
}
$local = [UnityEngine.ResourceManagement.ResourceLocations.TestLocation]::new()
$local.InternalId = 'Assets/Scenes/启动场景.unity'
if ($transform.Invoke($local) -ne $local.InternalId) {
    throw 'Bootstrap content must not be redirected.'
}

$field.SetValue($component, 'ftp://invalid.example.test')
if ($component.TryApply([ref]$errorMessage) -or
    [UnityEngine.AddressableAssets.Addressables]::InternalIdTransformFunc -ne $transform) {
    throw 'Invalid address must be rejected without changing the active transform.'
}

$field.SetValue($component, '')
if (-not $component.TryApply([ref]$errorMessage) -or
    $null -ne [UnityEngine.AddressableAssets.Addressables]::InternalIdTransformFunc) {
    throw "Empty Inspector address must restore the built-in Profile address: $errorMessage"
}

Write-Output 'Startup resource endpoint redirection and default tests passed.'
