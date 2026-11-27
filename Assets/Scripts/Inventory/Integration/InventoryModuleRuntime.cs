using Game.Items;
using Game.Client.BattleSync;
using Game.Client.BattleSync.Protocol;
using NewInventorySnapshot = Game.Client.BattleSync.Protocol.InventorySnapshotResultMessageDto;
using Game.Inventory.Integration.V2;
using Game.Inventory.Model.V2;
using Game.Inventory.Presentation.V2;
using Game.Inventory.Protocol.V2;
using Game.PlayerTrade.Protocol;
using Game.UI.Notice;
using UnityEngine;

namespace Game.Inventory
{
    [DefaultExecutionOrder(50)]
    [DisallowMultipleComponent]
    public sealed class InventoryModuleRuntime : MonoBehaviour, IInventoryBattleSyncModule, IBattleSyncSnapshotRoutingControl
    {
        [Header("Initialization")]
        [SerializeField] private bool initializeAutomatically = true;
        [SerializeField] private bool hidePanelOnStart = true;

        [Header("References")]
        [SerializeField] private NetworkManager networkManager;
        [SerializeField] private NetMessageDispatcher messageDispatcher;
        [SerializeField] private InventoryPanelView panelView;
        [SerializeField] private ItemDefinitionTable itemDefinitions;
        [SerializeField] private Transform equipmentSlotsRoot;

        [Header("Input")]
        [SerializeField] private bool enableKeyboardToggle = true;
        [SerializeField] private KeyCode toggleKey = KeyCode.B;

        private InventoryV2Session v2Session;
        private QuickbarSession quickbarSession;
        private bool networkEventsSubscribed;
        private bool missingDependenciesLogged;
        private long activeConnectionEpoch;

        public bool IsOpen => v2Session != null && v2Session.IsOpen;
        public InventoryOwnershipSnapshot CurrentSnapshot =>
            v2Session == null ? null : v2Session.CurrentSnapshot;
        public QuickbarSession Quickbar => quickbarSession;
        public ItemDefinitionTable ItemDefinitions => itemDefinitions;
        public event System.Action InventoryMismatchDetected;

        private void Awake()
        {
            if (initializeAutomatically)
                TryInitialize();
        }

        private void Start()
        {
            ResolveReferences();

            if (hidePanelOnStart && panelView != null)
            {
                panelView.Hide();
            }

            if (initializeAutomatically)
            {
                TryInitialize();
            }
        }

        private void Update()
        {
            v2Session?.Tick();
            quickbarSession?.Tick();
            if (v2Session == null && initializeAutomatically)
            {
                TryInitialize();
            }

            if (enableKeyboardToggle
                && v2Session != null
                && Input.GetKeyDown(toggleKey)
                && !GameplayUiInputGuard.BlocksWindowShortcuts)
            {
                Toggle();
            }
        }

        private void OnDestroy()
        {
            Shutdown();
        }

        public bool TryInitialize()
        {
            if (v2Session != null)
            {
                return true;
            }

            ResolveReferences();
            if (!HasRequiredReferences())
            {
                if (!missingDependenciesLogged)
                {
                    missingDependenciesLogged = true;
                    Debug.LogWarning(
                        "InventoryModuleRuntime is waiting for NetworkManager, "
                        + "NetMessageDispatcher, InventoryPanelView and "
                        + "ItemDefinitionTable.",
                        this);
                }

                return false;
            }

            v2Session = new InventoryV2Session(networkManager, messageDispatcher,
                panelView, itemDefinitions, equipmentSlotsRoot,
                () => FindObjectOfType<GameSession>()?.CurrentClientTick
                    ?? System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                CurrentLocalCombatRevision, ApplyItemUseHealth,
                ApplyItemUseMana,
                () =>
                {
                    GameSession session = FindObjectOfType<GameSession>();
                    session?.QueueAttributeSnapshotRefresh();
                    session?.QueueProgressionSnapshotRefresh();
                },
                message => GameNoticeService.Show(message),
                ApplyItemUseJingjieBalance,
                () => FindObjectOfType<GameSession>()?.LocalProgressionSnapshot != null,
                panelView.GetComponent<InventorySelectedItemDetailPanel>(),
                stackId => FindObjectOfType<GameSession>()?.TryLearnSkill(stackId) == true,
                PlayLocalPotionAnimation, ApplyEquipmentStats);
            quickbarSession = new QuickbarSession(networkManager, messageDispatcher,
                () => v2Session?.CurrentSnapshot,
                () => v2Session?.NotifyAuthoritativeInventoryChanged(),
                itemDefinitions, itemId => v2Session != null
                    && v2Session.CanUseItem(itemId));
            quickbarSession.OperationFailed += HandleQuickbarOperationFailed;
            SubscribeNetworkEvents();
            missingDependenciesLogged = false;
            return true;
        }

