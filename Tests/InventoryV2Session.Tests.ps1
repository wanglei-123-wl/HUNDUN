$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$paths = @(
    'Assets/Scripts/Inventory/Protocol/V2/InventoryV2Messages.cs'
    'Assets/Scripts/Inventory/Protocol/V2/InventoryV2SnapshotValidator.cs'
    'Assets/Scripts/Inventory/Protocol/V2/InventoryV2OperationValidator.cs'
    'Assets/Scripts/Inventory/Protocol/V2/InventoryV2Codec.cs'
    'Assets/Scripts/Inventory/Model/V2/InventoryOwnershipSnapshot.cs'
    'Assets/Scripts/Inventory/Model/V2/InventoryEquipmentChanges.cs'
    'Assets/Scripts/Inventory/Model/V2/InventoryV2DeltaChanges.cs'
    'Assets/Scripts/Inventory/Model/V2/InventoryBagLayout.cs'
    'Assets/Scripts/Inventory/Integration/V2/InventoryV2Session.cs'
    'Assets/Scripts/Cultivation/Protocol/CultivationCatalogWireState.cs'
)
$files = $paths | ForEach-Object { Get-Content (Join-Path $root $_) -Raw }
$usings = $files | ForEach-Object {
    [regex]::Matches($_, '(?m)^using [^;]+;') | ForEach-Object { $_.Value }
} | Sort-Object -Unique
$sources = "#define DEVELOPMENT_BUILD`n" + ($usings -join "`n") + "`n" +
    (($files | ForEach-Object { [regex]::Replace($_, '(?m)^using [^;]+;\r?\n', '') }) -join "`n")

$stubs = @'
public class NetMessageBase { public string msgId; public string type; }
public sealed class ServerErrorMessage : NetMessageBase
{
    public string requestMsgId;
    public string errorCode;
    public string message;
    public long expectedRequestSequence;
}
public sealed class NetworkManager
{
    public bool IsConnected = true;
    public readonly System.Collections.Generic.List<string> Sent = new();
    public bool Send<T>(T request)
    {
        Sent.Add(UnityEngine.JsonUtility.ToJson(request));
        return true;
    }
}
public sealed class NetMessageDispatcher
{
    private readonly System.Collections.Generic.Dictionary<string, System.Action<string>> handlers = new();
    public void Register(string id, System.Action<string> handler)
    {
        handlers.TryGetValue(id, out var previous);
        handlers[id] = previous + handler;
    }
    public void Unregister(string id, System.Action<string> handler)
    {
        if (handlers.TryGetValue(id, out var previous)) handlers[id] = previous - handler;
    }
    public void Dispatch(string json)
    {
        string id = UnityEngine.JsonUtility.FromJson<NetMessageBase>(json).msgId;
        if (handlers.TryGetValue(id, out var handler)) handler?.Invoke(json);
    }
}
namespace Game.PlayerTrade.Protocol
{
    public sealed class PlayerTradeInventoryChangeDto
    {
        public string type;
        public long stackId;
        public string itemId;
        public int count;
        public bool isBound;
        public string itemInstanceId;
        public string equipmentId;
        public int slotCode;
        public string currencyId;
        public long balance;
    }
}
namespace Game.Forging.Protocol
{
    public sealed class 锻造装备变化
    {
        public string itemInstanceId;
        public string itemId;
        public string equipmentId;
        public bool isBound;
        public int enhanceLevel;
        public int durability;
    }
}
namespace Game.Forging.Runtime
{
    public sealed class 锻造会话运行时
    {
        public static 锻造会话运行时 Instance;
        public bool HasUnresolvedLearn;
        public int LearnCount;
        public long LastStackId;
        public bool TryLearn(long stackId,
            Game.Inventory.Model.V2.InventoryOwnershipSnapshot ownership)
        {
            if (HasUnresolvedLearn || stackId <= 0 || ownership == null)
                return false;
            LastStackId = stackId;
            LearnCount++;
            HasUnresolvedLearn = true;
            return true;
        }
    }
}
namespace UnityEngine
{
    public class Transform { }
    public static class Time { public static float unscaledTime; }
    public static class Debug
    {
        public static readonly System.Collections.Generic.List<string> Messages = new();
        public static void Log(string message) { Messages.Add(message); }
        public static void LogWarning(string message) { }
        public static void LogError(string message) { }
    }
    public static class JsonUtility
    {
        private static readonly System.Text.Json.JsonSerializerOptions Options = new() { IncludeFields = true };
        public static T FromJson<T>(string json) => System.Text.Json.JsonSerializer.Deserialize<T>(json, Options);
        public static string ToJson(object value) => System.Text.Json.JsonSerializer.Serialize(value, Options);
    }
}
namespace Game.Items
{
    public enum ItemCategory { Other, Equipment, Consumable, ForgingBlueprint }
    public enum ItemDetailCategory { Auto, Equipment, Cultivation }
    public enum EquipmentCategoryCode
    {
        None, Weapon, Helmet, Armor, Necklace, Ring, Bracelet,
        Boots, Shield, Artifact, Formation
    }
    public sealed class ItemDefinition
    {
        public string ItemId;
        public EquipmentCategoryCode EquipmentCategory;
        public ItemCategory Category;
        public ItemDetailCategory DisplayCategory;
        public string DisplayName;
    }
    public sealed class ItemDefinitionTable
    {
        public int EquipmentCatalogVersion = 2;
        public bool TryGet(string id, out ItemDefinition item)
        {
            item = id == "hp_potion_01"
                ? new ItemDefinition { Category = ItemCategory.Consumable,
                    DisplayName = "生命药水" }
                : id == "pill_with_effect"
                    ? new ItemDefinition { Category = ItemCategory.Consumable,
                        DisplayCategory = ItemDetailCategory.Cultivation,
                        DisplayName = "待确认丹药" }
                : id == "gongfa_book"
                    ? new ItemDefinition { Category = ItemCategory.Consumable,
                        DisplayCategory = ItemDetailCategory.Cultivation,
                        DisplayName = "功法书" }
                : id == "recipe_book"
                    ? new ItemDefinition { Category = ItemCategory.Consumable,
                        DisplayCategory = ItemDetailCategory.Cultivation,
                        DisplayName = "丹方书" }
                : id == "tz001"
                    ? new ItemDefinition { Category = ItemCategory.ForgingBlueprint,
                        DisplayName = "玄铁剑图纸" } : null;
            return item != null;
        }
        public bool TryGetByEquipmentId(string id, out ItemDefinition item)
        {
            EquipmentCategoryCode category = id switch
            {
                "ring_xingyun_001" => EquipmentCategoryCode.Ring,
                "boots_test" => EquipmentCategoryCode.Boots,
                "shield_test" => EquipmentCategoryCode.Shield,
                "artifact_test" => EquipmentCategoryCode.Artifact,
                "formation_test" => EquipmentCategoryCode.Formation,
                _ => EquipmentCategoryCode.None
            };
            item = category == EquipmentCategoryCode.None
                ? null : new ItemDefinition {
                    EquipmentCategory = category, ItemId = id + "_item" };
            return item != null;
        }
    }
}
namespace Game.Cultivation.Protocol
{
    public sealed class CultivationTechniqueDto
    {
        public CultivationLevelDto[] levels;
    }
    public sealed class CultivationLevelDto
    {
        public bool hasReferenceComprehension;
        public int referenceComprehension;
    }
    public sealed class CultivationItemDefinitionDto
    {
        public string kind;
        public string learnTechniqueId;
        public string learnRecipeId;
        public string cooldownGroup;
        public bool HasUsablePillEffect;
    }
    public sealed class CultivationItemCatalogResultDto
    {
        public CultivationItemDefinitionDto[] items;
    }
    public sealed class CultivationCatalogResultDto
    {
        public CultivationTechniqueDto[] techniques;
    }
    public sealed class CultivationWriteResultDto
    {
        public bool success;
    }
}
namespace Game.Cultivation.Runtime
{
    public sealed class CultivationSessionRuntime
    {
        public static CultivationSessionRuntime Instance;
        public bool HasUnresolvedOperation;
        public bool HasTechnique = true;
        public bool HasBookDefinition = true;
        public bool HasRecipeBookDefinition = true;
        public bool HasPillEffects;
        public string SharedCooldownGroup;
        public bool IsSessionReady = true;
        public bool IsConfigurationReady = true;
        public bool HasAcceptedSnapshot = true;
        public Game.Cultivation.Protocol.CultivationItemCatalogResultDto ItemCatalog =
            new Game.Cultivation.Protocol.CultivationItemCatalogResultDto {
                items = new Game.Cultivation.Protocol.CultivationItemDefinitionDto[0] };
        public Game.Cultivation.Protocol.CultivationCatalogResultDto Catalog =
            new Game.Cultivation.Protocol.CultivationCatalogResultDto {
                techniques = new Game.Cultivation.Protocol.CultivationTechniqueDto[0] };
        public int LearnCount;
        public int RefreshCount;
        public long LearnedStackId;
        public event System.Action<Game.Cultivation.Protocol.CultivationWriteResultDto>
            LearnResultReceived;
        public event System.Action<string> LearnRejected;
        public bool TryGetItem(string itemId,
            out Game.Cultivation.Protocol.CultivationItemDefinitionDto item)
        {
            item = itemId == "gongfa_book" && HasBookDefinition
                ? new Game.Cultivation.Protocol.CultivationItemDefinitionDto {
                    kind = "techniquebook" }
                : itemId == "recipe_book" && HasRecipeBookDefinition
                    ? new Game.Cultivation.Protocol.CultivationItemDefinitionDto {
                        kind = "recipebook", learnRecipeId = "recipe_1" }
                : itemId == "hp_potion_01"
                    ? new Game.Cultivation.Protocol.CultivationItemDefinitionDto {
                        kind = "consumable", cooldownGroup = SharedCooldownGroup }
                    : itemId == "group_potion_2"
                        ? new Game.Cultivation.Protocol.CultivationItemDefinitionDto {
                            kind = "consumable", cooldownGroup = SharedCooldownGroup }
                    : itemId == "empty_potion_2"
                        ? new Game.Cultivation.Protocol.CultivationItemDefinitionDto {
                            kind = "consumable" }
                    : itemId == "empty_potion_3"
                        ? new Game.Cultivation.Protocol.CultivationItemDefinitionDto {
                            kind = "consumable" }
                    : itemId == "pill_without_effect" || itemId == "pill_with_effect"
                        ? new Game.Cultivation.Protocol.CultivationItemDefinitionDto {
                            kind = "pill", HasUsablePillEffect =
                                itemId == "pill_with_effect" && HasPillEffects }
                        : null;
            return item != null;
        }
        public bool TryResolveTechniqueBook(string itemId,
            out Game.Cultivation.Protocol.CultivationItemDefinitionDto item,
            out object technique)
        {
            TryGetItem(itemId, out item);
            technique = HasTechnique ? new object() : null;
            return item != null && technique != null;
        }
        public bool TryLearn(long stackId)
        {
            if (HasUnresolvedOperation || !HasTechnique) return false;
            LearnedStackId = stackId;
            LearnCount++;
            HasUnresolvedOperation = true;
            return true;
        }
        public void Refresh() { RefreshCount++; }
        public void Complete()
        {
            HasUnresolvedOperation = false;
            LearnResultReceived?.Invoke(
                new Game.Cultivation.Protocol.CultivationWriteResultDto {
                    success = true });
        }
        public void Reject(string code)
        {
            HasUnresolvedOperation = false;
            LearnRejected?.Invoke(code);
        }
    }
}
namespace Game.Alchemy.Protocol
{
    public sealed class AlchemyResponseDto
    {
        public bool success;
        public string errorCode;
        public string message;
    }
}
namespace Game.Alchemy.Runtime
{
    public sealed class AlchemySessionRuntime
    {
        public static AlchemySessionRuntime Instance;
        public bool IsCatalogCurrent = true;
        public bool IsSnapshotCurrent = true;
        public bool HasUnresolvedLearn;
        public long LastStackId;
        public int LearnCount;
        public event System.Action<Game.Alchemy.Protocol.AlchemyResponseDto>
            LearnResultReceived;
        public event System.Action LearnTimedOut;
        public bool RequestCatalog() => true;
        public bool RequestSnapshot() => true;
        public void EnsureLearningData() {}
        public bool TryLearn(long stackId, string itemId,
            Game.Inventory.Model.V2.InventoryOwnershipSnapshot ownership)
        {
            if (HasUnresolvedLearn || itemId != "recipe_book"
                || ownership == null || stackId <= 0) return false;
            LastStackId = stackId;
            LearnCount++;
            HasUnresolvedLearn = true;
            return true;
        }
        public void Complete()
        {
            HasUnresolvedLearn = false;
            LearnResultReceived?.Invoke(new Game.Alchemy.Protocol.AlchemyResponseDto {
                success = true });
        }
    }
}
namespace Game.Inventory
{
    public sealed class InventoryPanelView
    {
        public bool Visible;
        public bool Loading;
        public event System.Action CloseClicked;
        public void Show() => Visible = true;
        public void Hide() => Visible = false;
        public void SetLoading(bool value) => Loading = value;
    }
}
namespace Game.Inventory.Integration.V2
{
    public sealed class InventoryBagPlayerPrefsStorage { }
    public sealed class InventoryBagLocalController
    {
        public Game.Inventory.Model.V2.InventoryBagLayout Layout;
        public InventoryBagLocalController(InventoryBagPlayerPrefsStorage storage) { }
        public void Reset() => Layout = null;
        public void ApplySnapshot(Game.Inventory.Model.V2.InventoryOwnershipSnapshot snapshot) =>
            Layout = Game.Inventory.Model.V2.InventoryBagLayout.Reconcile(snapshot, Layout?.Slots);
        public bool TryDrop(int source, int target, string identity) => Layout.TryMove(source, target);
    }
}
namespace Game.Inventory.Presentation.V2
{
    public sealed class InventorySelectedItemDetailPanel
    {
        public event System.Action<string> LearnRequested;
        public event System.Action<string> UseRequested;
        public int LearnListenerCount => LearnRequested?.GetInvocationList().Length ?? 0;
        public int UseListenerCount => UseRequested?.GetInvocationList().Length ?? 0;
        public void TriggerLearn(string identity) => LearnRequested?.Invoke(identity);
        public void TriggerUse(string identity) => UseRequested?.Invoke(identity);
    }
    public sealed class InventoryBagGridPresenter
    {
        public static Game.Inventory.Model.V2.InventoryOwnershipSnapshot LastRendered;
        public static int RenderCount;
        public static int PersistCount;
        public static InventoryBagGridPresenter LastCreated;
        private readonly Game.Inventory.Integration.V2.InventoryBagLocalController bag;
        public event System.Action<int, int, string> UnequipRequested;
        public event System.Action<string> StackDoubleClicked;
        public event System.Action<string> WorldDropOffered;
        public InventoryBagGridPresenter(Game.Inventory.Integration.V2.InventoryBagLocalController controller,
            Game.Inventory.InventoryPanelView panel, Game.Items.ItemDefinitionTable items)
        {
            bag = controller;
            LastCreated = this;
        }
        public void TriggerUnequip(int slot, int index, string identity) =>
            UnequipRequested?.Invoke(slot, index, identity);
        public void TriggerDoubleClick(string identity) => StackDoubleClicked?.Invoke(identity);
        public void TriggerWorldDrop(string identity) => WorldDropOffered?.Invoke(identity);
        public string BagAt(int index) => bag.Layout.GetIdentity(index);
        public Game.Inventory.Model.V2.InventoryBagLayout CurrentLayout => bag.Layout;
        public Game.Inventory.Model.V2.InventoryBagLayout PrepareSnapshot(
            Game.Inventory.Model.V2.InventoryOwnershipSnapshot next) =>
            Game.Inventory.Model.V2.InventoryBagLayout.Reconcile(next, bag.Layout?.Slots);
        public void PresentSnapshot(Game.Inventory.Model.V2.InventoryOwnershipSnapshot next,
            Game.Inventory.Model.V2.InventoryBagLayout layout)
        {
            bag.Layout = layout;
            LastRendered = next;
            RenderCount++;
        }
        public void RestoreSnapshot(Game.Inventory.Model.V2.InventoryOwnershipSnapshot previous,
            Game.Inventory.Model.V2.InventoryBagLayout layout)
        {
            bag.Layout = layout;
            LastRendered = previous;
            RenderCount++;
        }
        public void PersistLayout() => PersistCount++;
        public void ApplySnapshot(Game.Inventory.Model.V2.InventoryOwnershipSnapshot snapshot)
        {
            bag.ApplySnapshot(snapshot);
            LastRendered = snapshot;
            RenderCount++;
        }
        public void Clear() { LastRendered = null; }
        public void RefreshCatalog() { }
        public void Dispose() { }
    }
    public sealed class InventoryEquipmentPanelView
    {
        public static InventoryEquipmentPanelView LastCreated;
        public Game.Inventory.Model.V2.InventoryOwnershipSnapshot LastRendered;
        public bool FailNextRender;
        public bool Pending;
        public event System.Action<string, int> EquipRequested;
        public event System.Action<int, int, string> MoveRequested;
        public static bool TryCreate(UnityEngine.Transform root, Game.Items.ItemDefinitionTable catalog,
            out InventoryEquipmentPanelView panel, out string error)
        {
            panel = LastCreated = new InventoryEquipmentPanelView();
            error = "";
            return true;
        }
        public void TriggerEquip(string identity, int slot) => EquipRequested?.Invoke(identity, slot);
        public void TriggerMove(int source, int target, string identity) =>
            MoveRequested?.Invoke(source, target, identity);
        public void Render(Game.Inventory.Model.V2.InventoryOwnershipSnapshot snapshot)
        {
            LastRendered = snapshot;
            if (FailNextRender)
            {
                FailNextRender = false;
                throw new System.InvalidOperationException("Simulated equipment view failure.");
            }
        }
        public void Clear() => LastRendered = null;
        public void SetPending(bool value) => Pending = value;
        public void Dispose() { }
    }
    public sealed class InventoryDiscardConfirmationView
    {
        public static InventoryDiscardConfirmationView LastCreated;
        public string Identity;
        public string Name;
    public int Count;
    public bool Visible;
    public bool Pending;
    public bool ConfirmEnabled;
    public bool Failed;
    public event System.Action<string, int> Confirmed;
    public static InventoryDiscardConfirmationView Create(Game.Inventory.InventoryPanelView panel) =>
        LastCreated = new InventoryDiscardConfirmationView();
    public void Show(string identity, string name, int count)
    {
        Identity = identity; Name = name; Count = count; Visible = true;
        Pending = Failed = ConfirmEnabled = false;
    }
    public void SetConfirmEnabled(bool value) => ConfirmEnabled = value && !Pending;
    public void SetPending(bool value) { Pending = value; ConfirmEnabled = !value; }
    public void ShowFailure() { SetPending(false); Failed = true; }
    public void TriggerConfirm(int count)
    {
        if (Visible && ConfirmEnabled && !Pending) Confirmed?.Invoke(Identity, count);
    }
    public void Hide() { Visible = false; Pending = false; }
    public void Dispose() => Visible = false;
    }
}
'@
Add-Type -TypeDefinition ($sources + "`n" + $stubs) -IgnoreWarnings

