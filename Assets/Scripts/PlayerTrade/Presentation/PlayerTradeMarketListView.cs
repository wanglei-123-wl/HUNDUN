using System;
using System.Collections.Generic;
using Game.Items;
using Game.Inventory;
using Game.PlayerTrade.Model;
using Game.PlayerTrade.Protocol;
using Game.UI.Notice;
using UnityEngine;
using UnityEngine.UI;

namespace Game.PlayerTrade.Presentation
{
    [DisallowMultipleComponent]
    public sealed class PlayerTradeMarketListView : MonoBehaviour
    {
        [Header("列表")]
        [SerializeField] private RectTransform contentRoot;
        [SerializeField] private PlayerTradeListingCardView listingPrefab;
        [SerializeField] private ItemDefinitionTable itemDefinitions;
        [SerializeField, Min(1)] private int pageSize = 20;
        [SerializeField] private string sort = "newest";

        [Header("滚动加载")]
        [SerializeField] private ScrollRect scrollRect;
        [SerializeField, Min(1), InspectorName("每行格子数")]
        private int columns = 3;
        [SerializeField, Min(1), InspectorName("滚动多少个物品后加载")]
        private int preloadItemThreshold = 15;

        [Header("布局（可视化调整）")]
        [SerializeField, Min(0f), InspectorName("横向间距")]
        private float horizontalSpacing = 5f;
        [SerializeField, Min(0f), InspectorName("纵向间距")]
        private float verticalSpacing = 5f;
        [SerializeField, Min(0), InspectorName("左边距")]
        private int leftPadding;
        [SerializeField, Min(0), InspectorName("右边距")]
        private int rightPadding;
        [SerializeField, Min(0), InspectorName("上边距")]
        private int topPadding;
        [SerializeField, Min(0), InspectorName("下边距")]
        private int bottomPadding;

        [Header("网络")]
        [SerializeField] private NetworkManager networkManager;
        [SerializeField] private NetMessageDispatcher messageDispatcher;

        private readonly List<PlayerTradeListingCardView> activeCards =
            new List<PlayerTradeListingCardView>();
        private GameSession gameSession;
        private GridLayoutGroup gridLayout;
        private ContentSizeFitter contentSizeFitter;
        private bool subscribed;
        private bool scrollSubscribed;
        private bool listRequestInFlight;
        private bool refreshQueued;
        private bool currentRequestReplacesList;
        private string nextCursor = string.Empty;
        private int nextLoadTriggerItemCount;
        private readonly Dictionary<string, PlayerTradeBuyRequestDto> pendingPurchases =
            new Dictionary<string, PlayerTradeBuyRequestDto>(StringComparer.Ordinal);
        private float buyRequestStartedAt;
        private bool buyRequestRetried;
        private const float BuyRequestTimeoutSeconds = 10f;
        private readonly HashSet<string> handledEventIds =
            new HashSet<string>(StringComparer.Ordinal);

        public bool HasNextPage => !string.IsNullOrEmpty(nextCursor);
        public int ActiveCardCount => activeCards.Count;

        private void Awake()
        {
            ResolveReferences();
            ApplyLayout();
            ResetPreloadTrigger();
            EnsureSubscribed();
        }

        private void OnEnable()
        {
            ResolveReferences();
            ApplyLayout();
            EnsureSubscribed();
            EnsureScrollSubscribed();
            Debug.Log("[PlayerTrade][UI][MarketList][OnEnable] object="
                + name + " active=" + gameObject.activeSelf, this);
            RequestFirstPageIfWorldReady();
        }

        private void Update()
        {
            if (!subscribed)
                EnsureSubscribed();
            TickBuyRequest();
            if (refreshQueued && !listRequestInFlight)
            {
                refreshQueued = false;
                RequestFirstPage();
            }
        }

        private void OnDisable()
        {
            Debug.Log("[PlayerTrade][UI][MarketList][OnDisable] object="
                + name, this);
            UnsubscribeScroll();
        }

        private void OnDestroy()
        {
            UnsubscribeScroll();
            Unsubscribe();
            ClearCards();
        }

        public void RefreshMarket()
        {
            ResolveReferences();
            EnsureSubscribed();
            RequestFirstPage();
        }

