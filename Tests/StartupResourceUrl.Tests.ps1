$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Join-Path $root 'Assets/Scripts/StartupResourceUrl.cs'
Add-Type -Path $source

$built = 'http://127.0.0.1:18761/StandaloneWindows64'
$normalized = ''
$errorMessage = ''

if (-not [StartupResourceUrl]::TryNormalize('http://192.168.1.20:18761',
        $built, [ref]$normalized, [ref]$errorMessage) -or
    $normalized -ne 'http://192.168.1.20:18761/StandaloneWindows64') {
    throw "Host-only address did not retain the build target: $errorMessage"
}

$selected = 'https://assets.example.test/game/StandaloneWindows64'
if (-not [StartupResourceUrl]::TryNormalize($selected, $built,
        [ref]$normalized, [ref]$errorMessage) -or $normalized -ne $selected) {
    throw "Full resource address was not accepted: $errorMessage"
}

foreach ($invalid in @(
        'ftp://assets.example.test/StandaloneWindows64',
        'https://user:secret@assets.example.test/StandaloneWindows64',
        'https://assets.example.test/StandaloneWindows64?token=secret',
        'https://assets.example.test/OtherPlatform')) {
    if ([StartupResourceUrl]::TryNormalize($invalid, $built,
            [ref]$normalized, [ref]$errorMessage)) {
        throw "Invalid resource address was accepted: $invalid"
    }
}

foreach ($name in @('catalog_0.1.hash', 'catalog_0.1.json',
        'remotegamecontent_scenes_all_abcd.bundle')) {
    $original = "$built/$name"
    $expected = "$selected/$name"
    $actual = [StartupResourceUrl]::Redirect($original, $built, $selected)
    if ($actual -ne $expected) {
        throw "Remote file was not redirected: $actual"
    }
}

foreach ($local in @(
        '{UnityEngine.AddressableAssets.Addressables.RuntimePath}/catalog.json',
        'http://auth.example.test/login',
        'http://127.0.0.1:18761/StandaloneWindows64/other.txt')) {
    if ([StartupResourceUrl]::Redirect($local, $built, $selected) -ne $local) {
        throw "Unrelated path was redirected: $local"
    }
}

$updater = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/StartupContentUpdate.cs') -Raw
$controller = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/StartupSceneController.cs') -Raw
$endpoint = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/StartupResourceEndpoint.cs') -Raw
if ($controller.IndexOf('resourceSettings.TryApply(') -lt 0 -or
    $controller.IndexOf('resourceSettings.TryApply(') -gt $controller.IndexOf('contentUpdate.Run()') -or
    $endpoint -notmatch 'string\.IsNullOrWhiteSpace\(input\) \? BuiltAddress : input' -or
    $endpoint -notmatch 'Addressables\.InternalIdTransformFunc = transform' -or
    $updater -notmatch 'Addressables\.InitializeAsync\(false\)' -or
    $updater -match 'SelectResourceAddress|OnGUI|GUI\.TextField|addressVisible' -or
    $endpoint -match 'PlayerPrefs|PreferenceKey|SavedAddress') {
    throw 'The Inspector address must apply before initialization without runtime input or saved overrides.'
}

Write-Output 'Startup resource URL tests passed.'
