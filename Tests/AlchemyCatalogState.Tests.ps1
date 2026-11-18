$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$paths = @(
    'Assets/Scripts/Alchemy/Protocol/AlchemyMessages.cs',
    'Assets/Scripts/Alchemy/Protocol/AlchemyCatalogState.cs'
)
$sources = foreach ($path in $paths) {
    (Get-Content (Join-Path $root $path) -Raw) -replace
        '(?m)^using [^\r\n]+;\r?\n', ''
}
$body = $sources -join "`n"
$stubs = @'
using System;
using System.Collections.Generic;
using Game.Alchemy.Protocol;
using Game.Cultivation.Protocol;
using Game.Inventory.Protocol.V2;
using UnityEngine;
public class NetMessageBase { public string msgId; public string type; }
namespace Game.Inventory.Protocol.V2 {
    public class InventoryV2StackChangeDto {
        public long stackId;
        public string itemId;
        public int previousCount;
        public int newCount;
        public bool isBound;
    }
}
namespace Game.Cultivation.Protocol {
    public static class RefiningSnapshotWireState {
        public static bool TryRead(string json, string task, out bool a,
            out bool b, out bool c, out bool d) {
            a = b = c = d = false;
            return true;
        }
        public static bool TryReadTaskBoolean(string json, string task,
            string field, out bool present, out bool? value) {
            present = false;
            value = null;
            return true;
        }
    }
}
namespace UnityEngine {
    public static class JsonUtility {
        public static string ToJson(object value) =>
            System.Text.Json.JsonSerializer.Serialize(value,
                new System.Text.Json.JsonSerializerOptions { IncludeFields = true });
    }
}
'@
Add-Type -TypeDefinition ($stubs + "`n" + $body) -IgnoreWarnings | Out-Null

function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
}
function Recipe([string]$id) {
    $recipe = [Game.Alchemy.Protocol.AlchemyRecipeDto]::new()
    $recipe.recipeId = $id
    $recipe.name = $id
    $recipe.durationSeconds = 60
    $recipe.successRateBps = 8000
    $recipe.materials = [Game.Alchemy.Protocol.AlchemyRecipeMaterialDto[]]@()
    $recipe.outputs = [Game.Alchemy.Protocol.AlchemyRecipeOutputDto[]]@()
    return $recipe
}
function Reply($sequence, $revision, $sync, $index, $count, $total,
    [Game.Alchemy.Protocol.AlchemyRecipeDto[]]$recipes) {
    $reply = [Game.Alchemy.Protocol.AlchemyResponseDto]::new()
    $reply.requestSequence = $sequence
    $reply.success = $true
    $reply.configurationRevision = $revision
    $reply.catalogSyncId = $sync
    $reply.batchIndex = $index
    $reply.batchCount = $count
    $reply.totalCount = $total
    $reply.recipes = $recipes
    $reply.serverTime = 1800000000000
    return $reply
}
$applied = [Game.Alchemy.Protocol.AlchemyCatalogApplyResult]::Applied
$pending = [Game.Alchemy.Protocol.AlchemyCatalogApplyResult]::Pending
$invalid = [Game.Alchemy.Protocol.AlchemyCatalogApplyResult]::Invalid
$stale = [Game.Alchemy.Protocol.AlchemyCatalogApplyResult]::Stale
$state = [Game.Alchemy.Protocol.AlchemyCatalogState]::new()
$sync = [Guid]::NewGuid().ToString('D')
$first = Reply 1 3 $sync 1 2 2 @((Recipe 'first'))
$second = Reply 1 3 $sync 2 2 2 @((Recipe 'second'))
$state.BeginQuery(1)
Assert-True ($state.Apply($second) -eq $pending -and
    -not $state.IsCurrent -and $null -eq $state.Recipes) `
    'A late-indexed batch cannot publish a partial directory.'
Assert-True ($state.Apply($second) -eq $pending -and
    $state.PendingSequence -eq 1) 'Identical batches count only once.'
Assert-True ($state.Apply($first) -eq $applied -and
    $state.IsCurrent -and $state.Recipes.Count -eq 2 -and
    $state.Recipes[0].recipeId -eq 'first' -and
    $state.Recipes[1].recipeId -eq 'second') `
    'All batches must publish in index order as a single directory.'

