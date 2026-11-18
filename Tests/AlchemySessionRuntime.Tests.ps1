$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$paths = @(
    'Assets/Scripts/Cultivation/Protocol/RefiningSnapshotWireState.cs',
    'Assets/Scripts/Alchemy/Protocol/AlchemyMessages.cs',
    'Assets/Scripts/Alchemy/Protocol/AlchemyCatalogState.cs',
    'Assets/Scripts/Alchemy/Protocol/AlchemySnapshotState.cs',
    'Assets/Scripts/Alchemy/Model/AlchemyMaterialSelection.cs',
    'Assets/Scripts/Alchemy/Runtime/AlchemyLearnOperationState.cs',
    'Assets/Scripts/Alchemy/Runtime/AlchemyStartOperationState.cs',
    'Assets/Scripts/Alchemy/Runtime/AlchemyClaimOperationState.cs',
    'Assets/Scripts/Alchemy/Runtime/AlchemySessionRuntime.cs'
)
$sources = foreach ($path in $paths) {
    (Get-Content (Join-Path $root $path) -Raw) -replace
        '(?m)^using [^\r\n]+;\r?\n', ''
}
$body = $sources -join "`n"
$stubs = @'
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Game.Alchemy.Protocol;
using Game.Alchemy.Model;
using Game.Alchemy.Runtime;
using Game.Cultivation.Protocol;
using Game.Cultivation.Runtime;
using Game.Inventory;
using Game.Inventory.Model.V2;
using Game.Inventory.Protocol.V2;
using Game.UI.Notice;
using UnityEngine;
public class NetMessageBase { public string msgId; public string type; }
public class ServerErrorMessage {
    public string requestMsgId;
    public string errorCode;
    public string message;
}
namespace Game.Inventory.Protocol.V2 {
    public class InventoryV2StackChangeDto {
        public long stackId;
        public string itemId;
        public int previousCount;
        public int newCount;
        public bool isBound;
    }
}
namespace Game.Inventory.Model.V2 {
    public class InventoryStackState {
        public long StackId;
        public string ItemId;
        public int Count;
        public InventoryStackState(long id, string item, int count) {
            StackId = id; ItemId = item; Count = count;
        }
    }
    public class InventoryEquipmentState {
        public string Identity;
        public InventoryEquipmentState(string identity) {
            Identity = identity;
        }
    }
    public class InventoryOwnershipSnapshot {
        public long CharacterDbId;
        public long InventoryRevision;
        public List<InventoryStackState> Stacks;
        public List<InventoryEquipmentState> Equipment = new();
        public InventoryOwnershipSnapshot(long owner, long revision,
            params InventoryStackState[] stacks) {
            CharacterDbId = owner;
            InventoryRevision = revision;
            Stacks = new List<InventoryStackState>(stacks);
        }
    }
}
namespace Game.Inventory {
    public class InventoryModuleRuntime {
        public InventoryOwnershipSnapshot CurrentSnapshot;
        public bool DeltaWorks = true;
        public int Repairs;
        public int DeltaCalls;
        public bool TryInitialize() => true;
        public void RequestReconciliation() { Repairs++; }
        public bool ApplyAlchemyInventoryChanges(long revision,
            InventoryV2StackChangeDto[] changes) {
            DeltaCalls++;
            if (!DeltaWorks) return false;
            var next = new List<InventoryStackState>();
            foreach (var stack in CurrentSnapshot.Stacks) {
                var change = Array.Find(changes, item => item.stackId == stack.StackId);
                if (change == null)
                    next.Add(new InventoryStackState(stack.StackId, stack.ItemId, stack.Count));
                else if (change.newCount > 0)
                    next.Add(new InventoryStackState(stack.StackId, stack.ItemId,
                        change.newCount));
            }
            foreach (var change in changes) {
                if (change.newCount <= 0 ||
                    CurrentSnapshot.Stacks.Exists(item => item.StackId == change.stackId))
                    continue;
                next.Add(new InventoryStackState(change.stackId,
                    change.itemId, change.newCount));
            }
            CurrentSnapshot = new InventoryOwnershipSnapshot(
                CurrentSnapshot.CharacterDbId, revision, next.ToArray());
            return true;
        }
        public bool ApplyAlchemyClaimInventoryChanges(long revision,
            InventoryV2StackChangeDto[] changes) =>
            ApplyAlchemyInventoryChanges(revision, changes);
        public bool ApplyAlchemyLearnInventoryChanges(long revision,
            InventoryV2StackChangeDto[] changes) =>
            ApplyAlchemyInventoryChanges(revision, changes);
    }
}
namespace Game.Cultivation.Runtime {
    public class CultivationSessionRuntime {
        public static CultivationSessionRuntime Instance = new();
        public int Requests;
        public bool AllowRequest = true;
        public bool HasRecipeBook = true;
        public string RecipeId = "r1";
        public bool TryGetItem(string itemId,
            out Game.Cultivation.Protocol.CultivationItemDefinitionDto item) {
            item = HasRecipeBook && itemId == "recipe_book"
                ? new Game.Cultivation.Protocol.CultivationItemDefinitionDto {
                    kind = "recipebook", learnRecipeId = RecipeId } : null;
            return item != null;
        }
        public bool RequestItemCatalog() {
            if (!AllowRequest) return false;
            Requests++;
            return true;
        }
    }
}
namespace Game.Cultivation.Protocol {
    public class CultivationItemDefinitionDto {
        public string kind;
        public string learnRecipeId;
    }
}
namespace Game.UI.Notice {
    public static class GameNoticeService {
        public static string LastMessage;
        public static bool Show(string message) {
            LastMessage = message; return true;
        }
    }
}
public class GameSession {
    public bool IsWorldReady = true;
    public long LocalCharacterDbId = 42;
    public string AlchemyOperationSessionId;
}
public class NetworkManager : MonoBehaviour {
    public bool IsConnected = true;
    public long ConnectionEpoch = 1;
    public readonly List<string> Sent = new();
    public event Action<long,string> OnDisconnectedWithEpoch;
    public bool Send<T>(T value) {
        Sent.Add(JsonSerializer.Serialize(value, new JsonSerializerOptions {
            IncludeFields = true
        }));
        return true;
    }
    public void Disconnect() {
        IsConnected = false;
        OnDisconnectedWithEpoch?.Invoke(ConnectionEpoch, "test");
    }
}
public class NetMessageDispatcher {
    public static NetMessageDispatcher Instance = new();
    private readonly Dictionary<string,Action<string>> handlers = new();
    public void Register(string id, Action<string> handler) {
        handlers.TryGetValue(id, out var old);
        handlers[id] = old + handler;
    }
    public void Unregister(string id, Action<string> handler) {
        if (handlers.TryGetValue(id, out var old))
            handlers[id] = old - handler;
    }
    public void Dispatch(string id, string json) {
        if (handlers.TryGetValue(id, out var callback)) callback?.Invoke(json);
    }
}
namespace UnityEngine {
    public static class PlayerPrefs {
        private static readonly Dictionary<string,string> saved = new();
        public static string GetString(string key, string fallback) =>
            saved.TryGetValue(key, out string value) ? value : fallback;
        public static void SetString(string key, string value) { saved[key] = value; }
        public static void DeleteKey(string key) { saved.Remove(key); }
        public static void Save() {}
    }
    public static class MockObjects {
        public static object Inventory;
        public static T Get<T>() where T : class => Inventory as T;
    }
    [AttributeUsage(AttributeTargets.Field)]
    public class SerializeField : Attribute {}
    [AttributeUsage(AttributeTargets.Field)]
    public class MinAttribute : Attribute {
        public MinAttribute(float value) {}
    }
    [AttributeUsage(AttributeTargets.Class)]
    public class DisallowMultipleComponent : Attribute {}
    public class GameObject {
        private readonly Dictionary<Type,object> components = new();
        public T AddComponent<T>() where T : new() {
            var value = new T(); components[typeof(T)] = value; return value;
        }
        public T GetComponent<T>() where T : class =>
            components.TryGetValue(typeof(T), out var value) ? value as T : null;
    }
    public class MonoBehaviour {
        public GameObject gameObject = new();
        public T GetComponent<T>() where T : class => gameObject.GetComponent<T>();
        public T FindObjectOfType<T>() where T : class => MockObjects.Get<T>();
    }
    public static class Debug {
        public static void LogWarning(string value) {}
        public static void LogError(string value) {}
    }
    public static class Time { public static float unscaledTime; }
    public static class JsonUtility {
        public static bool MaterializeExplicitNullTask;
        public static T FromJson<T>(string json) {
            if (typeof(T).Name == "Pending"
                && (typeof(T).DeclaringType?.Name == "AlchemyStartOperationState"
                    || typeof(T).DeclaringType?.Name == "AlchemyClaimOperationState"
                    || typeof(T).DeclaringType?.Name == "AlchemyLearnOperationState")) {
                T pending = (T)Activator.CreateInstance(typeof(T), true);
                using var document = JsonDocument.Parse(json);
                foreach (var field in typeof(T).GetFields()) {
                    if (!document.RootElement.TryGetProperty(field.Name, out var value))
                        continue;
                    if (field.Name == "materials") {
                        var materials = new List<AlchemySelectedMaterialDto>();
                        foreach (var item in value.EnumerateArray())
                            materials.Add(new AlchemySelectedMaterialDto(
                                item.GetProperty("stackId").GetInt64(),
                                item.GetProperty("count").GetInt32()));
                        field.SetValue(pending, materials.ToArray());
                        continue;
                    }
                    field.SetValue(pending, JsonSerializer.Deserialize(
                        value.GetRawText(), field.FieldType,
                        new JsonSerializerOptions { IncludeFields = true }));
                }
                return pending;
            }
            T result = JsonSerializer.Deserialize<T>(json.Replace(
                "\"succeeded\":null", "\"succeeded\":false"),
                new JsonSerializerOptions { IncludeFields = true });
            if (MaterializeExplicitNullTask
                && result is AlchemyResponseDto response
                && response.snapshot != null
                && AlchemySnapshotWireState.TryRead(json, out _, out _,
                    out _, out bool explicitNull) && explicitNull)
                response.snapshot.currentTask = new AlchemyTaskDto();
            return result;
        }
        public static string ToJson(object value) => JsonSerializer.Serialize(value,
            new JsonSerializerOptions { IncludeFields = true });
    }
}
'@
Add-Type -TypeDefinition ($stubs + "`n" + $body) -IgnoreWarnings | Out-Null

