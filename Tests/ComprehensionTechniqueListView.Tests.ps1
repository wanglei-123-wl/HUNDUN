$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$folder = Join-Path $root 'Assets/Scripts/Cultivation/Presentation'
$item = Get-Content -LiteralPath (Join-Path $folder 'ComprehensionTechniqueItemView.cs') -Raw
$entry = Get-Content -LiteralPath (Join-Path $folder 'LearnedTechniqueDisplayEntry.cs') -Raw
$list = Get-Content -LiteralPath (Join-Path $folder 'ComprehensionTechniqueListView.cs') -Raw
$meta = Get-Content -LiteralPath (Join-Path $folder 'ComprehensionTechniqueListView.cs.meta') -Raw
$prefabMeta = Get-Content -LiteralPath (
    Join-Path $root 'Assets/预制体/格子预制体/参悟面板功法预制体.prefab.meta') -Raw
$scene = Get-Content -LiteralPath (Join-Path $root 'Assets/Scenes/WorldShell.unity') -Raw

$stubs = @'
using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Cultivation.Protocol;
using Game.Cultivation.Runtime;
using Game.Inventory;
using Game.Inventory.Model.V2;
using Game.Inventory.Presentation.V2;
using Game.Items;
using Game.UI.Notice;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class DisallowMultipleComponent : Attribute {}
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SerializeField : Attribute {}
    public class Sprite {}
    public struct Color { public static Color white => new Color(); }
    public static class Time {
        public static double realtimeSinceStartupAsDouble;
        public static float unscaledTime;
    }
    public static class Mathf {
        public static float Clamp01(float value) => Math.Max(0, Math.Min(1, value));
    }
    public class Transform
    {
        public Transform parent;
        public readonly List<Transform> children = new List<Transform>();
        public MonoBehaviour owner;
        public void SetSiblingIndex(int index)
        {
            if (parent == null) return;
            parent.children.Remove(this);
            parent.children.Insert(index, this);
        }
    }
    public class RectTransform : Transform {}
    public class GameObject
    {
        public bool activeSelf = true;
        public Transform transform;
        private readonly Dictionary<Type,object> components = new();
        public void SetActive(bool active) { activeSelf = active; }
        public T GetComponent<T>() where T : class =>
            components.TryGetValue(typeof(T), out object value) ? value as T : null;
        public void SetComponent<T>(T value) where T : class =>
            components[typeof(T)] = value;
    }
    public class MonoBehaviour
    {
        public readonly GameObject gameObject = new GameObject();
        public readonly Transform transform = new Transform();
        public bool isActiveAndEnabled = true;
        public MonoBehaviour()
        {
            gameObject.transform = transform;
            transform.owner = this;
        }
        protected static T FindObjectOfType<T>() where T : class => null;
        protected static T Instantiate<T>(T prefab, RectTransform parent, bool keepWorld)
            where T : MonoBehaviour, new()
        {
            T result = new T();
            var highlight = typeof(T).GetField("selectedEffect",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            highlight?.SetValue(result, new GameObject());
            result.transform.parent = parent;
            parent.children.Add(result.transform);
            return result;
        }
        protected static void Destroy(GameObject obj)
        {
            if (obj.transform.parent != null)
                obj.transform.parent.children.Remove(obj.transform);
        }
    }
}
namespace TMPro { public class TMP_Text { public string text; public bool enableWordWrapping; } }
namespace UnityEngine.UI
{
    public class Image
    {
        public Sprite sprite;
        public Color color;
        public bool enabled;
        public bool preserveAspect;
        public float fillAmount;
    }
    public class ScrollRect { public float verticalNormalizedPosition; }
    public static class LayoutRebuilder
    {
        public static void ForceRebuildLayoutImmediate(RectTransform content) {}
    }
    public class Button {
        public bool interactable = true;
        public readonly ClickEvent onClick = new ClickEvent();
        public class ClickEvent {
            private event Action handlers;
            public void AddListener(Action handler) { handlers += handler; }
            public void RemoveListener(Action handler) { handlers -= handler; }
            public void Invoke() { handlers?.Invoke(); }
        }
    }
}
namespace Game.Items {
    public class ItemDefinition { public Sprite Icon; public string DisplayName; }
    public class ItemDefinitionTable {
        public readonly Dictionary<string,ItemDefinition> items = new();
        public bool TryGet(string id, out ItemDefinition item)
            => items.TryGetValue(id, out item);
    }
}
namespace Game.Inventory {
    public class InventoryModuleRuntime {
        public ItemDefinitionTable ItemDefinitions;
        public InventoryOwnershipSnapshot CurrentSnapshot;
    }
}
namespace Game.Inventory.Model.V2 {
    public class InventoryStackState {
        public long StackId; public string ItemId; public int Count;
        public string Identity => "s:" + StackId;
    }
    public class InventoryOwnershipSnapshot {
        public long CharacterDbId;
        public long InventoryRevision;
        public readonly List<InventoryStackState> Stacks = new();
        public bool TryGetItemCount(string id, out long count) {
            count = 0;
            foreach (var stack in Stacks)
                if (stack.ItemId == id) count += stack.Count;
            return count > 0;
        }
    }
}
namespace Game.Inventory.Presentation.V2 {
    public class InventoryBagDragSlot {
        public string DragIdentity; public string ItemId;
    }
}
namespace Game.UI.Notice {
    public static class GameNoticeService {
        public static string LastMessage;
        public static void Show(string text) { LastMessage = text; }
    }
}
namespace UnityEngine.EventSystems {
    public interface IDropHandler { void OnDrop(PointerEventData data); }
    public class PointerEventData { public GameObject pointerDrag; }
}
namespace Game.Cultivation.Protocol {
    public class CultivationWriteResultDto {
        public bool success; public bool idempotentReplay;
        public string errorCode; public CultivationMaterialDto[] missingMaterials;
    }
    public class CultivationMaterialDto { public string itemId; public int quantity; }
    public class CultivationLearnedTechniqueDto {
        public string techniqueId; public int level;
    }
    public class CultivationTechniqueDto {
        public string techniqueId; public string name; public string gradeId;
        public CultivationLevelDto[] levels; public bool enabled = true;
    }
    public class CultivationGradeDto { public string gradeId; public string name; }
    public class CultivationLevelDto {
        public int level; public int comprehension; public long durationSeconds;
        public int referenceComprehension; public bool hasReferenceComprehension;
        public CultivationMaterialDto[] materials;
    }
    public class CultivationTaskDto {
        public string taskId; public string techniqueId;
        public long startedAt; public long endsAt;
    }
    public class CultivationSnapshotDto {
        public long characterDbId; public long totalComprehension;
        public CultivationLearnedTechniqueDto[] techniques;
        public CultivationTaskDto activeTask;
    }
    public class CultivationCatalogResultDto {
        public CultivationTechniqueDto[] techniques;
        public CultivationGradeDto[] grades;
    }
    public class CultivationItemDefinitionDto {
        public string kind; public string learnTechniqueId; public string itemId;
    }
    public class CultivationItemCatalogResultDto {
        public CultivationItemDefinitionDto[] items;
    }
}
namespace Game.Cultivation.Runtime {
    public class CultivationSessionRuntime {
        public static CultivationSessionRuntime Instance;
        public bool IsConfigurationReady;
        public bool HasAcceptedSnapshot;
        public CultivationCatalogResultDto Catalog;
        public CultivationItemCatalogResultDto ItemCatalog;
        public CultivationSnapshotDto Snapshot;
        public long LastSnapshotServerTime;
        public int SnapshotRequests;
        public int Refreshes;
        public event Action<CultivationSnapshotDto> SnapshotChanged;
        public event Action<CultivationCatalogResultDto> CatalogChanged;
        public event Action<CultivationItemCatalogResultDto> ItemCatalogChanged;
        public event Action<CultivationWriteResultDto> LearnResultReceived;
        public event Action<string> LearnRejected;
        public event Action<CultivationWriteResultDto> StartResultReceived;
        public event Action<string> StartRejected;
        public bool HasUnresolvedOperation;
        public long LearnedStackId;
        public string StartedTechniqueId;
        public bool CanStartWrite = true;
        public bool TryGetNextLevel(string id, out CultivationLevelDto next) {
            next = null;
            if (!IsConfigurationReady || Snapshot == null || Catalog == null) return false;
            foreach (var learned in Snapshot.techniques)
                if (learned.techniqueId == id)
                    foreach (var technique in Catalog.techniques)
                        if (technique.techniqueId == id)
                            foreach (var level in technique.levels)
                                if (level.level == learned.level + 1) {
                                    next = level; return true;
                                }
            return false;
        }
        public static bool TryEstimateDurationSeconds(CultivationLevelDto level,
            long comprehension, out long seconds) {
            seconds = level?.durationSeconds ?? 0; return seconds > 0;
        }
        public bool CanStart(string id, InventoryOwnershipSnapshot ownership = null) =>
            CanStartWrite && !HasUnresolvedOperation
            && Snapshot?.activeTask == null
            && TryGetNextLevel(id, out _);
        public bool TryStart(string id) {
            if (!CanStart(id)) return false;
            StartedTechniqueId = id; HasUnresolvedOperation = true;
            return true;
        }
        public void NotifyStartResult(CultivationWriteResultDto result) {
            HasUnresolvedOperation = false;
            StartResultReceived?.Invoke(result);
        }
        public void NotifyStartRejected(string error) =>
            StartRejected?.Invoke(error);
        public bool TryResolveTechniqueBook(string id,
            out CultivationItemDefinitionDto book, out CultivationTechniqueDto technique) {
            book = null; technique = null;
            if (ItemCatalog == null || Catalog == null) return false;
            foreach (var item in ItemCatalog.items)
                if (item.itemId == id && item.kind == "techniquebook")
                    foreach (var known in Catalog.techniques)
                        if (known.techniqueId == item.learnTechniqueId) {
                            book = item; technique = known; return true;
                        }
            return false;
        }
        public bool TryLearn(long stackId) { LearnedStackId = stackId; return true; }
        public void Refresh() { Refreshes++; }
        public bool RequestSnapshot() { SnapshotRequests++; return true; }
        public void NotifySnapshot() { SnapshotChanged?.Invoke(Snapshot); }
        public void NotifyCatalog() { CatalogChanged?.Invoke(Catalog); }
        public void NotifyItems() { ItemCatalogChanged?.Invoke(ItemCatalog); }
    }
}
public sealed class NetworkManager
{
    public static NetworkManager Instance;
    public bool IsConnected = true;
    public long ConnectionEpoch = 1;
    public event Action<string> OnDisconnected;
    public void Disconnect()
    {
        IsConnected = false;
        OnDisconnected?.Invoke("test");
    }
}
public sealed class GameSession
{
    public bool IsWorldReady = true;
    public long LocalCharacterDbId = 42;
    public event Action<long, long> LocalWorldEntered;
    public void EnterWorld(long id)
    {
        LocalCharacterDbId = id;
        LocalWorldEntered?.Invoke(1, id);
    }
}
'@
foreach ($using in @($item, $entry, $list)) {
    $stubs += "`n" + ($using -replace '(?m)^using [^;]+;\r?\n', '')
}
Add-Type -TypeDefinition $stubs -IgnoreWarnings | Out-Null