        public void LoadNextPage()
        {
            if (!HasNextPage || listRequestInFlight)
                return;
            RequestList(nextCursor, false);
        }

        private void HandleScrollChanged(Vector2 _)
        {
            if (listRequestInFlight || !HasNextPage || contentRoot == null)
                return;

            int scrolledItemCount = GetScrolledItemCount();
            if (scrolledItemCount < nextLoadTriggerItemCount)
                return;

            LoadNextPage();
            if (listRequestInFlight)
                nextLoadTriggerItemCount += GetPreloadItemThreshold();
        }

        private void RequestFirstPageIfWorldReady()
        {
            if (gameSession != null && gameSession.LocalCharacterDbId > 0
                && networkManager != null && networkManager.IsConnected)
                RequestFirstPage();
        }

        private void RequestFirstPage()
        {
            nextCursor = string.Empty;
            ResetPreloadTrigger();
            RequestList(string.Empty, true);
        }

        private void RequestList(string cursor, bool replace)
        {
            if (networkManager == null || !networkManager.IsConnected)
                return;
            if (gameSession == null || gameSession.LocalCharacterDbId <= 0)
                return;
            if (listRequestInFlight)
            {
                refreshQueued = true;
                return;
            }

            PlayerTradeOrderListRequestDto request =
                new PlayerTradeOrderListRequestDto
                {
                    listType = "market",
                    cursor = cursor ?? string.Empty,
                    pageSize = Mathf.Clamp(pageSize, 1, 50),
                    itemIds = new string[0],
                    minimumPrice = null,
                    maximumPrice = null,
                    sort = string.IsNullOrWhiteSpace(sort) ? "newest" : sort
                };

            if (!networkManager.SendJson(
                    PlayerTradeRequestJson.ToOrderListJson(request)))
                return;

            listRequestInFlight = true;
            currentRequestReplacesList = replace;
            Debug.Log("[PlayerTrade][OrderList][TX] listType=market cursor="
                + (cursor ?? string.Empty) + " pageSize=" + request.pageSize);
        }

        private void HandleOrderListResult(string json)
        {
            PlayerTradeOrderListResultDto result;
            try
            {
                result = JsonUtility.FromJson<PlayerTradeOrderListResultDto>(json);
            }
            catch (Exception exception)
            {
                listRequestInFlight = false;
                Debug.LogWarning("[PlayerTrade][OrderList][RX] parse failed: "
                    + exception.Message);
                GameNoticeService.Show("市场列表加载失败，请稍后重试");
                return;
            }

            listRequestInFlight = false;
            if (result == null || !result.success)
            {
                Debug.LogWarning("[PlayerTrade][OrderList][RX] failed errorCode="
                    + (result == null ? "invalid_response" : result.errorCode)
                    + " message=" + (result == null ? string.Empty : result.message));
                GameNoticeService.Show("市场列表加载失败，请稍后重试");
                return;
            }
            if (!string.IsNullOrWhiteSpace(result.listType)
                && !string.Equals(result.listType, "market",
                    StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning("[PlayerTrade][OrderList][RX] ignored non-market list: "
                    + result.listType);
                return;
            }

            bool replace = currentRequestReplacesList;
            currentRequestReplacesList = false;
            nextCursor = result.nextCursor ?? string.Empty;
            RenderOrders(result.orders, replace);
            Debug.Log("[PlayerTrade][OrderList][RX] orders="
                + (result.orders == null ? 0 : result.orders.Length)
                + " nextCursor=" + nextCursor
                + " mode=" + (replace ? "replace" : "append"));
        }

        private void HandleBuyResult(string json)
        {
            PlayerTradeBuyResultDto result;
            try
            {
                result = JsonUtility.FromJson<PlayerTradeBuyResultDto>(json);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[PlayerTrade][Buy][RX] parse failed: "
                    + exception.Message);
                GameNoticeService.Show("购买结果异常，请刷新市场");
                return;
            }

            if (result == null)
                return;

            string orderId = result.order == null ? string.Empty
                : result.order.orderId;
            if (string.IsNullOrEmpty(orderId))
            {
                foreach (KeyValuePair<string, PlayerTradeBuyRequestDto> pending
                    in pendingPurchases)
                {
                    if (pending.Value.requestSequence == result.requestSequence)
                    {
                        orderId = pending.Key;
                        break;
                    }
                }
            }

            if (string.IsNullOrEmpty(orderId)
                || !pendingPurchases.TryGetValue(orderId,
                    out PlayerTradeBuyRequestDto request)
                || request.requestSequence != result.requestSequence
                || !PlayerTradeRequestSequence.IsCurrent(request,
                    result.playerTradeOperationSessionId, result.clientRequestId))
                return;

            PlayerTradeRequestSequence.Complete(request,
                result.playerTradeOperationSessionId, result.clientRequestId);
            pendingPurchases.Remove(orderId);
            SetCardPending(orderId, false);

            Debug.Log("[PlayerTrade][Buy][RX] success=" + result.success
                + " status=" + (result.status ?? string.Empty)
                + " requestSequence=" + result.requestSequence
                + " errorCode=" + (result.errorCode ?? string.Empty));

            if (!result.success && !string.Equals(result.status,
                    "succeeded_existing", StringComparison.OrdinalIgnoreCase))
            {
                GameNoticeService.Show("购买失败，请稍后重试");
                return;
            }

            GameNoticeService.Show("购买成功");
            InventoryModuleRuntime inventory =
                FindObjectOfType<InventoryModuleRuntime>();
            if (inventory != null)
            {
                inventory.RequestSnapshot();
                Debug.Log("[PlayerTrade][Buy] inventory snapshot requested after success.");
            }

            RequestFirstPage();
        }