        private static void PlayLocalPotionAnimation()
        {
            LocalPlayerProvider provider = LocalPlayerProvider.Instance;
            if (provider != null && provider.PlayerTransform != null)
            {
                provider.PlayerTransform.GetComponent<LocalPlayerInputController>()
                    ?.PlayPotionAnimation();
            }
        }

        public void SetSnapshotRoutingMode(BattleSyncSnapshotRoutingMode mode)
        {
            // V2 subscribes independently of the battle snapshot batch.
        }

        public BattleSyncApplyResult BeginSession(BattleSyncModuleSessionContext context)
        {
            if (!context.IsValid || !TryInitialize()) return BattleSyncApplyResult.RetryableFailure;
            activeConnectionEpoch = context.ConnectionEpoch;
            GameSession session = FindObjectOfType<GameSession>();
            v2Session.BeginSession(context.CharacterDbId, context.SceneId,
                session != null && session.LocalCharacterDbId == context.CharacterDbId
                    ? session.EquipmentOperationSessionId : null,
                session != null && session.LocalCharacterDbId == context.CharacterDbId
                    ? session.WorldDropOperationSessionId : null,
                session != null && session.LocalCharacterDbId == context.CharacterDbId
                    ? session.ItemUseOperationSessionId : null);
            quickbarSession?.BeginSession(context.CharacterDbId,
                session != null && session.LocalCharacterDbId == context.CharacterDbId
                    ? session.QuickbarOperationSessionId : null,
                session != null ? session.QuickbarProtocolVersion : 0,
                session != null ? session.QuickbarSlotCount : 0);
            return BattleSyncApplyResult.Applied;
        }

        public void EndSession(long connectionEpoch)
        {
            if (connectionEpoch != activeConnectionEpoch) return;
            activeConnectionEpoch = 0;
            v2Session?.Reset();
            quickbarSession?.Reset();
        }

        public BattleSyncApplyResult ApplyInventorySnapshot(NewInventorySnapshot message)
        {
            return BattleSyncApplyResult.InvalidMessage;
        }

        public void Open()
        {
            if (TryInitialize())
                v2Session.Open();
        }

        public void Close()
        {
            v2Session?.Close();
        }

        public void Toggle()
        {
            if (TryInitialize())
            {
                if (v2Session.IsOpen) v2Session.Close();
                else v2Session.Open();
            }
        }

        internal void ApplyTeleportStoneUse(TeleportStoneUseReceipt result)
        {
            if (result == null || !TryInitialize()) return;
            var change = new InventoryV2StackChangeDto
            {
                stackId = result.stackId,
                itemId = result.itemId,
                isBound = result.isBound,
                previousCount = 1,
                newCount = result.remainingUses == 0 ? 0 : 1,
                remainingUses = result.remainingUses
            };
            v2Session.ApplyPickupDelta(result.inventoryRevision, new[] { change },
                System.Array.Empty<InventoryV2EquipmentDto>(), System.Array.Empty<InventoryV2WalletChangeDto>());
        }

        public void RequestSnapshot()
        {
            if (TryInitialize())
                v2Session.RequestSnapshot();
        }

        public void RequestReconciliation()
        {
            if (TryInitialize())
                v2Session.NotifyAuthoritativeInventoryChanged();
        }

        public bool TryGetItemCount(string itemId, out long count)
        {
            count = 0;
            InventoryOwnershipSnapshot ownership = CurrentSnapshot;
            return ownership != null && ownership.TryGetItemCount(itemId, out count);
        }

