using System;
using System.Collections.Generic;
using Game.Items;
using Game.PlayerTrade.Protocol;
using Game.UI.Notice;
using UnityEngine;
using UnityEngine.UI;

namespace Game.PlayerTrade.Presentation
{
    [DisallowMultipleComponent]
    public sealed class PlayerTradeOwnedListView : MonoBehaviour
    {
        [Header("我的挂单列表")]
        [SerializeField] private RectTransform contentRoot;
        [SerializeField] private PlayerTradeListingCardView listingPrefab;
        [SerializeField] private ItemDefinitionTable itemDefinitions;
        [SerializeField, Min(1)] private int pageSize = 20;

        [Header("滚动加载")]
        [SerializeField] private ScrollRect scrollRect;
        [SerializeField, Min(0f), InspectorName("格子上下间距")]
        private float verticalSpacing = 5f;
        [SerializeField, Min(0), InspectorName("上边距")]
        private int topPadding;
        [SerializeField, Min(0), InspectorName("下边距")]
        private int bottomPadding;
        [SerializeField, Min(0), InspectorName("左边距")]
        private int leftPadding;
        [SerializeField, Min(0), InspectorName("右边距")]
        private int rightPadding;

        [Header("网络")]
        [SerializeField] private NetworkManager networkManager;
        [SerializeField] private NetMessageDispatcher messageDispatcher;

        private readonly List<PlayerTradeListingCardView> activeCards =
            new List<PlayerTradeListingCardView>();
        private GameSession gameSession;
        private VerticalLayoutGroup verticalLayout;
        private ContentSizeFitter contentSizeFitter;
        private string nextCursor = string.Empty;
        private bool subscribed;
        private bool scrollSubscribed;
        private bool listRequestInFlight;
        private bool currentRequestReplacesList;
        private bool refreshQueued;
        private PlayerTradeCancelRequestDto pendingCancelRequest;
        private float cancelRequestStartedAt;
        private bool cancelRequestRetried;
        private readonly HashSet<string> handledEventIds =
            new HashSet<string>(StringComparer.Ordinal);
        private const float CancelRequestTimeoutSeconds = 10f;

        public bool HasNextPage => !string.IsNullOrEmpty(nextCursor);
        public int ActiveCardCount => activeCards.Count;

        private void Awake()
        {
            ResolveReferences();
            ApplyLayout();
            EnsureSubscribed();
        }

        private void OnEnable()
        {
            ResolveReferences();
            ApplyLayout();
            EnsureSubscribed();
            EnsureScrollSubscribed();
            Debug.Log("[PlayerTrade][UI][OwnedList][OnEnable] object="
                + name + " active=" + gameObject.activeSelf, this);
            RequestFirstPageIfWorldReady();
        }

        private void Update()
        {
            if (!subscribed)
                EnsureSubscribed();
            TickCancelRequest();
            if (refreshQueued && !listRequestInFlight)
            {
                refreshQueued = false;
                RequestFirstPage();
            }
        }

        private void OnDisable()
        {
            Debug.Log("[PlayerTrade][UI][OwnedList][OnDisable] object="
                + name, this);
            UnsubscribeScroll();
            ClearCards();
        }

        private void OnDestroy()
        {
            UnsubscribeScroll();
            Unsubscribe();
            ClearCards();
        }

        public void RefreshOwned()
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
            if (listRequestInFlight || !HasNextPage || scrollRect == null)
                return;
            if (scrollRect.verticalNormalizedPosition > 0.1f)
                return;
            LoadNextPage();
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
            RequestList(string.Empty, true);
        }