        private void HandleOrderNotice(string json)
        {
            PlayerTradeOrderNoticeDto notice =
                JsonUtility.FromJson<PlayerTradeOrderNoticeDto>(json);
            if (notice != null && !RememberEvent(notice.eventId))
                return;
            Debug.Log("[PlayerTrade][Notice] received directed-order notice; "
                + "market list is unchanged.");
        }

        private void HandleOrderChanged(string json)
        {
            PlayerTradeOrderChangedDto changed =
                JsonUtility.FromJson<PlayerTradeOrderChangedDto>(json);
            if (changed != null && !RememberEvent(changed.eventId))
                return;
            if (changed == null
                || string.IsNullOrWhiteSpace(changed.eventType)
                || string.Equals(changed.eventType, "order_changed",
                    StringComparison.OrdinalIgnoreCase))
                refreshQueued = true;
        }

        private void HandleWorldEntered(long epoch, long characterDbId)
        {
            pendingPurchases.Clear();
            handledEventIds.Clear();
            RequestFirstPage();
        }

        private void HandleNetworkConnected()
        {
            RequestFirstPageIfWorldReady();
        }

        private void HandleNetworkDisconnected(string reason)
        {
            listRequestInFlight = false;
            pendingPurchases.Clear();
            nextCursor = string.Empty;
            ClearCards();
        }

        private bool RememberEvent(string eventId)
        {
            if (string.IsNullOrWhiteSpace(eventId))
                return true;
            if (!handledEventIds.Add(eventId))
                return false;
            if (handledEventIds.Count > 256)
                handledEventIds.Clear();
            return true;
        }

        private void HandleCardBuyRequested(
            PlayerTradeListingCardView card)
        {
            if (card == null || card.Order == null
                || string.IsNullOrWhiteSpace(card.Order.orderId)
                || pendingPurchases.ContainsKey(card.Order.orderId))
                return;
            if (networkManager == null || !networkManager.IsConnected)
            {
                GameNoticeService.Show("市场暂不可用，请检查网络连接");
                return;
            }

            PlayerTradeBuyRequestDto request =
                new PlayerTradeBuyRequestDto(0, card.Order.orderId);
            if (!PlayerTradeRequestSequence.TryPrepare(request))
            {
                GameNoticeService.Show("交易请求处理中，请稍后");
                return;
            }
            if (!networkManager.Send(request))
            {
                GameNoticeService.Show("购买请求发送失败，请稍后重试");
                return;
            }

            pendingPurchases[card.Order.orderId] = request;
            buyRequestStartedAt = Time.unscaledTime;
            buyRequestRetried = false;
            card.SetPurchasePending(true);
            GameNoticeService.Show("正在购买...");
            Debug.Log("[PlayerTrade][Buy][TX] orderId=" + card.Order.orderId
                + " requestSequence=" + request.requestSequence);
        }