function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
}
function Result-Json($sequence, $character, $revision, $task, $time) {
    return '{"msgId":"alchemy_snapshot_result","type":"alchemy_snapshot_result",' +
        '"alchemyOperationSessionId":"' + $owner.AlchemyOperationSessionId +
        '","requestSequence":' + $sequence + ',"success":true,"serverTime":' +
        $time + ',"snapshot":{"characterDbId":' + $character +
        ',"revision":' + $revision + ',"learnedRecipeIds":[],"currentTask":' +
        $task + '}}'
}

$owner = [GameSession]::new()
$owner.AlchemyOperationSessionId = [Guid]::NewGuid().ToString('D')
$network = [NetworkManager]::new()
$runtime = [Game.Alchemy.Runtime.AlchemySessionRuntime]::Ensure($network, $owner)
$runtime.BeginSession($owner, 42, $owner.AlchemyOperationSessionId)
$changes = 0
$runtime.add_SnapshotChanged([Action[Game.Alchemy.Protocol.AlchemySnapshotDto]]{
    param($snapshot)
    $script:changes++
})

Assert-True ($runtime.RequestSnapshot() -and $network.Sent.Count -eq 1) `
    'The active session must be able to request a snapshot.'
Assert-True (-not $runtime.IsSnapshotCurrent) `
    'The snapshot cannot be used for a new start while a query is pending.'
