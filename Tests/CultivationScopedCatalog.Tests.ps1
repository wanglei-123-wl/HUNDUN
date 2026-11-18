$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'CultivationProtocolStage1.Tests.ps1')

function Send-CatalogPart {
    param([long]$Sequence, [string]$SyncId, [int]$Index, [int]$Count,
        [long]$InventoryRevision, [string[]]$Ids, [object[]]$Grades,
        [object[]]$Techniques, [int]$TotalIds, [int]$TotalGrades, [int]$TotalTechniques)
    $message = @{
        msgId = 'cultivation_catalog_result'; type = 'cultivation_catalog_result'
        requestSequence = $Sequence; success = $true; configurationRevision = 57
        inventoryRevision = $InventoryRevision; cultivationRevision = 0
        catalogSyncId = $SyncId; batchIndex = $Index; batchCount = $Count
        totalCount = $TotalTechniques; totalGradeCount = $TotalGrades
        totalCharacterTechniqueCount = $TotalIds; characterTechniqueIds = @($Ids)
        grades = @($Grades); techniques = @($Techniques); serverTime = 4000
    } | ConvertTo-Json -Depth 10 -Compress
    [NetMessageDispatcher]::Instance.Dispatch('cultivation_catalog_result', $message)
}

function Send-CultivationSnapshot([long]$Sequence) {
    $reply = '{"msgId":"cultivation_snapshot_result","type":"cultivation_snapshot_result",' +
        '"requestSequence":' + $Sequence + ',"success":true,"serverTime":4000,"snapshot":' +
        $baseSnapshot + '}'
    [NetMessageDispatcher]::Instance.Dispatch('cultivation_snapshot_result', $reply)
}

$inventory = [Game.Inventory.Model.V2.InventoryOwnershipSnapshot]::new()
$inventory.CharacterDbId = 51
$inventory.InventoryRevision = 20
[Game.Inventory.InventoryModuleRuntime]::Shared = [Game.Inventory.InventoryModuleRuntime]::new()
[Game.Inventory.InventoryModuleRuntime]::Shared.CurrentSnapshot = $inventory
[UnityEngine.Time]::unscaledTime = 100
Send-CultivationSnapshot 0
[UnityEngine.Time]::unscaledTime = 103
$network.Sent.Clear()
$runtime.Refresh()
$requests = @($network.Sent | ForEach-Object { $_ | ConvertFrom-Json })
Assert-True ($requests.Count -eq 3 -and $requests[0].characterScoped -and
    $requests[0].supportsCatalogBatches -and $requests[0].knownConfigurationRevision -eq 0) `
    'The first cultivation query must advertise scope and batching.'
Send-CultivationSnapshot $requests[1].requestSequence
$itemReply = '{"msgId":"item_catalog_result","type":"item_catalog_result","requestSequence":' +
    $requests[2].requestSequence + ',"success":true,"catalogVersion":940,' +
    '"items":[],"errorCode":"","serverTime":4000}'
[NetMessageDispatcher]::Instance.Dispatch('item_catalog_result', $itemReply)

$grade = @{gradeId = 'heaven'; name = 'Heaven'; order = 1}
$technique = @{techniqueId = 'xuanqing'; gradeId = 'heaven'; name = 'Xuan Qing'
    enabled = $true; levels = @(
        @{level = 0; comprehension = 5; durationSeconds = 0
          referenceComprehension = $null; materials = @()},
        @{level = 1; comprehension = 7; durationSeconds = 60
          referenceComprehension = 0; materials = @()}
    )}
$sequence = $requests[0].requestSequence
$sync = [Guid]::NewGuid().ToString('D')
Send-CatalogPart $sequence $sync 3 3 20 @() @() @($technique) 1 1 1
Send-CatalogPart $sequence $sync 3 3 20 @() @() @($technique) 1 1 1
Send-CatalogPart $sequence $sync 1 3 20 @('xuanqing') @() @() 1 1 1
Assert-True ($null -eq $runtime.Catalog) 'Partial or duplicate batches must not publish.'
Send-CatalogPart $sequence $sync 2 3 20 @() @($grade) @() 1 1 1
Assert-True ($runtime.IsConfigurationReady -and $runtime.Catalog.techniques.Length -eq 1 -and
    $runtime.Catalog.techniques[0].techniqueId -eq 'xuanqing' -and
    -not $runtime.Catalog.techniques[0].levels[0].hasReferenceComprehension -and
    $runtime.Catalog.techniques[0].levels[1].hasReferenceComprehension) `
    'Scope, grades and definitions must merge atomically with nullable level settings.'

