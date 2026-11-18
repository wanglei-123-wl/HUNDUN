$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'CultivationProtocolStage1.Tests.ps1')

function Item-Result([long]$sequence, [int]$version, [string]$items,
    [bool]$success = $true) {
    $flag = if ($success) { 'true' } else { 'false' }
    return '{"msgId":"item_catalog_result","type":"item_catalog_result",' +
        '"requestSequence":' + $sequence + ',"success":' + $flag +
        ',"catalogVersion":' + $version + ',"items":' + $items +
        ',"errorCode":"","serverTime":3000}'
}

function Item-BatchResult([long]$sequence, [int]$version, [long]$lifecycle,
    [int]$index, [int]$count, [string]$items) {
    $result = Item-Result $sequence $version $items
    return $result.Substring(0, $result.Length - 1) +
        ',"lifecycleRevision":' + $lifecycle + ',"batchIndex":' + $index +
        ',"batchCount":' + $count + '}'
}

function Technique-Result([long]$sequence, [long]$revision) {
    return '{"msgId":"cultivation_catalog_result","type":"cultivation_catalog_result",' +
        '"requestSequence":' + $sequence + ',"success":true,"configurationRevision":' +
        $revision + ',"grades":[{"gradeId":"heaven","name":"grade","order":1}],' +
        '"techniques":[{"techniqueId":"xuanqing","name":"Xuan Qing","gradeId":"heaven",' +
        '"enabled":true,"levels":[{"level":0,"comprehension":5,"durationSeconds":0,' +
        '"referenceComprehension":null,"materials":[]}]}],"serverTime":3000}'
}

$items = '[{"itemId":"book","kind":"techniquebook","version":1,' +
    '"nameKey":"book.name","descriptionKey":"book.description",' +
    '"iconKey":"icons/book","maxStack":99,"learnTechniqueId":"xuanqing",' +
    '"learnSkillId":"","effects":[{"otherModuleField":10}],"attributes":[]},' +
    '{"itemId":"stone","kind":"material","version":1,' +
    '"maxStack":99,"learnTechniqueId":""},' +
    '{"itemId":"skill","kind":"skillbook","version":1,"maxStack":99,' +
    '"learnTechniqueId":"","learnSkillId":"attack"}]'
$network.Sent.Clear()
$runtime.Refresh()
$initialQuery = $network.Sent[2] | ConvertFrom-Json
Assert-True ($network.Sent.Count -eq 3 -and
    $initialQuery.knownCatalogVersion -eq 0 -and
    $initialQuery.supportsBatches -eq $true -and
    $initialQuery.supportsLifecycleNotifications -eq $false) `
    'First entry must advertise catalog batching without subscribing to lifecycle notifications.'
$firstTechniqueSequence = ($network.Sent[0] | ConvertFrom-Json).requestSequence
$firstItemSequence = ($network.Sent[2] | ConvertFrom-Json).requestSequence
$freshSnapshot = '{"msgId":"cultivation_snapshot_result","type":"cultivation_snapshot_result",' +
    '"requestSequence":0,"success":true,"serverTime":3000,"snapshot":' +
    $baseSnapshot + '}'
[NetMessageDispatcher]::Instance.Dispatch('cultivation_snapshot_result', $freshSnapshot)
[NetMessageDispatcher]::Instance.Dispatch('cultivation_catalog_result',
    (Technique-Result $firstTechniqueSequence 57))
[NetMessageDispatcher]::Instance.Dispatch('item_catalog_result',
    (Item-Result $firstItemSequence 93 $items))
$resolvedBook = $null
$resolvedTechnique = $null
Assert-True (-not $runtime.IsConfigurationReady -and
    -not $runtime.TryResolveTechniqueBook('book', [ref]$resolvedBook,
        [ref]$resolvedTechnique)) `
    'Catalogs queried before the snapshot must not satisfy new configuration sync.'