function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
}
function Assert-Selected($root, [string]$expected) {
    $flags = [System.Reflection.BindingFlags]'Instance,NonPublic'
    $visible = foreach ($child in $root.children) {
        $row = $child.owner
        if ($row.GetType().GetField('selectedEffect', $flags).GetValue($row).activeSelf) {
            $row.TechniqueId
        }
    }
    Assert-True (($visible -join ',') -eq $expected) 'Only the selected technique should be highlighted.'
}
function Set-Private($target, [string]$field, $value) {
    $target.GetType().GetField(
        $field, [System.Reflection.BindingFlags]'Instance,NonPublic'
    ).SetValue($target, $value)
}
function Invoke-Lifecycle($target, [string]$method) {
    $target.GetType().GetMethod(
        $method, [System.Reflection.BindingFlags]'Instance,NonPublic'
    ).Invoke($target, @()) | Out-Null
}
function SceneBlock([int]$kind, [long]$id) {
    $pattern = "(?s)--- !u!$kind &$id`r?`n(?<body>.*?)(?=`r?`n--- !u!|$)"
    $matches = [regex]::Matches($scene, $pattern)
    Assert-True ($matches.Count -eq 1) "Scene block $kind/$id must exist once."
    return $matches[0].Groups['body'].Value
}

$guid = [regex]::Match($meta, '(?m)^guid: ([0-9a-f]{32})$').Groups[1].Value
$prefabGuid = [regex]::Match($prefabMeta, '(?m)^guid: ([0-9a-f]{32})$').Groups[1].Value
Assert-True ((SceneBlock 1 1527283010) -match
    'component: \{fileID: 2200000010\}') 'The scroll list must own its controller.'