        private void RequestList(string cursor, bool replace)
        {
            if (networkManager == null || !networkManager.IsConnected
                || gameSession == null || gameSession.LocalCharacterDbId <= 0)
                return;
            if (listRequestInFlight)
            {
                refreshQueued = true;
                return;
            }

            PlayerTradeOrderListRequestDto request =
                new PlayerTradeOrderListRequestDto
                {
                    listType = "owned",
                    cursor = cursor ?? string.Empty,
                    pageSize = Mathf.Clamp(pageSize, 1, 50),
                    itemIds = new string[0],
                    minimumPrice = null,
                    maximumPrice = null,
                    sort = "newest"
                };
            if (!networkManager.SendJson(
                PlayerTradeRequestJson.ToOrderListJson(request)))
                return;

            listRequestInFlight = true;
            currentRequestReplacesList = replace;
            Debug.Log("[PlayerTrade][OwnedList][TX] cursor="
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
                Debug.LogWarning("[PlayerTrade][OwnedList][RX] parse failed: "
                    + exception.Message);
                GameNoticeService.Show("我的挂单加载失败，请稍后重试");
                return;
            }

            if (result == null)
            {
                listRequestInFlight = false;
                return;
            }
            if (!string.Equals(result.listType, "owned",
                    StringComparison.OrdinalIgnoreCase))
                return;

            listRequestInFlight = false;
            if (!result.success)
            {
                Debug.LogWarning("[PlayerTrade][OwnedList][RX] failed errorCode="
                    + (result.errorCode ?? string.Empty));
                GameNoticeService.Show("我的挂单加载失败，请稍后重试");
                return;
            }

            bool replace = currentRequestReplacesList;
            currentRequestReplacesList = false;
            nextCursor = result.nextCursor ?? string.Empty;
            RenderOrders(result.orders, replace);
            Debug.Log("[PlayerTrade][OwnedList][RX] orders="
                + (result.orders == null ? 0 : result.orders.Length)
                + " nextCursor=" + nextCursor);
        }

        private void HandleCancelRequested(PlayerTradeListingCardView card)
        {
            if (card == null || card.Order == null
                || string.IsNullOrWhiteSpace(card.Order.orderId)
                || pendingCancelRequest != null
                || networkManager == null || !networkManager.IsConnected)
            {
                if (networkManager == null || !networkManager.IsConnected)
                    GameNoticeService.Show("交易暂不可用，请检查网络连接");
                return;
            }

            PlayerTradeCancelRequestDto request =
                new PlayerTradeCancelRequestDto
                {
                    orderId = card.Order.orderId
                };
            if (!PlayerTradeRequestSequence.TryPrepare(request))
            {
                GameNoticeService.Show("交易请求处理中，请稍后");
                return;
            }
            if (!networkManager.Send(request))
            {
                GameNoticeService.Show("下架请求发送失败，请稍后重试");
                return;
            }

            pendingCancelRequest = request;
            cancelRequestStartedAt = Time.unscaledTime;
            cancelRequestRetried = false;
            card.SetCancelPending(true);
            GameNoticeService.Show("正在下架挂单...");
            Debug.Log("[PlayerTrade][Cancel][TX] orderId="
                + card.Order.orderId + " requestSequence="
                + request.requestSequence);
        }

        private void HandleCancelResult(string json)
        {
            PlayerTradeCancelResultDto result;
            try
            {
                result = JsonUtility.FromJson<PlayerTradeCancelResultDto>(json);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[PlayerTrade][Cancel][RX] parse failed: "
                    + exception.Message);
                GameNoticeService.Show("下架结果异常，请稍后重试");
                return;
            }

            if (result == null)
                return;

            string orderId = result.order == null
                ? string.Empty : result.order.orderId;
            if (pendingCancelRequest == null
                || result.requestSequence != pendingCancelRequest.requestSequence
                || !PlayerTradeRequestSequence.IsCurrent(pendingCancelRequest,
                    result.playerTradeOperationSessionId, result.clientRequestId)
                || (string.IsNullOrWhiteSpace(orderId)
                    && string.IsNullOrWhiteSpace(pendingCancelRequest.orderId)))
                return;

            if (!string.IsNullOrWhiteSpace(orderId)
                && !string.Equals(orderId, pendingCancelRequest.orderId,
                    StringComparison.Ordinal))
                return;
            if (string.IsNullOrWhiteSpace(orderId))
                orderId = pendingCancelRequest.orderId;
            PlayerTradeRequestSequence.Complete(pendingCancelRequest,
                result.playerTradeOperationSessionId, result.clientRequestId);
            pendingCancelRequest = null;
            SetCardCancelPending(orderId, false);
            Debug.Log("[PlayerTrade][Cancel][RX] success=" + result.success
                + " status=" + (result.status ?? string.Empty)
                + " requestSequence=" + result.requestSequence
                + " errorCode=" + (result.errorCode ?? string.Empty));

