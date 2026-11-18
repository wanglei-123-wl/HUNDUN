$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'AlchemySessionRuntime.Tests.ps1') | Out-Null

$owner = [GameSession]::new()
$owner.AlchemyOperationSessionId = [Guid]::NewGuid().ToString('D')
$network = [NetworkManager]::new()
$inventory = [Game.Inventory.InventoryModuleRuntime]::new()
$inventory.CurrentSnapshot = [Game.Inventory.Model.V2.InventoryOwnershipSnapshot]::new(
    42, 21,
    [Game.Inventory.Model.V2.InventoryStackState]::new(9007199254740993L,
        'ling_shi', 99))
[UnityEngine.MockObjects]::Inventory = $inventory
$runtime = [Game.Alchemy.Runtime.AlchemySessionRuntime]::Ensure($network, $owner)
$runtime.BeginSession($owner, 42, $owner.AlchemyOperationSessionId)
$dispatcher = [NetMessageDispatcher]::Instance
$recipeJson = '{"recipeId":"pill","name":"test","durationSeconds":60,' +
    '"successRateBps":8000,"materials":[{"itemId":"ling_shi","count":7}],' +
    '"outputs":[]}'
$runtime.RequestCatalog() | Out-Null
$catalogSequence = ($network.Sent[-1] | ConvertFrom-Json).requestSequence
$dispatcher.Dispatch('alchemy_catalog_result',
    (Catalog-Json $catalogSequence 12 ([Guid]::NewGuid().ToString('D')) $recipeJson))
$runtime.RequestSnapshot() | Out-Null
$snapshotSequence = ($network.Sent[-1] | ConvertFrom-Json).requestSequence
$dispatcher.Dispatch('alchemy_snapshot_result',
    (Result-Json $snapshotSequence 42 1 'null' 1800000000000).Replace(
        '"learnedRecipeIds":[]', '"learnedRecipeIds":["pill"]'))
$selection = [Game.Alchemy.Model.AlchemyMaterialSelection]::new()
Assert-True ($selection.SelectRecipe($runtime.Catalog[0], 12, 42) -and
    $runtime.CanStart($selection, $inventory.CurrentSnapshot)) `
    'An empty preselection does not disable the start button.'
$beforeEmpty = $network.Sent.Count
Assert-True (-not $runtime.TryStart($selection, $inventory.CurrentSnapshot) -and
    $network.Sent.Count -eq $beforeEmpty -and
    -not $runtime.HasUnresolvedStart) `
    'Clicking with no materials must not send an invalid start.'
Assert-True ($selection.SelectRecipe($runtime.Catalog[0], 12, 42) -and
    $selection.SetStack($inventory.CurrentSnapshot, 9007199254740993L, 7) -eq
        [Game.Alchemy.Model.AlchemySelectionError]::None -and
    $runtime.CanStart($selection, $inventory.CurrentSnapshot)) `
    'A learned recipe with a live stack may start.'
Assert-True ($runtime.TryStart($selection, $inventory.CurrentSnapshot) -and
    $selection.IsLocked -and $runtime.HasUnresolvedStart -and
    [object]::ReferenceEquals($runtime.StartSelection, $selection) -and
    -not $runtime.TryStart($selection, $inventory.CurrentSnapshot)) `
    'Runtime must retain a locked submitted choice while the panel is absent.'