[UnityEngine.Time]::unscaledTime = 27
$update.Invoke($runtime, @()) | Out-Null
$freshTechniqueSequence = ($network.Sent[-2] | ConvertFrom-Json).requestSequence
$freshItemSequence = ($network.Sent[-1] | ConvertFrom-Json).requestSequence
Assert-True (($network.Sent[-2] | ConvertFrom-Json).msgId -eq
    'cultivation_catalog' -and
    ($network.Sent[-1] | ConvertFrom-Json).msgId -eq 'item_catalog' -and
    ($network.Sent[-1] | ConvertFrom-Json).knownCatalogVersion -eq 93) `
    'A new configuration needs both catalogs; item version remains independent.'
[NetMessageDispatcher]::Instance.Dispatch('cultivation_catalog_result',
    (Technique-Result $freshTechniqueSequence 57))
[NetMessageDispatcher]::Instance.Dispatch('item_catalog_result',
    (Item-Result $freshItemSequence 93 $items))
Assert-True ($runtime.IsConfigurationReady -and
    $runtime.ItemCatalog.items.Length -eq 3 -and
    $runtime.ItemCatalog.rawJson.Contains('otherModuleField') -and
    $runtime.TryResolveTechniqueBook('book', [ref]$resolvedBook,
        [ref]$resolvedTechnique) -and
    $resolvedBook.iconKey -eq 'icons/book' -and
    $resolvedTechnique.techniqueId -eq 'xuanqing') `
    'Paired full catalogs must retain all items and link book to technique by ID.'
Assert-True (-not $runtime.TryResolveTechniqueBook('skill', [ref]$resolvedBook,
        [ref]$resolvedTechnique) -and
    -not $runtime.TryResolveTechniqueBook('stone', [ref]$resolvedBook,
        [ref]$resolvedTechnique)) `
    'Skillbooks and materials cannot be used as cultivation books.'

$nullBook = '[{"itemId":"book","kind":"techniquebook","maxStack":99,' +
    '"learnTechniqueId":"missing"}]'
$nullBookSequence = $runtime.RequestItemCatalog()
$requestSequence = ($network.Sent[-1] | ConvertFrom-Json).requestSequence
[NetMessageDispatcher]::Instance.Dispatch('item_catalog_result',
    (Item-Result ($requestSequence + 1) 100 $nullBook))
Assert-True ($runtime.ItemCatalog.catalogVersion -eq 93) `
    'An unmatched request sequence must not overwrite the item catalog.'
[NetMessageDispatcher]::Instance.Dispatch('item_catalog_result',
    (Item-Result $requestSequence 100 $nullBook $false))
Assert-True ($nullBookSequence -and
    $runtime.ItemCatalog.catalogVersion -eq 93) `
    'A failed response with an item array must not clear the cache.'

$nullBookSequence = $runtime.RequestItemCatalog()
$requestSequence = ($network.Sent[-1] | ConvertFrom-Json).requestSequence
[NetMessageDispatcher]::Instance.Dispatch('item_catalog_result',
    (Item-Result $requestSequence 92 $nullBook))
Assert-True ($runtime.ItemCatalog.catalogVersion -eq 93) `
    'An older version must not overwrite a newer version in the same session.'
$nullBookSequence = $runtime.RequestItemCatalog()
$requestSequence = ($network.Sent[-1] | ConvertFrom-Json).requestSequence
[NetMessageDispatcher]::Instance.Dispatch('item_catalog_result',
    (Item-Result $requestSequence 93 '[]'))
Assert-True ($runtime.ItemCatalog.items.Length -eq 0 -and
    -not $runtime.TryResolveTechniqueBook('book', [ref]$resolvedBook,
        [ref]$resolvedTechnique)) `
    'A successful same-version empty directory must replace the full cache.'
$nullBookSequence = $runtime.RequestItemCatalog()
$requestSequence = ($network.Sent[-1] | ConvertFrom-Json).requestSequence
[NetMessageDispatcher]::Instance.Dispatch('item_catalog_result',
    (Item-Result $requestSequence 93 $nullBook))
Assert-True (-not $runtime.TryResolveTechniqueBook('book', [ref]$resolvedBook,
    [ref]$resolvedTechnique)) `
    'An unknown learnTechniqueId must not be guessed from a book name.'

$inventoryRuntime = [Game.Inventory.InventoryModuleRuntime]::new()
$inventoryRuntime.CurrentSnapshot =
    [Game.Inventory.Model.V2.InventoryOwnershipSnapshot]::new()
$inventoryRuntime.CurrentSnapshot.CharacterDbId = 51
$unknown = [Game.Inventory.Model.V2.InventoryStackState]::new()
$unknown.StackId = 901
$unknown.ItemId = 'unknown_item'
$unknown.Count = 1
$inventoryRuntime.CurrentSnapshot.Stacks.Add($unknown)
[Game.Inventory.InventoryModuleRuntime]::Shared = $inventoryRuntime
[UnityEngine.Time]::unscaledTime = 30
$beforeUnknown = $network.Sent.Count
$update.Invoke($runtime, @()) | Out-Null
$unknownQueries = @($network.Sent | Select-Object -Skip $beforeUnknown |
    ForEach-Object { $_ | ConvertFrom-Json } |
    Where-Object { $_.msgId -eq 'item_catalog' })
