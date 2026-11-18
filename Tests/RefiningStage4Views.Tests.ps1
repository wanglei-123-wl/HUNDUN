$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$paths = @(
    'Assets/Scripts/Cultivation/Protocol/RefiningMessages.cs'
    'Assets/Scripts/Cultivation/Protocol/RefiningPreviewCalculator.cs'
    'Assets/Scripts/Cultivation/Protocol/RefiningTaskClock.cs'
    'Assets/Scripts/Cultivation/Protocol/RefiningBreakthroughConditions.cs'
    'Assets/Scripts/Cultivation/Presentation/RefiningTaskPresenter.cs'
    'Assets/Scripts/Cultivation/Presentation/CultivationProgressDisplay.cs'
    'Assets/Scripts/Cultivation/Presentation/CultivationProgressPresenter.cs'
)
$source = ($paths | ForEach-Object {
    (Get-Content -LiteralPath (Join-Path $root $_) -Raw) -replace
        '(?m)^using [^;]+;\r?\n', ''
}) -join "`n"
$stubs = @'
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Game.Cultivation.Protocol;
using Game.Cultivation.Runtime;
using Game.Inventory;
using Game.Inventory.Model.V2;
using Game.Inventory.Protocol.V2;
using Game.Items;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
public class NetMessageBase { public string msgId; public string type; }
public class ProgressionSnapshotResult {
    public bool success;
    public long characterDbId;
    public string jingjieId;
    public int jingjieStage;
    public long jingjieExp;
    public long totalJingjieExp;
    public bool hasNextJingjie;
    public string nextJingjieId;
    public int nextJingjieStage;
    public long requiredJingjieExp;
}
public class GameSession {
    public bool IsWorldReady = true;
    public long LocalCharacterDbId = 42;
    public ProgressionSnapshotResult LocalProgressionSnapshot;
    public int ProgressionRequests;
    public event Action<ProgressionSnapshotResult> LocalProgressionSnapshotChanged;
    public event Action<long,long> LocalWorldEntered;
    public void RequestProgressionSnapshot() { ProgressionRequests++; }
    public void SetProgression(ProgressionSnapshotResult result) {
        LocalProgressionSnapshot = result;
        LocalProgressionSnapshotChanged?.Invoke(result);
    }
    public void EnterWorld(long id) {
        LocalCharacterDbId = id;
        LocalProgressionSnapshot = null;
        LocalWorldEntered?.Invoke(1, id);
    }
}
public class NetworkManager {
    public static NetworkManager Instance = new();
    public event Action<string> OnDisconnected;
    public void Disconnect() { OnDisconnected?.Invoke("test"); }
}
namespace Game.Inventory.Protocol.V2 {
    public class InventoryV2StackChangeDto { public int newCount; }
}
namespace Game.Inventory.Model.V2 {
    public class InventoryOwnershipSnapshot {
        public long CharacterDbId;
        public readonly Dictionary<string,long> Counts = new();
        public bool TryGetItemCount(string id, out long count) =>
            Counts.TryGetValue(id, out count);
    }
}
namespace Game.Items {
    public class ItemDefinition {
        public string DisplayName;
        public Sprite Icon;
    }
    public class ItemDefinitionTable {
        public readonly Dictionary<string,ItemDefinition> Items = new();
        public bool TryGet(string id, out ItemDefinition definition) =>
            Items.TryGetValue(id, out definition);
    }
}
namespace Game.Inventory {
    public class InventoryModuleRuntime {
        public InventoryOwnershipSnapshot CurrentSnapshot;
        public ItemDefinitionTable ItemDefinitions = new();
    }
}
namespace Game.Cultivation.Runtime {
    public class RefiningSessionRuntime {
        public static RefiningSessionRuntime Instance;
        public RefiningResultDto Catalog;
        public RefiningResultDto LastSnapshotResult;
        public int SnapshotRequests;
        public event Action<RefiningResultDto> SnapshotChanged;
        public event Action<RefiningResultDto> CatalogChanged;
        public bool RequestSnapshot() { SnapshotRequests++; return true; }
        public void PublishSnapshot(RefiningResultDto result) {
            LastSnapshotResult = result;
            SnapshotChanged?.Invoke(result);
        }
        public void PublishCatalog(RefiningResultDto result) {
            Catalog = result;
            CatalogChanged?.Invoke(result);
        }
    }
}
namespace Game.Cultivation.Presentation {
    public class CultivationMaterialDropTarget {
        public bool Selected;
        public bool TryGetSelection(out long id, out int count, out long revision) {
            id = 1; count = 1; revision = 1;
            return Selected;
        }
    }
}
namespace TMPro {
    public enum TextAlignmentOptions { TopLeft }
    public enum TextOverflowModes { Overflow }
    public class TMP_Text {
        public GameObject gameObject = new();
        public Transform transform => gameObject.transform;
        public RectTransform rectTransform => (RectTransform)gameObject.transform;
        public string text;
        public bool raycastTarget;
        public bool enableAutoSizing;
        public bool enableWordWrapping;
        public float fontSize;
        public float fontSizeMin, fontSizeMax;
        public TextAlignmentOptions alignment;
        public TextOverflowModes overflowMode;
        public Vector2 GetPreferredValues(string value, float width, float height) =>
            new Vector2(width, 14 * (value.Split('\n').Length));
    }
}
namespace UnityEngine {
    [AttributeUsage(AttributeTargets.Class)]
    public class DisallowMultipleComponent : Attribute {}
    [AttributeUsage(AttributeTargets.Field)]
    public class SerializeField : Attribute {}
    public struct Vector2 {
        public float x; public float y;
        public Vector2(float a, float b) { x = a; y = b; }
    }
    public struct Color { public static Color clear = new(); }
    public class Sprite {}
    public class Transform {
        public Transform parent;
        public void SetParent(Transform value, bool stayWorld) { parent = value; }
    }
    public class RectTransform : Transform {
        public enum Axis { Vertical }
        public Vector2 anchorMin, anchorMax, pivot, anchoredPosition, sizeDelta;
        public void SetSizeWithCurrentAnchors(Axis axis, float value) {
            sizeDelta.y = value;
        }
    }
    public class GameObject {
        private readonly Dictionary<Type,object> components = new();
        public string name;
        public bool activeSelf = true;
        public Transform transform = new RectTransform();
        public void SetActive(bool active) { activeSelf = active; }
        public GameObject(params object[] args) {
            foreach (object arg in args) {
                if (arg is not Type type) continue;
                if (type == typeof(RectTransform)) components[type] = transform;
                if (type == typeof(Image)) components[type] = new Image();
                if (type == typeof(RectMask2D)) components[type] = new RectMask2D();
                if (type == typeof(ScrollRect)) components[type] = new ScrollRect {
                    gameObject = this
                };
            }
            if (args.Length > 0 && args[0] is string label) name = label;
        }
        public T GetComponent<T>() where T : class {
            return components.TryGetValue(typeof(T), out object value)
                ? value as T : null;
        }
    }
    public class MonoBehaviour {
        public GameObject gameObject = new();
        public Transform transform => gameObject.transform;
        protected static T FindObjectOfType<T>() where T : class => null;
        protected static T Instantiate<T>(T original, Transform parent)
            where T : class, new() {
            var copy = new T();
            if (copy is TMP_Text text) text.transform.SetParent(parent, false);
            return copy;
        }
        protected static void Destroy(GameObject item) {}
    }
    public static class Mathf {
        public static float Max(float left, float right) => Math.Max(left, right);
        public static float Clamp01(float value) =>
            Math.Max(0f, Math.Min(1f, value));
    }
    public static class Time {
        public static double realtimeSinceStartupAsDouble;
        public static float unscaledTime;
    }
}
namespace UnityEngine.UI {
    public class Image {
        public GameObject gameObject = new();
        public float fillAmount;
        public Color color;
        public bool raycastTarget;
        public Sprite sprite;
        public bool preserveAspect;
    }
    public class RectMask2D {}
    public class ScrollRect {
        public enum MovementType { Clamped }
        public GameObject gameObject;
        public RectTransform viewport, content;
        public bool horizontal, vertical;
        public float scrollSensitivity, verticalNormalizedPosition;
        public MovementType movementType;
    }
}
'@
Add-Type -TypeDefinition ($stubs + "`n" + $source) -IgnoreWarnings | Out-Null

