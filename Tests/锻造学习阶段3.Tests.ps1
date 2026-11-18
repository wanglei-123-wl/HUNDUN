$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '锻造学习阶段2.Tests.ps1')

function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
}

$inventory.CurrentSnapshot = [ForgingFixture]::Bag(
    43, 10, 415, 'tz001', 1, 0)
$network.Sent.Clear()
$runtime.BeginSession($owner, 43, 1, $sessionA)
Assert-True ($runtime.TryLearn(415, $inventory.CurrentSnapshot)) `
    'A current owned blueprint must be eligible for learning.'
$request = $network.Sent[0]
$result = '{"msgId":"forging_learn_result","type":"forging_learn_result",' +
    '"forgingOperationSessionId":"' + $sessionA +
    '","requestSequence":' + $request.requestSequence +
    ',"clientRequestId":"' + $request.clientRequestId +
    '","success":true,"inventoryRevision":11,' +
    '"stackItemChanges":[{"stackId":415,"itemId":"",' +
    '"previousCount":0,"newCount":0,"isBound":false}],' +
    '"snapshot":{"learnedRecipeIds":["WQ002"]}}'
$dispatcher.Dispatch('forging_learn_result', $result)
Assert-True ($runtime.LearnedRecipeIds.Count -eq 1 -and
    $runtime.LearnedRecipeIds[0] -eq 'WQ002' -and
    $runtime.IsRecipeLearned('WQ002') -and
    -not $runtime.IsRecipeLearned('unknown') -and
    $inventory.AppliedLearns -eq 1 -and
    $inventory.CurrentSnapshot.InventoryRevision -eq 11 -and
    $inventory.CurrentSnapshot.Stacks.Count -eq 0) `
    'A first success must unlock only the server recipe and remove the last sheet.'
$dispatcher.Dispatch('forging_learn_result', $result)
Assert-True ($inventory.AppliedLearns -eq 1) `
    'A duplicated success must not apply the inventory mutation again.'
$update.Invoke($runtime, @()) | Out-Null
$snapshotRequest = $network.Sent |
    Where-Object msgId -eq 'forging_snapshot' | Select-Object -Last 1
$snapshotResult = '{"msgId":"forging_snapshot_result",' +
    '"type":"forging_snapshot_result",' +
    '"forgingOperationSessionId":"' + $sessionA +
    '","requestSequence":' + $snapshotRequest.requestSequence +
    ',"success":true,"serverTime":1793757600000,' +
    '"snapshot":{"characterDbId":43,"revision":1,' +
    '"learnedRecipeIds":["WQ002"],"currentTask":null}}'
$dispatcher.Dispatch('forging_snapshot_result', $snapshotResult)
Assert-True ($runtime.IsSnapshotCurrent -and
    $runtime.Snapshot.learnedRecipeIds[0] -eq 'WQ002') `
    'The authoritative forging snapshot must preserve learned recipes.'

$inventory.CurrentSnapshot = [ForgingFixture]::Bag(
    43, 20, 415, 'tz001', 2, 0)
$network.Sent.Clear()
$runtime.BeginSession($owner, 43, 1, $sessionA)
Assert-True ($runtime.TryLearn(415, $inventory.CurrentSnapshot)) `
    'A new session must accept a new learning operation.'
$request = $network.Sent[0]
$replay = '{"msgId":"forging_learn_result","type":"forging_learn_result",' +
    '"forgingOperationSessionId":"' + $sessionA +
    '","requestSequence":' + $request.requestSequence +
    ',"clientRequestId":"' + $request.clientRequestId +
    '","success":true,"idempotentReplay":true,' +
    '"inventoryRevision":21,"snapshot":{"learnedRecipeIds":["WQ002"]},' +
    '"stackItemChanges":[{"stackId":415,"itemId":"tz001","newCount":1}]}'
$dispatcher.Dispatch('forging_learn_result', $replay)
Assert-True ($inventory.AppliedLearns -eq 1 -and
    $inventory.CurrentSnapshot.InventoryRevision -eq 20 -and
    $inventory.CurrentSnapshot.Stacks[0].Count -eq 2) `
    'An idempotent replay must refresh rather than apply an old decrement.'
