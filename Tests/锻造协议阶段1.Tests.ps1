$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$protocol = Get-Content (Join-Path $root 'Assets/Scripts/锻造/锻造协议.cs') -Raw
$runtime = Get-Content (Join-Path $root 'Assets/Scripts/锻造/锻造会话运行时.cs') -Raw
$session = Get-Content (Join-Path $root 'Assets/Scripts/GameSession.cs') -Raw
$entry = Get-Content (Join-Path $root 'Assets/Scripts/NetWork/NetMessages.cs') -Raw
$catalogState = Get-Content (Join-Path $root 'Assets/Scripts/锻造/锻造目录状态.cs') -Raw
$taskState = Get-Content (Join-Path $root 'Assets/Scripts/锻造/锻造任务状态.cs') -Raw
$startState = Get-Content (Join-Path $root 'Assets/Scripts/锻造/锻造开工状态.cs') -Raw
$claimState = Get-Content (Join-Path $root 'Assets/Scripts/锻造/锻造领取状态.cs') -Raw
$learnState = Get-Content (Join-Path $root 'Assets/Scripts/锻造/锻造学习状态.cs') -Raw
$selection = Get-Content (Join-Path $root 'Assets/Scripts/锻造/锻造交互数据.cs') -Raw
$blueprintRows = Get-Content (Join-Path $root 'Assets/Scripts/锻造/锻造图谱列表.cs') -Raw
$itemCatalog = Get-Content (Join-Path $root 'Assets/Scripts/Cultivation/Protocol/CultivationItemCatalogMessages.cs') -Raw
$ownership = Get-Content (Join-Path $root 'Assets/Scripts/Inventory/Model/V2/InventoryOwnershipSnapshot.cs') -Raw
$sources = @($protocol, $catalogState, $taskState, $startState, $claimState, $learnState,
    $selection, $blueprintRows, $itemCatalog, $ownership, $runtime) | ForEach-Object {
    $_ -replace '(?m)^using [^\r\n]+;\r?\n', ''
}
$fixture = @'
using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Cultivation.Protocol;
using Game.Forging;
using Game.Forging.Protocol;
using Game.Forging.Runtime;
using Game.Inventory;
using Game.Inventory.Model.V2;
using Game.Inventory.Protocol.V2;
using Game.Items;
using Game.UI.Notice;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public class NetMessageBase { public string msgId; public string type; }
namespace Game.Items {
    public class ItemDefinition {
        public string DisplayName;
        public Sprite Icon;
    }
    public class ItemDefinitionTable {
        public readonly Dictionary<string, ItemDefinition> Items = new();
        public bool TryGet(string id, out ItemDefinition item) {
            if (Items.TryGetValue(id, out item)) return true;
            item = new ItemDefinition { DisplayName = "默认资源", Icon = new Sprite() };
            return false;
        }
    }
}
public class ServerErrorMessage : NetMessageBase {
    public string requestMsgId;
    public string errorCode;
}
namespace Game.Inventory.Protocol.V2 {
    public class InventoryV2StackChangeDto {
        public long stackId;
        public string itemId;
        public int previousCount;
        public int newCount;
        public bool isBound;
    }
    public class InventoryV2WalletDto {
        public string currencyId;
        public long balance;
    }
}
namespace UnityEngine {
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class DisallowMultipleComponent : Attribute {}
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SerializeField : Attribute {}
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class MinAttribute : Attribute {
        public MinAttribute(float value) {}
    }
    public static class Time {
        public static float unscaledTime;
        public static float realtimeSinceStartup;
    }
    public class Sprite {}
    public static class PlayerPrefs {
        public static readonly Dictionary<string, string> Values = new();
        public static string GetString(string key, string fallback) =>
            Values.TryGetValue(key, out var value) ? value : fallback;
        public static void SetString(string key, string value) =>
            Values[key] = value;
        public static void DeleteKey(string key) => Values.Remove(key);
        public static void Save() {}
    }
    public static class JsonUtility {
        public static string ToJson(object value) =>
            JsonConvert.SerializeObject(value);
        public static T FromJson<T>(string json) =>
            JsonConvert.DeserializeObject<T>(json);
    }
    public static class Mathf {
        public static float Max(float a, float b) => Math.Max(a, b);
    }
    public class MonoBehaviour {
        public GameObject gameObject;
        public T GetComponent<T>() where T : class =>
            gameObject.GetComponent<T>();
        public T FindObjectOfType<T>() where T : class =>
            Game.Inventory.InventoryModuleRuntime.Instance as T;
    }
    public class GameObject {
        private readonly Dictionary<Type, MonoBehaviour> components = new();
        public T GetComponent<T>() where T : class =>
            components.TryGetValue(typeof(T), out var value) ? value as T : null;
        public T AddComponent<T>() where T : MonoBehaviour, new() {
            var value = new T { gameObject = this };
            components[typeof(T)] = value;
            return value;
        }
    }
    public static class Debug {
        public static readonly List<string> Warnings = new();
        public static void LogWarning(object value) =>
            Warnings.Add(value.ToString());
        public static void LogError(object value) =>
            Warnings.Add(value.ToString());
    }
}
public class NetworkManager : MonoBehaviour {
    public bool IsConnected = true;
    public long ConnectionEpoch = 1;
    public readonly List<NetMessageBase> Sent = new();
    public bool PersistedBeforeSend;
    public bool Send(NetMessageBase value) {
        if (value is 锻造开工请求 start)
            PersistedBeforeSend = UnityEngine.PlayerPrefs.GetString(
                "forging_start_in_flight_"
                    + Game.Inventory.InventoryModuleRuntime.Instance
                        ?.CurrentSnapshot?.CharacterDbId, "")
                .Contains(start.clientRequestId);
        if (value is 锻造领取请求 claim)
            PersistedBeforeSend = UnityEngine.PlayerPrefs.GetString(
                "forging_claim_in_flight_"
                    + Game.Inventory.InventoryModuleRuntime.Instance
                        ?.CurrentSnapshot?.CharacterDbId, "")
                .Contains(claim.clientRequestId);
        if (value is 锻造学习请求 learn)
            PersistedBeforeSend = UnityEngine.PlayerPrefs.GetString(
                "forging_learn_in_flight_"
                    + Game.Inventory.InventoryModuleRuntime.Instance
                        ?.CurrentSnapshot?.CharacterDbId, "")
                .Contains(learn.clientRequestId);
        Sent.Add(value);
        return true;
    }
    public event Action<long, string> OnDisconnectedWithEpoch;
    public void Disconnect(long epoch) {
        IsConnected = false;
        OnDisconnectedWithEpoch?.Invoke(epoch, "");
    }
}
public class GameSession {
    public long LocalCharacterDbId;
    public string ForgingOperationSessionId;
    public int ForgingProtocolVersion;
}
namespace Game.Inventory {
    public class InventoryModuleRuntime {
        public static InventoryModuleRuntime Instance;
        public InventoryOwnershipSnapshot CurrentSnapshot;
        public int SnapshotRequests;
        public int AppliedStarts;
        public int AppliedLearns;
        public int AppliedClaims;
        public int ReconciliationRequests;
        public void RequestSnapshot() { SnapshotRequests++; }
        public void RequestReconciliation() { ReconciliationRequests++; }
        public bool ApplyForgingStartInventoryChanges(long revision,
            InventoryV2StackChangeDto[] changes, InventoryV2WalletDto[] wallets) {
            AppliedStarts++;
            if (CurrentSnapshot == null || revision != CurrentSnapshot.InventoryRevision + 1)
                return false;
            var stacks = new List<InventoryStackState>(CurrentSnapshot.Stacks);
            var balances = new Dictionary<string, long>();
            foreach (var wallet in CurrentSnapshot.Wallets)
                balances[wallet.Key] = wallet.Value;
            foreach (var change in changes) {
                stacks.RemoveAll(value => value.StackId == change.stackId);
                if (change.newCount > 0)
                    stacks.Add(new InventoryStackState(change.stackId,
                        change.itemId, change.newCount, change.isBound));
            }
            foreach (var wallet in wallets)
                balances[wallet.currencyId] = wallet.balance;
            CurrentSnapshot = new InventoryOwnershipSnapshot(
                CurrentSnapshot.CharacterDbId, revision, 0, 24, 4, 4,
                stacks, new List<InventoryEquipmentState>(), balances,
                new InventoryAttributeBonusState(0, 0, 0));
            return true;
        }
        public bool ApplyForgingLearnInventoryChanges(long revision,
            InventoryV2StackChangeDto[] changes) {
            AppliedLearns++;
            if (CurrentSnapshot == null
                || revision != CurrentSnapshot.InventoryRevision + 1
                || changes == null || changes.Length != 1)
                return false;
            var stacks = new List<InventoryStackState>(CurrentSnapshot.Stacks);
            var change = changes[0];
            var owned = stacks.Find(value => value.StackId == change.stackId);
            if (owned == null) return false;
            stacks.Remove(owned);
            if (change.newCount > 0)
                stacks.Add(new InventoryStackState(change.stackId,
                    owned.ItemId, change.newCount, owned.IsBound));
            CurrentSnapshot = new InventoryOwnershipSnapshot(
                CurrentSnapshot.CharacterDbId, revision,
                CurrentSnapshot.EquipmentRevision, CurrentSnapshot.Capacity,
                4, 4, stacks,
                new List<InventoryEquipmentState>(CurrentSnapshot.Equipment),
                new Dictionary<string, long>(CurrentSnapshot.Wallets),
                CurrentSnapshot.AttributeBonus);
            return true;
        }
        public bool ApplyForgingClaimInventoryChanges(long revision,
            InventoryV2StackChangeDto[] stacks, 锻造装备变化[] equipment,
            InventoryV2WalletDto[] wallets) {
            AppliedClaims++;
            if (CurrentSnapshot == null
                || revision != CurrentSnapshot.InventoryRevision + 1
                || equipment == null || equipment.Length != 1)
                return false;
            var items = new List<InventoryEquipmentState>(CurrentSnapshot.Equipment);
            items.Add(new InventoryEquipmentState(equipment[0].itemInstanceId,
                equipment[0].equipmentId, equipment[0].isBound, 0));
            CurrentSnapshot = new InventoryOwnershipSnapshot(
                CurrentSnapshot.CharacterDbId, revision,
                CurrentSnapshot.EquipmentRevision, CurrentSnapshot.Capacity,
                4, 4, new List<InventoryStackState>(CurrentSnapshot.Stacks),
                items, new Dictionary<string, long>(CurrentSnapshot.Wallets),
                CurrentSnapshot.AttributeBonus);
            return true;
        }
    }
}
namespace Game.UI.Notice {
    public static class GameNoticeService {
        public static readonly List<string> Notices = new();
        public static void Show(string text) => Notices.Add(text);
    }
}
namespace Game.Cultivation.Runtime {
    public class CultivationSessionRuntime {
        public static CultivationSessionRuntime Instance;
        public void Refresh() {}
    }
}
public class NetMessageDispatcher {
    public static NetMessageDispatcher Instance;
    private readonly Dictionary<string, Action<string>> handlers = new();
    public void Register(string id, Action<string> callback) {
        if (!handlers.ContainsKey(id)) handlers[id] = null;
        handlers[id] += callback;
    }
    public void Unregister(string id, Action<string> callback) {
        if (!handlers.ContainsKey(id)) return;
        handlers[id] -= callback;
        if (handlers[id] == null) handlers.Remove(id);
    }
    public int HandlerCount(string id) =>
        handlers.TryGetValue(id, out var callback)
            ? callback.GetInvocationList().Length : 0;
    public void Dispatch(string id, string json) {
        if (handlers.TryGetValue(id, out var callback))
            callback?.Invoke(json);
    }
}
public static class ForgingFixture {
    public static readonly List<锻造响应> Received = new();
    public static void Subscribe(锻造会话运行时 runtime) =>
        runtime.MessageReceived += response => Received.Add(response);
    public static InventoryOwnershipSnapshot Bag(long character, long revision,
        long stackId, string itemId, int count, long coins) =>
        new InventoryOwnershipSnapshot(character, revision, 0, 24, 4, 4,
            new List<InventoryStackState> {
                new InventoryStackState(stackId, itemId, count, false)
            }, new List<InventoryEquipmentState>(),
            new Dictionary<string, long> { ["gold_coin"] = coins },
            new InventoryAttributeBonusState(0, 0, 0));
    public static InventoryOwnershipSnapshot BagWithEquipment(
        long character, long revision, string instanceId) =>
        new InventoryOwnershipSnapshot(character, revision, 0, 24, 4, 4,
            new List<InventoryStackState>(),
            new List<InventoryEquipmentState> {
                new InventoryEquipmentState(instanceId, "YF001", true, 0)
            }, new Dictionary<string, long>(),
            new InventoryAttributeBonusState(0, 0, 0));
}
'@
$references = @(Get-ChildItem "$PSHOME/ref" -Filter '*.dll' |
    Select-Object -ExpandProperty FullName) +
    [Newtonsoft.Json.JsonConvert].Assembly.Location
