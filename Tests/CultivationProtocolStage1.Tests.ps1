$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$files = @(
    'Assets/Scripts/Cultivation/Protocol/CultivationMessages.cs',
    'Assets/Scripts/Cultivation/Protocol/CultivationItemCatalogMessages.cs',
    'Assets/Scripts/Cultivation/Protocol/CultivationItemCatalogBatches.cs',
    'Assets/Scripts/Cultivation/Protocol/CultivationCatalogBatches.cs',
    'Assets/Scripts/Cultivation/Protocol/CultivationCatalogWireState.cs',
    'Assets/Scripts/Cultivation/Protocol/CultivationRequestSequence.cs',
    'Assets/Scripts/Cultivation/Protocol/RefiningSnapshotWireState.cs',
    'Assets/Scripts/Cultivation/Runtime/CultivationSessionRuntime.cs'
)
$sources = foreach ($file in $files) {
    (Get-Content (Join-Path $root $file) -Raw) -replace
        '(?m)^using [^\r\n]+;\r?\n', ''
}
$stubs = @'
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Game.Cultivation.Protocol;
using Game.Cultivation.Runtime;
using Game.Inventory;
using Game.Inventory.Model.V2;
using Game.Inventory.Protocol.V2;
using Newtonsoft.Json.Linq;
using UnityEngine;
public class NetMessageBase { public string msgId; public string type; }
public class ServerErrorMessage { public string requestMsgId; public string errorCode; }
public class GameSession {
    public bool IsWorldReady = true;
    public long LocalCharacterDbId;
    public string CultivationOperationSessionId;
    public int ProgressionRequests;
    public void RequestProgressionSnapshot() { ProgressionRequests++; }
}
public class NetworkManager : MonoBehaviour {
    public bool IsConnected = true;
    public long ConnectionEpoch = 1;
    public readonly List<string> Sent = new();
    public event Action<long,string> OnDisconnectedWithEpoch;
    public bool Send(NetMessageBase value) {
        Sent.Add(JsonUtility.ToJson(value)); return true;
    }
    public void Disconnect() { OnDisconnectedWithEpoch?.Invoke(ConnectionEpoch, "test"); }
}
public class NetMessageDispatcher {
    public static NetMessageDispatcher Instance = new();
    private readonly Dictionary<string,Action<string>> handlers = new();
    public void Register(string id, Action<string> handler) {
        handlers.TryGetValue(id, out var old); handlers[id] = old + handler;
    }
    public void Unregister(string id, Action<string> handler) {
        if (handlers.TryGetValue(id, out var old)) handlers[id] = old - handler;
    }
    public void Dispatch(string id, string json) {
        if (handlers.TryGetValue(id, out var callback)) callback?.Invoke(json);
    }
}
namespace Game.Inventory.Protocol.V2 {
    public class InventoryV2StackChangeDto {
        public long stackId; public string itemId; public int previousCount;
        public int newCount; public bool isBound;
    }
}
namespace Game.Inventory {
    public class InventoryModuleRuntime {
        public static InventoryModuleRuntime Shared;
        public InventoryOwnershipSnapshot CurrentSnapshot;
        public int Reconciliations;
        public int Applied;
        public void RequestReconciliation() { Reconciliations++; }
        public bool ApplyCultivationInventoryChanges(long revision, bool requires,
            Game.Inventory.Protocol.V2.InventoryV2StackChangeDto[] changes)
        {
            Applied++; return true;
        }
        public bool ApplyCultivationStartInventoryChanges(long revision, bool requires,
            Game.Inventory.Protocol.V2.InventoryV2StackChangeDto[] changes)
        {
            Applied++; return true;
        }
    }
}
namespace Game.Inventory.Model.V2 {
    public class InventoryStackState { public long StackId; public string ItemId; public int Count; }
    public class InventoryOwnershipSnapshot {
        public long CharacterDbId;
        public long InventoryRevision;
        public readonly List<InventoryStackState> Stacks = new();
        public bool TryGetItemCount(string itemId, out long count) {
            count = 0;
            foreach (var stack in Stacks)
                if (stack.ItemId == itemId) count += stack.Count;
            return count > 0;
        }
    }
}
namespace UnityEngine {
    [AttributeUsage(AttributeTargets.Class)]
    public class DisallowMultipleComponent : Attribute {}
    [AttributeUsage(AttributeTargets.Field)]
    public class SerializeField : Attribute {}
    public class MonoBehaviour {
        public GameObject gameObject = new();
        public T GetComponent<T>() where T : class => gameObject.GetComponent<T>();
        public T FindObjectOfType<T>() where T : class =>
            Game.Inventory.InventoryModuleRuntime.Shared as T;
    }
    public class GameObject {
        private readonly Dictionary<Type,object> components = new();
        public T GetComponent<T>() where T : class =>
            components.TryGetValue(typeof(T), out var value) ? value as T : null;
        public T AddComponent<T>() where T : new() {
            var value = new T(); components[typeof(T)] = value; return value;
        }
    }
    public static class Time { public static float unscaledTime; }
    public static class Debug {
        public static void Log(string text) {}
        public static void LogError(string text) {}
        public static void LogWarning(string text) {}
    }
    public static class PlayerPrefs {
        private static readonly Dictionary<string,string> values = new();
        public static string GetString(string key, string fallback) =>
            values.TryGetValue(key, out var value) ? value : fallback;
        public static void SetString(string key, string value) => values[key] = value;
        public static void DeleteKey(string key) => values.Remove(key);
        public static void Save() {}
    }
    public static class JsonUtility {
        public static bool MaterializeExplicitNullTask;
        public static bool MaterializeMissingProfessions;
        private static readonly JsonSerializerOptions options = new() { IncludeFields = true };
        public static string ToJson(object value) =>
            JsonSerializer.Serialize(value, value.GetType(), options);
        public static T FromJson<T>(string json) {
            string parsedJson = json;
            if (typeof(T) == typeof(CultivationCatalogResultDto)) {
                JsonNode root = JsonNode.Parse(json);
                if (root["techniques"] is JsonArray techniques)
                    foreach (JsonNode technique in techniques)
                        if (technique?["levels"] is JsonArray levels)
                            foreach (JsonNode level in levels)
                                if (level?["referenceComprehension"] == null)
                                    level["referenceComprehension"] = 0;
                parsedJson = root.ToJsonString();
            }
            T result = JsonSerializer.Deserialize<T>(parsedJson, options);
            if (MaterializeMissingProfessions
                && result is CultivationSnapshotResultDto professions
                && professions.snapshot != null) {
                professions.snapshot.alchemyProfession ??= new CultivationProfessionProgressDto();
                professions.snapshot.forgingProfession ??= new CultivationProfessionProgressDto();
            }
            if (MaterializeExplicitNullTask
                && RefiningSnapshotWireState.TryRead(json, out _, out _,
                    out _, out bool explicitNull) && explicitNull) {
                if (result is CultivationSnapshotResultDto snapshot && snapshot.snapshot != null)
                    snapshot.snapshot.activeTask = new CultivationTaskDto();
                if (result is CultivationChangedDto changed && changed.snapshot != null)
                    changed.snapshot.activeTask = new CultivationTaskDto();
            }
            return result;
        }
    }
}
'@
$references = @(Get-ChildItem "$PSHOME/ref" -Filter '*.dll' |
    Select-Object -ExpandProperty FullName) +
    [Newtonsoft.Json.JsonConvert].Assembly.Location