function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
}

$nullableParser = [Game.Cultivation.Protocol.CultivationCatalogWireState]
$current = [System.Nullable[long]]$null
Assert-True ($nullableParser::TryReadOptionalNonNegativeLong(
    '{"jingjieExp":0}', 'jingjieExp', [ref]$current) -and
    $current -eq 0) 'An explicit zero balance must remain present.'
Assert-True ($nullableParser::TryReadOptionalNonNegativeLong(
    '{"jingjieExp":null}', 'jingjieExp', [ref]$current) -and
    $null -eq $current) 'A null balance must not become zero.'
Assert-True ($nullableParser::TryReadOptionalNonNegativeLong(
    '{}', 'jingjieExp', [ref]$current) -and
    $null -eq $current) 'A missing balance must remain absent.'
Assert-True (-not $nullableParser::TryReadOptionalNonNegativeLong(
    '{"jingjieExp":-1}', 'jingjieExp', [ref]$current)) `
    'A negative balance must not be applied.'

$network = [NetworkManager]::new()
$dispatcher = [NetMessageDispatcher]::new()
$panel = [Game.Inventory.InventoryPanelView]::new()
$items = [Game.Items.ItemDefinitionTable]::new()
$equipmentSessionId = '0769791c-1234-4abc-8def-123456789abc'
$nextEquipmentSessionId = 'b93152cd-6e21-4dc9-8da7-98cbda0832a1'
$script:attributeRefreshCount = 0
$attributeRefresh = [System.Action]{ $script:attributeRefreshCount++ }
$session = [Game.Inventory.Integration.V2.InventoryV2Session]::new(
    $network, $dispatcher, $panel, $items, $null, $null, $null, $null,
    $null, $attributeRefresh, $null)
$session.BeginSession(123, 'world_001', $equipmentSessionId)
$push = '{"msgId":"inventory_snapshot_result","type":"inventory_snapshot_result","equipmentProtocolVersion":2,"equipmentCatalogVersion":2,"requestSequence":0,"success":true,"characterDbId":123,"capacity":60,"inventoryRevision":4,"equipmentRevision":0,"stackItems":[],"equipmentItems":[],"wallets":[],"attributeBonus":{"maxHp":0,"attack":0,"defense":0},"serverTime":1790470800000,"reason":"enter_world"}'
$dispatcher.Dispatch($push)
$presenter = [Game.Inventory.Presentation.V2.InventoryBagGridPresenter]
Assert-True ($presenter::LastRendered.Capacity -eq 60 -and $presenter::RenderCount -eq 1) 'Unsolicited snapshot must render empty capacity before opening the panel.'
$session.Open()
Assert-True ($panel.Visible -and $network.Sent.Count -eq 0) 'Opening a cached inventory must not send a request.'

[UnityEngine.Time]::unscaledTime = 2
$session.RequestSnapshot()
Assert-True ($network.Sent.Count -eq 1 -and $network.Sent[0] -match '"requestSequence":1') 'Active snapshot request must use a positive sequence.'
$response = $push.Replace('"requestSequence":0', '"requestSequence":1').Replace('"inventoryRevision":4', '"inventoryRevision":5')
$dispatcher.Dispatch($response)
Assert-True ($presenter::LastRendered.InventoryRevision -eq 5 -and $presenter::RenderCount -eq 3) 'Matching requested snapshot must update the panel.'
$stale = $push.Replace('"inventoryRevision":4', '"inventoryRevision":3')
$dispatcher.Dispatch($stale)
Assert-True ($presenter::LastRendered.InventoryRevision -eq 5) 'Stale push must not overwrite newer ownership.'
$otherScene = $push.Replace('"inventoryRevision":4', '"inventoryRevision":20').Replace('"reason":"enter_world"', '"sceneId":"another_scene","reason":"enter_world"')
$dispatcher.Dispatch($otherScene)
Assert-True ($presenter::LastRendered.InventoryRevision -eq 5) 'Snapshot with another scene must not overwrite this character.'
$dispatcher.Dispatch($response.Replace('"requestSequence":1', '"requestSequence":2'))
Assert-True ($presenter::RenderCount -eq 3) 'Unmatched requested response must be ignored.'
[UnityEngine.Time]::unscaledTime = 3
$session.RequestSnapshot()
Assert-True ($network.Sent.Count -eq 2 -and $network.Sent[1] -match '"requestSequence":2') 'Next request must advance the session sequence.'
$repair = $push.Replace('"inventoryRevision":4', '"inventoryRevision":6').Replace('"reason":"enter_world"', '"reason":"server_long_unacknowledged"')
$dispatcher.Dispatch($repair)
Assert-True ($presenter::LastRendered.InventoryRevision -eq 6 -and -not $panel.Loading) 'Repair push must settle an outstanding request.'
$dispatcher.Dispatch($response.Replace('"requestSequence":1', '"requestSequence":2'))
Assert-True ($presenter::LastRendered.InventoryRevision -eq 6) 'Late response must not replace a newer repair push.'
$legacy = '{"msgId":"inventory_snapshot_result","type":"inventory_snapshot_result","capacity":40,"slots":[]}'
$dispatcher.Dispatch($legacy)
Assert-True ($presenter::LastRendered.InventoryRevision -eq 6) 'Legacy snapshot must not enter the V2 store.'

$ringA = 'ring-instance-a'
$ringB = 'ring-instance-b'
$equipmentView = [Game.Inventory.Presentation.V2.InventoryEquipmentPanelView]::LastCreated
$bagView = [Game.Inventory.Presentation.V2.InventoryBagGridPresenter]::LastCreated
$equipmentSnapshot = @{
    msgId = 'inventory_snapshot_result'; type = 'inventory_snapshot_result'
    equipmentProtocolVersion = 2; equipmentCatalogVersion = 2
    requestSequence = 0; success = $true; characterDbId = 123
    capacity = 60; inventoryRevision = 10; equipmentRevision = 2
    stackItems = @(); equipmentItems = @(
        @{ itemInstanceId = $ringA; equipmentId = 'ring_xingyun_001'; isBound = $false; slotCode = 0 }
        @{ itemInstanceId = $ringB; equipmentId = 'ring_xingyun_001'; isBound = $true; slotCode = 6 }
    )
    wallets = @(); attributeBonus = @{ maxHp = 60; attack = 0; defense = 0 }
    serverTime = 1790500000000; reason = 'test'
} | ConvertTo-Json -Depth 10 -Compress
$dispatcher.Dispatch($equipmentSnapshot)
Assert-True ($equipmentView.LastRendered.Equipment.Count -eq 2 -and
    $presenter::LastRendered.EquipmentRevision -eq 2) 'Two independent rings must enter the equipment store.'
$layoutBeforeFailure = $bagView.CurrentLayout
$persistBeforeFailure = $presenter::PersistCount
$failedRender = $equipmentSnapshot | ConvertFrom-Json
$failedRender.inventoryRevision = 11
$failedRender.equipmentItems[0].itemInstanceId = 'ring-instance-c'
$failedRenderSnapshot = $failedRender | ConvertTo-Json -Depth 10 -Compress
$equipmentView.FailNextRender = $true
$dispatcher.Dispatch($failedRenderSnapshot)
$authorityField = [Game.Inventory.Integration.V2.InventoryV2Session].GetField(
    'snapshot', [System.Reflection.BindingFlags]'Instance,NonPublic')
Assert-True ($presenter::LastRendered.InventoryRevision -eq 10 -and
    $equipmentView.LastRendered.InventoryRevision -eq 10 -and
    $authorityField.GetValue($session).InventoryRevision -eq 10 -and
    [object]::ReferenceEquals($bagView.CurrentLayout, $layoutBeforeFailure) -and
    $presenter::PersistCount -eq $persistBeforeFailure) 'Failed equipment render must restore both views and leave local layout unsaved.'

function Get-SlotCode([string]$identity) {
    foreach ($item in $equipmentView.LastRendered.Equipment) {
        if ($item.ItemInstanceId -eq $identity) { return $item.SlotCode }
    }
    throw "Missing equipment instance $identity"
}
function Send-EquipmentResult(
    [long]$sequence, [string]$operation, [bool]$success,
    [long]$inventoryRevision, [long]$equipmentRevision,
    [array]$changes, [string]$errorCode = ''
) {
    $result = @{
        msgId = 'equipment_operation_result'; type = 'equipment_operation_result'
        equipmentOperationSessionId = $equipmentSessionId
        requestSequence = $sequence; operation = $operation; success = $success
        characterDbId = 123; inventoryRevision = $inventoryRevision
        equipmentRevision = $equipmentRevision; equipmentItemChanges = $changes
        attributeBonus = @{ maxHp = 60; attack = 0; defense = 0 }
        requiresSnapshot = $false; errorCode = $errorCode; message = ''
        serverTime = 1790500000000
    } | ConvertTo-Json -Depth 10 -Compress
    $dispatcher.Dispatch($result)
}
function Ring-Change([string]$id, [int]$slot) {
    return @{ itemInstanceId = $id; equipmentId = 'ring_xingyun_001'
        isBound = ($id -eq $ringB); slotCode = $slot }
}

$equipmentView.TriggerEquip("e:$ringA", 5)
$equipRequest = $network.Sent[-1] | ConvertFrom-Json
Assert-True ($equipRequest.msgId -eq 'equipment_equip' -and
    $equipRequest.equipmentOperationSessionId -eq $equipmentSessionId -and
    $equipRequest.requestSequence -eq 1 -and
    $equipRequest.itemInstanceId -eq $ringA -and $equipRequest.targetSlotCode -eq 5 -and
    (Get-SlotCode $ringA) -eq 0 -and $equipmentView.Pending) 'Equip must wait for Gate before changing ownership.'
$sentCount = $network.Sent.Count
$equipmentView.TriggerEquip("e:$ringA", 5)
Assert-True ($network.Sent.Count -eq $sentCount) 'Pending equip must prevent duplicate requests.'
Send-EquipmentResult $equipRequest.requestSequence 'equip' $false 10 2 @() 'equipment_slot_incompatible'
Assert-True ((Get-SlotCode $ringA) -eq 0 -and -not $equipmentView.Pending -and
    $network.Sent.Count -eq $sentCount -and
    $script:attributeRefreshCount -eq 0) 'Failed equip must leave ownership and final attributes unchanged.'

$equipmentView.TriggerEquip("e:$ringA", 5)
$equipRequest = $network.Sent[-1] | ConvertFrom-Json
Assert-True ($equipRequest.requestSequence -eq 2) `
    'A new equip after a failed result must use the next equipment sequence.'
Send-EquipmentResult $equipRequest.requestSequence 'equip' $true 11 3 @((Ring-Change $ringA 5))
Assert-True ((Get-SlotCode $ringA) -eq 5 -and (Get-SlotCode $ringB) -eq 6 -and
    $equipmentView.LastRendered.InventoryRevision -eq 11 -and -not $equipmentView.Pending -and
    $script:attributeRefreshCount -eq 1) 'Successful equip must commit and refresh final attributes.'
Send-EquipmentResult $equipRequest.requestSequence 'equip' $true 11 3 @((Ring-Change $ringA 5))
Assert-True ($equipmentView.LastRendered.EquipmentRevision -eq 3 -and
    $script:attributeRefreshCount -eq 1) 'Repeated success must not apply or refresh twice.'

$equipmentView.TriggerMove(5, 6, "e:$ringA")
$moveRequest = $network.Sent[-1] | ConvertFrom-Json
Assert-True ($moveRequest.msgId -eq 'equipment_slot_move' -and
    $moveRequest.equipmentOperationSessionId -eq $equipmentSessionId -and
    $moveRequest.requestSequence -eq 3 -and
    $moveRequest.sourceSlotCode -eq 5 -and $moveRequest.targetSlotCode -eq 6 -and
    (Get-SlotCode $ringA) -eq 5 -and (Get-SlotCode $ringB) -eq 6) 'Ring swap must use stable slot codes and wait for Gate.'
Send-EquipmentResult $moveRequest.requestSequence 'slot_move' $true 11 4 @(
    (Ring-Change $ringA 6), (Ring-Change $ringB 5))
Assert-True ((Get-SlotCode $ringA) -eq 6 -and (Get-SlotCode $ringB) -eq 5 -and
    $equipmentView.LastRendered.InventoryRevision -eq 11) 'Both rings must swap atomically on success.'

$sentCount = $network.Sent.Count
$equipmentView.TriggerMove(5, 7, "e:$ringB")
Assert-True ($network.Sent.Count -eq $sentCount) 'Ring-to-bracelet move must not be sent.'
$items.EquipmentCatalogVersion = 3
$equipmentView.TriggerMove(5, 7, "e:$ringB")
Assert-True ($network.Sent.Count -eq ($sentCount + 1)) 'Mismatched local catalog must leave slot validation to Gate.'
$items.EquipmentCatalogVersion = 2
Send-EquipmentResult (($network.Sent[-1] | ConvertFrom-Json).requestSequence) 'slot_move' $false 11 4 @() 'equipment_slot_incompatible'
Assert-True ((Get-SlotCode $ringB) -eq 5) 'Gate rejection must preserve equipment ownership.'
$bagView.TriggerUnequip(6, 4, "e:$ringA")
$unequipRequest = $network.Sent[-1] | ConvertFrom-Json
Assert-True ($unequipRequest.msgId -eq 'equipment_unequip' -and
    $unequipRequest.equipmentOperationSessionId -eq $equipmentSessionId -and
    $unequipRequest.requestSequence -eq 5 -and
    $unequipRequest.slotCode -eq 6 -and (Get-SlotCode $ringA) -eq 6 -and
    $null -eq $bagView.BagAt(4)) 'Unequip must wait for Gate and not send the local bag index.'
Send-EquipmentResult $unequipRequest.requestSequence 'unequip' $true 12 5 @((Ring-Change $ringA 0))
Assert-True ((Get-SlotCode $ringA) -eq 0 -and (Get-SlotCode $ringB) -eq 5 -and
    $bagView.BagAt(4) -eq "e:$ringA") 'Successful unequip must put the instance at the chosen local bag position.'

$stackChange = [Game.Inventory.Protocol.V2.InventoryV2StackChangeDto]::new()
$stackChange.stackId = 17
$stackChange.itemId = 'hp_potion_01'
$stackChange.previousCount = 0
$stackChange.newCount = 2
$ringC = [Game.Inventory.Protocol.V2.InventoryV2EquipmentDto]::new()
$ringC.itemInstanceId = 'ring-instance-c'
$ringC.equipmentId = 'ring_xingyun_001'
$ringC.slotCode = 0
$walletChange = [Game.Inventory.Protocol.V2.InventoryV2WalletChangeDto]::new()
$walletChange.currencyId = 'gold_coin'
$walletChange.previousBalance = 0
$walletChange.newBalance = 7
$stackChanges = [Game.Inventory.Protocol.V2.InventoryV2StackChangeDto[]]@($stackChange)
$equipmentChanges = [Game.Inventory.Protocol.V2.InventoryV2EquipmentDto[]]@($ringC)
$walletChanges = [Game.Inventory.Protocol.V2.InventoryV2WalletChangeDto[]]@($walletChange)
$sentCount = $network.Sent.Count
$session.ApplyPickupDelta(13, $stackChanges, $equipmentChanges, $walletChanges)
Assert-True ($equipmentView.LastRendered.InventoryRevision -eq 13 -and
    $equipmentView.LastRendered.Equipment.Count -eq 3 -and
    $equipmentView.LastRendered.Wallets['gold_coin'] -eq 7 -and
    $network.Sent.Count -eq $sentCount) 'Final pickup must apply V2 changes without a redundant snapshot request.'
$session.ApplyPickupDelta(13, $stackChanges, $equipmentChanges, $walletChanges)
Assert-True ($presenter::LastRendered.Stacks.Count -eq 1 -and
    $network.Sent.Count -eq $sentCount) 'Repeated final pickup must not add or request twice.'
$session.ApplyPickupDelta(15, $stackChanges, $equipmentChanges, $walletChanges)
$session.ApplyPickupDelta(15, $stackChanges, $equipmentChanges, $walletChanges)
Assert-True ($presenter::LastRendered.InventoryRevision -eq 13 -and
    $network.Sent.Count -eq $sentCount) 'Revision gap must not mutate ownership or bypass the request throttle.'
[UnityEngine.Time]::unscaledTime = 5
$session.Tick()
$repairRequest = $network.Sent[-1] | ConvertFrom-Json
Assert-True ($network.Sent.Count -eq ($sentCount + 1) -and
    $repairRequest.msgId -eq 'inventory_snapshot' -and
    $repairRequest.requestSequence -gt 0) 'Repeated revision gaps must coalesce into one numbered V2 snapshot request.'
$session.Dispose()

