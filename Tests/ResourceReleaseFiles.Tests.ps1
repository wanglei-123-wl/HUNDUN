$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Add-Type -Path (Join-Path $root 'Assets/Editor/ResourceReleaseFiles.cs')
$parent = Join-Path $root 'Temp'
$testRoot = Join-Path $parent ('ReleaseFilesTests-' + [guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $testRoot
try {
    $source = Join-Path $testRoot 'source'
    $null = New-Item -ItemType Directory -Path (Join-Path $source 'nested')
    $file = Join-Path $source 'nested/catalog.hash'
    [IO.File]::WriteAllText($file, 'verified content')
    $valid = [ResourceReleaseFiles]::Inside($source, $file)
    if ($valid -ne [IO.Path]::GetFullPath($file)) { throw 'A valid child was rejected.' }
    foreach ($path in @($source, (Join-Path $testRoot 'source-sibling/file'), (Join-Path $source '../outside'))) {
        $rejected = $false
        try { $null = [ResourceReleaseFiles]::Inside($source, $path) } catch { $rejected = $true }
        if (!$rejected) { throw "Unsafe boundary was accepted: $path" }
    }
    $copy = Join-Path $testRoot 'copy'
    [ResourceReleaseFiles]::CopyTree($source, $copy)
    $target = Join-Path $copy 'nested/catalog.hash'
    if ([ResourceReleaseFiles]::Hash($file) -ne [ResourceReleaseFiles]::Hash($target)) { throw 'Copied content differs.' }
    $rejected = $false
    try { [ResourceReleaseFiles]::CopyTree($source, $copy) } catch { $rejected = $true }
    if (!$rejected) { throw 'Existing release content was overwritten.' }
    [IO.File]::WriteAllText($target, 'tampered')
    if ([ResourceReleaseFiles]::Hash($file) -eq [ResourceReleaseFiles]::Hash($target)) { throw 'Artifact tampering was not detected.' }
    $nested = Join-Path $source 'remoteworldmaps_assets_game/map-traversal/map_007_hash.bundle'
    $other = Join-Path $source 'different/map_007_hash.bundle'
    $found = [ResourceReleaseFiles]::FindBundleFile('remoteworldmaps_assets_game/map-traversal/map_007_hash.bundle', [string[]]@($nested, $other))
    if ($found -ne $nested) { throw 'Nested logical bundle names lost their directory hierarchy.' }
    $name = [ResourceReleaseFiles]::FindBundleName('http://example.test/StandaloneWindows64/dir/shared.bundle', [string[]]@('shared.bundle', 'dir/shared.bundle'))
    if ($name -ne 'dir/shared.bundle') { throw 'A catalog path must resolve to the most specific logical bundle name.' }
    foreach ($name in @('../map.bundle', 'missing.bundle', 'map_007_hash.bundle')) {
        $rejected = $false
        try { $null = [ResourceReleaseFiles]::FindBundleFile($name, [string[]]@($nested, $other)) } catch { $rejected = $true }
        if (!$rejected) { throw "Missing, escaped or ambiguous bundle name was accepted: $name" }
    }
} finally {
    $resolved = [IO.Path]::GetFullPath($testRoot)
    if (!$resolved.StartsWith([IO.Path]::GetFullPath($parent).TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing to remove a test directory outside project Temp.'
    }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
Write-Output 'Release path containment, non-overwriting copies, exact content hashes and tampering tests passed.'