$first = $network.Sent[-1] | ConvertFrom-Json
Assert-True ($first.msgId -eq 'alchemy_start' -and
    $first.type -eq 'alchemy_start' -and
    $first.alchemyOperationSessionId -eq $owner.AlchemyOperationSessionId -and
    $first.recipeId -eq 'pill' -and
    $first.configurationRevision -eq 12 -and
    $first.expectedInventoryRevision -eq 21 -and
    $first.materials.Count -eq 1 -and
    $first.materials[0].stackId -eq 9007199254740993L -and
    $first.materials[0].count -eq 7 -and
    $inventory.CurrentSnapshot.Stacks[0].Count -eq 99) `
    'Start must send the exact 64-bit stack/count and must not deduct locally.'

$activeTask = '{"taskId":"task-1","recipeId":"pill","status":"active",' +
    '"succeeded":null,"startedAtUnixMilliseconds":1800000000000,' +
    '"endsAtUnixMilliseconds":1800000060000}'
$success = '{"msgId":"alchemy_start_result","type":"alchemy_start_result",' +
    '"alchemyOperationSessionId":"' + $first.alchemyOperationSessionId +
    '","requestSequence":' + $first.requestSequence +
    ',"clientRequestId":"' + $first.clientRequestId +
    '","success":true,"configurationRevision":12,"serverTime":1800000000000,' +
    '"inventoryRevision":22,"stackItemChanges":[{"stackId":9007199254740993,' +
    '"itemId":"ling_shi","previousCount":0,"newCount":92,"isBound":false}],' +
    '"snapshot":{"characterDbId":42,"revision":2,"learnedRecipeIds":["pill"],' +
    '"currentTask":' + $activeTask + '}}'
$dispatcher.Dispatch('alchemy_start_result',
    $success.Replace($first.clientRequestId, [Guid]::NewGuid().ToString('D')))
Assert-True ($runtime.HasUnresolvedStart -and $inventory.DeltaCalls -eq 0) `
    'A mismatched operation identity cannot mutate the inventory.'
$dispatcher.Dispatch('alchemy_start_result', $success)
Assert-True (-not $runtime.HasUnresolvedStart -and
    $runtime.Snapshot.currentTask.taskId -eq 'task-1' -and
    $inventory.DeltaCalls -eq 1 -and
    $inventory.CurrentSnapshot.InventoryRevision -eq 22 -and
    $inventory.CurrentSnapshot.Stacks[0].Count -eq 92 -and
    -not $runtime.StartSelection.IsLocked -and
    $runtime.StartSelection.SelectedStackCount -eq 0) `
    'First success without a panel clears held slots and applies inventory once.'
$dispatcher.Dispatch('alchemy_start_result', $success)
Assert-True ($inventory.DeltaCalls -eq 1) `
    'A duplicate success cannot deduct materials again.'

$freshChoice = [Game.Alchemy.Model.AlchemyMaterialSelection]::new()
Assert-True ($freshChoice.SelectRecipe($runtime.Catalog[0], 12, 42) -and
    $freshChoice.SetStack($inventory.CurrentSnapshot, 9007199254740993L, 7) -eq
        [Game.Alchemy.Model.AlchemySelectionError]::None -and
    -not $runtime.CanStart($freshChoice, $inventory.CurrentSnapshot)) `
    'An occupied furnace blocks a valid independent material choice.'
Assert-True ($runtime.RequestSnapshot()) 'The furnace can be checked again.'
$emptySequence = ($network.Sent[-1] | ConvertFrom-Json).requestSequence
$dispatcher.Dispatch('alchemy_snapshot_result',
    (Result-Json $emptySequence 42 2 'null' 1800000000100).Replace(
        '"learnedRecipeIds":[]', '"learnedRecipeIds":["pill"]'))
Assert-True ($runtime.IsSnapshotCurrent -and
    $null -eq $runtime.Snapshot.currentTask -and
    $runtime.CanStart($freshChoice, $inventory.CurrentSnapshot)) `
    'An explicit empty furnace at the same revision restores start eligibility without bypassing material checks.'

$owner.AlchemyOperationSessionId = [Guid]::NewGuid().ToString('D')
$runtime.BeginSession($owner, 42, $owner.AlchemyOperationSessionId)
$runtime.RequestCatalog() | Out-Null
$seq = ($network.Sent[-1] | ConvertFrom-Json).requestSequence
$dispatcher.Dispatch('alchemy_catalog_result',
    (Catalog-Json $seq 12 ([Guid]::NewGuid().ToString('D')) $recipeJson))
$runtime.RequestSnapshot() | Out-Null
$seq = ($network.Sent[-1] | ConvertFrom-Json).requestSequence
$dispatcher.Dispatch('alchemy_snapshot_result',
    (Result-Json $seq 42 3 'null' 1800000000100).Replace(
        '"learnedRecipeIds":[]', '"learnedRecipeIds":["pill"]'))