foreach ($case in @(
    @{ Slot = 9; Equipment = 'boots_test' }
    @{ Slot = 10; Equipment = 'shield_test' }
    @{ Slot = 11; Equipment = 'artifact_test' }
    @{ Slot = 12; Equipment = 'formation_test' }
)) {
    $slot = $case.Slot
    $equipmentId = $case.Equipment
    $character = 600 + $slot
    $bagId = "bag-$slot"
    $wornId = "worn-$slot"
    $twelveNetwork = [NetworkManager]::new()
    $twelveDispatcher = [NetMessageDispatcher]::new()
    $twelvePanel = [Game.Inventory.InventoryPanelView]::new()
    $twelveItems = [Game.Items.ItemDefinitionTable]::new()
    $twelveItems.EquipmentCatalogVersion = 4
    $script:twelveRefreshes = 0
    $notices = [System.Collections.Generic.List[string]]::new()
    $twelveSession = [Game.Inventory.Integration.V2.InventoryV2Session]::new(
        $twelveNetwork, $twelveDispatcher, $twelvePanel, $twelveItems, $null,
        $null, $null, $null, $null,
        [System.Action]{ $script:twelveRefreshes++ },
        [System.Action[string]]{ param($message) $notices.Add($message) })
    $twelveSession.BeginSession($character, 'world_001', $equipmentSessionId)
    $snapshot = @{
        msgId = 'inventory_snapshot_result'; type = 'inventory_snapshot_result'
        equipmentProtocolVersion = 3; equipmentCatalogVersion = 4
        requestSequence = 0; success = $true; characterDbId = $character
        sceneId = 'world_001'; capacity = 1; inventoryRevision = 1; equipmentRevision = 1
        stackItems = @(); equipmentItems = @(
            @{ itemInstanceId = $bagId; equipmentId = $equipmentId; slotCode = 0; isBound = $false }
            @{ itemInstanceId = $wornId; equipmentId = $equipmentId; slotCode = $slot; isBound = $false }
        )
        wallets = @()
        attributeBonus = @{
            maxHp = 10; attack = 1; defense = 2; maxMp = 3
            critRateBps = 100; critDamageBps = 15000
            magicAttack = 4; magicDefense = 5
        }
        serverTime = 1790500000000; reason = 'enter_world'
    }
    $twelveDispatcher.Dispatch(($snapshot | ConvertTo-Json -Depth 10 -Compress))
    $view = [Game.Inventory.Presentation.V2.InventoryEquipmentPanelView]::LastCreated
    $bag = [Game.Inventory.Presentation.V2.InventoryBagGridPresenter]::LastCreated
    Assert-True ($view.LastRendered.Equipment.Count -eq 2 -and
        $bag.BagAt(0) -eq "e:$bagId") "Slot $slot must load with a full bag."
    $wrongSlot = if ($slot -eq 9) { 10 } else { 9 }
    $view.TriggerEquip("e:$bagId", $wrongSlot)
    Assert-True ($twelveNetwork.Sent.Count -eq 0) `
        "Slot $slot must reject locally known incompatible categories."
    $view.TriggerEquip("e:$bagId", $slot)
    $equip = $twelveNetwork.Sent[-1] | ConvertFrom-Json
    Assert-True ($equip.msgId -eq 'equipment_equip' -and
        $equip.itemInstanceId -eq $bagId -and $equip.targetSlotCode -eq $slot -and
        $bag.BagAt(0) -eq "e:$bagId") `
        "Full-bag replacement for slot $slot must send the instance ID without predicting state."
    $success = @{
        msgId = 'equipment_operation_result'; type = 'equipment_operation_result'
        equipmentOperationSessionId = $equipmentSessionId
        requestSequence = $equip.requestSequence; operation = 'equip'; success = $true
        characterDbId = $character; inventoryRevision = 2; equipmentRevision = 2
        equipmentItemChanges = @(
            @{ itemInstanceId = $bagId; equipmentId = $equipmentId; slotCode = $slot; isBound = $false }
            @{ itemInstanceId = $wornId; equipmentId = $equipmentId; slotCode = 0; isBound = $false }
        )
        attributeBonus = @{
            maxHp = 20; attack = 6; defense = 7; maxMp = 8
            critRateBps = 200; critDamageBps = 16000
            magicAttack = 9; magicDefense = 10
        }
        requiresSnapshot = $false; errorCode = ''; message = ''
        serverTime = 1790500000001
    } | ConvertTo-Json -Depth 10 -Compress
    $twelveDispatcher.Dispatch($success)
    Assert-True ($bag.BagAt(0) -eq "e:$wornId" -and
        @($view.LastRendered.Equipment | Where-Object {
            $_.ItemInstanceId -eq $bagId -and $_.SlotCode -eq $slot
        }).Count -eq 1 -and
        $view.LastRendered.AttributeBonus.MaxMp -eq 8 -and
        $view.LastRendered.AttributeBonus.CritRateBps -eq 200 -and
        $script:twelveRefreshes -eq 1) `
        "Slot $slot must atomically replace at full capacity and refresh attributes once."
    $twelveDispatcher.Dispatch($success)
    Assert-True ($script:twelveRefreshes -eq 1) `
        "Replayed success must not refresh attributes twice for slot $slot."
    $bag.TriggerUnequip($slot, 0, "e:$bagId")
    $unequip = $twelveNetwork.Sent[-1] | ConvertFrom-Json
    Assert-True ($unequip.msgId -eq 'equipment_unequip' -and
        $unequip.slotCode -eq $slot -and $unequip.requestSequence -eq 2) `
        "Full-bag unequip must send the equipped slot $slot."
    $failure = @{
        msgId = 'equipment_operation_result'; type = 'equipment_operation_result'
        equipmentOperationSessionId = $equipmentSessionId
        requestSequence = $unequip.requestSequence; operation = 'unequip'; success = $false
        characterDbId = $character; inventoryRevision = 2; equipmentRevision = 2
        equipmentItemChanges = @(); errorCode = 'equipment_inventory_full'
        requiresSnapshot = $false; message = ''
    } | ConvertTo-Json -Depth 10 -Compress
    $twelveDispatcher.Dispatch($failure)
    Assert-True ($bag.BagAt(0) -eq "e:$wornId" -and
        $view.LastRendered.EquipmentRevision -eq 2 -and
        $notices.Contains('背包已满，无法卸下装备') -and
        $script:twelveRefreshes -eq 1) `
        "Rejected full-bag unequip must preserve slot $slot and show a useful message."
    $twelveSession.BeginSession($character, 'world_001', $equipmentSessionId)
    $restored = $snapshot.Clone()
    $restored.inventoryRevision = 3
    $restored.equipmentRevision = 3
    $restored.equipmentItems[0].slotCode = $slot
    $restored.equipmentItems[1].slotCode = 0
    $twelveDispatcher.Dispatch(($restored | ConvertTo-Json -Depth 10 -Compress))
    Assert-True ($view.LastRendered.EquipmentRevision -eq 3 -and
        $bag.BagAt(0) -eq "e:$wornId" -and
        @($view.LastRendered.Equipment | Where-Object {
            $_.ItemInstanceId -eq $bagId -and $_.SlotCode -eq $slot
        }).Count -eq 1) `
        "Relogin snapshot must restore slot $slot from server ownership."
    $room = $restored.Clone()
    $room.inventoryRevision = 4
    $room.equipmentRevision = 4
    $room.equipmentItems = @($room.equipmentItems[0])
    $twelveDispatcher.Dispatch(($room | ConvertTo-Json -Depth 10 -Compress))
    Assert-True ($null -eq $bag.BagAt(0)) `
        "Server snapshot must free the bag before unequipping slot $slot."
    $bag.TriggerUnequip($slot, 0, "e:$bagId")
    $freeUnequip = $twelveNetwork.Sent[-1] | ConvertFrom-Json
    Assert-True ($freeUnequip.msgId -eq 'equipment_unequip' -and
        $freeUnequip.slotCode -eq $slot -and $freeUnequip.requestSequence -eq 1) `
        "Slot $slot must send a new-session unequip after the bag has room."
    $unequipSuccess = @{
        msgId = 'equipment_operation_result'; type = 'equipment_operation_result'
        equipmentOperationSessionId = $equipmentSessionId
        requestSequence = $freeUnequip.requestSequence
        operation = 'unequip'; success = $true
        characterDbId = $character; inventoryRevision = 5; equipmentRevision = 5
        equipmentItemChanges = @(
            @{ itemInstanceId = $bagId; equipmentId = $equipmentId; slotCode = 0; isBound = $false }
        )
        attributeBonus = @{
            maxHp = 0; attack = 0; defense = 0; maxMp = 0
            critRateBps = 0; critDamageBps = 0
            magicAttack = 0; magicDefense = 0
        }
        requiresSnapshot = $false; errorCode = ''; message = ''
        serverTime = 1790500000002
    } | ConvertTo-Json -Depth 10 -Compress
    $twelveDispatcher.Dispatch($unequipSuccess)
    Assert-True ($bag.BagAt(0) -eq "e:$bagId" -and
        @($view.LastRendered.Equipment | Where-Object SlotCode -eq $slot).Count -eq 0 -and
        $view.LastRendered.AttributeBonus.MaxMp -eq 0 -and
        $script:twelveRefreshes -eq 2) `
        "Successful unequip must empty slot $slot, return the item and replace bonuses."
    $twelveSession.Dispose()
}
[UnityEngine.Debug]::Messages.Clear()

$fullNetwork = [NetworkManager]::new()
$fullDispatcher = [NetMessageDispatcher]::new()
$fullPanel = [Game.Inventory.InventoryPanelView]::new()
$fullSession = [Game.Inventory.Integration.V2.InventoryV2Session]::new(
    $fullNetwork, $fullDispatcher, $fullPanel, $items, $null,
    $null, $null, $null, $null, $null, $null)
$fullSession.BeginSession(456, 'world_001')
$fullSnapshot = @{
    msgId = 'inventory_snapshot_result'; type = 'inventory_snapshot_result'
    equipmentProtocolVersion = 2; equipmentCatalogVersion = 2
    requestSequence = 0; success = $true; characterDbId = 456
    sceneId = 'world_001'; capacity = 3; inventoryRevision = 1; equipmentRevision = 1
    stackItems = @(@{ stackId = 101; itemId = 'potion'; count = 2; isBound = $false })
    equipmentItems = @(
        @{ itemInstanceId = 'ring-a'; equipmentId = 'ring_xingyun_001'; slotCode = 0; isBound = $false }
        @{ itemInstanceId = 'ring-b'; equipmentId = 'ring_xingyun_001'; slotCode = 5; isBound = $false }
    )
    wallets = @(@{ currencyId = 'gold_coin'; balance = 100 })
    attributeBonus = @{ maxHp = 10; attack = 1; defense = 2 }
    serverTime = 1790500000000; reason = 'enter_world'
} | ConvertTo-Json -Depth 10 -Compress
$fullDispatcher.Dispatch($fullSnapshot)
$fullBag = [Game.Inventory.Presentation.V2.InventoryBagGridPresenter]::LastCreated
Assert-True ($fullBag.BagAt(0) -eq 's:101' -and $fullBag.BagAt(1) -eq 'e:ring-a') 'Initial full snapshot must populate owned bag identities.'
$null = $fullBag.CurrentLayout.TryMove(1, 2)
$nextFullSnapshot = $fullSnapshot | ConvertFrom-Json
$nextFullSnapshot.inventoryRevision = 2
$nextFullSnapshot.equipmentRevision = 2
$nextFullSnapshot.stackItems[0].stackId = 102
$nextFullSnapshot.equipmentItems[1].itemInstanceId = 'ring-c'
$nextFullSnapshot.wallets[0].balance = 7
$nextFullSnapshot.attributeBonus.maxHp = 20
$nextFullSnapshot.reason = 'server_long_unacknowledged'
$replacement = $nextFullSnapshot | ConvertTo-Json -Depth 10 -Compress
$fullDispatcher.Dispatch($replacement)
$replaced = $authorityField.GetValue($fullSession)
Assert-True ($replaced.Stacks.Count -eq 1 -and $replaced.Stacks[0].StackId -eq 102 -and
    $replaced.Equipment.Count -eq 2 -and $replaced.Equipment[1].ItemInstanceId -eq 'ring-c' -and
    $replaced.Wallets['gold_coin'] -eq 7 -and $replaced.AttributeBonus.MaxHp -eq 20 -and
    $fullBag.BagAt(0) -eq 's:102' -and $fullBag.BagAt(2) -eq 'e:ring-a') 'Repair snapshot must replace all ownership while retaining the surviving local visual position.'
$incompleteSnapshot = $replacement | ConvertFrom-Json
$incompleteSnapshot.PSObject.Properties.Remove('wallets')
$incomplete = $incompleteSnapshot | ConvertTo-Json -Depth 10 -Compress
Assert-True ($incomplete -notmatch '"wallets"') 'Test fixture must omit wallets.'
$fullDispatcher.Dispatch($incomplete)
Assert-True ([object]::ReferenceEquals($authorityField.GetValue($fullSession), $replaced)) 'Incomplete snapshot cannot partially replace ownership.'
$fullSession.Dispose()

$useNetwork = [NetworkManager]::new()
$useDispatcher = [NetMessageDispatcher]::new()
$usePanel = [Game.Inventory.InventoryPanelView]::new()
$hpResults = [System.Collections.Generic.List[string]]::new()
$tickProvider = [System.Func[long]] { return 12345L }
$revisionProvider = [System.Func[long]] { return 7L }
$healthUpdater = [System.Action[int,long]] {
    param($hp, $revision)
    $hpResults.Add("$hp/$revision")
}
$itemUseSessionId = '783c2a95-172d-4c43-90fb-26db3dca93d1'
$useRuntime = [Game.Cultivation.Runtime.CultivationSessionRuntime]::new()
[Game.Cultivation.Runtime.CultivationSessionRuntime]::Instance = $useRuntime
$useDetail = [Game.Inventory.Presentation.V2.InventorySelectedItemDetailPanel]::new()
$useSession = [Game.Inventory.Integration.V2.InventoryV2Session]::new(
    $useNetwork, $useDispatcher, $usePanel, $items, $null,
    $tickProvider, $revisionProvider, $healthUpdater, $null, $null, $null,
    $null, $null, $useDetail)
$useSession.BeginSession(789, 'world_001', $null, $null, $itemUseSessionId)
$useSnapshot = @{
    msgId = 'inventory_snapshot_result'; type = 'inventory_snapshot_result'
    equipmentProtocolVersion = 2; equipmentCatalogVersion = 2
    requestSequence = 0; success = $true; characterDbId = 789
    sceneId = 'world_001'; capacity = 3; inventoryRevision = 122; equipmentRevision = 0
    stackItems = @(@{ stackId = 456; itemId = 'hp_potion_01'; count = 3; isBound = $false })
    equipmentItems = @(); wallets = @()
    attributeBonus = @{ maxHp = 0; attack = 0; defense = 0 }
    serverTime = 1790500000000
} | ConvertTo-Json -Depth 10 -Compress
$useDispatcher.Dispatch($useSnapshot)
$useSession.Open()
$useBag = [Game.Inventory.Presentation.V2.InventoryBagGridPresenter]::LastCreated
$confirmation = [Game.Inventory.Presentation.V2.InventoryDiscardConfirmationView]::LastCreated
Assert-True ($null -ne $presenter::LastRendered -and
    $presenter::LastRendered.Stacks.Count -eq 1 -and
    $useBag.BagAt(0) -eq 's:456') 'Item use fixture must load a V2 consumable stack.'
$useBag.TriggerWorldDrop('s:999')
Assert-True (-not $confirmation.Visible) 'Unknown bag identity cannot offer world drop.'
$useBag.TriggerWorldDrop('s:456')
Assert-True (-not $confirmation.Visible -and
    $useNetwork.Sent.Count -eq 0 -and
    $presenter::LastRendered.Stacks[0].Count -eq 3) `
    'Until the server contract is known, a stack world drop must not destroy ownership.'
$useBag.TriggerDoubleClick('s:999')
Assert-True ($useNetwork.Sent.Count -eq 0) 'Unknown stack must not send item_use.'
$useDetail.TriggerLearn('s:456')
$useDetail.TriggerUse('s:999')
Assert-True ($useNetwork.Sent.Count -eq 0) `
    'Learn button cannot consume a potion, and unknown use identity is ignored.'
$useDetail.TriggerUse('s:456')
$useRequest = $useNetwork.Sent[-1] | ConvertFrom-Json
Assert-True ($useRequest.msgId -eq 'item_use' -and $useRequest.type -eq 'item_use' -and
    $useRequest.requestSequence -eq 1 -and $useRequest.stackId -eq 456 -and
    $useRequest.expectedInventoryRevision -eq 122 -and $useRequest.count -eq 1 -and
    $useRequest.clientTick -eq 12345 -and
    $presenter::LastRendered.Stacks[0].Count -eq 3) 'Item use must send the real stack with current revision and wait for success.'
$useBag.TriggerDoubleClick('s:456')
Assert-True ($useNetwork.Sent.Count -eq 1) 'Pending item use must not send twice.'
[UnityEngine.Time]::unscaledTime = 20
$useSession.Tick()
Assert-True ($useNetwork.Sent.Count -eq 2 -and
    $useNetwork.Sent[0] -eq $useNetwork.Sent[1]) 'Timed-out item use must retry the identical request.'
$useDispatcher.Dispatch(('{"msgId":"item_use_result","requestSequence":1,"success":true,"inventoryRevision":123,"stackItemChanges":[{"stackId":456,"itemId":"hp_potion_01","previousCount":3,"newCount":2,"isBound":false}],"currentHp":80}' -replace '"requestSequence":', ('"itemUseOperationSessionId":"' + $itemUseSessionId + '","requestSequence":')))
Assert-True ($presenter::LastRendered.Stacks[0].Count -eq 2 -and
    $presenter::LastRendered.InventoryRevision -eq 123 -and
    $hpResults.Count -eq 1 -and $hpResults[0] -eq '80/7') 'Successful item use must apply stack delta and authoritative HP once.'