Add-Type -TypeDefinition ($stubs + "`n" + ($sources -join "`n")) `
    -ReferencedAssemblies $references -IgnoreWarnings | Out-Null

function Assert-True($condition, [string]$reason) {
    if (-not $condition) { throw $reason }
}

$identity = [Game.Cultivation.Protocol.CultivationRequestSequence]
$sessionId = [Guid]::NewGuid().ToString('D')
$nextSession = [Guid]::NewGuid().ToString('D')
$identity::EnterWorld($sessionId, 82)
$query = $null
Assert-True ($identity::TryNextQuery(82, $sessionId, 'cultivation_catalog',
    [ref]$query) -and $query.requestSequence -eq 1) 'Catalog query must get a fresh sequence.'
$learn = $null
Assert-True ($identity::TryPrepareLearn(82, $sessionId, 57, 20, 9001,
    [ref]$learn) -and $learn.requestSequence -eq 2) 'Learning must use the cultivation sequence.'
$learnWire = [UnityEngine.JsonUtility]::ToJson($learn) | ConvertFrom-Json
Assert-True ($learnWire.msgId -eq 'cultivation_learn' -and
    $learnWire.cultivationOperationSessionId -eq $sessionId -and
    $learnWire.stackId -eq 9001 -and
    @($learnWire.PSObject.Properties).Count -eq 8 -and
    -not $learnWire.PSObject.Properties['techniqueId']) `
    'Learn wire format must not include a technique ID or unrelated fields.'
