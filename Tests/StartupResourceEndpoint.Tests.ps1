$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$url = Get-Content -LiteralPath (Join-Path $root 'Assets/Bootstrap/Runtime/StartupResourceUrl.cs') -Raw
$endpoint = Get-Content -LiteralPath (Join-Path $root 'Assets/Bootstrap/Runtime/StartupResourceEndpoint.cs') -Raw
$settings = Get-Content -LiteralPath (Join-Path $root 'Assets/Bootstrap/Runtime/StartupResourceServerSettings.cs') -Raw
$stubs = @'
namespace UnityEngine
{
    public class MonoBehaviour {}
    public sealed class SerializeField : System.Attribute {}
    public sealed class HideInInspector : System.Attribute {}
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
    public class TextAsset { public string text; }
    public static class Resources { public static T Load<T>(string path) { return default(T); } }
    public static class JsonUtility
    {
        public static T FromJson<T>(string value) { return default(T); }
    }
}
namespace QiDao.Bootstrap.CodeUpdate
{
    public class CodeUpdateConfiguration { public string manifestUrl; }
    public static class CodeUpdateProtocol
    {
        public static System.Uri ManifestUri(CodeUpdateConfiguration config) { return new System.Uri(config.manifestUrl); }
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
$usings = "using System.Collections.Generic;`n" +
    ($endpoint.Substring(0, $classStart) -replace '(?m)^using System;\r?\n', '')
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

# Migrate an old scene without changing its active address, and migrate only once.
$legacyAddress = 'http://192.168.31.126:18761/StandaloneWindows64'
$cloudAddress = 'http://47.93.78.106:18761/StandaloneWindows64'
$field.SetValue($component, $legacyAddress)
if (-not $component.MigrateLegacyAddress()) {
    throw 'An existing single-address scene must migrate to a selected list entry.'
}
$instanceBinding = [System.Reflection.BindingFlags]'NonPublic,Instance'
$serversField = $component.GetType().GetField('resourceServers', $instanceBinding)
$selectionField = $component.GetType().GetField('selectedServerId', $instanceBinding)
$servers = $serversField.GetValue($component)
$originalServer = $servers[0]
if ($originalServer.Address -ne $legacyAddress -or
    $selectionField.GetValue($component) -ne $originalServer.Id -or
    $field.GetValue($component) -ne '' -or $component.MigrateLegacyAddress()) {
    throw 'Migration must preserve the active address, clear the legacy field, and be idempotent.'
}

# Selection controls the catalog, hash, and nested bundle URLs.
$cloudServer = [StartupResourceServerProfile]::new('Cloud', $cloudAddress)
$servers.Add($cloudServer)
$selectionField.SetValue($component, $cloudServer.Id)
if (-not $component.TryApply([ref]$errorMessage)) {
    throw "Selected cloud server was rejected: $errorMessage"
}
$cloudTransform = [UnityEngine.AddressableAssets.Addressables]::InternalIdTransformFunc
foreach ($name in @('catalog_0.1.hash', 'catalog_0.1.json', 'maps/world001.bundle')) {
    $location = [UnityEngine.ResourceManagement.ResourceLocations.TestLocation]::new()
    $location.InternalId = "$built/$name"
    if ($cloudTransform.Invoke($location) -ne "$cloudAddress/$name") {
        throw "Selected server did not redirect a remote dependency: $name"
    }
}

# Display names and list order must not change the selected server.
$cloudServer.GetType().GetField('name', $instanceBinding).SetValue($cloudServer, 'Renamed cloud')
$servers.Reverse()
$resolvedAddress = ''
if (-not $component.TryGetSelectedAddress([ref]$resolvedAddress, [ref]$errorMessage) -or
    $resolvedAddress -ne $cloudAddress) {
    throw 'Renaming or reordering a profile must retain selection by its stable identifier.'
}
$selectionField.SetValue($component, $originalServer.Id)
if (-not $component.TryApply([ref]$errorMessage)) {
    throw "Switching back to the LAN server failed: $errorMessage"
}
$location.InternalId = "$built/maps/world001.bundle"
if ([UnityEngine.AddressableAssets.Addressables]::InternalIdTransformFunc.Invoke($location) -ne
    "$legacyAddress/maps/world001.bundle") {
    throw 'Switching back to the original server must restore its resource destination.'
}

# Broken selections must not silently connect to a different server.
$selectionField.SetValue($component, $cloudServer.Id)
if (-not $component.TryApply([ref]$errorMessage)) { throw $errorMessage }
$selectionField.SetValue($component, 'missing-server')
if ($component.TryApply([ref]$errorMessage) -or [string]::IsNullOrWhiteSpace($errorMessage) -or
    [UnityEngine.AddressableAssets.Addressables]::InternalIdTransformFunc.Invoke($location) -ne
        "$cloudAddress/maps/world001.bundle") {
    throw 'A missing selected server must fail without changing the applied destination.'
}
$duplicate = [StartupResourceServerProfile]::new('Duplicate', $legacyAddress)
$duplicate.GetType().GetField('id', $instanceBinding).SetValue($duplicate, $cloudServer.Id)
$servers.Add($duplicate)
$selectionField.SetValue($component, $cloudServer.Id)
if ($component.TryApply([ref]$errorMessage)) {
    throw 'Duplicate selected identifiers must fail rather than pick an arbitrary server.'
}
$servers.Remove($duplicate) | Out-Null

$invalidServer = [StartupResourceServerProfile]::new('Invalid', 'ftp://invalid.example.test')
$servers.Add($invalidServer)
if (-not $component.TryApply([ref]$errorMessage)) {
    throw 'An inactive invalid address must not block the valid selected server.'
}
$selectionField.SetValue($component, $invalidServer.Id)
if ($component.TryApply([ref]$errorMessage) -or
    [UnityEngine.AddressableAssets.Addressables]::InternalIdTransformFunc.Invoke($location) -ne
        "$cloudAddress/maps/world001.bundle") {
    throw 'An invalid selected URL must fail without changing the applied destination.'
}

# The built-in choice must work alongside saved servers, without resurrecting legacy data.
$selectionField.SetValue($component, '')
if (-not $component.TryApply([ref]$errorMessage) -or
    $null -ne [UnityEngine.AddressableAssets.Addressables]::InternalIdTransformFunc) {
    throw 'The explicit built-in choice must remove the custom resource destination.'
}
$servers.Clear()
if ($component.MigrateLegacyAddress() -or -not $component.TryApply([ref]$errorMessage) -or
    $null -ne [UnityEngine.AddressableAssets.Addressables]::InternalIdTransformFunc) {
    throw 'Removing all migrated profiles must not recreate the previous legacy address.'
}

# A default Inspector can create a new entry before the custom Inspector loads.
# That must neither erase the old entry nor implicitly change its destination.
$lateMigration = [StartupResourceServerSettings]::new()
$lateServers = $serversField.GetValue($lateMigration)
$typedCloud = [StartupResourceServerProfile]::new('User-entered cloud', $cloudAddress)
$lateServers.Add($typedCloud)
$field.SetValue($lateMigration, $legacyAddress)
if (-not $lateMigration.TryGetSelectedAddress([ref]$resolvedAddress, [ref]$errorMessage) -or
    $resolvedAddress -ne $legacyAddress -or -not $lateMigration.MigrateLegacyAddress() -or
    $lateServers.Count -ne 2 -or $typedCloud.Address -ne $cloudAddress -or
    $typedCloud.Name -ne 'User-entered cloud' -or
    -not $lateMigration.TryGetSelectedAddress([ref]$resolvedAddress, [ref]$errorMessage) -or
    $resolvedAddress -ne $legacyAddress) {
    throw 'Migration after adding a new server must retain both addresses and the original destination.'
}
if ($lateMigration.MigrateLegacyAddress() -or $lateServers.Count -ne 2) {
    throw 'Repeated migration must not duplicate restored entries.'
}

$explicitSelection = [StartupResourceServerSettings]::new()
$explicitServers = $serversField.GetValue($explicitSelection)
$explicitServers.Add($typedCloud)
$field.SetValue($explicitSelection, $legacyAddress)
$selectionField.SetValue($explicitSelection, $typedCloud.Id)
if (-not $explicitSelection.MigrateLegacyAddress() -or $explicitServers.Count -ne 2 -or
    $selectionField.GetValue($explicitSelection) -ne $typedCloud.Id) {
    throw 'Restoring the old address must not overwrite an explicitly selected new server.'
}

$alreadyListed = [StartupResourceServerSettings]::new()
$alreadyListedServers = $serversField.GetValue($alreadyListed)
$listedLegacy = [StartupResourceServerProfile]::new('My original name', $legacyAddress)
$alreadyListedServers.Add($listedLegacy)
$field.SetValue($alreadyListed, $legacyAddress)
if (-not $alreadyListed.MigrateLegacyAddress() -or $alreadyListedServers.Count -ne 1 -or
    $selectionField.GetValue($alreadyListed) -ne $listedLegacy.Id -or
    $listedLegacy.Name -ne 'My original name') {
    throw 'Migration must reuse an already listed legacy address and preserve its name.'
}

$validationSettings = [StartupResourceServerSettings]::new()
$validationServers = $serversField.GetValue($validationSettings)
$firstServer = [StartupResourceServerProfile]::new('First', $legacyAddress)
$clonedServer = [StartupResourceServerProfile]::new('Clone', $cloudAddress)
$emptyIdServer = [StartupResourceServerProfile]::new('Uninitialized', '')
$idField = $firstServer.GetType().GetField('id', $instanceBinding)
$idField.SetValue($clonedServer, $firstServer.Id)
$idField.SetValue($emptyIdServer, '')
$validationServers.Add($firstServer)
$validationServers.Add($clonedServer)
$validationServers.Add($emptyIdServer)
$selectionField.SetValue($validationSettings, $firstServer.Id)
$validateMethod = $component.GetType().GetMethod('OnValidate', $instanceBinding)
$validateMethod.Invoke($validationSettings, $null) | Out-Null
if ([string]::IsNullOrEmpty($emptyIdServer.Id) -or $firstServer.Id -eq $clonedServer.Id -or
    $selectionField.GetValue($validationSettings) -ne $firstServer.Id) {
    throw 'Validation must repair cloned or missing identifiers without changing the original selection.'
}
$stableIds = @($validationServers | ForEach-Object { $_.Id }) -join ','
$validateMethod.Invoke($validationSettings, $null) | Out-Null
if ((@($validationServers | ForEach-Object { $_.Id }) -join ',') -ne $stableIds) {
    throw 'Validation must leave valid identifiers stable.'
}

Write-Output 'Startup resource endpoint redirection, selection, and migration tests passed.'