$useDispatcher.Dispatch(('{"msgId":"item_use_result","requestSequence":1,"success":true,"inventoryRevision":123,"stackItemChanges":[{"stackId":456,"itemId":"hp_potion_01","previousCount":3,"newCount":2,"isBound":false}],"currentHp":80}' -replace '"requestSequence":', ('"itemUseOperationSessionId":"' + $itemUseSessionId + '","requestSequence":')))
Assert-True ($presenter::LastRendered.Stacks[0].Count -eq 2 -and
    $hpResults.Count -eq 1) 'Replay must not apply a second dose.'
$useBag.TriggerDoubleClick('s:456')
$secondRequest = $useNetwork.Sent[-1] | ConvertFrom-Json
Assert-True ($secondRequest.requestSequence -eq 2 -and
    $secondRequest.expectedInventoryRevision -eq 123 -and
    $secondRequest.clientTick -gt $useRequest.clientTick) 'Next new use must increment its own sequence, revision and tick.'
$useDispatcher.Dispatch(('{"msgId":"item_use_result","requestSequence":2,"success":false,"inventoryRevision":123,"errorCode":"item_use_rejected"}' -replace '"requestSequence":', ('"itemUseOperationSessionId":"' + $itemUseSessionId + '","requestSequence":')))
Assert-True ($presenter::LastRendered.Stacks[0].Count -eq 2 -and
    $hpResults.Count -eq 1) 'Rejected use must not change inventory or health.'
$useBag.TriggerDoubleClick('s:456')
$useDispatcher.Dispatch(('{"msgId":"item_use_result","requestSequence":3,"success":true,"inventoryRevision":124,"stackItemChanges":[{"stackId":456,"itemId":"hp_potion_01","previousCount":2,"newCount":1,"isBound":false}]}' -replace '"requestSequence":', ('"itemUseOperationSessionId":"' + $itemUseSessionId + '","requestSequence":')))
Assert-True ($presenter::LastRendered.Stacks[0].Count -eq 1 -and
    $hpResults.Count -eq 1) 'Missing HP field must not overwrite combat health with zero.'
$useSession.Dispose()
Assert-True ($useDetail.LearnListenerCount -eq 0 -and
    $useDetail.UseListenerCount -eq 0) `
    'Disposing a session must detach both detail button events.'

$script:balances = [System.Collections.Generic.List[string]]::new()
$balanceUpdater = [System.Action[System.Nullable[long],System.Nullable[long]]] {
    param($current, $total)
    $script:balances.Add("$current/$total")
}
$script:progressReady = $false
$readyProvider = [System.Func[bool]] { return $script:progressReady }
$stageNetwork = [NetworkManager]::new()
$stageDispatcher = [NetMessageDispatcher]::new()
$script:stageNotices = [System.Collections.Generic.List[string]]::new()
$stageNotice = [System.Action[string]] { param($message) $script:stageNotices.Add($message) }
$stageSession = [Game.Inventory.Integration.V2.InventoryV2Session]::new(
    $stageNetwork, $stageDispatcher, [Game.Inventory.InventoryPanelView]::new(),
    $items, $null, $tickProvider, $revisionProvider, $null, $null, $null,
    $stageNotice, $balanceUpdater, $readyProvider)
$stageSession.BeginSession(789, 'world_001', $null, $null, $itemUseSessionId)
$stageSnapshot = $useSnapshot | ConvertFrom-Json
$stageSnapshot.stackItems = @(
    @{ stackId = 456; itemId = 'hp_potion_01'; count = 1; isBound = $false }
    @{ stackId = 457; itemId = 'group_potion_2'; count = 2; isBound = $false }
    @{ stackId = 458; itemId = 'empty_potion_2'; count = 2; isBound = $false }
    @{ stackId = 459; itemId = 'empty_potion_3'; count = 2; isBound = $false }
    @{ stackId = 460; itemId = 'empty_potion_2'; count = 2; isBound = $false }
)
$stageSnapshot.capacity = 8
$stageDispatcher.Dispatch(($stageSnapshot | ConvertTo-Json -Depth 10 -Compress))
[UnityEngine.Time]::unscaledTime = 50
$useRuntime.SharedCooldownGroup = 'healing'
$stageSession.TryUseStack(456) | Out-Null
$firstStageRequest = $stageNetwork.Sent[-1]
$stageDispatcher.Dispatch(('{"msgId":"item_use_result","itemUseOperationSessionId":"' +
    $itemUseSessionId +
    '","requestSequence":1,"success":false,"status":"effect_pending",' +
    '"inventoryRevision":123,"jingjieExp":20,"totalJingjieExp":80,' +
    '"gainedJingjieExp":5,"serverTime":100000,"cooldownAvailableAt":130000}'))
Assert-True ($script:balances.Count -eq 1 -and $script:balances[0] -eq '20/80' -and
    ($stageNetwork.Sent[-1] | ConvertFrom-Json).msgId -eq 'inventory_snapshot') `
    'effect_pending must apply authoritative balance and request an inventory snapshot.'
$stageSession.TryUseStack(457) | Out-Null
Assert-True (($stageNetwork.Sent | Where-Object { $_ -match '"msgId":"item_use"' }).Count -eq 1) `
    'An unsettled use cannot start a different stack.'
[UnityEngine.Time]::unscaledTime = 61
$stageSession.Tick()
Assert-True ($stageNetwork.Sent[-1] -eq $firstStageRequest) `
    'effect_pending must retry the exact original use after timeout.'
$stageSnapshot.inventoryRevision = 123
$stageSnapshot.stackItems = @(
    @{ stackId = 457; itemId = 'group_potion_2'; count = 2; isBound = $false }
    @{ stackId = 458; itemId = 'empty_potion_2'; count = 2; isBound = $false }
    @{ stackId = 459; itemId = 'empty_potion_3'; count = 2; isBound = $false }
    @{ stackId = 460; itemId = 'empty_potion_2'; count = 2; isBound = $false }
)
$stageDispatcher.Dispatch(($stageSnapshot | ConvertTo-Json -Depth 10 -Compress))
$stageDispatcher.Dispatch(('{"msgId":"item_use_result","itemUseOperationSessionId":"' +
    $itemUseSessionId +
    '","requestSequence":1,"success":true,"inventoryRevision":123,' +
    '"requiresInventorySnapshot":true,"jingjieExp":null,"totalJingjieExp":null}'))
Assert-True ($stageSession.CurrentSnapshot.Stacks.Count -eq 4 -and
    $script:balances.Count -eq 2 -and $script:balances[1] -eq '/') `
    'The last consumed stack stays absent, and null balances only trigger reconciliation.'
$stageSnapshot.requestSequence = ($stageNetwork.Sent[-1] | ConvertFrom-Json).requestSequence
$stageDispatcher.Dispatch(($stageSnapshot | ConvertTo-Json -Depth 10 -Compress))
$sentAfterPending = $stageNetwork.Sent.Count
$stageSession.TryUseStack(457) | Out-Null
Assert-True ($stageNetwork.Sent.Count -eq $sentAfterPending) `
    'Shared cooldown must reject another item in the same nonempty group.'
$stageSession.TryUseStack(458) | Out-Null
Assert-True (($stageNetwork.Sent[-1] | ConvertFrom-Json).stackId -eq 458) `
    'An empty-group item must not inherit the named group cooldown.'
$stageDispatcher.Dispatch(('{"msgId":"error","requestMsgId":"item_use",' +
    '"expectedRequestSequence":2,"errorCode":"item_not_consumable"}'))
Assert-True ($script:stageNotices[-1] -eq '该物品暂不可使用') `
    'A generic item_use error must settle the operation and show a useful notice.'
$stageSession.TryUseStack(458) | Out-Null
$stageDispatcher.Dispatch(('{"msgId":"item_use_result","itemUseOperationSessionId":"' +
    $itemUseSessionId +
    '","requestSequence":3,"success":true,"inventoryRevision":124,' +
    '"stackItemChanges":[{"stackId":458,"itemId":"empty_potion_2",' +
    '"previousCount":2,"newCount":1}],"serverTime":100000,' +
    '"cooldownAvailableAt":130000,"jingjieExp":44,"totalJingjieExp":null}'))
Assert-True ($script:balances.Count -eq 3 -and $script:balances[2] -eq '44/' -and
    $stageSession.CurrentSnapshot.InventoryRevision -eq 124) `
    'A provided current balance must update without inventing a null total.'
$stageSession.TryUseStack(458) | Out-Null
Assert-True (($stageNetwork.Sent[-1] | ConvertFrom-Json).requestSequence -eq 3) `
    'Different stacks of the same empty-group item share cooldown.'
$stageSession.TryUseStack(460) | Out-Null
Assert-True (($stageNetwork.Sent[-1] | ConvertFrom-Json).requestSequence -eq 3) `
    'Cooldown must also block a second stack of the same itemId.'
$stageSession.TryUseStack(459) | Out-Null
Assert-True (($stageNetwork.Sent[-1] | ConvertFrom-Json).stackId -eq 459 -and
    ($stageNetwork.Sent[-1] | ConvertFrom-Json).requestSequence -eq 4) `
    'Two distinct empty-group item IDs must have independent cooldown.'
$stageDispatcher.Dispatch(('{"msgId":"error","requestMsgId":"item_use",' +
    '"expectedRequestSequence":4,"errorCode":"item_not_consumable"}'))
$stageSession.Dispose()

$recoveryNetwork = [NetworkManager]::new()
$recoveryDispatcher = [NetMessageDispatcher]::new()
$recoverySession = [Game.Inventory.Integration.V2.InventoryV2Session]::new(
    $recoveryNetwork, $recoveryDispatcher, [Game.Inventory.InventoryPanelView]::new(),
    $items, $null, $tickProvider, $revisionProvider, $null, $null, $null,
    $null, $balanceUpdater, $readyProvider)
$recoverySession.BeginSession(789, 'world_001', $null, $null, $itemUseSessionId)
$recoveryDispatcher.Dispatch($useSnapshot)
$recoverySession.TryUseStack(456) | Out-Null
$recoverySession.Reset()
$recoverySession.BeginSession(789, 'world_001', $null, $null, $itemUseSessionId)
$recoveryDispatcher.Dispatch($useSnapshot)
$recoverySession.TryUseStack(456) | Out-Null
Assert-True ($recoveryNetwork.Sent.Count -eq 1) `
    'A new session with an uncertain old use must wait for the progression snapshot.'
$script:progressReady = $true
$recoverySession.TryUseStack(456) | Out-Null
Assert-True ($recoveryNetwork.Sent.Count -eq 2 -and
    ($recoveryNetwork.Sent[-1] | ConvertFrom-Json).requestSequence -eq 1) `
    'After fresh inventory and progression snapshots, a new session can use an item.'
$recoverySession.Dispose()

$pillNetwork = [NetworkManager]::new()
$pillDispatcher = [NetMessageDispatcher]::new()
$script:pillNotices = [System.Collections.Generic.List[string]]::new()
$pillNotice = [System.Action[string]] {
    param($message) $script:pillNotices.Add($message)
}
$pillSession = [Game.Inventory.Integration.V2.InventoryV2Session]::new(
    $pillNetwork, $pillDispatcher, [Game.Inventory.InventoryPanelView]::new(),
    $items, $null, $tickProvider, $revisionProvider,
    $null, $null, $null, $pillNotice)
$pillSession.BeginSession(789, 'world_001', $null, $null, $itemUseSessionId)
$pillSnapshot = $useSnapshot.Replace('hp_potion_01', 'pill_without_effect')
$pillDispatcher.Dispatch($pillSnapshot)
$pillSession.Open()
$pillBag = [Game.Inventory.Presentation.V2.InventoryBagGridPresenter]::LastCreated
Assert-True ($pillSession.CurrentSnapshot.Stacks.Count -eq 1 -and
    -not $pillSession.CanUseItem('pill_without_effect')) `
    'An effectless server pill remains in the bag but is not usable.'
$pillBag.TriggerDoubleClick('s:456')
Assert-True ($pillNetwork.Sent.Count -eq 0 -and
    $script:pillNotices[-1] -eq '该丹药尚无可用功效，暂不可服用') `
    'Double-clicking an effectless pill must not send item_use.'
$useRuntime.HasPillEffects = $true
$pillWithEffect = $pillSnapshot.Replace('pill_without_effect', 'pill_with_effect')
$pillDispatcher.Dispatch($pillWithEffect)
Assert-True ($pillSession.CanUseItem('pill_with_effect')) `
    'A supported server pill effect permits use even without local artwork.'
$pillBag.TriggerDoubleClick('s:456')
Assert-True ($pillNetwork.Sent.Count -eq 1 -and
    ($pillNetwork.Sent[0] | ConvertFrom-Json).msgId -eq 'item_use') `
    'An effective pill uses item_use even if its local art has an old category.'
$pillSession.Dispose()

$bookNetwork = [NetworkManager]::new()
$bookDispatcher = [NetMessageDispatcher]::new()
$bookPanel = [Game.Inventory.InventoryPanelView]::new()
$bookDetail = [Game.Inventory.Presentation.V2.InventorySelectedItemDetailPanel]::new()
$script:bookNotices = [System.Collections.Generic.List[string]]::new()
$bookNotice = [System.Action[string]]{
    param($message) $script:bookNotices.Add($message)
}
$bookSession = [Game.Inventory.Integration.V2.InventoryV2Session]::new(
    $bookNetwork, $bookDispatcher, $bookPanel, $items, $null,
    $tickProvider, $revisionProvider, $null, $null, $null, $bookNotice,
    $null, $null, $bookDetail)
$bookSession.BeginSession(789, 'world_001', $null, $null, $itemUseSessionId)
$bookSnapshot = $useSnapshot.Replace('hp_potion_01', 'gongfa_book').
    Replace('"stackId":456', '"stackId":789')
$bookDispatcher.Dispatch($bookSnapshot)
$bookSession.Open()
$bookBag = [Game.Inventory.Presentation.V2.InventoryBagGridPresenter]::LastCreated
$bookRuntime = [Game.Cultivation.Runtime.CultivationSessionRuntime]::new()
[Game.Cultivation.Runtime.CultivationSessionRuntime]::Instance = $bookRuntime
$bookBag.TriggerDoubleClick('s:999')
Assert-True ($bookRuntime.LearnCount -eq 0 -and $bookNetwork.Sent.Count -eq 0) `
    'An unknown identity must never submit a book learning request.'
$bookDetail.TriggerUse('s:789')
Assert-True ($bookRuntime.LearnCount -eq 0 -and $bookNetwork.Sent.Count -eq 0) `
    'Use button cannot learn or consume a technique book.'
$bookDetail.TriggerLearn('s:789')
Assert-True ($bookRuntime.LearnCount -eq 1 -and
    $bookRuntime.LearnedStackId -eq 789 -and $bookNetwork.Sent.Count -eq 0) `
    'The server-catalog book must learn its owned stack, even if the local category is consumable.'
$bookBag.TriggerDoubleClick('s:789')
Assert-True ($bookRuntime.LearnCount -eq 1 -and
    $script:bookNotices[-1] -eq '功法学习结果待确认') `
    'A pending learning request must not be duplicated or sent as item_use.'
$bookRuntime.Complete()
Assert-True ($script:bookNotices[-1] -eq '功法学习成功') `
    'Learning completion must notify the bag even when the technique panel is closed.'
$bookRuntime.HasBookDefinition = $false
$bookBag.TriggerDoubleClick('s:789')
Assert-True ($bookRuntime.LearnCount -eq 1 -and
    $bookRuntime.RefreshCount -eq 1 -and
    $bookNetwork.Sent.Count -eq 0 -and
    $script:bookNotices[-1] -eq '该书不在有效物品目录中，当前不可学习') `
    'A locally labelled book without an authoritative catalog must never use item_use.'
$bookRuntime.HasBookDefinition = $true
$bookRuntime.HasTechnique = $false
$bookBag.TriggerDoubleClick('s:789')
Assert-True ($bookRuntime.LearnCount -eq 1 -and
    $bookRuntime.RefreshCount -eq 2 -and
    $bookNetwork.Sent.Count -eq 0) `
    'A missing technique definition requests refresh without using the book.'
$bookRuntime.HasTechnique = $true
$bookBag.TriggerDoubleClick('s:789')
$bookRuntime.Reject('cultivation_already_learned')
Assert-True ($bookRuntime.LearnCount -eq 2 -and
    $script:bookNotices[-1] -eq '该功法已经学会') `
    'A rejected learning result must be displayed in the bag.'
$noticesBeforeReset = $script:bookNotices.Count
$bookSession.Reset()
$bookRuntime.Complete()
Assert-True ($script:bookNotices.Count -eq $noticesBeforeReset) `
    'Resetting the inventory session must remove old learning-result listeners.'
[Game.Cultivation.Runtime.CultivationSessionRuntime]::Instance = $null
$bookSession.Dispose()
Assert-True ($bookDetail.LearnListenerCount -eq 0 -and
    $bookDetail.UseListenerCount -eq 0) `
    'Learning session cleanup must detach both detail button events.'

$blueprintNetwork = [NetworkManager]::new()
$blueprintDispatcher = [NetMessageDispatcher]::new()
$blueprintDetail = [Game.Inventory.Presentation.V2.InventorySelectedItemDetailPanel]::new()
$script:blueprintNotices = [System.Collections.Generic.List[string]]::new()
$blueprintNotice = [System.Action[string]] {
    param($message) $script:blueprintNotices.Add($message)
}
$blueprintSession = [Game.Inventory.Integration.V2.InventoryV2Session]::new(
    $blueprintNetwork, $blueprintDispatcher,
    [Game.Inventory.InventoryPanelView]::new(), $items, $null,
    $tickProvider, $revisionProvider, $null, $null, $null,
    $blueprintNotice, $null, $null, $blueprintDetail)
