$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/Item/Editor/UsedGameContentMigration.cs') -Raw
$updater = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/StartupContentUpdate.cs') -Raw
$navigation = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/RemoteGameContent.cs') -Raw

foreach ($token in @(
    'AssetDatabase.GetDependencies(StartupScene, true)',
    'table.GetRequiredRemoteIconAddresses()',
    'requiredIcons.SetEquals(registeredIcons)',
    'remoteRoots.Concat(iconPaths).Any(bootstrap.Contains)',
    'new[] { LoginScene, WorldScene, DemoMapScene, Map001Scene, ItemTable }',
    'EnsureEntry(settings, localGroup, ChineseFont',
    'EnsureEntry(settings, localGroup, SharedTextShader',
    'EnsureEntry(settings, remoteGroup, LoginScene',
    'EnsureEntry(settings, remoteGroup, WorldScene',
    'EnsureEntry(settings, remoteGroup, DemoMapScene',
    'EnsureEntry(settings, remoteGroup, Map001Scene',
    'EnsureEntry(settings, remoteGroup, ItemTable',
    'ProjectConfigData.GenerateBuildLayout = true',
    'EditorBuildSettings.scenes = new[]',
    'AddressableAssetSettings.BuildPlayerContent(out result)'
    'VerifyBuiltContent()'
)) {
    if (-not $source.Contains($token)) { throw "Migration guard is missing: $token" }
}
if ($source -match 'AssetDatabase\.GetAllAssetPaths\(\).*CreateOrMoveEntry' -or
    $updater -notmatch 'keys\.AddRange\(addresses\)' -or
    $updater -notmatch 'new List<object> \{ RemoteGameContent\.RequiredContentLabel \}' -or
    $navigation -notmatch 'Addressables\.LoadSceneAsync\(\s*address, LoadSceneMode\.Single') {
    throw 'Download scope must consist of game scene roots and item-table icon addresses.'
}
Write-Output 'Migration source guards passed; Unity build layout still requires verification.'
