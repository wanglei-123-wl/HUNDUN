$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '锻造读取阶段2.Tests.ps1')

function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
}

$bag = [ForgingFixture]::Bag(43, 35, 101, 'iron_ore', 5, 100)
$coin = [Game.Forging.锻造材料需求]::new()
$coin.itemId = 'gold_coin'
$coin.count = 10
$coin.isCurrency = $true
$ore = [Game.Forging.锻造材料需求]::new()
$ore.itemId = 'iron_ore'
$ore.count = 5
$recipe = [Game.Forging.锻造图谱]::new()
$recipe.recipeId = 'YF001_item'
$recipe.materials = @($ore, $coin)
$choice = [Game.Forging.锻造本地选择]::new()
Assert-True ($choice.TrySet($bag, 43, 's:101', 'iron_ore', 5) -and
    $choice.Matches($recipe, $bag)) `
    'Mixed materials must match a stack and a wallet without a currency stack.'
$currencyRecipe = [Game.Forging.锻造图谱]::new()
$currencyRecipe.recipeId = 'YF002_item'
$currencyRecipe.materials = @($coin)
$emptyChoice = [Game.Forging.锻造本地选择]::new()
Assert-True ($emptyChoice.Matches($currencyRecipe, $bag) -and
    -not $choice.Matches($currencyRecipe, $bag)) `
    'A currency-only recipe must send no stack entries.'
$coin.count = 101
Assert-True (-not $choice.Matches($recipe, $bag)) `
    'Insufficient wallet balance must prevent local submission.'
$coin.count = 10

$inventory = [Game.Inventory.InventoryModuleRuntime]::new()
$inventory.CurrentSnapshot = $bag
[Game.Inventory.InventoryModuleRuntime]::Instance = $inventory
$owner.ForgingOperationSessionId = $sessionB
$network.Sent.Clear()
$network.PersistedBeforeSend = $false
$runtime.BeginSession($owner, 43, 1, $sessionB)
$update.Invoke($runtime, @()) | Out-Null
$mixedRecipe = '[{"recipeId":"YF001_item","name":"皮甲锻造",' +
    '"durationSeconds":60,"successRateBps":8000,"materials":[' +
    '{"itemId":"iron_ore","count":5,"isCurrency":false},' +
    '{"itemId":"gold_coin","count":10,"isCurrency":true}]}]'
$catalogPacket = Packet $network.Sent[0].requestSequence 12 $sync 1 1 1 $mixedRecipe
$dispatcher.Dispatch('forging_catalog_result',
    [Newtonsoft.Json.JsonConvert]::SerializeObject($catalogPacket))
$emptyTask = '{"msgId":"forging_snapshot_result","type":"forging_snapshot_result",' +
    '"forgingOperationSessionId":"' + $sessionB + '","requestSequence":' +
    $network.Sent[1].requestSequence + ',"success":true,' +
    '"serverTime":1793757600000,' +
    '"snapshot":{"characterDbId":43,"revision":0,"currentTask":null}}'
$dispatcher.Dispatch('forging_snapshot_result', $emptyTask)

$selected = [System.Collections.Generic.List[Game.Forging.锻造已选材料]]::new()
$selected.Add([Game.Forging.锻造已选材料]::new(101, 'iron_ore', 5))
$intent = [Game.Forging.锻造开始意向]::new(43, 35,
    'YF001_item', $selected.AsReadOnly())
Assert-True ($runtime.CanStart($intent, $bag) -and
    $runtime.TryStart($intent, $bag) -and
    $network.PersistedBeforeSend -and $runtime.HasUnresolvedStart) `
    'Start must persist its identity and business parameters before sending.'
$request = $network.Sent[2]
Assert-True ($request.msgId -eq 'forging_start' -and
    $request.configurationRevision -eq 12 -and
    $request.expectedInventoryRevision -eq 35 -and
    $request.materials.Length -eq 1 -and
    $request.materials[0].stackId -eq 101 -and
    -not $runtime.TryStart($intent, $bag)) `
    'The same selection must be serialized once and duplicate clicks blocked.'
$taskJson = '{"characterDbId":43,"revision":1,"currentTask":{' +
    '"taskId":"33333333-3333-4333-8333-333333333333",' +
    '"recipeId":"YF001_item","recipeName":"皮甲锻造",' +
    '"status":"active","succeeded":null,' +
    '"startedAtUnixMilliseconds":1793757600000,' +
    '"endsAtUnixMilliseconds":1793757660000,"outputCount":0}}'
$success = '{"msgId":"forging_start_result","type":"forging_start_result",' +
    '"forgingOperationSessionId":"' + $sessionB + '","requestSequence":' +
    $request.requestSequence + ',"clientRequestId":"' +
    $request.clientRequestId + '","success":true,' +
    '"configurationRevision":12,"serverTime":1793757600000,' +
    '"inventoryRevision":36,"stackItemChanges":[{"stackId":101,' +
    '"itemId":"","newCount":0}],"wallets":[' +
    '{"currencyId":"gold_coin","balance":90}],"equipmentItems":[],' +
    '"snapshot":' + $taskJson + '}'
$wrong = $success.Replace($request.clientRequestId, $sessionA)
$dispatcher.Dispatch('forging_start_result', $wrong)
Assert-True ($inventory.AppliedStarts -eq 0 -and
    $runtime.HasUnresolvedStart) `
    'A foreign UUID cannot mutate inventory or resolve the pending request.'