$state.BeginQuery(2)
$sync = [Guid]::NewGuid().ToString('D')
$part = Reply 2 4 $sync 1 2 2 @((Recipe 'new'))
$conflict = Reply 2 4 $sync 1 2 2 @((Recipe 'different'))
Assert-True ($state.Apply($part) -eq $pending -and
    $state.Recipes.Count -eq 2 -and -not $state.IsCurrent) `
    'A new query keeps the last complete directory without marking it current.'
Assert-True ($state.Apply($conflict) -eq $invalid -and
    $state.PendingSequence -eq 0 -and $state.Recipes.Count -eq 2) `
    'A conflicting duplicate must discard only the pending query.'

$state.BeginQuery(3)
$sync = [Guid]::NewGuid().ToString('D')
$empty = Reply 3 5 $sync 1 1 0 @()
Assert-True ($state.Apply($empty) -eq $applied -and
    $state.IsCurrent -and $state.Recipes.Count -eq 0 -and
    $state.ConfigurationRevision -eq 5) `
    'One valid empty batch must clear the old complete directory.'

$state.BeginQuery(4)
$legacy = Reply 4 6 '' 0 0 0 @((Recipe 'legacy'))
Assert-True ($state.Apply($legacy) -eq $applied -and
    $state.Recipes.Count -eq 1 -and
    $state.Recipes[0].recipeId -eq 'legacy') `
    'A successful old-server single package remains supported.'

$state.BeginQuery(5)
$sync = [Guid]::NewGuid().ToString('D')
$invalidEntry = Reply 5 7 $sync 1 1 1 @($null)
Assert-True ($state.Apply($invalidEntry) -eq $invalid -and
    $state.Recipes[0].recipeId -eq 'legacy') `
    'A null recipe must reject the batch without throwing or replacing the directory.'

$state.BeginQuery(6)
$sync = [Guid]::NewGuid().ToString('D')
$duplicateIds = Reply 6 7 $sync 1 1 2 @((Recipe 'dup'), (Recipe 'dup'))
Assert-True ($state.Apply($duplicateIds) -eq $invalid -and
    $state.Recipes[0].recipeId -eq 'legacy') `
    'Duplicate recipe IDs cannot be published.'

$state.BeginQuery(7)
$sync = [Guid]::NewGuid().ToString('D')
$old = Reply 7 7 $sync 1 1 1 @((Recipe 'old'))
Assert-True ($state.NoteConfigurationRevision(8) -and
    $state.Apply($old) -eq $stale -and
    $state.Recipes[0].recipeId -eq 'legacy' -and
    -not $state.IsCurrent) `
    'An updated configuration hint invalidates older in-flight batches.'
$state.BeginQuery(8)
$wrongSync = Reply 8 8 ([Guid]::NewGuid().ToString('D')) 2 2 2 @((Recipe 'b'))
$state.Apply((Reply 8 8 $sync 1 2 2 @((Recipe 'a')))) | Out-Null
Assert-True ($state.Apply($wrongSync) -eq $invalid -and
    $state.Recipes[0].recipeId -eq 'legacy') `
    'Batches from different sync IDs must never merge.'
$state.BeginQuery(9)
$state.Apply((Reply 9 8 $sync 1 2 2 @((Recipe 'a')))) | Out-Null
$state.Invalidate()
Assert-True ($state.PendingSequence -eq 0 -and
    $state.Recipes[0].recipeId -eq 'legacy' -and -not $state.IsCurrent) `
    'A missing batch timeout discards the buffer but preserves the last directory.'

Write-Output 'PASS: alchemy batch assembly, atomic replacement and stale configuration.'
