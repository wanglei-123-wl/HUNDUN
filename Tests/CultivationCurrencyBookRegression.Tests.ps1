$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'CultivationProtocolStage1.Tests.ps1')

$techniqueId = 'book_c3b0257abd2ccae0b657b0a5ea37de8c0caea2421039f54d'
$owner.LocalCharacterDbId = 1
$owner.CultivationOperationSessionId = [Guid]::NewGuid().ToString('D')
$network.ConnectionEpoch = 3
$runtime.BeginSession($owner, 1, $owner.CultivationOperationSessionId)

$inventory = [Game.Inventory.InventoryModuleRuntime]::new()
$inventory.CurrentSnapshot =
    [Game.Inventory.Model.V2.InventoryOwnershipSnapshot]::new()
$inventory.CurrentSnapshot.CharacterDbId = 1
$inventory.CurrentSnapshot.InventoryRevision = 15
$bookStack = [Game.Inventory.Model.V2.InventoryStackState]::new()
$bookStack.StackId = 7
$bookStack.ItemId = 'gongfa_yuluxinfa'
$bookStack.Count = 1
$inventory.CurrentSnapshot.Stacks.Add($bookStack)
[Game.Inventory.InventoryModuleRuntime]::Shared = $inventory

$realSnapshot = $automatic | ConvertFrom-Json
$realSnapshot.snapshot.characterDbId = 1
$realSnapshot.snapshot.revision = 4
$realSnapshot.snapshot.configurationRevision = 7
[NetMessageDispatcher]::Instance.Dispatch('cultivation_snapshot_result',
    ($realSnapshot | ConvertTo-Json -Depth 10 -Compress))
$network.Sent.Clear()
$runtime.Refresh()
$queries = @($network.Sent | ForEach-Object { $_ | ConvertFrom-Json })
$catalogQuery = @($queries | Where-Object {
    $_.msgId -eq 'cultivation_catalog' })[-1]
$itemQuery = @($queries | Where-Object {
    $_.msgId -eq 'item_catalog' })[-1]
$realCatalog = $catalog | ConvertFrom-Json
$realCatalog.requestSequence = $catalogQuery.requestSequence
$realCatalog.configurationRevision = 7
$realCatalog.grades[0].gradeId = 'tian'
$realCatalog.techniques[0].gradeId = 'tian'
$realCatalog.techniques[0].techniqueId = $techniqueId
$itemResult = [ordered]@{
    msgId = 'item_catalog_result'
    type = 'item_catalog_result'
    requestSequence = $itemQuery.requestSequence
    success = $true
    catalogVersion = 8
    items = @(
        [ordered]@{ itemId = 'gold_coin'; kind = 'currency'; maxStack = 0 }
        [ordered]@{ itemId = 'gongfa_yuluxinfa'; kind = 'techniquebook'
            maxStack = 1; learnTechniqueId = $techniqueId }
    )
    serverTime = 1800
}
[NetMessageDispatcher]::Instance.Dispatch('cultivation_catalog_result',
    ($realCatalog | ConvertTo-Json -Depth 10 -Compress))
[NetMessageDispatcher]::Instance.Dispatch('item_catalog_result',
    ($itemResult | ConvertTo-Json -Depth 10 -Compress))
$linkedBook = $null
$linkedTechnique = $null
Assert-True ($runtime.IsConfigurationReady -and
    $runtime.ItemCatalog.catalogVersion -eq 8 -and
    $runtime.Snapshot.configurationRevision -eq 7 -and
    $runtime.Snapshot.revision -eq 4 -and
    $runtime.ItemCatalog.items.Length -eq 2 -and
    $runtime.TryResolveTechniqueBook('gongfa_yuluxinfa',
        [ref]$linkedBook, [ref]$linkedTechnique) -and
    $linkedBook.learnTechniqueId -eq $techniqueId -and
    $linkedTechnique.techniqueId -eq $techniqueId -and
    $runtime.CanLearn(7) -and $runtime.TryLearn(7)) `
    'A full catalog with zero-stack currency must still link and learn the owned book.'
$realRequest = $network.Sent[-1] | ConvertFrom-Json
Assert-True ($realRequest.msgId -eq 'cultivation_learn' -and
    $realRequest.stackId -eq 7 -and
    $realRequest.configurationRevision -eq 7 -and
    $realRequest.expectedInventoryRevision -eq 15 -and
    $realRequest.cultivationOperationSessionId -eq
        $owner.CultivationOperationSessionId -and
    -not $realRequest.PSObject.Properties['techniqueId']) `
    'Learning sends the owned stack and independent configuration/inventory revisions.'
$runtime.RequestItemCatalog() | Out-Null
Assert-True (($network.Sent[-1] | ConvertFrom-Json).knownCatalogVersion -eq 8) `
    'The next item query must use the accepted catalog version.'
Write-Output 'PASS: currency catalog with owned technique book learning.'
