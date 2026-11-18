$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$protocol = Get-Content (Join-Path $root 'Assets/Scripts/Cultivation/Protocol/RefiningMessages.cs') -Raw
$wireState = Get-Content (Join-Path $root 'Assets/Scripts/Cultivation/Protocol/RefiningSnapshotWireState.cs') -Raw
$sequence = Get-Content (Join-Path $root 'Assets/Scripts/Cultivation/Protocol/RefiningRequestSequence.cs') -Raw
$calculator = Get-Content (Join-Path $root 'Assets/Scripts/Cultivation/Protocol/RefiningPreviewCalculator.cs') -Raw
$runtime = Get-Content (Join-Path $root 'Assets/Scripts/Cultivation/Runtime/RefiningSessionRuntime.cs') -Raw
$startButton = Get-Content (Join-Path $root 'Assets/Scripts/Cultivation/Presentation/RefiningStartButton.cs') -Raw
$buttonMeta = Get-Content (Join-Path $root 'Assets/Scripts/Cultivation/Presentation/RefiningStartButton.cs.meta') -Raw
$scene = Get-Content -LiteralPath (Join-Path $root 'Assets/Scenes/WorldShell.unity') -Raw
$session = Get-Content (Join-Path $root 'Assets/Scripts/GameSession.cs') -Raw
$panel = Get-Content (Join-Path $root 'Assets/Scripts/Cultivation/Presentation/CultivationPanelToggle.cs') -Raw
$window = Get-Content (Join-Path $root 'Assets/Scripts/Cultivation/Presentation/CultivationWindowToggle.cs') -Raw
foreach ($name in 'protocol', 'wireState', 'sequence', 'calculator', 'runtime', 'startButton') {
    Set-Variable $name ((Get-Variable $name).Value -replace '(?m)^using [^\r\n]+;\r?\n', '')
}

