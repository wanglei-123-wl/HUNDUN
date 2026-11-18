$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '锻造协议阶段1.Tests.ps1')

function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
}

function Packet([long]$sequence, [long]$revision, [string]$sync,
    [int]$index, [int]$count, [int]$total, [string]$recipes) {
    $json = '{"msgId":"forging_catalog_result","type":"forging_catalog_result",' +
        '"forgingOperationSessionId":"' + $sessionB + '",' +
        '"requestSequence":' + $sequence + ',"success":true,' +
        '"serverTime":1793757600000,"configurationRevision":' + $revision +
        ',"catalogSyncId":"' + $sync + '","batchIndex":' + $index +
        ',"batchCount":' + $count + ',"totalCount":' + $total +
        ',"recipes":' + $recipes + '}'
    return [Newtonsoft.Json.JsonConvert]::DeserializeObject[
        Game.Forging.Protocol.锻造响应]($json)
}

$state = [Game.Forging.Protocol.锻造目录状态]::new()
$recipeA = '[{"recipeId":"YF001_item","name":"皮甲锻造","durationSeconds":60,' +
    '"successRateBps":8000,"materials":[{"itemId":"iron_ore","count":5}]}]'
$recipeB = '[{"recipeId":"YF002_item","name":"玄铁剑锻造","durationSeconds":60,' +
    '"successRateBps":8000,"materials":[{"itemId":"iron_ore","count":8}]}]'
$sync = '66666666-6666-4666-8666-666666666666'
$state.BeginQuery(1)
Assert-True ($state.Apply((Packet 1 12 $sync 2 2 2 $recipeB)).ToString() -eq '等待') `
    'Out-of-order batch must wait for all parts.'
Assert-True ($state.Apply((Packet 1 12 $sync 2 2 2 $recipeB)).ToString() -eq '等待') `
    'Duplicate batch must not append.'
Assert-True ($null -eq $state.Recipes) 'Partial catalog must not show.'
Assert-True ($state.Apply((Packet 1 12 $sync 1 2 2 $recipeA)).ToString() -eq '完成' -and
    $state.Recipes.Length -eq 2 -and
    $state.Recipes[0].recipeId -ceq 'YF001_item') `
    'Complete catalog must commit in batch order.'

$state.BeginQuery(2)
Assert-True ($state.Apply((Packet 1 13 $sync 1 1 1 $recipeB)).ToString() -eq '忽略' -and
    $state.Recipes.Length -eq 2) 'Old sequence cannot replace the catalog.'
$state.Apply((Packet 2 13 $sync 1 2 2 $recipeA)) | Out-Null
Assert-True ($state.Apply((Packet 2 13 '77777777-7777-4777-8777-777777777777' 2 2 2 $recipeB)).ToString() -eq '无效' -and
    $state.Recipes.Length -eq 2) 'Mixed sync IDs must not replace catalog.'
$state.BeginQuery(3)
Assert-True ($state.Apply((Packet 3 13 $sync 1 2 2 $recipeA)).ToString() -eq '等待' -and
    $state.Apply((Packet 3 13 $sync 2 2 2 $recipeA)).ToString() -eq '无效' -and
    $state.Recipes.Length -eq 2) 'Duplicate recipe IDs must reject the whole batch.'
$state.BeginQuery(4)
Assert-True ($state.NoteRevision(14) -and
    $state.Apply((Packet 4 13 $sync 1 1 1 $recipeB)).ToString() -eq '过期') `
    'Higher configuration notification must invalidate older results.'
$state.BeginQuery(5)
Assert-True ($state.Apply((Packet 5 14 $sync 1 1 0 '[]')).ToString() -eq '完成' -and
    $state.IsCurrent -and $state.Recipes.Length -eq 0) `
    'An empty terminal batch must clear the old complete catalog.'

$tasks = [Game.Forging.Protocol.锻造任务状态]::new()
$active = '{"snapshot":{"characterDbId":43,"revision":1,"currentTask":' +
    '{"taskId":"33333333-3333-4333-8333-333333333333",' +
    '"recipeId":"YF001_item","status":"active","succeeded":null,' +
    '"startedAtUnixMilliseconds":1793757600000,' +
    '"endsAtUnixMilliseconds":1793757660000}}}'
$ready = $active.Replace('"revision":1', '"revision":2').
    Replace('"status":"active","succeeded":null',
        '"status":"ready","succeeded":false')
$taskA = [Newtonsoft.Json.JsonConvert]::DeserializeObject[
    Game.Forging.Protocol.锻造响应]($active)
$taskB = [Newtonsoft.Json.JsonConvert]::DeserializeObject[
    Game.Forging.Protocol.锻造响应]($ready)
Assert-True ($tasks.Apply($active, $taskA.snapshot, 43, 1793757600000).ToString() -eq '已更新') `
    'Active must retain an unknown outcome.'
