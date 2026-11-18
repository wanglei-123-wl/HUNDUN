$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'AlchemySessionRuntime.Tests.ps1') | Out-Null

function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
}
function Learn-Result($request, [string]$status, [long]$revision,
    [string]$snapshot, [string]$changes, [string]$extra) {
    return '{"msgId":"alchemy_learn_result","type":"alchemy_learn_result",' +
        '"alchemyOperationSessionId":"' + $request.alchemyOperationSessionId +
        '","requestSequence":' + $request.requestSequence +
        ',"clientRequestId":"' + $request.clientRequestId +
        '","success":' + $status + ',"configurationRevision":12,' +
        '"serverTime":1800000001000,"inventoryRevision":' + $revision +
        ',"stackItemChanges":' + $changes + ',"snapshot":' + $snapshot +
        $extra + '}'
}

$owner = [GameSession]::new()
$owner.AlchemyOperationSessionId = [Guid]::NewGuid().ToString('D')
$network = [NetworkManager]::new()
$inventory = [Game.Inventory.InventoryModuleRuntime]::new()
$inventory.CurrentSnapshot =
    [Game.Inventory.Model.V2.InventoryOwnershipSnapshot]::new(
        42, 20, [Game.Inventory.Model.V2.InventoryStackState]::new(
            5001, 'recipe_book', 2))
[UnityEngine.MockObjects]::Inventory = $inventory
$items = [Game.Cultivation.Runtime.CultivationSessionRuntime]::new()
[Game.Cultivation.Runtime.CultivationSessionRuntime]::Instance = $items
$runtime = [Game.Alchemy.Runtime.AlchemySessionRuntime]::Ensure($network, $owner)
$runtime.BeginSession($owner, 42, $owner.AlchemyOperationSessionId)
$dispatcher = [NetMessageDispatcher]::Instance

Assert-True (-not $runtime.CanLearn(5001, 'recipe_book',
    $inventory.CurrentSnapshot)) 'Learning requires a current catalog and snapshot.'
$runtime.RequestCatalog() | Out-Null
$catalogSequence = ($network.Sent[-1] | ConvertFrom-Json).requestSequence
$recipe = '{"recipeId":"r1","name":"test","durationSeconds":60,' +
    '"successRateBps":8000,"materials":[],"outputs":[]}'
$dispatcher.Dispatch('alchemy_catalog_result',
    (Catalog-Json $catalogSequence 12 ([Guid]::NewGuid().ToString('D')) $recipe))
$runtime.RequestSnapshot() | Out-Null
$snapshotSequence = ($network.Sent[-1] | ConvertFrom-Json).requestSequence
$dispatcher.Dispatch('alchemy_snapshot_result',
    (Result-Json $snapshotSequence 42 1 'null' 1800000000000))
Assert-True ($runtime.CanLearn(5001, 'recipe_book',
    $inventory.CurrentSnapshot) -and
    -not $runtime.CanLearn(0, 'recipe_book', $inventory.CurrentSnapshot) -and
    -not $runtime.CanLearn(5001, 'not_a_book',
        $inventory.CurrentSnapshot)) `
    'Only an owned active recipebook with a catalog-matched recipe may learn.'

$notices = 0
$runtime.add_SnapshotChanged(
    [Action[Game.Alchemy.Protocol.AlchemySnapshotDto]]{
        param($snapshot) $script:notices++
    })
Assert-True ($runtime.TryLearn(5001, 'recipe_book',
    $inventory.CurrentSnapshot) -and $runtime.HasUnresolvedLearn -and
    -not $runtime.TryLearn(5001, 'recipe_book', $inventory.CurrentSnapshot) -and
    $inventory.CurrentSnapshot.Stacks[0].Count -eq 2) `
    'One learn may be pending; submitting does not consume a book locally.'
$sent = $network.Sent[-1] | ConvertFrom-Json
Assert-True ($sent.msgId -eq 'alchemy_learn' -and
    $sent.type -eq 'alchemy_learn' -and
    $sent.stackId -eq 5001 -and $sent.recipeId -eq 'r1' -and
    $sent.configurationRevision -eq 12 -and
    $sent.expectedInventoryRevision -eq 20 -and
    $sent.alchemyOperationSessionId -eq $owner.AlchemyOperationSessionId -and
    [Guid]::TryParse($sent.clientRequestId, [ref]([Guid]::Empty))) `
    'Learn sends its independent operation identity and the exact book stack.'
$success = Learn-Result $sent 'true' 21 `
    '{"characterDbId":42,"revision":2,"learnedRecipeIds":["r1"],"currentTask":null}' `
    '[{"stackId":5001,"itemId":"recipe_book","newCount":1}]' `
    ',"idempotentReplay":false'
