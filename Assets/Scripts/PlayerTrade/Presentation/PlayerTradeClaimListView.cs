using System;
using System.Collections.Generic;
using Game.Items;
using Game.Inventory;
using Game.PlayerTrade.Protocol;
using Game.UI.Notice;
using UnityEngine;
using UnityEngine.UI;

namespace Game.PlayerTrade.Presentation
{
    [DisallowMultipleComponent]
    public sealed class PlayerTradeClaimListView : MonoBehaviour
    {
        [Header("待领取列表")]
        [SerializeField] private RectTransform contentRoot;
        [SerializeField] private PlayerTradeClaimCardView claimPrefab;
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

        private readonly List<PlayerTradeClaimCardView> activeCards =
            new List<PlayerTradeClaimCardView>();
        private readonly HashSet<string> handledEventIds =
            new HashSet<string>(StringComparer.Ordinal);
        private GameSession gameSession;
        private VerticalLayoutGroup verticalLayout;
        private ContentSizeFitter contentSizeFitter;
        private string nextCursor = string.Empty;
        private bool subscribed;
        private bool scrollSubscribed;
        private bool listRequestInFlight;
        private bool currentRequestReplacesList;
        private bool refreshQueued;
        private PlayerTradeClaimRequestDto pendingClaim;
        private float claimRequestStartedAt;
        private bool claimRequestRetried;
        private const float ClaimRequestTimeoutSeconds = 10f;

        public bool HasNextPage => !string.IsNullOrEmpty(nextCursor);
        public int ActiveCardCount => activeCards.Count;

        private void Awake()
        {
            ResolveReferences();
            ApplyLayout();
        }

        private void OnEnable()
        {
            ResolveReferences();
            ApplyLayout();
            EnsureSubscribed();
            EnsureScrollSubscribed();
            RequestFirstPageIfWorldReady();
        }

        private void Update()
        {
            if (!subscribed)
                EnsureSubscribed();
            TickClaimRequest();
            if (refreshQueued && !listRequestInFlight && pendingClaim == null)
            {
                refreshQueued = false;
                RequestFirstPage();
            }
        }

        private void OnDisable()
        {
            UnsubscribeScroll();
            Unsubscribe();
        }

        private void OnDestroy()
        {
            UnsubscribeScroll();
            Unsubscribe();
            ClearCards();
        }

        public void RefreshClaims()
        {
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

            PlayerTradeClaimListRequestDto request =
                new PlayerTradeClaimListRequestDto
                {
                    cursor = cursor ?? string.Empty,
                    pageSize = Mathf.Clamp(pageSize, 1, 50),
                    includeClaimed = false
                };
            if (!networkManager.Send(request))
                return;

            listRequestInFlight = true;
            currentRequestReplacesList = replace;
            Debug.Log("[PlayerTrade][ClaimList][TX] cursor="
                + request.cursor + " pageSize=" + request.pageSize);
        }

        private void HandleClaimListResult(string json)
        {
            PlayerTradeClaimListResultDto result;
            try
            {
                result = JsonUtility.FromJson<PlayerTradeClaimListResultDto>(json);
            }
            catch (Exception exception)
            {
                listRequestInFlight = false;
                Debug.LogWarning("[PlayerTrade][ClaimList][RX] parse failed: "
                    + exception.Message);
                GameNoticeService.Show("待领取列表加载失败，请稍后重试");
                return;
            }

            listRequestInFlight = false;
            if (result == null || !result.success)
            {
                Debug.LogWarning("[PlayerTrade][ClaimList][RX] failed errorCode="
                    + (result == null ? "invalid_response" : result.errorCode));
                GameNoticeService.Show("待领取列表加载失败，请稍后重试");
                return;
            }

            bool replace = currentRequestReplacesList;
            currentRequestReplacesList = false;
            nextCursor = result.nextCursor ?? string.Empty;
            RenderClaims(result.claims, replace);
            Debug.Log("[PlayerTrade][ClaimList][RX] claims="
                + (result.claims == null ? 0 : result.claims.Length)
                + " nextCursor=" + nextCursor);
        }

        private void HandleClaimRequested(PlayerTradeClaimCardView card)
        {
            if (card == null || card.Claim == null
                || string.IsNullOrWhiteSpace(card.Claim.claimId)
                || pendingClaim != null
                || networkManager == null || !networkManager.IsConnected)
            {
                if (networkManager == null || !networkManager.IsConnected)
                    GameNoticeService.Show("交易暂不可用，请检查网络连接");
                return;
            }

            PlayerTradeClaimRequestDto request =
                new PlayerTradeClaimRequestDto
                {
                    claimId = card.Claim.claimId
                };
            if (!PlayerTradeRequestSequence.TryPrepare(request))
            {
                GameNoticeService.Show("交易请求处理中，请稍后");
                return;
            }
            if (!networkManager.Send(request))
            {
                GameNoticeService.Show("领取请求发送失败，请稍后重试");
                return;
            }

            pendingClaim = request;
            claimRequestStartedAt = Time.unscaledTime;
            claimRequestRetried = false;
            card.SetClaimPending(true);
            GameNoticeService.Show("正在领取...");
            Debug.Log("[PlayerTrade][Claim][TX] claimId=" + request.claimId
                + " requestSequence=" + request.requestSequence);
        }

        private void HandleClaimResult(string json)
        {
            PlayerTradeClaimResultDto result;
            try
            {
                result = JsonUtility.FromJson<PlayerTradeClaimResultDto>(json);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[PlayerTrade][Claim][RX] parse failed: "
                    + exception.Message);
                GameNoticeService.Show("领取结果异常，请稍后重试");
                return;
            }