        private void TickBuyRequest()
        {
            if (pendingPurchases.Count == 0
                || Time.unscaledTime - buyRequestStartedAt
                    < BuyRequestTimeoutSeconds)
                return;

            foreach (KeyValuePair<string, PlayerTradeBuyRequestDto> pending
                in pendingPurchases)
            {
                if (!buyRequestRetried && networkManager != null
                    && networkManager.IsConnected)
                {
                    buyRequestRetried = true;
                    buyRequestStartedAt = Time.unscaledTime;
                    if (networkManager.Send(pending.Value))
                    {
                        Debug.Log("[PlayerTrade][Buy][RETRY] requestSequence="
                            + pending.Value.requestSequence
                            + " clientRequestId=" + pending.Value.clientRequestId);
                        return;
                    }
                }

                SetCardPending(pending.Key, false);
                GameNoticeService.Show("购买结果未确认，请刷新市场");
                Debug.LogWarning("[PlayerTrade][Buy] Result unknown. "
                    + "Refresh orders, claims, inventory and wallet; do not issue "
                    + "a new trade until resolved. clientRequestId="
                    + pending.Value.clientRequestId);
                break;
            }

            pendingPurchases.Clear();
            InventoryModuleRuntime inventory =
                FindObjectOfType<InventoryModuleRuntime>();
            inventory?.RequestSnapshot();
            RequestFirstPage();
        }

        private void RenderOrders(PlayerTradeOrderDto[] orders, bool replace)
        {
            if (replace)
            {
                ClearCards();
                ResetScrollPosition();
                ResetPreloadTrigger();
            }

            if (orders == null || listingPrefab == null || contentRoot == null)
                return;

            int firstCardIndex = activeCards.Count;
            for (int index = 0; index < orders.Length; index++)
            {
                if (orders[index] == null)
                    continue;

                PlayerTradeListingCardView card =
                    Instantiate(listingPrefab, contentRoot, false);
                card.name = "市场格子_" + (firstCardIndex + index).ToString("D2");
                card.Bind(orders[index], itemDefinitions,
                    HandleCardBuyRequested);
                activeCards.Add(card);
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRoot);
        }

        private int GetScrolledItemCount()
        {
            if (gridLayout == null || contentRoot == null)
                return 0;

            float rowStride = gridLayout.cellSize.y + gridLayout.spacing.y;
            if (rowStride <= 0f)
                return 0;

            int scrolledRows = Mathf.FloorToInt(
                Mathf.Max(0f, contentRoot.anchoredPosition.y) / rowStride);
            return scrolledRows * GetColumnCount();
        }

        private int GetColumnCount()
        {
            return Mathf.Max(1, columns);
        }

        private int GetPreloadItemThreshold()
        {
            return Mathf.Max(1, preloadItemThreshold);
        }

        private void ResetPreloadTrigger()
        {
            nextLoadTriggerItemCount = GetPreloadItemThreshold();
        }

        private void ResetScrollPosition()
        {
            if (contentRoot != null)
            {
                Vector2 position = contentRoot.anchoredPosition;
                position.y = 0f;
                contentRoot.anchoredPosition = position;
            }

            if (scrollRect != null)
                scrollRect.verticalNormalizedPosition = 1f;
        }

        private void ClearCards()
        {
            for (int index = 0; index < activeCards.Count; index++)
            {
                if (activeCards[index] != null)
                    Destroy(activeCards[index].gameObject);
            }
            activeCards.Clear();
        }

        private void SetCardPending(string orderId, bool pending)
        {
            for (int index = 0; index < activeCards.Count; index++)
            {
                PlayerTradeListingCardView card = activeCards[index];
                if (card != null && string.Equals(card.OrderId, orderId,
                        StringComparison.Ordinal))
                {
                    card.SetPurchasePending(pending);
                    return;
                }
            }
        }