$sequence = ($network.Sent[-1] | ConvertFrom-Json).requestSequence
Assert-True (($network.Sent[-1] | ConvertFrom-Json).msgId -eq 'alchemy_snapshot') `
    'Snapshot query must use the alchemy message ID.'
$activeTask = '{"taskId":"task-1","recipeId":"r1","status":"active",' +
    '"succeeded":null,"startedAtUnixMilliseconds":1000,"endsAtUnixMilliseconds":2000}'
$dispatcher = [NetMessageDispatcher]::Instance
$dispatcher.Dispatch('alchemy_snapshot_result',
    (Result-Json ($sequence + 1) 42 4 $activeTask 1100))
Assert-True ($null -eq $runtime.Snapshot) `
    'A response for another request sequence must be ignored.'
$foreignSession = (Result-Json $sequence 42 4 $activeTask 1100).Replace(
    '"success":true', '"alchemyOperationSessionId":"' +
    [Guid]::NewGuid().ToString('D') + '","success":true')
$dispatcher.Dispatch('alchemy_snapshot_result', $foreignSession)
Assert-True ($null -eq $runtime.Snapshot) `
    'A reply carrying a foreign operation session must be ignored.'
$dispatcher.Dispatch('alchemy_snapshot_result',
    (Result-Json $sequence 9 4 $activeTask 1100))
Assert-True ($null -eq $runtime.Snapshot -and $network.Sent.Count -eq 2) `
    'A foreign character snapshot must be ignored and queried again.'
$acceptedSequence = ($network.Sent[-1] | ConvertFrom-Json).requestSequence
$dispatcher.Dispatch('alchemy_snapshot_result',
    (Result-Json $sequence 42 5 'null' 1150))
Assert-True ($null -eq $runtime.Snapshot) `
    'A delayed response cannot satisfy the recovery query.'
$dispatcher.Dispatch('alchemy_snapshot_result',
    (Result-Json $acceptedSequence 42 4 $activeTask 1200))
Assert-True ($runtime.Snapshot.currentTask.taskId -eq 'task-1' -and
    $changes -eq 1 -and $runtime.IsSnapshotCurrent) `
    'A current character task must be accepted once and mark the state current.'

