[CmdletBinding()]
param([string] $ServerProjectRoot = (Join-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) 'gameserver/GameServer'))
$ErrorActionPreference = 'Stop'
$clientRoot = Split-Path $PSScriptRoot -Parent
$destination = Join-Path $clientRoot 'Assets/Scripts/MapTraversal/Geometry'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
$files = @('IWorldSurfaceQuery.cs', 'ServerMapManifest.cs', 'ServerMapData.cs', 'ServerMapCollider.cs',
    'ServerMapCollisionIndex.cs', 'ServerMapGroundSurfaceIndex.cs', 'ServerMapMeshCollisionIndex.cs',
    'TriangleDistance.cs', 'MapTraversal.cs')
$provenance = @()
function Convert-PrimaryRecord([System.Text.RegularExpressions.Match] $match) {
    $name = $match.Groups['name'].Value
    $args = $match.Groups['args'].Value
    $indent = $match.Groups['indent'].Value
    $kind = $match.Groups['kind'].Value
    if ([string]::IsNullOrWhiteSpace($kind)) { $kind = 'class' }
    $declaration = $indent + $match.Groups['access'].Value + ' ' + $kind + ' ' + $name + "`n" + $indent + "{`n"
    $assignments = @()
    foreach ($arg in $args.Split(',')) {
        $parts = $arg.Trim() -split '\s+'
        $declaration += $indent + '    public readonly ' + $parts[0] + ' ' + $parts[1] + ";`n"
        $assignments += 'this.' + $parts[1] + ' = ' + $parts[1] + ';'
    }
    $declaration += $indent + '    public ' + $name + '(' + (($args.Split(',') | ForEach-Object { $_.Trim() }) -join ', ') + ")`n"
    $declaration += $indent + "    {`n"
    foreach ($assignment in $assignments) { $declaration += $indent + '        ' + $assignment + "`n" }
    $declaration += $indent + "    }`n"
    if ($match.Groups['tail'].Value -eq ';') { $declaration += $indent + '}' }
    $declaration
}
foreach ($file in $files) {
    $sourcePath = Join-Path $ServerProjectRoot "Game.BattleCore/Maps/$file"
    $sourceHash = (Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash.ToLowerInvariant()
    $source = Get-Content -LiteralPath $sourcePath -Raw
    $source = $source.Replace('namespace Game.BattleCore.Maps;', "namespace Game.Client.MapTraversal.Geometry`n{")
    $source = $source.Replace('using System.Text.Json.Serialization;', 'using Newtonsoft.Json;')
    $source = $source.Replace('using System.Text.Json;', 'using Newtonsoft.Json;')
    $source = $source.Replace('JsonPropertyName(', 'JsonProperty(')
    $source = $source.Replace('public sealed record ServerMapManifest', 'public sealed class ServerMapManifest')
    $source = $source.Replace('public sealed record MapVector3', 'public sealed class MapVector3')
    $source = $source.Replace('get; init;', 'get; set;')
    $source = [regex]::Replace($source,
        '(?m)^(?<indent>[ \t]*)(?<access>private readonly|public sealed) record (?<kind>struct|class)?\s*(?<name>\w+)\((?<args>[^)]+)\)\s*(?<tail>;|\{)',
        { param($m) Convert-PrimaryRecord $m })
    # The collider is a record class whose declaration omits the word "class".
    $source = $source.Replace('public sealed  ServerMapCollider', 'public sealed class ServerMapCollider')
    $source = [regex]::Replace($source, '(?s)(BoxTriangleIndices = )\[(.*?)\];', '${1}new int[] {${2}};')
    $source = $source.Replace('times[..count].Sort();', 'PortableMath.Sort(times.Slice(0, count));')
    $source = $source.Replace('Math.Clamp(', 'PortableMath.Clamp(')
    $source = $source.Replace('double.IsFinite(', 'PortableMath.IsFinite(').Replace('float.IsFinite(', 'PortableMath.IsFinite(')
    $source = [regex]::Replace($source,
        'private readonly struct Point3\(double x, double y, double z\)\s*\{\s*private readonly double _x = x, _y = y, _z = z;',
        'private readonly struct Point3 { private readonly double _x, _y, _z; public Point3(double x, double y, double z) { _x = x; _y = y; _z = z; }')
    if ($file -eq 'ServerMapData.cs') {
        $source = [regex]::Replace($source,
            '(?s)    private readonly Dictionary<\(double Radius, double Height\), NavigationQueryCache>.*?(?=    private ServerMapData\()', '')
        $load = @'
    public static ServerMapData Load(IReadOnlyDictionary<string, byte[]> files)
    {
        if (files == null) throw new ArgumentNullException(nameof(files));
        var manifest = JsonConvert.DeserializeObject<ServerMapManifest>(Encoding.UTF8.GetString(RequiredFile(files, "manifest.json")))
            ?? throw new InvalidDataException("Map manifest is invalid.");
        ValidateManifest(manifest);
        int samples = checked(manifest.Width * manifest.Height);
        float[] heightfield = ReadHeightfield(RequiredFile(files, manifest.HeightFile), samples);
        byte[] walkable = ReadWalkable(RequiredFile(files, manifest.WalkableFile), samples);
        ServerMapCollider[] colliders = ReadColliders(RequiredFile(files, manifest.ColliderFile));
        var meshes = manifest.FormatVersion >= 2
            ? ServerMapMeshCollisionIndex.Load(RequiredFile(files, manifest.MeshColliderFile), colliders.Select(c => c.ColliderId)) : null;
        var navigation = ReadNavigation(RequiredFile(files, manifest.NavMeshFile), manifest);
        var ground = manifest.FormatVersion == 3
            ? ServerMapGroundSurfaceIndex.Load(RequiredFile(files, manifest.GroundSurfaceFile), manifest) : null;
        return new ServerMapData("package:" + manifest.MapId, manifest, heightfield, walkable, colliders, meshes,
            navigation.Cells, navigation.TriangleCount, ground);
    }

'@
        $source = [regex]::Replace($source, '(?s)    public static ServerMapData Load\(string directoryPath\).*?(?=    public bool IsInsideBounds)', $load)
        $source = $source.Replace('ReadHeightfield(string path,', 'ReadHeightfield(byte[] bytes,')
        $source = $source.Replace('ReadWalkable(string path,', 'ReadWalkable(byte[] bytes,')
        $source = $source.Replace('ReadColliders(string path)', 'ReadColliders(byte[] bytes)')
        $source = $source.Replace('ReadNavigation(string path,', 'ReadNavigation(byte[] bytes,')
        $source = $source.Replace('        byte[] bytes = File.ReadAllBytes(path);', '')
        $safe = @'
    private static byte[] RequiredFile(IReadOnlyDictionary<string, byte[]> files, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName.IndexOf('/') >= 0 || fileName.IndexOf((char)92) >= 0
            || !files.TryGetValue(fileName, out byte[] bytes))
            throw new InvalidDataException("Missing or invalid map data file: " + fileName);
        return bytes;
    }
}
'@
        $source = [regex]::Replace($source, '(?s)    private static string SafeMapFilePath\(.*\z', $safe)
    }
    if ($file -eq 'ServerMapMeshCollisionIndex.cs' -or $file -eq 'ServerMapGroundSurfaceIndex.cs') {
        $source = $source.Replace('Load(string path,', 'Load(byte[] data,')
        $source = $source.Replace('File.OpenRead(path)', 'new MemoryStream(data, false)')
    }
    $header = "// Adapted from Game.BattleCore/Maps/$file. Source SHA256: $sourceHash`n"
    $header += "// Geometry predicates retain backend arithmetic; changes are C# 9/runtime I/O compatibility only.`n"
    $header += "#nullable enable annotations`nusing System;`nusing System.Collections.Generic;`nusing System.IO;`nusing System.Linq;`n"
    $targetPath = Join-Path $destination $file
    Set-Content -LiteralPath $targetPath -Value ($header + $source + "`n}") -Encoding utf8
    $provenance += [pscustomobject]@{ sourceFile = "Game.BattleCore/Maps/$file"; sourceSha256 = $sourceHash;
        adaptedFile = "Assets/Scripts/MapTraversal/Geometry/$file"; adaptedSha256 = (Get-FileHash -LiteralPath $targetPath).Hash.ToLowerInvariant() }
}
$hashText = ($provenance | Sort-Object sourceFile | ForEach-Object { $_.sourceFile + ':' + $_.sourceSha256 }) -join [char]10
$hasher = [Security.Cryptography.SHA256]::Create()
try { $fingerprint = [BitConverter]::ToString($hasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($hashText))).Replace('-', '').ToLowerInvariant() }
finally { $hasher.Dispose() }
$buildInfo = "namespace Game.Client.MapTraversal { internal static class MapTraversalBuildInfo { public const string QuerySourceFingerprint = `"$fingerprint`"; } }"
Set-Content -LiteralPath (Join-Path $destination '../MapTraversalBuildInfo.cs') -Value $buildInfo -Encoding utf8
[ordered]@{ querySourceFingerprint = $fingerprint; sourceRoot = [IO.Path]::GetFullPath($ServerProjectRoot); files = $provenance } |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $clientRoot 'Docs/MapTraversalGeometrySources.json') -Encoding utf8
Write-Output 'Imported the backend map query geometry with Unity C# 9 and memory-source adapters.'
