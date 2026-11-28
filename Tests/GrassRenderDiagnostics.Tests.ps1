$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$diagnostics = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/GrassRenderDiagnostics.cs') -Raw
$endpoint = Get-Content -LiteralPath (Join-Path $root 'Assets/Bootstrap/Runtime/StartupResourceEndpoint.cs') -Raw
$updater = Get-Content -LiteralPath (Join-Path $root 'Assets/Bootstrap/Runtime/StartupContentUpdate.cs') -Raw
$session = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/GameSession.cs') -Raw

if (-not $diagnostics.StartsWith('#if UNITY_EDITOR || DEVELOPMENT_BUILD') -or
    $diagnostics -match 'Shader\.Find|GetPixels\(' -or
    $diagnostics -notmatch 'LogCatalog\(string\[\] updatedLocators\)' -or
    $diagnostics -notmatch 'AssetBundleRequestOptions' -or
    $diagnostics -notmatch 'LogResolvedAddress\(string original, string resolved\)' -or
    $diagnostics -notmatch 'Terrain\.activeTerrains' -or
    $diagnostics -notmatch 'detailPrototypes' -or
    $diagnostics -notmatch 'CheckAlphaProbe\(\)' -or
    $diagnostics -notmatch 'RenderTexture\.active = previous' -or
    $diagnostics -notmatch 'RenderTexture\.ReleaseTemporary\(target\)' -or
    $endpoint -notmatch 'string resolved = StartupResourceUrl\.Redirect\(' -or
    $endpoint -notmatch 'GrassRenderDiagnostics\.LogResolvedAddress\(internalId, resolved\)' -or
    $updater -notmatch 'GrassRenderDiagnostics\.LogCatalog\(catalogs\)' -or
    $session -notmatch '(?s)private void Start\(\)\s*\{\s*StartSession\(\);\s*#if UNITY_EDITOR \|\| DEVELOPMENT_BUILD\s*GrassRenderDiagnostics\.LogWorld\(\);') {
    throw 'Grass diagnostic guard, identity, alpha probe, or session order is invalid.'
}

Write-Output 'Grass diagnostics scope and lifecycle tests passed.'