$dispatcher.Dispatch('alchemy_learn_result', $success.Replace(
    '"requestSequence":' + $sent.requestSequence,
    '"requestSequence":' + ($sent.requestSequence + 1)))
Assert-True ($runtime.HasUnresolvedLearn -and
    $inventory.CurrentSnapshot.Stacks[0].Count -eq 2) `
    'An unrelated result must not consume a book or resolve learning.'
$dispatcher.Dispatch('alchemy_learn_result', $success)
Assert-True (-not $runtime.HasUnresolvedLearn -and
    $runtime.Snapshot.learnedRecipeIds[0] -eq 'r1' -and
    $inventory.CurrentSnapshot.Stacks[0].Count -eq 1 -and
    $inventory.CurrentSnapshot.InventoryRevision -eq 21 -and
    $inventory.DeltaCalls -eq 1 -and $notices -eq 1 -and
    -not $runtime.CanLearn(5001, 'recipe_book', $inventory.CurrentSnapshot)) `
    'The first successful result publishes learned recipes and absolute inventory counts.'
$dispatcher.Dispatch('alchemy_learn_result', $success)
Assert-True ($inventory.DeltaCalls -eq 1 -and
    $inventory.CurrentSnapshot.Stacks[0].Count -eq 1) `
    'A duplicate success must not consume another book.'

$owner.AlchemyOperationSessionId = [Guid]::NewGuid().ToString('D')
$runtime.BeginSession($owner, 42, $owner.AlchemyOperationSessionId)
$inventory.CurrentSnapshot =
    [Game.Inventory.Model.V2.InventoryOwnershipSnapshot]::new(
        42, 30, [Game.Inventory.Model.V2.InventoryStackState]::new(
            5001, 'recipe_book', 1))
$runtime.RequestCatalog() | Out-Null
$sequence = ($network.Sent[-1] | ConvertFrom-Json).requestSequence
$dispatcher.Dispatch('alchemy_catalog_result',
    (Catalog-Json $sequence 12 ([Guid]::NewGuid().ToString('D')) $recipe))
$runtime.RequestSnapshot() | Out-Null
$sequence = ($network.Sent[-1] | ConvertFrom-Json).requestSequence
$dispatcher.Dispatch('alchemy_snapshot_result',
    (Result-Json $sequence 42 3 'null' 1800000000000))
$items.HasRecipeBook = $false
Assert-True (-not $runtime.CanLearn(5001, 'recipe_book',
    $inventory.CurrentSnapshot) -and
    $inventory.CurrentSnapshot.Stacks[0].Count -eq 1) `
    'A book removed from the effective item catalog cannot start a new learn.'
$items.HasRecipeBook = $true
Assert-True ($runtime.TryLearn(5001, 'recipe_book',
    $inventory.CurrentSnapshot)) 'A new server snapshot permits a new request.'