            if (result == null || pendingClaim == null
                || result.requestSequence != pendingClaim.requestSequence
                || !PlayerTradeRequestSequence.IsCurrent(pendingClaim,
                    result.playerTradeOperationSessionId, result.clientRequestId))
                return;
            if (result.claim != null
                && !string.IsNullOrWhiteSpace(result.claim.claimId)
                && !string.Equals(result.claim.claimId, pendingClaim.claimId,
                    StringComparison.Ordinal))
                return;

            string claimId = pendingClaim.claimId;
            PlayerTradeRequestSequence.Complete(pendingClaim,
                result.playerTradeOperationSessionId, result.clientRequestId);
            pendingClaim = null;
            SetCardPending(claimId, false);
            Debug.Log("[PlayerTrade][Claim][RX] success=" + result.success
                + " status=" + (result.status ?? string.Empty)
                + " claimId=" + claimId
                + " requestSequence=" + result.requestSequence
                + " errorCode=" + (result.errorCode ?? string.Empty));

            if (!result.success && !string.Equals(result.status,
                    "succeeded_existing", StringComparison.OrdinalIgnoreCase))
            {
                GameNoticeService.Show("领取失败，请稍后重试");
                RequestFirstPage();
                return;
            }

            GameNoticeService.Show("领取成功");
            ApplyClaimInventoryResult(result);
            RequestFirstPage();
        }

        private void ApplyClaimInventoryResult(PlayerTradeClaimResultDto result)
        {
            InventoryModuleRuntime inventory =
                FindObjectOfType<InventoryModuleRuntime>();
            if (inventory == null)
            {
                Debug.LogWarning("[PlayerTrade][Claim] InventoryModuleRuntime is unavailable.");
                return;
            }

            PlayerTradeInventoryChangeDto[] changes = result.inventoryChanges;
            if ((changes == null || changes.Length == 0)
                && result.balance != null)
            {
                changes = new[]
                {
                    new PlayerTradeInventoryChangeDto
                    {
                        type = "wallet_set",
                        currencyId = result.balance.currencyId,
                        balance = result.balance.balance
                    }
                };
            }

            if (result.requiresSnapshot || (changes != null && changes.Length > 0))
            {
                bool applied = inventory.ApplyTradeInventoryChanges(
                    result.inventoryRevision, result.requiresSnapshot, changes);
                if (!applied)
                    Debug.LogWarning("[PlayerTrade][Claim] inventory delta was not applied; snapshot recovery requested.");
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
            if (changed != null && string.Equals(changed.eventType,
                "claim_created", StringComparison.OrdinalIgnoreCase))
                refreshQueued = true;
        }

        private void HandleWorldEntered(long epoch, long characterDbId)
        {
            pendingClaim = null;
            nextCursor = string.Empty;
            handledEventIds.Clear();
            ClearCards();
            RequestFirstPage();
        }

        private void HandleNetworkConnected()
        {
            RequestFirstPageIfWorldReady();
        }

        private void HandleNetworkDisconnected(string reason)
        {
            listRequestInFlight = false;
            pendingClaim = null;
            nextCursor = string.Empty;
            ClearCards();
        }

        private void TickClaimRequest()
        {
            if (pendingClaim == null
                || Time.unscaledTime - claimRequestStartedAt
                    < ClaimRequestTimeoutSeconds)
                return;

            if (!claimRequestRetried && networkManager != null
                && networkManager.IsConnected)
            {
                claimRequestRetried = true;
                claimRequestStartedAt = Time.unscaledTime;
                if (networkManager.Send(pendingClaim))
                {
                    Debug.Log("[PlayerTrade][Claim][RETRY] claimId="
                        + pendingClaim.claimId + " requestSequence="
                        + pendingClaim.requestSequence);
                    return;
                }
            }

            string claimId = pendingClaim.claimId;
            pendingClaim = null;
            SetCardPending(claimId, false);
            GameNoticeService.Show("领取结果未确认，请刷新待领取列表");
            Debug.LogWarning("[玩家交易] 领取结果超时，保留服务端待领取状态并刷新列表。",
                this);
            RequestFirstPage();
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

        private void RenderClaims(PlayerTradeClaimDto[] claims, bool replace)
        {
            if (replace)
            {
                ClearCards();
                ResetScrollPosition();
            }
            if (claims == null || claimPrefab == null || contentRoot == null)
                return;

            int firstIndex = activeCards.Count;
            for (int index = 0; index < claims.Length; index++)
            {
                if (claims[index] == null)
                    continue;
                PlayerTradeClaimCardView card =
                    Instantiate(claimPrefab, contentRoot, false);
                card.name = "待领取_" + (firstIndex + index).ToString("D2");
                card.Bind(claims[index], itemDefinitions, HandleClaimRequested);
                activeCards.Add(card);
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentRoot);
        }

        private void SetCardPending(string claimId, bool pending)
        {
            for (int index = 0; index < activeCards.Count; index++)
            {
                PlayerTradeClaimCardView card = activeCards[index];
                if (card != null && string.Equals(card.ClaimId, claimId,
                    StringComparison.Ordinal))
                {
                    card.SetClaimPending(pending);
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
            messageDispatcher.Register(PlayerTradeMessageIds.ClaimListResult,
                HandleClaimListResult);
            messageDispatcher.Register(PlayerTradeMessageIds.ClaimResult,
                HandleClaimResult);
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
                messageDispatcher.Unregister(PlayerTradeMessageIds.ClaimListResult,
                    HandleClaimListResult);
                messageDispatcher.Unregister(PlayerTradeMessageIds.ClaimResult,
                    HandleClaimResult);
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