function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
}
function Invoke-Lifecycle($target, [string]$method) {
    $target.GetType().GetMethod($method,
        [System.Reflection.BindingFlags]'Instance,NonPublic'
    ).Invoke($target, @()) | Out-Null
}
function Set-Private($target, [string]$name, $value) {
    $target.GetType().GetField($name,
        [System.Reflection.BindingFlags]'Instance,NonPublic'
    ).SetValue($target, $value)
}
function Get-Private($target, [string]$name) {
    return $target.GetType().GetField($name,
        [System.Reflection.BindingFlags]'Instance,NonPublic'
    ).GetValue($target)
}

$session = [GameSession]::new()
$runtime = [Game.Cultivation.Runtime.RefiningSessionRuntime]::new()
[Game.Cultivation.Runtime.RefiningSessionRuntime]::Instance = $runtime
$taskPresenter = [Game.Cultivation.Presentation.RefiningTaskPresenter]::new()
$timer = [TMPro.TMP_Text]::new()
$reward = [TMPro.TMP_Text]::new()
$taskFill = [UnityEngine.UI.Image]::new()
$icon = [UnityEngine.UI.Image]::new()
$taskInventory = [Game.Inventory.InventoryModuleRuntime]::new()
$taskDefinition = [Game.Items.ItemDefinition]::new()
$taskDefinition.Icon = [UnityEngine.Sprite]::new()
$taskInventory.ItemDefinitions.Items['ling_shi'] = $taskDefinition
$dropTarget = [Game.Cultivation.Presentation.CultivationMaterialDropTarget]::new()
Set-Private $taskPresenter 'session' $session
Set-Private $taskPresenter 'countdownText' $timer
Set-Private $taskPresenter 'cultivationText' $reward
Set-Private $taskPresenter 'taskFill' $taskFill
Set-Private $taskPresenter 'materialIcon' $icon
Set-Private $taskPresenter 'inventory' $taskInventory
Set-Private $taskPresenter 'materialDropTarget' $dropTarget
[UnityEngine.Time]::realtimeSinceStartupAsDouble = 10
Invoke-Lifecycle $taskPresenter 'OnEnable'
Assert-True ($timer.text -eq '00:00:00' -and $taskFill.fillAmount -eq 0) `
    'Idle task display starts empty.'
Assert-True ($timer.enableAutoSizing -and
    -not $timer.enableWordWrapping -and
    $reward.enableAutoSizing -and -not $reward.enableWordWrapping) `
    'Long task values must remain on one line and size to their bounds.'