Add-Type -TypeDefinition ($fixture + "`n" + ($sources -join "`n")) `
    -ReferencedAssemblies $references -IgnoreWarnings | Out-Null

function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
}

$sessionA = '11111111-1111-4111-8111-111111111111'
$sessionB = '22222222-2222-4222-8222-222222222222'
$catalog = [Game.Forging.Protocol.锻造目录请求]::new(1)
$claim = [Game.Forging.Protocol.锻造领取请求]::new(
    $sessionA, 4, $sessionB, 9, $sessionA)
$start = [Game.Forging.Protocol.锻造开工请求]::new(
    $sessionA, 3, $sessionB, 5, 9, 'YF001_item',
    @([Game.Forging.Protocol.锻造所选材料]::new(101, 2)))
Assert-True ($catalog.msgId -eq 'forging_catalog' -and
    $catalog.type -eq $catalog.msgId -and
    $catalog.supportsCatalogBatches -and
    $start.recipeId -ceq 'YF001_item' -and
    $start.materials[0].stackId -eq 101 -and
    $claim.configurationRevision -eq 0) `
    'Request fields must match the forging wire contract.'
$parsed = [Newtonsoft.Json.JsonConvert]::DeserializeObject[
    Game.Forging.Protocol.锻造响应](
    '{"msgId":"forging_snapshot_result","type":"forging_snapshot_result",' +
    '"forgingSucceeded":null,"snapshot":{"characterDbId":42,' +
    '"currentTask":{"succeeded":null}}}')
