$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$config = Join-Path $root 'Assets/Scripts/Item/Config'
$sources = @(
    'ItemCategory.cs',
    'ItemDetailTypes.cs',
    'ItemDefinition.cs',
    'ItemDefinitionAsset.cs',
    'ItemDefinitionTable.cs'
) | ForEach-Object { Get-Content -LiteralPath (Join-Path $config $_) -Raw }
$usings = @($sources | ForEach-Object {
    [regex]::Matches($_, '(?m)^using [^;]+;') | ForEach-Object Value
} | Select-Object -Unique)
$bodies = @($sources | ForEach-Object {
    [regex]::Replace($_, '(?m)^using [^;]+;\r?\n', '')
})

$stubs = @'
namespace UnityEngine
{
    public class ScriptableObject {}
    public class Sprite {}
    public class AudioClip {}
    public class GameObject {}
    public struct Color { public static Color white => new Color(); }
    public static class Debug
    {
        public static readonly System.Collections.Generic.List<string> Errors =
            new System.Collections.Generic.List<string>();
        public static void LogError(string message, object context) { Errors.Add(message); }
    }
    public class SerializeField : System.Attribute {}
    public class InspectorName : System.Attribute { public InspectorName(string name) {} }
    public class Tooltip : System.Attribute { public Tooltip(string text) {} }
    public class Header : System.Attribute { public Header(string text) {} }
    public class TextArea : System.Attribute
    {
        public TextArea() {}
        public TextArea(int min, int max) {}
    }
    public class CreateAssetMenu : System.Attribute
    {
        public string fileName;
        public string menuName;
    }
}
namespace UnityEditor
{
    public static class SessionState
    {
        private static readonly System.Collections.Generic.Dictionary<string, bool> values =
            new System.Collections.Generic.Dictionary<string, bool>();
        public static bool GetBool(string key, bool fallback)
        {
            return values.TryGetValue(key, out bool value) ? value : fallback;
        }
        public static void SetBool(string key, bool value) { values[key] = value; }
    }
}
namespace Game.Items
{
    public static class RemoteItemIconCache
    {
        private static readonly System.Collections.Generic.Dictionary<string, UnityEngine.Sprite> icons =
            new System.Collections.Generic.Dictionary<string, UnityEngine.Sprite>();
        public static bool TryGetIcon(string itemId, out UnityEngine.Sprite icon)
        {
            return icons.TryGetValue(itemId, out icon);
        }
        public static void Set(string itemId, UnityEngine.Sprite icon)
        {
            if (icon == null) icons.Remove(itemId);
            else icons[itemId] = icon;
        }
        public static string AddressFor(string itemId)
        {
            return "item/icon/" + itemId;
        }
    }
}
'@
$combinedSource = ($usings -join "`n") + "`n" + ($bodies -join "`n") + "`n" + $stubs
$combinedSource = [regex]::Replace($combinedSource,
    '(?m)^#(?:if UNITY_EDITOR|endif)\r?\n', '')
Add-Type -TypeDefinition $combinedSource -IgnoreWarnings | Out-Null

$local = [UnityEngine.Sprite]::new()
$remote = [UnityEngine.Sprite]::new()
$newRemote = [UnityEngine.Sprite]::new()
$trial = [Game.Items.ItemDefinition]::new()
$trial.ItemId = 'dan1_huichundan'
$trial.DisplayName = '回春丹'
$trial.Icon = $local
$trial.MaxStack = 4
$other = [Game.Items.ItemDefinition]::new()
$other.ItemId = 'another_pill'
$other.Icon = $local
$material = [Game.Items.ItemDefinition]::new()
$material.ItemId = 'cai_chiyushen'
$material.Icon = $local
$equipment = [Game.Items.ItemDefinition]::new()
$equipment.ItemId = 'weapon_xingyun_001_item'
$equipment.EquipmentId = 'weapon_xingyun_001'
$equipment.Category = [Game.Items.ItemCategory]::Equipment
$equipment.Icon = $local
$remoteOnly = [Game.Items.ItemDefinition]::new()
$remoteOnly.ItemId = 'remote_only'
$remoteOnly.RemoteIconAddress = 'item/icon/remote_only'

$table = [Game.Items.ItemDefinitionTable]::new()
$field = $table.GetType().GetField('items', [System.Reflection.BindingFlags]'Instance,NonPublic')
$list = [System.Collections.Generic.List[Game.Items.ItemDefinition]]::new()
$list.Add($trial)
$list.Add($other)
$list.Add($material)
$list.Add($equipment)
$list.Add($remoteOnly)
$field.SetValue($table, $list)
$table.RefreshLookup()

$required = $table.GetRequiredRemoteIconAddresses()
$missingId = $null
if ($required.Count -ne 1 -or $required[0] -ne 'item/icon/remote_only' -or
    $table.AreRequiredRemoteIconsLoaded([ref]$missingId) -or
    $missingId -ne 'remote_only' -or
    $table.GetOrDefault('remote_only').Icon -ne $null) {
    throw 'A remote-only item must declare its address and reject a missing downloaded icon.'
}
$remoteOnlySprite = [UnityEngine.Sprite]::new()
[Game.Items.RemoteItemIconCache]::Set('remote_only', $remoteOnlySprite)
if (-not $table.AreRequiredRemoteIconsLoaded([ref]$missingId) -or
    -not [object]::ReferenceEquals($table.GetOrDefault('remote_only').Icon,
        $remoteOnlySprite)) {
    throw 'A remote-only item must render once its remote icon is loaded.'
}
[UnityEngine.Debug]::Errors.Clear()