Assert-True ((SceneBlock 114 2200000010) -match
    "(?s)guid: $guid.*?session: \{fileID: 2108349142\}.*?" +
    'contentRoot: \{fileID: 183202210\}.*?' +
    "itemPrefab: \{fileID: 900000000000000101, guid: $prefabGuid, type: 3\}.*?" +
    'scrollRect: \{fileID: 1527283012\}.*?' +
    'inventory: \{fileID: 390961493\}.*?' +
    'selectedIcon: \{fileID: 1169892803\}.*?' +
    'selectedName: \{fileID: 628647205\}.*?' +
    'selectedGrade: \{fileID: 733064936\}.*?' +
    'selectedDetails: \{fileID: 1445598711\}.*?' +
    'taskFill: \{fileID: 1245288150\}.*?' +
    'taskProgress: \{fileID: 253644563\}.*?' +
    'startButton: \{fileID: 1828698652\}') 'Technique details must bind the comprehension panel only.'
Assert-True ((SceneBlock 114 2200000010) -match
    'materialsText: \{fileID: 2200000033\}' -and
    (SceneBlock 114 2200000010) -match
    'materialsScroll: \{fileID: 2200000040\}' -and
    (SceneBlock 224 2200000035) -match
    'm_Father: \{fileID: 758649459\}') `
    'The recipe text must belong to the technique detail panel.'
Assert-True ((SceneBlock 114 2200000040) -match
    '(?s)m_Content: \{fileID: 2200000031\}.*?m_Viewport: \{fileID: 2200000035\}' -and
    (SceneBlock 114 2200000039) -match 'm_VerticalFit: 2') `
    'Long recipes must scroll inside their clipped viewport.'