$choice = [Game.Alchemy.Model.AlchemyMaterialSelection]::new()
Assert-True ($choice.SelectRecipe($runtime.Catalog[0], 12, 42) -and
    $choice.SetStack($inventory.CurrentSnapshot, 9007199254740993L, 7) -eq
        [Game.Alchemy.Model.AlchemySelectionError]::None -and
    $runtime.TryStart($choice, $inventory.CurrentSnapshot)) `
    'A free furnace in a new session can open another operation.'
$unconfirmed = $network.Sent[-1] | ConvertFrom-Json
$network.Disconnect()
$replacementNetwork = [NetworkManager]::new()
$owner.AlchemyOperationSessionId = [Guid]::NewGuid().ToString('D')
$replacement = [Game.Alchemy.Runtime.AlchemySessionRuntime]::Ensure(
    $replacementNetwork, $owner)
$replacement.BeginSession($owner, 42, $owner.AlchemyOperationSessionId)
Assert-True ($replacement.HasUnresolvedStart) `
    'A new runtime instance must load the original immutable request.'
$restored = $replacement.StartSelection
Assert-True ($null -ne $restored -and $restored.IsLocked -and
    $restored.CharacterDbId -eq 42 -and $restored.RecipeId -eq 'pill' -and
    $restored.SelectedCountForStack(9007199254740993L) -eq 7) `
    'A destroyed panel restores its locked slots from the persisted request.'
$owner.AlchemyOperationSessionId = [Guid]::NewGuid().ToString('D')
$replacement.BeginSession($owner, 42, $owner.AlchemyOperationSessionId)
Assert-True ([object]::ReferenceEquals($restored,
    $replacement.StartSelection) -and $restored.IsLocked) `
    'Re-entry in the same runtime keeps the displayed choice and its order.'
$tick = [Game.Alchemy.Runtime.AlchemySessionRuntime].GetMethod(
    'Update', [System.Reflection.BindingFlags]'Instance, NonPublic')
$tick.Invoke($replacement, @()) | Out-Null
$retry = $replacementNetwork.Sent[-1] | ConvertFrom-Json
Assert-True ($retry.msgId -eq 'alchemy_start' -and
    $retry.clientRequestId -eq $unconfirmed.clientRequestId -and
    $retry.alchemyOperationSessionId -eq $owner.AlchemyOperationSessionId -and
    $retry.requestSequence -ne $unconfirmed.requestSequence -and
    $retry.materials[0].stackId -eq $unconfirmed.materials[0].stackId -and
    $retry.materials[0].count -eq $unconfirmed.materials[0].count -and
    $retry.expectedInventoryRevision -eq $unconfirmed.expectedInventoryRevision) `
    'Recovery changes only session/sequence and preserves UUID and all business fields.'
$beforeReplay = $inventory.DeltaCalls
$dispatcher.Dispatch('alchemy_start_result',
    '{"msgId":"alchemy_start_result","type":"alchemy_start_result",' +
    '"alchemyOperationSessionId":"' + $owner.AlchemyOperationSessionId +
    '","requestSequence":' + $retry.requestSequence +
    ',"clientRequestId":"' + $retry.clientRequestId +
    '","success":true,"idempotentReplay":true,' +
    '"requiresInventorySnapshot":true,"snapshot":null}')
Assert-True ($replacement.HasUnresolvedStart -and $restored.IsLocked -and
    $replacement.IsInventoryRecovering -and
    $inventory.DeltaCalls -eq $beforeReplay -and
    ($replacementNetwork.Sent[-1] | ConvertFrom-Json).msgId -eq
        'alchemy_snapshot') `
    'Replay keeps the original request until both snapshots are verified.'

$replacementNetwork.Disconnect()
$retryNetwork = [NetworkManager]::new()
$owner.AlchemyOperationSessionId = [Guid]::NewGuid().ToString('D')
$retryRuntime = [Game.Alchemy.Runtime.AlchemySessionRuntime]::Ensure(
    $retryNetwork, $owner)
$retryRuntime.BeginSession($owner, 42, $owner.AlchemyOperationSessionId)
Assert-True ($retryRuntime.HasUnresolvedStart -and
    $retryRuntime.StartSelection.IsLocked -and
    $retryRuntime.StartSelection.SelectedCountForStack(
        9007199254740993L) -eq 7) `
    'Disconnect during replay recovery keeps the original request and slots.'
$tick.Invoke($retryRuntime, @()) | Out-Null
$lastRetry = $retryNetwork.Sent[-1] | ConvertFrom-Json
Assert-True ($lastRetry.msgId -eq 'alchemy_start' -and
    $lastRetry.clientRequestId -eq $unconfirmed.clientRequestId -and
    $lastRetry.materials[0].count -eq 7) `
    'Re-entry resends the same operation identity and quantities.'
