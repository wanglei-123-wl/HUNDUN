using System;
using System.Globalization;
using Game.Inventory;
using Game.Inventory.Model.V2;
using Game.Inventory.Presentation.V2;
using Game.Items;
using Game.PlayerTrade.Protocol;
using Game.UI.Notice;
using Game.UI.ItemDetail;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.PlayerTrade.Presentation
{
    [DisallowMultipleComponent]
    public sealed class PlayerTradeSellPanelController : MonoBehaviour
    {
        [Header("挂单面板")]
        [SerializeField, InspectorName("挂单面板")]
        private GameObject listingPanel;
        [SerializeField, InspectorName("关闭按钮")]
        private Button closeButton;
        [SerializeField, InspectorName("面板对象名称")]
        private string listingPanelName = "挂单面板";

        [Header("挂单物品")]
        [SerializeField, InspectorName("物品图标")]
        private Image selectedItemIcon;
        [SerializeField, InspectorName("拥有数量")]
        private TMP_Text ownedCountText;
        [SerializeField, InspectorName("可出售数量")]
        private TMP_Text sellableCountText;
        [SerializeField, InspectorName("出售数量输入框")]
        private TMP_InputField quantityInput;
        [SerializeField, InspectorName("单价输入框")]
        private TMP_InputField unitPriceInput;
        [SerializeField, InspectorName("总价")]
        private TMP_Text totalPriceText;
        [SerializeField, InspectorName("确定出售按钮")]
        private Button confirmCreateButton;
        [SerializeField, InspectorName("增加数量按钮")]
        private Button increaseQuantityButton;
        [SerializeField, InspectorName("减少数量按钮")]
        private Button decreaseQuantityButton;
        [SerializeField, InspectorName("最大数量按钮")]
        private Button maximumQuantityButton;
        [SerializeField, InspectorName("取消选择按钮")]
        private Button cancelSelectionButton;
        private ItemDetailHoverTrigger selectedItemDetailHoverTrigger;

        [Header("物品目录")]
        [SerializeField, InspectorName("物品总表")]
        private ItemDefinitionTable itemDefinitions;

        [Header("网络")]
        [SerializeField, InspectorName("网络管理器")]
        private NetworkManager networkManager;
        [SerializeField, InspectorName("消息分发器")]
        private NetMessageDispatcher messageDispatcher;

        [Header("输入拦截")]
        [SerializeField, InspectorName("打开时拦截底层操作")]
        private bool blockUnderlyingPanels = true;
        [SerializeField, InspectorName("按 Escape 关闭")]
        private bool closeWithEscape = true;

        private Button openButton;
        private GameObject inputBlocker;
        private Button inputBlockerButton;
        private PlayerTradeSellDropTarget dropTarget;
        private InventoryModuleRuntime inventoryRuntime;
        private GameSession gameSession;
        private PlayerTradeTabController tabController;
        private string selectedIdentity;
        private string selectedAssetKind;
        private long selectedStackId;
        private string selectedItemInstanceId;
        private int availableQuantity;
        private bool updatingInputs;
        private PlayerTradeCreateRequestDto pendingCreateRequest;
        private float createRequestStartedAt;
        private bool createRequestRetried;
        private bool subscribed;
        private static PlayerTradeSellPanelController activeController;
        private const float CreateRequestTimeoutSeconds = 10f;

        public bool IsOpen =>
            listingPanel != null && listingPanel.activeSelf;

        public static bool BlocksUnderlyingPanels =>
            activeController != null
            && activeController.IsOpen
            && activeController.blockUnderlyingPanels;

        private void Awake()
        {
            ResolveReferences();
            CreateInputBlocker();
            EnsureDropTarget();
            ClearSelection();
            SetPanelVisible(false);
        }

        private void OnEnable()
        {
            ResolveReferences();
            EnsureDropTarget();
            Subscribe();
        }

        private void Update()
        {
            if (closeWithEscape && IsOpen
                && Input.GetKeyDown(KeyCode.Escape))
                ClosePanel();

            TickCreateRequest();
        }

        private void OnDisable()
        {
            Unsubscribe();
            if (activeController == this)
                activeController = null;
        }

        private void OnDestroy()
        {
            Unsubscribe();
            if (activeController == this)
                activeController = null;
            ClearSelectedItemDetail();
            if (inputBlocker != null)
                Destroy(inputBlocker);
        }

        public void OpenPanel()
        {
            ResolveReferences();
            if (listingPanel == null)
            {
                Debug.LogWarning(
                    "[玩家交易] 找不到挂单面板，请将面板命名为“"
                    + listingPanelName + "”或拖到脚本字段。", this);
                return;
            }

            if (tabController == null)
                tabController = FindObjectOfType<PlayerTradeTabController>(true);
            if (tabController != null)
                tabController.ShowOwned();

            activeController = this;
            SetPanelVisible(true);
            PutInputBlockerBehindPanel();
            listingPanel.transform.SetAsLastSibling();
            Debug.Log("[玩家交易] 已打开挂单面板。", this);
            Debug.Log("[PlayerTrade][UI][SellPanel][Open] listingPanel="
                + DescribeObject(listingPanel)
                + " tabController=" + (tabController == null
                    ? "<null>" : tabController.name),
                this);
        }

        public void ClosePanel()
        {
            if (pendingCreateRequest != null)
                return;
            CloseForNavigation();
            Debug.Log("[PlayerTrade][UI][SellPanel][Close] listingPanel="
                + DescribeObject(listingPanel), this);
        }

        public void CloseForNavigation()
        {
            SetPanelVisible(false);
            if (activeController == this)
                activeController = null;
        }

        public void TogglePanel()
        {
            if (IsOpen)
                ClosePanel();
            else
                OpenPanel();
        }

        public bool HasSelectedItem =>
            !string.IsNullOrWhiteSpace(selectedIdentity);

        public string SelectedIdentity => selectedIdentity;
        public string SelectedAssetKind => selectedAssetKind;
        public long SelectedStackId => selectedStackId;
        public string SelectedItemInstanceId => selectedItemInstanceId;
        public int SelectedQuantity => ReadNonNegativeInt(quantityInput, 0);
        public long SelectedTotalPrice => CalculateTotalPrice(
            ReadPositiveLong(unitPriceInput), SelectedQuantity);

        public bool CanSellIdentity(string identity)
        {
            if (string.IsNullOrWhiteSpace(identity))
                return false;

            if (inventoryRuntime == null)
                inventoryRuntime = FindObjectOfType<InventoryModuleRuntime>();
            return TryFindSellableItem(
                inventoryRuntime == null ? null : inventoryRuntime.CurrentSnapshot,
                identity, out _, out _);
        }

        public void ClearSelection()
        {
            if (pendingCreateRequest != null)
                return;
            selectedIdentity = null;
            selectedAssetKind = null;
            selectedStackId = 0;
            selectedItemInstanceId = null;
            availableQuantity = 0;
            updatingInputs = true;
            ClearSelectedItemDetail();
            SetIcon(selectedItemIcon, null, false);
            SetText(ownedCountText, string.Empty);
            SetText(sellableCountText, string.Empty);
            SetInputText(quantityInput, string.Empty);
            SetInputText(unitPriceInput, string.Empty);
            SetText(totalPriceText, string.Empty);
            updatingInputs = false;
            RefreshConfirmInteractable();
        }

        private void ResolveReferences()
        {
            if (openButton == null)
                openButton = GetComponent<Button>();

            if (listingPanel == null)
                listingPanel = FindObjectByName(listingPanelName);

            if (closeButton == null && listingPanel != null)
            {
                Button[] buttons =
                    listingPanel.GetComponentsInChildren<Button>(true);
                for (int index = 0; index < buttons.Length; index++)
                {
                    Button candidate = buttons[index];
                    if (candidate != null
                        && candidate.name.Contains("关闭"))
                    {
                        closeButton = candidate;
                        break;
                    }
                }
            }

            if (selectedItemIcon == null)
                selectedItemIcon = FindChild<Image>(
                    "挂单面板图标", "挂单预制体图标", "物品图标",
                    "商城出售格子预制体物品图标");
            EnsureSelectedItemDetailTrigger();
            if (ownedCountText == null)
                ownedCountText = FindChild<TMP_Text>(
                "拥有数量", "拥有数量1", "拥有");
            if (sellableCountText == null)
                sellableCountText = FindChild<TMP_Text>(
                "可出售数量", "可出售数量1", "可出售", "可售");
            if (quantityInput == null)
                quantityInput = FindChild<TMP_InputField>(
                    "出售数量输入框", "数量输入框", "出售数量");
            if (quantityInput == null)
                quantityInput = EnsureInputField(FindChild<TMP_Text>(
                    "出售数量输入框", "数量输入框", "出售数量"));
            if (unitPriceInput == null)
                unitPriceInput = FindChild<TMP_InputField>(
                    "单价输入框", "单价");
            if (unitPriceInput == null)
                unitPriceInput = EnsureInputField(FindChild<TMP_Text>(
                    "单价输入框", "单价"));
            if (totalPriceText == null)
                totalPriceText = FindChild<TMP_Text>("总价");
            if (confirmCreateButton == null)
                confirmCreateButton = FindChild<Button>(
                    "确认出售按钮", "确定出售按钮", "确定按钮", "上架按钮");
            if (increaseQuantityButton == null)
                increaseQuantityButton = FindChild<Button>(
                    "增加数量按钮", "数量增加按钮", "加号按钮", "加");
            if (decreaseQuantityButton == null)
                decreaseQuantityButton = FindChild<Button>(
                    "减少数量按钮", "数量减少按钮", "减号按钮", "减");
            if (maximumQuantityButton == null)
                maximumQuantityButton = FindChild<Button>(
                    "最大数量按钮", "全部数量按钮");
            if (cancelSelectionButton == null)
                cancelSelectionButton = FindChild<Button>(
                    "取消选择按钮", "取消按钮");
            if (inventoryRuntime == null)
                inventoryRuntime = FindObjectOfType<InventoryModuleRuntime>();
            if (gameSession == null)
                gameSession = FindObjectOfType<GameSession>();
            if (tabController == null)
                tabController = FindObjectOfType<PlayerTradeTabController>(true);
            if (networkManager == null)
                networkManager = NetworkManager.Instance;
            if (messageDispatcher == null)
                messageDispatcher = NetMessageDispatcher.Instance;
        }

        private static TMP_InputField EnsureInputField(TMP_Text text)
        {
            if (text == null)
                return null;

            TMP_InputField input = text.GetComponent<TMP_InputField>();
            if (input == null)
                input = text.gameObject.AddComponent<TMP_InputField>();

            input.textComponent = text;
            input.targetGraphic = text;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.contentType = TMP_InputField.ContentType.IntegerNumber;
            input.SetTextWithoutNotify(text.text ?? string.Empty);
            return input;
        }

        private GameObject FindObjectByName(string objectName)
        {
            if (string.IsNullOrWhiteSpace(objectName))
                return null;

            Canvas canvas = GetComponentInParent<Canvas>(true);
            Transform root = canvas != null
                ? canvas.rootCanvas.transform
                : transform.root;
            Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
            for (int index = 0; index < transforms.Length; index++)
            {
                Transform candidate = transforms[index];
                if (candidate != null
                    && string.Equals(candidate.name, objectName,
                        System.StringComparison.Ordinal))
                    return candidate.gameObject;
            }

            return null;
        }

        private void CreateInputBlocker()
        {
            if (!blockUnderlyingPanels || inputBlocker != null)
                return;

            Transform parent = listingPanel != null
                ? listingPanel.transform.parent
                : transform.parent;
            if (parent == null)
                return;

            inputBlocker = new GameObject(
                "挂单面板输入拦截层",
                typeof(RectTransform),
                typeof(Image),
                typeof(Button));
            RectTransform blockerRect =
                inputBlocker.GetComponent<RectTransform>();
            blockerRect.SetParent(parent, false);
            blockerRect.anchorMin = Vector2.zero;
            blockerRect.anchorMax = Vector2.one;
            blockerRect.offsetMin = Vector2.zero;
            blockerRect.offsetMax = Vector2.zero;
            blockerRect.localScale = Vector3.one;

            Image blockerImage = inputBlocker.GetComponent<Image>();
            blockerImage.color = new Color(0f, 0f, 0f, 0f);
            blockerImage.raycastTarget = true;

            inputBlockerButton = inputBlocker.GetComponent<Button>();
            inputBlockerButton.transition = Selectable.Transition.None;
            inputBlockerButton.onClick.AddListener(
                HandleInputBlockerClicked);
            inputBlocker.SetActive(false);
        }

        private void EnsureDropTarget()
        {
            if (listingPanel == null)
                return;

            dropTarget = listingPanel.GetComponent<PlayerTradeSellDropTarget>();
            if (dropTarget == null)
                dropTarget = listingPanel.AddComponent<PlayerTradeSellDropTarget>();
            dropTarget.SetDropHandler(HandleItemDropped);
        }

        private void HandleInputBlockerClicked()
        {
            // 只拦截底层点击，不把点击穿透到市场面板。
        }

        private void PutInputBlockerBehindPanel()
        {
            if (inputBlocker == null)
                return;

            inputBlocker.SetActive(true);
            inputBlocker.transform.SetAsLastSibling();
            if (tabController == null)
                tabController = FindObjectOfType<PlayerTradeTabController>(true);
            if (tabController != null)
                tabController.BringNavigationToFront(inputBlocker.transform.parent);
        }

        private void SetPanelVisible(bool visible)
        {
            if (inputBlocker != null)
                inputBlocker.SetActive(visible && blockUnderlyingPanels);
            if (listingPanel != null)
                listingPanel.SetActive(visible);
        }

        private void Subscribe()
        {
            if (subscribed)
                return;

            if (openButton != null)
                openButton.onClick.AddListener(OpenPanel);
            if (closeButton != null)
                closeButton.onClick.AddListener(ClosePanel);
            if (confirmCreateButton != null)
                confirmCreateButton.onClick.AddListener(HandleCreateClicked);
            if (increaseQuantityButton != null)
                increaseQuantityButton.onClick.AddListener(IncreaseQuantity);
            if (decreaseQuantityButton != null)
                decreaseQuantityButton.onClick.AddListener(DecreaseQuantity);
            if (maximumQuantityButton != null)
                maximumQuantityButton.onClick.AddListener(SetMaximumQuantity);
            if (cancelSelectionButton != null)
                cancelSelectionButton.onClick.AddListener(ClearSelection);
            if (quantityInput != null)
            {
                quantityInput.contentType = TMP_InputField.ContentType.IntegerNumber;
                quantityInput.onValueChanged.AddListener(
                    HandleQuantityInputChanged);
            }
            if (unitPriceInput != null)
            {
                unitPriceInput.contentType = TMP_InputField.ContentType.IntegerNumber;
                unitPriceInput.onValueChanged.AddListener(
                    HandleUnitPriceInputChanged);
            }
            if (messageDispatcher != null)
                messageDispatcher.Register(
                    PlayerTradeMessageIds.CreateResult,
                    HandleCreateResult);
            if (gameSession != null)
                gameSession.LocalWorldEntered += HandleWorldEntered;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed)
                return;

            if (openButton != null)
                openButton.onClick.RemoveListener(OpenPanel);
            if (closeButton != null)
                closeButton.onClick.RemoveListener(ClosePanel);
            if (confirmCreateButton != null)
                confirmCreateButton.onClick.RemoveListener(HandleCreateClicked);
            if (increaseQuantityButton != null)
                increaseQuantityButton.onClick.RemoveListener(IncreaseQuantity);
            if (decreaseQuantityButton != null)
                decreaseQuantityButton.onClick.RemoveListener(DecreaseQuantity);
            if (maximumQuantityButton != null)
                maximumQuantityButton.onClick.RemoveListener(SetMaximumQuantity);
            if (cancelSelectionButton != null)
                cancelSelectionButton.onClick.RemoveListener(ClearSelection);
            if (quantityInput != null)
            {
                quantityInput.onValueChanged.RemoveListener(
                    HandleQuantityInputChanged);
            }
            if (unitPriceInput != null)
            {
                unitPriceInput.onValueChanged.RemoveListener(
                    HandleUnitPriceInputChanged);
            }
            if (messageDispatcher != null)
                messageDispatcher.Unregister(
                    PlayerTradeMessageIds.CreateResult,
                    HandleCreateResult);
            if (gameSession != null)
                gameSession.LocalWorldEntered -= HandleWorldEntered;
            if (inputBlockerButton != null)
                inputBlockerButton.onClick.RemoveListener(
                    HandleInputBlockerClicked);
            subscribed = false;
        }

        private void HandleCreateClicked()
        {
            if (pendingCreateRequest != null || !HasSelectedItem)
                return;
            if (networkManager == null)
                networkManager = NetworkManager.Instance;
            if (networkManager == null || !networkManager.IsConnected)
            {
                GameNoticeService.Show("交易暂不可用，请检查网络连接");
                Debug.LogWarning("[玩家交易] 当前未连接服务器，无法创建挂单。",
                    this);
                return;
            }

            int quantity = SelectedQuantity;
            long totalPrice = SelectedTotalPrice;
            if (quantity <= 0 || quantity > availableQuantity)
            {
                GameNoticeService.Show("出售数量无效");
                Debug.LogWarning("[玩家交易] 出售数量超出当前背包数量。",
                    this);
                return;
            }
            if (totalPrice <= 0 || totalPrice > 999999999L)
            {
                GameNoticeService.Show("总价无效");
                Debug.LogWarning("[玩家交易] 总价必须在服务端允许范围内。",
                    this);
                return;
            }

            PlayerTradeCreateRequestDto request =
                new PlayerTradeCreateRequestDto
                {
                    visibility = "public",
                    targetNickname = string.Empty,
                    assetKind = selectedAssetKind,
                    stackId = selectedStackId,
                    itemInstanceId = selectedItemInstanceId ?? string.Empty,
                    quantity = quantity,
                    totalPrice = totalPrice
                };
            if (!PlayerTradeRequestSequence.TryPrepare(request))
            {
                GameNoticeService.Show("交易请求处理中，请稍后");
                return;
            }
            if (!networkManager.Send(request))
            {
                GameNoticeService.Show("挂单请求发送失败，请稍后重试");
                Debug.LogWarning("[玩家交易] 创建挂单请求发送失败。",
                    this);
                return;
            }

            pendingCreateRequest = request;
            createRequestStartedAt = Time.unscaledTime;
            createRequestRetried = false;
            if (confirmCreateButton != null)
                confirmCreateButton.interactable = false;
            GameNoticeService.Show("正在创建挂单...");
            Debug.Log("[PlayerTrade][Create][TX] requestSequence="
                + request.requestSequence
                + " assetKind=" + request.assetKind
                + " stackId=" + request.stackId
                + " itemInstanceId=" + request.itemInstanceId
                + " quantity=" + request.quantity
                + " totalPrice=" + request.totalPrice);
        }

        private void HandleWorldEntered(long epoch, long characterDbId)
        {
            pendingCreateRequest = null;
        }

        private void TickCreateRequest()
        {
            if (pendingCreateRequest == null
                || Time.unscaledTime - createRequestStartedAt
                    < CreateRequestTimeoutSeconds)
                return;

            if (!createRequestRetried && networkManager != null
                && networkManager.IsConnected)
            {
                createRequestRetried = true;
                createRequestStartedAt = Time.unscaledTime;
                if (networkManager.Send(pendingCreateRequest))
                {
                    Debug.Log("[PlayerTrade][Create][RETRY] requestSequence="
                        + pendingCreateRequest.requestSequence);
                    return;
                }
            }

            Debug.LogWarning("[玩家交易] 创建挂单结果超时，保留当前选择并请求背包校准。",
                this);
            pendingCreateRequest = null;
            if (confirmCreateButton != null)
                confirmCreateButton.interactable = true;
            RefreshConfirmInteractable();
            GameNoticeService.Show("挂单结果未确认，请刷新背包和市场");
            if (inventoryRuntime == null)
                inventoryRuntime = FindObjectOfType<InventoryModuleRuntime>();
            inventoryRuntime?.RequestReconciliation();
        }

        private void HandleCreateResult(string json)
        {
            PlayerTradeCreateResultDto result;
            try
            {
                result = JsonUtility.FromJson<PlayerTradeCreateResultDto>(json);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[PlayerTrade][Create][RX] parse failed: "
                    + exception.Message);
                GameNoticeService.Show("挂单结果异常，请稍后重试");
                return;
            }

            if (pendingCreateRequest == null || result == null
                || result.requestSequence != pendingCreateRequest.requestSequence
                || !PlayerTradeRequestSequence.IsCurrent(pendingCreateRequest,
                    result.playerTradeOperationSessionId, result.clientRequestId))
                return;

            PlayerTradeRequestSequence.Complete(pendingCreateRequest,
                result.playerTradeOperationSessionId, result.clientRequestId);
            pendingCreateRequest = null;
            if (confirmCreateButton != null)
                confirmCreateButton.interactable = true;
            Debug.Log("[PlayerTrade][Create][RX] success=" + result.success
                + " status=" + (result.status ?? string.Empty)
                + " requestSequence=" + result.requestSequence
                + " errorCode=" + (result.errorCode ?? string.Empty)
                + " inventoryRevision=" + result.inventoryRevision);

            if (!result.success && !string.Equals(result.status,
                    "succeeded_existing", StringComparison.OrdinalIgnoreCase))
            {
                GameNoticeService.Show("挂单失败，请稍后重试");
                return;
            }

            GameNoticeService.Show("挂单成功");
            if (inventoryRuntime == null)
                inventoryRuntime = FindObjectOfType<InventoryModuleRuntime>();
            bool applied = inventoryRuntime != null
                && inventoryRuntime.ApplyTradeInventoryChanges(
                    result.inventoryRevision, result.requiresSnapshot,
                    result.inventoryChanges);
            if (!applied)
            {
                Debug.LogWarning("[玩家交易] 挂单成功，但本地库存需要快照恢复。",
                    this);
            }

            ClearSelection();
            PlayerTradeMarketListView marketList =
                FindObjectOfType<PlayerTradeMarketListView>(true);
            marketList?.RefreshMarket();
            PlayerTradeOwnedListView ownedList =
                FindObjectOfType<PlayerTradeOwnedListView>(true);
            ownedList?.RefreshOwned();
            Debug.Log("[PlayerTrade][UI][Create][UIRefresh] panel="
                + DescribeObject(listingPanel)
                + " market="
                + (marketList == null ? "<null>" : marketList.name)
                + " owned=" + (ownedList == null ? "<null>" : ownedList.name),
                this);
        }

        private void HandleItemDropped(InventoryBagDragSlot source)
        {
            if (source == null)
                return;

            TrySelectIdentity(source.DragIdentity);
        }

        public bool TrySelectIdentity(string identity)
        {
            if (!IsOpen || string.IsNullOrWhiteSpace(identity)
                || pendingCreateRequest != null)
                return false;

            if (HasSelectedItem)
            {
                Debug.LogWarning("[玩家交易] 挂单面板已经选择了一个物品，请先取消当前选择。",
                    this);
                return false;
            }

            if (inventoryRuntime == null)
                inventoryRuntime = FindObjectOfType<InventoryModuleRuntime>();
            InventoryOwnershipSnapshot snapshot =
                inventoryRuntime == null ? null : inventoryRuntime.CurrentSnapshot;
            if (snapshot == null || !TryBindIdentity(snapshot, identity))
            {
                Debug.LogWarning("[玩家交易] 所选物品不是可用于挂单的背包物品。",
                    this);
                return false;
            }

            Debug.Log("[玩家交易] 已选择挂单物品 identity=" + selectedIdentity,
                this);
            return true;
        }

        private bool TryBindIdentity(
            InventoryOwnershipSnapshot snapshot,
            string identity)
        {
            if (!TryFindSellableItem(snapshot, identity,
                    out InventoryStackState stack,
                    out InventoryEquipmentState equipment))
                return false;

            if (stack != null)
            {
                ItemDefinition definition = FindItemDefinition(stack.ItemId);
                if (definition == null)
                    Debug.LogWarning("[玩家交易] 找不到物品目录配置: "
                        + stack.ItemId, this);
                selectedIdentity = identity;
                selectedAssetKind = "stack";
                selectedStackId = stack.StackId;
                selectedItemInstanceId = string.Empty;
                availableQuantity = stack.Count;
                ApplySelection(definition, stack.Count, stack.Count);
                return true;
            }

            ItemDefinition equipmentDefinition =
                FindEquipmentDefinition(equipment.EquipmentId);
            if (equipmentDefinition == null)
                Debug.LogWarning("[玩家交易] 找不到装备目录配置: "
                    + equipment.EquipmentId, this);
            selectedIdentity = identity;
            selectedAssetKind = "equipment";
            selectedStackId = 0;
            selectedItemInstanceId = equipment.ItemInstanceId;
            availableQuantity = 1;
            CountEquipmentAvailability(snapshot, equipment.EquipmentId,
                out int ownedCount, out int sellableCount);
            ApplySelection(equipmentDefinition, ownedCount, sellableCount);
            return true;
        }

        private static bool TryFindSellableItem(
            InventoryOwnershipSnapshot snapshot,
            string identity,
            out InventoryStackState matchedStack,
            out InventoryEquipmentState matchedEquipment)
        {
            matchedStack = null;
            matchedEquipment = null;
            if (snapshot == null || string.IsNullOrWhiteSpace(identity))
                return false;

            if (identity.StartsWith("s:", StringComparison.Ordinal)
                && long.TryParse(identity.Substring(2),
                    NumberStyles.Integer, CultureInfo.InvariantCulture,
                    out long stackId))
            {
                foreach (InventoryStackState stack in snapshot.Stacks)
                {
                    if (stack != null && stack.StackId == stackId
                        && stack.Count > 0 && !stack.IsBound)
                    {
                        matchedStack = stack;
                        return true;
                    }
                }
                return false;
            }

            if (!identity.StartsWith("e:", StringComparison.Ordinal))
                return false;

            string instanceId = identity.Substring(2);
            foreach (InventoryEquipmentState equipment in snapshot.Equipment)
            {
                if (equipment != null
                    && string.Equals(equipment.ItemInstanceId, instanceId,
                        StringComparison.Ordinal)
                    && equipment.SlotCode == 0 && !equipment.IsBound)
                {
                    matchedEquipment = equipment;
                    return true;
                }
            }
            return false;
        }

        private static void CountEquipmentAvailability(
            InventoryOwnershipSnapshot snapshot,
            string equipmentId,
            out int ownedCount,
            out int sellableCount)
        {
            ownedCount = 0;
            sellableCount = 0;
            if (snapshot == null || string.IsNullOrWhiteSpace(equipmentId))
                return;

            foreach (InventoryEquipmentState equipment in snapshot.Equipment)
            {
                if (equipment == null
                    || !string.Equals(equipment.EquipmentId, equipmentId,
                        StringComparison.Ordinal))
                    continue;

                ownedCount++;
                if (equipment.SlotCode == 0 && !equipment.IsBound)
                    sellableCount++;
            }
        }

        private void ApplySelection(
            ItemDefinition definition,
            int ownedCount,
            int sellableCount)
        {
            updatingInputs = true;
            BindSelectedItemDetail(definition);
            SetIcon(selectedItemIcon,
                definition == null ? null : definition.Icon,
                definition != null && definition.Icon != null);
            SetText(ownedCountText, ownedCount.ToString(
                CultureInfo.InvariantCulture));
            SetText(sellableCountText, sellableCount.ToString(
                CultureInfo.InvariantCulture));
            SetInputText(quantityInput, "0");
            SetText(totalPriceText, string.Empty);
            updatingInputs = false;
            RecalculateTotal();
        }

        private void IncreaseQuantity()
        {
            SetQuantity(ReadNonNegativeInt(quantityInput, 0) + 1);
        }

        private void DecreaseQuantity()
        {
            SetQuantity(ReadNonNegativeInt(quantityInput, 0) - 1);
        }

        private void SetMaximumQuantity()
        {
            SetQuantity(availableQuantity);
        }

        private void HandleQuantityInputChanged(string _)
        {
            if (!updatingInputs)
                SetQuantity(ReadNonNegativeInt(quantityInput, 0));
        }

        private void HandleUnitPriceInputChanged(string _)
        {
            if (!updatingInputs)
                RecalculateTotal();
        }

        private void SetQuantity(int quantity)
        {
            if (!HasSelectedItem)
                return;
            quantity = Mathf.Clamp(quantity, 0, Math.Max(0, availableQuantity));
            updatingInputs = true;
            SetInputText(quantityInput, quantity.ToString(
                CultureInfo.InvariantCulture));
            updatingInputs = false;
            RecalculateTotal();
        }

        private void RecalculateTotal()
        {
            long unitPrice = ReadPositiveLong(unitPriceInput);
            int quantity = ReadNonNegativeInt(quantityInput, 0);
            long total = CalculateTotalPrice(unitPrice, quantity);
            SetText(totalPriceText, total > 0
                ? total.ToString(CultureInfo.InvariantCulture)
                : string.Empty);
            RefreshConfirmInteractable();
        }

        private void RefreshConfirmInteractable()
        {
            if (confirmCreateButton == null)
                return;

            bool valid = pendingCreateRequest == null
                && PlayerTradeRequestSequence.CanStart
                && HasSelectedItem
                && SelectedQuantity > 0
                && SelectedQuantity <= availableQuantity
                && SelectedTotalPrice > 0
                && SelectedTotalPrice <= 999999999L;
            confirmCreateButton.interactable = valid;
        }

        private static long CalculateTotalPrice(long unitPrice, int quantity)
        {
            if (unitPrice <= 0 || quantity <= 0)
                return 0;
            return unitPrice > long.MaxValue / quantity
                ? long.MaxValue
                : unitPrice * quantity;
        }

        private static string DescribeObject(GameObject target)
        {
            if (target == null)
                return "<null>";
            return target.name + "(active=" + target.activeSelf
                + ",id=" + target.GetInstanceID() + ")";
        }

        private ItemDefinition FindItemDefinition(string itemId)
        {
            return itemDefinitions != null
                && itemDefinitions.TryGet(itemId, out ItemDefinition definition)
                ? definition : null;
        }

        private ItemDefinition FindEquipmentDefinition(string equipmentId)
        {
            return itemDefinitions != null
                && itemDefinitions.TryGetByEquipmentId(equipmentId,
                    out ItemDefinition definition)
                ? definition : null;
        }

        private T FindChild<T>(params string[] names) where T : Component
        {
            if (listingPanel == null || names == null)
                return null;
            T[] components = listingPanel.GetComponentsInChildren<T>(true);
            for (int index = 0; index < components.Length; index++)
            {
                Component component = components[index];
                if (component == null)
                    continue;
                for (int nameIndex = 0; nameIndex < names.Length; nameIndex++)
                {
                    if (string.Equals(component.gameObject.name, names[nameIndex],
                            StringComparison.Ordinal))
                        return (T)component;
                }
            }
            return null;
        }

        private static void SetIcon(Image image, Sprite sprite, bool visible)
        {
            if (image == null)
                return;
            image.sprite = sprite;
            image.gameObject.SetActive(visible);
        }

        private void EnsureSelectedItemDetailTrigger()
        {
            if (selectedItemIcon == null)
            {
                selectedItemDetailHoverTrigger = null;
                return;
            }

            if (selectedItemDetailHoverTrigger == null)
            {
                selectedItemDetailHoverTrigger =
                    selectedItemIcon.GetComponent<ItemDetailHoverTrigger>();
                if (selectedItemDetailHoverTrigger == null
                    && ItemDetailHoverService.IsConfiguredInScene(this))
                {
                    selectedItemDetailHoverTrigger =
                        selectedItemIcon.gameObject.AddComponent<
                            ItemDetailHoverTrigger>();
                }
            }

            selectedItemIcon.raycastTarget =
                selectedItemDetailHoverTrigger != null;
        }

        private void BindSelectedItemDetail(ItemDefinition definition)
        {
            EnsureSelectedItemDetailTrigger();
            if (selectedItemDetailHoverTrigger == null)
                return;

            ItemDetailData detail =
                ItemDetailDataResolver.FromItem(definition);
            if (detail == null || !detail.IsValid)
                selectedItemDetailHoverTrigger.ClearBinding();
            else
                selectedItemDetailHoverTrigger.Bind(detail);
        }

        private void ClearSelectedItemDetail()
        {
            if (selectedItemDetailHoverTrigger != null)
                selectedItemDetailHoverTrigger.ClearBinding();
        }

        private static void SetText(TMP_Text text, string value)
        {
            if (text != null)
                text.text = value ?? string.Empty;
        }

        private static void SetInputText(TMP_InputField input, string value)
        {
            if (input != null)
                input.SetTextWithoutNotify(value ?? string.Empty);
        }

        private static int ReadNonNegativeInt(
            TMP_InputField input,
            int fallback)
        {
            if (input == null
                || !int.TryParse(input.text, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int value))
                return fallback;
            return Math.Max(0, value);
        }

        private static long ReadPositiveLong(TMP_InputField input)
        {
            if (input == null
                || !long.TryParse(input.text, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out long value))
                return 0;
            return Math.Max(0L, value);
        }
    }
}
