$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/Item/Config/RemoteItemIconCache.cs') -Raw
$sourceUsings = [regex]::Matches($source, '(?m)^using [^;]+;') |
    ForEach-Object Value
$sourceBody = [regex]::Replace($source, '(?m)^using [^;]+;\r?\n', '')
$stubs = @'
namespace UnityEngine
{
    public class Sprite {}
    public static class Application
    {
        public static event Action quitting;
        public static void Quit() { quitting?.Invoke(); }
    }
    public static class Debug
    {
        public static readonly List<string> Warnings = new List<string>();
        public static void Log(string text) {}
        public static void LogWarning(string text) { Warnings.Add(text); }
    }
    public enum RuntimeInitializeLoadType { SubsystemRegistration }
    public class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType type) {}
    }
}
namespace UnityEngine.ResourceManagement.AsyncOperations
{
    public enum AsyncOperationStatus { Succeeded, Failed }
    public struct AsyncOperationHandle<T>
    {
        public T Result;
        public AsyncOperationStatus Status;
        public bool IsValid() { return true; }
    }
}
namespace UnityEngine.AddressableAssets.ResourceLocators
{
    public interface IResourceLocator
    {
        IEnumerable<object> Keys { get; }
        bool Locate(object key, Type type, out IList<object> locations);
    }
    public sealed class TestLocator : IResourceLocator
    {
        public readonly List<object> Entries = new List<object>();
        public IEnumerable<object> Keys { get { return Entries; } }
        public bool Locate(object key, Type type, out IList<object> locations)
        {
            bool found = Entries.Contains(key) && type == typeof(UnityEngine.Sprite);
            locations = found ? new List<object> { key } : null;
            return found;
        }
    }
}
namespace UnityEngine.AddressableAssets
{
    using UnityEngine;
    using UnityEngine.AddressableAssets.ResourceLocators;
    using UnityEngine.ResourceManagement.AsyncOperations;
    public static class Addressables
    {
        public static readonly List<IResourceLocator> Locators = new List<IResourceLocator>();
        public static readonly Dictionary<string, Sprite> Icons = new Dictionary<string, Sprite>();
        public static int Releases;
        public static IEnumerable<IResourceLocator> ResourceLocators { get { return Locators; } }
        public static AsyncOperationHandle<T> LoadAssetAsync<T>(string address) where T : class
        {
            Icons.TryGetValue(address, out Sprite icon);
            return new AsyncOperationHandle<T>
            {
                Status = icon == null ? AsyncOperationStatus.Failed : AsyncOperationStatus.Succeeded,
                Result = icon as T
            };
        }
        public static void Release<T>(AsyncOperationHandle<T> handle) { Releases++; }
    }
}
'@
Add-Type -TypeDefinition (($sourceUsings -join "`n") + "`n" + $stubs + "`n" + $sourceBody) -IgnoreWarnings | Out-Null

$locator = [UnityEngine.AddressableAssets.ResourceLocators.TestLocator]::new()
$locator.Entries.Add('item/icon/cai_chiyushen')
$locator.Entries.Add('item/icon/weapon_xingyun_001_item')
$locator.Entries.Add('item/icon/armor_001_item')
$locator.Entries.Add('item/icon/ring_001_item')
$locator.Entries.Add('trial/icon/huichun')
$locator.Entries.Add('item/icon/')
$locator.Entries.Add('unrelated')
[UnityEngine.AddressableAssets.Addressables]::Locators.Add($locator)
$duplicate = [UnityEngine.AddressableAssets.ResourceLocators.TestLocator]::new()
$duplicate.Entries.Add('item/icon/cai_chiyushen')
[UnityEngine.AddressableAssets.Addressables]::Locators.Add($duplicate)

