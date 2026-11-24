$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/StartupContentUpdate.cs') -Raw
$synchronization = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/StartupCatalogSynchronization.cs') -Raw
$plan = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/StartupResourceDownloadPlan.cs') -Raw
$combined = $source + $synchronization + $plan
$settings = Get-Content -LiteralPath (Join-Path $root 'Assets/AddressableAssetsData/AddressableAssetSettings.asset') -Raw
$schema = Get-Content -LiteralPath (Join-Path $root 'Assets/AddressableAssetsData/AssetGroups/Schemas/RemoteTrialOnly_BundledAssetGroupSchema.asset') -Raw
$group = Get-Content -LiteralPath (Join-Path $root 'Assets/AddressableAssetsData/AssetGroups/RemoteTrialOnly.asset') -Raw
$scene = Get-Content -LiteralPath (Join-Path $root 'Assets/Scenes/启动场景.unity') -Raw
$builder = Get-Content -LiteralPath (Join-Path $root 'Assets/Editor/AddressablesLocalTrialSetup.cs') -Raw

$required = @(
    'Addressables.InitializeAsync(false)',
    'RemoteGameContent.BootstrapFontAddress',
    'Addressables.CheckForCatalogUpdates(false)',
    'Addressables.UpdateCatalogs(false, catalogs, false)',
    'StartupResourceDownloadPlan.Create(Addressables.ResourceLocators)',
    'RemoteGameContent.ItemTableAddress',
    'requiredItemIcons.GetRequiredRemoteIconAddresses()',
    'new StartupResourceDownloadOperation(plan.DownloadLocations',
    'yield return cacheVerification.Run()',
    'yield return catalogSync.ConfirmBeforeLogin()',
    'yield return allResources.Run()',
    'if (!allResources.Succeeded)',
    'RemoteItemIconCache.Load(addresses)',
    'requiredItemIcons.AreRequiredRemoteIconsLoaded(out string missingItemId)',
    'if (addresses.Count == 0)',
    'retryButton.SetActive(true)',
    'while (!retryRequested)',
    'Addressables.Release(initialize)',
    'Addressables.Release(check)',
    'Addressables.Release(update)',
    'Application.isEditor'
)
foreach ($token in $required) {
    if (-not $combined.Contains($token)) { throw "Startup updater is missing: $token" }
}
if ($source.IndexOf('yield return catalogSync.Run()') -gt
    $source.IndexOf('GameFontService.Load()')) {
    throw 'Refresh the catalog before loading the bootstrap font from an old cached catalog.'
}
if ($source.IndexOf('ReleaseAttemptContent();') -gt $source.IndexOf('yield return catalogSync.Run()') -or
    $source.IndexOf('yield return cacheVerification.Run()') -gt $source.IndexOf('GameFontService.Load()') -or
    $source.IndexOf('yield return catalogSync.ConfirmBeforeLogin()') -lt $source.IndexOf('RemoteItemIconCache.Load(addresses)')) {
    throw 'Release previous handles before updates, validate cached bundles before loading assets, and confirm the server version before login.'
}
if ($source -notmatch 'if \(!allResources.Succeeded\)(?s:.*?)Fail\(StartupUiText.ResourcesDownloadFailed\)' -or
    $source -notmatch 'RemoteItemIconCache.Clear\(\)' -or
    $source -match 'gameLocations.Result.Count == requiredAddresses.Length') {
    throw 'Failed or incomplete full-resource downloads must stop startup; do not retain the fixed-map label boundary.'
}

if ($settings -notmatch '(?m)^  m_DisableCatalogUpdateOnStart: 1\r?$' -or
    $settings -notmatch '(?m)^  m_CatalogRequestsTimeout: 5\r?$' -or
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