$stubs = @'
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Game.Cultivation.Protocol;
using Game.Cultivation.Runtime;
using Game.Inventory.Protocol.V2;
using Game.Inventory;
using Game.Inventory.Model.V2;
using Game.UI.Notice;
using UnityEngine;
using UnityEngine.UI;
public class NetMessageBase { public string msgId; public string type; }
public class ServerErrorMessage { public string requestMsgId; public string errorCode; }
public class GameSession {
    public bool IsWorldReady;
    public long LocalCharacterDbId;
    public string JingjieOperationSessionId;
    public ProgressionSnapshotResult LocalProgressionSnapshot;
    public int ProgressionRefreshes;
    public void QueueProgressionSnapshotRefresh() { ProgressionRefreshes++; }
}
public class ProgressionSnapshotResult { public long jingjieExp; }
public class NetworkManager : UnityEngine.MonoBehaviour {
    public static NetworkManager Instance;
    public bool IsConnected = true;
    public bool FailNextSend;
    public long ConnectionEpoch = 1;
    public readonly List<string> Sent = new();
    public event Action<long,string> OnDisconnectedWithEpoch;
    public NetworkManager() { Instance = this; }
    public bool Send(NetMessageBase value) {
        if (FailNextSend) { FailNextSend = false; return false; }
        Sent.Add(UnityEngine.JsonUtility.ToJson(value));
        return true;
    }
}
public class NetMessageDispatcher {
    public static NetMessageDispatcher Instance = new();
    private readonly Dictionary<string, Action<string>> handlers = new();
    public void Register(string id, Action<string> handler) {
        handlers.TryGetValue(id, out var old);
        handlers[id] = old + handler;
    }
    public void Unregister(string id, Action<string> handler) {
        if (handlers.TryGetValue(id, out var old))
            handlers[id] = old - handler;
    }
    public void Dispatch(string id, string json) {
        if (handlers.TryGetValue(id, out var handler)) handler?.Invoke(json);
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
        public static InventoryModuleRuntime Shared = new();
        public int Requests;
        public int Reconciliations;
        public int Applied;
        public InventoryOwnershipSnapshot CurrentSnapshot;
        public bool TryInitialize() => true;
        public void RequestSnapshot() { Requests++; }
        public void RequestReconciliation() { Reconciliations++; }
        public bool ApplyRefiningInventoryChanges(long revision,
            InventoryV2StackChangeDto[] changes) {
            if (CurrentSnapshot == null || revision <= CurrentSnapshot.InventoryRevision
                || changes == null || changes.Length == 0) return false;
            if (revision != CurrentSnapshot.InventoryRevision + 1) return false;
            foreach (var change in changes) {
                var stack = CurrentSnapshot.Stacks.Find(s => s.StackId == change.stackId);
                if (stack == null || change.newCount < 0) return false;
                if (change.newCount == 0) CurrentSnapshot.Stacks.Remove(stack);
                else stack.Count = change.newCount;
            }
            CurrentSnapshot.InventoryRevision = revision;
            Applied++;
            return true;
        }
    }
}
namespace Game.Inventory.Model.V2 {
    public class InventoryStackState {
        public long StackId; public string ItemId; public int Count;
    }
    public class InventoryOwnershipSnapshot {
        public long CharacterDbId; public long InventoryRevision;
        public List<InventoryStackState> Stacks = new();
    }
}
namespace Game.UI.Notice {
    public static class GameNoticeService {
        public static string Last;
        public static bool Show(string message) { Last = message; return true; }
    }
}
namespace Game.Cultivation.Presentation {
    public class CultivationMaterialDropTarget {
        public long StackId = 123;
        public int Count = 5;
        public long Revision = 12;
        public bool Selected = true;
        public int Clears;
        public bool TryGetSelection(out long stackId, out int count,
            out long revision) {
            stackId = StackId; count = Count; revision = Revision;
            return Selected;
        }
        public void ClearSelectedMaterial() { Selected = false; Clears++; }
    }
}
namespace UnityEngine.UI {
    public class Button {
        public bool interactable = true;
        public readonly ClickEvent onClick = new();
        public class ClickEvent {
            private event Action clicked;
            public void AddListener(Action callback) { clicked += callback; }
            public void RemoveListener(Action callback) { clicked -= callback; }
            public void Invoke() { clicked?.Invoke(); }
        }
    }
}
namespace UnityEngine {
    public static class Time { public static float unscaledTime; }
    public class GameObject {
        private readonly Dictionary<Type, object> components = new();
        public T AddComponent<T>() where T : new() {
            var value = new T(); components[typeof(T)] = value; return value;
        }
        public T GetComponent<T>() where T : class {
            return components.TryGetValue(typeof(T), out var value) ? value as T : null;
        }
    }
    public class MonoBehaviour {
        public GameObject gameObject = new();
        public T GetComponent<T>() where T : class => gameObject.GetComponent<T>();
        public T FindObjectOfType<T>() where T : class =>
            Game.Inventory.InventoryModuleRuntime.Shared as T;
    }
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class DisallowMultipleComponent : Attribute {}
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class RequireComponent : Attribute {
        public RequireComponent(Type type) {}
    }
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SerializeField : Attribute {}
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class MinAttribute : Attribute {
        public MinAttribute(float value) {}
    }
    public static class Debug {
        public static void LogError(string value) {}
        public static void LogWarning(string value) {}
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
        private static readonly JsonSerializerOptions options = new() {
            IncludeFields = true
        };
        public static string ToJson(object value) =>
            JsonSerializer.Serialize(value, value.GetType(), options);
        public static T FromJson<T>(string json) {
            T result = JsonSerializer.Deserialize<T>(json, options);
            if (MaterializeExplicitNullTask
                && result is RefiningResultDto refining
                && RefiningSnapshotWireState.TryRead(json, out _, out _,
                    out _, out bool explicitNull)
                && explicitNull && refining.snapshot != null)
                refining.snapshot.activeTask = new RefiningTaskDto();
            return result;
        }
    }
}
'@
Add-Type -TypeDefinition ($stubs + "`n" + $protocol + "`n" +
    $wireState + "`n" +
    $sequence + "`n" + $calculator + "`n" + $runtime + "`n" +
    $startButton) -IgnoreWarnings | Out-Null

function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
}

$wire = [Game.Cultivation.Protocol.RefiningSnapshotWireState]
$hasSnapshot = $false
$snapshotIsNull = $false
$hasTask = $false
$taskIsNull = $false
$wireJson = '{"message":"\"activeTask\":null","history":{"activeTask":null},' +
    '"snapshot":{"activeTask":{"taskId":"running"}}}'
Assert-True ($wire::TryRead($wireJson, [ref]$hasSnapshot,
    [ref]$snapshotIsNull, [ref]$hasTask, [ref]$taskIsNull) -and
    $hasSnapshot -and $hasTask -and -not $snapshotIsNull -and
    -not $taskIsNull) 'Text and nested history cannot mask a real active task.'