$dispatcher.Dispatch('forging_start_result', $success)
Assert-True ($inventory.AppliedStarts -eq 1 -and
    $inventory.CurrentSnapshot.InventoryRevision -eq 36 -and
    $inventory.CurrentSnapshot.Stacks.Count -eq 0 -and
    $inventory.CurrentSnapshot.Wallets['gold_coin'] -eq 90 -and
    $runtime.Snapshot.currentTask.status -eq 'active' -and
    -not $runtime.HasUnresolvedStart) `
    'First success must apply absolute stack and wallet changes once.'
$dispatcher.Dispatch('forging_start_result', $success)
Assert-True ($inventory.AppliedStarts -eq 1) `
    'Duplicate success must never apply the debit twice.'

$inventory.CurrentSnapshot = [ForgingFixture]::Bag(43, 41, 101,
    'iron_ore', 5, 100)
$network.Sent.Clear()
$runtime.BeginSession($owner, 43, 1, $sessionB)
$update.Invoke($runtime, @()) | Out-Null
$catalogPacket = Packet $network.Sent[0].requestSequence 12 $sync 1 1 1 $mixedRecipe
$dispatcher.Dispatch('forging_catalog_result',
    [Newtonsoft.Json.JsonConvert]::SerializeObject($catalogPacket))
$snapshotPacket = '{"msgId":"forging_snapshot_result","type":"forging_snapshot_result",' +
    '"forgingOperationSessionId":"' + $sessionB + '","requestSequence":' +
    $network.Sent[1].requestSequence + ',"success":true,' +
    '"serverTime":1793757600000,' +
    '"snapshot":{"characterDbId":43,"revision":0,"currentTask":null}}'
$dispatcher.Dispatch('forging_snapshot_result', $snapshotPacket)
$intent = [Game.Forging.锻造开始意向]::new(43, 41,
    'YF001_item', $selected.AsReadOnly())
Assert-True ($runtime.TryStart($intent, $inventory.CurrentSnapshot)) `
    'A fresh session must accept a fresh operation.'
$pending = $network.Sent[2]
$persistFailed = '{"msgId":"forging_start_result","type":"forging_start_result",' +
    '"forgingOperationSessionId":"' + $sessionB + '","requestSequence":' +
    $pending.requestSequence + ',"clientRequestId":"' +
    $pending.clientRequestId + '","success":false,' +
    '"errorCode":"forging_persist_failed"}'
$dispatcher.Dispatch('forging_start_result', $persistFailed)
Assert-True ($runtime.HasUnresolvedStart -and
    $inventory.AppliedStarts -eq 1) `
    'Unconfirmed persistence must retain the same pending write.'
$inventory.CurrentSnapshot = [ForgingFixture]::Bag(43, 42, 102,
    'iron_ore', 1, 90)
$owner.ForgingOperationSessionId = $sessionA
$network.Sent.Clear()
$runtime.BeginSession($owner, 43, 1, $sessionA)
$update.Invoke($runtime, @()) | Out-Null
$catalogPacket = Packet $network.Sent[0].requestSequence 12 $sync 1 1 1 $mixedRecipe
$catalogJson = [Newtonsoft.Json.JsonConvert]::SerializeObject($catalogPacket).
    Replace($sessionB, $sessionA)
$dispatcher.Dispatch('forging_catalog_result', $catalogJson)
$snapshotPacket = '{"msgId":"forging_snapshot_result","type":"forging_snapshot_result",' +
    '"forgingOperationSessionId":"' + $sessionA + '","requestSequence":' +
    $network.Sent[1].requestSequence + ',"success":true,' +
    '"serverTime":1793757600000,' +
    '"snapshot":{"characterDbId":43,"revision":2,"currentTask":null}}'
$dispatcher.Dispatch('forging_snapshot_result', $snapshotPacket)
$update.Invoke($runtime, @()) | Out-Null
$replayed = $network.Sent[$network.Sent.Count - 1]
Assert-True ($replayed.msgId -eq 'forging_start' -and
    $replayed.clientRequestId -eq $pending.clientRequestId -and
    $replayed.forgingOperationSessionId -eq $sessionA -and
    $replayed.requestSequence -ne $pending.requestSequence -and
    $replayed.configurationRevision -eq $pending.configurationRevision -and
    $replayed.expectedInventoryRevision -eq $pending.expectedInventoryRevision -and
    $replayed.recipeId -ceq $pending.recipeId -and
    $replayed.materials[0].stackId -eq $pending.materials[0].stackId) `
    'Re-entry must keep the UUID and parameters but use the new session and sequence.'
$replayResult = '{"msgId":"forging_start_result","type":"forging_start_result",' +
    '"forgingOperationSessionId":"' + $sessionA + '","requestSequence":' +
    $replayed.requestSequence + ',"clientRequestId":"' +
    $replayed.clientRequestId + '","success":true,"idempotentReplay":true,' +
    '"requiresInventorySnapshot":true,"inventoryRevision":0,' +
    '"serverTime":1793757600000,"snapshot":null}'
$dispatcher.Dispatch('forging_start_result', $replayResult)
Assert-True ($runtime.HasUnresolvedStart -and
    $inventory.AppliedStarts -eq 1 -and
    $inventory.SnapshotRequests -gt 0) `
    'Replay must request repair without applying an old debit or clearing the task.'
