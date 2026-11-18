using System;
using System.Globalization;
using Game.Items;
using Game.PlayerTrade.Protocol;
using Game.UI.ItemDetail;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.PlayerTrade.Presentation
{
    [DisallowMultipleComponent]
    public sealed class PlayerTradeClaimCardView : MonoBehaviour
    {
        [Header("显示")]
        [SerializeField] private Image itemIcon;
        [SerializeField] private TMP_Text itemNameText;
        [SerializeField] private TMP_Text quantityText;
        [SerializeField] private TMP_Text amountText;
        [SerializeField] private TMP_Text reasonText;
        [SerializeField] private TMP_Text statusText;

        [Header("交互")]
        [SerializeField] private Button claimButton;

        private ItemDetailHoverTrigger detailHoverTrigger;
        private PlayerTradeClaimDto boundClaim;
        private Action<PlayerTradeClaimCardView> claimRequested;

        public string ClaimId => boundClaim == null
            ? string.Empty : boundClaim.claimId;
        public PlayerTradeClaimDto Claim => boundClaim;

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnDestroy()
        {
            ClearDetailBinding();
            if (claimButton != null)
                claimButton.onClick.RemoveListener(HandleClaimClicked);
        }

        public void Bind(
            PlayerTradeClaimDto claim,
            ItemDefinitionTable itemDefinitions,
            Action<PlayerTradeClaimCardView> onClaimRequested)
        {
            boundClaim = claim;
            claimRequested = onClaimRequested;
            ResolveReferences();

            if (boundClaim == null)
            {
                ClearDetailBinding();
                gameObject.SetActive(false);
                return;
            }

            bool isCurrency = string.Equals(boundClaim.kind, "currency",
                StringComparison.OrdinalIgnoreCase);
            PlayerTradeAssetDto asset = boundClaim.asset;

            if (itemNameText != null)
            {
                itemNameText.text = isCurrency
                    ? GetCurrencyName(boundClaim.currencyId)
                    : GetItemName(asset, itemDefinitions);
            }

            if (quantityText != null)
            {
                quantityText.text = isCurrency
                    ? "数量：--"
                    : "数量：" + Mathf.Max(1, asset == null ? 1 : asset.quantity)
                        .ToString(CultureInfo.InvariantCulture);
            }

            if (amountText != null)
            {
                amountText.text = isCurrency
                    ? "可领取：" + Math.Max(0L, boundClaim.remainingAmount)
                        .ToString(CultureInfo.InvariantCulture)
                    : string.Empty;
            }

            if (reasonText != null)
                reasonText.text = FormatReason(boundClaim.reason);
            if (statusText != null)
                statusText.text = FormatStatus(boundClaim.status);

            BindIcon(boundClaim, itemDefinitions);

            if (claimButton != null)
            {
                claimButton.onClick.RemoveListener(HandleClaimClicked);
                claimButton.onClick.AddListener(HandleClaimClicked);
                claimButton.interactable = true;
            }

            gameObject.SetActive(true);
        }

        public void SetClaimPending(bool pending)
        {
            if (claimButton != null)
                claimButton.interactable = !pending;
        }

        private void BindIcon(
            PlayerTradeClaimDto claim,
            ItemDefinitionTable itemDefinitions)
        {
            ItemDefinition definition = null;
            if (claim != null && itemDefinitions != null)
            {
                if (string.Equals(claim.kind, "currency",
                    StringComparison.OrdinalIgnoreCase))
                {
                    itemDefinitions.TryGet(claim.currencyId, out definition);
                }
                else if (claim.asset != null
                    && string.Equals(claim.asset.kind, "equipment",
                        StringComparison.OrdinalIgnoreCase))
                {
                    itemDefinitions.TryGetByEquipmentId(
                        claim.asset.equipmentId, out definition);
                }
                else if (claim.asset != null)
                {
                    itemDefinitions.TryGet(claim.asset.itemId, out definition);
                }
            }

            BindDetail(definition);
            if (itemIcon != null)
            {
                itemIcon.sprite = definition == null ? null : definition.Icon;
                itemIcon.gameObject.SetActive(itemIcon.sprite != null);
            }
        }

        private string GetItemName(
            PlayerTradeAssetDto asset,
            ItemDefinitionTable itemDefinitions)
        {
            if (asset == null || itemDefinitions == null)
                return "未知物品";

            ItemDefinition definition = null;
            if (string.Equals(asset.kind, "equipment",
                StringComparison.OrdinalIgnoreCase))
                itemDefinitions.TryGetByEquipmentId(asset.equipmentId, out definition);
            else
                itemDefinitions.TryGet(asset.itemId, out definition);

            return definition == null || string.IsNullOrWhiteSpace(definition.DisplayName)
                ? "未知物品" : definition.DisplayName;
        }

        private static string GetCurrencyName(string currencyId)
        {
            if (string.Equals(currencyId, "gold_coin",
                StringComparison.OrdinalIgnoreCase))
                return "金币";
            return string.IsNullOrWhiteSpace(currencyId) ? "货币" : currencyId;
        }

        private static string FormatReason(string reason)
        {
            switch (reason)
            {
                case "sale_proceeds": return "出售所得";
                case "cancelled_return": return "撤单返还";
                case "rejected_return": return "拒绝返还";
                case "expired_return": return "过期返还";
                default: return string.IsNullOrWhiteSpace(reason) ? "--" : reason;
            }
        }

        private static string FormatStatus(string status)
        {
            if (string.Equals(status, "pending",
                StringComparison.OrdinalIgnoreCase))
                return "待领取";
            if (string.Equals(status, "claimed",
                StringComparison.OrdinalIgnoreCase))
                return "已领取";
            return string.IsNullOrWhiteSpace(status) ? "--" : status;
        }

        private void HandleClaimClicked()
        {
            if (boundClaim != null && claimRequested != null)
                claimRequested(this);
        }

        private void ResolveReferences()
        {
            if (itemIcon == null)
                itemIcon = FindChild<Image>("物品图标", "挂单预制体图标",
                    "商城出售格子预制体物品图标");
            if (itemNameText == null)
                itemNameText = FindChild<TMP_Text>("物品名称", "名称", "物品名");
            if (quantityText == null)
                quantityText = FindChild<TMP_Text>("数量", "数量1");
            if (amountText == null)
                amountText = FindChild<TMP_Text>("可领取", "金额", "总价1");
            if (reasonText == null)
                reasonText = FindChild<TMP_Text>("原因", "来源");
            if (statusText == null)
                statusText = FindChild<TMP_Text>("状态");
            if (claimButton == null)
                claimButton = FindChild<Button>("领取按钮", "领取");

            EnsureDetailTrigger();
        }

        private void EnsureDetailTrigger()
        {
            if (itemIcon == null)
            {
                detailHoverTrigger = null;
                return;
            }

            if (detailHoverTrigger == null)
            {
                detailHoverTrigger =
                    itemIcon.GetComponent<ItemDetailHoverTrigger>();
                if (detailHoverTrigger == null
                    && ItemDetailHoverService.IsConfiguredInScene(this))
                {
                    detailHoverTrigger =
                        itemIcon.gameObject.AddComponent<ItemDetailHoverTrigger>();
                }
            }

            itemIcon.raycastTarget = detailHoverTrigger != null;
        }

        private void BindDetail(ItemDefinition definition)
        {
            EnsureDetailTrigger();
            if (detailHoverTrigger == null)
                return;

            ItemDetailData detail =
                ItemDetailDataResolver.FromItem(definition);
            if (detail == null || !detail.IsValid)
                detailHoverTrigger.ClearBinding();
            else
                detailHoverTrigger.Bind(detail);
        }

        private void ClearDetailBinding()
        {
            if (detailHoverTrigger != null)
                detailHoverTrigger.ClearBinding();
        }

        private T FindChild<T>(params string[] childNames) where T : Component
        {
            if (childNames == null)
                return null;

            T[] components = GetComponentsInChildren<T>(true);
            for (int index = 0; index < components.Length; index++)
            {
                T component = components[index];
                if (component == null)
                    continue;

                for (int nameIndex = 0; nameIndex < childNames.Length; nameIndex++)
                {
                    if (string.Equals(component.gameObject.name,
                        childNames[nameIndex], StringComparison.Ordinal))
                        return component;
                }
            }
            return null;
        }
    }
}