$nullWire = '{"snapshot": { "activeTask" : null, "lastCompletedTask": {"taskId":"done"} }}'
Assert-True ($wire::TryRead($nullWire, [ref]$hasSnapshot,
    [ref]$snapshotIsNull, [ref]$hasTask, [ref]$taskIsNull) -and
    $hasSnapshot -and $hasTask -and $taskIsNull -and -not $snapshotIsNull) `
    'An explicit null must be distinct from the completed task object.'
Assert-True ($wire::TryRead('{"snapshot":null}', [ref]$hasSnapshot,
    [ref]$snapshotIsNull, [ref]$hasTask, [ref]$taskIsNull) -and
    $hasSnapshot -and $snapshotIsNull -and -not $hasTask) `
    'A null snapshot cannot be mistaken for an idle snapshot.'
Assert-True (-not $wire::TryRead('{"snapshot":{"activeTask":null},"snapshot":null}',
    [ref]$hasSnapshot, [ref]$snapshotIsNull,
    [ref]$hasTask, [ref]$taskIsNull)) `
    'Ambiguous duplicate snapshot fields must not clear a task.'

$state = [Game.Cultivation.Protocol.RefiningRequestSequence]
$sessionId = [Guid]::NewGuid().ToString('D')
$nextSession = [Guid]::NewGuid().ToString('D')
$state::EnterWorld($sessionId, 42)
$catalog = $null
$snapshot = $null
Assert-True ($state::TryNextQuery(42, $sessionId, 'refining_catalog', [ref]$catalog) -and
    $state::TryNextQuery(42, $sessionId, 'refining_snapshot', [ref]$snapshot)) `
    'Catalog and snapshot must use the refining sequence.'
Assert-True ($catalog.requestSequence -eq 1 -and $snapshot.requestSequence -eq 2) `
    'Query sequences must increase in this session.'
$queryJson = [UnityEngine.JsonUtility]::ToJson($catalog) | ConvertFrom-Json
Assert-True ($queryJson.msgId -eq 'refining_catalog' -and
    $queryJson.type -eq 'refining_catalog' -and
    @($queryJson.PSObject.Properties).Count -eq 3) `
    'Query wire format must contain only msgId, type and requestSequence.'

$start = $null
Assert-True ($state::TryPrepareStart(42, $sessionId, 12, 9, 123, 5, [ref]$start)) `
    'Valid start parameters must be persisted before sending.'
$json = [UnityEngine.JsonUtility]::ToJson($start) | ConvertFrom-Json
Assert-True ($json.requestSequence -eq 3 -and $json.configurationRevision -eq 12 -and
    $json.expectedInventoryRevision -eq 9 -and $json.stackId -eq 123 -and
    $json.count -eq 5 -and @($json.PSObject.Properties).Count -eq 9) `
    'Start must contain exactly the nine protocol fields.'
Assert-True (-not $state::TryPrepareStart(42, $sessionId, 12, 9, 123, 5, [ref]$null)) `
    'A second operation cannot replace an unresolved request.'
$state::Suspend()
$state::EnterWorld($nextSession, 42)
$recovery = $null
Assert-True ($state::TryGetRecoveryRequest([ref]$recovery) -and
    $recovery.clientRequestId -eq $start.clientRequestId -and
    $recovery.jingjieOperationSessionId -eq $nextSession -and
    $recovery.configurationRevision -eq 12 -and
    $recovery.expectedInventoryRevision -eq 9 -and
    $recovery.stackId -eq 123 -and $recovery.count -eq 5) `
    'Re-entry must retain the original operation identity and business parameters.'
Assert-True (-not $state::Matches(42, $sessionId, 3, $start.clientRequestId) -and
    -not $state::Complete(42, $sessionId, 3, $start.clientRequestId)) `
    'Old-session results must not complete the recovered operation.'
$now = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
$state::MarkSent($now)
$retry = $null
Assert-True ($state::TryGetSameSessionRetry(($now + 11000), 10000, [ref]$retry) -and
    [UnityEngine.JsonUtility]::ToJson($retry) -eq
        [UnityEngine.JsonUtility]::ToJson($recovery)) `
    'Timeout retries must preserve the complete request.'
Assert-True ($state::Complete(42, $nextSession, $recovery.requestSequence,
    $recovery.clientRequestId)) 'Matching result must clear pending state.'
$state::EnterWorld($sessionId, 43)
$conflicted = $null
Assert-True ($state::TryPrepareStart(43, $sessionId, 12, 9, 123, 1,
    [ref]$conflicted)) 'A separate character must receive an independent request.'
$state::MarkConflict()
$state::Suspend()
$state::EnterWorld($nextSession, 43)
$blockedRecovery = $null
Assert-True ($state::HasConflict -and
    -not $state::TryGetRecoveryRequest([ref]$blockedRecovery) -and
    -not $state::TryPrepareStart(43, $nextSession, 12, 9, 123, 1,
        [ref]$blockedRecovery)) `
    'An identity conflict must survive re-entry without automatic resend or replacement.'
Assert-True ($state::Complete(43, $nextSession, 1, $conflicted.clientRequestId)) `
    'A manually resolved conflict must permit later operations.'