$update.Invoke($runtime, @()) | Out-Null
Assert-True ($inventory.SnapshotRequests -gt 0) `
    'Replayed learning must request the authoritative inventory.'

$inventory.CurrentSnapshot = [ForgingFixture]::Bag(
    43, 30, 415, 'tz001', 2, 0)
$network.Sent.Clear()
$runtime.BeginSession($owner, 43, 1, $sessionA)
$runtime.TryLearn(415, $inventory.CurrentSnapshot) | Out-Null
$request = $network.Sent[0]
$gap = '{"msgId":"forging_learn_result","type":"forging_learn_result",' +
    '"forgingOperationSessionId":"' + $sessionA +
    '","requestSequence":' + $request.requestSequence +
    ',"clientRequestId":"' + $request.clientRequestId +
    '","success":true,"inventoryRevision":33,' +
    '"snapshot":{"learnedRecipeIds":["WQ002"]},' +
    '"stackItemChanges":[{"stackId":415,"itemId":"tz001","newCount":1}]}'
$dispatcher.Dispatch('forging_learn_result', $gap)
Assert-True ($inventory.AppliedLearns -eq 1 -and
    $inventory.CurrentSnapshot.Stacks[0].Count -eq 2) `
    'A revision gap must not apply a local decrement.'

$inventory.CurrentSnapshot = [ForgingFixture]::Bag(
    43, 40, 415, 'tz001', 2, 0)
$network.Sent.Clear()
$runtime.BeginSession($owner, 43, 1, $sessionA)
$runtime.TryLearn(415, $inventory.CurrentSnapshot) | Out-Null
$request = $network.Sent[0]
$needsSnapshot = '{"msgId":"forging_learn_result",' +
    '"type":"forging_learn_result","forgingOperationSessionId":"' +
    $sessionA + '","requestSequence":' + $request.requestSequence +
    ',"clientRequestId":"' + $request.clientRequestId +
    '","success":true,"requiresInventorySnapshot":true,' +
    '"inventoryRevision":41,"snapshot":{"learnedRecipeIds":["WQ002"]},' +
    '"stackItemChanges":[{"stackId":415,"itemId":"tz001","newCount":1}]}'
$dispatcher.Dispatch('forging_learn_result', $needsSnapshot)
Assert-True ($inventory.AppliedLearns -eq 1 -and
    $inventory.CurrentSnapshot.InventoryRevision -eq 40) `
    'A result requiring inventory snapshot must skip the local debit.'
$update.Invoke($runtime, @()) | Out-Null
Assert-True ($inventory.SnapshotRequests -gt 1) `
    'The snapshot-required result must request inventory recovery.'

$inventory.CurrentSnapshot = [ForgingFixture]::Bag(
    43, 50, 415, 'tz001', 1, 0)
$network.Sent.Clear()
$runtime.BeginSession($owner, 43, 1, $sessionA)
$runtime.TryLearn(415, $inventory.CurrentSnapshot) | Out-Null
$request = $network.Sent[0]
$wrongCharacter = '{"msgId":"forging_learn_result",' +
    '"type":"forging_learn_result","forgingOperationSessionId":"' +
    $sessionA + '","requestSequence":' + $request.requestSequence +
    ',"clientRequestId":"' + $request.clientRequestId +
    '","success":true,"inventoryRevision":51,' +
    '"snapshot":{"characterDbId":99,"learnedRecipeIds":["WQ002"]},' +
    '"stackItemChanges":[{"stackId":415,"itemId":"","newCount":0}]}'
$dispatcher.Dispatch('forging_learn_result', $wrongCharacter)
Assert-True ($inventory.AppliedLearns -eq 1 -and
    $inventory.CurrentSnapshot.Stacks.Count -eq 1 -and
    $null -eq $runtime.LearnedRecipeIds) `
    'A mismatched character snapshot must not change unlocks or inventory.'

$network.Sent.Clear()
$runtime.BeginSession($owner, 43, 1, $sessionA)
$runtime.TryLearn(415, $inventory.CurrentSnapshot) | Out-Null
$request = $network.Sent[0]
$alreadyLearned = '{"msgId":"forging_learn_result",' +
    '"type":"forging_learn_result","forgingOperationSessionId":"' +
    $sessionA + '","requestSequence":' + $request.requestSequence +
    ',"clientRequestId":"' + $request.clientRequestId +
    '","success":false,"errorCode":"forging_already_learned"}'
$dispatcher.Dispatch('forging_learn_result', $alreadyLearned)
Assert-True (-not $runtime.HasUnresolvedLearn -and
    $inventory.CurrentSnapshot.Stacks.Count -eq 1 -and
    $inventory.AppliedLearns -eq 1) `
    'Already learned must leave the owned blueprint unchanged.'