$active = [Game.Cultivation.Protocol.RefiningTaskDto]::new()
$active.taskId = 'batch-1'
$active.itemId = 'ling_shi'
$active.totalCultivation = 50
$active.startedAtUnixMilliseconds = 1790500000000
$active.endsAtUnixMilliseconds = 1790500300000
$state = [Game.Cultivation.Protocol.RefiningSnapshotDto]::new()
$state.characterDbId = 42
$state.revision = 1
$state.cultivation = 80
$state.activeTask = $active
$result = [Game.Cultivation.Protocol.RefiningResultDto]::new()
$result.serverTime = 1790500000000
$result.snapshot = $state
$runtime.PublishSnapshot($result)
Assert-True ($timer.text -eq '00:05:00' -and $taskFill.fillAmount -eq 0 -and
    $reward.text -eq '预计 +50' -and
    [object]::ReferenceEquals($icon.sprite, $taskDefinition.Icon) -and
    $icon.preserveAspect -and $icon.gameObject.activeSelf -and
    $state.cultivation -eq 80) `
    'An active task displays server time, item and reward without an early grant.'
[UnityEngine.Time]::realtimeSinceStartupAsDouble = 70
Invoke-Lifecycle $taskPresenter 'Update'
Assert-True ($timer.text -eq '00:04:00' -and
    [math]::Abs($taskFill.fillAmount - 0.2) -lt 0.0001) `
    'Countdown and fill advance together using elapsed monotonic time.'