$owner = [GameSession]::new()
$owner.LocalCharacterDbId = 51
$owner.JingjieOperationSessionId = [Guid]::NewGuid().ToString('D')
$owner.IsWorldReady = $true
$network = [NetworkManager]::new()
$instance = [Game.Cultivation.Runtime.RefiningSessionRuntime]::Ensure($network, $owner)
$instance.BeginSession($owner, 51, $owner.JingjieOperationSessionId)
$update = $instance.GetType().GetMethod('Update',
    [System.Reflection.BindingFlags]'Instance,NonPublic')
$network.FailNextSend = $true
$update.Invoke($instance, @()) | Out-Null
[UnityEngine.Time]::unscaledTime = 1.1
$update.Invoke($instance, @()) | Out-Null
Assert-True ($network.Sent.Count -eq 3) `
    'Failed initial query must be retried after the refresh interval.'
$network.Sent.Clear()
$instance.Refresh()
Assert-True ($network.Sent.Count -eq 2 -and
    [Game.Inventory.InventoryModuleRuntime]::Shared.Requests -eq 3) `
    'Accepted world entry must query catalog, refining snapshot and inventory.'
$catalogSeq = ($network.Sent[0] | ConvertFrom-Json).requestSequence
$snapshotSeq = ($network.Sent[1] | ConvertFrom-Json).requestSequence
$base = '"jingjieOperationSessionId":"' + $owner.JingjieOperationSessionId + '",'
$rules = '"configurationRevision":12,"rules":[{"itemId":"ling_shi","secondsPerItem":60,"cultivationPerItem":10}],'
$rules += '"breakthroughs":[{"fromJingjieId":"a","fromJingjieStage":1,"toJingjieId":"b","toJingjieStage":2,'
$rules += '"requiredCultivation":100,"materials":[]}],'
$badCatalog = '{"msgId":"refining_catalog_result","type":"refining_catalog_result",' +
    $base + '"success":true,"requestSequence":' + ($catalogSeq + 1) + ',' + $rules + '"serverTime":1}'
[NetMessageDispatcher]::Instance.Dispatch('refining_catalog_result', $badCatalog)
Assert-True ($null -eq $instance.Catalog) 'Unknown catalog sequence must be ignored.'
$goodCatalog = '{"msgId":"refining_catalog_result","type":"refining_catalog_result",' +
    $base + '"success":true,"requestSequence":' + $catalogSeq + ',' + $rules + '"serverTime":1}'
[NetMessageDispatcher]::Instance.Dispatch('refining_catalog_result', $goodCatalog)
Assert-True ($instance.Catalog.configurationRevision -eq 12 -and
    $instance.Catalog.rules[0].secondsPerItem -eq 60) `
    'Matching catalog response must be available to later UI stages.'
$snapshotValue = '{"characterDbId":51,"revision":5,"cultivation":40,"totalCultivation":50}'
$goodSnapshot = '{"msgId":"refining_snapshot_result","type":"refining_snapshot_result",' +
    $base + '"success":true,"requestSequence":' + $snapshotSeq +
    ',"snapshot":' + $snapshotValue + '}'
[NetMessageDispatcher]::Instance.Dispatch('refining_snapshot_result', $goodSnapshot)
Assert-True ($instance.Snapshot.revision -eq 5) 'Matching snapshot must be accepted.'
$oldPush = '{"msgId":"refining_changed","type":"refining_changed",' +
    $base + '"success":true,"snapshot":{"characterDbId":51,"revision":4,' +
    '"cultivation":1,"totalCultivation":1}}'
[NetMessageDispatcher]::Instance.Dispatch('refining_changed', $oldPush)
Assert-True ($instance.Snapshot.cultivation -eq 40) `
    'Older revisions must not replace a newer task snapshot.'
$wrongCharacter = '{"msgId":"refining_changed","type":"refining_changed",' +
    $base + '"success":true,"snapshot":{"characterDbId":52,"revision":6,' +
    '"cultivation":99,"totalCultivation":99}}'