$blueprintSession.BeginSession(789, 'world_001', $null, $null, $itemUseSessionId)
$blueprintDispatcher.Dispatch($useSnapshot.Replace('hp_potion_01', 'tz001'))
$blueprintRuntime = [Game.Forging.Runtime.锻造会话运行时]::new()
[Game.Forging.Runtime.锻造会话运行时]::Instance = $blueprintRuntime
$blueprintDetail.TriggerUse('s:456')
Assert-True ($blueprintRuntime.LearnCount -eq 0 -and
    $blueprintNetwork.Sent.Count -eq 0) `
    'Use cannot learn or consume a forging blueprint.'
$blueprintDetail.TriggerLearn('s:456')
$blueprintBag = [Game.Inventory.Presentation.V2.InventoryBagGridPresenter]::LastCreated
$blueprintBag.TriggerDoubleClick('s:456')
Assert-True ($blueprintRuntime.LearnCount -eq 1 -and
    $blueprintRuntime.LastStackId -eq 456 -and
    $blueprintNetwork.Sent.Count -eq 0 -and
    $blueprintSession.CurrentSnapshot.Stacks[0].Count -eq 3 -and
    $script:blueprintNotices[-1] -eq '图谱学习结果待确认') `
    'A blueprint must delegate only to forging and block duplicate clicks.'
[Game.Forging.Runtime.锻造会话运行时]::Instance = $null
$blueprintSession.Dispose()

$recipeNetwork = [NetworkManager]::new()
$recipeDispatcher = [NetMessageDispatcher]::new()
$recipeDetail = [Game.Inventory.Presentation.V2.InventorySelectedItemDetailPanel]::new()
$script:recipeNotices = [System.Collections.Generic.List[string]]::new()
$recipeNotice = [System.Action[string]] {
    param($message) $script:recipeNotices.Add($message)
}
$recipeSession = [Game.Inventory.Integration.V2.InventoryV2Session]::new(
    $recipeNetwork, $recipeDispatcher, [Game.Inventory.InventoryPanelView]::new(),
    $items, $null, $tickProvider, $revisionProvider,
    $null, $null, $null, $recipeNotice, $null, $null, $recipeDetail)
$recipeSession.BeginSession(789, 'world_001', $null, $null, $itemUseSessionId)
$recipeSnapshot = $useSnapshot.Replace('hp_potion_01', 'recipe_book')
$recipeDispatcher.Dispatch($recipeSnapshot)
$recipeBag = [Game.Inventory.Presentation.V2.InventoryBagGridPresenter]::LastCreated
$recipeCatalog = [Game.Cultivation.Runtime.CultivationSessionRuntime]::new()
[Game.Cultivation.Runtime.CultivationSessionRuntime]::Instance = $recipeCatalog
$alchemyLearn = [Game.Alchemy.Runtime.AlchemySessionRuntime]::new()
[Game.Alchemy.Runtime.AlchemySessionRuntime]::Instance = $alchemyLearn
$recipeDetail.TriggerUse('s:456')
Assert-True ($alchemyLearn.LearnCount -eq 0 -and
    $recipeNetwork.Sent.Count -eq 0) `
    'Use button cannot learn or consume a recipe book.'
$recipeDetail.TriggerLearn('s:456')
Assert-True ($alchemyLearn.LearnCount -eq 1 -and
    $alchemyLearn.LastStackId -eq 456 -and
    $recipeNetwork.Sent.Count -eq 0 -and
    $recipeSession.CurrentSnapshot.Stacks[0].Count -eq 3 -and
    $script:recipeNotices[-1] -eq '正在确认丹方学习结果') `
    'An owned recipe book must delegate learning to alchemy without item_use or local consumption.'
$recipeBag.TriggerDoubleClick('s:456')
Assert-True ($alchemyLearn.LearnCount -eq 1 -and
    $script:recipeNotices[-1] -eq '丹方学习结果待确认') `
    'A pending recipe learn must not send another request.'
$alchemyLearn.Complete()
Assert-True ($script:recipeNotices[-1] -eq '丹方学习成功') `
    'A successful recipe learn must notify the inventory even with the alchemy panel closed.'
$recipeCatalog.HasRecipeBookDefinition = $false
$recipeBag.TriggerDoubleClick('s:456')
Assert-True ($alchemyLearn.LearnCount -eq 1 -and
    $recipeNetwork.Sent.Count -eq 0 -and
    $recipeSession.CurrentSnapshot.Stacks[0].ItemId -eq 'recipe_book' -and
    $script:recipeNotices[-1] -eq '该书不在有效物品目录中，当前不可学习') `
    'Removing a book from the accepted catalog must not delete its owned stack or use it.'
[Game.Cultivation.Runtime.CultivationSessionRuntime]::Instance = $null
[Game.Alchemy.Runtime.AlchemySessionRuntime]::Instance = $null
$recipeSession.Dispose()

$dropNetwork = [NetworkManager]::new()
$dropDispatcher = [NetMessageDispatcher]::new()
$dropPanel = [Game.Inventory.InventoryPanelView]::new()
$dropSession = [Game.Inventory.Integration.V2.InventoryV2Session]::new(
    $dropNetwork, $dropDispatcher, $dropPanel, $items, $null,
    $null, $null, $null, $null, $null, $null)
$stackWorldDropSessionId = '22222222-2222-2222-2222-222222222222'
$dropSession.BeginSession(321, 'world_001', $null, $stackWorldDropSessionId)
$dropSnapshot = @{
    msgId = 'inventory_snapshot_result'; type = 'inventory_snapshot_result'
    equipmentProtocolVersion = 2; equipmentCatalogVersion = 2
    requestSequence = 0; success = $true; characterDbId = 321
    sceneId = 'world_001'; capacity = 4; inventoryRevision = 40; equipmentRevision = 2
    stackItems = @(
        @{ stackId = 777; itemId = 'hp_potion_01'; count = 3; isBound = $false }
        @{ stackId = 778; itemId = 'skill_book'; count = 1; isBound = $false }
    )
    equipmentItems = @(@{ itemInstanceId = 'ring-discard'; equipmentId = 'ring_xingyun_001'; slotCode = 0; isBound = $false })
    wallets = @(); attributeBonus = @{ maxHp = 0; attack = 0; defense = 0 }
    serverTime = 1790500000000
} | ConvertTo-Json -Depth 10 -Compress
$dropDispatcher.Dispatch($dropSnapshot)
$dropSession.Open()
$dropBag = [Game.Inventory.Presentation.V2.InventoryBagGridPresenter]::LastCreated
$dropDialog = [Game.Inventory.Presentation.V2.InventoryDiscardConfirmationView]::LastCreated
$dropBag.TriggerWorldDrop('s:999')
Assert-True (-not $dropDialog.Visible -and $dropNetwork.Sent.Count -eq 0) `
    'Unknown bag identity must not request world drop.'
$dropBag.TriggerWorldDrop('s:777')
Assert-True ($dropDialog.Visible -and $dropDialog.Count -eq 3 -and
    $dropNetwork.Sent.Count -eq 0) 'Stack world drop must offer a quantity before sending.'
$dropDialog.Hide()
Assert-True ($dropNetwork.Sent.Count -eq 0) 'Canceling a world drop must not send or remove a stack.'
$dropBag.TriggerWorldDrop('s:777')
[UnityEngine.Time]::unscaledTime = 30
$dropDialog.TriggerConfirm(1)
$stackRequest = $dropNetwork.Sent[-1] | ConvertFrom-Json
Assert-True ($dropNetwork.Sent.Count -eq 1 -and
    $stackRequest.msgId -eq 'inventory_world_drop' -and
    $stackRequest.type -eq 'inventory_world_drop' -and
    $stackRequest.worldDropOperationSessionId -eq $stackWorldDropSessionId -and
    $stackRequest.requestSequence -eq 1 -and
    $stackRequest.expectedInventoryRevision -eq 40 -and
    $stackRequest.stackId -eq 777 -and $stackRequest.count -eq 1 -and
    $stackRequest.confirmationAccepted -and
    $null -eq $stackRequest.PSObject.Properties['itemId'] -and
    $null -eq $stackRequest.PSObject.Properties['itemInstanceId'] -and
    $null -eq $stackRequest.PSObject.Properties['sceneSessionId'] -and
    $dropDialog.Pending -and $dropSession.CurrentSnapshot.Stacks[0].Count -eq 3) `
    'Partial drop must send the exact stack protocol without optimistic removal.'
$dropDispatcher.Dispatch((@{
    msgId = 'world_drop_result'; type = 'world_drop_result'
    worldDropOperationSessionId = $stackWorldDropSessionId
    requestSequence = 1; assetKind = 'equipment'; success = $true
    status = 'active'; inventoryRevision = 41
} | ConvertTo-Json -Compress))
Assert-True ($dropDialog.Pending -and $dropSession.CurrentSnapshot.Stacks[0].Count -eq 3) `
    'A result with the wrong asset kind must not settle a stack request.'
$dropDispatcher.Dispatch((@{
    msgId = 'world_drop_result'; type = 'world_drop_result'
    worldDropOperationSessionId = '33333333-3333-3333-3333-333333333333'
    requestSequence = 1; assetKind = 'stack'; success = $true
    status = 'active'; inventoryRevision = 41
} | ConvertTo-Json -Compress))
Assert-True ($dropDialog.Pending) 'A result from another world-drop session must be ignored.'
[UnityEngine.Time]::unscaledTime = 40
$dropSession.Tick()
Assert-True ($dropNetwork.Sent.Count -eq 2 -and
    $dropNetwork.Sent[0] -eq $dropNetwork.Sent[1]) `
    'World drop retry must preserve the original request JSON.'
$partialResult = @{
    msgId = 'world_drop_result'; type = 'world_drop_result'
    worldDropOperationSessionId = $stackWorldDropSessionId
    requestSequence = 1; assetKind = 'stack'; success = $true
    status = 'pending_scene_sync'; characterDbId = 321; inventoryRevision = 41
    stackItemChanges = @(@{ stackId = 777; itemId = 'hp_potion_01'
        previousCount = 3; newCount = 2; isBound = $false })
    removedItemInstanceIds = @(); requiresSnapshot = $false
} | ConvertTo-Json -Depth 10 -Compress
$dropDispatcher.Dispatch($partialResult)
Assert-True (-not $dropDialog.Visible -and
    $dropSession.CurrentSnapshot.Stacks[0].Count -eq 2 -and
    $dropSession.CurrentSnapshot.InventoryRevision -eq 41) `
    'Pending scene sync must apply the server stack change only once.'
$dropDispatcher.Dispatch($partialResult)
Assert-True ($dropSession.CurrentSnapshot.Stacks[0].Count -eq 2) `
    'Repeated world-drop results must not subtract twice.'

$dropBag.TriggerWorldDrop('e:ring-discard')
$equipmentRequest = $dropNetwork.Sent[-1] | ConvertFrom-Json
Assert-True ($equipmentRequest.msgId -eq 'equipment_world_drop' -and
    $equipmentRequest.requestSequence -eq 2 -and
    $equipmentRequest.worldDropOperationSessionId -eq $stackWorldDropSessionId -and
    $equipmentRequest.itemInstanceId -eq 'ring-discard' -and
    $null -eq $equipmentRequest.PSObject.Properties['stackId']) `
    'Equipment must share the world-drop sequence but keep its own request type.'
$dropDispatcher.Dispatch((@{
    msgId = 'world_drop_result'; type = 'world_drop_result'
    worldDropOperationSessionId = $stackWorldDropSessionId
    requestSequence = 2; assetKind = 'equipment'; success = $true
    status = 'active'; characterDbId = 321; inventoryRevision = 42
    stackItemChanges = @(); removedItemInstanceIds = @('ring-discard')
    requiresSnapshot = $false
} | ConvertTo-Json -Depth 10 -Compress))
Assert-True ($dropSession.CurrentSnapshot.Equipment.Count -eq 0 -and
    $dropSession.CurrentSnapshot.EquipmentRevision -eq 2 -and
    $dropSession.CurrentSnapshot.InventoryRevision -eq 42) `
    'Equipment result without equipmentRevision must still apply the verified removal.'

$dropBag.TriggerWorldDrop('s:778')
Assert-True ($dropDialog.Visible -and $dropDialog.Count -eq 1) `
    'Skill book world drop must use the same quantity confirmation.'
$dropDialog.TriggerConfirm(1)
$bookRequest = $dropNetwork.Sent[-1] | ConvertFrom-Json
Assert-True ($bookRequest.msgId -eq 'inventory_world_drop' -and
    $bookRequest.requestSequence -eq 3 -and $bookRequest.stackId -eq 778 -and
    $bookRequest.expectedInventoryRevision -eq 42) `
    'A skill book after equipment must continue the same world-drop sequence.'
$dropDispatcher.Dispatch((@{
    msgId = 'world_drop_result'; type = 'world_drop_result'
    worldDropOperationSessionId = $stackWorldDropSessionId
    requestSequence = 3; assetKind = 'stack'; success = $true
    status = 'expired'; characterDbId = 321; inventoryRevision = 43
    stackItemChanges = @(@{ stackId = 778; itemId = 'skill_book'
        previousCount = 1; newCount = 0; isBound = $false })
    removedItemInstanceIds = @(); requiresSnapshot = $false
} | ConvertTo-Json -Depth 10 -Compress))
Assert-True (-not $dropDialog.Visible -and
    $dropSession.CurrentSnapshot.Stacks.Count -eq 1 -and
    $dropSession.CurrentSnapshot.Stacks[0].Count -eq 2 -and
    $dropSession.CurrentSnapshot.InventoryRevision -eq 43) `
    'Expired ground loot still consumes only the confirmed skill book stack.'

$dropBag.TriggerWorldDrop('s:777')
$dropDialog.TriggerConfirm(1)
$rejectedRequest = $dropNetwork.Sent[-1] | ConvertFrom-Json
$dropDispatcher.Dispatch((@{
    msgId = 'world_drop_result'; type = 'world_drop_result'
    worldDropOperationSessionId = $stackWorldDropSessionId
    requestSequence = $rejectedRequest.requestSequence
    assetKind = 'stack'; success = $false; status = 'rejected'
    inventoryRevision = 43; requiresSnapshot = $false
    errorCode = 'world_drop_bound_item_forbidden'
    stackItemChanges = @(); removedItemInstanceIds = @()
} | ConvertTo-Json -Depth 10 -Compress))
Assert-True ($dropDialog.Visible -and $dropDialog.Failed -and
    -not $dropDialog.Pending -and $dropSession.CurrentSnapshot.Stacks[0].Count -eq 2) `
    'A rejected drop without snapshot repair must retain ownership and allow confirmation again.'
$dropDialog.TriggerConfirm(2)
$failedRequest = $dropNetwork.Sent[-1] | ConvertFrom-Json
Assert-True ($failedRequest.requestSequence -eq $rejectedRequest.requestSequence + 1 -and
    $failedRequest.count -eq 2) `
    'A changed request after explicit rejection must use a fresh world-drop sequence.'
$dropDispatcher.Dispatch((@{
    msgId = 'world_drop_result'; type = 'world_drop_result'
    worldDropOperationSessionId = $stackWorldDropSessionId
    requestSequence = $failedRequest.requestSequence
    assetKind = 'stack'; success = $false; status = 'rejected'
    inventoryRevision = 44; requiresSnapshot = $true
    errorCode = 'world_drop_inventory_revision_conflict'
    stackItemChanges = @(); removedItemInstanceIds = @()
} | ConvertTo-Json -Depth 10 -Compress))
Assert-True ($dropSession.CurrentSnapshot.Stacks[0].Count -eq 2 -and
    -not $dropDialog.Visible -and
    ($dropNetwork.Sent[-1] | ConvertFrom-Json).msgId -eq 'inventory_snapshot') `
    'A revision conflict must keep ownership and request a repair snapshot.'
$repairRequest = $dropNetwork.Sent[-1] | ConvertFrom-Json
$repairSnapshot = $dropSnapshot | ConvertFrom-Json
$repairSnapshot.requestSequence = $repairRequest.requestSequence
$repairSnapshot.inventoryRevision = 44
$repairSnapshot.stackItems = @(
    @{ stackId = 777; itemId = 'hp_potion_01'; count = 2; isBound = $false }
)
$repairSnapshot.equipmentItems = @()
$dropDispatcher.Dispatch(($repairSnapshot | ConvertTo-Json -Depth 10 -Compress))
[UnityEngine.Time]::unscaledTime = 42
$dropBag.TriggerWorldDrop('s:777')
$dropDialog.TriggerConfirm(1)
$invalidDeltaRequest = $dropNetwork.Sent[-1] | ConvertFrom-Json
$dropDispatcher.Dispatch((@{
    msgId = 'world_drop_result'; type = 'world_drop_result'
    worldDropOperationSessionId = $stackWorldDropSessionId
    requestSequence = $invalidDeltaRequest.requestSequence
    assetKind = 'stack'; success = $true; status = 'active'
    inventoryRevision = 45; requiresSnapshot = $false
    stackItemChanges = @(@{ stackId = 777; itemId = 'hp_potion_01'
        previousCount = 3; newCount = 2; isBound = $false })
    removedItemInstanceIds = @()
} | ConvertTo-Json -Depth 10 -Compress))
Assert-True ($dropSession.CurrentSnapshot.Stacks[0].Count -eq 2 -and
    $dropSession.CurrentSnapshot.InventoryRevision -eq 44 -and
    ($dropNetwork.Sent[-1] | ConvertFrom-Json).msgId -eq 'inventory_snapshot') `
    'A mismatched stack delta must never remove ownership locally.'
$invalidRepairRequest = $dropNetwork.Sent[-1] | ConvertFrom-Json
$repairSnapshot.requestSequence = $invalidRepairRequest.requestSequence
$repairSnapshot.inventoryRevision = 45
$dropDispatcher.Dispatch(($repairSnapshot | ConvertTo-Json -Depth 10 -Compress))
[UnityEngine.Time]::unscaledTime = 44
$dropBag.TriggerWorldDrop('s:777')
$dropDialog.TriggerConfirm(2)
$fullStackRequest = $dropNetwork.Sent[-1] | ConvertFrom-Json
Assert-True ($fullStackRequest.count -eq 2 -and
    $fullStackRequest.expectedInventoryRevision -eq 45) `
    'A full stack world drop must use the repaired inventory revision.'
$dropDispatcher.Dispatch((@{
    msgId = 'world_drop_result'; type = 'world_drop_result'
    worldDropOperationSessionId = $stackWorldDropSessionId
    requestSequence = $fullStackRequest.requestSequence
    assetKind = 'stack'; success = $true; status = 'active'
    inventoryRevision = 46; requiresSnapshot = $false
    stackItemChanges = @(@{ stackId = 777; itemId = 'hp_potion_01'
        previousCount = 2; newCount = 0; isBound = $false })
    removedItemInstanceIds = @()
} | ConvertTo-Json -Depth 10 -Compress))
Assert-True ($dropSession.CurrentSnapshot.Stacks.Count -eq 0 -and
    $dropSession.CurrentSnapshot.InventoryRevision -eq 46 -and
    $dropBag.BagAt(0) -ne 's:777') `
    'A successful active world drop must remove the complete stack and its bag placement.'