Assert-True ($runtime.Snapshot.revision -eq 2 -and
    $null -eq $runtime.Snapshot.currentTask) `
    'A pre-replay empty task cannot confirm a successful replay.'
$taskRefresh = '{"msgId":"forging_snapshot_result","type":"forging_snapshot_result",' +
    '"forgingOperationSessionId":"' + $sessionA + '","requestSequence":' +
    $network.Sent[$network.Sent.Count - 1].requestSequence +
    ',"success":true,"serverTime":1793757600000,"snapshot":' +
    $taskJson.Replace('"revision":1', '"revision":3') + '}'
$dispatcher.Dispatch('forging_snapshot_result', $taskRefresh)
$update.Invoke($runtime, @()) | Out-Null
Assert-True (-not $runtime.HasUnresolvedStart -and
    $inventory.AppliedStarts -eq 1) `
    'A confirmed replay resolves only after task and inventory recovery.'

$inventory.CurrentSnapshot = [ForgingFixture]::Bag(43, 45, 999,
    'unrelated_item', 1, 100)
$network.Sent.Clear()
$runtime.BeginSession($owner, 43, 1, $sessionA)
$update.Invoke($runtime, @()) | Out-Null
$currencyCatalog = '[{"recipeId":"YF002_item","name":"纯货币锻造",' +
    '"durationSeconds":60,"successRateBps":10000,"materials":[' +
    '{"itemId":"gold_coin","count":10,"isCurrency":true}]}]'
$currencyPacket = Packet $network.Sent[0].requestSequence 12 $sync 1 1 1 $currencyCatalog
$dispatcher.Dispatch('forging_catalog_result',
    [Newtonsoft.Json.JsonConvert]::SerializeObject($currencyPacket).
        Replace($sessionB, $sessionA))
$snapshotPacket = '{"msgId":"forging_snapshot_result","type":"forging_snapshot_result",' +
    '"forgingOperationSessionId":"' + $sessionA + '","requestSequence":' +
    $network.Sent[1].requestSequence + ',"success":true,' +
    '"serverTime":1793757600000,' +
    '"snapshot":{"characterDbId":43,"revision":0,"currentTask":null}}'
$dispatcher.Dispatch('forging_snapshot_result', $snapshotPacket)
$nothing = [System.Collections.Generic.List[Game.Forging.锻造已选材料]]::new()
$currencyIntent = [Game.Forging.锻造开始意向]::new(43, 45,
    'YF002_item', $nothing.AsReadOnly())
Assert-True ($runtime.CanStart($currencyIntent, $inventory.CurrentSnapshot) -and
    $runtime.TryStart($currencyIntent, $inventory.CurrentSnapshot) -and
    $network.Sent[2].materials.Length -eq 0) `
    'A pure currency recipe must submit an empty stack selection.'
$currencyRequest = $network.Sent[2]
$rejected = '{"msgId":"forging_start_result","type":"forging_start_result",' +
    '"forgingOperationSessionId":"' + $sessionA + '","requestSequence":' +
    $currencyRequest.requestSequence + ',"clientRequestId":"' +
    $currencyRequest.clientRequestId + '","success":false,' +
    '"errorCode":"forging_technique_required"}'
$dispatcher.Dispatch('forging_start_result', $rejected)
Assert-True (-not $runtime.HasUnresolvedStart -and
    $inventory.AppliedStarts -eq 1 -and
    -not [UnityEngine.PlayerPrefs]::Values.ContainsKey(
        'forging_start_in_flight_43') -and
    [Game.UI.Notice.GameNoticeService]::Notices[-1] -eq
        '请先学习锻造类功法') `
    'A definite technique rejection must unblock the next attempt without a debit.'

$panel = Get-Content (Join-Path $root 'Assets/Scripts/锻造/锻造面板视图.cs') -Raw
Assert-True ($panel.Contains('runtime.StartStateChanged += RefreshStartButton') -and
    $panel.Contains('runtime.HasUnresolvedStart') -and
    $panel.Contains('runtime.TryStart(new 锻造开始意向') -and
    $panel.Contains('selection.Matches(recipe, bag)') -and
    $panel.Contains('runtime.IsSnapshotCurrent')) `
    'The panel must lock while pending and submit only current bag selections.'

Write-Output 'PASS: forging start selection, persisted identity, delta and replay.'
