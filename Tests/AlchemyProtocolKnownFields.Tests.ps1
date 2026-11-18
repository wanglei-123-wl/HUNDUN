$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$messages = Get-Content (Join-Path $root 'Assets/Scripts/Alchemy/Protocol/AlchemyMessages.cs') -Raw
$reader = Get-Content (Join-Path $root 'Assets/Scripts/Cultivation/Protocol/RefiningSnapshotWireState.cs') -Raw
$stateSource = Get-Content (Join-Path $root 'Assets/Scripts/Alchemy/Protocol/AlchemySnapshotState.cs') -Raw
$body = ($reader + "`n" + $messages + "`n" + $stateSource) -replace
    '(?m)^using [^\r\n]+;\r?\n', ''
$header = @'
using System;
using System.Globalization;
using System.Text;
using Game.Alchemy.Protocol;
using Game.Cultivation.Protocol;
using Game.Inventory.Protocol.V2;
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
'@
Add-Type -TypeDefinition ($header + "`n" + $body) -IgnoreWarnings | Out-Null

function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
}
function Check-Task($json, $present, $snapshotNull, $taskPresent, $taskNull) {
    $hasSnapshot = $false
    $isSnapshotNull = $false
    $hasTask = $false
    $isTaskNull = $false
    $valid = [Game.Alchemy.Protocol.AlchemySnapshotWireState]::TryRead($json,
        [ref]$hasSnapshot, [ref]$isSnapshotNull, [ref]$hasTask, [ref]$isTaskNull)
    Assert-True ($valid -and $hasSnapshot -eq $present -and
        $isSnapshotNull -eq $snapshotNull -and $hasTask -eq $taskPresent -and
        $isTaskNull -eq $taskNull) "Incorrect currentTask wire state: $json"
}

$options = [System.Text.Json.JsonSerializerOptions]@{ IncludeFields = $true }
$session = [Guid]::NewGuid().ToString('D')
$requestId = [Guid]::NewGuid().ToString('D')
$stackId = [long]9007199254740993
$catalog = [Game.Alchemy.Protocol.AlchemyCatalogRequestDto]::new(1)
$catalogJson = [System.Text.Json.JsonSerializer]::Serialize($catalog, $options)
Assert-True ($catalogJson -match '"msgId":"alchemy_catalog"' -and
    $catalogJson -match '"type":"alchemy_catalog"' -and
    $catalogJson -match '"requestSequence":1' -and
    $catalogJson -match '"supportsCatalogBatches":true') 'Catalog query must declare batch support.'

$snapshot = [Game.Alchemy.Protocol.AlchemySnapshotRequestDto]::new(2)
$snapshotJson = [System.Text.Json.JsonSerializer]::Serialize($snapshot, $options)
Assert-True ($snapshotJson -match '"msgId":"alchemy_snapshot"' -and
    $snapshotJson -match '"requestSequence":2' -and
    $snapshotJson -notmatch 'supportsCatalogBatches') 'Snapshot query must not inherit catalog-only fields.'

$materials = [Game.Alchemy.Protocol.AlchemySelectedMaterialDto[]]@(
    [Game.Alchemy.Protocol.AlchemySelectedMaterialDto]::new($stackId, 4),
    [Game.Alchemy.Protocol.AlchemySelectedMaterialDto]::new(5011, 7))
$start = [Game.Alchemy.Protocol.AlchemyStartRequestDto]::new(
    $session, 3, $requestId, 12, 21, 'hui_chun_dan', $materials)