Assert-True ($unknownQueries.Count -eq 1) `
    'An unknown bag item must trigger a separate low-frequency catalog refresh.'
$unknownSequence = $unknownQueries[0].requestSequence
[NetMessageDispatcher]::Instance.Dispatch('item_catalog_result',
    (Item-Result $unknownSequence 94 $items))
$beforeDuplicate = $network.Sent.Count
[UnityEngine.Time]::unscaledTime = 32
$update.Invoke($runtime, @()) | Out-Null
Assert-True ($network.Sent.Count -eq $beforeDuplicate) `
    'Unknown item refresh must be rate limited.'
$inventoryRuntime.CurrentSnapshot.Stacks.Clear()
$latestTechniqueQuery = @($network.Sent | ForEach-Object {
    $_ | ConvertFrom-Json
} | Where-Object { $_.msgId -eq 'cultivation_catalog' })[-1]
[NetMessageDispatcher]::Instance.Dispatch('cultivation_catalog_result',
    (Technique-Result $latestTechniqueQuery.requestSequence 57))
$firstSnapshotSequence = ($network.Sent[1] | ConvertFrom-Json).requestSequence
$confirmedSnapshot = $freshSnapshot.Replace('"requestSequence":0',
    '"requestSequence":' + $firstSnapshotSequence)
[NetMessageDispatcher]::Instance.Dispatch('cultivation_snapshot_result',
    $confirmedSnapshot)

$changedConfig = $baseSnapshot.Replace('"revision":0', '"revision":1').
    Replace('"configurationRevision":57', '"configurationRevision":58')
$changedPush = '{"msgId":"cultivation_changed","type":"cultivation_changed",' +
    '"cultivationOperationSessionId":"' + $owner.CultivationOperationSessionId +
    '","serverTime":3100,"snapshot":' + $changedConfig + '}'
[NetMessageDispatcher]::Instance.Dispatch('cultivation_changed', $changedPush)
Assert-True (-not $runtime.IsConfigurationReady -and
    $runtime.Snapshot.configurationRevision -eq 58) `
    'Configuration change must invalidate both directory sync markers.'
[UnityEngine.Time]::unscaledTime = 35
$update.Invoke($runtime, @()) | Out-Null
$updatedTechnique = ($network.Sent[-3] | ConvertFrom-Json)
$updatedItem = ($network.Sent[-1] | ConvertFrom-Json)
Assert-True ($updatedTechnique.msgId -eq 'cultivation_catalog' -and
    $updatedItem.msgId -eq 'item_catalog') `
    'A configuration change must requery both catalogs.'
[NetMessageDispatcher]::Instance.Dispatch('cultivation_catalog_result',
    (Technique-Result $updatedTechnique.requestSequence 58))
[NetMessageDispatcher]::Instance.Dispatch('item_catalog_result',
    (Item-Result $updatedItem.requestSequence 95 $items))
Assert-True ($runtime.IsConfigurationReady -and
    $runtime.ItemCatalog.catalogVersion -eq 95 -and
    $runtime.Catalog.configurationRevision -eq 58) `
    'Only matching current configuration and both refreshed catalogs enable conditions.'
$inFlightFromOldConfig = $runtime.RequestItemCatalog()
$oldItemSequence = ($network.Sent[-1] | ConvertFrom-Json).requestSequence
$newerConfig = $changedConfig.Replace('"revision":1', '"revision":2').
    Replace('"configurationRevision":58', '"configurationRevision":59')
$newerPush = $changedPush.Replace($changedConfig, $newerConfig)
[NetMessageDispatcher]::Instance.Dispatch('cultivation_changed', $newerPush)
[NetMessageDispatcher]::Instance.Dispatch('item_catalog_result',
    (Item-Result $oldItemSequence 96 $items))
Assert-True ($inFlightFromOldConfig -and
    $runtime.ItemCatalog.catalogVersion -eq 96 -and
    -not $runtime.IsConfigurationReady) `
    'An old in-flight item response cannot finish a newer configuration sync.'
[UnityEngine.Time]::unscaledTime = 40
$update.Invoke($runtime, @()) | Out-Null
$newTechniqueSequence = ($network.Sent[-2] | ConvertFrom-Json).requestSequence
$newItemSequence = ($network.Sent[-1] | ConvertFrom-Json).requestSequence
[NetMessageDispatcher]::Instance.Dispatch('cultivation_catalog_result',
    (Technique-Result $newTechniqueSequence 59))