$dispatcher.Dispatch('alchemy_start_result',
    '{"msgId":"alchemy_start_result","type":"alchemy_start_result",' +
    '"alchemyOperationSessionId":"' + $owner.AlchemyOperationSessionId +
    '","requestSequence":' + $lastRetry.requestSequence +
    ',"clientRequestId":"' + $lastRetry.clientRequestId +
    '","success":true,"idempotentReplay":true,' +
    '"requiresInventorySnapshot":true,"snapshot":null}')
$repair = $retryNetwork.Sent[-1] | ConvertFrom-Json
$inventory.CurrentSnapshot =
    [Game.Inventory.Model.V2.InventoryOwnershipSnapshot]::new(
        42, 23, [Game.Inventory.Model.V2.InventoryStackState]::new(
            9007199254740993L, 'ling_shi', 85))
$tick.Invoke($retryRuntime, @()) | Out-Null
Assert-True ($retryRuntime.HasUnresolvedStart -and
    $retryRuntime.StartSelection.IsLocked) `
    'Inventory alone cannot resolve a replay while the furnace is unverified.'
$dispatcher.Dispatch('alchemy_snapshot_result',
    (Result-Json $repair.requestSequence 42 4 $activeTask 1800000000500
        ).Replace('"learnedRecipeIds":[]', '"learnedRecipeIds":["pill"]'))
$tick.Invoke($retryRuntime, @()) | Out-Null
Assert-True ($retryRuntime.IsSnapshotCurrent -and
    $retryRuntime.Snapshot.currentTask.taskId -eq 'task-1') `
    'Fresh furnace snapshot must be accepted after replay.'
Assert-True (-not $retryRuntime.IsInventoryRecovering) `
    'Fresh inventory snapshot must clear replay recovery.'
Assert-True (-not $retryRuntime.HasUnresolvedStart) `
    'Both snapshots must remove the replay request.'
Assert-True (-not $retryRuntime.StartSelection.IsLocked -and
    $retryRuntime.StartSelection.SelectedStackCount -eq 0) `
    'Confirmed replay success must clear restored slots.'
Assert-True ($inventory.DeltaCalls -eq $beforeReplay) `
    'Replay must not apply any historical inventory delta.'

$failureOwner = [GameSession]::new()
$failureOwner.LocalCharacterDbId = 43
$failureOwner.AlchemyOperationSessionId = [Guid]::NewGuid().ToString('D')
$owner = $failureOwner
$failureNetwork = [NetworkManager]::new()
$failureInventory = [Game.Inventory.InventoryModuleRuntime]::new()
$failureInventory.CurrentSnapshot =
    [Game.Inventory.Model.V2.InventoryOwnershipSnapshot]::new(
        43, 5, @(
            [Game.Inventory.Model.V2.InventoryStackState]::new(
                5011, 'ling_shi', 20),
            [Game.Inventory.Model.V2.InventoryStackState]::new(
                5012, 'other', 3)))
[UnityEngine.MockObjects]::Inventory = $failureInventory
$failureRuntime = [Game.Alchemy.Runtime.AlchemySessionRuntime]::Ensure(
    $failureNetwork, $failureOwner)
$failureRuntime.BeginSession($failureOwner, 43,
    $failureOwner.AlchemyOperationSessionId)
$failureRuntime.RequestCatalog() | Out-Null
$seq = ($failureNetwork.Sent[-1] | ConvertFrom-Json).requestSequence
$dispatcher.Dispatch('alchemy_catalog_result',
    (Catalog-Json $seq 12 ([Guid]::NewGuid().ToString('D')) $recipeJson))
$failureRuntime.RequestSnapshot() | Out-Null
$seq = ($failureNetwork.Sent[-1] | ConvertFrom-Json).requestSequence
$dispatcher.Dispatch('alchemy_snapshot_result',
    (Result-Json $seq 43 1 'null' 1800000000000).Replace(
        '"learnedRecipeIds":[]', '"learnedRecipeIds":["pill"]'))