Assert-True ((SceneBlock 1 1192511591) -notmatch
    'component: \{fileID: 1192511593\}') `
    'The comprehension task progress must not retain the player health driver.'
Assert-True ((SceneBlock 114 1527283012) -match 'm_Horizontal: 0') `
    'The technique list should scroll vertically only.'
Assert-True ((SceneBlock 1 183202209) -match
    '(?s)component: \{fileID: 2200000011\}.*?component: \{fileID: 2200000012\}') `
    'Content must have a layout group and size fitter.'
Assert-True ((SceneBlock 224 183202210) -match
    'm_AnchorMin: \{x: 0, y: 1\}') 'The list must grow downward from the top.'
Assert-True ((SceneBlock 114 2200000011) -match
    '(?s)guid: 59f8146938fff824cb5fd77236b75775.*?m_ChildControlHeight: 0') `
    'Keep the authored row height.'
Assert-True ((SceneBlock 114 2200000012) -match 'm_VerticalFit: 2') `
    'The content height should follow the list rows.'
Assert-True ((SceneBlock 224 1527283011) -match
    'm_Father: \{fileID: 1581756385\}' -and
    (SceneBlock 224 1581756385) -match 'm_Father: \{fileID: 112115725\}') `
    'The technique list belongs to the manually toggled cultivation window.'
# The panel's authoring visibility/scale is owned by the scene designer.

$network = [NetworkManager]::new()
[NetworkManager]::Instance = $network
$session = [GameSession]::new()
$view = [Game.Cultivation.Presentation.ComprehensionTechniqueListView]::new()
$content = [UnityEngine.RectTransform]::new()
$prefab = [Game.Cultivation.Presentation.ComprehensionTechniqueItemView]::new()
$scroll = [UnityEngine.UI.ScrollRect]::new()
Set-Private $view 'session' $session
Set-Private $view 'contentRoot' $content
Set-Private $view 'itemPrefab' $prefab
Set-Private $view 'scrollRect' $scroll
Invoke-Lifecycle $view 'OnEnable'

$a = [Game.Cultivation.Presentation.LearnedTechniqueDisplayEntry]::new(
    'a', '功法甲', $null, '初学')
$b = [Game.Cultivation.Presentation.LearnedTechniqueDisplayEntry]::new(
    'b', '功法乙', $null, '熟练')
$invalid = [Game.Cultivation.Presentation.LearnedTechniqueDisplayEntry]::new(
    ' ', '无效', $null, '')