$inventory.CurrentSnapshot = [ForgingFixture]::Bag(
    43, 60, 415, 'tz001', 1, 0)
$network.Sent.Clear()
$runtime.BeginSession($owner, 43, 1, $sessionA)
$update.Invoke($runtime, @()) | Out-Null
$oldQuery = $network.Sent |
    Where-Object msgId -eq 'forging_snapshot' | Select-Object -Last 1
$runtime.TryLearn(415, $inventory.CurrentSnapshot) | Out-Null
$request = $network.Sent | Where-Object msgId -eq 'forging_learn' |
    Select-Object -Last 1
$freshLearn = '{"msgId":"forging_learn_result",' +
    '"type":"forging_learn_result","forgingOperationSessionId":"' +
    $sessionA + '","requestSequence":' + $request.requestSequence +
    ',"clientRequestId":"' + $request.clientRequestId +
    '","success":true,"inventoryRevision":61,' +
    '"snapshot":{"learnedRecipeIds":["WQ002"]},' +
    '"stackItemChanges":[{"stackId":415,"itemId":"","newCount":0}]}'
$dispatcher.Dispatch('forging_learn_result', $freshLearn)
$staleQuery = '{"msgId":"forging_snapshot_result",' +
    '"type":"forging_snapshot_result","forgingOperationSessionId":"' +
    $sessionA + '","requestSequence":' + $oldQuery.requestSequence +
    ',"success":true,"serverTime":1793757600000,' +
    '"snapshot":{"characterDbId":43,"revision":0,' +
    '"learnedRecipeIds":[],"currentTask":null}}'
$dispatcher.Dispatch('forging_snapshot_result', $staleQuery)
Assert-True ($runtime.LearnedRecipeIds[0] -eq 'WQ002' -and
    $null -eq $runtime.Snapshot) `
    'A query sent before learning must not roll back the new unlock.'
$update.Invoke($runtime, @()) | Out-Null
$newQuery = $network.Sent |
    Where-Object msgId -eq 'forging_snapshot' | Select-Object -Last 1
Assert-True ($newQuery.requestSequence -ne $oldQuery.requestSequence) `
    'The learn result must issue a fresh forging snapshot query.'

$state = [Game.Forging.Protocol.锻造任务状态]::new()
$valid = '{"snapshot":{"characterDbId":43,"revision":1,' +
    '"learnedRecipeIds":["WQ002"],"currentTask":null}}'
$dto = [Newtonsoft.Json.JsonConvert]::DeserializeObject[
    Game.Forging.Protocol.锻造响应]($valid)
Assert-True ($state.Apply($valid, $dto.snapshot, 43, 1000).ToString() -eq
    '已更新') 'A snapshot must retain its learned recipe list.'
$duplicate = $valid.Replace('"WQ002"', '"WQ002","WQ002"')
$dto = [Newtonsoft.Json.JsonConvert]::DeserializeObject[
    Game.Forging.Protocol.锻造响应]($duplicate)
Assert-True ($state.Apply($duplicate, $dto.snapshot, 43, 1001).ToString() -eq
    '无效' -and $state.Snapshot.learnedRecipeIds.Count -eq 1) `
    'Invalid learned lists must not replace an accepted snapshot.'
$newer = $valid.Replace('"WQ002"', '"WQ002","WQ003"')
$dto = [Newtonsoft.Json.JsonConvert]::DeserializeObject[
    Game.Forging.Protocol.锻造响应]($newer)
Assert-True ($state.Apply($newer, $dto.snapshot, 43, 1002).ToString() -eq
    '已更新' -and $state.Snapshot.learnedRecipeIds.Count -eq 2) `
    'A newer server time can advance learned recipes at the same task revision.'