[NetMessageDispatcher]::Instance.Dispatch('item_catalog_result',
    (Item-Result $newItemSequence 96 $items))
Assert-True ($runtime.IsConfigurationReady) `
    'Both catalog queries issued for the new configuration restore readiness.'

$duplicate = $runtime.RequestItemCatalog()
$duplicateSequence = ($network.Sent[-1] | ConvertFrom-Json).requestSequence
$duplicateItems = '[{"itemId":"book","kind":"techniquebook","maxStack":99},' +
    '{"itemId":"book","kind":"techniquebook","maxStack":99}]'
[NetMessageDispatcher]::Instance.Dispatch('item_catalog_result',
    (Item-Result $duplicateSequence 97 $duplicateItems))
Assert-True ($duplicate -and $runtime.ItemCatalog.catalogVersion -eq 96) `
    'Duplicate item IDs must not partially replace the full catalog.'
$nullItems = $runtime.RequestItemCatalog()
$nullSequence = ($network.Sent[-1] | ConvertFrom-Json).requestSequence
[NetMessageDispatcher]::Instance.Dispatch('item_catalog_result',
    (Item-Result $nullSequence 97 'null'))
Assert-True ($nullItems -and $runtime.ItemCatalog.catalogVersion -eq 96) `
    'A successful envelope without an item array is not a valid full catalog.'

$itemsWithCurrency = '[{"itemId":"gold_coin","kind":"currency","maxStack":0},' +
    $items.Substring(1)
$currencyQuery = $runtime.RequestItemCatalog()
$currencyRequest = $network.Sent[-1] | ConvertFrom-Json
Assert-True ($currencyQuery -and $currencyRequest.knownCatalogVersion -eq 96) `
    'A rejected full catalog must leave the previous known version intact.'
[NetMessageDispatcher]::Instance.Dispatch('item_catalog_result',
    (Item-Result $currencyRequest.requestSequence 97 $itemsWithCurrency))
$currencyItem = $null
Assert-True ($runtime.ItemCatalog.catalogVersion -eq 97 -and
    $runtime.ItemCatalog.items.Length -eq 4 -and
    $runtime.TryGetItem('gold_coin', [ref]$currencyItem) -and
    $currencyItem.maxStack -eq 0 -and
    $runtime.TryResolveTechniqueBook('book', [ref]$resolvedBook,
        [ref]$resolvedTechnique)) `
    'A zero-stack currency must not discard the full catalog or block book linkage.'

$invalidBookQuery = $runtime.RequestItemCatalog()
$invalidBookSequence = ($network.Sent[-1] | ConvertFrom-Json).requestSequence
$invalidBookItems = $itemsWithCurrency.Replace('"maxStack":99',
    '"maxStack":0')
[NetMessageDispatcher]::Instance.Dispatch('item_catalog_result',
    (Item-Result $invalidBookSequence 98 $invalidBookItems))
Assert-True ($invalidBookQuery -and $runtime.ItemCatalog.catalogVersion -eq 97) `
    'A non-currency entry with zero maxStack must not replace the valid catalog.'

$negativeCurrencyQuery = $runtime.RequestItemCatalog()
$negativeCurrencyRequest = $network.Sent[-1] | ConvertFrom-Json
$negativeCurrencyItems = $itemsWithCurrency.Replace(
    '"kind":"currency","maxStack":0', '"kind":"currency","maxStack":-1')
[NetMessageDispatcher]::Instance.Dispatch('item_catalog_result',
    (Item-Result $negativeCurrencyRequest.requestSequence 98 $negativeCurrencyItems))
Assert-True ($negativeCurrencyQuery -and
    $negativeCurrencyRequest.knownCatalogVersion -eq 97 -and
    $runtime.ItemCatalog.catalogVersion -eq 97) `
    'Negative currency maxStack must still be rejected without clearing the cache.'

$network.ConnectionEpoch = 3
$owner.CultivationOperationSessionId = [Guid]::NewGuid().ToString('D')
$runtime.BeginSession($owner, 51, $owner.CultivationOperationSessionId)
Assert-True ($null -eq $runtime.ItemCatalog -and
    -not $runtime.IsConfigurationReady) `
    'Re-entry must discard prior connection item definitions.'