Assert-True ($tasks.Apply($ready, $taskB.snapshot, 43, 1793757660000).ToString() -eq '已更新' -and
    $tasks.Snapshot.currentTask.succeeded -eq $false) `
    'Ready failure must remain distinct from null.'
Assert-True ($tasks.Apply($active, $taskA.snapshot, 43, 1793757600000).ToString() -eq '过期' -and
    $tasks.Apply($ready, $taskB.snapshot, 44, 1793757660000).ToString() -eq '无效') `
    'Old revision and wrong character must be ignored.'
Assert-True ($tasks.Apply('{"snapshot":null}', $null, 43, 1793757660000).ToString() -eq '无快照' -and
    $tasks.Snapshot.currentTask.status -eq 'ready') `
    'A null snapshot must not erase the task.'
$empty = '{"snapshot":{"characterDbId":43,"revision":3,"currentTask":null}}'
$emptyResponse = [Newtonsoft.Json.JsonConvert]::DeserializeObject[
    Game.Forging.Protocol.锻造响应]($empty)
Assert-True ($tasks.Apply($empty, $emptyResponse.snapshot, 43, 1793757661000).ToString() -eq '已更新' -and
    $null -eq $tasks.Snapshot.currentTask) `
    'A newer explicit empty task must clear the task.'

$network.Sent.Clear()
$runtime.BeginSession($owner, 43, 1, $sessionB)
$update = $runtime.GetType().GetMethod('Update',
    [System.Reflection.BindingFlags]'NonPublic,Instance')
$update.Invoke($runtime, @()) | Out-Null
Assert-True ($network.Sent.Count -eq 2 -and
    $network.Sent[0].msgId -eq 'forging_catalog' -and
    $network.Sent[1].msgId -eq 'forging_snapshot') `
    'Entering WorldShell must actively request catalog and task.'
$dispatcher.Dispatch('forging_catalog_result',
    [Newtonsoft.Json.JsonConvert]::SerializeObject(
        (Packet $network.Sent[0].requestSequence 12 $sync 1 1 1 $recipeA)))
Assert-True ($runtime.IsCatalogCurrent -and
    $runtime.Catalog[0].recipeId -ceq 'YF001_item') `
    'A complete response must reach the runtime.'
$snapshotJson = '{"msgId":"forging_snapshot_result","type":"forging_snapshot_result",' +
    '"forgingOperationSessionId":"' + $sessionB + '","requestSequence":' +
    $network.Sent[1].requestSequence + ',"success":true,' +
    '"serverTime":1793757600000,' +
    '"snapshot":{"characterDbId":43,"revision":1,"currentTask":null}}'
$dispatcher.Dispatch('forging_snapshot_result', $snapshotJson)
Assert-True ($runtime.IsSnapshotCurrent -and $runtime.Snapshot.revision -eq 1) `
    'Snapshot query must hydrate the current task state.'
$changed = '{"msgId":"forging_changed","type":"forging_changed",' +
    '"forgingOperationSessionId":"' + $sessionB + '","requestSequence":0,' +
    '"clientRequestId":"","configurationRevision":13,"success":true,' +
    '"serverTime":1793757601000,' +
    '"snapshot":{"characterDbId":43,"revision":1,"currentTask":null}}'
$dispatcher.Dispatch('forging_changed', $changed)
Assert-True (-not $runtime.IsCatalogCurrent -and
    $runtime.Snapshot.revision -eq 1) `
    'A same-task-revision notice must still invalidate a newer catalog.'
$update.Invoke($runtime, @()) | Out-Null
Assert-True ($network.Sent.Count -eq 3 -and
    $network.Sent[2].msgId -eq 'forging_catalog') `
    'Higher configuration revision must trigger a new full catalog request.'
$oldSessionChanged = $changed.Replace($sessionB, $sessionA).
    Replace('"configurationRevision":13', '"configurationRevision":14')
$dispatcher.Dispatch('forging_changed', $oldSessionChanged)
Assert-True ($network.Sent.Count -eq 3) `
    'An old session notice must not schedule another catalog refresh.'
$owner.ForgingOperationSessionId = $sessionA
Assert-True (-not $runtime.IsSessionActive) 'Session mismatch must revoke reads.'

$panel = Get-Content (Join-Path $root 'Assets/Scripts/锻造/锻造面板视图.cs') -Raw
Assert-True ($panel.Contains('runtime.CatalogChanged += HandleCatalogChanged') -and
    $panel.Contains('runtime.SnapshotChanged += HandleSnapshotChanged') -and
    $panel.Contains('锻造图谱列表.Build(shownCatalog,') -and
    $panel.Contains('shownLearnedRecipeIds != runtime.LearnedRecipeIds') -and
    $panel.Contains('FindIcon(current.outputItemId)') -and
    $panel.Contains('current.succeeded == true') -and
    $panel.Contains('runtime?.EstimatedServerTime')) `
    'The panel must display server recipes and task time without inventing unlocks.'

Write-Output 'PASS: forging catalog batches, snapshot revisions and active queries.'