Assert-True ($null -eq $parsed.forgingSucceeded -and
    $null -eq $parsed.snapshot.currentTask.succeeded) `
    'Nullable forging results must stay distinct from failure.'

$dispatcher = [NetMessageDispatcher]::new()
[NetMessageDispatcher]::Instance = $dispatcher
$network = [NetworkManager]::new()
$network.gameObject = [UnityEngine.GameObject]::new()
$owner = [GameSession]::new()
$owner.LocalCharacterDbId = 42
$owner.ForgingOperationSessionId = $sessionA
$owner.ForgingProtocolVersion = 1
$runtime = [Game.Forging.Runtime.锻造会话运行时]::Ensure($network, $owner)
[ForgingFixture]::Subscribe($runtime)
[Game.Forging.Runtime.锻造会话运行时]::Ensure($network, $owner) | Out-Null
Assert-True ($dispatcher.HandlerCount('forging_changed') -eq 1 -and
    $dispatcher.HandlerCount('error') -eq 1) `
    'Ensure must not double-register message handlers.'

$runtime.BeginSession($owner, 42, 3, $sessionA)
Assert-True (-not $runtime.IsSessionActive) 'Unknown version must be rejected.'
$runtime.BeginSession($owner, 42, 1, 'invalid-session')
Assert-True (-not $runtime.IsSessionActive) 'Invalid GUID must be rejected.'
$runtime.BeginSession($owner, 42, 1, $sessionA)
Assert-True ($runtime.IsSessionActive) 'Valid entry must activate forging.'
$owner.ForgingProtocolVersion = 2
Assert-True (-not $runtime.IsSessionActive) `
    'Changing versions inside a session must revoke it.'
$runtime.BeginSession($owner, 42, 2, $sessionA)
Assert-True ($runtime.IsSessionActive) 'V2 entry must activate forging.'
$owner.ForgingProtocolVersion = 1
$runtime.BeginSession($owner, 42, 1, $sessionA)

function Changed([string]$id, [long]$ownerId, [long]$sequence = 0) {
    return '{"msgId":"forging_changed","type":"forging_changed",' +
        '"forgingOperationSessionId":"' + $id + '",' +
        '"requestSequence":' + $sequence + ',"clientRequestId":"",' +
        '"snapshot":{"characterDbId":' + $ownerId + ',"revision":1}}'
}
$dispatcher.Dispatch('forging_changed', (Changed $sessionB 42))
$dispatcher.Dispatch('forging_changed', (Changed $sessionA 43))
$dispatcher.Dispatch('forging_changed', (Changed $sessionA 42 1))
Assert-True ([ForgingFixture]::Received.Count -eq 0) `
    'Old session, another character and malformed notifications must be ignored.'