Assert-True ($runtime.RequestSnapshot()) 'The furnace must be refreshable.'
$nullSequence = ($network.Sent[-1] | ConvertFrom-Json).requestSequence
$beforeNull = $network.Sent.Count
$dispatcher.Dispatch('alchemy_snapshot_result',
    ('{"msgId":"alchemy_snapshot_result","type":"alchemy_snapshot_result",' +
    '"alchemyOperationSessionId":"' + $owner.AlchemyOperationSessionId + '",' +
    '"requestSequence":' + $nullSequence +
    ',"success":true,"serverTime":1300,"snapshot":null}'))
Assert-True ($runtime.Snapshot.currentTask.taskId -eq 'task-1' -and
    $changes -eq 1 -and -not $runtime.IsSnapshotCurrent -and
    $network.Sent.Count -eq ($beforeNull + 1)) `
    'snapshot:null must preserve the furnace and trigger one recovery query.'
$nullRecoverySequence = ($network.Sent[-1] | ConvertFrom-Json).requestSequence
$dispatcher.Dispatch('alchemy_snapshot_result',
    ('{"msgId":"alchemy_snapshot_result","type":"alchemy_snapshot_result",' +
    '"alchemyOperationSessionId":"' + $owner.AlchemyOperationSessionId + '",' +
    '"requestSequence":' + $nullRecoverySequence +
    ',"success":true,"serverTime":1350,"snapshot":null}'))
Assert-True ($network.Sent.Count -eq ($beforeNull + 1) -and
    $runtime.Snapshot.currentTask.taskId -eq 'task-1') `
    'Repeated missing snapshots must not trigger an unbounded query loop.'

Assert-True ($runtime.RequestSnapshot()) 'A null result must allow a fresh query.'
$clearSequence = ($network.Sent[-1] | ConvertFrom-Json).requestSequence
[UnityEngine.JsonUtility]::MaterializeExplicitNullTask = $true
$dispatcher.Dispatch('alchemy_snapshot_result',
    (Result-Json $clearSequence 42 5 'null' 1400))
Assert-True ($null -eq $runtime.Snapshot.currentTask -and $changes -eq 2 -and
    $runtime.IsSnapshotCurrent) `
    'currentTask:null must clear even if the deserializer creates an empty task.'

$sentBeforeChange = $network.Sent.Count
$dispatcher.Dispatch('alchemy_changed',
    ('{"msgId":"alchemy_changed","type":"alchemy_changed",' +
    '"alchemyOperationSessionId":"' + [Guid]::NewGuid().ToString('D') + '"}'))
$dispatcher.Dispatch('alchemy_changed',
    '{"msgId":"alchemy_changed","type":"alchemy_changed","snapshot":{"characterDbId":9}}')
Assert-True ($network.Sent.Count -eq $sentBeforeChange) `
    'Foreign-session and foreign-character notifications must be ignored.'

$dispatcher.Dispatch('alchemy_changed',
    ('{"msgId":"alchemy_changed","type":"alchemy_changed",' +
    '"alchemyOperationSessionId":"' + $owner.AlchemyOperationSessionId + '",' +
    '"success":true,"serverTime":1450,"snapshot":null}'))
Assert-True ($network.Sent.Count -eq ($sentBeforeChange + 1) -and
    $null -eq $runtime.Snapshot.currentTask -and $changes -eq 2 -and
    -not $runtime.IsSnapshotCurrent) `
    'A notification may query the server but must not directly occupy the furnace.'
$notificationSequence = ($network.Sent[-1] | ConvertFrom-Json).requestSequence
$dispatcher.Dispatch('alchemy_changed',
    ('{"msgId":"alchemy_changed","type":"alchemy_changed",' +
    '"alchemyOperationSessionId":"' + $owner.AlchemyOperationSessionId + '",' +
    '"success":true,"serverTime":1450,"snapshot":null}'))
$dispatcher.Dispatch('alchemy_changed',
    ('{"msgId":"alchemy_changed","type":"alchemy_changed",' +
    '"alchemyOperationSessionId":"' + $owner.AlchemyOperationSessionId + '",' +
    '"success":true,"serverTime":1450,"snapshot":null}'))
Assert-True ($network.Sent.Count -eq ($sentBeforeChange + 1)) `
    'Repeated notifications while a snapshot is pending must be coalesced.'
$dispatcher.Dispatch('alchemy_snapshot_result',
    (Result-Json $clearSequence 42 8 $activeTask 1500))