$blocked = $null
Assert-True (-not $identity::TryPrepareStart(82, $sessionId, 57, 20, 'xuanqing',
    [ref]$blocked)) 'A pending learn must block another write.'
$identity::Suspend()
$identity::EnterWorld($nextSession, 82)
$recovered = $null
Assert-True ($identity::TryGetPending([ref]$recovered) -and
    $recovered.clientRequestId -eq $learn.clientRequestId -and
    $recovered.cultivationOperationSessionId -eq $nextSession -and
    $recovered.requestSequence -eq 1 -and
    $recovered.stackId -eq 9001 -and
    $recovered.expectedInventoryRevision -eq 20) `
    'Re-entry must retain business parameters and ID, changing only session/sequence.'
Assert-True (-not $identity::Matches(82, $sessionId, 'cultivation_learn', 2,
    $learn.clientRequestId) -and $identity::Matches(82, $nextSession,
    'cultivation_learn', 1, $learn.clientRequestId)) `
    'Old session results must not confirm pending operations.'
Assert-True ($identity::Complete(82, $nextSession, 'cultivation_learn', 1,
    $learn.clientRequestId)) 'A matching result must release the write identity.'
$start = $null
Assert-True ($identity::TryPrepareStart(82, $nextSession, 57, 21, 'xuanqing',
    [ref]$start) -and $start.requestSequence -eq 2 -and
    $start.clientRequestId -ne $learn.clientRequestId) `
    'A new write after completion must have a new ID and increasing sequence.'
$startWire = [UnityEngine.JsonUtility]::ToJson($start) | ConvertFrom-Json
Assert-True (@($startWire.PSObject.Properties).Count -eq 8 -and
    -not $startWire.PSObject.Properties['stackId'] -and
    $startWire.techniqueId -eq 'xuanqing') `
    'Start wire format must not include a stack ID.'
$writeResult = [UnityEngine.JsonUtility]::FromJson[
    Game.Cultivation.Protocol.CultivationWriteResultDto](
    '{"msgId":"cultivation_learn_result","type":"cultivation_learn_result",' +
    '"requestSequence":2,"clientRequestId":"' + $start.clientRequestId + '",' +
    '"idempotentReplay":true,"requiresInventorySnapshot":true,' +
    '"inventoryRevision":21,"stackItemChanges":[{"stackId":9001,"newCount":1}],' +
    '"missingMaterials":[{"itemId":"stone","quantity":2}],"snapshot":null}')
Assert-True ($writeResult.idempotentReplay -and
    $writeResult.requiresInventorySnapshot -and
    $writeResult.stackItemChanges[0].newCount -eq 1 -and
    $writeResult.missingMaterials[0].quantity -eq 2) `
    'Write results must retain replay, inventory and missing-material fields.'
$identity::MarkConflict()
$identity::Suspend()
$identity::EnterWorld($sessionId, 82)
Assert-True ($identity::HasConflict -and
    -not $identity::TryGetPending([ref]$blocked)) `
    'Identity conflicts must survive re-entry without automatic resubmission.'

$owner = [GameSession]::new()
$owner.LocalCharacterDbId = 51
$owner.CultivationOperationSessionId = [Guid]::NewGuid().ToString('D')
$network = [NetworkManager]::new()
$runtime = [Game.Cultivation.Runtime.CultivationSessionRuntime]::Ensure($network, $owner)
$runtime.BeginSession($owner, 51, $owner.CultivationOperationSessionId)
$update = $runtime.GetType().GetMethod('Update',
    [System.Reflection.BindingFlags]'Instance,NonPublic')