            if (result.success || string.Equals(result.status,
                    "succeeded_existing", StringComparison.OrdinalIgnoreCase))
            {
                GameNoticeService.Show("挂单已下架");
                RequestFirstPage();
            }
            else
            {
                GameNoticeService.Show("下架失败，请稍后重试");
            }
        }

        private void HandleOrderChanged(string json)
        {
            PlayerTradeOrderChangedDto changed;
            try
            {
                changed = JsonUtility.FromJson<PlayerTradeOrderChangedDto>(json);
            }
            catch (Exception)
            {
                return;
            }
            if (changed != null && !RememberEvent(changed.eventId))
                return;
            refreshQueued = true;
        }

        private void TickCancelRequest()
        {
            if (pendingCancelRequest == null
                || Time.unscaledTime - cancelRequestStartedAt
                    < CancelRequestTimeoutSeconds)
                return;

            if (!cancelRequestRetried && networkManager != null
                && networkManager.IsConnected)
            {
                cancelRequestRetried = true;
                cancelRequestStartedAt = Time.unscaledTime;
                if (networkManager.Send(pendingCancelRequest))
                {
                    Debug.Log("[PlayerTrade][Cancel][RETRY] orderId="
                        + pendingCancelRequest.orderId
                        + " requestSequence="
                        + pendingCancelRequest.requestSequence);
                    return;
                }
            }

            string orderId = pendingCancelRequest.orderId;
            pendingCancelRequest = null;
            SetCardCancelPending(orderId, false);
            GameNoticeService.Show("下架结果未确认，请刷新我的挂单");
            Debug.LogWarning("[玩家交易] 下架结果超时，保留服务端状态并刷新我的挂单。",
                this);
            RequestFirstPage();
        }

        private void HandleWorldEntered(long epoch, long characterDbId)
        {
            pendingCancelRequest = null;
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
            pendingCancelRequest = null;
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

        private void RenderOrders(PlayerTradeOrderDto[] orders, bool replace)
        {
            if (replace)
            {
                ClearCards();
                ResetScrollPosition();
            }
            if (orders == null || listingPrefab == null || contentRoot == null)
                return;

            int firstIndex = activeCards.Count;
            for (int index = 0; index < orders.Length; index++)
            {
                if (orders[index] == null)
                    continue;
                PlayerTradeListingCardView card =
                    Instantiate(listingPrefab, contentRoot, false);
                card.name = "我的挂单_" + (firstIndex + index).ToString("D2");
                card.BindOwned(orders[index], itemDefinitions,
                    HandleCancelRequested);
                activeCards.Add(card);
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRoot);
        }

        private void SetCardCancelPending(string orderId, bool pending)
        {
            for (int index = 0; index < activeCards.Count; index++)
            {
                PlayerTradeListingCardView card = activeCards[index];
                if (card != null && string.Equals(card.OrderId, orderId,
                        StringComparison.Ordinal))
                {
                    card.SetCancelPending(pending);
                    return;
                }
            }
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

        private void ResetScrollPosition()
        {
            if (contentRoot != null)
                contentRoot.anchoredPosition = Vector2.zero;
            if (scrollRect != null)
                scrollRect.verticalNormalizedPosition = 1f;
        }

        private void ResolveReferences()
        {
            if (contentRoot == null)
                contentRoot = GetComponent<RectTransform>();
            if (verticalLayout == null && contentRoot != null)
                verticalLayout = contentRoot.GetComponent<VerticalLayoutGroup>();
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
            if (contentRoot == null)
                return;
            if (verticalLayout == null)
                verticalLayout = contentRoot.gameObject.AddComponent<VerticalLayoutGroup>();
            verticalLayout.spacing = Mathf.Max(0f, verticalSpacing);
            verticalLayout.padding = new RectOffset(
                Mathf.Max(0, leftPadding), Mathf.Max(0, rightPadding),
                Mathf.Max(0, topPadding), Mathf.Max(0, bottomPadding));
            verticalLayout.childControlWidth = true;
            verticalLayout.childControlHeight = true;
            verticalLayout.childForceExpandWidth = true;
            verticalLayout.childForceExpandHeight = false;
            if (contentSizeFitter == null)
                contentSizeFitter = contentRoot.gameObject.AddComponent<ContentSizeFitter>();
            contentSizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
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
            messageDispatcher.Register(PlayerTradeMessageIds.OrderListResult,
                HandleOrderListResult);
            messageDispatcher.Register(PlayerTradeMessageIds.CancelResult,
                HandleCancelResult);
            messageDispatcher.Register(PlayerTradeMessageIds.OrderChanged,
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
                messageDispatcher.Unregister(PlayerTradeMessageIds.OrderListResult,
                    HandleOrderListResult);
                messageDispatcher.Unregister(PlayerTradeMessageIds.CancelResult,
                    HandleCancelResult);
                messageDispatcher.Unregister(PlayerTradeMessageIds.OrderChanged,
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