$dropSession.Dispose()

$equipmentDiscardNetwork = [NetworkManager]::new()
$equipmentDiscardDispatcher = [NetMessageDispatcher]::new()
$equipmentDiscardPanel = [Game.Inventory.InventoryPanelView]::new()
$equipmentDiscardSession = [Game.Inventory.Integration.V2.InventoryV2Session]::new(
    $equipmentDiscardNetwork, $equipmentDiscardDispatcher, $equipmentDiscardPanel, $items, $null,
    $null, $null, $null, $null, $null, $null)
$worldDropSessionId = 'cc4f6f51-66c5-4fa2-b841-7ca67840b42a'
$nextWorldDropSessionId = '3e7a8084-c21b-4ed0-9308-92900504f71a'
$equipmentDiscardSession.BeginSession(654, 'world_001', $equipmentSessionId, $worldDropSessionId)
$equipmentDiscardSnapshot = @{
    msgId = 'inventory_snapshot_result'; type = 'inventory_snapshot_result'
    equipmentProtocolVersion = 2; equipmentCatalogVersion = 2
    requestSequence = 0; success = $true; characterDbId = 654
    sceneId = 'world_001'; capacity = 5; inventoryRevision = 20; equipmentRevision = 7
    stackItems = @(); equipmentItems = @(
        @{ itemInstanceId = 'ring-a'; equipmentId = 'ring_xingyun_001'; slotCode = 0; isBound = $false }
        @{ itemInstanceId = 'ring-b'; equipmentId = 'ring_xingyun_001'; slotCode = 0; isBound = $false }
        @{ itemInstanceId = 'ring-worn'; equipmentId = 'ring_xingyun_001'; slotCode = 5; isBound = $false }
    )
    wallets = @(); attributeBonus = @{ maxHp = 0; attack = 0; defense = 0 }
    serverTime = 1790500000000
} | ConvertTo-Json -Depth 10 -Compress
$equipmentDiscardDispatcher.Dispatch($equipmentDiscardSnapshot)
$equipmentDiscardSession.Open()
$equipmentDiscardBag = [Game.Inventory.Presentation.V2.InventoryBagGridPresenter]::LastCreated
$equipmentDiscardView = [Game.Inventory.Presentation.V2.InventoryEquipmentPanelView]::LastCreated
$equipmentDiscardDialog = [Game.Inventory.Presentation.V2.InventoryDiscardConfirmationView]::LastCreated
$null = $equipmentDiscardBag.CurrentLayout.TryMove(0, 3)
$equipmentDiscardBag.TriggerWorldDrop('e:ring-worn')
Assert-True (-not $equipmentDiscardDialog.Visible -and
    $equipmentDiscardNetwork.Sent.Count -eq 0) 'Worn equipment must not be world-dropped from the panel.'
[UnityEngine.Time]::unscaledTime = 70
$equipmentDiscardBag.TriggerWorldDrop('e:ring-a')
$equipmentDiscardRequest = $equipmentDiscardNetwork.Sent[-1] | ConvertFrom-Json
Assert-True ($equipmentDiscardNetwork.Sent.Count -eq 1 -and
    $equipmentDiscardRequest.msgId -eq 'equipment_world_drop' -and
    $equipmentDiscardRequest.type -eq 'equipment_world_drop' -and
    $equipmentDiscardRequest.worldDropOperationSessionId -eq $worldDropSessionId -and
    $equipmentDiscardRequest.requestSequence -eq 1 -and
    $equipmentDiscardRequest.itemInstanceId -eq 'ring-a' -and
    $equipmentDiscardRequest.confirmationAccepted -eq $true -and
    $null -eq $equipmentDiscardRequest.PSObject.Properties['stackId'] -and
    $null -eq $equipmentDiscardRequest.PSObject.Properties['equipmentOperationSessionId'] -and
    $equipmentDiscardBag.BagAt(3) -eq 'e:ring-a' -and
    $equipmentDiscardView.LastRendered.Equipment.Count -eq 3 -and
    -not $equipmentDiscardDialog.Visible -and
    $equipmentDiscardView.Pending) 'Dragging bag equipment out must send world drop without a dialog or optimistic removal.'
$equipmentDiscardBag.TriggerWorldDrop('e:ring-b')
$equipmentDiscardView.TriggerEquip('e:ring-b', 6)
$equipmentDiscardSession.Close()
Assert-True ($equipmentDiscardNetwork.Sent.Count -eq 1 -and
    $equipmentDiscardSession.IsOpen) 'Pending world drop must block other operations and panel closing.'
[UnityEngine.Time]::unscaledTime = 80
$equipmentDiscardSession.Tick()
Assert-True ($equipmentDiscardNetwork.Sent.Count -eq 2 -and
    $equipmentDiscardNetwork.Sent[0] -eq $equipmentDiscardNetwork.Sent[1]) 'World drop retry must reuse the exact JSON.'
function Send-WorldDropResult(
    [long]$sequence, [bool]$success, [long]$inventoryRevision,
    [long]$equipmentRevision, [string[]]$removed, [string]$status = 'active'
) {
    $result = @{
        msgId = 'world_drop_result'; type = 'world_drop_result'
        worldDropOperationSessionId = $worldDropSessionId
        requestSequence = $sequence; assetKind = 'equipment'
        status = $status; success = $success
        characterDbId = 654
        inventoryRevision = $inventoryRevision; equipmentRevision = $equipmentRevision
        equipmentItemChanges = @(); removedItemInstanceIds = $removed
        requiresSnapshot = $false
        errorCode = $(if ($success) { '' } else { 'world_drop_rejected' })
        message = ''
    }
    $result = $result | ConvertTo-Json -Depth 10 -Compress
    $equipmentDiscardDispatcher.Dispatch($result)
}
Send-WorldDropResult ($equipmentDiscardRequest.requestSequence + 1) $true 21 7 @('ring-a')
Assert-True ($equipmentDiscardView.Pending -and
    $equipmentDiscardBag.BagAt(3) -eq 'e:ring-a') 'Wrong sequence must not consume an instance.'
$wrongSessionResult = @{
    msgId = 'world_drop_result'; type = 'world_drop_result'
    worldDropOperationSessionId = $nextWorldDropSessionId
    requestSequence = $equipmentDiscardRequest.requestSequence
    assetKind = 'equipment'; status = 'active'; success = $true; characterDbId = 654
    inventoryRevision = 21; equipmentRevision = 7
    equipmentItemChanges = @(); removedItemInstanceIds = @('ring-a')
    requiresSnapshot = $false; errorCode = ''; message = ''
} | ConvertTo-Json -Depth 10 -Compress
$equipmentDiscardDispatcher.Dispatch($wrongSessionResult)
Assert-True ($equipmentDiscardView.Pending -and
    $equipmentDiscardBag.BagAt(3) -eq 'e:ring-a') `
    'A result from another world-drop session must not remove the local instance.'
$oldOperation = @{
    msgId = 'equipment_operation_result'; type = 'equipment_operation_result'
    equipmentOperationSessionId = $equipmentSessionId
    requestSequence = $equipmentDiscardRequest.requestSequence
    operation = 'discard'; success = $true; characterDbId = 654
    inventoryRevision = 21; equipmentRevision = 7
    equipmentItemChanges = @(); removedItemInstanceIds = @('ring-a')
} | ConvertTo-Json -Depth 10 -Compress
$equipmentDiscardDispatcher.Dispatch($oldOperation)
Assert-True ($equipmentDiscardView.Pending -and
    $equipmentDiscardBag.BagAt(3) -eq 'e:ring-a') `
    'Legacy equipment operation result cannot complete a world drop.'
Send-WorldDropResult $equipmentDiscardRequest.requestSequence $false 20 7 @()
Assert-True (-not $equipmentDiscardView.Pending -and
    $equipmentDiscardBag.BagAt(3) -eq 'e:ring-a') 'Failed world drop must leave ownership unchanged.'
[UnityEngine.Time]::unscaledTime = 81
$equipmentDiscardBag.TriggerWorldDrop('e:ring-a')
$equipmentDiscardRequest2 = $equipmentDiscardNetwork.Sent[-1] | ConvertFrom-Json
Assert-True ($equipmentDiscardRequest2.requestSequence -eq 2 -and
    $equipmentDiscardRequest2.worldDropOperationSessionId -eq $worldDropSessionId -and
    $equipmentDiscardView.Pending) 'A new world drop must use its own sequence.'
Send-WorldDropResult $equipmentDiscardRequest2.requestSequence $true 21 7 @('ring-a') 'pending_scene_sync'
Assert-True ($equipmentDiscardView.LastRendered.Equipment.Count -eq 2 -and
    $equipmentDiscardView.LastRendered.InventoryRevision -eq 21 -and
    $equipmentDiscardView.LastRendered.EquipmentRevision -eq 7 -and
    $equipmentDiscardBag.BagAt(3) -ne 'e:ring-a' -and
    $equipmentDiscardBag.BagAt(1) -eq 'e:ring-b' -and
    -not $equipmentDiscardDialog.Visible -and
    -not $equipmentDiscardView.Pending) 'Pending scene sync must apply verified bag removal without opening a dialog.'
Send-WorldDropResult $equipmentDiscardRequest2.requestSequence $true 21 7 @('ring-a')
Assert-True ($equipmentDiscardView.LastRendered.Equipment.Count -eq 2 -and
    $equipmentDiscardView.LastRendered.InventoryRevision -eq 21) 'Repeated equipment result must not apply again.'
$equipmentDiscardBag.TriggerWorldDrop('e:ring-b')
$gapEquipmentRequest = $equipmentDiscardNetwork.Sent[-1] | ConvertFrom-Json
[UnityEngine.Time]::unscaledTime = 82
Send-WorldDropResult $gapEquipmentRequest.requestSequence $true 23 7 @('ring-b')
$equipmentRepair = $equipmentDiscardNetwork.Sent[-1] | ConvertFrom-Json
Assert-True ($equipmentDiscardView.LastRendered.Equipment.Count -eq 2 -and
    $equipmentRepair.msgId -eq 'inventory_snapshot') 'World drop revision gap must defer removal until snapshot repair.'
$equipmentRepairSnapshot = $equipmentDiscardSnapshot | ConvertFrom-Json
$equipmentRepairSnapshot.requestSequence = $equipmentRepair.requestSequence
$equipmentRepairSnapshot.inventoryRevision = 23
$equipmentRepairSnapshot.equipmentItems = @(
    @{ itemInstanceId = 'ring-b'; equipmentId = 'ring_xingyun_001'; slotCode = 0; isBound = $false }
    @{ itemInstanceId = 'ring-worn'; equipmentId = 'ring_xingyun_001'; slotCode = 5; isBound = $false }
)
$equipmentDiscardDispatcher.Dispatch(($equipmentRepairSnapshot | ConvertTo-Json -Depth 10 -Compress))
$equipmentDiscardBag.TriggerWorldDrop('e:ring-b')
$wrongRemovalRequest = $equipmentDiscardNetwork.Sent[-1] | ConvertFrom-Json
[UnityEngine.Time]::unscaledTime = 83
Send-WorldDropResult $wrongRemovalRequest.requestSequence $true 24 7 @('ring-worn')
Assert-True ($equipmentDiscardView.LastRendered.InventoryRevision -eq 23 -and
    $equipmentDiscardView.LastRendered.Equipment.Count -eq 2 -and
    ($equipmentDiscardNetwork.Sent[-1] | ConvertFrom-Json).msgId -eq 'inventory_snapshot') 'Wrong removal ID must not delete another instance, especially worn equipment.'
$equipmentRepairSnapshot.requestSequence = ($equipmentDiscardNetwork.Sent[-1] | ConvertFrom-Json).requestSequence
$equipmentRepairSnapshot.inventoryRevision = 24
$equipmentDiscardDispatcher.Dispatch(($equipmentRepairSnapshot | ConvertTo-Json -Depth 10 -Compress))
$equipmentDiscardBag.TriggerWorldDrop('e:ring-b')
$timeoutEquipmentRequest = $equipmentDiscardNetwork.Sent[-1]
[UnityEngine.Time]::unscaledTime = 93
$equipmentDiscardSession.Tick()
Assert-True ($equipmentDiscardNetwork.Sent[-1] -eq $timeoutEquipmentRequest) 'World drop timeout must retry the identical request.'
[UnityEngine.Time]::unscaledTime = 103
$equipmentDiscardSession.Tick()
Assert-True ($equipmentDiscardView.LastRendered.Equipment.Count -eq 2 -and
    -not $equipmentDiscardDialog.Visible -and
    ($equipmentDiscardNetwork.Sent[-1] | ConvertFrom-Json).msgId -eq 'inventory_snapshot') 'Final world drop timeout must preserve ownership and request repair.'
$equipmentDiscardSession.Dispose()

$v3Network = [NetworkManager]::new()
$v3Dispatcher = [NetMessageDispatcher]::new()
$v3Panel = [Game.Inventory.InventoryPanelView]::new()
$v3Session = [Game.Inventory.Integration.V2.InventoryV2Session]::new(
    $v3Network, $v3Dispatcher, $v3Panel, $items, $null,
    $null, $null, $null, $null, $null, $null)
$v3Session.BeginSession(901, 'world_001', $equipmentSessionId, $worldDropSessionId)
$v3Message = $push | ConvertFrom-Json
$v3Message.equipmentProtocolVersion = 3
$v3Message.characterDbId = 901
$v3Message.PSObject.Properties.Remove('reason')
$v3Message.stackItems = @(@{ stackId = 101; itemId = 'unknown_stack'
    count = 5; isBound = $false })
$v3Equipment = [System.Collections.Generic.List[object]]::new()
for ($i = 1; $i -le 45; $i++) {
    $v3Equipment.Add(@{ itemInstanceId = "bag-$i"
        equipmentId = $(if ($i -eq 45) { 'unknown_equipment' } else { 'ring_xingyun_001' })
        slotCode = 0; isBound = $false })
}
for ($slot = 1; $slot -le 8; $slot++) {
    $v3Equipment.Add(@{ itemInstanceId = "equipped-$slot"
        equipmentId = 'ring_xingyun_001'; slotCode = $slot; isBound = $false })
}
$v3Message.equipmentItems = $v3Equipment.ToArray()
$v3Push = $v3Message | ConvertTo-Json -Depth 10 -Compress
$items.EquipmentCatalogVersion = 3
$v3Dispatcher.Dispatch($v3Push)
Assert-True (@([UnityEngine.Debug]::Messages | Where-Object {
    $_ -eq ('[InventorySnapshot][RAW] ' + $v3Push) }).Count -eq 1) `
    'Development snapshot log must preserve the complete received JSON, including equipment identities.'
Assert-True ($presenter::LastRendered.CharacterDbId -eq 901 -and
    $presenter::LastRendered.Capacity -eq 60 -and
    $presenter::LastRendered.Stacks.Count -eq 1 -and
    $presenter::LastRendered.Equipment.Count -eq 53) `
    'V3 enter-world push must retain all instances before opening the bag.'
$v3Layout = [Game.Inventory.Presentation.V2.InventoryBagGridPresenter]::LastCreated.CurrentLayout
Assert-True ($v3Layout.Slots.Count -eq 60 -and
    @($v3Layout.Slots | Where-Object { $null -ne $_ }).Count -eq 46 -and
    $v3Layout.Slots -contains 's:101' -and
    $v3Layout.Slots -contains 'e:bag-45') `
    'Missing static definitions must not remove the unknown stack or equipment from the bag.'
$v3Logs = @([UnityEngine.Debug]::Messages | Where-Object { $_.StartsWith('[InventoryV3][RX]') })
Assert-True ($v3Logs.Count -eq 1 -and
    $v3Logs[0].Contains('protocol=3 success=true capacity=60') -and
    $v3Logs[0].Contains('stackItems=1 equipmentItems=53 bagEquipment=45 equippedEquipment=8') -and
    $v3Logs[0].Contains('unknownStackItemIds=[unknown_stack]') -and
    $v3Logs[0].Contains('unknownEquipmentIds=[unknown_equipment]') -and
    $v3Logs[0].Contains('catalogMatchesClient=False') -and
    -not $v3Logs[0].Contains('bag-45') -and
    $v3Logs[0].Contains('applied=true')) `
    "Applied V3 log must summarize ownership and missing static IDs: $($v3Logs -join '; ')"
$items.EquipmentCatalogVersion = 2
$v3EquipmentView = [Game.Inventory.Presentation.V2.InventoryEquipmentPanelView]::LastCreated
$v3BagView = [Game.Inventory.Presentation.V2.InventoryBagGridPresenter]::LastCreated
$v3BagView.TriggerUnequip(5, 59, 'e:equipped-5')
$v3Unequip = $v3Network.Sent[-1] | ConvertFrom-Json
Assert-True ($v3Network.Sent.Count -eq 1 -and
    $v3Unequip.msgId -eq 'equipment_unequip' -and
    $v3Unequip.equipmentOperationSessionId -eq $equipmentSessionId -and
    $v3Unequip.requestSequence -eq 1 -and
    $v3Unequip.slotCode -eq 5 -and
    $v3EquipmentView.Pending -and
    $v3EquipmentView.LastRendered.Equipment[49].SlotCode -eq 5 -and
    $null -eq $v3BagView.BagAt(59)) `
    'V3 ownership must send a slot-code unequip without changing local state first.'
$v3UnequipResult = @{
    msgId = 'equipment_operation_result'; type = 'equipment_operation_result'
    equipmentOperationSessionId = $equipmentSessionId
    requestSequence = $v3Unequip.requestSequence; operation = 'unequip'
    success = $true; characterDbId = 901
    inventoryRevision = 5; equipmentRevision = 1
    equipmentItemChanges = @(@{
        itemInstanceId = 'equipped-5'; equipmentId = 'ring_xingyun_001'
        isBound = $false; slotCode = 0
    })
    attributeBonus = @{ maxHp = 0; attack = 0; defense = 0 }
    requiresSnapshot = $false; errorCode = ''; message = ''
    serverTime = 1790500000000
} | ConvertTo-Json -Depth 10 -Compress
$v3Dispatcher.Dispatch($v3UnequipResult)
Assert-True (-not $v3EquipmentView.Pending -and
    $v3EquipmentView.LastRendered.InventoryRevision -eq 5 -and
    $v3EquipmentView.LastRendered.EquipmentRevision -eq 1 -and
    $v3EquipmentView.LastRendered.Equipment[49].SlotCode -eq 0 -and
    $v3BagView.BagAt(59) -eq 'e:equipped-5') `
    'V3 unequip must commit only after the matching authoritative result.'