$failed = $network.Sent[-1] | ConvertFrom-Json
$dispatcher.Dispatch('alchemy_learn_result',
    (Learn-Result $failed 'false' 0 'null' '[]' `
        ',"errorCode":"alchemy_realm_required"'))
Assert-True (-not $runtime.HasUnresolvedLearn -and
    $inventory.CurrentSnapshot.Stacks[0].Count -eq 1 -and
    $inventory.DeltaCalls -eq 1 -and
    $runtime.Snapshot.learnedRecipeIds.Count -eq 0) `
    'A definite learning failure neither deducts a book nor invents knowledge.'
Assert-True ($runtime.TryLearn(5001, 'recipe_book',
    $inventory.CurrentSnapshot)) 'After a definite failure, a new operation may start.'
$pending = $network.Sent[-1] | ConvertFrom-Json
$network.Disconnect()
$newNetwork = [NetworkManager]::new()
$owner.AlchemyOperationSessionId = [Guid]::NewGuid().ToString('D')
$runtime = [Game.Alchemy.Runtime.AlchemySessionRuntime]::Ensure(
    $newNetwork, $owner)
$runtime.BeginSession($owner, 42, $owner.AlchemyOperationSessionId)
$attemptField = [Game.Alchemy.Runtime.AlchemySessionRuntime].GetField(
    'lastLearnAttemptAt', [Reflection.BindingFlags]'Instance, NonPublic')
$attemptField.SetValue($runtime, 0L)
$update = [Game.Alchemy.Runtime.AlchemySessionRuntime].GetMethod(
    'Update', [Reflection.BindingFlags]'Instance, NonPublic')
$update.Invoke($runtime, @()) | Out-Null
$retry = @($newNetwork.Sent | ForEach-Object { $_ | ConvertFrom-Json } |
    Where-Object msgId -eq 'alchemy_learn')[0]
Assert-True ($retry.clientRequestId -eq $pending.clientRequestId -and
    $retry.requestSequence -ne $pending.requestSequence -and
    $retry.alchemyOperationSessionId -eq $owner.AlchemyOperationSessionId -and
    $retry.recipeId -eq 'r1' -and $retry.stackId -eq 5001 -and
    $retry.expectedInventoryRevision -eq 30) `
    'Re-entry keeps the original UUID and business parameters with a new session sequence.'
$beforeReplay = $inventory.DeltaCalls
$dispatcher.Dispatch('alchemy_learn_result',
    (Learn-Result $retry 'true' 31 'null' '[]' ',"idempotentReplay":true'))
Assert-True (-not $runtime.HasUnresolvedLearn -and
    $inventory.DeltaCalls -eq $beforeReplay -and
    $inventory.Repairs -gt 0 -and
    @($newNetwork.Sent | ForEach-Object { $_ | ConvertFrom-Json } |
        Where-Object msgId -eq 'alchemy_snapshot').Count -gt 0) `
    'Replayed success queries both snapshots without applying a second deduction.'

$autoOwner = [GameSession]::new()
$autoOwner.AlchemyOperationSessionId = [Guid]::NewGuid().ToString('D')
$autoNetwork = [NetworkManager]::new()
$autoRuntime = [Game.Alchemy.Runtime.AlchemySessionRuntime]::Ensure(
    $autoNetwork, $autoOwner)
$autoRuntime.BeginSession($autoOwner, 42, $autoOwner.AlchemyOperationSessionId)
$update.Invoke($autoRuntime, @()) | Out-Null
Assert-True ($autoNetwork.Sent.Count -eq 2 -and
    ($autoNetwork.Sent[0] | ConvertFrom-Json).msgId -eq 'alchemy_catalog' -and
    ($autoNetwork.Sent[1] | ConvertFrom-Json).msgId -eq 'alchemy_snapshot') `
    'Entering the world fetches learning prerequisites before the panel is opened.'
$update.Invoke($autoRuntime, @()) | Out-Null
Assert-True ($autoNetwork.Sent.Count -eq 2) `
    'Initial learning queries must not repeat each frame.'
$autoRuntime.EnsureLearningData()
Assert-True ($autoNetwork.Sent.Count -eq 2) `
    'Repeated bag double-clicks must not replace in-flight learning queries.'

$partialOwner = [GameSession]::new()
$partialOwner.AlchemyOperationSessionId = [Guid]::NewGuid().ToString('D')
$partialNetwork = [NetworkManager]::new()
$partialRuntime = [Game.Alchemy.Runtime.AlchemySessionRuntime]::Ensure(
    $partialNetwork, $partialOwner)
$partialRuntime.BeginSession($partialOwner, 42,
    $partialOwner.AlchemyOperationSessionId)
$partialRuntime.RequestSnapshot() | Out-Null
$seq = ($partialNetwork.Sent[-1] | ConvertFrom-Json).requestSequence
$dispatcher.Dispatch('alchemy_snapshot_result',
    (Result-Json $seq 42 4 'null' 1800000000000))
$update.Invoke($partialRuntime, @()) | Out-Null
Assert-True ($partialNetwork.Sent.Count -eq 2 -and
    ($partialNetwork.Sent[-1] | ConvertFrom-Json).msgId -eq 'alchemy_catalog') `
    'A snapshot arriving before the automatic catalog query must not suppress it.'

$panel = Get-Content (Join-Path $root `
    'Assets/Scripts/Alchemy/Presentation/AlchemyPanelView.cs') -Raw
$render = [regex]::Match($panel,
    '(?s)private void Render\(\).*?(?=private void RenderTask)').Value
Assert-True ($render.Contains('recipe != null && IsLearned(recipe.recipeId)') -and
    $render.Contains('if (recipe == null || !IsLearned(recipe.recipeId))') -and
    -not $render.Contains('runtime.Catalog[0]') -and
    $render.Contains('selectedRecipeId = current?.recipeId;')) `
    'The recipe list and selected details must come only from learned IDs.'
Write-Output 'PASS: recipe learning identity, inventory, replay and learned-list filter.'