[NetMessageDispatcher]::Instance.Dispatch('refining_changed', $wrongCharacter)
Assert-True ($instance.Snapshot.cultivation -eq 40) `
    'Another character must not replace the active snapshot.'
$wrongSession = '{"msgId":"refining_changed","type":"refining_changed",' +
    '"jingjieOperationSessionId":"' + $sessionId + '","success":true,' +
    '"snapshot":{"characterDbId":51,"revision":6,"cultivation":99,"totalCultivation":99}}'
[NetMessageDispatcher]::Instance.Dispatch('refining_changed', $wrongSession)
Assert-True ($instance.Snapshot.cultivation -eq 40) `
    'An old world session must not replace the active snapshot.'
$newPush = '{"msgId":"refining_changed","type":"refining_changed",' +
    $base + '"success":true,"snapshot":{"characterDbId":51,"revision":6,' +
    '"cultivation":50,"totalCultivation":60}}'
$owner.LocalProgressionSnapshot = [ProgressionSnapshotResult]::new()
$owner.LocalProgressionSnapshot.jingjieExp = 40
[NetMessageDispatcher]::Instance.Dispatch('refining_changed', $newPush)
Assert-True ($instance.Snapshot.cultivation -eq 50 -and
    $owner.ProgressionRefreshes -eq 1) `
    'A changed refining balance must refresh the separate progression snapshot.'
$newSnapshotSequence = [long]0
$onSnapshot = [System.Action[Game.Cultivation.Protocol.RefiningResultDto]] {
    param($changed)
    $null = $instance.RequestSnapshot()
    $script:newSnapshotSequence = ($network.Sent[-1] |
        ConvertFrom-Json).requestSequence
}
$instance.add_SnapshotChanged($onSnapshot)
$pushWithFollowUp = '{"msgId":"refining_changed","type":"refining_changed",' +
    $base + '"success":true,"snapshot":{"characterDbId":51,"revision":7,' +
    '"cultivation":50,"totalCultivation":60}}'
[NetMessageDispatcher]::Instance.Dispatch('refining_changed', $pushWithFollowUp)
$instance.remove_SnapshotChanged($onSnapshot)
$followingSnapshot = '{"msgId":"refining_snapshot_result","type":"refining_snapshot_result",' +
    $base + '"success":true,"requestSequence":' + $newSnapshotSequence +
    ',"snapshot":{"characterDbId":51,"revision":7,"cultivation":50,' +
    '"totalCultivation":60}}'
[NetMessageDispatcher]::Instance.Dispatch('refining_snapshot_result', $followingSnapshot)
Assert-True ($newSnapshotSequence -gt 0 -and
    $instance.LastSnapshotResult.msgId -eq 'refining_snapshot_result' -and
    $owner.ProgressionRefreshes -eq 1) `
    'A push callback must preserve a newer query and not requeue unchanged cultivation.'

$owned = [Game.Inventory.Model.V2.InventoryOwnershipSnapshot]::new()
$owned.CharacterDbId = 51
$owned.InventoryRevision = 9
$stack = [Game.Inventory.Model.V2.InventoryStackState]::new()
$stack.StackId = 123
$stack.ItemId = 'ling_shi'
$stack.Count = 8
$owned.Stacks.Add($stack)
$inventoryRuntime = [Game.Inventory.InventoryModuleRuntime]::Shared
$inventoryRuntime.CurrentSnapshot = $owned
$selection = [Game.Cultivation.Presentation.CultivationMaterialDropTarget]::new()
$startControl = [Game.Cultivation.Presentation.RefiningStartButton]::new()
$clickButton = $startControl.gameObject.AddComponent[UnityEngine.UI.Button]()
$fieldFlags = [System.Reflection.BindingFlags]'Instance,NonPublic'
$startControl.GetType().GetField('session', $fieldFlags).SetValue(
    $startControl, $owner)
$startControl.GetType().GetField('materialDropTarget', $fieldFlags).SetValue(
    $startControl, $selection)
$startControl.GetType().GetMethod('OnEnable', $fieldFlags).Invoke(
    $startControl, @()) | Out-Null
Assert-True ($clickButton.interactable -and
    $instance.CanStart(123, 5, 12)) 'The configured button must accept the selected owned stack.'
$sentBeforeStart = $network.Sent.Count
$clickButton.onClick.Invoke()
$firstStart = $network.Sent[-1] | ConvertFrom-Json
Assert-True ($network.Sent.Count -eq ($sentBeforeStart + 1) -and
    $firstStart.msgId -eq 'refining_start' -and
    $firstStart.jingjieOperationSessionId -eq $owner.JingjieOperationSessionId -and
    $firstStart.configurationRevision -eq 12 -and
    $firstStart.expectedInventoryRevision -eq 9 -and
    $firstStart.stackId -eq 123 -and $firstStart.count -eq 5 -and
    $stack.Count -eq 8 -and $owned.InventoryRevision -eq 9 -and
    -not $clickButton.interactable) `
    'Only clicking the existing button sends a full start request; inventory waits for Gate.'
$clickButton.onClick.Invoke()
Assert-True ($network.Sent.Count -eq ($sentBeforeStart + 1)) `
    'Double clicking must not create another refining operation.'