$update.Invoke($runtime, @()) | Out-Null
Assert-True ($network.Sent.Count -eq 3 -and
    ($network.Sent[0] | ConvertFrom-Json).msgId -eq 'cultivation_catalog' -and
    ($network.Sent[1] | ConvertFrom-Json).msgId -eq 'cultivation_snapshot' -and
    ($network.Sent[2] | ConvertFrom-Json).msgId -eq 'item_catalog') `
    'Accepted entry must query both catalogs and snapshot, without writes.'
$catalogSequence = ($network.Sent[0] | ConvertFrom-Json).requestSequence
$snapshotSequence = ($network.Sent[1] | ConvertFrom-Json).requestSequence
$catalog = @'
{"msgId":"cultivation_catalog_result","type":"cultivation_catalog_result","requestSequence":SEQ,
"success":true,"configurationRevision":57,"grades":[{"gradeId":"heaven","name":"grade","order":1}],
"techniques":[{"techniqueId":"xuanqing","name":"technique","gradeId":"heaven","requiredLevel":1,
"enabled":true,"levels":[{"level":0,"comprehension":5,"durationSeconds":0,
"referenceComprehension":null,"materials":[]},{"level":1,"comprehension":12,"durationSeconds":28800,
"referenceComprehension":0,"materials":[{"itemId":"insight_stone","quantity":2}]}]}],
"errorCode":"","serverTime":1802476800000}
'@.Replace('SEQ', [string]$catalogSequence).Replace("`r", '').Replace("`n", '')
[NetMessageDispatcher]::Instance.Dispatch('cultivation_catalog_result', $catalog)
$initialItemResult = '{"msgId":"item_catalog_result","type":"item_catalog_result",' +
    '"requestSequence":1,"success":true,"catalogVersion":93,"items":[],' +
    '"errorCode":"","serverTime":1000}'
[NetMessageDispatcher]::Instance.Dispatch('item_catalog_result', $initialItemResult)
Assert-True ($runtime.Catalog.configurationRevision -eq 57 -and
    -not $runtime.Catalog.techniques[0].levels[0].hasReferenceComprehension -and
    $runtime.Catalog.techniques[0].levels[1].hasReferenceComprehension -and
    $runtime.Catalog.techniques[0].levels[1].referenceComprehension -eq 0) `
    'Nullable reference must distinguish an explicit zero from JSON null.'
$catalogPositive = $catalog.Replace('"referenceComprehension":0',
    '"referenceComprehension":100')
$positiveParsed = [UnityEngine.JsonUtility]::FromJson[
    Game.Cultivation.Protocol.CultivationCatalogResultDto]($catalogPositive)
Assert-True ([Game.Cultivation.Protocol.CultivationCatalogWireState]::
    TryRestoreNullableReferences($catalogPositive, $positiveParsed) -and
    $positiveParsed.techniques[0].levels[1].referenceComprehension -eq 100) `
    'Positive nullable integers must be restored exactly from the wire.'
$missingReference = $catalog.Replace('"referenceComprehension":null,', '')
$missingParsed = [UnityEngine.JsonUtility]::FromJson[
    Game.Cultivation.Protocol.CultivationCatalogResultDto]($missingReference)
Assert-True (-not [Game.Cultivation.Protocol.CultivationCatalogWireState]::
    TryRestoreNullableReferences($missingReference, $missingParsed)) `
    'A missing nullable field is not an explicit null.'
$baseSnapshot = '{"characterDbId":51,"revision":0,"configurationRevision":57,' +
    '"baseComprehension":10,"techniqueComprehension":0,"totalComprehension":10,' +
    '"techniques":[],"activeTask":null}'
$automatic = '{"msgId":"cultivation_snapshot_result","type":"cultivation_snapshot_result",' +
    '"requestSequence":0,"success":true,"serverTime":1000,"snapshot":' + $baseSnapshot + '}'