$network.Sent.Clear()
$runtime.Refresh()
Assert-True (($network.Sent[2] | ConvertFrom-Json).knownCatalogVersion -eq 0) `
    'A new connection must query with known version zero.'
$newItemSequence = ($network.Sent[2] | ConvertFrom-Json).requestSequence
[NetMessageDispatcher]::Instance.Dispatch('item_catalog_result',
    (Item-Result $newItemSequence 1 $items))
Assert-True ($runtime.ItemCatalog.catalogVersion -eq 1) `
    'First valid full response on a new connection may have a lower version.'

$recipe = '[{"itemId":"danfang_demo","kind":"recipebook","maxStack":99,' +
    '"learnRecipeId":"recipe_demo","availability":"available"}]'
$material = '[{"itemId":"cailiao_demo","kind":"material","maxStack":99,' +
    '"attributes":[{"otherModuleField":12}]}]'
[UnityEngine.Time]::unscaledTime = 50
Assert-True ($runtime.RequestItemCatalog()) 'Start a batched catalog request.'
$batchQuery = $network.Sent[-1] | ConvertFrom-Json
$batchSequence = $batchQuery.requestSequence
Assert-True ($batchQuery.supportsBatches -eq $true -and
    $batchQuery.knownCatalogVersion -eq 1) `
    'Refresh requests must keep the current version and advertise batching.'
[UnityEngine.Time]::unscaledTime = 57
$secondBatch = Item-BatchResult $batchSequence 5 35 1 2 $material
[NetMessageDispatcher]::Instance.Dispatch('item_catalog_result', $secondBatch)
[NetMessageDispatcher]::Instance.Dispatch('item_catalog_result',
    (Item-BatchResult ($batchSequence + 1) 5 35 0 2 $recipe))
$cachedItem = $null
Assert-True ($runtime.ItemCatalog.catalogVersion -eq 1 -and
    -not $runtime.TryGetItem('cailiao_demo', [ref]$cachedItem)) `
    'An out-of-order packet and another sequence must not publish partial data.'
[UnityEngine.Time]::unscaledTime = 61
$update.Invoke($runtime, @()) | Out-Null
Assert-True (-not $runtime.RequestItemCatalog()) `
    'A valid new packet must extend the inactivity timeout for the current request.'
[NetMessageDispatcher]::Instance.Dispatch('item_catalog_result', $secondBatch)
[NetMessageDispatcher]::Instance.Dispatch('item_catalog_result',
    (Item-BatchResult $batchSequence 5 35 0 2 $recipe))
$mergedJson = $runtime.ItemCatalog.rawJson | ConvertFrom-Json
Assert-True ($runtime.ItemCatalog.catalogVersion -eq 5 -and
    $runtime.ItemCatalog.lifecycleRevision -eq 35 -and
    $runtime.ItemCatalog.items.Length -eq 2 -and
    $runtime.ItemCatalog.items[0].learnRecipeId -eq 'recipe_demo' -and
    $runtime.ItemCatalog.items[1].itemId -eq 'cailiao_demo' -and
    $mergedJson.items[0].availability -eq 'available' -and
    $mergedJson.items[1].attributes[0].otherModuleField -eq 12) `
    'All packets must merge once in zero-based order and retain item metadata.'

$badBatches = @(
    @{ Version = 7; Lifecycle = 36; Count = 2; Index = 1; Items = $material }
    @{ Version = 6; Lifecycle = 37; Count = 2; Index = 1; Items = $material }
    @{ Version = 6; Lifecycle = 36; Count = 3; Index = 1; Items = $material }
    @{ Version = 6; Lifecycle = 36; Count = 2; Index = 2; Items = $material }
    @{ Version = 6; Lifecycle = 36; Count = 2; Index = 0; Items = $material }
    @{ Version = 6; Lifecycle = 36; Count = 2; Index = 1; Items = $recipe }
)
foreach ($badBatch in $badBatches) {
    Assert-True ($runtime.RequestItemCatalog()) 'A failed batch must allow a new query.'
    $sequence = ($network.Sent[-1] | ConvertFrom-Json).requestSequence
    [NetMessageDispatcher]::Instance.Dispatch('item_catalog_result',
        (Item-BatchResult $sequence 6 36 0 2 $recipe))
    $bad = Item-BatchResult $sequence $badBatch.Version $badBatch.Lifecycle `
        $badBatch.Index $badBatch.Count $badBatch.Items
    [NetMessageDispatcher]::Instance.Dispatch('item_catalog_result', $bad)
    Assert-True ($runtime.ItemCatalog.catalogVersion -eq 5 -and
        $runtime.ItemCatalog.items.Length -eq 2) `
        'Conflicting metadata, packet indexes, or duplicate IDs must leave the last good catalog intact.'
}

