using System;
using System.Globalization;
using Game.Items;
using Game.PlayerTrade.Model;
using Game.PlayerTrade.Protocol;
using Game.UI.ItemDetail;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.PlayerTrade.Presentation
{
    [DisallowMultipleComponent]
    public sealed class PlayerTradeListingCardView : MonoBehaviour
    {
        [Header("显示")]
        [SerializeField] private Image itemIcon;
        [SerializeField] private TMP_Text quantityText;
        [SerializeField] private TMP_Text sellerText;
        [SerializeField] private TMP_Text unitPriceText;
        [SerializeField] private TMP_Text totalPriceText;
        [SerializeField] private TMP_Text remainingTimeText;

        [Header("交互")]
        [SerializeField] private Button buyButton;
        [SerializeField] private Button cancelButton;

        private ItemDetailHoverTrigger detailHoverTrigger;
        private PlayerTradeOrderDto boundOrder;
        private Action<PlayerTradeListingCardView> buyRequested;
        private Action<PlayerTradeListingCardView> cancelRequested;

        public string OrderId => boundOrder == null ? string.Empty : boundOrder.orderId;
        public PlayerTradeOrderDto Order => boundOrder;

        private void Awake()
        {
            ResolveReferences();
        }

        private void Update()
        {
            if (boundOrder != null && remainingTimeText != null)
                remainingTimeText.text =
                    PlayerTradeTimeFormatter.FormatRemaining(
                        boundOrder.expiresAtUnixMilliseconds);
        }

        private void OnDestroy()
        {
            ClearDetailBinding();
            if (buyButton != null)
                buyButton.onClick.RemoveListener(HandleBuyClicked);
            if (cancelButton != null)
                cancelButton.onClick.RemoveListener(HandleCancelClicked);
        }

        public void Bind(
            PlayerTradeOrderDto order,
            ItemDefinitionTable itemDefinitions,
            Action<PlayerTradeListingCardView> onBuyRequested)
        {
            boundOrder = order;
            buyRequested = onBuyRequested;
            cancelRequested = null;
            ResolveReferences();

            if (boundOrder == null)
            {
                ClearDetailBinding();
                gameObject.SetActive(false);
                return;
            }

            PlayerTradeAssetDto asset = boundOrder.asset;
            int quantity = asset == null ? 1 : Mathf.Max(1, asset.quantity);
            if (quantityText != null)
                quantityText.text = FormatItemQuantity(asset, quantity);
            if (sellerText != null)
                sellerText.text = string.IsNullOrWhiteSpace(boundOrder.sellerNickname)
                    ? "--" : boundOrder.sellerNickname;
            if (totalPriceText != null)
                totalPriceText.text = boundOrder.totalPrice.ToString(
                    CultureInfo.InvariantCulture);
            if (unitPriceText != null)
            {
                long unitPrice = quantity <= 0 ? boundOrder.totalPrice
                    : boundOrder.totalPrice / quantity;
                unitPriceText.text = unitPrice.ToString(
                    CultureInfo.InvariantCulture);
            }
            if (remainingTimeText != null)
                remainingTimeText.text =
                    PlayerTradeTimeFormatter.FormatRemaining(
                        boundOrder.expiresAtUnixMilliseconds);

            BindIcon(asset, itemDefinitions);

            if (buyButton != null)
            {
                buyButton.onClick.RemoveListener(HandleBuyClicked);
                buyButton.onClick.AddListener(HandleBuyClicked);
                buyButton.interactable = true;
            }
            if (cancelButton != null)
            {
                cancelButton.onClick.RemoveListener(HandleCancelClicked);
                cancelButton.interactable = false;
            }

            gameObject.SetActive(true);
        }

        public void BindOwned(
            PlayerTradeOrderDto order,
            ItemDefinitionTable itemDefinitions,
            Action<PlayerTradeListingCardView> onCancelRequested)
        {
            boundOrder = order;
            buyRequested = null;
            cancelRequested = onCancelRequested;
            ResolveReferences();

            if (boundOrder == null)
            {
                ClearDetailBinding();
                gameObject.SetActive(false);
                return;
            }

            PlayerTradeAssetDto asset = boundOrder.asset;
            int quantity = asset == null ? 1 : Mathf.Max(1, asset.quantity);
            if (quantityText != null)
                quantityText.text = FormatItemQuantity(asset, quantity);
            if (sellerText != null)
                sellerText.text = string.IsNullOrWhiteSpace(boundOrder.status)
                    ? "--" : boundOrder.status;
            if (totalPriceText != null)
                totalPriceText.text = boundOrder.totalPrice.ToString(
                    CultureInfo.InvariantCulture);
            if (unitPriceText != null)
            {
                long unitPrice = quantity <= 0 ? boundOrder.totalPrice
                    : boundOrder.totalPrice / quantity;
                unitPriceText.text = unitPrice.ToString(
                    CultureInfo.InvariantCulture);
            }
            if (remainingTimeText != null)
                remainingTimeText.text =
                    PlayerTradeTimeFormatter.FormatRemaining(
                        boundOrder.expiresAtUnixMilliseconds);

            BindIcon(asset, itemDefinitions);

            if (buyButton != null)
                buyButton.interactable = false;
            if (cancelButton != null)
            {
                cancelButton.onClick.RemoveListener(HandleCancelClicked);
                cancelButton.onClick.AddListener(HandleCancelClicked);
                cancelButton.interactable = true;
            }

            gameObject.SetActive(true);
        }

        public void SetPurchasePending(bool pending)
        {
            if (buyButton != null)
                buyButton.interactable = !pending;
        }

        public void SetCancelPending(bool pending)
        {
            if (cancelButton != null)
                cancelButton.interactable = !pending;
        }

        private void BindIcon(
            PlayerTradeAssetDto asset,
            ItemDefinitionTable itemDefinitions)
        {
            ItemDefinition definition = null;
            if (asset != null && itemDefinitions != null)
            {
                if (string.Equals(asset.kind, "equipment",
                        StringComparison.OrdinalIgnoreCase))
                {
                    itemDefinitions.TryGetByEquipmentId(
                        asset.equipmentId, out definition);
                }
                else
                {
                    itemDefinitions.TryGet(asset.itemId, out definition);
                }
            }

            BindDetail(definition);
            if (itemIcon != null)
            {
                itemIcon.sprite = definition == null ? null : definition.Icon;
                itemIcon.gameObject.SetActive(
                    itemIcon.sprite != null);
            }
        }

        private void HandleBuyClicked()
        {
            if (boundOrder != null && buyRequested != null)
                buyRequested(this);
        }

        private void HandleCancelClicked()
        {
            if (boundOrder != null && cancelRequested != null)
                cancelRequested(this);
        }

        private static string FormatItemQuantity(PlayerTradeAssetDto asset, int quantity)
        {
            string count = quantity.ToString(CultureInfo.InvariantCulture);
            return asset != null && asset.remainingUses > 0
                ? count + " ·" + asset.remainingUses.ToString(CultureInfo.InvariantCulture) + "次"
                : count;
        }

        private void ResolveReferences()
        {
            if (itemIcon == null)
                itemIcon = FindChild<Image>(
                    "商城出售格子预制体物品图标", "挂单预制体图标");
            if (quantityText == null)
                quantityText = FindChild<TMP_Text>("数量", "数量1");
            if (sellerText == null)
                sellerText = FindChild<TMP_Text>("卖家", "状态");
            if (unitPriceText == null)
                unitPriceText = FindChild<TMP_Text>("单价", "单价1");
            if (totalPriceText == null)
                totalPriceText = FindChild<TMP_Text>("总价", "总价1");
            if (remainingTimeText == null)
                remainingTimeText = FindChild<TMP_Text>(
                    "剩余时间", "时间1");
            if (buyButton == null)
                buyButton = FindChild<Button>("购买按钮");
            if (cancelButton == null)
                cancelButton = FindChild<Button>("下架按钮");

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
                if (components[index] == null)
                    continue;
                for (int nameIndex = 0; nameIndex < childNames.Length; nameIndex++)
                {
                    if (string.Equals(components[index].gameObject.name,
                        childNames[nameIndex], StringComparison.Ordinal))
                        return components[index];
                }
            }
            return null;
        }
    }
}