$staleV3 = $v3Push | ConvertFrom-Json
$staleV3.inventoryRevision = 3
$staleV3Json = $staleV3 | ConvertTo-Json -Depth 10 -Compress
$v3Dispatcher.Dispatch($staleV3Json)
Assert-True (@([UnityEngine.Debug]::Messages | Where-Object {
    $_ -eq ('[InventorySnapshot][RAW] ' + $staleV3Json) }).Count -eq 1) `
    'Development snapshot log must include rejected and stale payloads for diagnosis.'
Assert-True (@([UnityEngine.Debug]::Messages | Where-Object {
    $_.StartsWith('[InventoryV3][RX]') }).Count -eq 1) `
    'Rejected V3 snapshots must not claim to have been applied.'
$v3SentBeforeOpen = $v3Network.Sent.Count
$v3Session.Open()
Assert-True ($v3Network.Sent.Count -eq $v3SentBeforeOpen -and
    @([UnityEngine.Debug]::Messages | Where-Object { $_.StartsWith('[InventoryV3][RX]') }).Count -eq 1) `
    'Opening a cached V3 push must not send another request or log a second application.'
$v3BagView.TriggerWorldDrop('e:equipped-5')
$v3DiscardDialog = [Game.Inventory.Presentation.V2.InventoryDiscardConfirmationView]::LastCreated
$v3Discard = $v3Network.Sent[-1] | ConvertFrom-Json
Assert-True ($v3Discard.msgId -eq 'equipment_world_drop' -and
    $v3Discard.worldDropOperationSessionId -eq $worldDropSessionId -and
    $v3Discard.requestSequence -eq 1 -and
    $v3Discard.itemInstanceId -eq 'equipped-5' -and
    $v3Discard.confirmationAccepted -and
    -not $v3DiscardDialog.Visible -and
    $v3BagView.BagAt(59) -eq 'e:equipped-5') `
    'World drop after V3 unequip must use an independent sequence and skip confirmation.'
$v3DiscardResult = @{
    msgId = 'world_drop_result'; type = 'world_drop_result'
    worldDropOperationSessionId = $worldDropSessionId
    requestSequence = $v3Discard.requestSequence; assetKind = 'equipment'; status = 'active'
    success = $true; characterDbId = 901
    inventoryRevision = 6; equipmentRevision = 1
    equipmentItemChanges = @(); removedItemInstanceIds = @('equipped-5')
    requiresSnapshot = $false; errorCode = ''; message = ''
} | ConvertTo-Json -Depth 10 -Compress
$v3Dispatcher.Dispatch($v3DiscardResult)
Assert-True ($v3EquipmentView.LastRendered.Equipment.Count -eq 52 -and
    $v3BagView.BagAt(59) -ne 'e:equipped-5' -and
    -not $v3DiscardDialog.Visible) `
    'Only the matching successful world drop result may remove the equipment instance.'
$v3BagView.TriggerWorldDrop('e:bag-1')
$missingDeltaRequest = $v3Network.Sent[-1] | ConvertFrom-Json
$missingDeltaResult = @{
    msgId = 'world_drop_result'; type = 'world_drop_result'
    worldDropOperationSessionId = $worldDropSessionId
    requestSequence = $missingDeltaRequest.requestSequence
    assetKind = 'equipment'; status = 'active'; success = $true; characterDbId = 901
    inventoryRevision = 7; equipmentRevision = 1
    requiresSnapshot = $false
} | ConvertTo-Json -Depth 10 -Compress
$v3Dispatcher.Dispatch($missingDeltaResult)
Assert-True ($v3EquipmentView.LastRendered.Equipment.Count -eq 52 -and
    ($v3Network.Sent[-1] | ConvertFrom-Json).msgId -eq 'inventory_snapshot') `
    'A world-drop result without verifiable removal delta must request a snapshot.'
$v3Session.BeginSession(902, 'world_001', $nextEquipmentSessionId, $nextWorldDropSessionId)
Assert-True ($null -eq $presenter::LastRendered -and
    -not $v3Panel.Visible -and
    $null -eq ([Game.Inventory.Presentation.V2.InventoryEquipmentPanelView]::LastCreated).LastRendered) `
    'A new world session must clear the old bag and equipped view before replay.'
[UnityEngine.Time]::unscaledTime = 110
$v3Session.Open()
$v3Request = $v3Network.Sent[-1] | ConvertFrom-Json
Assert-True ($v3Request.msgId -eq 'inventory_snapshot' -and
    $v3Request.requestSequence -gt 0) 'An uncached V3 session must request a numbered snapshot.'
$v3Message.characterDbId = 902
$v3Message.requestSequence = $v3Request.requestSequence
$v3Dispatcher.Dispatch(($v3Message | ConvertTo-Json -Depth 10 -Compress))
Assert-True ($presenter::LastRendered.CharacterDbId -eq 902 -and
    $presenter::LastRendered.Capacity -eq 60 -and
    -not $v3Panel.Loading) 'Numbered V3 response must populate the new session.'
$v3BagView.TriggerWorldDrop('e:bag-1')
$newWorldDrop = $v3Network.Sent[-1] | ConvertFrom-Json
Assert-True ($newWorldDrop.msgId -eq 'equipment_world_drop' -and
    $newWorldDrop.requestSequence -eq 1 -and
    $newWorldDrop.worldDropOperationSessionId -eq $nextWorldDropSessionId) `
    'A new world entry must reset the world-drop sequence and replace its session ID.'
$v3Dispatcher.Dispatch((@{
    msgId = 'world_drop_result'; type = 'world_drop_result'
    worldDropOperationSessionId = $nextWorldDropSessionId
    requestSequence = 1; assetKind = 'equipment'; status = 'rejected'; success = $false
    characterDbId = 902; inventoryRevision = 4; equipmentRevision = 0
    errorCode = 'drop_rejected'
} | ConvertTo-Json -Compress))
$v3BagView.TriggerUnequip(5, 59, 'e:equipped-5')
$newSessionUnequip = $v3Network.Sent[-1] | ConvertFrom-Json
Assert-True ($newSessionUnequip.msgId -eq 'equipment_unequip' -and
    $newSessionUnequip.equipmentOperationSessionId -eq $nextEquipmentSessionId -and
    $newSessionUnequip.requestSequence -eq 1) `
    'A new world entry must reset the shared equipment sequence and replace the old session ID.'
$v3Session.BeginSession(903, 'world_001', '0769791c', '0769791c')
$v3Message.characterDbId = 903
$v3Message.requestSequence = 0
$v3Dispatcher.Dispatch(($v3Message | ConvertTo-Json -Depth 10 -Compress))
$sentBeforeShortSession = $v3Network.Sent.Count
$v3BagView.TriggerUnequip(5, 59, 'e:equipped-5')
Assert-True ($v3Network.Sent.Count -eq $sentBeforeShortSession) `
    'A truncated log prefix must never be sent as an equipment session ID.'
$v3BagView.TriggerWorldDrop('e:bag-1')
Assert-True ($v3Network.Sent.Count -eq $sentBeforeShortSession) `
    'A truncated world-drop session ID must never be sent.'
Assert-True (@([UnityEngine.Debug]::Messages | Where-Object {
    $_.StartsWith('[InventoryV3][RX]') }).Count -eq 3) `
    'Numbered V3 response must log only after it is applied.'
$v3Session.Dispose()

$jingjieNetwork = [NetworkManager]::new()
$jingjieDispatcher = [NetMessageDispatcher]::new()
$jingjiePanel = [Game.Inventory.InventoryPanelView]::new()
$jingjieSession = [Game.Inventory.Integration.V2.InventoryV2Session]::new(
    $jingjieNetwork, $jingjieDispatcher, $jingjiePanel, $items, $null,
    $null, $null, $null, $null, $null, $null)
$jingjieError = ''
$jingjieChange = [Game.Inventory.Protocol.V2.InventoryV2StackChangeDto]::new()
$jingjieChange.stackId = 501
$jingjieChange.itemId = 'ling_shi'
$jingjieChanges = [Game.Inventory.Protocol.V2.InventoryV2StackChangeDto[]]@($jingjieChange)
Assert-True (-not $jingjieSession.ApplyJingjieBreakthroughInventoryChanges(
    21, $false, $jingjieChanges, [ref]$jingjieError) -and
    $jingjieNetwork.Sent.Count -eq 0) 'No active character must not accept or request a breakthrough delta.'
$jingjieSession.BeginSession(501, 'world_001')
[UnityEngine.Time]::unscaledTime = 150
Assert-True (-not $jingjieSession.ApplyJingjieBreakthroughInventoryChanges(
    21, $false, $jingjieChanges, [ref]$jingjieError) -and
    ($jingjieNetwork.Sent[-1] | ConvertFrom-Json).msgId -eq 'inventory_snapshot') `
    'Missing inventory ownership must request a full snapshot.'
$jingjieSnapshot = $push | ConvertFrom-Json
$jingjieSnapshot.characterDbId = 501
$jingjieSnapshot.inventoryRevision = 20
$jingjieSnapshot.requestSequence = ($jingjieNetwork.Sent[-1] | ConvertFrom-Json).requestSequence
$jingjieSnapshot.stackItems = @(@{ stackId = 501; itemId = 'ling_shi'
    count = 9; isBound = $false })
$jingjieDispatcher.Dispatch(($jingjieSnapshot | ConvertTo-Json -Depth 10 -Compress))
Assert-True ($jingjieSession.CurrentSnapshot.Stacks[0].Count -eq 9) `
    'A failed breakthrough must not change nine owned materials without a success delta.'

[UnityEngine.Time]::unscaledTime = 152
$jingjieSnapshot.requestSequence = 0
$jingjieSnapshot.inventoryRevision = 22
$jingjieSnapshot.stackItems[0].count = 10
$jingjieDispatcher.Dispatch(($jingjieSnapshot | ConvertTo-Json -Depth 10 -Compress))
$jingjieChange.previousCount = 0
$jingjieChange.itemId = ''
$jingjieChange.newCount = 0
$jingjieRenderedBefore = [Game.Inventory.Presentation.V2.InventoryBagGridPresenter]::RenderCount
Assert-True ($jingjieSession.ApplyJingjieBreakthroughInventoryChanges(
    23, $false, $jingjieChanges, [ref]$jingjieError) -and
    $jingjieSession.CurrentSnapshot.Stacks.Count -eq 0 -and
    [Game.Inventory.Presentation.V2.InventoryBagGridPresenter]::LastCreated.BagAt(0) -eq $null) `
    'Server newCount=0 must remove the material stack from bag ownership.'
Assert-True ($jingjieSession.ApplyJingjieBreakthroughInventoryChanges(
    23, $false, $jingjieChanges, [ref]$jingjieError) -and
    $jingjieSession.CurrentSnapshot.InventoryRevision -eq 23 -and
    [Game.Inventory.Presentation.V2.InventoryBagGridPresenter]::RenderCount -eq
        ($jingjieRenderedBefore + 1)) `
    'A duplicate confirmed revision must not apply or render twice.'

$jingjieSnapshot.inventoryRevision = 24
$jingjieSnapshot.stackItems[0].count = 15
$jingjieDispatcher.Dispatch(($jingjieSnapshot | ConvertTo-Json -Depth 10 -Compress))
$jingjieChange.previousCount = 0
$jingjieChange.itemId = 'ling_shi'
$jingjieChange.newCount = 5
Assert-True ($jingjieSession.ApplyJingjieBreakthroughInventoryChanges(
    25, $false, $jingjieChanges, [ref]$jingjieError) -and
    $jingjieSession.CurrentSnapshot.Stacks[0].Count -eq 5) `
    'Server newCount=5 must replace the quantity, not subtract a local cost.'

[UnityEngine.Time]::unscaledTime = 154
$jingjieChange.itemId = 'other_item'
Assert-True (-not $jingjieSession.ApplyJingjieBreakthroughInventoryChanges(
    26, $false, $jingjieChanges, [ref]$jingjieError) -and
    $jingjieSession.CurrentSnapshot.Stacks[0].Count -eq 5 -and
    ($jingjieNetwork.Sent[-1] | ConvertFrom-Json).msgId -eq 'inventory_snapshot') `
    'A conflicting item identity must leave ownership unchanged and request repair.'
$jingjieChange.itemId = 'ling_shi'
$jingjieSnapshot.requestSequence = ($jingjieNetwork.Sent[-1] | ConvertFrom-Json).requestSequence
$jingjieSnapshot.inventoryRevision = 26
$jingjieSnapshot.stackItems[0].count = 5
$jingjieDispatcher.Dispatch(($jingjieSnapshot | ConvertTo-Json -Depth 10 -Compress))
[UnityEngine.Time]::unscaledTime = 156
Assert-True (-not $jingjieSession.ApplyJingjieBreakthroughInventoryChanges(
    28, $false, $jingjieChanges, [ref]$jingjieError) -and
    $jingjieSession.CurrentSnapshot.InventoryRevision -eq 26 -and
    ($jingjieNetwork.Sent[-1] | ConvertFrom-Json).msgId -eq 'inventory_snapshot') `
    'A revision gap must not apply a partial delta.'
$jingjieSnapshot.requestSequence = ($jingjieNetwork.Sent[-1] | ConvertFrom-Json).requestSequence
$jingjieSnapshot.inventoryRevision = 28
$jingjieDispatcher.Dispatch(($jingjieSnapshot | ConvertTo-Json -Depth 10 -Compress))
[UnityEngine.Time]::unscaledTime = 158
Assert-True (-not $jingjieSession.ApplyJingjieBreakthroughInventoryChanges(
    29, $true, $jingjieChanges, [ref]$jingjieError) -and
    $jingjieSession.CurrentSnapshot.InventoryRevision -eq 28 -and
    ($jingjieNetwork.Sent[-1] | ConvertFrom-Json).msgId -eq 'inventory_snapshot') `
    'A server-required snapshot must take precedence over the supplied delta.'
$jingjieSnapshot.requestSequence = ($jingjieNetwork.Sent[-1] | ConvertFrom-Json).requestSequence
$jingjieSnapshot.inventoryRevision = 29
$jingjieDispatcher.Dispatch(($jingjieSnapshot | ConvertTo-Json -Depth 10 -Compress))

[UnityEngine.Time]::unscaledTime = 160
$jingjieSession.RequestSnapshot()
$pendingSnapshot = $jingjieNetwork.Sent[-1] | ConvertFrom-Json
Assert-True (-not $jingjieSession.ApplyJingjieBreakthroughInventoryChanges(
    30, $false, $jingjieChanges, [ref]$jingjieError) -and
    $jingjieSession.CurrentSnapshot.InventoryRevision -eq 29) `
    'A snapshot request in flight must defer breakthrough changes.'
$jingjieSnapshot.requestSequence = $pendingSnapshot.requestSequence
$jingjieSnapshot.inventoryRevision = 29
$jingjieDispatcher.Dispatch(($jingjieSnapshot | ConvertTo-Json -Depth 10 -Compress))
[UnityEngine.Time]::unscaledTime = 162
$jingjieSession.Tick()
$deferredRequest = $jingjieNetwork.Sent[-1] | ConvertFrom-Json
Assert-True ($deferredRequest.requestSequence -gt $pendingSnapshot.requestSequence) `
    'Deferred breakthrough repair must request a fresh snapshot after the in-flight response.'
$jingjieSnapshot.requestSequence = $deferredRequest.requestSequence
$jingjieSnapshot.inventoryRevision = 30
$jingjieDispatcher.Dispatch(($jingjieSnapshot | ConvertTo-Json -Depth 10 -Compress))

$pendingDiscardField = [Game.Inventory.Integration.V2.InventoryV2Session].GetField(
    'pendingDiscard', [System.Reflection.BindingFlags]'Instance,NonPublic')
$pendingDiscardField.SetValue($jingjieSession,
    [Game.Inventory.Protocol.V2.InventoryV2DiscardRequestDto]::new(1, 30, 501, 1))
[UnityEngine.Time]::unscaledTime = 164
$jingjieChange.previousCount = 5
$jingjieChange.newCount = 4
Assert-True (-not $jingjieSession.ApplyJingjieBreakthroughInventoryChanges(
    31, $false, $jingjieChanges, [ref]$jingjieError) -and
    $jingjieSession.CurrentSnapshot.Stacks[0].Count -eq 5) `
    'A pending local inventory write must defer an external breakthrough delta.'
$pendingDiscardField.SetValue($jingjieSession, $null)
$jingjieSession.Tick()
$repairAfterDiscard = $jingjieNetwork.Sent[-1] | ConvertFrom-Json
Assert-True ($repairAfterDiscard.requestSequence -gt $deferredRequest.requestSequence) `
    'Deferred local-write conflict must request ownership reconciliation.'
$jingjieSnapshot.requestSequence = $repairAfterDiscard.requestSequence
$jingjieSnapshot.inventoryRevision = 31
$jingjieDispatcher.Dispatch(($jingjieSnapshot | ConvertTo-Json -Depth 10 -Compress))