foreach ($field in 'catalogVersion', 'lifecycleRevision', 'batchIndex') {
    Assert-True ($runtime.RequestItemCatalog()) 'Start a query to check required batch fields.'
    $sequence = ($network.Sent[-1] | ConvertFrom-Json).requestSequence
    $bad = (Item-BatchResult $sequence 6 36 0 2 $recipe) -replace
        ('\s*,"' + $field + '":\d+'), ''
    [NetMessageDispatcher]::Instance.Dispatch('item_catalog_result', $bad)
    [NetMessageDispatcher]::Instance.Dispatch('item_catalog_result',
        (Item-BatchResult $sequence 6 36 1 2 $material))
    Assert-True ($runtime.ItemCatalog.catalogVersion -eq 5) `
        'A missing batch field must not be interpreted as a default value.'
}

[UnityEngine.Time]::unscaledTime = 70
Assert-True ($runtime.RequestItemCatalog()) 'Start a request that will lose a packet.'
$timedOutSequence = ($network.Sent[-1] | ConvertFrom-Json).requestSequence
$incomplete = Item-BatchResult $timedOutSequence 6 36 0 2 $recipe
[UnityEngine.Time]::unscaledTime = 71
[NetMessageDispatcher]::Instance.Dispatch('item_catalog_result', $incomplete)
[UnityEngine.Time]::unscaledTime = 79
[NetMessageDispatcher]::Instance.Dispatch('item_catalog_result', $incomplete)
[UnityEngine.Time]::unscaledTime = 81
$update.Invoke($runtime, @()) | Out-Null
Assert-True ($runtime.RequestItemCatalog()) `
    'Identical retransmissions must not extend a request without progress.'
$retrySequence = ($network.Sent[-1] | ConvertFrom-Json).requestSequence
[NetMessageDispatcher]::Instance.Dispatch('item_catalog_result',
    (Item-BatchResult $timedOutSequence 6 36 1 2 $material))
Assert-True ($runtime.ItemCatalog.catalogVersion -eq 5) `
    'Late packets from a timed-out request must not reach the new request.'
[NetMessageDispatcher]::Instance.Dispatch('item_catalog_result',
    (Item-BatchResult $retrySequence 6 36 0 1 $material))
Assert-True ($runtime.ItemCatalog.catalogVersion -eq 6 -and
    $runtime.ItemCatalog.items.Length -eq 1 -and
    $runtime.ItemCatalog.items[0].itemId -eq 'cailiao_demo') `
    'The negotiated single-packet reply must replace the catalog after a retry.'
function Item-Queries {
    $network.Sent | ForEach-Object { $_ | ConvertFrom-Json } |
        Where-Object msgId -eq 'item_catalog'
}

function Begin-CatalogSession([int]$epoch) {
    $network.ConnectionEpoch = $epoch
    $owner.CultivationOperationSessionId = [Guid]::NewGuid().ToString('D')
    $runtime.BeginSession($owner, 51, $owner.CultivationOperationSessionId)
    [NetMessageDispatcher]::Instance.Dispatch('cultivation_snapshot_result', $freshSnapshot)
    $network.Sent.Clear()
    $runtime.Refresh()
    $initial = @(Item-Queries)[0]
    [NetMessageDispatcher]::Instance.Dispatch('item_catalog_result',
        (Item-Result $initial.requestSequence 100 $material))
    $network.Sent.Clear()
}

[UnityEngine.Time]::unscaledTime = 100
Begin-CatalogSession 4
$blueprint = '[{"itemId":"tp_008","kind":"forgingblueprint","maxStack":99,' +
    '"learnForgingRecipeId":"zb_008_item"}]'
Assert-True ($runtime.RequestItemCatalog()) 'Start an ordinary catalog refresh.'
$sequence = @(Item-Queries)[0].requestSequence
$runtime.QueueMissingForgingBlueprint('zb_008_item')
[NetMessageDispatcher]::Instance.Dispatch('item_catalog_result',
    (Item-BatchResult $sequence 101 35 0 2 $material))
$update.Invoke($runtime, @()) | Out-Null
Assert-True (@(Item-Queries).Count -eq 1) 'Wait for all parts of the in-flight refresh.'
[NetMessageDispatcher]::Instance.Dispatch('item_catalog_result',
    (Item-BatchResult $sequence 101 35 1 2 $blueprint))
$update.Invoke($runtime, @()) | Out-Null
Assert-True (@(Item-Queries).Count -eq 1 -and
    $runtime.ItemCatalog.items[1].learnForgingRecipeId -eq 'zb_008_item') `
    'An ordinary refresh that provides the association must satisfy the queued request.'

$network.Sent.Clear()
Assert-True ($runtime.RequestItemCatalog()) 'Start a refresh with an outdated catalog.'
$sequence = @(Item-Queries)[0].requestSequence
$runtime.QueueMissingForgingBlueprint('zb_021_item')
$runtime.QueueMissingForgingBlueprint('zb_022_item')
$runtime.QueueMissingForgingBlueprint('zb_021_item')
[NetMessageDispatcher]::Instance.Dispatch('item_catalog_result',
    (Item-Result $sequence 101 $blueprint))
$update.Invoke($runtime, @()) | Out-Null
$queries = @(Item-Queries)
Assert-True ($queries.Count -eq 2 -and $queries[1].knownCatalogVersion -eq 101 -and
    $queries[1].supportsBatches) 'Multiple unresolved associations must share one refresh.'
$newBlueprint = $blueprint.Replace('tp_008', 'tp_022').Replace('zb_008_item', 'zb_022_item')
[NetMessageDispatcher]::Instance.Dispatch('item_catalog_result',
    (Item-Result $queries[1].requestSequence 102 $newBlueprint))
$runtime.QueueMissingForgingBlueprint('zb_021_item')
$update.Invoke($runtime, @()) | Out-Null
Assert-True (@(Item-Queries).Count -eq 2 -and
    $runtime.ItemCatalog.items[0].learnForgingRecipeId -eq 'zb_022_item') `
    'Publish the refreshed association once and leave the other unavailable row as a placeholder.'

$network.Sent.Clear()
$runtime.QueueMissingForgingBlueprint('zb_030_item')
$update.Invoke($runtime, @()) | Out-Null
[UnityEngine.Time]::unscaledTime = 111
$update.Invoke($runtime, @()) | Out-Null
$runtime.QueueMissingForgingBlueprint('zb_030_item')
[UnityEngine.Time]::unscaledTime = 114
$update.Invoke($runtime, @()) | Out-Null
Assert-True (@(Item-Queries).Count -eq 1 -and $runtime.ItemCatalog.catalogVersion -eq 102) `
    'A supplemental timeout must leave the last accepted catalog and must not schedule ordinary retries.'

Begin-CatalogSession 5
$runtime.QueueMissingForgingBlueprint('zb_030_item')
$update.Invoke($runtime, @()) | Out-Null
$sequence = @(Item-Queries)[0].requestSequence
[NetMessageDispatcher]::Instance.Dispatch('item_catalog_result',
    (Item-Result $sequence 100 $material $false))
$runtime.QueueMissingForgingBlueprint('zb_030_item')
[UnityEngine.Time]::unscaledTime = 117
$update.Invoke($runtime, @()) | Out-Null
Assert-True (@(Item-Queries).Count -eq 1 -and $runtime.ItemCatalog.catalogVersion -eq 100) `
    'A new connection may retry a previously missing association, but a rejection cannot start a loop.'

$equipment = '[{"itemId":"helm_item","kind":"equipment","maxStack":1,' +
    '"equipmentId":"helm_template","availability":"off_shelf",' +
    '"requiredJingjieId":"jingjie_qi","requiredJingjieStage":2,' +
    '"requiredAttack":10,"requiredMagicAttack":20,' +
    '"attributes":[{"attributeId":"max_hp","value":60},' +
    '{"attributeId":"crit_rate_bps","value":250}],"effects":[]}]'
$potion = '[{"itemId":"potion","kind":"pill","maxStack":99,' +
    '"effects":[{"type":"restore_hp_max_bps","amount":1500,"valueMode":"percent"}]}]'
$previousCatalog = $runtime.ItemCatalog
Assert-True ($runtime.RequestItemCatalog()) 'Request the full item stat catalog.'
$sequence = ($network.Sent[-1] | ConvertFrom-Json).requestSequence
[NetMessageDispatcher]::Instance.Dispatch('item_catalog_result',
    (Item-BatchResult $sequence 101 36 1 2 $equipment))
$byEquipmentId = $null
Assert-True ([object]::ReferenceEquals($runtime.ItemCatalog, $previousCatalog) -and
    -not $runtime.TryGetItemByEquipmentId('helm_template', [ref]$byEquipmentId)) `
    'A partial refresh cannot replace either lookup.'
[NetMessageDispatcher]::Instance.Dispatch('item_catalog_result',
    (Item-BatchResult $sequence 101 36 0 2 $potion))
$byItemId = $null
$cachedPotion = $null
Assert-True ($runtime.IsItemCatalogReady -and
    $runtime.TryGetItem('helm_item', [ref]$byItemId) -and
    $runtime.TryGetItemByEquipmentId('helm_template', [ref]$byEquipmentId) -and
    [object]::ReferenceEquals($byItemId, $byEquipmentId) -and
    $byEquipmentId.attributes[0].attributeId -ceq 'max_hp' -and
    $byEquipmentId.attributes[0].value -eq 60 -and
    $byEquipmentId.attributes[1].attributeId -ceq 'crit_rate_bps' -and
    $byEquipmentId.attributes[1].value -eq 250 -and
    $byEquipmentId.availability -ceq 'off_shelf' -and
    $byEquipmentId.requiredJingjieId -ceq 'jingjie_qi' -and
    $byEquipmentId.requiredJingjieStage -eq 2 -and
    $byEquipmentId.requiredAttack -eq 10 -and
    $byEquipmentId.requiredMagicAttack -eq 20 -and
    $runtime.TryGetItem('potion', [ref]$cachedPotion) -and
    $cachedPotion.effects[0].type -ceq 'restore_hp_max_bps' -and
    $cachedPotion.effects[0].amount -eq 1500 -and
    $cachedPotion.effects[0].valueMode -ceq 'percent') `
    'A complete response must expose the same typed stats by item and equipment ID and retain effects.'

$duplicateEquipmentId = $equipment.TrimEnd(']') + ',' +
    $equipment.TrimStart('[').Replace('helm_item', 'conflicting_helm_item')
Assert-True ($runtime.RequestItemCatalog()) 'Request a replacement catalog.'
$sequence = ($network.Sent[-1] | ConvertFrom-Json).requestSequence
[NetMessageDispatcher]::Instance.Dispatch('item_catalog_result',
    (Item-BatchResult $sequence 102 37 0 1 $duplicateEquipmentId))
Assert-True ($runtime.ItemCatalog.catalogVersion -eq 101 -and
    $runtime.TryGetItemByEquipmentId('helm_template', [ref]$byEquipmentId) -and
    $byEquipmentId.itemId -ceq 'helm_item') 'Ambiguous equipment IDs cannot overwrite the accepted cache.'

Assert-True ($runtime.RequestItemCatalog()) 'Request a catalog that removes the equipment.'
$sequence = ($network.Sent[-1] | ConvertFrom-Json).requestSequence
[NetMessageDispatcher]::Instance.Dispatch('item_catalog_result',
    (Item-Result $sequence 102 $potion))
Assert-True (-not $runtime.TryGetItemByEquipmentId('helm_template', [ref]$byEquipmentId) -and
    -not $runtime.TryGetItem('helm_item', [ref]$byItemId)) `
    'An accepted full replacement removes equipment from both indexes.'

Assert-True ($runtime.RequestItemCatalog()) 'Leave an item catalog request in flight before reconnect.'
$oldSequence = ($network.Sent[-1] | ConvertFrom-Json).requestSequence
$network.Disconnect()
$network.ConnectionEpoch++
[NetMessageDispatcher]::Instance.Dispatch('item_catalog_result',
    (Item-Result $oldSequence 103 $equipment))
Assert-True (-not $runtime.IsItemCatalogReady -and $null -eq $runtime.ItemCatalog -and
    -not $runtime.TryGetItemByEquipmentId('helm_template', [ref]$byEquipmentId)) `
    'Disconnect clears both indexes and ignores an old result.'
$owner.LocalCharacterDbId = 52
$owner.CultivationOperationSessionId = [Guid]::NewGuid().ToString('D')
$runtime.BeginSession($owner, 52, $owner.CultivationOperationSessionId)
Assert-True ($runtime.RequestItemCatalog()) 'The new character can load a fresh catalog.'
$sequence = ($network.Sent[-1] | ConvertFrom-Json).requestSequence
[NetMessageDispatcher]::Instance.Dispatch('item_catalog_result',
    (Item-Result $sequence 1 $equipment))
Assert-True ($runtime.IsItemCatalogReady -and $runtime.ItemCatalog.catalogVersion -eq 1 -and
    $runtime.TryGetItemByEquipmentId('helm_template', [ref]$byEquipmentId)) `
    'A fresh character can accept its own catalog independently of earlier versions.'

Write-Output 'PASS: item catalog batching, typed item and equipment stats, session isolation, book lookup and bounded blueprint refresh.'