$badResult = @{
    msgId = 'refining_start_result'; type = 'refining_start_result'
    jingjieOperationSessionId = $owner.JingjieOperationSessionId
    requestSequence = $firstStart.requestSequence
    clientRequestId = [Guid]::NewGuid().ToString('D')
    success = $true; serverTime = 1790500000000
} | ConvertTo-Json -Compress
[NetMessageDispatcher]::Instance.Dispatch('refining_start_result', $badResult)
Assert-True ($stack.Count -eq 8 -and $instance.HasUnresolvedOperation) `
    'A mismatched operation ID must never change inventory.'
$firstResult = @{
    msgId = 'refining_start_result'; type = 'refining_start_result'
    jingjieOperationSessionId = $owner.JingjieOperationSessionId
    requestSequence = $firstStart.requestSequence
    clientRequestId = $firstStart.clientRequestId
    success = $true; serverTime = 1790500000000
    inventoryRevision = 10
    stackItemChanges = @(@{ stackId = 123; itemId = 'ling_shi'
        previousCount = 0; newCount = 3; isBound = $false })
    snapshot = @{ characterDbId = 51; revision = 7; cultivation = 50
        totalCultivation = 60; activeTask = @{ taskId = 'task-1'
            itemId = 'ling_shi'; count = 5; status = 'active' } }
} | ConvertTo-Json -Depth 10 -Compress
[NetMessageDispatcher]::Instance.Dispatch('refining_start_result', $firstResult)
Assert-True ($stack.Count -eq 3 -and $owned.InventoryRevision -eq 10 -and
    $inventoryRuntime.Applied -eq 1 -and
    $instance.Snapshot.activeTask.taskId -eq 'task-1' -and
    $instance.Snapshot.cultivation -eq 50 -and
    -not $instance.HasUnresolvedOperation -and
    -not $selection.Selected -and $selection.Clears -eq 1) `
    'First confirmed success must replace newCount once without granting cultivation.'
[NetMessageDispatcher]::Instance.Dispatch('refining_start_result', $firstResult)
Assert-True ($stack.Count -eq 3 -and $inventoryRuntime.Applied -eq 1 -and
    $selection.Clears -eq 1) 'Repeated success must not apply or display twice.'
$noSnapshotPush = '{"msgId":"refining_changed","type":"refining_changed",' +
    $base + '"success":true,"snapshot":null}'
[NetMessageDispatcher]::Instance.Dispatch('refining_changed', $noSnapshotPush)
Assert-True ($instance.Snapshot.activeTask.taskId -eq 'task-1') `
    'A null snapshot must not clear the existing active task.'
$olderIdlePush = '{"msgId":"refining_changed","type":"refining_changed",' +
    $base + '"success":true,"snapshot":{"characterDbId":51,"revision":6,' +
    '"cultivation":50,"totalCultivation":60,"activeTask":null}}'
[NetMessageDispatcher]::Instance.Dispatch('refining_changed', $olderIdlePush)
Assert-True ($instance.Snapshot.activeTask.taskId -eq 'task-1') `
    'An older idle snapshot must not clear a newer active task.'
$completedWithExplicitNull = '{"msgId":"refining_changed","type":"refining_changed",' +
    $base + '"success":true,"snapshot":{"characterDbId":51,"revision":8,' +
    '"cultivation":50,"totalCultivation":60,"activeTask":null,' +
    '"lastCompletedTask":{"taskId":"task-1","itemId":"ling_shi"}}}'
[UnityEngine.JsonUtility]::MaterializeExplicitNullTask = $true
[NetMessageDispatcher]::Instance.Dispatch('refining_changed', $completedWithExplicitNull)
[UnityEngine.JsonUtility]::MaterializeExplicitNullTask = $false
Assert-True ($null -eq $instance.Snapshot.activeTask -and
    $instance.Snapshot.lastCompletedTask.taskId -eq 'task-1' -and
    $instance.CanStart(123, 1, 12)) `
    'An accepted explicit null must clear a materialized empty task without hiding completed history.'
$nullQuerySent = $instance.RequestSnapshot()
$nullQuerySequence = ($network.Sent[-1] | ConvertFrom-Json).requestSequence
$nullQueryResult = '{"msgId":"refining_snapshot_result","type":"refining_snapshot_result",' +
    $base + '"success":true,"requestSequence":' + $nullQuerySequence +
    ',"snapshot":{"characterDbId":51,"revision":8,"cultivation":50,' +
    '"totalCultivation":60,"activeTask":null}}'
[UnityEngine.JsonUtility]::MaterializeExplicitNullTask = $true
[NetMessageDispatcher]::Instance.Dispatch('refining_snapshot_result', $nullQueryResult)
[UnityEngine.JsonUtility]::MaterializeExplicitNullTask = $false
Assert-True ($nullQuerySent -and $null -eq $instance.Snapshot.activeTask -and
    $instance.LastSnapshotResult.msgId -eq 'refining_snapshot_result') `
    'A current query result must also restore the idle state from explicit null.'