Assert-True ($network.Sent.Count -eq ($sentBeforeChange + 1) -and
    $null -eq $runtime.Snapshot.currentTask) `
    'An earlier reply must not satisfy the pending notification query.'
$dispatcher.Dispatch('alchemy_snapshot_result',
    (Result-Json $notificationSequence 42 6 $activeTask 1600))
Assert-True ($network.Sent.Count -eq ($sentBeforeChange + 2) -and
    $runtime.Snapshot.currentTask.taskId -eq 'task-1' -and $changes -eq 3) `
    'A notification arriving during a query must trigger one follow-up query.'
$followUpSequence = ($network.Sent[-1] | ConvertFrom-Json).requestSequence
$dispatcher.Dispatch('alchemy_snapshot_result',
    (Result-Json $followUpSequence 42 7 'null' 1700))
Assert-True ($null -eq $runtime.Snapshot.currentTask -and $changes -eq 4) `
    'A later explicit empty task must clear the furnace after notification refresh.'

Assert-True ($runtime.RequestSnapshot()) 'A snapshot error must be recoverable.'
$beforeError = $network.Sent.Count
$dispatcher.Dispatch('alchemy_changed',
    ('{"msgId":"alchemy_changed","type":"alchemy_changed",' +
    '"alchemyOperationSessionId":"' + $owner.AlchemyOperationSessionId + '",' +
    '"success":true,"serverTime":1750,"snapshot":null}'))
$dispatcher.Dispatch('error',
    '{"msgId":"error","requestMsgId":"alchemy_snapshot","errorCode":"temporary"}')
Assert-True ($network.Sent.Count -eq ($beforeError + 1) -and
    $null -eq $runtime.Snapshot.currentTask -and
    -not $runtime.IsSnapshotCurrent) `
    'A queued notification must query again after a snapshot error.'
$recoverySequence = ($network.Sent[-1] | ConvertFrom-Json).requestSequence
$dispatcher.Dispatch('alchemy_snapshot_result',
    (Result-Json $recoverySequence 42 8 'null' 1800))

$owner.AlchemyOperationSessionId = [Guid]::NewGuid().ToString('D')
$runtime.BeginSession($owner, 42, $owner.AlchemyOperationSessionId)
Assert-True ($null -eq $runtime.Snapshot -and -not $runtime.IsSnapshotCurrent) `
    'Re-entry must clear old task state and invalidate readiness.'
Assert-True ($runtime.RequestSnapshot()) 'A new session must be queryable.'
$nextSequence = ($network.Sent[-1] | ConvertFrom-Json).requestSequence
Assert-True ($nextSequence -gt $clearSequence) `
    'New-session queries must not reuse old request sequences.'
$dispatcher.Dispatch('alchemy_snapshot_result',
    (Result-Json $clearSequence 42 6 $activeTask 1500))
Assert-True ($null -eq $runtime.Snapshot) `
    'A prior-session delayed response must be ignored.'
$dispatcher.Dispatch('alchemy_snapshot_result',
    (Result-Json $nextSequence 42 6 $activeTask 1600))
Assert-True ($runtime.Snapshot.currentTask.taskId -eq 'task-1') `
    'A matching response in the new session must be accepted.'

Assert-True ($runtime.RequestSnapshot()) 'A query may overlap a later notification.'
$outstanding = ($network.Sent[-1] | ConvertFrom-Json).requestSequence
$dispatcher.Dispatch('alchemy_changed',
    ('{"msgId":"alchemy_changed","type":"alchemy_changed",' +
    '"alchemyOperationSessionId":"' + $owner.AlchemyOperationSessionId +
    '","requestSequence":0,"success":true,"serverTime":1800,' +
    '"snapshot":{"characterDbId":42,"revision":8,"learnedRecipeIds":[],' +
    '"currentTask":' + $activeTask + '}}'))
Assert-True ($runtime.Snapshot.revision -eq 8 -and $runtime.IsSnapshotCurrent) `
    'A valid newer notification can make the snapshot current during a query.'
$dispatcher.Dispatch('alchemy_snapshot_result',
    (Result-Json $outstanding 42 7 $activeTask 1700))
Assert-True ($runtime.Snapshot.revision -eq 8 -and $runtime.IsSnapshotCurrent) `
    'An older reply cannot invalidate a newer accepted notification.'

$recipeJson = '{"recipeId":"r1","name":"test","durationSeconds":60,' +
    '"successRateBps":8000,"materials":[],"outputs":[]}'
