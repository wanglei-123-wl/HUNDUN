$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$messages = Get-Content (Join-Path $root 'Assets/Scripts/CharacterPanel/Protocol/JingjieBreakthroughMessages.cs') -Raw
$sequence = Get-Content (Join-Path $root 'Assets/Scripts/CharacterPanel/Protocol/JingjieBreakthroughRequestSequence.cs') -Raw
$runtime = Get-Content (Join-Path $root 'Assets/Scripts/CharacterPanel/Runtime/JingjieBreakthroughRuntime.cs') -Raw
$session = Get-Content (Join-Path $root 'Assets/Scripts/GameSession.cs') -Raw
$messages = $messages -replace '(?m)^using [^\r\n]+;\r?\n', ''
$sequence = $sequence -replace '(?m)^using [^\r\n]+;\r?\n', ''
$runtime = $runtime -replace '(?m)^using [^\r\n]+;\r?\n', ''

$stubs = @'
using System;
using System.Collections.Generic;
using System.Text.Json;
using Game.CharacterPanel.Protocol;
using Game.Inventory;
using Game.Inventory.Protocol.V2;
using UnityEngine;
public class NetMessageBase { public string msgId; public string type; }
public class NetworkManager : UnityEngine.MonoBehaviour {
    public bool IsConnected { get; set; }
    public long ConnectionEpoch { get; set; } = 1;
    public int SentCount;
    public event Action<long, string> OnDisconnectedWithEpoch;
    public bool Send(NetMessageBase message) { SentCount++; return true; }
    public bool SendJson(string json) => true;
}
public class ServerErrorMessage : NetMessageBase {
    public string requestMsgId;
    public string errorCode;
    public string message;
}
public class NetMessageDispatcher {
    public static NetMessageDispatcher Instance { get; set; }
    private readonly Dictionary<string, Action<string>> handlers = new();
    public void Register(string name, Action<string> handler) {
        handlers.TryGetValue(name, out var previous);
        handlers[name] = previous + handler;
    }
    public void Unregister(string name, Action<string> handler) {
        if (handlers.TryGetValue(name, out var previous))
            handlers[name] = previous - handler;
    }
    public void Dispatch(string name, string json) {
        if (handlers.TryGetValue(name, out var handler)) handler?.Invoke(json);
    }
}
public class GameSession {
    public long LocalCharacterDbId { get; set; }
    public string JingjieOperationSessionId { get; set; }
    public bool IsWorldReady { get; set; }
    public int ReconcileCount;
    public int SnapshotRequests;
    public void ReconcileJingjieBreakthrough(Game.CharacterPanel.Protocol.JingjieBreakthroughResultDto result) {
        ReconcileCount++;
    }
    public void RequestProgressionSnapshot() { SnapshotRequests++; }
    public void QueueProgressionSnapshotRefresh() { SnapshotRequests++; }
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
namespace Game.Inventory {
    public class InventoryModuleRuntime {
        public static InventoryModuleRuntime Shared = new();
        public int Reconciliations;
        public int Applications;
        public bool TryInitialize() => true;
        public void RequestReconciliation() { Reconciliations++; }
        public bool ApplyJingjieBreakthroughInventoryChanges(long revision, bool requiresSnapshot,
            Game.Inventory.Protocol.V2.InventoryV2StackChangeDto[] changes) {
            Applications++;
            return true;
        }
    }
}
namespace UnityEngine {
    public class GameObject {
        public T AddComponent<T>() where T : new() => new T();
    }
    public class MonoBehaviour {
        public GameObject gameObject = new GameObject();
        public T GetComponent<T>() => default;
        public T FindObjectOfType<T>() where T : class =>
            Game.Inventory.InventoryModuleRuntime.Shared as T;
    }
    public static class Time { public static float unscaledTime; }
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class DisallowMultipleComponent : Attribute {}
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SerializeField : Attribute {}
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class MinAttribute : Attribute {
        public MinAttribute(float value) {}
    }
    public static class Debug {
        public static void LogWarning(string message) {}
        public static void LogError(string message) {}
    }
    public static class PlayerPrefs {
        private static readonly Dictionary<string, string> Values = new Dictionary<string, string>();
        public static string GetString(string key, string fallback) =>
            Values.TryGetValue(key, out string value) ? value : fallback;
        public static void SetString(string key, string value) => Values[key] = value;
        public static void DeleteKey(string key) => Values.Remove(key);
        public static void Save() {}
    }
    public static class JsonUtility {
        private static readonly JsonSerializerOptions Options =
            new JsonSerializerOptions { IncludeFields = true };
        public static string ToJson(object value) =>
            JsonSerializer.Serialize(value, value.GetType(), Options);
        public static T FromJson<T>(string json) =>
            JsonSerializer.Deserialize<T>(json, Options);
    }
}
'@
Add-Type -TypeDefinition ($stubs + "`n" + $messages + "`n" +
    $sequence + "`n" + $runtime) -IgnoreWarnings | Out-Null

function Assert-True($value, [string]$message) {
    if (-not $value) { throw $message }
}

$state = [Game.CharacterPanel.Protocol.JingjieBreakthroughRequestSequence]
$firstSession = [Guid]::NewGuid().ToString('D')
$secondSession = [Guid]::NewGuid().ToString('D')
$state::EnterWorld($firstSession, 42)
$request = $null
Assert-True ($state::TryPrepare([ref]$request)) 'First operation must be prepared.'
$payload = [UnityEngine.JsonUtility]::ToJson($request) | ConvertFrom-Json
$parsedId = [Guid]::Empty
Assert-True ($payload.msgId -eq 'jingjie_breakthrough' -and
    $payload.type -eq 'jingjie_breakthrough' -and
    $payload.jingjieOperationSessionId -eq $firstSession -and
    $payload.requestSequence -eq 1 -and
    [Guid]::TryParse($payload.clientRequestId, [ref]$parsedId) -and
    @($payload.PSObject.Properties).Count -eq 5) `
    'Request must have exactly the server-defined five fields.'
$nextResult = [UnityEngine.JsonUtility]::FromJson[
    Game.CharacterPanel.Protocol.JingjieBreakthroughResultDto](
    '{"success":true,"jingjieId":"sample_id","jingjieStage":2,"jingjieName":"sample_two","hasNextJingjie":true,"nextJingjieId":"sample_id","nextJingjieStage":3,"nextJingjieName":"sample_three","requiredJingjieExp":200}')
Assert-True ($nextResult.jingjieName -eq 'sample_two' -and
    $nextResult.hasNextJingjie -and $nextResult.nextJingjieId -eq 'sample_id' -and
    $nextResult.nextJingjieStage -eq 3 -and
    $nextResult.nextJingjieName -eq 'sample_three' -and
    $nextResult.requiredJingjieExp -eq 200) `
    'Breakthrough result must read the current and server-provided next realm.'
$noNextResult = [UnityEngine.JsonUtility]::FromJson[
    Game.CharacterPanel.Protocol.JingjieBreakthroughResultDto](
    '{"success":true,"hasNextJingjie":false,"nextJingjieId":"","nextJingjieStage":0,"nextJingjieName":""}')
Assert-True (-not $noNextResult.hasNextJingjie -and
    $noNextResult.nextJingjieId -eq '' -and
    $noNextResult.nextJingjieStage -eq 0 -and
    $noNextResult.nextJingjieName -eq '') `
    'Breakthrough result must retain empty next realm fields when no path exists.'
$firstId = $request.clientRequestId
$another = $null
Assert-True (-not $state::TryPrepare([ref]$another)) `
    'A second click must not create another operation.'
$now = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds() + 60000
Assert-True ($state::IsTimedOut($now, 10000)) 'Unresolved operation must time out.'
$retry = $null
Assert-True ($state::TryGetSameSessionRetry($now, 10000, [ref]$retry)) `
    'A timed-out operation may be retried in the same session.'
Assert-True ($retry -eq [UnityEngine.JsonUtility]::ToJson($request)) `
    'A retry must preserve session, sequence and request ID exactly.'

$state::Suspend()
$state::EnterWorld($secondSession, 42)
Assert-True ($state::HasPending -and -not $state::TryPrepare([ref]$another)) `
    'A new world session must preserve unresolved operation and block new writes.'
Assert-True (-not $state::IsTimedOut($now, 10000)) `
    'A remapped pending operation must not time out before its recovery send.'
Assert-True (-not $state::TryGetSameSessionRetry($now, 10000, [ref]$retry)) `
    'Recovery must use the new session identity, not resend the old JSON.'
Assert-True (-not $state::Complete($firstSession, 1, $firstId)) `
    'An old-session result must not clear pending state in a new session.'
$recovery = $null
Assert-True ($state::TryGetRecoveryRequest([ref]$recovery) -and
    $recovery.jingjieOperationSessionId -eq $secondSession -and
    $recovery.requestSequence -eq 1 -and
    $recovery.clientRequestId -eq $firstId) `
    'A resumed request must use the new session and sequence with the original request ID.'
$state::MarkRecoverySent($now)
Assert-True ($state::Matches(42, $secondSession, 1, $firstId) -and
    -not $state::Matches(42, $firstSession, 1, $firstId) -and
    $state::Complete($secondSession, 1, $firstId)) `
    'Only a matching current-session result may complete a recovered request.'
Assert-True (-not $state::HasPending) 'Completion must remove pending state.'
Assert-True ($state::TryPrepare([ref]$another) -and
    $another.requestSequence -eq 2 -and
    $another.clientRequestId -ne $firstId) `
    'A later operation must have a new sequence and request ID.'
$state::Complete($secondSession, 2, $another.clientRequestId) | Out-Null

$thirdSession = [Guid]::NewGuid().ToString('D')
$owner = [GameSession]::new()
$owner.LocalCharacterDbId = 43
$owner.JingjieOperationSessionId = $thirdSession
$owner.IsWorldReady = $true
$transport = [NetworkManager]::new()
$transport.IsConnected = $true
[NetMessageDispatcher]::Instance = [NetMessageDispatcher]::new()
$runtimeInstance = [Game.CharacterPanel.Runtime.JingjieBreakthroughRuntime]::Ensure(
    $transport, $owner)
$runtimeInstance.BeginSession($owner, 43, $thirdSession)
Assert-True ($runtimeInstance.TryBreakthrough()) 'An accepted session may send a request.'
$pending = $null
Assert-True (-not $state::TryPrepare([ref]$pending)) 'A sent request blocks another click.'
$wrong = '{"msgId":"jingjie_breakthrough_result","type":"jingjie_breakthrough_result",' +
    '"jingjieOperationSessionId":"' + $thirdSession + '","characterDbId":43,' +
    '"requestSequence":1,"clientRequestId":"' + $firstId + '","success":true}'
[NetMessageDispatcher]::Instance.Dispatch('jingjie_breakthrough_result', $wrong)
Assert-True ($state::HasPending -and $owner.ReconcileCount -eq 0) `
    'An unrelated request ID cannot settle the current operation.'
$pendingField = $state.GetField('pending',
    [System.Reflection.BindingFlags]'NonPublic,Static')
$current = $pendingField.GetValue($null)
$currentId = $current.clientRequestId
$invalid = '{"msgId":"jingjie_breakthrough_result","type":"jingjie_breakthrough_result",' +
    '"jingjieOperationSessionId":"' + $thirdSession + '","characterDbId":43,' +
    '"requestSequence":1,"clientRequestId":"' + $currentId + '",' +
    '"success":true,"jingjieId":"lianqi","jingjieStage":2,"jingjieName":"sample",' +
    '"hasNextJingjie":true,"nextJingjieId":"lianqi","nextJingjieStage":3,' +
    '"nextJingjieName":"","jingjieExp":50,"requiredJingjieExp":200,"totalJingjieExp":150}'
[NetMessageDispatcher]::Instance.Dispatch('jingjie_breakthrough_result', $invalid)
Assert-True ($state::HasPending -and $owner.ReconcileCount -eq 0 -and
    $owner.SnapshotRequests -eq 1 -and
    [Game.Inventory.InventoryModuleRuntime]::Shared.Applications -eq 0) `
    'A success without a valid next realm must keep the request pending and refresh.'
$result = '{"msgId":"jingjie_breakthrough_result","type":"jingjie_breakthrough_result",' +
    '"jingjieOperationSessionId":"' + $thirdSession + '","characterDbId":43,' +
    '"requestSequence":1,"clientRequestId":"' + $currentId + '",' +
    '"success":true,"jingjieId":"lianqi","jingjieStage":2,"jingjieName":"sample",' +
    '"hasNextJingjie":true,"nextJingjieId":"lianqi","nextJingjieStage":3,' +
    '"nextJingjieName":"sample_next",' +
    '"jingjieExp":50,"requiredJingjieExp":200,"totalJingjieExp":150,' +
    '"inventoryRevision":0,"stackItemChanges":[]}'
[NetMessageDispatcher]::Instance.Dispatch('jingjie_breakthrough_result', $result)
Assert-True (-not $state::HasPending -and $owner.ReconcileCount -eq 1 -and
    [Game.Inventory.InventoryModuleRuntime]::Shared.Applications -eq 1) `
    'A valid result must settle once and hand inventory reconciliation to its module.'
[NetMessageDispatcher]::Instance.Dispatch('jingjie_breakthrough_result', $result)
Assert-True ($owner.ReconcileCount -eq 1) `
    'A duplicated result cannot apply the same operation twice.'

Assert-True ($runtimeInstance.TryBreakthrough()) 'A confirmed request unblocks a new click.'
$current = $pendingField.GetValue($null)
$replay = '{"msgId":"jingjie_breakthrough_result","type":"jingjie_breakthrough_result",' +
    '"jingjieOperationSessionId":"' + $thirdSession + '","characterDbId":43,' +
    '"requestSequence":2,"clientRequestId":"' + $current.clientRequestId + '",' +
    '"success":true,"idempotentReplay":true,"requiresInventorySnapshot":true}'
[NetMessageDispatcher]::Instance.Dispatch('jingjie_breakthrough_result', $replay)
Assert-True (-not $state::HasPending -and $owner.ReconcileCount -eq 2 -and
    [Game.Inventory.InventoryModuleRuntime]::Shared.Reconciliations -eq 1 -and
    [Game.Inventory.InventoryModuleRuntime]::Shared.Applications -eq 1) `
    'An idempotent replay requests fresh ownership instead of reapplying a historical delta.'

Assert-True ($runtimeInstance.TryBreakthrough()) 'A new click may follow a replay confirmation.'
$current = $pendingField.GetValue($null)
$owner.SnapshotRequests = 0
[NetMessageDispatcher]::Instance.Dispatch('error',
    '{"msgId":"error","requestMsgId":"chat_send","errorCode":"not_authenticated"}')
Assert-True ($state::HasPending -and $owner.SnapshotRequests -eq 0) `
    'An unrelated generic Gate error cannot affect the breakthrough request.'
[NetMessageDispatcher]::Instance.Dispatch('error',
    '{"msgId":"error","requestMsgId":"jingjie_breakthrough","errorCode":"invalid_jingjie_request"}')
Assert-True ($state::HasPending -and $owner.SnapshotRequests -eq 1) `
    'A generic breakthrough error keeps the operation for recovery and refreshes state.'
$failure = '{"msgId":"jingjie_breakthrough_result","type":"jingjie_breakthrough_result",' +
    '"jingjieOperationSessionId":"' + $thirdSession + '","characterDbId":43,' +
    '"requestSequence":3,"clientRequestId":"' + $current.clientRequestId + '",' +
    '"success":false,"errorCode":"required_items_insufficient"}'
[NetMessageDispatcher]::Instance.Dispatch('jingjie_breakthrough_result', $failure)
Assert-True (-not $state::HasPending -and $owner.ReconcileCount -eq 3 -and
    [Game.Inventory.InventoryModuleRuntime]::Shared.Applications -eq 1) `
    'A definitive failure clears the pending request without touching ownership.'

Assert-True ($runtimeInstance.TryBreakthrough()) 'A failed operation permits a fresh click.'
$current = $pendingField.GetValue($null)
$fourthSession = [Guid]::NewGuid().ToString('D')
$runtimeInstance.SuspendSession($owner)
$owner.JingjieOperationSessionId = $fourthSession
$runtimeInstance.BeginSession($owner, 43, $fourthSession)
$sentBeforeRecovery = $transport.SentCount
$update = $runtimeInstance.GetType().GetMethod(
    'Update', [System.Reflection.BindingFlags]'Instance,NonPublic')
$update.Invoke($runtimeInstance, @()) | Out-Null
Assert-True ($transport.SentCount -eq ($sentBeforeRecovery + 1) -and
    -not $state::TryGetRecoveryRequest([ref]$pending) -and
    $state::Matches(43, $fourthSession, 1, $current.clientRequestId)) `
    'Re-entry must automatically confirm the old operation with the new session ID.'

Assert-True ($session.Contains('JingjieBreakthroughRuntime.Ensure(networkManager, this)') -and
    $session.Contains('JingjieBreakthroughRuntime.Instance?.BeginSession(this,') -and
    $session.Contains('JingjieBreakthroughRuntime.Instance?.SuspendSession(this)')) `
    'Runtime must follow the existing world session lifecycle.'
Assert-True ($runtime.Contains('session.IsWorldReady')) `
    'Breakthrough sends must wait for a fully accepted world session.'
Assert-True ($runtime.Contains('JingjieBreakthroughMessageIds.Result') -and
    $runtime.Contains('HandleServerError')) `
    'Runtime must dispatch both breakthrough results and generic Gate errors.'
Write-Output 'PASS: breakthrough request, recovery, result matching and duplicate isolation.'