$entries = [Game.Cultivation.Presentation.LearnedTechniqueDisplayEntry[]]@($a, $b, $a, $invalid)
Assert-True (-not $view.ApplySnapshot(99, $entries) -and $view.VisibleCount -eq 0) `
    'Another character cannot populate the local list.'
Assert-True ($view.ApplySnapshot(42, $entries) -and $view.VisibleCount -eq 2 -and
    $content.children.Count -eq 2 -and
    $content.children[0].owner.TechniqueId -eq 'a' -and
    $content.children[1].owner.TechniqueId -eq 'b' -and
    $scroll.verticalNormalizedPosition -eq 1) `
    'Valid entries should appear in order without duplicate IDs.'
Assert-Selected $content 'a'
$select = $view.GetType().GetMethod(
    'SelectTechnique', [System.Reflection.BindingFlags]'Instance,NonPublic')
$select.Invoke($view, @('b')) | Out-Null
Assert-Selected $content 'b'
$view.ApplySnapshot(42, $entries) | Out-Null
Assert-Selected $content 'b'

$oldA = $content.children[0].owner
$reversed = [Game.Cultivation.Presentation.LearnedTechniqueDisplayEntry[]]@($b, $a)
Assert-True ($view.ApplySnapshot(42, $reversed) -and
    [object]::ReferenceEquals($content.children[1].owner, $oldA) -and
    $content.children[0].owner.TechniqueId -eq 'b') `
    'Repeated snapshots must reuse items and reorder them.'
$select.Invoke($view, @('a')) | Out-Null
Assert-Selected $content 'a'
Assert-True ($view.ApplySnapshot(42,
    [Game.Cultivation.Presentation.LearnedTechniqueDisplayEntry[]]@($b)) -and
    $view.VisibleCount -eq 1 -and $content.children.Count -eq 1) `
    'Removing a learned technique should remove its row.'
Assert-Selected $content 'b'
Assert-True ($view.ApplySnapshot(42,
    [Game.Cultivation.Presentation.LearnedTechniqueDisplayEntry[]]@()) -and
    $view.VisibleCount -eq 0 -and $content.children.Count -eq 0) `
    'An authoritative empty snapshot must leave the list empty.'
Assert-Selected $content ''
$session.IsWorldReady = $false
Assert-True (-not $view.ApplySnapshot(42, $entries)) `
    'A session not ready for world updates cannot populate the list.'
$session.IsWorldReady = $true
$view.ApplySnapshot(42,
    [Game.Cultivation.Presentation.LearnedTechniqueDisplayEntry[]]@($b)) | Out-Null

$view.isActiveAndEnabled = $false
Invoke-Lifecycle $view 'OnDisable'
Assert-True ($view.ApplySnapshot(42,
    [Game.Cultivation.Presentation.LearnedTechniqueDisplayEntry[]]@($a)) -and
    $content.children[0].owner.TechniqueId -eq 'b') `
    'A hidden panel should cache new data without rendering.'
$view.isActiveAndEnabled = $true
Invoke-Lifecycle $view 'OnEnable'
Assert-True ($view.VisibleCount -eq 1 -and
    $content.children[0].owner.TechniqueId -eq 'a') `
    'Reopening must show the cached snapshot.'

$session.EnterWorld(43)
Assert-True ($view.VisibleCount -eq 0 -and $content.children.Count -eq 0) `
    'Changing character must remove the old list.'
$session.LocalCharacterDbId = 42
$view.ApplySnapshot(42, [Game.Cultivation.Presentation.LearnedTechniqueDisplayEntry[]]@($a)) | Out-Null
$network.ConnectionEpoch = 2
Invoke-Lifecycle $view 'Update'
Assert-True ($view.VisibleCount -eq 0 -and $content.children.Count -eq 0) `
    'A replaced connection must not retain the previous connection list.'
Assert-True ($view.ApplySnapshot(42,
    [Game.Cultivation.Presentation.LearnedTechniqueDisplayEntry[]]@($a)) -and
    $view.VisibleCount -eq 1) `
    'The new connection may accept its own fresh snapshot.'
$oldNetwork = $network
$network = [NetworkManager]::new()
[NetworkManager]::Instance = $network
Invoke-Lifecycle $view 'Update'
Assert-True ($view.VisibleCount -eq 0 -and $content.children.Count -eq 0) `
    'Replacing the network object must remove the previous connection rows.'