$catalogSyncId = [Guid]::NewGuid().ToString('D')
function Catalog-Json($sequence, $revision, $sync, $recipes) {
    return '{"msgId":"alchemy_catalog_result","type":"alchemy_catalog_result",' +
        '"alchemyOperationSessionId":"' + $owner.AlchemyOperationSessionId +
        '","requestSequence":' + $sequence + ',"success":true,' +
        '"configurationRevision":' + $revision +
        ',"catalogSyncId":"' + $sync + '","batchIndex":1,' +
        '"batchCount":1,"totalCount":1,"recipes":[' + $recipes +
        '],"serverTime":1800000000000}'
}
$beforeCatalog = $network.Sent.Count
Assert-True ($runtime.RequestCatalog()) 'A live operation session can query recipes.'
$failedSequence = ($network.Sent[-1] | ConvertFrom-Json).requestSequence
$dispatcher.Dispatch('error',
    '{"msgId":"error","requestMsgId":"alchemy_catalog","errorCode":"temporary"}')
Assert-True ($network.Sent.Count -eq ($beforeCatalog + 2) -and
    $runtime.ConfigurationRevision -eq 0 -and -not $runtime.IsCatalogCurrent) `
    'A catalog general error must leave old data untouched and retry once.'
$retrySequence = ($network.Sent[-1] | ConvertFrom-Json).requestSequence
$dispatcher.Dispatch('alchemy_catalog_result',
    (Catalog-Json $failedSequence 9 $catalogSyncId $recipeJson))
Assert-True (-not $runtime.IsCatalogCurrent) `
    'A response to an abandoned catalog query must be ignored.'
$dispatcher.Dispatch('alchemy_catalog_result',
    (Catalog-Json $retrySequence 9 $catalogSyncId $recipeJson))
Assert-True ($runtime.IsCatalogCurrent -and
    $runtime.ConfigurationRevision -eq 9 -and
    $runtime.Catalog[0].recipeId -eq 'r1') `
    'A matching complete catalog must become available atomically.'

$beforeNotification = $network.Sent.Count
$dispatcher.Dispatch('alchemy_changed',
    ('{"msgId":"alchemy_changed","type":"alchemy_changed",' +
    '"alchemyOperationSessionId":"' + $owner.AlchemyOperationSessionId +
    '","requestSequence":0,"success":true,' +
    '"configurationRevision":10,"serverTime":1800000001000,"snapshot":null}'))
Assert-True (-not $runtime.IsCatalogCurrent -and
    $runtime.Catalog[0].recipeId -eq 'r1' -and
    $network.Sent.Count -eq ($beforeNotification + 2)) `
    'A newer configuration hint must retain the old recipes while refreshing both queries.'
$sentIds = @($network.Sent | Select-Object -Last 2 |
    ForEach-Object { ($_ | ConvertFrom-Json).msgId })
Assert-True ($sentIds -contains 'alchemy_catalog' -and
    $sentIds -contains 'alchemy_snapshot') `
    'A status hint with snapshot:null must refresh state without clearing the furnace.'
$newCatalogSequence = @($network.Sent | Select-Object -Last 2 |
    ForEach-Object { $_ | ConvertFrom-Json } |
    Where-Object msgId -eq 'alchemy_catalog')[0].requestSequence
$update = [Game.Alchemy.Runtime.AlchemySessionRuntime].GetMethod(
    'Update', [System.Reflection.BindingFlags]'Instance, NonPublic')
$update.Invoke($runtime, @()) | Out-Null
Assert-True ([Game.Cultivation.Runtime.CultivationSessionRuntime]::Instance.Requests -eq 1) `
    'A configuration hint must refresh the existing item catalog owner.'
$dispatcher.Dispatch('alchemy_catalog_result',
    (Catalog-Json $newCatalogSequence 9 $catalogSyncId $recipeJson))
$freshSequence = ($network.Sent[-1] | ConvertFrom-Json).requestSequence
Assert-True ($freshSequence -ne $newCatalogSequence -and
    -not $runtime.IsCatalogCurrent) `
    'An obsolete in-flight catalog must trigger a fresh query.'
$dispatcher.Dispatch('alchemy_catalog_result',
    (Catalog-Json $freshSequence 10 ([Guid]::NewGuid().ToString('D')) $recipeJson))
Assert-True ($runtime.IsCatalogCurrent -and
    $runtime.ConfigurationRevision -eq 10) `
    'Only a catalog matching the newer configuration becomes current.'

$beforeTooLarge = $network.Sent.Count
Assert-True ($runtime.RequestCatalog()) 'A catalog query can be retried manually.'
$tooLargeSequence = ($network.Sent[-1] | ConvertFrom-Json).requestSequence
$dispatcher.Dispatch('alchemy_catalog_result',
    ('{"msgId":"alchemy_catalog_result","type":"alchemy_catalog_result",' +
    '"alchemyOperationSessionId":"' + $owner.AlchemyOperationSessionId +
    '","requestSequence":' + $tooLargeSequence + ',"success":false,' +
    '"errorCode":"alchemy_catalog_entry_too_large","recipes":[]}'))
