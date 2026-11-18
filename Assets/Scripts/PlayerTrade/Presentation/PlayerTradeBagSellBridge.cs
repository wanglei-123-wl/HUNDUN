using Game.Inventory.Presentation.V2;
using Game.UI.Notice;
using UnityEngine;

namespace Game.PlayerTrade.Presentation
{
    [DisallowMultipleComponent]
    public sealed class PlayerTradeBagSellBridge : MonoBehaviour
    {
        [SerializeField] private InventorySelectedItemDetailPanel bagDetail;
        [SerializeField] private PlayerTradeMarketToggleController market;
        [SerializeField] private PlayerTradeSellPanelController sellPanel;

        private void OnEnable()
        {
            if (bagDetail != null)
                bagDetail.SellRequested += HandleSellRequested;
        }

        private void OnDisable()
        {
            if (bagDetail != null)
                bagDetail.SellRequested -= HandleSellRequested;
        }

        private void HandleSellRequested(string identity)
        {
            if (string.IsNullOrWhiteSpace(identity) || market == null
                || sellPanel == null)
                return;

            if (!sellPanel.CanSellIdentity(identity))
            {
                GameNoticeService.Show("该物品不可出售");
                return;
            }

            if (sellPanel.IsOpen)
            {
                if (sellPanel.HasSelectedItem
                    && sellPanel.SelectedIdentity != identity)
                {
                    GameNoticeService.Show("请先取消当前挂单物品选择");
                    return;
                }
            }

            if (!market.IsVisible)
                market.Show();
            if (sellPanel.IsOpen && sellPanel.SelectedIdentity == identity)
                return;
            if (!sellPanel.IsOpen)
                sellPanel.OpenPanel();
            sellPanel.TrySelectIdentity(identity);
        }
    }
}