$completedPush = '{"msgId":"refining_changed","type":"refining_changed",' +
    $base + '"success":true,"snapshot":{"characterDbId":51,"revision":8,' +
    '"cultivation":50,"totalCultivation":60}}'
[NetMessageDispatcher]::Instance.Dispatch('refining_changed', $completedPush)
$selection.Selected = $true
$selection.Count = 1
$clickButton.onClick.Invoke()
$failedStart = $network.Sent[-1] | ConvertFrom-Json
$failureResult = @{
    msgId = 'refining_start_result'; type = 'refining_start_result'
    jingjieOperationSessionId = $owner.JingjieOperationSessionId
    requestSequence = $failedStart.requestSequence
    clientRequestId = $failedStart.clientRequestId
    success = $false; errorCode = 'refining_count_insufficient'
} | ConvertTo-Json -Compress
[NetMessageDispatcher]::Instance.Dispatch('refining_start_result', $failureResult)
Assert-True ($stack.Count -eq 3 -and -not $instance.HasUnresolvedOperation -and
    $inventoryRuntime.Reconciliations -eq 1) `
    'A definite business failure must not mutate the bag or retain an operation ID.'
$selection.Selected = $true
$network.FailNextSend = $true
$sentBeforeUncertain = $network.Sent.Count
$clickButton.onClick.Invoke()
Assert-True ($network.Sent.Count -eq $sentBeforeUncertain -and
    $instance.HasUnresolvedOperation -and -not $clickButton.interactable) `
    'An uncertain send must retain an unresolved operation and block new clicks.'
$now = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
$state::MarkSent($now - 11000)
Assert-True ($instance.TryRetryTimedOut() -and
    $network.Sent.Count -eq ($sentBeforeUncertain + 1)) `
    'Timed-out retry must send the retained request.'
$uncertain = $network.Sent[-1] | ConvertFrom-Json
Assert-True ($uncertain.clientRequestId -ne $failedStart.clientRequestId -and
    $uncertain.requestSequence -gt $failedStart.requestSequence -and
    $uncertain.expectedInventoryRevision -eq 10 -and
    $uncertain.count -eq 1) `
    'A new operation after a definite failure must use a new ID and latest revision.'
$persistFailed = @{
    msgId = 'refining_start_result'; type = 'refining_start_result'
    jingjieOperationSessionId = $owner.JingjieOperationSessionId
    requestSequence = $uncertain.requestSequence
    clientRequestId = $uncertain.clientRequestId
    success = $false; errorCode = 'refining_persist_failed'
} | ConvertTo-Json -Compress
[NetMessageDispatcher]::Instance.Dispatch('refining_start_result', $persistFailed)
Assert-True ($instance.HasUnresolvedOperation -and $stack.Count -eq 3) `
    'Persist failure must leave the same operation pending without touching inventory.'
$state::MarkSent(([DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds() - 11000))
$beforePersistRetry = $network.Sent.Count
$update.Invoke($instance, @()) | Out-Null
Assert-True ($network.Sent.Count -eq ($beforePersistRetry + 1) -and
    $network.Sent[-1] -eq ($uncertain | ConvertTo-Json -Compress)) `
    'Persist failure must automatically retry the original wire request.'