Assert-True ($network.Sent.Count -eq ($beforeTooLarge + 1) -and
    $runtime.Catalog[0].recipeId -eq 'r1' -and -not $runtime.IsCatalogCurrent) `
    'An oversized recipe is terminal until administrators change configuration.'

$beforeInvalid = $network.Sent.Count
Assert-True ($runtime.RequestCatalog()) 'An explicit new query may recover later.'
$firstInvalid = ($network.Sent[-1] | ConvertFrom-Json).requestSequence
$dispatcher.Dispatch('alchemy_catalog_result',
    (Catalog-Json $firstInvalid 11 ([Guid]::NewGuid().ToString('D')) 'null'))
$secondInvalid = ($network.Sent[-1] | ConvertFrom-Json).requestSequence
Assert-True ($secondInvalid -ne $firstInvalid -and
    $network.Sent.Count -eq ($beforeInvalid + 2)) `
    'An invalid batch may start one fresh catalog query.'
$dispatcher.Dispatch('alchemy_catalog_result',
    (Catalog-Json $secondInvalid 11 ([Guid]::NewGuid().ToString('D')) 'null'))
Assert-True ($network.Sent.Count -eq ($beforeInvalid + 2) -and
    $runtime.Catalog[0].recipeId -eq 'r1' -and -not $runtime.IsCatalogCurrent) `
    'A second bad batch must stop retrying without publishing partial data.'

$bookInventory = [Game.Inventory.InventoryModuleRuntime]::new()
[UnityEngine.MockObjects]::Inventory = $bookInventory
$dispatcher.Dispatch('alchemy_changed',
    ('{"msgId":"alchemy_changed","type":"alchemy_changed",' +
    '"alchemyOperationSessionId":"' + $owner.AlchemyOperationSessionId +
    '","requestSequence":0,"success":true,"configurationRevision":10,' +
    '"serverTime":1800000002000,"snapshot":{"characterDbId":42,' +
    '"revision":9,"learnedRecipeIds":["r1"],"currentTask":' +
    $activeTask + '}}'))
$taskBeforeBookError = $runtime.Snapshot.currentTask.taskId
$itemsBeforeBookError = [Game.Cultivation.Runtime.CultivationSessionRuntime]::Instance.Requests
$sentBeforeBookError = $network.Sent.Count
$dispatcher.Dispatch('error',
    '{"msgId":"error","requestMsgId":"alchemy_learn","errorCode":"alchemy_book_missing"}')
Assert-True ($bookInventory.Repairs -eq 1 -and
    [Game.Cultivation.Runtime.CultivationSessionRuntime]::Instance.Requests -eq
        ($itemsBeforeBookError + 1) -and
    $network.Sent.Count -eq ($sentBeforeBookError + 1) -and
    ($network.Sent[-1] | ConvertFrom-Json).msgId -eq 'alchemy_snapshot' -and
    $runtime.Snapshot.currentTask.taskId -eq $taskBeforeBookError -and
    $runtime.Snapshot.learnedRecipeIds[0] -eq 'r1' -and
    [Game.UI.Notice.GameNoticeService]::LastMessage -match '不可学习') `
    'book_missing repairs inventory and catalogs without clearing an in-flight task.'

$network.Disconnect()
Assert-True ($null -eq $runtime.Snapshot -and -not $runtime.RequestSnapshot() -and
    -not $runtime.IsSnapshotCurrent) `
    'A disconnected session must clear the task and reject queries.'
$sentAfterDisconnect = $network.Sent.Count
$dispatcher.Dispatch('alchemy_changed',
    '{"msgId":"alchemy_changed","type":"alchemy_changed"}')
Assert-True ($network.Sent.Count -eq $sentAfterDisconnect) `
    'A disconnected session must ignore stale status notifications.'

$owner.AlchemyOperationSessionId = [Guid]::NewGuid().ToString('D')
$network = [NetworkManager]::new()
$runtime = [Game.Alchemy.Runtime.AlchemySessionRuntime]::Ensure($network, $owner)
$runtime.BeginSession($owner, 42, $owner.AlchemyOperationSessionId)
$sameRevisionChanges = 0
$runtime.add_SnapshotChanged([Action[Game.Alchemy.Protocol.AlchemySnapshotDto]]{
    param($snapshot)
    $script:sameRevisionChanges++
})
Assert-True ($runtime.RequestSnapshot()) 'The same-revision case needs a query.'
$occupiedSequence = ($network.Sent[-1] | ConvertFrom-Json).requestSequence
$dispatcher.Dispatch('alchemy_snapshot_result',
    (Result-Json $occupiedSequence 42 20 $activeTask 2000))