Assert-True ($view.ApplySnapshot(42,
    [Game.Cultivation.Presentation.LearnedTechniqueDisplayEntry[]]@($a))) `
    'Only the new network object can accept a full list.'
$oldNetwork.Disconnect()
Assert-True ($view.VisibleCount -eq 1) `
    'An old connection event cannot clear the new connection list.'
$network.Disconnect()
Assert-True ($view.VisibleCount -eq 0 -and $content.children.Count -eq 0 -and
    -not $view.ApplySnapshot(42, $entries)) `
    'Disconnect must remove rows and reject stale data.'
Invoke-Lifecycle $view 'OnDisable'

$network = [NetworkManager]::new()
[NetworkManager]::Instance = $network
$session = [GameSession]::new()
$runtime = [Game.Cultivation.Runtime.CultivationSessionRuntime]::new()
$runtime.IsConfigurationReady = $true
$runtime.HasAcceptedSnapshot = $true
[Game.Cultivation.Runtime.CultivationSessionRuntime]::Instance = $runtime
$catalog = [Game.Cultivation.Protocol.CultivationCatalogResultDto]::new()
$technique = [Game.Cultivation.Protocol.CultivationTechniqueDto]::new()
$technique.techniqueId = 'alpha'
$technique.name = '玄青诀'
$technique.gradeId = 'ordinary'
$level = [Game.Cultivation.Protocol.CultivationLevelDto]::new()
$level.level = 0
$level.comprehension = 5
$nextLevel = [Game.Cultivation.Protocol.CultivationLevelDto]::new()
$nextLevel.level = 1
$nextLevel.durationSeconds = 28800
$stoneRequirement = [Game.Cultivation.Protocol.CultivationMaterialDto]::new()
$stoneRequirement.itemId = 'stone'
$stoneRequirement.quantity = 2
$nextLevel.materials = @($stoneRequirement)
$technique.levels = @($level, $nextLevel)
$catalog.techniques = @($technique)
$grade = [Game.Cultivation.Protocol.CultivationGradeDto]::new()
$grade.gradeId = 'ordinary'
$grade.name = '凡品'
$catalog.grades = @($grade)
$runtime.Catalog = $catalog
$items = [Game.Cultivation.Protocol.CultivationItemCatalogResultDto]::new()
$book = [Game.Cultivation.Protocol.CultivationItemDefinitionDto]::new()
$book.itemId = 'book_alpha'
$book.kind = 'techniquebook'
$book.learnTechniqueId = 'alpha'
$items.items = @($book)
$runtime.ItemCatalog = $items
$bag = [Game.Inventory.InventoryModuleRuntime]::new()
$bag.ItemDefinitions = [Game.Items.ItemDefinitionTable]::new()
$bookIcon = [UnityEngine.Sprite]::new()
$localBook = [Game.Items.ItemDefinition]::new()
$localBook.Icon = $bookIcon
$bag.ItemDefinitions.items['book_alpha'] = $localBook
$snapshot = [Game.Cultivation.Protocol.CultivationSnapshotDto]::new()
$snapshot.characterDbId = 42
$snapshot.totalComprehension = 15
$learned = [Game.Cultivation.Protocol.CultivationLearnedTechniqueDto]::new()
$learned.techniqueId = 'alpha'
$learned.level = 0
$snapshot.techniques = @($learned)
$task = [Game.Cultivation.Protocol.CultivationTaskDto]::new()
$task.taskId = 'task_alpha'
$task.techniqueId = 'alpha'
$task.startedAt = 999000
$task.endsAt = 1009000
$snapshot.activeTask = $task
$runtime.Snapshot = $snapshot
$runtime.LastSnapshotServerTime = 1000000
$display = [Game.Cultivation.Presentation.ComprehensionTechniqueListView]::new()
$content = [UnityEngine.RectTransform]::new()
$scroll = [UnityEngine.UI.ScrollRect]::new()
$icon = [UnityEngine.UI.Image]::new()
$name = [TMPro.TMP_Text]::new()
$gradeText = [TMPro.TMP_Text]::new()
$details = [TMPro.TMP_Text]::new()
$materials = [TMPro.TMP_Text]::new()
$materialsScroll = [UnityEngine.UI.ScrollRect]::new()
$fill = [UnityEngine.UI.Image]::new()
$progress = [TMPro.TMP_Text]::new()
$start = [UnityEngine.UI.Button]::new()
Set-Private $display 'session' $session
Set-Private $display 'contentRoot' $content
Set-Private $display 'itemPrefab' $prefab
Set-Private $display 'scrollRect' $scroll
Set-Private $display 'inventory' $bag
Set-Private $display 'selectedIcon' $icon
Set-Private $display 'selectedName' $name
Set-Private $display 'selectedGrade' $gradeText
Set-Private $display 'selectedDetails' $details
Set-Private $display 'materialsText' $materials
Set-Private $display 'materialsScroll' $materialsScroll
Set-Private $display 'taskFill' $fill
Set-Private $display 'taskProgress' $progress
Set-Private $display 'startButton' $start
[UnityEngine.Time]::realtimeSinceStartupAsDouble = 10
Invoke-Lifecycle $display 'OnEnable'
Assert-True ($runtime.Refreshes -eq 1 -and $display.VisibleCount -eq 1 -and
    $name.text -eq '玄青诀' -and $gradeText.text -eq '凡品' -and
    $details.text.Contains('悟性 +5') -and $details.text.Contains('总悟性 15') -and
    $details.text.Contains('预计 8:00:00') -and
    $materials.text.Contains('stone') -and
    $icon.enabled -and [object]::ReferenceEquals($icon.sprite, $bookIcon) -and
    $progress.text -eq '0:00:09' -and
    [Math]::Abs($fill.fillAmount - 0.1) -lt 0.001 -and
    -not $start.interactable) `
    'Authoritative snapshot must populate the learned row and selected read-only details.'