$owner.JingjieOperationSessionId = [Guid]::NewGuid().ToString('D')
$network.ConnectionEpoch = 2
$instance.BeginSession($owner, 51, $owner.JingjieOperationSessionId)
$network.Sent.Clear()
$instance.Refresh()
$resumed = $network.Sent[0] | ConvertFrom-Json
Assert-True ($network.Sent.Count -eq 1 -and $resumed.msgId -eq 'refining_start' -and
    $resumed.requestSequence -eq 1 -and
    $resumed.jingjieOperationSessionId -eq $owner.JingjieOperationSessionId -and
    $resumed.clientRequestId -eq $uncertain.clientRequestId -and
    $resumed.expectedInventoryRevision -eq $uncertain.expectedInventoryRevision -and
    $resumed.configurationRevision -eq $uncertain.configurationRevision -and
    $resumed.stackId -eq $uncertain.stackId -and
    $resumed.count -eq $uncertain.count) `
    'Re-entry must send the preserved start before any higher-sequence queries.'
$instance.Refresh()
Assert-True ($network.Sent.Count -eq 3 -and
    ($network.Sent[1] | ConvertFrom-Json).msgId -eq 'refining_catalog' -and
    ($network.Sent[2] | ConvertFrom-Json).msgId -eq 'refining_snapshot' -and
    ($network.Sent[1] | ConvertFrom-Json).requestSequence -eq 2 -and
    ($network.Sent[2] | ConvertFrom-Json).requestSequence -eq 3) `
    'Catalog and snapshot refresh must follow recovery in ascending sequence.'
$replay = @{
    msgId = 'refining_start_result'; type = 'refining_start_result'
    jingjieOperationSessionId = $owner.JingjieOperationSessionId
    requestSequence = $resumed.requestSequence
    clientRequestId = $resumed.clientRequestId
    success = $true; idempotentReplay = $true
    snapshot = $null; stackItemChanges = @(); inventoryRevision = 0
} | ConvertTo-Json -Depth 10 -Compress
$reconciliationsBeforeReplay = $inventoryRuntime.Reconciliations
[NetMessageDispatcher]::Instance.Dispatch('refining_start_result', $replay)
Assert-True (-not $instance.HasUnresolvedOperation -and
    $inventoryRuntime.Applied -eq 1 -and
    $inventoryRuntime.Reconciliations -eq ($reconciliationsBeforeReplay + 1) -and
    $owned.InventoryRevision -eq 10 -and $stack.Count -eq 3 -and
    $null -eq $instance.Snapshot -and
    ($network.Sent[-1] | ConvertFrom-Json).msgId -eq 'refining_snapshot') `
    'Idempotent replay must request both repairs without applying empty historical deltas.'
[NetMessageDispatcher]::Instance.Dispatch('refining_start_result', $replay)
Assert-True ($inventoryRuntime.Reconciliations -eq ($reconciliationsBeforeReplay + 1) -and
    $inventoryRuntime.Applied -eq 1) `
    'A repeated replay must not request another inventory repair.'
$startControl.GetType().GetMethod('OnDisable', $fieldFlags).Invoke(
    $startControl, @()) | Out-Null

$inventory = [UnityEngine.JsonUtility]::FromJson[
    Game.Cultivation.Protocol.RefiningResultDto](
    '{"stackItemChanges":[{"stackId":123,"newCount":3,"previousCount":0}]}')
Assert-True ($inventory.stackItemChanges[0].newCount -eq 3) `
    'Start inventory changes must use the absolute newCount field.'
Assert-True ($session.Contains('RefiningSessionRuntime.Ensure(networkManager, this)') -and
    $session.Contains('RefiningSessionRuntime.Instance?.BeginSession(this,') -and
    $session.Contains('RefiningSessionRuntime.Instance?.SuspendSession(this)') -and
    $panel.Contains('RefiningSessionRuntime.Instance?.Refresh()') -and
    $window.Contains('RefiningSessionRuntime.Instance?.Refresh()')) `
    'World lifecycle and both open actions must trigger the refining runtime.'
$buttonGuid = [regex]::Match($buttonMeta,
    '(?m)^guid: ([0-9a-f]{32})$').Groups[1].Value
$buttonObject = [regex]::Match($scene,
    '(?s)--- !u!1 &1676371470\r?\n(?<body>.*?)(?=\r?\n--- !u!)'
).Groups['body'].Value
$buttonComponent = [regex]::Match($scene,
    '(?s)--- !u!114 &1676371475\r?\n(?<body>.*?)(?=\r?\n--- !u!)'
).Groups['body'].Value
Assert-True ($buttonGuid -ne '' -and
    $buttonObject -match '- component: \{fileID: 1676371475\}' -and
    $buttonComponent -match "(?s)guid: $buttonGuid.*?session: \{fileID: 2108349142\}.*?materialDropTarget: \{fileID: 1887203104\}" -and
    $scene -match '(?s)--- !u!114 &1676371472\r?\n.*?m_OnClick:\r?\n    m_PersistentCalls:\r?\n      m_Calls: \[\]') `
    'The existing refining button must own the new component and exact scene references.'
Write-Output 'PASS: refining start, UI binding, ownership, failure, retry and recovery.'