        public bool ApplyJingjieBreakthroughInventoryChanges(
            long inventoryRevision, bool requiresInventorySnapshot,
            InventoryV2StackChangeDto[] stackItemChanges)
        {
            if (!TryInitialize())
                return false;
            bool applied = v2Session.ApplyJingjieBreakthroughInventoryChanges(
                inventoryRevision, requiresInventorySnapshot,
                stackItemChanges, out string error);
            if (!applied)
                Debug.LogWarning("[Jingjie] inventory changes require snapshot: "
                    + error);
            return applied;
        }

        public bool ApplyRefiningInventoryChanges(long inventoryRevision,
            InventoryV2StackChangeDto[] stackItemChanges)
        {
            if (!TryInitialize())
                return false;
            bool applied = v2Session.ApplyJingjieBreakthroughInventoryChanges(
                inventoryRevision, false, stackItemChanges, out string error,
                "refining_start_result");
            if (!applied)
                Debug.LogWarning("[Refining] inventory changes require snapshot: "
                    + error);
            return applied;
        }

        public bool ApplyAlchemyInventoryChanges(long inventoryRevision,
            InventoryV2StackChangeDto[] stackItemChanges)
        {
            if (!TryInitialize())
                return false;
            bool applied = v2Session.ApplyJingjieBreakthroughInventoryChanges(
                inventoryRevision, false, stackItemChanges, out string error,
                "alchemy_start_result");
            if (!applied)
                Debug.LogWarning("[Alchemy] inventory changes require snapshot: "
                    + error);
            return applied;
        }

        public bool ApplyForgingStartInventoryChanges(long inventoryRevision,
            InventoryV2StackChangeDto[] stackItemChanges,
            InventoryV2WalletDto[] wallets)
        {
            if (!TryInitialize())
                return false;
            bool applied = v2Session.ApplyJingjieBreakthroughInventoryChanges(
                inventoryRevision, false, stackItemChanges, out string error,
                "forging_start_result", false, wallets);
            if (!applied)
                Debug.LogWarning("[Forging] inventory changes require snapshot: "
                    + error);
            return applied;
        }

        public bool ApplyForgingLearnInventoryChanges(long inventoryRevision,
            InventoryV2StackChangeDto[] stackItemChanges)
        {
            if (!TryInitialize())
                return false;
            bool applied = v2Session.ApplyJingjieBreakthroughInventoryChanges(
                inventoryRevision, false, stackItemChanges, out string error,
                "forging_learn_result");
            if (!applied)
                Debug.LogWarning("[Forging] learn inventory requires snapshot: "
                    + error);
            return applied;
        }

        public bool ApplyForgingClaimInventoryChanges(long inventoryRevision,
            InventoryV2StackChangeDto[] stackItemChanges,
            Game.Forging.Protocol.锻造装备变化[] equipmentItems,
            InventoryV2WalletDto[] wallets)
        {
            if (!TryInitialize())
                return false;
            bool applied = v2Session.ApplyForgingClaimInventoryChanges(
                inventoryRevision, stackItemChanges, equipmentItems,
                wallets, out string error);
            if (!applied)
                Debug.LogWarning("[Forging] claim inventory requires snapshot: "
                    + error);
            return applied;
        }

        public bool ApplyAlchemyLearnInventoryChanges(long inventoryRevision,
            InventoryV2StackChangeDto[] stackItemChanges)
        {
            if (!TryInitialize())
                return false;
            bool applied = v2Session.ApplyJingjieBreakthroughInventoryChanges(
                inventoryRevision, false, stackItemChanges, out string error,
                "alchemy_learn_result");
            if (!applied)
                Debug.LogWarning("[Alchemy] learn inventory requires snapshot: "
                    + error);
            return applied;
        }

        public bool ApplyAlchemyClaimInventoryChanges(long inventoryRevision,
            InventoryV2StackChangeDto[] stackItemChanges)
        {
            if (!TryInitialize())
                return false;
            bool applied = v2Session.ApplyJingjieBreakthroughInventoryChanges(
                inventoryRevision, false, stackItemChanges, out string error,
                "alchemy_claim_result", true);
            if (!applied)
                Debug.LogWarning("[Alchemy] claim inventory requires snapshot: "
                    + error);
            return applied;
        }