Assert-True (-not ([UnityEngine.EventSystems.IDropHandler].IsAssignableFrom(
    [Game.Cultivation.Presentation.ComprehensionTechniqueListView]))) `
    'The learned-technique list must not accept book drops.'
$bag.CurrentSnapshot = [Game.Inventory.Model.V2.InventoryOwnershipSnapshot]::new()
$bag.CurrentSnapshot.CharacterDbId = 42
[UnityEngine.Time]::realtimeSinceStartupAsDouble = 13
Invoke-Lifecycle $display 'Update'
Assert-True ($progress.text -eq '0:00:06' -and
    [Math]::Abs($fill.fillAmount - 0.4) -lt 0.001) `
    'Only monotonic elapsed time may advance the task progress.'
$otherTechnique = [Game.Cultivation.Protocol.CultivationTechniqueDto]::new()
$otherTechnique.techniqueId = 'beta'
$otherTechnique.name = '另一门功法'
$otherTechnique.gradeId = 'ordinary'
$otherTechnique.levels = @($level)
$catalog.techniques = @($technique, $otherTechnique)
$otherLearned = [Game.Cultivation.Protocol.CultivationLearnedTechniqueDto]::new()
$otherLearned.techniqueId = 'beta'
$snapshot.techniques = @($learned, $otherLearned)
$runtime.NotifySnapshot()
$select = $display.GetType().GetMethod(
    'SelectTechnique', [System.Reflection.BindingFlags]'Instance,NonPublic')
$select.Invoke($display, @('beta')) | Out-Null
Assert-True ($display.VisibleCount -eq 2 -and $name.text -eq '另一门功法' -and
    [string]::IsNullOrEmpty($progress.text) -and $fill.fillAmount -eq 0) `
    'Selecting another learned technique must not borrow the active task progress.'
Assert-Selected $content 'beta'
[Game.UI.Notice.GameNoticeService]::LastMessage = $null
Assert-True $start.interactable 'The other technique button must allow a notice.'
$start.onClick.Invoke()
Assert-True ([Game.UI.Notice.GameNoticeService]::LastMessage -eq '其他功法正在参悟中') `
    'Selecting another technique must show the conflict only when clicked.'
[UnityEngine.Time]::realtimeSinceStartupAsDouble = 24
Invoke-Lifecycle $display 'Update'
Assert-True ($runtime.SnapshotRequests -eq 1 -and
    [string]::IsNullOrEmpty($progress.text)) `
    'Completion must be checked even while a different technique is selected.'
$select.Invoke($display, @('alpha')) | Out-Null
Invoke-Lifecycle $display 'Update'
Assert-True ([string]::IsNullOrEmpty($progress.text) -and $fill.fillAmount -eq 1 -and
    $runtime.SnapshotRequests -eq 1 -and $learned.level -eq 0 -and
    $null -ne $snapshot.activeTask) `
    'Reaching the server deadline only queries a snapshot; it cannot upgrade locally.'