$panel = Get-Content (Join-Path $root 'Assets/Scripts/锻造/锻造面板视图.cs') -Raw
Assert-True ($panel.Contains('runtime.LearnedRecipesChanged +=') -and
    $panel.Contains('runtime.IsRecipeLearned(recipeId)') -and
    $panel.Contains('FindIcon(current.outputItemId)')) `
    'The forging panel must react to unlock changes and gate unlearned recipes.'

$recipes = [Newtonsoft.Json.JsonConvert]::DeserializeObject[
    Game.Forging.Protocol.锻造配方[]]('[{"recipeId":"WQ002","name":"玄铁剑",' +
    '"materials":[{"itemId":"iron_ore","count":5}]},' +
    '{"recipeId":"WQ003","name":"玄罡剑","materials":[]}]')
$items = [Newtonsoft.Json.JsonConvert]::DeserializeObject[
    Game.Cultivation.Protocol.CultivationItemDefinitionDto[]](
    '[{"itemId":"tz999","kind":"forgingblueprint","learnForgingRecipeId":"WQ002"},' +
    '{"itemId":"tz001","kind":"forgingblueprint","learnForgingRecipeId":"WQ002"},' +
    '{"itemId":"tz003","kind":"forgingblueprint","learnForgingRecipeId":"WQ003"},' +
    '{"itemId":"tz004","kind":"forgingblueprint","learnForgingRecipeId":"WQ004"}]')
$definitions = [Game.Items.ItemDefinitionTable]::new()
$blueprintIcon = [UnityEngine.Sprite]::new()
$equipmentIcon = [UnityEngine.Sprite]::new()
$definitions.Items['tz001'] = [Game.Items.ItemDefinition]@{
    DisplayName = '玄铁剑图纸'; Icon = $blueprintIcon
}
$definitions.Items['tz999'] = [Game.Items.ItemDefinition]@{
    DisplayName = '等价图谱'; Icon = [UnityEngine.Sprite]::new()
}
$definitions.Items['WQ002'] = [Game.Items.ItemDefinition]@{
    DisplayName = '玄铁剑'; Icon = $equipmentIcon
}
$view = [Game.Forging.锻造图谱列表]
Assert-True (@($view::Build($recipes, $null, $true, $items, $definitions)).Count -eq 0 -and
    @($view::Build($recipes, [string[]]@(), $true, $items, $definitions)).Count -eq 0) `
    'An absent or empty V2 learned list must never show the public recipe directory.'
$rows = @($view::Build($recipes, [string[]]@('WQ002', 'WQ004'), $true, $items, $definitions))
Assert-True ($rows.Count -eq 2 -and $rows[0].name -eq '玄铁剑图纸' -and
    $rows[0].recipeId -eq 'WQ002' -and $rows[0].canForge -and
    $rows[0].materials[0].count -eq 5 -and
    [object]::ReferenceEquals($rows[0].icon, $blueprintIcon) -and
    $rows[1].recipeId -eq 'WQ004' -and -not $rows[1].canForge -and
    $rows[1].name.Contains('不可锻造')) `
    'Only learned IDs are rendered, with blueprint art and an unavailable row for missing recipes.'
$fallback = @($view::Build($recipes, [string[]]@('WQ003'), $true, $items, $definitions))
Assert-True ($fallback.Count -eq 1 -and $fallback[0].name -eq '玄罡剑图谱' -and
    $null -eq $fallback[0].icon -and $fallback[0].hasBlueprintAssociation) `
    'Missing blueprint art must use the text fallback.'
$missing = @($view::Build($recipes, [string[]]@('WQ002'), $true,
    [Game.Cultivation.Protocol.CultivationItemDefinitionDto[]]@(), $definitions))
Assert-True ($missing.Count -eq 1 -and $missing[0].canForge -and
    -not $missing[0].hasBlueprintAssociation -and $rows[0].hasBlueprintAssociation) `
    'A newly learned active recipe must report a missing association until the item catalog is refreshed.'
$legacy = @($view::Build($recipes, $null, $false, $items, $definitions))
Assert-True ($legacy.Count -eq 2 -and
    [object]::ReferenceEquals($legacy[0].icon, $equipmentIcon)) `
    'V1 keeps its legacy list and equipment art.'

Write-Output 'PASS: forging learning, blueprint display, learned-only rows and V1 compatibility.'