$failedChoice = [Game.Alchemy.Model.AlchemyMaterialSelection]::new()
Assert-True ($failedChoice.SelectRecipe($failureRuntime.Catalog[0], 12, 43) -and
    $failedChoice.SetStack($failureInventory.CurrentSnapshot, 5011, 8) -eq
        [Game.Alchemy.Model.AlchemySelectionError]::None -and
    $failedChoice.SetStack($failureInventory.CurrentSnapshot, 5012, 1) -eq
        [Game.Alchemy.Model.AlchemySelectionError]::None -and
    $failureRuntime.TryStart($failedChoice, $failureInventory.CurrentSnapshot)) `
    'Wrong and excess materials are submitted for authoritative validation.'
$uncertain = $failureNetwork.Sent[-1] | ConvertFrom-Json
Assert-True ($uncertain.materials.Count -eq 2 -and
    $uncertain.materials[0].stackId -eq 5011 -and
    $uncertain.materials[0].count -eq 8 -and
    $uncertain.materials[1].stackId -eq 5012 -and
    $uncertain.materials[1].count -eq 1) `
    'The sent request preserves every selected stack and actual quantity.'
$prefix = '{"msgId":"alchemy_start_result","type":"alchemy_start_result",' +
    '"alchemyOperationSessionId":"' + $failureOwner.AlchemyOperationSessionId +
    '","requestSequence":' + $uncertain.requestSequence +
    ',"clientRequestId":"' + $uncertain.clientRequestId + '"'
$dispatcher.Dispatch('alchemy_start_result', $prefix +
    ',"success":false,"errorCode":"alchemy_persist_failed"}')
Assert-True ($failureRuntime.HasUnresolvedStart -and
    -not $failureRuntime.CanStart($failedChoice,
        $failureInventory.CurrentSnapshot)) `
    'Persist failure keeps the original operation pending and disables new starts.'
$operation = [Game.Alchemy.Runtime.AlchemySessionRuntime].GetField(
    'startOperation', [System.Reflection.BindingFlags]'Instance, NonPublic'
    ).GetValue($failureRuntime)
$pending = [Game.Alchemy.Runtime.AlchemyStartOperationState].GetField(
    'pending', [System.Reflection.BindingFlags]'Instance, NonPublic'
    ).GetValue($operation)
$pending.GetType().GetField('lastSentAt').SetValue($pending,
    [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds() - 20000)
$tick.Invoke($failureRuntime, @()) | Out-Null
$resent = $failureNetwork.Sent[-1] | ConvertFrom-Json
Assert-True ($resent.msgId -eq 'alchemy_start' -and
    $resent.clientRequestId -eq $uncertain.clientRequestId -and
    $resent.requestSequence -eq $uncertain.requestSequence -and
    $resent.materials[0].count -eq $uncertain.materials[0].count -and
    $resent.materials[1].stackId -eq $uncertain.materials[1].stackId) `
    'A persist failure retries the same sequence, UUID and material choices.'
$dispatcher.Dispatch('error',
    '{"msgId":"error","requestMsgId":"alchemy_start",' +
    '"errorCode":"alchemy_invalid_request","message":"request rejected"}')
Assert-True ($failureRuntime.HasUnresolvedStart -and
    -not $failureRuntime.CanStart($failedChoice,
        $failureInventory.CurrentSnapshot) -and
    ($failureNetwork.Sent[-1] | ConvertFrom-Json).msgId -eq
        'alchemy_snapshot') `
    'A generic start error keeps the unknown operation locked and queries the furnace.'
$dispatcher.Dispatch('alchemy_start_result', $prefix +
    ',"success":false,"errorCode":"alchemy_material_mismatch"}')
Assert-True (-not $failureRuntime.HasUnresolvedStart -and
    $failureInventory.DeltaCalls -eq 0 -and
    [object]::ReferenceEquals($failureRuntime.StartSelection, $failedChoice) -and
    -not $failureRuntime.StartSelection.IsLocked -and
    $failureRuntime.StartSelection.SelectedStackCount -eq 2) `
    'A definite rejection without a panel keeps both slots editable and deducts nothing.'
$repairSequence = ($failureNetwork.Sent[-1] | ConvertFrom-Json).requestSequence
$dispatcher.Dispatch('alchemy_snapshot_result',
    (Result-Json $repairSequence 43 1 'null' 1800000000200).Replace(
        '"learnedRecipeIds":[]', '"learnedRecipeIds":["pill"]'))