Invoke-Lifecycle $display 'Update'
Assert-True ($runtime.SnapshotRequests -eq 1) `
    'Completion checks must be rate limited.'
$snapshot.activeTask = $null
$bag.CurrentSnapshot.InventoryRevision = 22
$stone = [Game.Inventory.Model.V2.InventoryStackState]::new()
$stone.ItemId = 'stone'
$stone.Count = 3
$bag.CurrentSnapshot.Stacks.Add($stone)
$runtime.NotifySnapshot()
Assert-True ($start.interactable -and
    $materials.text.Contains('3/2') -and
    $fill.fillAmount -eq 0 -and
    $materialsScroll.verticalNormalizedPosition -eq 1) `
    'A cleared task with owned recipe materials allows starting the selected technique.'
$start.onClick.Invoke()
Assert-True ($runtime.StartedTechniqueId -eq 'alpha' -and
    -not $start.interactable -and $learned.level -eq 0) `
    'Click must submit the selected technique exactly once without local leveling.'
$start.onClick.Invoke()
Assert-True ($runtime.StartedTechniqueId -eq 'alpha') `
    'An unresolved start must not issue another write.'
$snapshot.activeTask = $task
$runtime.NotifySnapshot()
$startResult = [Game.Cultivation.Protocol.CultivationWriteResultDto]::new()
$startResult.success = $true
$runtime.NotifyStartResult($startResult)
Assert-True (-not $start.interactable -and $progress.text -match '^\d+:\d{2}:\d{2}$' -and
    $learned.level -eq 0) `
    'Confirmed start displays the server task and still keeps the current level.'
$snapshot.activeTask = $null
$learned.level = 1
$runtime.LastSnapshotServerTime = 1003000
$runtime.NotifySnapshot()
Assert-True ($display.VisibleCount -eq 2 -and [string]::IsNullOrEmpty($progress.text) -and
    $fill.fillAmount -eq 0 -and $content.children[0].owner.TechniqueId -eq 'alpha') `
    'An authoritative null task must stop and clear the prior task progress.'
$technique.name = '新玄青诀'
$runtime.NotifyCatalog()
Assert-True ($name.text -eq '新玄青诀') `
    'A refreshed catalog must update selected names without inventing a new technique.'
$items.items = @()
$runtime.NotifyItems()
Assert-True (-not $icon.enabled -and $null -eq $icon.sprite) `
    'No matching book means no guessed icon.'
$snapshot.techniques = @($otherLearned, $learned)
$snapshot.activeTask = $task
$runtime.NotifySnapshot()
Assert-Selected $content 'alpha'
Invoke-Lifecycle $display 'OnDisable'
$scroll.verticalNormalizedPosition = 0
Invoke-Lifecycle $display 'OnEnable'
Assert-Selected $content 'beta'
Assert-True ($name.text -eq '另一门功法' -and $scroll.verticalNormalizedPosition -eq 1 -and
    [string]::IsNullOrEmpty($progress.text)) `
    'Reopening must show the first row and its details, even if another technique is active.'
$snapshot.activeTask = $null
$snapshot.techniques = @()
$runtime.NotifySnapshot()
Assert-True ($display.VisibleCount -eq 0 -and $name.text -eq '--' -and
    $details.text -eq '总悟性 15' -and $fill.fillAmount -eq 0) `
    'An empty learned list remains empty while retaining authoritative comprehension.'
$runtime.IsConfigurationReady = $false
Invoke-Lifecycle $display 'Update'
Assert-True ($display.VisibleCount -eq 0 -and $name.text -eq '--') `
    'An unsynced configuration must not show mixed-version technique details.'
$network.Disconnect()
Invoke-Lifecycle $display 'Update'
Assert-True ($display.VisibleCount -eq 0 -and $fill.fillAmount -eq 0) `
    'Disconnect must not retain a learned row or task progress.'
Invoke-Lifecycle $display 'OnDisable'
[Game.Cultivation.Runtime.CultivationSessionRuntime]::Instance = $null
Write-Output 'PASS: comprehension row selection, details, task progress, session guard and scene wiring.'