[UnityEngine.JsonUtility]::MaterializeExplicitNullTask = $true
[UnityEngine.JsonUtility]::MaterializeMissingProfessions = $true
[NetMessageDispatcher]::Instance.Dispatch('cultivation_snapshot_result', $automatic)
[UnityEngine.JsonUtility]::MaterializeExplicitNullTask = $false
[UnityEngine.JsonUtility]::MaterializeMissingProfessions = $false
Assert-True ($runtime.HasAcceptedSnapshot -and $runtime.Snapshot.revision -eq 0 -and
    $null -eq $runtime.Snapshot.activeTask) `
    'Unsolicited first revision zero must be accepted and explicit null restored.'
Assert-True ($null -eq $runtime.Snapshot.alchemyProfession -and
    $null -eq $runtime.Snapshot.forgingProfession) `
    'Absent profession objects must not materialize into apprentice ranks.'
$task = '{"taskId":"task-1","techniqueId":"xuanqing","fromLevel":0,' +
    '"targetLevel":1,"startedAt":1000,"endsAt":2000,"configurationRevision":57,' +
    '"paidMaterials":[],"status":"active"}'
$running = $baseSnapshot.Replace('"revision":0', '"revision":1').
    Replace('"activeTask":null', '"activeTask":' + $task)
$changed = '{"msgId":"cultivation_changed","type":"cultivation_changed",' +
    '"cultivationOperationSessionId":"' + $owner.CultivationOperationSessionId +
    '","serverTime":1100,"snapshot":' + $running + '}'
[NetMessageDispatcher]::Instance.Dispatch('cultivation_changed', $changed)
Assert-True ($runtime.Snapshot.activeTask.taskId -eq 'task-1') `
    'A changed notification without success or sequence must apply.'
$nullResult = '{"msgId":"cultivation_snapshot_result","type":"cultivation_snapshot_result",' +
    '"requestSequence":' + $snapshotSequence + ',"success":true,"serverTime":1100,"snapshot":null}'
[NetMessageDispatcher]::Instance.Dispatch('cultivation_snapshot_result', $nullResult)
Assert-True ($runtime.Snapshot.activeTask.taskId -eq 'task-1') `
    'A null snapshot is not an idle snapshot.'
[NetMessageDispatcher]::Instance.Dispatch('cultivation_snapshot_result', $automatic)
Assert-True ($runtime.Snapshot.activeTask.taskId -eq 'task-1') `
    'Older idle snapshot must not erase a newer task.'
$wrongCharacter = $running.Replace('"characterDbId":51', '"characterDbId":52').
    Replace('"revision":1', '"revision":2')
$wrongCharacterChanged = $changed.Replace($running, $wrongCharacter)
[NetMessageDispatcher]::Instance.Dispatch('cultivation_changed', $wrongCharacterChanged)
Assert-True ($runtime.Snapshot.characterDbId -eq 51 -and
    $runtime.Snapshot.revision -eq 1) `
    'Another character cannot overwrite the current task.'
$missingTask = $baseSnapshot.Replace('"revision":0', '"revision":2').
    Replace(',"activeTask":null', '')
$missingChanged = '{"msgId":"cultivation_changed","type":"cultivation_changed",' +
    '"cultivationOperationSessionId":"' + $owner.CultivationOperationSessionId +
    '","serverTime":1200,"snapshot":' + $missingTask + '}'
[NetMessageDispatcher]::Instance.Dispatch('cultivation_changed', $missingChanged)
Assert-True ($runtime.Snapshot.activeTask.taskId -eq 'task-1') `
    'A missing activeTask cannot be interpreted as no task.'
$oldSession = '{"msgId":"cultivation_changed","type":"cultivation_changed",' +
    '"cultivationOperationSessionId":"' + $sessionId + '","serverTime":1200,"snapshot":' +
    $baseSnapshot.Replace('"revision":0', '"revision":2') + '}'
[NetMessageDispatcher]::Instance.Dispatch('cultivation_changed', $oldSession)
Assert-True ($runtime.Snapshot.activeTask.taskId -eq 'task-1') `
    'A notification from an old session must not modify current state.'
$completed = $baseSnapshot.Replace('"revision":0', '"revision":2')
$completedChanged = '{"msgId":"cultivation_changed","type":"cultivation_changed",' +
    '"cultivationOperationSessionId":"' + $owner.CultivationOperationSessionId +
    '","serverTime":1300,"snapshot":' + $completed + '}'