$jingjieNoMutation = [Game.Inventory.Protocol.V2.InventoryV2StackChangeDto[]]@()
Assert-True ($jingjieSession.ApplyJingjieBreakthroughInventoryChanges(
    0, $false, $jingjieNoMutation, [ref]$jingjieError) -and
    $jingjieSession.CurrentSnapshot.InventoryRevision -eq 31) `
    'A breakthrough with no material cost must not reset the inventory revision.'

$jingjieEquipmentView = [Game.Inventory.Presentation.V2.InventoryEquipmentPanelView]::LastCreated
$jingjieEquipmentView.FailNextRender = $true
[UnityEngine.Time]::unscaledTime = 166
Assert-True (-not $jingjieSession.ApplyJingjieBreakthroughInventoryChanges(
    32, $false, $jingjieChanges, [ref]$jingjieError) -and
    $jingjieSession.CurrentSnapshot.InventoryRevision -eq 31 -and
    $jingjieSession.CurrentSnapshot.Stacks[0].Count -eq 5 -and
    [Game.Inventory.Presentation.V2.InventoryBagGridPresenter]::LastRendered.InventoryRevision -eq 31 -and
    $jingjieEquipmentView.LastRendered.InventoryRevision -eq 31 -and
    ($jingjieNetwork.Sent[-1] | ConvertFrom-Json).msgId -eq 'inventory_snapshot') `
    'A failed display must roll both views back and request the authoritative snapshot.'
$jingjieSession.Dispose()

$refiningNetwork = [NetworkManager]::new()
$refiningDispatcher = [NetMessageDispatcher]::new()
$refiningPanel = [Game.Inventory.InventoryPanelView]::new()
$refiningInventory = [Game.Inventory.Integration.V2.InventoryV2Session]::new(
    $refiningNetwork, $refiningDispatcher, $refiningPanel, $items, $null,
    $null, $null, $null, $null, $null, $null)
$refiningInventory.BeginSession(501, 'world_001')
$refiningSnapshot = $jingjieSnapshot | ConvertTo-Json -Depth 10 -Compress |
    ConvertFrom-Json
$refiningSnapshot.requestSequence = 0
$refiningSnapshot.inventoryRevision = 40
$refiningSnapshot.stackItems[0].count = 8
$refiningDispatcher.Dispatch(($refiningSnapshot | ConvertTo-Json -Depth 10 -Compress))
$refiningChange = [Game.Inventory.Protocol.V2.InventoryV2StackChangeDto]::new()
$refiningChange.stackId = 501
$refiningChange.itemId = 'ling_shi'
$refiningChange.previousCount = 0
$refiningChange.newCount = 3
$refiningChanges = [Game.Inventory.Protocol.V2.InventoryV2StackChangeDto[]]@(
    $refiningChange)
$refiningError = ''
$refiningRenderedBefore = [Game.Inventory.Presentation.V2.InventoryBagGridPresenter]::RenderCount
Assert-True ($refiningInventory.ApplyJingjieBreakthroughInventoryChanges(
    41, $false, $refiningChanges, [ref]$refiningError, 'refining_start_result') -and
    $refiningInventory.CurrentSnapshot.Stacks[0].Count -eq 3 -and
    $refiningInventory.CurrentSnapshot.InventoryRevision -eq 41 -and
    [Game.Inventory.Presentation.V2.InventoryBagGridPresenter]::RenderCount -eq
        ($refiningRenderedBefore + 1)) `
    'Refining newCount=3 must replace eight owned materials despite previousCount=0.'
Assert-True ($refiningInventory.ApplyJingjieBreakthroughInventoryChanges(
    41, $false, $refiningChanges, [ref]$refiningError, 'refining_start_result') -and
    [Game.Inventory.Presentation.V2.InventoryBagGridPresenter]::RenderCount -eq
        ($refiningRenderedBefore + 1)) `
    'Repeated refining revision must not apply or render the change twice.'
$refiningChange.newCount = 0
$refiningChange.itemId = ''
Assert-True ($refiningInventory.ApplyJingjieBreakthroughInventoryChanges(
    42, $false, $refiningChanges, [ref]$refiningError, 'refining_start_result') -and
    $refiningInventory.CurrentSnapshot.Stacks.Count -eq 0) `
    'Refining newCount=0 must remove the identified stack.'
[UnityEngine.Time]::unscaledTime = 170
Assert-True (-not $refiningInventory.ApplyJingjieBreakthroughInventoryChanges(
    44, $false, $refiningChanges, [ref]$refiningError, 'refining_start_result') -and
    $refiningInventory.CurrentSnapshot.InventoryRevision -eq 42 -and
    ($refiningNetwork.Sent[-1] | ConvertFrom-Json).msgId -eq
        'inventory_snapshot') `
    'A refining inventory revision gap must request a full snapshot.'
$refiningInventory.Dispose()

$alchemyNetwork = [NetworkManager]::new()
$alchemyDispatcher = [NetMessageDispatcher]::new()
$alchemyInventory = [Game.Inventory.Integration.V2.InventoryV2Session]::new(
    $alchemyNetwork, $alchemyDispatcher,
    [Game.Inventory.InventoryPanelView]::new(), $items, $null,
    $null, $null, $null, $null, $null, $null)
$alchemyInventory.BeginSession(501, 'world_001')
$alchemySnapshot = $refiningSnapshot | ConvertTo-Json -Depth 10 -Compress |
    ConvertFrom-Json
$alchemySnapshot.inventoryRevision = 50
$alchemyDispatcher.Dispatch(($alchemySnapshot | ConvertTo-Json -Depth 10 -Compress))
$award = [Game.Inventory.Protocol.V2.InventoryV2StackChangeDto]::new()
$award.stackId = 777
$award.itemId = 'ling_shi'
$award.previousCount = 0
$award.newCount = 2
$award.isBound = $true
$alchemyError = ''
$awards = [Game.Inventory.Protocol.V2.InventoryV2StackChangeDto[]]@($award)
Assert-True ($alchemyInventory.ApplyJingjieBreakthroughInventoryChanges(
    51, $false, $awards, [ref]$alchemyError, 'alchemy_claim_result', $true) -and
    $alchemyInventory.CurrentSnapshot.InventoryRevision -eq 51 -and
    $alchemyInventory.CurrentSnapshot.Stacks.Count -eq 2 -and
    $alchemyInventory.CurrentSnapshot.Stacks[1].StackId -eq 777 -and
    $alchemyInventory.CurrentSnapshot.Stacks[1].Count -eq 2 -and
    $alchemyInventory.CurrentSnapshot.Stacks[1].IsBound) `
    'Alchemy claim adds the server-assigned absolute stack and actual binding.'
Assert-True ($alchemyInventory.ApplyJingjieBreakthroughInventoryChanges(
    51, $false, $awards, [ref]$alchemyError, 'alchemy_claim_result', $true) -and
    $alchemyInventory.CurrentSnapshot.Stacks.Count -eq 2) `
    'A duplicate award revision must not grant another stack.'
$award.stackId = 778
[UnityEngine.Time]::unscaledTime = 171
Assert-True (-not $alchemyInventory.ApplyJingjieBreakthroughInventoryChanges(
    52, $false, $awards, [ref]$alchemyError, 'alchemy_start_result') -and
    $alchemyInventory.CurrentSnapshot.Stacks.Count -eq 2 -and
    ($alchemyNetwork.Sent[-1] | ConvertFrom-Json).msgId -eq
        'inventory_snapshot') `
    'The original alchemy start path must still reject unowned stack IDs.'
$alchemyInventory.Dispose()

$forgingNetwork = [NetworkManager]::new()
$forgingDispatcher = [NetMessageDispatcher]::new()
$forgingInventory = [Game.Inventory.Integration.V2.InventoryV2Session]::new(
    $forgingNetwork, $forgingDispatcher,
    [Game.Inventory.InventoryPanelView]::new(), $items, $null,
    $null, $null, $null, $null, $null, $null)
$forgingInventory.BeginSession(501, 'world_001')
$forgingSnapshot = $jingjieSnapshot | ConvertTo-Json -Depth 10 -Compress |
    ConvertFrom-Json
$forgingSnapshot.requestSequence = 0
$forgingSnapshot.inventoryRevision = 60
$forgingSnapshot.stackItems[0].count = 5
$forgingSnapshot.wallets = @(@{
    currencyId = 'gold_coin'; balance = 100
})
$forgingDispatcher.Dispatch(
    ($forgingSnapshot | ConvertTo-Json -Depth 10 -Compress))
$forgingStack = [Game.Inventory.Protocol.V2.InventoryV2StackChangeDto]::new()
$forgingStack.stackId = 501
$forgingStack.itemId = ''
$forgingStack.previousCount = 0
$forgingStack.newCount = 0
$forgingWallet = [Game.Inventory.Protocol.V2.InventoryV2WalletDto]::new()
$forgingWallet.currencyId = 'gold_coin'
$forgingWallet.balance = 90
$forgingError = ''
$forgingStacks = [Game.Inventory.Protocol.V2.InventoryV2StackChangeDto[]]@(
    $forgingStack)
$forgingWallets = [Game.Inventory.Protocol.V2.InventoryV2WalletDto[]]@(
    $forgingWallet)
$forgingRenderedBefore =
    [Game.Inventory.Presentation.V2.InventoryBagGridPresenter]::RenderCount
Assert-True ($forgingInventory.ApplyJingjieBreakthroughInventoryChanges(
    61, $false, $forgingStacks, [ref]$forgingError,
    'forging_start_result', $false, $forgingWallets) -and
    $forgingInventory.CurrentSnapshot.Stacks.Count -eq 0 -and
    $forgingInventory.CurrentSnapshot.Wallets['gold_coin'] -eq 90 -and
    $forgingInventory.CurrentSnapshot.InventoryRevision -eq 61 -and
    [Game.Inventory.Presentation.V2.InventoryBagGridPresenter]::RenderCount -eq
        ($forgingRenderedBefore + 1)) `
    'Forging stack and wallet changes must apply atomically.'
Assert-True ($forgingInventory.ApplyJingjieBreakthroughInventoryChanges(
    61, $false, $forgingStacks, [ref]$forgingError,
    'forging_start_result', $false, $forgingWallets) -and
    [Game.Inventory.Presentation.V2.InventoryBagGridPresenter]::RenderCount -eq
        ($forgingRenderedBefore + 1)) `
    'The same inventory revision must never debit twice.'
$forgingWallet.balance = 80
$noStacks = [Game.Inventory.Protocol.V2.InventoryV2StackChangeDto[]]@()
Assert-True ($forgingInventory.ApplyJingjieBreakthroughInventoryChanges(
    62, $false, $noStacks, [ref]$forgingError,
    'forging_start_result', $false, $forgingWallets) -and
    $forgingInventory.CurrentSnapshot.Wallets['gold_coin'] -eq 80 -and
    $forgingInventory.CurrentSnapshot.InventoryRevision -eq 62) `
    'Pure currency forging must advance the inventory revision.'
$duplicateWallets = [Game.Inventory.Protocol.V2.InventoryV2WalletDto[]]@(
    $forgingWallet, $forgingWallet)
[UnityEngine.Time]::unscaledTime = 180
Assert-True (-not $forgingInventory.ApplyJingjieBreakthroughInventoryChanges(
    63, $false, $noStacks, [ref]$forgingError,
    'forging_start_result', $false, $duplicateWallets) -and
    $forgingInventory.CurrentSnapshot.Wallets['gold_coin'] -eq 80 -and
    ($forgingNetwork.Sent[-1] | ConvertFrom-Json).msgId -eq
        'inventory_snapshot') `
    'Duplicate wallet entries must fail without partial application.'
$forgingInventory.Dispose()

$learnNetwork = [NetworkManager]::new()
$learnDispatcher = [NetMessageDispatcher]::new()
$learnInventory = [Game.Inventory.Integration.V2.InventoryV2Session]::new(
    $learnNetwork, $learnDispatcher,
    [Game.Inventory.InventoryPanelView]::new(), $items, $null,
    $null, $null, $null, $null, $null, $null)
$learnInventory.BeginSession(501, 'world_001')
$learnSnapshot = $forgingSnapshot | ConvertTo-Json -Depth 10 -Compress |
    ConvertFrom-Json
$learnSnapshot.inventoryRevision = 70
$learnSnapshot.stackItems[0].itemId = 'tz001'
$learnSnapshot.stackItems[0].count = 1
$learnDispatcher.Dispatch(($learnSnapshot | ConvertTo-Json -Depth 10 -Compress))
$learnChange = [Game.Inventory.Protocol.V2.InventoryV2StackChangeDto]::new()
$learnChange.stackId = 501
$learnChange.itemId = ''
$learnChange.previousCount = 0
$learnChange.newCount = 0
$learnChanges = [Game.Inventory.Protocol.V2.InventoryV2StackChangeDto[]]@(
    $learnChange)
$learnError = ''
Assert-True ($learnInventory.ApplyJingjieBreakthroughInventoryChanges(
    71, $false, $learnChanges, [ref]$learnError,
    'forging_learn_result') -and
    $learnInventory.CurrentSnapshot.InventoryRevision -eq 71 -and
    $learnInventory.CurrentSnapshot.Stacks.Count -eq 0) `
    'An empty itemId and placeholder previousCount must remove the last blueprint.'
Assert-True ($learnInventory.ApplyJingjieBreakthroughInventoryChanges(
    71, $false, $learnChanges, [ref]$learnError,
    'forging_learn_result') -and
    $learnInventory.CurrentSnapshot.Stacks.Count -eq 0) `
    'An already-applied revision must never consume the blueprint twice.'
$learnInventory.Dispose()

$claimNetwork = [NetworkManager]::new()
$claimDispatcher = [NetMessageDispatcher]::new()
$claimInventory = [Game.Inventory.Integration.V2.InventoryV2Session]::new(
    $claimNetwork, $claimDispatcher,
    [Game.Inventory.InventoryPanelView]::new(), $items, $null,
    $null, $null, $null, $null, $null, $null)
$claimInventory.BeginSession(501, 'world_001')
$claimDispatcher.Dispatch(
    ($forgingSnapshot | ConvertTo-Json -Depth 10 -Compress))
$claimEquipment = [Game.Forging.Protocol.锻造装备变化]::new()
$claimEquipment.itemInstanceId = '55555555-5555-4555-8555-555555555555'
$claimEquipment.itemId = 'ring_xingyun_001_item'
$claimEquipment.equipmentId = 'ring_xingyun_001'
$claimEquipment.isBound = $true
$claimEquipment.durability = 100
$claimChanges = [Game.Forging.Protocol.锻造装备变化[]]@($claimEquipment)
$claimWallets = [Game.Inventory.Protocol.V2.InventoryV2WalletDto[]]@()
$claimError = ''
Assert-True ($claimInventory.ApplyForgingClaimInventoryChanges(
    61, $noStacks, $claimChanges, $claimWallets, [ref]$claimError) -and
    $claimInventory.CurrentSnapshot.Equipment.Count -eq 1 -and
    $claimInventory.CurrentSnapshot.Equipment[0].ItemInstanceId -eq
        $claimEquipment.itemInstanceId -and
    $claimInventory.CurrentSnapshot.Equipment[0].IsBound) `
    'A consecutive claim mutation must add its independent bound equipment.'
Assert-True ($claimInventory.ApplyForgingClaimInventoryChanges(
    61, $noStacks, $claimChanges, $claimWallets, [ref]$claimError) -and
    $claimInventory.CurrentSnapshot.Equipment.Count -eq 1) `
    'A repeated claim revision must not add another equipment instance.'
[UnityEngine.Time]::unscaledTime = 190
Assert-True (-not $claimInventory.ApplyForgingClaimInventoryChanges(
    62, $noStacks, $claimChanges, $claimWallets, [ref]$claimError) -and
    $claimInventory.CurrentSnapshot.InventoryRevision -eq 61 -and
    ($claimNetwork.Sent[-1] | ConvertFrom-Json).msgId -eq
        'inventory_snapshot') `
    'An existing instance with a new revision must request repair.'
$claimInventory.Dispose()

$slotSource = Get-Content (Join-Path $root 'Assets/Scripts/Inventory/Presentation/InventorySlotView.cs') -Raw
Assert-True ($slotSource.Contains('eventData.clickCount == 2') -and
    $slotSource.Contains('V2DoubleClicked?.Invoke(this)')) 'V2 click must require a double-click.'
$bagPresenterSource = Get-Content (
    Join-Path $root 'Assets/Scripts/Inventory/Presentation/V2/InventoryBagGridPresenter.cs') -Raw
Assert-True ($bagPresenterSource.Contains('view.V2DoubleClicked += HandleDoubleClick;') -and
    $bagPresenterSource.Contains('StackDoubleClicked?.Invoke(view.V2Identity);') -and
    -not $bagPresenterSource.Contains('UseRequested?.Invoke(view.V2Identity);')) `
    'The real bag grid must forward the clicked stack to the catalog-aware session.'
$equipmentSlotSource = Get-Content (Join-Path $root 'Assets/Scripts/Inventory/Presentation/V2/InventoryEquipmentSlotView.cs') -Raw
$categoryMethod = [Game.Inventory.Integration.V2.InventoryV2Session].GetMethod(
    'ExpectedCategory', [System.Reflection.BindingFlags]'NonPublic, Static')
foreach ($pair in @(@(9, 7), @(10, 8), @(11, 9), @(12, 10))) {
    Assert-True ($categoryMethod.Invoke($null, @($pair[0])) -eq $pair[1]) `
        "Slot $($pair[0]) must use its fixed equipment category $($pair[1])."
}
Assert-True (-not $slotSource.Contains('SetUnknownPlaceholder') -and
    -not $slotSource.Contains('未知物品占位图') -and
    $slotSource.Contains('stackCountText.text = count > 1 ? count.ToString()') -and
    $equipmentSlotSource.Contains('new GameObject("未知装备占位图"') -and
    $equipmentSlotSource.Contains('unknownPlaceholder.gameObject.SetActive(itemIdentity != null && sprite == null)') -and
    $equipmentSlotSource.Contains('unknownPlaceholder.raycastTarget = false')) `
    'Bag slots must use only prefab visuals; equipment unknown placeholders remain unchanged.'
Write-Output 'PASS: V2 snapshot, equipment, pickup, item use, stack/equipment world drop and repair.'