$network.Sent.Clear()
$inventory.InventoryRevision = 21
[UnityEngine.Time]::unscaledTime = 106
$update.Invoke($runtime, @()) | Out-Null
[UnityEngine.Time]::unscaledTime = 109
$update.Invoke($runtime, @()) | Out-Null
$requests = @($network.Sent | ForEach-Object { $_ | ConvertFrom-Json })
Assert-True ($requests.Count -eq 2 -and $requests[0].knownConfigurationRevision -eq 57 -and
    $requests[0].knownTechniqueIds -contains 'xuanqing' -and
    $requests[1].msgId -eq 'cultivation_snapshot') `
    'A new inventory revision must refresh scoped techniques and advertise cached definitions.'
Send-CultivationSnapshot $requests[1].requestSequence
Send-CatalogPart $requests[0].requestSequence ([Guid]::NewGuid().ToString('D')) 1 1 21 `
    @('xuanqing') @() @() 1 0 0
Assert-True ($runtime.Catalog.inventoryRevision -eq 21 -and
    $runtime.Catalog.techniques[0].techniqueId -eq 'xuanqing') `
    'A cache-hit reply may omit all grades and technique definitions.'

Assert-True ($runtime.RequestCatalog()) 'Start a query that fails.'
$sequence = ($network.Sent[-1] | ConvertFrom-Json).requestSequence
$failure = '{"msgId":"cultivation_catalog_result","type":"cultivation_catalog_result",' +
    '"requestSequence":' + $sequence + ',"success":false,' +
    '"errorCode":"cultivation_catalog_upgrade_required"}'
[NetMessageDispatcher]::Instance.Dispatch('cultivation_catalog_result', $failure)
$network.Sent.Clear()
[UnityEngine.Time]::unscaledTime = 112
$update.Invoke($runtime, @()) | Out-Null
$requests = @($network.Sent | ForEach-Object { $_ | ConvertFrom-Json })
Assert-True ($requests.Count -eq 2 -and
    $requests[0].msgId -eq 'cultivation_catalog' -and
    $requests[1].msgId -eq 'cultivation_snapshot' -and $runtime.ItemCatalog.catalogVersion -eq 940) `
    'A cultivation query failure must not resync the accepted item catalog.'
Send-CultivationSnapshot $requests[1].requestSequence

$inventory.InventoryRevision = 22
Send-CatalogPart $requests[0].requestSequence ([Guid]::NewGuid().ToString('D')) 1 1 21 `
    @() @() @() 0 0 0
Assert-True ($runtime.Catalog.inventoryRevision -eq 21 -and
    $runtime.Catalog.techniques.Length -eq 1) `
    'A reply older than the current inventory must preserve the accepted scope.'
[UnityEngine.Time]::unscaledTime = 115
$update.Invoke($runtime, @()) | Out-Null
$sequence = ($network.Sent[-2] | ConvertFrom-Json).requestSequence
Send-CatalogPart $sequence ([Guid]::NewGuid().ToString('D')) 1 1 22 @() @() @() 0 0 0
Assert-True ($runtime.Catalog.inventoryRevision -eq 22 -and
    $runtime.Catalog.techniques.Length -eq 0) `
    'An authoritative empty scope must remove techniques no longer held or learned.'

Write-Output 'PASS: scoped cultivation catalog, batch merge, cache and isolated retries.'