        public bool ApplyCultivationInventoryChanges(long inventoryRevision,
            bool requiresInventorySnapshot,
            InventoryV2StackChangeDto[] stackItemChanges)
        {
            if (!TryInitialize())
                return false;
            bool applied = v2Session.ApplyJingjieBreakthroughInventoryChanges(
                inventoryRevision, requiresInventorySnapshot, stackItemChanges,
                out string error, "cultivation_learn_result");
            if (!applied)
                Debug.LogWarning("[Cultivation] inventory changes require snapshot: "
                    + error);
            return applied;
        }

        public void ApplySkillLearnInventoryChanges(long inventoryRevision,
            bool requiresInventorySnapshot,
            InventoryV2StackChangeDto[] stackItemChanges)
        {
            if (!TryInitialize())
                return;
            if (!v2Session.ApplyJingjieBreakthroughInventoryChanges(
                    inventoryRevision, requiresInventorySnapshot, stackItemChanges,
                    out string error, "skill_learn_result"))
                Debug.LogWarning("[SkillLearn] 背包需要刷新：" + error);
        }

        public bool ApplyCultivationStartInventoryChanges(long inventoryRevision,
            bool requiresInventorySnapshot,
            InventoryV2StackChangeDto[] stackItemChanges)
        {
            if (!TryInitialize())
                return false;
            bool applied = v2Session.ApplyJingjieBreakthroughInventoryChanges(
                inventoryRevision, requiresInventorySnapshot, stackItemChanges,
                out string error, "cultivation_start_result");
            if (!applied)
                Debug.LogWarning("[Cultivation] start inventory requires snapshot: "
                    + error);
            return applied;
        }

        public bool TryUseQuickbarSlot(int slotIndex)
        {
            QuickbarSlotDto slot = quickbarSession?.GetSlot(slotIndex);
            return slot != null && slot.stackId > 0 && slot.count > 0
                && v2Session != null && v2Session.TryUseStack(slot.stackId);
        }

        public bool ApplyTradeInventoryChanges(
            long inventoryRevision,
            bool requiresSnapshot,
            PlayerTradeInventoryChangeDto[] changes)
        {
            if (!TryInitialize())
                return false;
            bool applied = v2Session.ApplyTradeInventoryChanges(
                inventoryRevision, requiresSnapshot, changes, out string error);
            if (!applied)
                LogTradeInventoryApplyFailure(error);
            return applied;
        }

        public void ApplyLootPickupResult(LootPickupResultMessageDto message)
        {
            if (message != null && message.success
                && message.status == InventoryProtocolConstants.PickupStatuses.Succeeded
                && TryInitialize())
            {
                InventoryV2WalletChangeDto[] wallets = null;
                if (message.walletChanges != null)
                {
                    wallets = new InventoryV2WalletChangeDto[message.walletChanges.Length];
                    for (int i = 0; i < wallets.Length; i++)
                    {
                        WalletChangeDto change = message.walletChanges[i];
                        wallets[i] = change == null ? null : new InventoryV2WalletChangeDto
                        {
                            currencyId = change.currencyId,
                            previousBalance = change.previousBalance,
                            newBalance = change.newBalance
                        };
                    }
                }
                v2Session.ApplyPickupDelta(message.inventoryRevision,
                    message.stackItemChanges, message.equipmentItemChanges, wallets);
            }
        }

        public void ResetConnectionState()
        {
            v2Session?.Reset();
            quickbarSession?.Reset();
        }

        public void Shutdown()
        {
            UnsubscribeNetworkEvents();
            if (quickbarSession != null)
                quickbarSession.OperationFailed -= HandleQuickbarOperationFailed;
            quickbarSession?.Dispose();
            quickbarSession = null;
            v2Session?.Dispose();
            v2Session = null;
            activeConnectionEpoch = 0;
        }

        private void ResolveReferences()
        {
            if (NetworkManager.Instance != null)
            {
                networkManager = NetworkManager.Instance;
            }

            if (NetMessageDispatcher.Instance != null)
            {
                messageDispatcher = NetMessageDispatcher.Instance;
            }

            if (panelView == null)
            {
                panelView = FindObjectOfType<InventoryPanelView>(true);
            }
        }

