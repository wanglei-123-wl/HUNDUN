$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$ownership = Get-Content -LiteralPath (Join-Path $root 'Assets/Editor/ResourceMigrationAuditModel.cs') -Raw
$planner = Get-Content -LiteralPath (Join-Path $root 'Assets/Editor/RemoteArtPackingModel.cs') -Raw
$planner = $planner -replace '(?m)^using [^\r\n]+;\r?\n', ''
$checks = @'
public static class RemoteArtPackingChecks {
    private static void Check(bool condition, string message) { if (!condition) throw new System.Exception(message); }
    private static System.Collections.Generic.HashSet<string> Set(params string[] values) =>
        new System.Collections.Generic.HashSet<string>(values, System.StringComparer.Ordinal);
    public static void Run() {
        var direct = new System.Collections.Generic.Dictionary<string, string[]> {
            ["map-a"] = new[] { "npc" }, ["map-b"] = new[] { "npc" },
            ["npc"] = new[] { "model", "material" }, ["material"] = new[] { "texture" },
            ["model"] = new string[0], ["texture"] = new string[0]
        };
        var roots = new[] {
            new ResourceMigrationStage3.PackingRoot { Path = "map-a", Bucket = "a" },
            new ResourceMigrationStage3.PackingRoot { Path = "map-b", Bucket = "b" }
        };
        var eligible = Set("npc", "model", "material", "texture");
        var required = Set("npc", "model", "material", "texture");
        var result = ResourceMigrationStage3.PackingPlanner.Create(roots, direct, eligible, required);
        Check(result.Promotions.Count == 1 && result.Promotions[0].Path == "npc",
            "A shared prefab must retain its exclusive descendants without making each one an entry.");
        direct["map-b"] = new[] { "npc", "texture" };
        result = ResourceMigrationStage3.PackingPlanner.Create(roots, direct, eligible, required);
        Check(result.Promotions.Any(item => item.Path == "texture") && result.After["texture"].Count == 1,
            "A child also referenced independently must gain its own shared boundary.");
        Check(result.Promotions.All(item => item.Group == ResourceMigrationStage3.PackingPlanner.RequiredSharedGroup),
            "Assets needed by the startup download must not be assigned to deferred map content.");
        result = ResourceMigrationStage3.PackingPlanner.Create(roots, direct, eligible, Set());
        Check(result.Promotions.All(item => item.Group == ResourceMigrationStage3.PackingPlanner.DeferredSharedGroup),
            "Future-map-only assets must remain deferred.");
        var protectedEligible = Set("npc");
        result = ResourceMigrationStage3.PackingPlanner.Create(roots, direct, protectedEligible, required);
        Check(result.Promotions.All(item => protectedEligible.Contains(item.Path)),
            "Built-in and other excluded dependencies must never be promoted remotely.");
        direct["npc"] = new[] { "material" };
        direct["material"] = new[] { "npc" };
        direct["map-b"] = new[] { "npc" };
        result = ResourceMigrationStage3.PackingPlanner.Create(roots, direct, eligible, required);
        Check(result.Promotions.Count > 0 && result.Promotions.Count <= 2 && result.After.Values.All(owners => owners.Count == 1),
            "Cyclic shared references must converge without creating redundant owners.");
        var again = ResourceMigrationStage3.PackingPlanner.Create(roots.Reverse(), direct, eligible, required);
        Check(string.Join(",", result.Promotions.Select(item => item.Path)) == string.Join(",", again.Promotions.Select(item => item.Path)),
            "Promotion selection must be deterministic regardless of root order.");
        bool rejected = false;
        try { ResourceMigrationStage3.PackingPlanner.Create(new[] { roots[0], roots[0] }, direct, eligible, required); }
        catch (System.InvalidOperationException) { rejected = true; }
        Check(rejected, "Multiple explicit owners for one source asset must be rejected.");
    }
}
'@
Add-Type -TypeDefinition ("using System.Linq;`nusing ResourceMigrationStage1;`n" + $ownership + "`n" + $planner + "`n" + $checks)
[RemoteArtPackingChecks]::Run()
$native = Get-Content -LiteralPath (Join-Path $root 'Assets/Editor/RemoteArtMigrationStage3.cs') -Raw
if ($native -match 'AssetDatabase\.(MoveAsset|DeleteAsset|CreateAsset|SaveAssets)\s*\(' -or
    $native -match 'BuildPlayerContent\s*\(|BuildPipeline\.BuildPlayer\s*\(|SaveScene\s*\(') {
    throw 'Stage three must only save specifically changed Addressables assets; source art, scenes and builds stay outside this phase.'
}
Write-Output 'Shared-prefab ownership, independent child references, required/deferred content, protected assets, cycles, deterministic planning and phase-boundary tests passed.'