$startJson = [System.Text.Json.JsonSerializer]::Serialize($start, $options)
$startWire = [System.Text.Json.JsonDocument]::Parse($startJson)
try {
    $rootElement = $startWire.RootElement
    $sentMaterials = $rootElement.GetProperty('materials')
    Assert-True ($rootElement.GetProperty('msgId').GetString() -eq 'alchemy_start' -and
        $rootElement.GetProperty('type').GetString() -eq 'alchemy_start' -and
        $rootElement.GetProperty('alchemyOperationSessionId').GetString() -eq $session -and
        $rootElement.GetProperty('requestSequence').GetInt64() -eq 3 -and
        $rootElement.GetProperty('clientRequestId').GetString() -eq $requestId -and
        $rootElement.GetProperty('configurationRevision').GetInt64() -eq 12 -and
        $rootElement.GetProperty('expectedInventoryRevision').GetInt64() -eq 21 -and
        $rootElement.GetProperty('recipeId').GetString() -eq 'hui_chun_dan' -and
        $sentMaterials.GetArrayLength() -eq 2 -and
        $sentMaterials[0].GetProperty('stackId').GetInt64() -eq $stackId -and
        $sentMaterials[0].GetProperty('stackId').GetRawText() -eq '9007199254740993' -and
        $sentMaterials[0].GetProperty('count').GetInt32() -eq 4 -and
        $sentMaterials[1].GetProperty('count').GetInt32() -eq 7 -and
        $startJson -notmatch '"itemId"|"characterDbId"|"isBound"') `
        'Start request must preserve exact selected stacks and avoid client-owned asset fields.'
}
finally {
    $startWire.Dispose()
}

$claim = [Game.Alchemy.Protocol.AlchemyClaimRequestDto]::new(
    $session, 4, $requestId, 22, 'task-1')
$claimJson = [System.Text.Json.JsonSerializer]::Serialize($claim, $options)
Assert-True ($claimJson -match '"msgId":"alchemy_claim"' -and
    $claimJson -match '"type":"alchemy_claim"' -and
    $claimJson -match '"taskId":"task-1"' -and
    $claimJson -match '"expectedInventoryRevision":22' -and
    $claimJson -notmatch '"materials"|"configurationRevision"') `
    'Claim request must not send start-only fields.'

$learn = [Game.Alchemy.Protocol.AlchemyLearnRequestDto]::new(
    $session, 5, $requestId, 12, 21, 'hui_chun_dan', $stackId)
$learnJson = [System.Text.Json.JsonSerializer]::Serialize($learn, $options)
Assert-True ($learnJson -match '"msgId":"alchemy_learn"' -and
    $learnJson -match '"recipeId":"hui_chun_dan"' -and
    $learnJson -match '"stackId":9007199254740993' -and
    $learnJson -notmatch '"materials"') 'Learn sends the exact book stack without material selection.'
$recipe = [System.Text.Json.JsonSerializer]::Deserialize[
    Game.Alchemy.Protocol.AlchemyRecipeDto](
    '{"recipeId":"r1","requiredJingjieId":"qi_refining",' +
    '"requiredJingjieStage":2,"materials":[{"itemId":"ling_shi","count":7}],' +
    '"outputs":[{"quality":1,"itemId":"pill","count":3,"weight":10000}]}',
    $options)
Assert-True ($recipe.requiredJingjieStage -eq 2 -and
    $recipe.materials[0].count -eq 7 -and
    $recipe.outputs[0].itemId -eq 'pill') `
    'Recipe and output fields must retain their distinct item IDs and counts.'

Check-Task '{"snapshot":null}' $true $true $false $false
Check-Task '{"snapshot":{"currentTask":null,"lastCompletedTask":{"taskId":"old"}}}' `
    $true $false $true $true
Check-Task '{"snapshot":{"currentTask":{"taskId":"current"},"lastCompletedTask":null}}' `
    $true $false $true $false
Check-Task '{"snapshot":{}}' $true $false $false $false
Check-Task '{"success":false}' $false $false $false $false
$present = $false
$succeeded = [Nullable[bool]]$null
Assert-True ([Game.Alchemy.Protocol.AlchemySnapshotWireState]::TryReadSucceeded(
    '{"snapshot":{"currentTask":{"succeeded":null}}}',
    [ref]$present, [ref]$succeeded) -and $present -and
    -not $succeeded.HasValue) 'Active result must preserve explicit null.'
Assert-True ([Game.Alchemy.Protocol.AlchemySnapshotWireState]::TryReadSucceeded(
    '{"snapshot":{"currentTask":{"succeeded":false}}}',
    [ref]$present, [ref]$succeeded) -and $present -and
    $succeeded -eq $false) `
    'Ready failure must remain distinct from an unknown active result.'
Assert-True (-not [Game.Alchemy.Protocol.AlchemySnapshotWireState]::TryReadSucceeded(
    '{"snapshot":{"currentTask":{"succeeded":"false"}}}',
    [ref]$present, [ref]$succeeded)) `
    'A string must not stand in for a boolean outcome.'

$hasSnapshot = $false
$snapshotIsNull = $false
$hasTask = $false
$taskIsNull = $false
Assert-True (-not [Game.Alchemy.Protocol.AlchemySnapshotWireState]::TryRead(
    '{"snapshot":{"currentTask":null,"currentTask":{}}}',
    [ref]$hasSnapshot, [ref]$snapshotIsNull, [ref]$hasTask, [ref]$taskIsNull)) `
    'Duplicate currentTask fields must be rejected.'
Assert-True ([Game.Cultivation.Protocol.RefiningSnapshotWireState]::TryRead(
    '{"snapshot":{"activeTask":null}}',
    [ref]$hasSnapshot, [ref]$snapshotIsNull, [ref]$hasTask, [ref]$taskIsNull) -and
    $hasSnapshot -and $hasTask -and $taskIsNull) `
    'Existing refining activeTask semantics must remain intact.'

$state = [Game.Alchemy.Protocol.AlchemySnapshotState]::new()
$activeJson = '{"snapshot":{"characterDbId":42,"revision":4,"learnedRecipeIds":["r1"],"currentTask":{"taskId":"task-1","recipeId":"r1","status":"active","succeeded":null,"startedAtUnixMilliseconds":1000,"endsAtUnixMilliseconds":2000}}}'
$deserializableActiveJson = $activeJson.Replace(
    '"succeeded":null', '"succeeded":false')
$active = [System.Text.Json.JsonSerializer]::Deserialize[
    Game.Alchemy.Protocol.AlchemySnapshotResultDto]($deserializableActiveJson, $options)
Assert-True ($state.Apply($activeJson, $active.snapshot, 42, 1200) -eq
    [Game.Alchemy.Protocol.AlchemySnapshotApplyResult]::Applied -and
    $state.HasCurrentTask -and $state.Snapshot.currentTask.taskId -eq 'task-1' -and
    $null -eq $state.Snapshot.currentTask.ParsedSucceeded) `
    'A valid active task must occupy the furnace.'

$noSnapshot = '{"snapshot":null}'
Assert-True ($state.Apply($noSnapshot, $null, 42, 1300) -eq
    [Game.Alchemy.Protocol.AlchemySnapshotApplyResult]::NotProvided -and
    $state.HasCurrentTask) 'A missing snapshot must not clear the furnace.'

$staleJson = '{"snapshot":{"characterDbId":42,"revision":3,"learnedRecipeIds":[],"currentTask":null}}'
$stale = [System.Text.Json.JsonSerializer]::Deserialize[
    Game.Alchemy.Protocol.AlchemySnapshotResultDto]($staleJson, $options)
Assert-True ($state.Apply($staleJson, $stale.snapshot, 42, 1400) -eq
    [Game.Alchemy.Protocol.AlchemySnapshotApplyResult]::Stale -and
    $state.HasCurrentTask) 'An older empty task must not clear a newer active task.'

$sameJson = '{"snapshot":{"characterDbId":42,"revision":4,"learnedRecipeIds":["r1"],"currentTask":null}}'
$same = [System.Text.Json.JsonSerializer]::Deserialize[
    Game.Alchemy.Protocol.AlchemySnapshotResultDto]($sameJson, $options)
Assert-True ($state.Apply($sameJson, $same.snapshot, 42, 1200) -eq
    [Game.Alchemy.Protocol.AlchemySnapshotApplyResult]::Invalid -and
    $state.HasCurrentTask) `
    'Conflicting tasks at the same revision and server time need recovery.'
Assert-True ($state.Apply($sameJson, $same.snapshot, 42, 1100) -eq
    [Game.Alchemy.Protocol.AlchemySnapshotApplyResult]::Stale -and
    $state.HasCurrentTask) `
    'An older same-revision empty task must not release a newer furnace.'
$same.snapshot.currentTask = [Game.Alchemy.Protocol.AlchemyTaskDto]::new()
Assert-True ($state.Apply($sameJson, $same.snapshot, 42, 1500) -eq
    [Game.Alchemy.Protocol.AlchemySnapshotApplyResult]::Applied -and
    -not $state.HasCurrentTask -and $null -eq $state.Snapshot.currentTask -and
    $state.LastServerTime -eq 1500) `
    'A newer explicit empty task must release the furnace at the same revision.'
Assert-True ($state.Apply($activeJson, $active.snapshot, 42, 1600) -eq
    [Game.Alchemy.Protocol.AlchemySnapshotApplyResult]::Invalid -and
    -not $state.HasCurrentTask -and $state.LastServerTime -eq 1500) `
    'A contradictory same-revision task cannot reoccupy an emptied furnace.'
Assert-True ($state.Apply($sameJson, $same.snapshot, 42, 1700) -eq
    [Game.Alchemy.Protocol.AlchemySnapshotApplyResult]::Unchanged -and
    -not $state.HasCurrentTask -and $state.LastServerTime -eq 1700) `
    'An identical same-revision snapshot may calibrate server time.'

$invalidJson = '{"snapshot":{"characterDbId":42,"revision":5,"learnedRecipeIds":[],"currentTask":{}}}'
$invalid = [System.Text.Json.JsonSerializer]::Deserialize[
    Game.Alchemy.Protocol.AlchemySnapshotResultDto]($invalidJson, $options)
Assert-True ($state.Apply($invalidJson, $invalid.snapshot, 42, 1600) -eq
    [Game.Alchemy.Protocol.AlchemySnapshotApplyResult]::Invalid -and
    -not $state.HasCurrentTask) 'A default empty task must not occupy or release the furnace.'

$clearJson = '{"snapshot":{"characterDbId":42,"revision":5,"learnedRecipeIds":["r1"],"currentTask":null}}'
$clear = [System.Text.Json.JsonSerializer]::Deserialize[
    Game.Alchemy.Protocol.AlchemySnapshotResultDto]($clearJson, $options)
$clear.snapshot.currentTask = [Game.Alchemy.Protocol.AlchemyTaskDto]::new()
Assert-True ($state.Apply($clearJson, $clear.snapshot, 42, 1700) -eq
    [Game.Alchemy.Protocol.AlchemySnapshotApplyResult]::Applied -and
    -not $state.HasCurrentTask -and $null -eq $state.Snapshot.currentTask -and
    $state.Snapshot.learnedRecipeIds[0] -eq 'r1') `
    'An explicit currentTask:null must override a deserialized empty object.'

$foreign = [System.Text.Json.JsonSerializer]::Deserialize[
    Game.Alchemy.Protocol.AlchemySnapshotResultDto]($deserializableActiveJson, $options)
Assert-True ($state.Apply($activeJson, $foreign.snapshot, 9, 1800) -eq
    [Game.Alchemy.Protocol.AlchemySnapshotApplyResult]::Invalid -and
    -not $state.HasCurrentTask) 'Other characters must not change the active task.'
$state.Reset()
Assert-True ($null -eq $state.Snapshot -and $state.LastServerTime -eq 0) `
    'A new operation session must begin without the previous task cache.'

$readyJson = '{"snapshot":{"characterDbId":42,"revision":6,"learnedRecipeIds":[],"currentTask":{"taskId":"task-2","recipeId":"r1","status":"ready","succeeded":false,"startedAtUnixMilliseconds":1000,"endsAtUnixMilliseconds":2000}}}'
$ready = [System.Text.Json.JsonSerializer]::Deserialize[
    Game.Alchemy.Protocol.AlchemySnapshotResultDto]($readyJson, $options)
Assert-True ($state.Apply($readyJson, $ready.snapshot, 42, 2200) -eq
    [Game.Alchemy.Protocol.AlchemySnapshotApplyResult]::Applied -and
    $state.Snapshot.currentTask.ParsedSucceeded -eq $false) `
    'A ready failed furnace remains occupied until claimed.'

Write-Output 'PASS: known alchemy requests, snapshot versioning and explicit currentTask null.'