        private bool HasRequiredReferences()
        {
            return networkManager != null
                && messageDispatcher != null
                && panelView != null
                && itemDefinitions != null;
        }

        private void SubscribeNetworkEvents()
        {
            if (networkEventsSubscribed || networkManager == null)
            {
                return;
            }

            networkManager.OnConnected += HandleNetworkConnected;
            networkManager.OnDisconnected += HandleNetworkDisconnected;
            networkEventsSubscribed = true;
        }

        private void UnsubscribeNetworkEvents()
        {
            if (!networkEventsSubscribed || networkManager == null)
            {
                return;
            }

            networkManager.OnConnected -= HandleNetworkConnected;
            networkManager.OnDisconnected -= HandleNetworkDisconnected;
            networkEventsSubscribed = false;
        }

        private void HandleNetworkConnected()
        {
            v2Session?.Reset();
            quickbarSession?.Reset();
        }

        private void HandleNetworkDisconnected(string reason)
        {
            v2Session?.Reset();
            quickbarSession?.Reset();
        }

        private long CurrentLocalCombatRevision()
        {
            GameSession session = FindObjectOfType<GameSession>();
            CombatStateStore store = FindObjectOfType<CombatStateStore>();
            if (session == null || session.LocalCharacterDbId <= 0 || store == null
                || !store.TryGetState("player",
                    session.LocalCharacterDbId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    out CombatStateData state))
                return -1;
            return state.CombatRevision;
        }

        private void ApplyItemUseHealth(int hp, long expectedCombatRevision)
        {
            GameSession session = FindObjectOfType<GameSession>();
            CombatStateStore store = FindObjectOfType<CombatStateStore>();
            if (session == null || store == null || session.LocalCharacterDbId <= 0
                || !store.TryApplyItemUseHealth(store.ActiveSceneId,
                    session.LocalCharacterDbId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    hp, expectedCombatRevision))
                Debug.LogWarning("V2 item use HP was not applied; waiting for authoritative combat state.");
        }

        private bool ApplyEquipmentStats(long characterDbId, InventoryV2EquipmentCharacterStatsDto stats,
            long serverTime)
        {
            GameSession session = FindObjectOfType<GameSession>();
            return session != null && session.ApplyEquipmentStats(characterDbId, stats, serverTime,
                FindObjectOfType<CombatStateStore>());
        }

        private void ApplyItemUseMana(int currentMp, int maxMp, long serverTime)
        {
            GameSession session = FindObjectOfType<GameSession>();
            session?.ApplyItemUseMana(currentMp, maxMp, serverTime);
        }

        private void ApplyItemUseJingjieBalance(long? current, long? total)
        {
            FindObjectOfType<GameSession>()?.ApplyItemUseJingjieBalance(current, total);
        }

        private static bool LogTradeInventoryApplyFailure(string error)
        {
            Debug.LogWarning("[PlayerTrade] inventory changes require snapshot: "
                + (error ?? string.Empty));
            return false;
        }

        private static void HandleQuickbarOperationFailed(string errorCode)
        {
            string message;
            switch (errorCode ?? string.Empty)
            {
                case "quickbar_snapshot_failed":
                    message = "快捷栏加载失败，请稍后重试";
                    break;
                case "quickbar_timeout":
                    message = "快捷栏操作超时，请稍后重试";
                    break;
                case "quickbar_rate_limited":
                    message = "快捷栏操作频繁，请稍后再试";
                    break;
                case "quickbar_operation_session_invalid":
                    message = "快捷栏暂不可用，请重新进入场景";
                    break;
                case "quickbar_revision_conflict":
                    message = "快捷栏已发生变化，请刷新后重试";
                    break;
                case "quickbar_inventory_revision_conflict":
                case "inventory_revision_conflict":
                    message = "背包已发生变化，请刷新后重试";
                    break;
                default:
                    message = "快捷栏操作失败，请稍后重试";
                    break;
            }

            GameNoticeService.Show(message);
        }
    }
}