[UnityEngine.JsonUtility]::MaterializeExplicitNullTask = $true
[NetMessageDispatcher]::Instance.Dispatch('cultivation_changed', $completedChanged)
[UnityEngine.JsonUtility]::MaterializeExplicitNullTask = $false
Assert-True ($runtime.Snapshot.revision -eq 2 -and
    $null -eq $runtime.Snapshot.activeTask -and
    $runtime.LastSnapshotServerTime -eq 1300) `
    'A newer explicit null must clear even a materialized phantom task.'
$cross = $completed.Replace('"revision":2', '"revision":3').
    Replace('"configurationRevision":57', '"configurationRevision":56')
$crossChanged = $completedChanged.Replace($completed, $cross)
[NetMessageDispatcher]::Instance.Dispatch('cultivation_changed', $crossChanged)
Assert-True ($runtime.Snapshot.revision -eq 2 -and
    $runtime.Snapshot.configurationRevision -eq 57) `
    'Crossing configuration and character revisions must not be mixed.'
[UnityEngine.Time]::unscaledTime = 11
$update.Invoke($runtime, @()) | Out-Null
[UnityEngine.Time]::unscaledTime = 22
$update.Invoke($runtime, @()) | Out-Null
[UnityEngine.Time]::unscaledTime = 24
$update.Invoke($runtime, @()) | Out-Null
Assert-True ($network.Sent.Count -eq 9 -and
    ($network.Sent[-2] | ConvertFrom-Json).msgId -eq 'cultivation_snapshot' -and
    ($network.Sent[-1] | ConvertFrom-Json).msgId -eq 'item_catalog') `
    'A lost query must time out and requery without a per-frame request loop.'
$latestCatalogSequence = ($network.Sent[-3] | ConvertFrom-Json).requestSequence
$staleCatalog = $catalog.Replace('"configurationRevision":57',
    '"configurationRevision":99')
[NetMessageDispatcher]::Instance.Dispatch('cultivation_catalog_result', $staleCatalog)
Assert-True ($runtime.Catalog.configurationRevision -eq 57) `
    'An older query response must not replace the current catalog.'
$failedCatalog = $catalog.Replace('"requestSequence":' + $catalogSequence,
    '"requestSequence":' + $latestCatalogSequence).
    Replace('"success":true', '"success":false')
[NetMessageDispatcher]::Instance.Dispatch('cultivation_catalog_result', $failedCatalog)
Assert-True ($runtime.Catalog.configurationRevision -eq 57) `
    'A paired failed catalog response must preserve the last valid catalog.'
$newSequence = ($network.Sent[-2] | ConvertFrom-Json).requestSequence
$lateQuery = $automatic.Replace('"requestSequence":0',
    '"requestSequence":' + $snapshotSequence).Replace('"revision":0', '"revision":4')
[NetMessageDispatcher]::Instance.Dispatch('cultivation_snapshot_result', $lateQuery)
Assert-True ($runtime.Snapshot.revision -eq 2) `
    'A superseded query response must not replace the latest state.'
$freshQuery = $automatic.Replace('"requestSequence":0',
    '"requestSequence":' + $newSequence).Replace('"revision":0', '"revision":3')
[NetMessageDispatcher]::Instance.Dispatch('cultivation_snapshot_result', $freshQuery)
Assert-True ($runtime.Snapshot.revision -eq 3) `
    'The newest paired query response must update the state.'
$network.ConnectionEpoch = 2
[NetMessageDispatcher]::Instance.Dispatch('cultivation_changed', $crossChanged)
Assert-True ($runtime.Snapshot.revision -eq 3) `
    'An obsolete connection epoch cannot update current state.'
$owner.CultivationOperationSessionId = [Guid]::NewGuid().ToString('D')
$runtime.BeginSession($owner, 51, $owner.CultivationOperationSessionId)
Assert-True (-not $runtime.HasAcceptedSnapshot -and $null -eq $runtime.Snapshot) `
    'Re-entry must isolate the previous connection snapshot.'
$originalCultivationSession = $owner.CultivationOperationSessionId
$ranks = '"alchemyProfession":{"rank":-1,"experience":0,"requiredExperience":0},' +
    '"forgingProfession":{"rank":0,"experience":3,"requiredExperience":10},'
$professionSnapshot = $baseSnapshot.Replace('"techniques":', ($ranks + '"techniques":'))
[NetMessageDispatcher]::Instance.Dispatch('cultivation_snapshot_result',
    $automatic.Replace($baseSnapshot, $professionSnapshot))
Assert-True ($runtime.Snapshot.alchemyProfession.rank -eq -1 -and
    $runtime.Snapshot.forgingProfession.rank -eq 0 -and
    $runtime.Snapshot.forgingProfession.experience -eq 3) `
    'The automatic snapshot must retain both distinct profession ranks and experience.'