[UnityEngine.Time]::realtimeSinceStartupAsDouble = 310
[UnityEngine.Time]::unscaledTime = 100
Invoke-Lifecycle $taskPresenter 'Update'
Invoke-Lifecycle $taskPresenter 'Update'
Assert-True ($timer.text -eq '00:00:00' -and $taskFill.fillAmount -eq 1 -and
    $runtime.SnapshotRequests -eq 1 -and $state.cultivation -eq 80) `
    'Completion only queries the server at a limited rate without granting cultivation.'
$state.activeTask = $null
$state.revision = 2
$state.cultivation = 130
$runtime.PublishSnapshot($result)
Assert-True ($taskFill.fillAmount -eq 0 -and $timer.text -eq '00:00:00' -and
    -not $icon.gameObject.activeSelf -and $reward.text -eq '--') `
    'Confirmed completion clears the task display.'
$dropTarget.Selected = $true
$timer.text = '00:02:00'
$reward.text = '预计 +20'
$icon.gameObject.SetActive($true)
$runtime.PublishSnapshot($result)
Assert-True ($timer.text -eq '00:02:00' -and $reward.text -eq '预计 +20' -and
    $icon.gameObject.activeSelf) `
    'An idle snapshot must not erase an unconfirmed local selection preview.'
$dropTarget.Selected = $false
$state.activeTask = $active
$runtime.PublishSnapshot($result)
Invoke-Lifecycle $taskPresenter 'OnDisable'
Assert-True (-not $icon.gameObject.activeSelf -and $reward.text -eq '--') `
    'Closing during a task must not leave a stale task-owned icon.'
$state.activeTask = $null
Invoke-Lifecycle $taskPresenter 'OnEnable'
Assert-True (-not $icon.gameObject.activeSelf -and $taskFill.fillAmount -eq 0) `
    'Reopening after an offline completion must not restore the finished icon.'
Invoke-Lifecycle $taskPresenter 'OnDisable'

$inventory = [Game.Inventory.InventoryModuleRuntime]::new()
$bag = [Game.Inventory.Model.V2.InventoryOwnershipSnapshot]::new()
$bag.CharacterDbId = 42
$bag.Counts['tupo_dan'] = 1
$inventory.CurrentSnapshot = $bag
$definition = [Game.Items.ItemDefinition]::new()
$definition.DisplayName = '突破丹'
$inventory.ItemDefinitions.Items['tupo_dan'] = $definition
$right = [Game.Cultivation.Presentation.CultivationProgressPresenter]::new()
$rightText = [TMPro.TMP_Text]::new()
$rightFill = [UnityEngine.UI.Image]::new()
$nextRealm = [TMPro.TMP_Text]::new()
$nextRealm.rectTransform.anchoredPosition = [UnityEngine.Vector2]::new(134, -18)
Set-Private $right 'session' $session
Set-Private $right 'inventory' $inventory
Set-Private $right 'progressText' $rightText
Set-Private $right 'cultivationFill' $rightFill
Set-Private $right 'nextRealmText' $nextRealm
Invoke-Lifecycle $right 'OnEnable'
Assert-True ($rightText.text -eq '--/--' -and $rightFill.fillAmount -eq 0) `
    'The right side cannot invent cultivation or a requirement without a snapshot.'
Assert-True ($rightText.enableAutoSizing -and
    -not $rightText.enableWordWrapping) `
    'Long cultivation values must size within the existing numeric text box.'
$catalog = [Game.Cultivation.Protocol.RefiningResultDto]::new()
$catalog.success = $true
$path = [Game.Cultivation.Protocol.RefiningBreakthroughDto]::new()
$path.fromJingjieId = 'lianqi'
$path.fromJingjieStage = 1
$path.toJingjieId = 'lianqi'
$path.toJingjieStage = 2
$path.requiredCultivation = 100
$material = [Game.Cultivation.Protocol.RefiningMaterialDto]::new()
$material.itemId = 'tupo_dan'
$material.count = 2
$path.materials = @($material)
$catalog.breakthroughs = @($path)
$runtime.PublishCatalog($catalog)
$progression = [ProgressionSnapshotResult]::new()
$progression.success = $true
$progression.characterDbId = 42
$progression.jingjieId = 'lianqi'
$progression.jingjieStage = 1
$progression.jingjieExp = 80
$progression.hasNextJingjie = $true
$progression.nextJingjieId = 'lianqi'
$progression.nextJingjieStage = 2
$progression.requiredJingjieExp = 100
$session.SetProgression($progression)
$materialsText = Get-Private $right 'materialText'
Assert-True ($rightText.text -eq '80/100' -and
    [math]::Abs($rightFill.fillAmount - 0.8) -lt 0.0001 -and
    $materialsText.text -eq '突破丹 1/2') `
    'Progress uses the snapshot requirement; matching catalog supplies materials.'
