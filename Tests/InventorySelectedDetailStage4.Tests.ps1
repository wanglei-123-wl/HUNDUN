$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$scene = Get-Content -LiteralPath (Join-Path $root 'Assets/Scenes/WorldShell.unity') -Raw
$view = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Inventory/Presentation/V2/InventorySelectedItemDetailPanel.cs') -Raw
$sell = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/PlayerTrade/Presentation/PlayerTradeSellPanelController.cs') -Raw
$bridge = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/PlayerTrade/Presentation/PlayerTradeBagSellBridge.cs') -Raw

function Assert-True([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}
function Block([int]$type, [string]$id) {
    $pattern = "(?ms)^--- !u!$type &$id`r?`n(.*?)(?=^--- !u!|\z)"
    $matches = [regex]::Matches($scene, $pattern)
    Assert-True ($matches.Count -eq 1) "Scene block $id must exist exactly once."
    return $matches[0].Value
}

$bagComponent = Block 114 '1536511460'
$bridgeComponent = Block 114 '1966549458'
$marketOwner = Block 1 '1966549451'
$sellButton = Block 114 '1433993389'
$listingIcon = Block 1 '1470087495'
$ownedCount = Block 1 '752100919'
$sellableCount = Block 1 '1784696068'
Assert-True ($bagComponent.Contains('sellButton: {fileID: 1433993389}') -and
    $sellButton.Contains('guid: 4e29b1a8efbd4b44bb3f3716e73f07ff') -and
    $marketOwner.Contains('component: {fileID: 1966549458}') -and
    $bridgeComponent.Contains('guid: c8ec4b83b9604a339bb9eb1907c7837d') -and
    $bridgeComponent.Contains('bagDetail: {fileID: 1536511460}') -and
    $bridgeComponent.Contains('market: {fileID: 916274839204}') -and
    $bridgeComponent.Contains('sellPanel: {fileID: 138943774}')) `
    'WorldShell must bind the detail sell button to the active market and sell panel.'
Assert-True ($listingIcon.Contains('m_Name: "\u6302\u5355\u9762\u677F\u56FE\u6807"') -and
    $ownedCount.Contains('m_Name: "\u62E5\u6709\u6570\u91CF"') -and
    $sellableCount.Contains('m_Name: "\u53EF\u552E"') -and
    (Block 224 '1470087496').Contains('m_Father: {fileID: 2096536115}') -and
    (Block 224 '752100920').Contains('m_Father: {fileID: 2096536115}') -and
    (Block 224 '1784696069').Contains('m_Father: {fileID: 2096536115}')) `
    'The listing icon and both quantity labels must be inside the existing sell panel.'
Assert-True ($view.Contains('SellRequested?.Invoke(identity);') -and
    $view.Contains('sellButton.onClick.RemoveListener(HandleSellClicked);') -and
    $sell.Contains('TrySelectIdentity(source.DragIdentity);') -and
    $sell.Contains('public bool TrySelectIdentity(string identity)') -and
    $sell.Contains('TryBindIdentity(snapshot, identity)') -and
    $sell.Contains('public bool CanSellIdentity(string identity)') -and
    $sell.Contains('TryFindSellableItem(') -and
    $sell.Contains('stack.Count > 0 && !stack.IsBound') -and
    $sell.Contains('equipment.SlotCode == 0 && !equipment.IsBound') -and
    $sell.Contains('ApplySelection(definition, stack.Count, stack.Count);') -and
    $sell.Contains('ApplySelection(equipmentDefinition, ownedCount, sellableCount);') -and
    $sell.Contains('SetText(ownedCountText, ownedCount.ToString(') -and
    $sell.Contains('SetText(sellableCountText, sellableCount.ToString(') -and
    $sell.Contains('pendingCreateRequest != null')) `
    'Button and drag entry must share availability, icon, and quantity binding.'

$stubs = @'
using System;
namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Class)] public sealed class DisallowMultipleComponent : Attribute {}
    [AttributeUsage(AttributeTargets.Field)] public sealed class SerializeField : Attribute {}
    public class MonoBehaviour {}
}
namespace Game.Inventory.Presentation.V2
{
    public class InventorySelectedItemDetailPanel
    {
        public event Action<string> SellRequested;
        public void ClickSell(string identity) { SellRequested?.Invoke(identity); }
    }
}
namespace Game.PlayerTrade.Presentation
{
    public class PlayerTradeMarketToggleController
    {
        public bool IsVisible { get; private set; }
        public int ShowCount;
        public void Show() { IsVisible = true; ShowCount++; }
        public void Hide() { IsVisible = false; }
    }
    public class PlayerTradeSellPanelController
    {
        public bool IsOpen;
        public string SelectedIdentity { get; private set; }
        public bool HasSelectedItem => SelectedIdentity != null;
        public PlayerTradeMarketToggleController Market;
        public int OpenCount;
        public int SelectCount;
        public bool CanSellIdentity(string identity) =>
            identity == "s:123" || identity == "e:456";
        public void OpenPanel()
        {
            if (!Market.IsVisible) throw new Exception("Market must be open first.");
            IsOpen = true;
            OpenCount++;
        }
        public bool TrySelectIdentity(string identity)
        {
            if (!IsOpen || !Market.IsVisible) throw new Exception("Wrong call order.");
            SelectedIdentity = identity;
            SelectCount++;
            return true;
        }
        public void ClearSelection() { SelectedIdentity = null; }
    }
}
namespace Game.UI.Notice
{
    public static class GameNoticeService
    {
        public static string LastMessage;
        public static bool Show(string message) { LastMessage = message; return true; }
    }
}
public static class BagSellBridgeCheck
{
    static void Set(object owner, string name, object value)
    {
        owner.GetType().GetField(name,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).SetValue(owner, value);
    }
    static void Call(object owner, string name)
    {
        owner.GetType().GetMethod(name,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(owner, null);
    }
    public static void Run()
    {
        var detail = new Game.Inventory.Presentation.V2.InventorySelectedItemDetailPanel();
        var market = new Game.PlayerTrade.Presentation.PlayerTradeMarketToggleController();
        var sell = new Game.PlayerTrade.Presentation.PlayerTradeSellPanelController { Market = market };
        var bridge = new Game.PlayerTrade.Presentation.PlayerTradeBagSellBridge();
        Set(bridge, "bagDetail", detail);
        Set(bridge, "market", market);
        Set(bridge, "sellPanel", sell);
        Call(bridge, "OnEnable");
        detail.ClickSell(null);
        if (market.IsVisible || sell.IsOpen) throw new Exception("Empty selection must not open the market.");
        detail.ClickSell("s:bound");
        if (market.IsVisible || sell.IsOpen || Game.UI.Notice.GameNoticeService.LastMessage != "该物品不可出售")
            throw new Exception("Unsellable item must show notice without opening market.");
        Game.UI.Notice.GameNoticeService.LastMessage = null;
        detail.ClickSell("s:123");
        if (!market.IsVisible || !sell.IsOpen || sell.SelectedIdentity != "s:123"
            || market.ShowCount != 1 || sell.OpenCount != 1 || sell.SelectCount != 1)
            throw new Exception("Click must open market and sell panel, then bind identity.");
        detail.ClickSell("s:123");
        if (market.ShowCount != 1 || sell.OpenCount != 1 || sell.SelectCount != 1)
            throw new Exception("Already active listing must not reopen or rebind.");
        market.Hide();
        detail.ClickSell("s:123");
        if (!market.IsVisible || market.ShowCount != 2 || sell.OpenCount != 1
            || sell.SelectCount != 1)
            throw new Exception("Hidden market must reopen even when listing is already active.");
        detail.ClickSell("e:456");
        if (sell.SelectedIdentity != "s:123" || sell.SelectCount != 1
            || Game.UI.Notice.GameNoticeService.LastMessage != "请先取消当前挂单物品选择")
            throw new Exception("Different item must not overwrite the existing listing.");
        sell.ClearSelection();
        detail.ClickSell("e:456");
        if (sell.SelectedIdentity != "e:456" || sell.OpenCount != 1
            || sell.SelectCount != 2)
            throw new Exception("Already open listing with empty slot must bind selected item.");
        detail.ClickSell("s:bound");
        if (sell.SelectedIdentity != "e:456" || market.ShowCount != 2
            || sell.SelectCount != 2 || Game.UI.Notice.GameNoticeService.LastMessage != "该物品不可出售")
            throw new Exception("Unsellable item must preserve existing listing and show notice.");
        sell.ClearSelection();
        sell.IsOpen = false;
        detail.ClickSell("s:123");
        if (market.ShowCount != 2 || sell.OpenCount != 2
            || sell.SelectedIdentity != "s:123")
            throw new Exception("Visible market must open listing without reopening market.");
        Call(bridge, "OnDisable");
        detail.ClickSell("e:456");
        if (sell.SelectedIdentity != "s:123")
            throw new Exception("Disabled bridge must remove its subscription.");
    }
}
'@
$bridgeBody = $bridge -replace '(?m)^using .*;\r?\n', ''
$stubBodies = $stubs -replace '(?m)^using System;\r?\n', ''
$source = "using System;`nusing Game.Inventory.Presentation.V2;`nusing Game.UI.Notice;`nusing UnityEngine;`n" +
    ($stubBodies -replace '(?m)^namespace Game.PlayerTrade.Presentation',
        ($bridgeBody + "`nnamespace Game.PlayerTrade.Presentation"))
Add-Type -TypeDefinition $source -IgnoreWarnings -WarningAction SilentlyContinue
[BagSellBridgeCheck]::Run()
Write-Output 'PASS: sell eligibility notice, market/listing navigation, selection and repeat-click behavior.'