$foundEquipment = $null
if (-not [object]::ReferenceEquals($table.GetOrDefault($trial.ItemId), $trial)) {
    throw 'Before remote load the original item definition must be returned.'
}
if (-not [object]::ReferenceEquals($table.GetOrDefault($material.ItemId), $material) -or
    -not [object]::ReferenceEquals($table.GetOrDefault($equipment.ItemId), $equipment) -or
    -not $table.TryGetByEquipmentId($equipment.EquipmentId, [ref]$foundEquipment) -or
    -not [object]::ReferenceEquals($foundEquipment, $equipment)) {
    throw 'Materials and equipment without remote addresses must keep their local icons.'
}
[Game.Items.RemoteItemIconCache]::Set($trial.ItemId, $remote)
[Game.Items.RemoteItemIconCache]::Set($material.ItemId, $remote)
[Game.Items.RemoteItemIconCache]::Set($equipment.ItemId, $remote)
$first = $table.GetOrDefault($trial.ItemId)
$again = $table.GetOrDefault($trial.ItemId)
if ([object]::ReferenceEquals($first, $trial) -or
    -not [object]::ReferenceEquals($first, $again) -or
    -not [object]::ReferenceEquals($first.Icon, $remote) -or
    -not [object]::ReferenceEquals($trial.Icon, $local) -or
    $first.DisplayName -ne '回春丹' -or
    $first.MaxStack -ne 4 -or
    -not [object]::ReferenceEquals($table.GetOrDefault($other.ItemId), $other)) {
    throw 'Only items with a loaded remote icon may receive a cached icon-only copy.'
}
$remoteEquipment = $table.GetOrDefault($equipment.ItemId)
if (-not $table.TryGetByEquipmentId($equipment.EquipmentId, [ref]$foundEquipment) -or
    -not [object]::ReferenceEquals($remoteEquipment, $foundEquipment) -or
    -not [object]::ReferenceEquals($remoteEquipment.Icon, $remote) -or
    -not [object]::ReferenceEquals($equipment.Icon, $local) -or
    -not [object]::ReferenceEquals($table.GetOrDefault($material.ItemId).Icon, $remote)) {
    throw 'Equipment and materials must resolve the same remote icons through the item table.'
}

$trial.DisplayName = '回春丹·刷新'
$table.RefreshLookup()
if ($table.GetOrDefault($trial.ItemId).DisplayName -ne '回春丹·刷新') {
    throw 'Refreshing the item table must invalidate the cached display copy.'
}

[Game.Items.RemoteItemIconCache]::Set($trial.ItemId, $newRemote)
if (-not [object]::ReferenceEquals($table.GetOrDefault($trial.ItemId).Icon, $newRemote)) {
    throw 'A refreshed remote icon must invalidate the trial copy.'
}
[Game.Items.RemoteItemIconCache]::Set($trial.ItemId, $null)
if (-not [object]::ReferenceEquals($table.GetOrDefault($trial.ItemId), $trial)) {
    throw 'Removing the remote icon must restore the original definition.'
}
[Game.Items.RemoteItemIconCache]::Set($equipment.ItemId, $null)
if (-not $table.TryGetByEquipmentId($equipment.EquipmentId, [ref]$foundEquipment) -or
    -not [object]::ReferenceEquals($foundEquipment, $equipment)) {
    throw 'Missing remote equipment icons must restore the original definition.'
}

[UnityEditor.SessionState]::SetBool('Game.Items.StrictRemoteIconVerification', $true)
[Game.Items.RemoteItemIconCache]::Set($material.ItemId, $null)
$table.RefreshLookup()
$missingMaterial = $table.GetOrDefault($material.ItemId)
$sameMissingMaterial = $table.GetOrDefault($material.ItemId)
if ($missingMaterial.Icon -ne $null -or
    -not [object]::ReferenceEquals($missingMaterial, $sameMissingMaterial) -or
    -not [object]::ReferenceEquals($material.Icon, $local) -or
    -not $table.TryGetByEquipmentId($equipment.EquipmentId, [ref]$foundEquipment) -or
    $foundEquipment.Icon -ne $null -or
    -not [object]::ReferenceEquals($equipment.Icon, $local) -or
    $table.GetOrDefault($other.ItemId).Icon -ne $null -or
    [UnityEngine.Debug]::Errors.Count -ne 3) {
    throw 'Strict Editor verification must hide missing item icons without mutating local assets or spamming errors.'
}
[Game.Items.RemoteItemIconCache]::Set($equipment.ItemId, $newRemote)
if (-not $table.TryGetByEquipmentId($equipment.EquipmentId, [ref]$foundEquipment) -or
    -not [object]::ReferenceEquals($foundEquipment.Icon, $newRemote)) {
    throw 'Strict verification must show a successfully loaded remote equipment icon.'
}
[UnityEditor.SessionState]::SetBool('Game.Items.StrictRemoteIconVerification', $false)
if (-not [object]::ReferenceEquals($table.GetOrDefault($material.ItemId), $material)) {
    throw 'Disabling strict verification must immediately restore normal local fallback.'
}

$cache = Get-Content -LiteralPath (Join-Path $config 'RemoteItemIconCache.cs') -Raw
$startup = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/StartupContentUpdate.cs') -Raw
if ($cache -notmatch 'locator.Keys' -or
    $cache -notmatch 'locator.Locate\(address, typeof\(Sprite\)' -or
    $cache -notmatch 'HasSpriteAddress\(LegacyAddress\)' -or
    $cache -notmatch 'Addressables.LoadAssetAsync<Sprite>\(address\)' -or
    $cache -notmatch 'Addressables.Release\(handle\)' -or
    $startup -notmatch 'RemoteItemIconCache.Load\(addresses\)') {
    throw 'Remote asset discovery, lifetime or startup binding is missing.'
}

Write-Output 'Remote item icon override, strict Editor verification and fallback tests passed.'
