using System;
using System.Globalization;
using Game.Items;
using Game.Client.BattleSync;
using NewLootSnapshot = Game.Client.BattleSync.Protocol.LootSnapshotMessageDto;
using NewInventorySnapshot = Game.Client.BattleSync.Protocol.InventorySnapshotResultMessageDto;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class LootModuleRuntime : MonoBehaviour, ILootBattleSyncModule,
    IBattleSyncSnapshotRoutingControl
{
    [Header("Runtime References")]
    [SerializeField] private NetworkManager networkManager;
    [SerializeField] private NetMessageDispatcher messageDispatcher;
    [SerializeField] private GameSession gameSession;
    [SerializeField] private Game.Inventory.InventoryModuleRuntime inventoryModuleRuntime;

    [Header("Loot References")]
    [SerializeField] private ItemDefinitionTable itemConfigTable;
    [SerializeField] private LootSystemSettings settings;
    [SerializeField] private LootFeedbackPresenter feedbackPresenter;
    [SerializeField] private Transform viewRoot;

    [Header("Scene")]
    [SerializeField] private string initialSceneId = "world_001";
    [SerializeField] private bool initializeAutomatically = true;
    [SerializeField] private bool enterInitialScene = true;

    private LootModule module;
    private string currentSceneId;
    private bool missingDependenciesLogged;
    private bool networkEventsSubscribed;
    private long activeConnectionEpoch;
    private BattleSyncSnapshotRoutingMode snapshotRoutingMode = BattleSyncSnapshotRoutingMode.LegacySelfRegistered;

    public LootModule Module => module;
    public LootFeedbackPresenter FeedbackPresenter => feedbackPresenter;
    public bool IsInitialized => module != null;
    public string CurrentSceneId => currentSceneId;

    public event Action<LootState> MoveCloserRequested;
    public event Action WorldResyncRequested;
    public event Action PlayerWorldStateCheckRequested;

    private void Start()
    {
        if (initializeAutomatically)
        {
            TryInitialize();
        }
    }

    private void Update()
    {
        if (module == null)
        {
            if (initializeAutomatically)
            {
                TryInitialize();
            }

            return;
        }

        module.Tick();
    }

    private void OnDestroy()
    {
        Shutdown();
    }

    public bool TryInitialize()
    {
        if (module != null)
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
                    "LootModuleRuntime is waiting for NetworkManager, "
                    + "NetMessageDispatcher, GameSession, LootSystemSettings "
                    + "and LootFeedbackPresenter.",
                    this);
            }

            return false;
        }

        LootModuleDependencies dependencies = new LootModuleDependencies
        {
            NetworkManager = networkManager,
            MessageDispatcher = messageDispatcher,
            ItemConfigTable = itemConfigTable,
            Settings = settings,
            FeedbackPresenter = feedbackPresenter,
            ViewRoot = viewRoot == null ? transform : viewRoot,
            GetClientTick = GetClientTick,
            GetSelfCharacterDbId = GetSelfCharacterDbId,
            PickupSessionState = networkManager.LootPickupSession,
            ApplyInventoryPickupResult = ApplyInventoryPickupResult,
            RequestInventoryReconciliation = RequestInventoryReconciliation
        };

        module = new LootModule(dependencies);
        module.MoveCloserRequested += HandleMoveCloserRequested;
        module.WorldResyncRequested += HandleWorldResyncRequested;
        module.PlayerWorldStateCheckRequested += HandlePlayerWorldStateCheckRequested;
        bool legacy = snapshotRoutingMode == BattleSyncSnapshotRoutingMode.LegacySelfRegistered;
        module.Initialize(legacy, false, legacy);
        SubscribeNetworkEvents();
        missingDependenciesLogged = false;

        if (enterInitialScene && !string.IsNullOrWhiteSpace(initialSceneId))
        {
            EnterScene(initialSceneId);
        }

        return true;
    }

    public void SetSnapshotRoutingMode(BattleSyncSnapshotRoutingMode mode)
    {
        if (snapshotRoutingMode == mode) return;
        snapshotRoutingMode = mode;
        if (module != null) { Shutdown(); TryInitialize(); }
    }

    public BattleSyncApplyResult BeginSession(BattleSyncModuleSessionContext context)
    {
        if (!context.IsValid || !TryInitialize()) return BattleSyncApplyResult.RetryableFailure;
        activeConnectionEpoch = context.ConnectionEpoch;
        module.ResetConnectionState();
        EnterScene(context.SceneId);
        return BattleSyncApplyResult.Applied;
    }

    public void EndSession(long connectionEpoch)
    {
        if (connectionEpoch != activeConnectionEpoch) return;
        module?.ResetConnectionState(); activeConnectionEpoch = 0;
    }

    public BattleSyncApplyResult ApplyLootSnapshot(NewLootSnapshot message)
    {
        if (message == null || !TryInitialize()) return BattleSyncApplyResult.InvalidMessage;
        LootDataDto[] items = new LootDataDto[message.loots == null ? 0 : message.loots.Length];
        for (int i = 0; i < items.Length; i++)
        {
            var source = message.loots[i];
            items[i] = new LootDataDto { lootId=source.lootId,sceneId=source.sceneId,itemId=source.itemId,
                count=source.count,x=source.x,y=source.y,z=source.z,ownerType=source.ownerType,ownerId=source.ownerId,
                ownerExpireAtMs=source.ownerExpireAtMs,expireAtMs=source.expireAtMs,aoiRevision=source.aoiRevision,state=source.state };
        }
        LootSyncResult result = module.SyncService.ApplySnapshot(new LootSnapshotMessageDto
            { msgId=LootProtocolConstants.Snapshot,type=LootProtocolConstants.Snapshot,sceneId=message.sceneId,serverTime=message.serverTime,loots=items });
        return result == LootSyncResult.Applied ? BattleSyncApplyResult.Applied
            : result == LootSyncResult.IgnoredScene ? BattleSyncApplyResult.IgnoredWrongScene : BattleSyncApplyResult.InvalidMessage;
    }

    public BattleSyncApplyResult ApplyLootUpsert(Game.Client.BattleSync.Protocol.LootUpsertMessageDto message)
    {
        if(message==null||message.loot==null||!TryInitialize())return BattleSyncApplyResult.InvalidMessage;
        LootDataDto loot=new LootDataDto{lootId=message.loot.lootId,sceneId=message.loot.sceneId,
            itemId=message.loot.itemId,count=message.loot.count,x=message.loot.x,y=message.loot.y,z=message.loot.z,
            ownerType=message.loot.ownerType,ownerId=message.loot.ownerId,ownerExpireAtMs=message.loot.ownerExpireAtMs,
            expireAtMs=message.loot.expireAtMs,aoiRevision=message.aoiRevision,state=message.loot.state};
        LootSyncResult result=module.SyncService.ApplyUpsert(new LootUpsertMessageDto{msgId=LootProtocolConstants.Upsert,
            type=LootProtocolConstants.Upsert,sceneId=message.sceneId,aoiRevision=message.aoiRevision,
            serverTime=message.serverTime,loot=loot});
        return MapLootResult(result);
    }

    public BattleSyncApplyResult ApplyLootRemove(Game.Client.BattleSync.Protocol.LootRemoveMessageDto message)
    {
        if(message==null||!TryInitialize())return BattleSyncApplyResult.InvalidMessage;
        LootSyncResult result=module.SyncService.ApplyRemove(new LootRemoveMessageDto{msgId=LootProtocolConstants.Remove,
            type=LootProtocolConstants.Remove,sceneId=message.sceneId,lootId=message.lootId,
            aoiRevision=message.aoiRevision,reason=message.reason,serverTime=message.serverTime});
        return MapLootResult(result);
    }

    private static BattleSyncApplyResult MapLootResult(LootSyncResult result)
    {
        switch(result)
        {
            case LootSyncResult.Applied:return BattleSyncApplyResult.Applied;
            case LootSyncResult.IgnoredOldRevision:return BattleSyncApplyResult.IgnoredStale;
            case LootSyncResult.IgnoredNotFound:return BattleSyncApplyResult.AlreadyApplied;
            case LootSyncResult.IgnoredScene:return BattleSyncApplyResult.IgnoredWrongScene;
            default:return BattleSyncApplyResult.InvalidMessage;
        }
    }

    public void EnterScene(string sceneId)
    {
        if (string.IsNullOrWhiteSpace(sceneId) || !TryInitialize())
        {
            return;
        }

        module.OnEnterScene(sceneId);
        currentSceneId = sceneId;
    }

    public void LeaveScene(string sceneId)
    {
        if (module == null || string.IsNullOrWhiteSpace(sceneId))
        {
            return;
        }

        module.OnLeaveScene(sceneId);
        if (string.Equals(currentSceneId, sceneId, StringComparison.Ordinal))
        {
            currentSceneId = null;
        }
    }

    public void ResetConnectionState()
    {
        if (module != null)
        {
            module.ResetConnectionState();
        }
    }

    public LootInteractionResult TryHandleClick(
        WorldLootView view,
        Vector3 playerPosition)
    {
        if (!TryInitialize())
        {
            return LootInteractionResult.PickupRejected;
        }

        return module.InteractionController.TryHandleClick(view, playerPosition);
    }

    public void Shutdown()
    {
        UnsubscribeNetworkEvents();

        if (module == null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(currentSceneId))
        {
            module.OnLeaveScene(currentSceneId);
            currentSceneId = null;
        }

        module.MoveCloserRequested -= HandleMoveCloserRequested;
        module.WorldResyncRequested -= HandleWorldResyncRequested;
        module.PlayerWorldStateCheckRequested -= HandlePlayerWorldStateCheckRequested;
        module.Dispose();
        module = null;
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

        if (gameSession == null)
        {
            gameSession = FindObjectOfType<GameSession>();
        }

        if (inventoryModuleRuntime == null)
        {
            inventoryModuleRuntime = FindObjectOfType<Game.Inventory.InventoryModuleRuntime>();
        }

        if (feedbackPresenter == null)
        {
            feedbackPresenter = FindObjectOfType<LootFeedbackPresenter>();
        }
    }

    private bool HasRequiredReferences()
    {
        return networkManager != null
            && messageDispatcher != null
            && gameSession != null
            && inventoryModuleRuntime != null
            && settings != null
            && feedbackPresenter != null;
    }

    private long GetClientTick()
    {
        return gameSession == null ? 1L : Math.Max(1L, gameSession.CurrentClientTick);
    }

    private string GetSelfCharacterDbId()
    {
        if (gameSession == null || gameSession.LocalCharacterDbId <= 0)
        {
            return null;
        }

        return gameSession.LocalCharacterDbId.ToString(CultureInfo.InvariantCulture);
    }

    private void ApplyInventoryPickupResult(LootPickupResultMessageDto message)
    {
        inventoryModuleRuntime.ApplyLootPickupResult(message);
    }

    private void RequestInventoryReconciliation()
    {
        inventoryModuleRuntime.RequestReconciliation();
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
        module?.ResetConnectionState();
    }

    private void HandleNetworkDisconnected(string reason)
    {
        module?.ResetConnectionState();
        if (module == null)
            networkManager?.LootPickupSession.Reset();
        activeConnectionEpoch = 0;
    }

    private void HandleMoveCloserRequested(LootState loot)
    {
        MoveCloserRequested?.Invoke(loot);
    }

    private void HandleWorldResyncRequested()
    {
        WorldResyncRequested?.Invoke();
    }

    private void HandlePlayerWorldStateCheckRequested()
    {
        PlayerWorldStateCheckRequested?.Invoke();
    }
}