$dispatcher.Dispatch('forging_changed', (Changed $sessionA 42))
Assert-True ([ForgingFixture]::Received.Count -eq 1) `
    'The current character notification must be accepted.'
$network.Disconnect(1)
$dispatcher.Dispatch('forging_changed', (Changed $sessionA 42))
Assert-True (-not $runtime.IsSessionActive -and
    [ForgingFixture]::Received.Count -eq 1) `
    'Disconnect must invalidate the old session.'

$network.IsConnected = $true
$network.ConnectionEpoch = 2
$owner.LocalCharacterDbId = 43
$owner.ForgingOperationSessionId = $sessionB
$owner.ForgingProtocolVersion = 1
$runtime.BeginSession($owner, 43, 1, $sessionB)
$dispatcher.Dispatch('forging_changed', (Changed $sessionA 42))
$dispatcher.Dispatch('forging_changed', (Changed $sessionB 43))
Assert-True ([ForgingFixture]::Received.Count -eq 2) `
    'Character switch must accept only the new session.'
$runtime.SuspendSession($owner)
Assert-True (-not $runtime.IsSessionActive) `
    'Returning to login must clear the session.'

Assert-True ($entry.Contains('public int forgingProtocolVersion;') -and
    $entry.Contains('public string forgingOperationSessionId;') -and
    $session.Contains('锻造会话运行时.Ensure(networkManager, this)') -and
    $session.Contains('锻造消息.IsSupportedVersion(result.forgingProtocolVersion)') -and
    $session.Contains('锻造会话运行时.Instance?.BeginSession(this,') -and
    ([regex]::Matches($session,
        '锻造会话运行时\.Instance\?\.SuspendSession\(this\)')).Count -ge 6) `
    'GameSession must own forging entry and every exit path.'

Write-Output 'PASS: forging protocol, session gate, route and lifecycle.'
