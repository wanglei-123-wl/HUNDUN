$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$model = Get-Content -Raw -LiteralPath (Join-Path $root 'Assets/Editor/ResourceMigrationAuditModel.cs')
$checks = @'
public static class ResourceMigrationStage1Checks
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new System.Exception(message);
    }
    public static void Run()
    {
        var facts = new ResourceMigrationStage1.OwnershipFacts { Path = "Assets/Unused.prefab" };
        Check(ResourceMigrationStage1.OwnershipPolicy.Classify(facts) == "pending_review_not_proven_unused",
            "Unknown content must remain pending review, never be classified as deletable.");
        facts.Path = "Assets/Resources/Font.asset";
        facts.BuiltIn = true;
        facts.LocalAddressable = true;
        Check(ResourceMigrationStage1.OwnershipPolicy.Classify(facts) == "review_duplicate_local_packing_paths",
            "A font packed through Resources and local Addressables must be reviewed.");
        facts.RemoteExplicit = true;
        Check(ResourceMigrationStage1.OwnershipPolicy.Classify(facts) == "review_remote_root_forced_into_player",
            "A remote root directly forced into the player must be identified as a conflict.");
        facts = new ResourceMigrationStage1.OwnershipFacts { Path = "Assets/Boot.mp4", Startup = true };
        Check(ResourceMigrationStage1.OwnershipPolicy.Classify(facts) == "keep_startup_local", "Startup media must remain local.");
        facts = new ResourceMigrationStage1.OwnershipFacts { Path = "Assets/Resources/NetworkEndpointSettings.asset", BuiltIn = true };
        Check(ResourceMigrationStage1.OwnershipPolicy.Classify(facts) == "keep_infrastructure_local", "Networking configuration must remain local.");
        facts = new ResourceMigrationStage1.OwnershipFacts { Path = "Assets/Npc.prefab", RemoteBundleOwners = 2 };
        Check(ResourceMigrationStage1.OwnershipPolicy.Classify(facts) == "review_remote_shared_dependency", "Cross-bundle NPC dependencies need a shared ownership review.");
        facts.Path = "Assets/Editor/Example.cs";
        Check(ResourceMigrationStage1.OwnershipPolicy.Classify(facts) == "editor_only", "Editor code is not a remote art asset.");

        var direct = new System.Collections.Generic.Dictionary<string, string[]>
        {
            ["scene"] = new[] { "font", "npc" },
            ["font"] = new[] { "font-texture" },
            ["npc"] = new[] { "scene", "material" },
            ["material"] = new[] { "texture" }
        };
        var explicitOwners = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.HashSet<string>>
        {
            ["scene"] = new System.Collections.Generic.HashSet<string> { "map" },
            ["font"] = new System.Collections.Generic.HashSet<string> { "bootstrap" }
        };
        var boundaries = new System.Collections.Generic.HashSet<string>();
        var result = ResourceMigrationStage1.OwnershipPolicy.OwnedDependencies("scene", "map", direct, explicitOwners, boundaries);
        Check(result.SetEquals(new[] { "scene", "npc", "material", "texture" }) && boundaries.SetEquals(new[] { "font" }),
            "Explicit cross-bundle boundaries must stop traversal, while cycles must terminate.");
        explicitOwners["npc"] = new System.Collections.Generic.HashSet<string> { "map" };
        result = ResourceMigrationStage1.OwnershipPolicy.OwnedDependencies("scene", "map", direct, explicitOwners, new System.Collections.Generic.HashSet<string>());
        Check(result.Contains("material"), "Explicit roots in the same packing bucket must retain shared dependencies.");
        explicitOwners["npc"] = new System.Collections.Generic.HashSet<string> { "characters" };
        result = ResourceMigrationStage1.OwnershipPolicy.OwnedDependencies("scene", "map", direct, explicitOwners, new System.Collections.Generic.HashSet<string>());
        Check(!result.Contains("npc") && !result.Contains("material"),
            "A separately owned character and its children must not be falsely assigned to the map bundle.");
    }
}
'@
Add-Type -TypeDefinition ($model + "`n" + $checks)
[ResourceMigrationStage1Checks]::Run()

$audit = Get-Content -Raw -LiteralPath (Join-Path $root 'Assets/Editor/ResourceMigrationStage1Audit.cs')
if ($audit -match 'AssetDatabase\.(SaveAssets|MoveAsset|DeleteAsset|CreateAsset|Refresh)\s*\(' -or
    $audit -match 'BuildPipeline\.BuildPlayer\s*\(' -or
    $audit -match 'AddressableAssetSettings\.BuildPlayerContent\s*\(' -or
    $audit -match 'EditorSceneManager\.SaveScene\s*\(' -or
    $audit -match '\.CreateOrMoveEntry\s*\(') {
    throw 'The stage-one audit must not perform asset mutations, saves, builds, or migrations.'
}
Write-Output 'Stage-one ownership, explicit dependency boundaries, and read-only guards passed.'