Assert-True ($runtime.Snapshot.currentTask.taskId -eq 'task-1') `
    'The baseline furnace must be occupied.'
Assert-True ($runtime.RequestSnapshot()) 'The same-revision case needs a refresh.'
$emptySequence = ($network.Sent[-1] | ConvertFrom-Json).requestSequence
[UnityEngine.JsonUtility]::MaterializeExplicitNullTask = $true
$dispatcher.Dispatch('alchemy_snapshot_result',
    (Result-Json $emptySequence 42 20 'null' 2100))
Assert-True ($runtime.IsSnapshotCurrent -and
    $runtime.Snapshot.revision -eq 20 -and
    $null -eq $runtime.Snapshot.currentTask -and
    $sameRevisionChanges -eq 2) `
    'The matching latest empty snapshot must clear the furnace at the same revision.'
$dispatcher.Dispatch('alchemy_changed',
    ('{"msgId":"alchemy_changed","type":"alchemy_changed",' +
    '"alchemyOperationSessionId":"' + $owner.AlchemyOperationSessionId +
    '","requestSequence":0,"success":true,"serverTime":2050,' +
    '"snapshot":{"characterDbId":42,"revision":20,"learnedRecipeIds":[],' +
    '"currentTask":' + $activeTask + '}}'))
Assert-True ($runtime.IsSnapshotCurrent -and
    $null -eq $runtime.Snapshot.currentTask -and
    $sameRevisionChanges -eq 2) `
    'A delayed same-revision task notification must not reoccupy the furnace.'
$beforeConflict = $network.Sent.Count
$dispatcher.Dispatch('alchemy_changed',
    ('{"msgId":"alchemy_changed","type":"alchemy_changed",' +
    '"alchemyOperationSessionId":"' + $owner.AlchemyOperationSessionId +
    '","requestSequence":0,"success":true,"serverTime":2200,' +
    '"snapshot":{"characterDbId":42,"revision":20,"learnedRecipeIds":[],' +
    '"currentTask":' + $activeTask + '}}'))
Assert-True (-not $runtime.IsSnapshotCurrent -and
    $null -eq $runtime.Snapshot.currentTask -and
    $network.Sent.Count -eq ($beforeConflict + 1) -and
    ($network.Sent[-1] | ConvertFrom-Json).msgId -eq 'alchemy_snapshot') `
    'A newer conflicting same-revision task must query recovery before permitting writes.'
$conflictSequence = ($network.Sent[-1] | ConvertFrom-Json).requestSequence
$dispatcher.Dispatch('alchemy_snapshot_result',
    (Result-Json $conflictSequence 42 20 'null' 2300))
Assert-True ($runtime.IsSnapshotCurrent -and
    $null -eq $runtime.Snapshot.currentTask -and
    $sameRevisionChanges -eq 3) `
    'A fresh matching empty response restores readiness without inventing a task.'
Assert-True ($runtime.RequestSnapshot()) `
    'A late matching query should still be recoverable.'
$staleSequence = ($network.Sent[-1] | ConvertFrom-Json).requestSequence
$beforeStale = $network.Sent.Count
$dispatcher.Dispatch('alchemy_snapshot_result',
    (Result-Json $staleSequence 42 20 $activeTask 2200))
Assert-True (-not $runtime.IsSnapshotCurrent -and
    $null -eq $runtime.Snapshot.currentTask -and
    $network.Sent.Count -eq ($beforeStale + 1)) `
    'An older same-revision query must preserve the empty furnace and retry once.'
$staleRecovery = ($network.Sent[-1] | ConvertFrom-Json).requestSequence
$dispatcher.Dispatch('alchemy_snapshot_result',
    (Result-Json $staleRecovery 42 20 'null' 2400))
Assert-True ($runtime.IsSnapshotCurrent -and
    $null -eq $runtime.Snapshot.currentTask -and
    $sameRevisionChanges -eq 4) `
    'The retried query restores current empty-furnace state.'

$sessionSource = Get-Content (Join-Path $root 'Assets/Scripts/GameSession.cs') -Raw
Assert-True ($sessionSource.Contains('AlchemySessionRuntime.Ensure(networkManager, this);') -and
    $sessionSource.Contains('AlchemySessionRuntime.Instance?.BeginSession(this,') -and
    ([regex]::Matches($sessionSource,
        'AlchemySessionRuntime.Instance\?\.SuspendSession\(this\);').Count -ge 6)) `
    'GameSession must wire alchemy entry and all existing exit/recovery paths.'
Write-Output 'PASS: alchemy snapshot dispatch, session filtering and explicit empty furnace.'