$addresses = [Game.Items.RemoteItemIconCache]::DiscoverAddresses()
if ($addresses.Count -ne 5 -or
    -not $addresses.Contains('item/icon/cai_chiyushen') -or
    -not $addresses.Contains('item/icon/weapon_xingyun_001_item') -or
    -not $addresses.Contains('item/icon/armor_001_item') -or
    -not $addresses.Contains('item/icon/ring_001_item') -or
    -not $addresses.Contains('trial/icon/huichun')) {
    throw 'Discover must include distinct Sprite addresses plus the legacy trial key only.'
}
$remoteMaterial = [UnityEngine.Sprite]::new()
$remoteTrial = [UnityEngine.Sprite]::new()
$remoteArmor = [UnityEngine.Sprite]::new()
$remoteRing = [UnityEngine.Sprite]::new()
[UnityEngine.AddressableAssets.Addressables]::Icons['item/icon/cai_chiyushen'] = $remoteMaterial
[UnityEngine.AddressableAssets.Addressables]::Icons['trial/icon/huichun'] = $remoteTrial
[UnityEngine.AddressableAssets.Addressables]::Icons['item/icon/armor_001_item'] = $remoteArmor
[UnityEngine.AddressableAssets.Addressables]::Icons['item/icon/ring_001_item'] = $remoteRing
$load = [Game.Items.RemoteItemIconCache]::Load($addresses)
while ($load.MoveNext()) {}

$icon = $null
if (-not [Game.Items.RemoteItemIconCache]::TryGetIcon('cai_chiyushen', [ref]$icon) -or
    -not [object]::ReferenceEquals($icon, $remoteMaterial) -or
    -not [Game.Items.RemoteItemIconCache]::TryGetIcon('dan1_huichundan', [ref]$icon) -or
    -not [object]::ReferenceEquals($icon, $remoteTrial) -or
    -not [Game.Items.RemoteItemIconCache]::TryGetIcon('armor_xingyun_001_item', [ref]$icon) -or
    -not [object]::ReferenceEquals($icon, $remoteArmor) -or
    -not [Game.Items.RemoteItemIconCache]::TryGetIcon('ring_xingyun_001_item', [ref]$icon) -or
    -not [object]::ReferenceEquals($icon, $remoteRing) -or
    [Game.Items.RemoteItemIconCache]::TryGetIcon('weapon_xingyun_001_item', [ref]$icon) -or
    [UnityEngine.AddressableAssets.Addressables]::Releases -ne 1) {
    throw 'Successful icons must survive a failed individual icon without leaking its handle.'
}
if ([Game.Items.RemoteItemIconCache]::AddressFor('armor_xingyun_001_item') -ne 'item/icon/armor_001_item' -or
    [Game.Items.RemoteItemIconCache]::AddressFor('ring_xingyun_001_item') -ne 'item/icon/ring_001_item' -or
    [Game.Items.RemoteItemIconCache]::AddressFor('dan1_huichundan') -ne 'trial/icon/huichun') {
    throw 'Shared icons and legacy icons must resolve to their canonical remote addresses.'
}

[Game.Items.RemoteItemIconCache]::Clear()
if ([Game.Items.RemoteItemIconCache]::TryGetIcon('cai_chiyushen', [ref]$icon) -or
    [Game.Items.RemoteItemIconCache]::TryGetIcon('armor_xingyun_001_item', [ref]$icon) -or
    [UnityEngine.AddressableAssets.Addressables]::Releases -ne 5) {
    throw 'Clearing the cache must release loaded assets and restore local fallback.'
}

$locator.Entries.Add('item/icon/dan1_huichundan')
$addresses = [Game.Items.RemoteItemIconCache]::DiscoverAddresses()
if ($addresses.Count -ne 6 -or $addresses[0] -ne 'trial/icon/huichun') {
    throw 'The legacy key must load first when a newer key is also available.'
}
$load = [Game.Items.RemoteItemIconCache]::Load($addresses)
while ($load.MoveNext()) {}
if (-not [Game.Items.RemoteItemIconCache]::TryGetIcon('dan1_huichundan', [ref]$icon) -or
    -not [object]::ReferenceEquals($icon, $remoteTrial)) {
    throw 'A failed newer key must retain the working legacy icon.'
}
$newTrial = [UnityEngine.Sprite]::new()
[UnityEngine.AddressableAssets.Addressables]::Icons['item/icon/dan1_huichundan'] = $newTrial
$load = [Game.Items.RemoteItemIconCache]::Load($addresses)
while ($load.MoveNext()) {}
if (-not [Game.Items.RemoteItemIconCache]::TryGetIcon('dan1_huichundan', [ref]$icon) -or
    -not [object]::ReferenceEquals($icon, $newTrial)) {
    throw 'A loaded new key must supersede the legacy icon.'
}
[UnityEngine.Application]::Quit()
if ([Game.Items.RemoteItemIconCache]::TryGetIcon('dan1_huichundan', [ref]$icon)) {
    throw 'Application exit must release all loaded icons.'
}
Write-Output 'Remote icon discovery, partial failure and handle lifetime tests passed.'