$rankChangedSnapshot = $professionSnapshot.Replace('"revision":0', '"revision":1').
    Replace('"rank":-1', '"rank":1').Replace('"totalComprehension":10', '"totalComprehension":24')
$rankChanged = '{"msgId":"cultivation_changed","type":"cultivation_changed",' +
    '"cultivationOperationSessionId":"' + $owner.CultivationOperationSessionId +
    '","serverTime":1300,"snapshot":' + $rankChangedSnapshot + '}'
[NetMessageDispatcher]::Instance.Dispatch('cultivation_changed', $rankChanged)
Assert-True ($runtime.Snapshot.alchemyProfession.rank -eq 1 -and
    $runtime.Snapshot.forgingProfession.rank -eq 0 -and
    $runtime.Snapshot.totalComprehension -eq 24) `
    'A newer notification must update the correct profession and comprehension.'
$missingRank = $baseSnapshot.Replace('"revision":0', '"revision":2').
    Replace('"techniques":', '"alchemyProfession":{},"forgingProfession":null,"techniques":')
$legacyChanged = '{"msgId":"cultivation_changed","type":"cultivation_changed",' +
    '"cultivationOperationSessionId":"' + $owner.CultivationOperationSessionId +
    '","serverTime":1400,"snapshot":' + $missingRank + '}'
[NetMessageDispatcher]::Instance.Dispatch('cultivation_changed', $legacyChanged)
Assert-True ($runtime.Snapshot.revision -eq 2 -and
    $runtime.Snapshot.alchemyProfession.rank -eq 1 -and
    $runtime.Snapshot.forgingProfession.rank -eq 0) `
    'Missing or null rank data must preserve known ranks for this character.'
$owner.LocalCharacterDbId = 52
$owner.CultivationOperationSessionId = [Guid]::NewGuid().ToString('D')
$runtime.BeginSession($owner, 52, $owner.CultivationOperationSessionId)
[NetMessageDispatcher]::Instance.Dispatch('cultivation_snapshot_result',
    $automatic.Replace('"characterDbId":51', '"characterDbId":52'))
Assert-True ($runtime.Snapshot.characterDbId -eq 52 -and
    $null -eq $runtime.Snapshot.alchemyProfession -and
    $null -eq $runtime.Snapshot.forgingProfession) `
    'A new character with missing ranks must not inherit the previous character ranks.'
$owner.LocalCharacterDbId = 51
$owner.CultivationOperationSessionId = $originalCultivationSession
$runtime.BeginSession($owner, 51, $originalCultivationSession)
$sessionSource = Get-Content (Join-Path $root 'Assets/Scripts/GameSession.cs') -Raw
$messagesSource = Get-Content (Join-Path $root 'Assets/Scripts/NetWork/NetMessages.cs') -Raw
Assert-True ($messagesSource.Contains('public string cultivationOperationSessionId;') -and
    $messagesSource.Contains('public long cultivationConfigurationRevision;') -and
    $messagesSource.Contains('public long cultivationRevision;') -and
    $messagesSource.Contains('public long totalComprehension;') -and
    $sessionSource.Contains('CultivationSessionRuntime.Ensure(networkManager, this)') -and
    $sessionSource.Contains('CultivationSessionRuntime.Instance?.BeginSession(this,') -and
    ([regex]::Matches($sessionSource,
        'CultivationSessionRuntime\.Instance\?\.SuspendSession\(this\)')).Count -ge 6) `
    'Cultivation session must be initialized and cleared at every world transition.'
Write-Output 'PASS: cultivation protocol stage 1 (identity, catalog, snapshot, lifecycle).'
