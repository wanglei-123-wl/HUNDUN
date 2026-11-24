$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$captureSource = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/CharacterPanel/Presentation/CharacterPanelPointerCapture.cs') -Raw
$guardSource = (Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Player/GameplayUiInputGuard.cs') -Raw) `
    -replace '(?m)^using [^\r\n]+;\r?\n', ''

$stubs = @'
namespace UnityEngine
{
    public class Transform
    {
        public Transform parent;
        public bool IsChildOf(Transform target)
        {
            for (Transform node = this; node != null; node = node.parent)
                if (node == target) return true;
            return false;
        }
    }
    public class RectTransform : Transform {}
    public class GameObject
    {
        public Transform transform = new Transform();
        public bool activeInHierarchy;
        public object component;
        public T GetComponent<T>() where T : class { return component as T; }
        public T GetComponentInParent<T>() where T : class { return component as T; }
    }
    public class Behaviour
    {
        public bool isActiveAndEnabled = true;
        public Transform transform;
        public T GetComponent<T>() where T : class { return transform as T; }
    }
    public struct Vector3
    {
        public float x, y, z;
    }
    public static class RectTransformUtility
    {
        public static bool RectangleContainsScreenPoint(RectTransform area, Vector3 point)
        {
            return false;
        }
    }
    public static class Input
    {
        public static Vector3 mousePosition;
        public static int held, pressed, released;
        public static bool GetMouseButton(int button) => (held & (1 << button)) != 0;
        public static bool GetMouseButtonDown(int button) => (pressed & (1 << button)) != 0;
        public static bool GetMouseButtonUp(int button) => (released & (1 << button)) != 0;
    }
    public static class Time
    {
        public static int frameCount;
    }
}
namespace UnityEngine.EventSystems
{
    public class BaseRaycaster {}
    public struct RaycastResult
    {
        public UnityEngine.GameObject gameObject;
        public BaseRaycaster module;
    }
    public class PointerEventData
    {
        public UnityEngine.Vector3 position;
        public PointerEventData(EventSystem owner) {}
        public void Reset() { position = new UnityEngine.Vector3(); }
    }
    public class EventSystem
    {
        public static EventSystem current;
        public UnityEngine.GameObject currentSelectedGameObject;
        public readonly List<RaycastResult> hits = new List<RaycastResult>();
        public void RaycastAll(PointerEventData data, List<RaycastResult> results)
        {
            results.AddRange(hits);
        }
    }
}
namespace UnityEngine.UI
{
    public class GraphicRaycaster : UnityEngine.EventSystems.BaseRaycaster {}
    public class InputField
    {
        public bool isFocused, isActiveAndEnabled = true;
    }
}
namespace TMPro
{
    public class TMP_InputField
    {
        public bool isFocused, isActiveAndEnabled = true;
    }
}
namespace Game.CharacterPanel.Presentation
{
    public static class CharacterPanelInputGuard
    {
        public static bool BlocksGameplayInput;
        public static bool PointerOnPanel;
    }
}
namespace Game.Chat.UI
{
    public static class WorldChatPanelController
    {
        public static bool IsChatInputFocused;
    }
    public static class PrivateChatPanelController
    {
        public static bool IsChatInputFocused;
    }
}
namespace Game.Friend.UI
{
    public static class FriendSearchPanelController
    {
        public static bool IsSearchInputFocused;
    }
}
namespace Game.Inventory
{
    public class InventoryPanelView : UnityEngine.Behaviour
    {
        public static InventoryPanelView ActivePanel;
    }
}
namespace Game.Inventory.Presentation.V2
{
    public static class InventoryDiscardConfirmationView
    {
        public static bool BlocksGameplayInput;
    }
}
public static class WorldEntryLoadingView
{
    public static object ActiveWorldEntry;
}
namespace Game.PlayerTrade.Presentation
{
    public class PlayerTradeMarketToggleController : UnityEngine.Behaviour
    {
        public static PlayerTradeMarketToggleController ActiveMarket;
        public bool IsVisible;
        public UnityEngine.Transform MarketRoot;
    }
}
'@
Add-Type -TypeDefinition (
    "using System;`nusing System.Collections.Generic;`nusing UnityEngine;`n" +
    "using UnityEngine.EventSystems;`nusing UnityEngine.UI;`nusing TMPro;`n" +
    "using Game.CharacterPanel.Presentation;`n" +
    "using Game.Chat.UI;`nusing Game.Friend.UI;`nusing Game.Inventory;`n" +
    "using Game.Inventory.Presentation.V2;`nusing Game.PlayerTrade.Presentation;`n" +
    $stubs + $captureSource + $guardSource) -IgnoreWarnings | Out-Null

function Assert-Blocked([bool]$expected, [string]$message) {
    [UnityEngine.Time]::frameCount++
    $actual = [GameplayUiInputGuard]::BlocksGameplayInput
    if ($actual -ne $expected) { throw $message }
    if ([GameplayUiInputGuard]::BlocksWorldActions -ne ($null -ne [WorldEntryLoadingView]::ActiveWorldEntry)) {
        throw 'Ordinary UI and text focus must not become world-action locks.'
    }
}

$eventSystem = [UnityEngine.EventSystems.EventSystem]::new()
[UnityEngine.EventSystems.EventSystem]::current = $eventSystem
Assert-Blocked $false 'World input must remain available outside UI.'
[WorldEntryLoadingView]::ActiveWorldEntry = [object]::new()
Assert-Blocked $true 'World entry loading must block gameplay input.'
if (-not [GameplayUiInputGuard]::BlocksWindowShortcuts) {
    throw 'World entry loading must also block window shortcuts.'
}
[WorldEntryLoadingView]::ActiveWorldEntry = $null

$dialog = [UnityEngine.GameObject]::new()
[GameplayUiInputGuard]::RegisterModal($dialog)
Assert-Blocked $false 'A hidden dialog must not block gameplay.'
$dialog.activeInHierarchy = $true
Assert-Blocked $false 'An ordinary dialog must allow world camera input outside its actual UI.'
$dialogHit = [UnityEngine.EventSystems.RaycastResult]::new()
$dialogHit.gameObject = [UnityEngine.GameObject]::new()
$dialogHit.gameObject.transform.parent = $dialog.transform
$dialogHit.module = [UnityEngine.UI.GraphicRaycaster]::new()
$eventSystem.hits.Add($dialogHit)
Assert-Blocked $true 'Hovering a real dialog graphic must consume pointer and camera input.'
[UnityEngine.Input]::held = 1
[UnityEngine.Input]::pressed = 1
Assert-Blocked $true 'Pressing a dialog must capture the pointer.'
$dialog.activeInHierarchy = $false
$eventSystem.hits.Clear()
[UnityEngine.Input]::pressed = 0
Assert-Blocked $true 'Closing a dialog during a press must retain the capture.'
[UnityEngine.Input]::held = 0
[UnityEngine.Input]::released = 1
Assert-Blocked $true 'Dialog release must remain blocked for that frame.'
[UnityEngine.Input]::released = 0
Assert-Blocked $false 'World input must resume after the dialog release.'
[GameplayUiInputGuard]::UnregisterModal($dialog)

$hardDialog = [UnityEngine.GameObject]::new()
[GameplayUiInputGuard]::RegisterModal($hardDialog, $true)
Assert-Blocked $false 'An inactive loading dialog must not lock the world.'
$hardDialog.activeInHierarchy = $true
[UnityEngine.Time]::frameCount++
if (-not [GameplayUiInputGuard]::BlocksWorldActions -or
    -not [GameplayUiInputGuard]::BlocksWorldPointerInput -or
    -not [GameplayUiInputGuard]::BlocksCameraInput -or
    -not [GameplayUiInputGuard]::BlocksWindowShortcuts) {
    throw 'Visible map loading must lock world actions, clicks, camera and shortcuts even without a raycaster.'
}
$hardDialog.activeInHierarchy = $false
Assert-Blocked $false 'Hiding a hard loading dialog must release the world lock.'
$hardDialog.activeInHierarchy = $true
[GameplayUiInputGuard]::UnregisterModal($hardDialog)
Assert-Blocked $false 'Unregistering loading must release both pointer and action registration.'

[Game.Chat.UI.WorldChatPanelController]::IsChatInputFocused = $true
Assert-Blocked $true 'Focused chat must block gameplay.'
if (-not [GameplayUiInputGuard]::IsEditingText) {
    throw 'Focused world chat must block panel shortcuts.'
}
[Game.Chat.UI.WorldChatPanelController]::IsChatInputFocused = $false
Assert-Blocked $true 'The chat submit/blur frame must still consume UI keyboard input.'
Assert-Blocked $false 'Gameplay must resume when chat loses focus.'

[Game.Chat.UI.PrivateChatPanelController]::IsChatInputFocused = $true
Assert-Blocked $true 'Focused private chat must block gameplay.'
if (-not [GameplayUiInputGuard]::IsEditingText) {
    throw 'Focused private chat must block panel shortcuts.'
}
[Game.Chat.UI.PrivateChatPanelController]::IsChatInputFocused = $false
Assert-Blocked $true 'The private chat blur frame must still consume UI keyboard input.'
Assert-Blocked $false 'Gameplay must resume when private chat loses focus.'

[Game.Friend.UI.FriendSearchPanelController]::IsSearchInputFocused = $true
Assert-Blocked $true 'Focused friend search must block gameplay.'
if (-not [GameplayUiInputGuard]::IsEditingText) {
    throw 'Focused friend search must block panel shortcuts.'
}
[Game.Friend.UI.FriendSearchPanelController]::IsSearchInputFocused = $false
Assert-Blocked $true 'The search submit frame must still consume UI keyboard input.'
Assert-Blocked $false 'Gameplay must resume when friend search loses focus.'

$selected = [UnityEngine.GameObject]::new()
$tmp = [TMPro.TMP_InputField]::new()
$tmp.isFocused = $true
$selected.component = $tmp
$eventSystem.currentSelectedGameObject = $selected
Assert-Blocked $true 'Any selected TMP input must block movement while focused.'
if (-not [GameplayUiInputGuard]::IsEditingText) {
    throw 'A focused TMP input must block panel shortcuts.'
}
$tmp.isFocused = $false
Assert-Blocked $true 'The input blur frame must remain consumed.'
Assert-Blocked $false 'A selected but unfocused TMP input must not block movement.'
if ([GameplayUiInputGuard]::IsEditingText) {
    throw 'An unfocused TMP input must allow panel shortcuts.'
}
$selected.component = [UnityEngine.UI.InputField]::new()
$selected.component.isFocused = $true
Assert-Blocked $true 'A focused legacy input must also block movement.'
if (-not [GameplayUiInputGuard]::IsEditingText) {
    throw 'A focused legacy input must block panel shortcuts.'
}
$selected.component.isActiveAndEnabled = $false
Assert-Blocked $true 'The final text-input frame must remain consumed.'
Assert-Blocked $false 'A disabled input must not keep blocking movement.'
$eventSystem.currentSelectedGameObject = $null

$uiRaycaster = [UnityEngine.UI.GraphicRaycaster]::new()
$uiHit = [UnityEngine.EventSystems.RaycastResult]::new()
$uiHit.gameObject = [UnityEngine.GameObject]::new()
$uiHit.module = $uiRaycaster
$worldHit = [UnityEngine.EventSystems.RaycastResult]::new()
$worldHit.gameObject = [UnityEngine.GameObject]::new()
$worldHit.module = [UnityEngine.EventSystems.BaseRaycaster]::new()
$eventSystem.hits.Add($worldHit)
Assert-Blocked $false 'A world raycast must not be mistaken for UI.'
$eventSystem.hits.Add($uiHit)
Assert-Blocked $true 'UI must block gameplay even after a non-UI raycast result.'
$eventSystem.hits.Clear()
Assert-Blocked $false 'World input must resume outside UI when no button is held.'
$eventSystem.hits.Add($uiHit)
[UnityEngine.Input]::held = 3
[UnityEngine.Input]::pressed = 3
Assert-Blocked $true 'Both mouse buttons pressed over UI must be captured.'
$eventSystem.hits.Clear()
[UnityEngine.Input]::pressed = 0
Assert-Blocked $true 'Dragging both buttons outside UI must stay captured.'
[UnityEngine.Input]::held = 2
[UnityEngine.Input]::released = 1
Assert-Blocked $true 'Releasing one button must retain the other capture.'
[UnityEngine.Input]::held = 0
[UnityEngine.Input]::released = 2
Assert-Blocked $true 'The last release frame must still block gameplay.'
[UnityEngine.Input]::released = 0
Assert-Blocked $false 'World input must resume after the last button is released.'
$eventSystem.hits.Add($uiHit)
Assert-Blocked $true 'Hovering over UI must block camera and world input.'
$eventSystem.hits.Clear()
Assert-Blocked $false 'Leaving UI without a held button must resume gameplay.'

$marketRoot = [UnityEngine.Transform]::new()
$market = [Game.PlayerTrade.Presentation.PlayerTradeMarketToggleController]::new()
$market.transform = $marketRoot
$market.MarketRoot = $marketRoot
$market.IsVisible = $true
[Game.PlayerTrade.Presentation.PlayerTradeMarketToggleController]::ActiveMarket = $market
$unrelated = [UnityEngine.GameObject]::new()
$hit = [UnityEngine.EventSystems.RaycastResult]::new()
$hit.gameObject = $unrelated
$hit.module = $uiRaycaster
$eventSystem.hits.Add($hit)
Assert-Blocked $true 'Other UI must block gameplay even while market is open.'
$eventSystem.hits.Clear()
Assert-Blocked $false 'World input remains available outside the open market.'

$marketChild = [UnityEngine.GameObject]::new()
$marketChild.transform.parent = $marketRoot
$hit.gameObject = $marketChild
$eventSystem.hits.Add($hit)
[UnityEngine.Input]::held = 1
[UnityEngine.Input]::pressed = 1
Assert-Blocked $true 'Pressing market UI must block gameplay.'
$eventSystem.hits.Clear()
[UnityEngine.Input]::pressed = 0
Assert-Blocked $true 'Dragging outside market must stay blocked.'
[UnityEngine.Input]::held = 0
[UnityEngine.Input]::released = 1
Assert-Blocked $true 'Release outside market must stay blocked for that frame.'
[UnityEngine.Input]::released = 0
Assert-Blocked $false 'Gameplay must resume after market release.'
$market.IsVisible = $false
$eventSystem.hits.Add($hit)
Assert-Blocked $true 'Unrelated visible UI still blocks while market is hidden.'
$eventSystem.hits.Clear()
Assert-Blocked $false 'Hidden market must not capture the pointer.'

$inventoryRoot = [UnityEngine.RectTransform]::new()
$inventory = [Game.Inventory.InventoryPanelView]::new()
$inventory.transform = $inventoryRoot
[Game.Inventory.InventoryPanelView]::ActivePanel = $inventory
$inventoryChild = [UnityEngine.GameObject]::new()
$inventoryChild.transform.parent = $inventoryRoot
$hit.gameObject = $inventoryChild
$eventSystem.hits.Add($hit)
[UnityEngine.Input]::held = 1
[UnityEngine.Input]::pressed = 1
Assert-Blocked $true 'Inventory child outside the root rectangle must capture the pointer.'
$eventSystem.hits.Clear()
[UnityEngine.Input]::pressed = 0
Assert-Blocked $true 'Dragging outside inventory must stay blocked.'
[UnityEngine.Input]::held = 0
[UnityEngine.Input]::released = 1
Assert-Blocked $true 'Inventory release must stay blocked for that frame.'
[UnityEngine.Input]::released = 0
Assert-Blocked $false 'Gameplay must resume after inventory release.'

$cameraSource = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Player/ThirdPersonCameraFollow.cs') -Raw
if ($cameraSource -notmatch 'leftHeld = !uiBlocked && Input.GetMouseButton\(0\)' -or
    $cameraSource -notmatch 'SetCursorCaptured\(rotatingCamera\)') {
    throw 'World camera drag must remain available when the UI guard is clear.'
}

foreach ($path in @(
    'Assets/Scripts/CharacterPanel/Presentation/CharacterPanelToggle.cs',
    'Assets/Scripts/PlayerTrade/Presentation/PlayerTradeMarketToggleController.cs',
    'Assets/Scripts/Inventory/Integration/InventoryModuleRuntime.cs'
)) {
    $source = Get-Content -LiteralPath (Join-Path $root $path) -Raw
    if ($source -notmatch 'Input.GetKeyDown\([^)]+\)\s*&&\s*!GameplayUiInputGuard.BlocksWindowShortcuts') {
        throw "$path must ignore toggle keys while loading or editing text."
    }
}

$scene = Get-Content -LiteralPath (Join-Path $root 'Assets/Scenes/WorldShell.unity') -Raw
foreach ($id in @('411326658', '1523190448', '1966549455')) {
    if ($scene -notmatch "(?s)--- !u!223 &$id`r?`n(?:(?!--- !u!).)*m_Enabled: 1(?:(?!--- !u!).)*m_RenderMode: 0") {
        throw "WorldShell canvas $id must support screen-space UI raycasts."
    }
}
if ($scene -notmatch '(?s)--- !u!1 &1523190445\r?\n(?:(?!--- !u!).)*m_IsActive: 0') {
    throw 'Market must remain hidden by its active state on startup.'
}

$eventSystem.hits.Clear()
$uiHit.gameObject.component = $null
$eventSystem.hits.Add($uiHit)
[UnityEngine.Input]::held = 3
[UnityEngine.Input]::pressed = 0
[UnityEngine.Input]::released = 0
[UnityEngine.Time]::frameCount++
if (-not [GameplayUiInputGuard]::BlocksCameraInput -or
    -not [GameplayUiInputGuard]::IsWorldMouseButtonHeld(0) -or
    -not [GameplayUiInputGuard]::IsWorldMouseButtonHeld(1)) {
    throw 'Hovering UI must freeze camera input while preserving pre-existing world mouse movement.'
}

$pendingText = [TMPro.TMP_InputField]::new()
$pendingText.isFocused = $false
$uiHit.gameObject.component = $pendingText
[UnityEngine.Input]::held = 1
[UnityEngine.Input]::pressed = 1
[UnityEngine.Time]::frameCount++
if (-not [GameplayUiInputGuard]::BlocksWorldKeyboardInput -or
    -not [GameplayUiInputGuard]::BlocksWindowShortcuts -or
    [GameplayUiInputGuard]::BlocksWorldActions) {
    throw 'The first keyboard consumer must capture a text-field click before camera sampling or TMP focus, without pausing world actions.'
}
Write-Output 'PASS: UI raycast, text focus, first-click capture, panel shortcuts and pointer ownership.'