        private void ResolveReferences()
        {
            if (contentRoot == null)
                contentRoot = GetComponent<RectTransform>();
            if (gridLayout == null && contentRoot != null)
                gridLayout = contentRoot.GetComponent<GridLayoutGroup>();
            if (contentSizeFitter == null && contentRoot != null)
                contentSizeFitter = contentRoot.GetComponent<ContentSizeFitter>();
            if (scrollRect == null && contentRoot != null)
                scrollRect = contentRoot.GetComponentInParent<ScrollRect>();
            if (networkManager == null)
                networkManager = NetworkManager.Instance;
            if (messageDispatcher == null)
                messageDispatcher = NetMessageDispatcher.Instance;
            if (gameSession == null)
                gameSession = FindObjectOfType<GameSession>();
            if (itemDefinitions == null)
                itemDefinitions = FindObjectOfType<ItemDefinitionTable>(true);
        }

        private void ApplyLayout()
        {
            if (gridLayout == null)
                return;

            gridLayout.spacing = new Vector2(
                Mathf.Max(0f, horizontalSpacing),
                Mathf.Max(0f, verticalSpacing));
            gridLayout.padding = new RectOffset(
                Mathf.Max(0, leftPadding),
                Mathf.Max(0, rightPadding),
                Mathf.Max(0, topPadding),
                Mathf.Max(0, bottomPadding));
            gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            gridLayout.constraintCount = GetColumnCount();

            if (listingPrefab != null)
            {
                RectTransform prefabRect =
                    listingPrefab.GetComponent<RectTransform>();
                if (prefabRect != null && prefabRect.sizeDelta.x > 0f
                    && prefabRect.sizeDelta.y > 0f)
                    gridLayout.cellSize = prefabRect.sizeDelta;
            }

            if (contentSizeFitter != null)
                contentSizeFitter.verticalFit =
                    ContentSizeFitter.FitMode.PreferredSize;

            if (scrollRect != null)
            {
                scrollRect.horizontal = false;
                scrollRect.vertical = true;
            }
        }

        private void EnsureSubscribed()
        {
            ResolveReferences();
            if (subscribed || messageDispatcher == null)
                return;

            messageDispatcher.Register(
                PlayerTradeMessageIds.OrderListResult,
                HandleOrderListResult);
            messageDispatcher.Register(
                PlayerTradeMessageIds.BuyResult,
                HandleBuyResult);
            messageDispatcher.Register(
                PlayerTradeMessageIds.OrderNotice,
                HandleOrderNotice);
            messageDispatcher.Register(
                PlayerTradeMessageIds.OrderChanged,
                HandleOrderChanged);
            if (gameSession != null)
                gameSession.LocalWorldEntered += HandleWorldEntered;
            if (networkManager != null)
            {
                networkManager.OnConnected += HandleNetworkConnected;
                networkManager.OnDisconnected += HandleNetworkDisconnected;
            }
            subscribed = true;
        }

        private void EnsureScrollSubscribed()
        {
            ResolveReferences();
            if (scrollSubscribed || scrollRect == null)
                return;

            scrollRect.onValueChanged.AddListener(HandleScrollChanged);
            scrollSubscribed = true;
        }

        private void UnsubscribeScroll()
        {
            if (!scrollSubscribed)
                return;

            if (scrollRect != null)
                scrollRect.onValueChanged.RemoveListener(HandleScrollChanged);
            scrollSubscribed = false;
        }

        private void Unsubscribe()
        {
            if (!subscribed)
                return;

            if (messageDispatcher != null)
            {
                messageDispatcher.Unregister(
                    PlayerTradeMessageIds.OrderListResult,
                    HandleOrderListResult);
                messageDispatcher.Unregister(
                    PlayerTradeMessageIds.BuyResult,
                    HandleBuyResult);
                messageDispatcher.Unregister(
                    PlayerTradeMessageIds.OrderNotice,
                    HandleOrderNotice);
                messageDispatcher.Unregister(
                    PlayerTradeMessageIds.OrderChanged,
                    HandleOrderChanged);
            }
            if (gameSession != null)
                gameSession.LocalWorldEntered -= HandleWorldEntered;
            if (networkManager != null)
            {
                networkManager.OnConnected -= HandleNetworkConnected;
                networkManager.OnDisconnected -= HandleNetworkDisconnected;
            }
            subscribed = false;
        }
    }
}