$path.requiredCultivation = 50
$runtime.PublishCatalog($catalog)
Assert-True ($rightText.text -eq '80/100' -and
    [math]::Abs($rightFill.fillAmount - 0.8) -lt 0.0001) `
    'A catalog requirement must not replace the snapshot next-breakthrough cost.'
$path.requiredCultivation = 100
$path.toJingjieStage = 3
$runtime.PublishCatalog($catalog)
Assert-True ($rightText.text -eq '80/100' -and
    $materialsText.text -eq '突破材料 --') `
    'Materials for a different catalog target cannot appear under the server next realm.'
$path.toJingjieStage = 2
$runtime.PublishCatalog($catalog)
Assert-True ($materialsText.text -eq '突破丹 1/2') `
    'Matching catalog materials return after the target is corrected.'
$path.materials = @()
$runtime.PublishCatalog($catalog)
Assert-True ($materialsText.text -eq '' -and $rightText.text -eq '80/100' -and
    [math]::Abs($rightFill.fillAmount - 0.8) -lt 0.0001) `
    'An empty material list must clear its label and preserve cultivation progress.'
$path.materials = @($material)
$runtime.PublishCatalog($catalog)
Assert-True ($materialsText.text -eq '突破丹 1/2') `
    'Materials must display again after switching away from a material-free path.'
$progression.jingjieExp = 130
$session.SetProgression($progression)
$bag.Counts['tupo_dan'] = 2
Invoke-Lifecycle $right 'Update'
Assert-True ($rightText.text -eq '130/100' -and $rightFill.fillAmount -eq 1 -and
    $materialsText.text -eq '突破丹 2/2') `
    'Refreshed usable cultivation and owned materials update the separate right display.'
$additional = [Game.Cultivation.Protocol.RefiningMaterialDto]::new()
$additional.itemId = 'ling_shi'
$additional.count = 3
$third = [Game.Cultivation.Protocol.RefiningMaterialDto]::new()
$third.itemId = 'material_3'
$third.count = 1
$path.materials = @($material, $additional, $third)
$runtime.PublishCatalog($catalog)
$scroll = Get-Private $right 'materialScroll'
Assert-True ($materialsText.text -match '突破丹 2/2' -and
    $materialsText.text -match 'ling_shi 0/3' -and
    $materialsText.text -match 'material_3 0/1' -and
    $scroll.content.sizeDelta.y -gt $scroll.viewport.sizeDelta.y -and
    $scroll.vertical -and -not $scroll.horizontal) `
    'Multiple materials must remain in a vertically scrollable, bounded list.'
$catalog.breakthroughs = @()
$runtime.PublishCatalog($catalog)
Assert-True ($rightText.text -eq '130/100' -and
    $rightFill.fillAmount -eq 1 -and
    $materialsText.text -eq '突破材料 --') `
    'A missing catalog must not erase the authoritative snapshot requirement.'
$progression.hasNextJingjie = $false
$progression.nextJingjieId = ''
$progression.nextJingjieStage = 0
$session.SetProgression($progression)
Assert-True ($rightText.text -eq '130/--' -and
    $rightFill.fillAmount -eq 0 -and
    $materialsText.text -eq '突破材料 --') `
    'Without a next path, no requirement or materials should be shown.'
$session.EnterWorld(43)
Assert-True ($rightText.text -eq '--/--' -and $rightFill.fillAmount -eq 0) `
    'Another character cannot inherit the prior available cultivation.'
Invoke-Lifecycle $right 'OnDisable'
Write-Output 'PASS: task display, deadline query, cultivation and material presentation.'
