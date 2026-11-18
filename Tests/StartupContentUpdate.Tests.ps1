$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/StartupContentUpdate.cs') -Raw
$settings = Get-Content -LiteralPath (Join-Path $root 'Assets/AddressableAssetsData/AddressableAssetSettings.asset') -Raw
$schema = Get-Content -LiteralPath (Join-Path $root 'Assets/AddressableAssetsData/AssetGroups/Schemas/RemoteTrialOnly_BundledAssetGroupSchema.asset') -Raw
$group = Get-Content -LiteralPath (Join-Path $root 'Assets/AddressableAssetsData/AssetGroups/RemoteTrialOnly.asset') -Raw
$scene = Get-Content -LiteralPath (Join-Path $root 'Assets/Scenes/启动场景.unity') -Raw
$builder = Get-Content -LiteralPath (Join-Path $root 'Assets/Editor/AddressablesLocalTrialSetup.cs') -Raw

$required = @(
    'Addressables.InitializeAsync(false)',
    'RemoteGameContent.BootstrapFontAddress',
    'Addressables.CheckForCatalogUpdates(false)',
    'Addressables.UpdateCatalogs(catalogs, false)',
    'Addressables.LoadResourceLocationsAsync(',
    'RemoteGameContent.RequiredContentLabel',
    'RemoteGameContent.ItemTableAddress',
    'requiredItemIcons.GetRequiredRemoteIconAddresses()',
    'IEnumerable downloadKeys = keys',
    'Addressables.GetDownloadSizeAsync(downloadKeys)',
    'Addressables.DownloadDependenciesAsync(',
    'downloadKeys, Addressables.MergeMode.Union, false)',
    'Addressables.MergeMode.Union',
    'RemoteItemIconCache.Load(addresses)',
    'requiredItemIcons.AreRequiredRemoteIconsLoaded(out string missingItemId)',
    'if (addresses.Count == 0)',
    'download.GetDownloadStatus().DownloadedBytes',
    'download.Status == AsyncOperationStatus.Succeeded',
    'retryButton.SetActive(true)',
    'while (!retryRequested)',
    'Addressables.Release(initialize)',
    'Addressables.Release(check)',
    'Addressables.Release(update)',
    'Addressables.Release(size)',
    'Addressables.Release(download)'
)
foreach ($token in $required) {
    if (-not $source.Contains($token)) { throw "Startup updater is missing: $token" }
}
if ($source.IndexOf('Addressables.CheckForCatalogUpdates(false)') -gt
    $source.IndexOf('Addressables.LoadAssetAsync<TMP_FontAsset>')) {
    throw 'Refresh the catalog before loading the bootstrap font from an old cached catalog.'
}
if ($source -notmatch 'if \(!downloadedSuccessfully\)(?s:.*?)Fail\("资源下载失败"\)' -or
    $source -notmatch 'RemoteItemIconCache.Clear\(\)' -or
    $source -notmatch 'keys\.AddRange\(addresses\)') {
    throw 'Failed downloads must stop startup; only item-table icons and game content should download.'
}

if ($settings -notmatch '(?m)^  m_DisableCatalogUpdateOnStart: 1$' -or
    $settings -notmatch '(?m)^  m_CatalogRequestsTimeout: 5$' -or
    $schema -notmatch '(?s)m_BuildPath:\s+m_Id: c3f93f389ba4bd04eaa9b6259c3c1029' -or
    $schema -notmatch '(?s)m_LoadPath:\s+m_Id: 23bbf730465448142a8d74e00d6f2c14' -or
    $group -notmatch 'm_Address: trial/icon/huichun' -or
    $scene -match 'chineseFont: \{fileID: 11400000' -or
    $scene -match 'requiredItemIcons: \{fileID: 11400000' -or
    $builder -notmatch 'settings.DisableCatalogUpdateOnStartup = true;' -or
    $builder -notmatch 'bundle.Timeout = 10;') {
    throw 'The isolated remote trial settings or startup font binding are invalid.'
}

$otherSources = @(
    Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/LoginScripts/AuthApiClient.cs') -Raw
    Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/Inventory/Integration/V2/InventoryV2Session.cs') -Raw
)
if ($otherSources -match 'trial/icon/huichun') {
    throw 'The trial Addressables key must not be used by login or inventory yet.'
}

Write-Output 'Startup content update configuration tests passed.'