$failedChoice.ResolveOperation($false)
Assert-True ($failedChoice.SetStack($failureInventory.CurrentSnapshot, 5011, 7) -eq
        [Game.Alchemy.Model.AlchemySelectionError]::None -and
    $failedChoice.RemoveStack(5012) -and
    $failureRuntime.TryStart($failedChoice, $failureInventory.CurrentSnapshot) -and
    ($failureNetwork.Sent[-1] | ConvertFrom-Json).clientRequestId -ne
        $uncertain.clientRequestId) `
    'After a definite failure, corrected selections use a new UUID.'
$corrected = $failureNetwork.Sent[-1] | ConvertFrom-Json
$beforeFailureReplay = $failureInventory.DeltaCalls
$dispatcher.Dispatch('alchemy_start_result',
    '{"msgId":"alchemy_start_result","type":"alchemy_start_result",' +
    '"alchemyOperationSessionId":"' + $failureOwner.AlchemyOperationSessionId +
    '","requestSequence":' + $corrected.requestSequence +
    ',"clientRequestId":"' + $corrected.clientRequestId +
    '","success":false,"errorCode":"alchemy_material_quantity_mismatch",' +
    '"idempotentReplay":true,"requiresInventorySnapshot":true,"snapshot":null}')
Assert-True ($failureRuntime.HasUnresolvedStart -and
    $failureRuntime.StartSelection.IsLocked -and
    $failureRuntime.IsInventoryRecovering -and
    -not $failureRuntime.IsSnapshotCurrent -and
    ($failureNetwork.Sent[-1] | ConvertFrom-Json).msgId -eq
        'alchemy_snapshot' -and
    $failureInventory.DeltaCalls -eq $beforeFailureReplay) `
    'A replayed failure retains the request and slots until both snapshots are repaired.'
$failedRepair = ($failureNetwork.Sent[-1] | ConvertFrom-Json).requestSequence
$failureInventory.CurrentSnapshot =
    [Game.Inventory.Model.V2.InventoryOwnershipSnapshot]::new(
        43, 6,
        [Game.Inventory.Model.V2.InventoryStackState]::new(5011, 'ling_shi', 20),
        [Game.Inventory.Model.V2.InventoryStackState]::new(5012, 'other', 3))
$dispatcher.Dispatch('alchemy_snapshot_result',
    (Result-Json $failedRepair 43 2 'null' 1800000000300).Replace(
        '"learnedRecipeIds":[]', '"learnedRecipeIds":["pill"]'))
$tick.Invoke($failureRuntime, @()) | Out-Null
Assert-True (-not $failureRuntime.HasUnresolvedStart -and
    -not $failureRuntime.StartSelection.IsLocked -and
    $failureRuntime.StartSelection.SelectedStackCount -eq 1 -and
    $failureRuntime.StartSelection.SelectedCountForStack(5011) -eq 7 -and
    $failureRuntime.StartSelection.SetStack(
        $failureInventory.CurrentSnapshot, 5011, 6) -eq
        [Game.Alchemy.Model.AlchemySelectionError]::None) `
    'Replayed failure retains the exact editable selection after both snapshots.'

$failureOwner.LocalCharacterDbId = 44
$failureOwner.AlchemyOperationSessionId = [Guid]::NewGuid().ToString('D')
$failureRuntime.BeginSession($failureOwner, 44,
    $failureOwner.AlchemyOperationSessionId)
Assert-True ($null -eq $failureRuntime.StartSelection) `
    'A new character cannot inherit the previous character material slots.'
[UnityEngine.PlayerPrefs]::SetString('alchemy_start_in_flight_44',
    '{invalid saved request')
$failureRuntime.BeginSession($failureOwner, 44,
    $failureOwner.AlchemyOperationSessionId)
Assert-True ($failureRuntime.HasUnresolvedStart -and
    $null -eq $failureRuntime.StartSelection -and
    -not $failureRuntime.CanStart($failedChoice,
        $failureInventory.CurrentSnapshot)) `
    'Unreadable saved requests block starts without inventing empty materials.'
Write-Output 'PASS: alchemy start identity, exact materials, first result and replay recovery.'
