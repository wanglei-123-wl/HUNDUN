using System;
using Game.Items;
using UnityEngine;

public sealed class LootModuleDependencies
{
    public NetworkManager NetworkManager;
    public NetMessageDispatcher MessageDispatcher;
    public ItemDefinitionTable ItemConfigTable;
    public LootSystemSettings Settings;
    public LootFeedbackPresenter FeedbackPresenter;
    public Transform ViewRoot;
    public Func<long> GetClientTick;
    public Func<string> GetSelfCharacterDbId;
    public LootPickupSessionState PickupSessionState;
    public Action<LootPickupResultMessageDto> ApplyInventoryPickupResult;
    public Action RequestInventoryReconciliation;
}

public sealed class LootModule : IDisposable
{
    private readonly LootModuleDependencies dependencies;
    private bool initialized;
    private bool disposed;
    private float nextOwnershipRefreshTime;

    public LootStateStore StateStore { get; private set; }
    public LootServerTimeService ServerTimeService { get; private set; }
    public LootOwnershipService OwnershipService { get; private set; }
    public LootSyncService SyncService { get; private set; }
    public LootNetworkSender NetworkSender { get; private set; }
    public LootMessageReceiver MessageReceiver { get; private set; }
    public LootViewPool ViewPool { get; private set; }
    public LootViewFactory ViewFactory { get; private set; }
    public WorldLootViewManager ViewManager { get; private set; }
    public LootPickupRangeChecker PickupRangeChecker { get; private set; }
    public LootPickupService PickupService { get; private set; }
    public LootInteractionController InteractionController { get; private set; }
    public LootErrorTextProvider ErrorTextProvider { get; private set; }
    public LootErrorHandler ErrorHandler { get; private set; }

    public event Action<LootState> MoveCloserRequested;
    public event Action WorldResyncRequested;
    public event Action PlayerWorldStateCheckRequested;

    public LootModule(LootModuleDependencies dependencies)
    {
        this.dependencies = dependencies
            ?? throw new ArgumentNullException(nameof(dependencies));
    }

    public void Initialize(bool registerLootSnapshot = true,bool registerInventorySnapshot = false,
        bool registerReliableStructures = true)
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(LootModule));
        }

        if (initialized)
        {
            return;
        }

        ValidateDependencies();

        StateStore = new LootStateStore();
        ServerTimeService = new LootServerTimeService();
        OwnershipService = new LootOwnershipService(ServerTimeService);
        SyncService = new LootSyncService(StateStore, ServerTimeService);
        NetworkSender = new LootNetworkSender(dependencies.NetworkManager);
        ViewPool = new LootViewPool(dependencies.Settings, dependencies.ViewRoot);
        ViewFactory = new LootViewFactory(
            dependencies.ItemConfigTable,
            dependencies.Settings,
            ViewPool);
        ViewManager = new WorldLootViewManager(
            ViewFactory,
            ViewPool,
            OwnershipService,
            dependencies.GetSelfCharacterDbId);
        PickupRangeChecker = new LootPickupRangeChecker(dependencies.Settings);
        PickupService = new LootPickupService(
            StateStore,
            NetworkSender,
            ViewManager,
            SyncService,
            dependencies.Settings,
            dependencies.GetClientTick,
            () => Time.realtimeSinceStartup,
            dependencies.PickupSessionState);
        InteractionController = new LootInteractionController(
            StateStore,
            SyncService,
            PickupService,
            OwnershipService,
            PickupRangeChecker,
            dependencies.GetSelfCharacterDbId);
        ErrorTextProvider = new LootErrorTextProvider();
        ErrorHandler = new LootErrorHandler(
            StateStore,
            ViewManager,
            PickupService,
            dependencies.FeedbackPresenter,
            ErrorTextProvider);
        MessageReceiver = new LootMessageReceiver(dependencies.MessageDispatcher);

        Subscribe();
        MessageReceiver.Initialize(registerLootSnapshot,registerInventorySnapshot,registerReliableStructures);
        initialized = true;
    }

    public void OnEnterScene(string sceneId)
    {
        EnsureInitialized();

        if (string.IsNullOrWhiteSpace(sceneId))
        {
            throw new ArgumentException("Scene ID cannot be empty.", nameof(sceneId));
        }

        string previousSceneId = SyncService.CurrentSceneId;
        if (string.Equals(previousSceneId, sceneId, StringComparison.Ordinal))
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(previousSceneId)
            && !string.Equals(previousSceneId, sceneId, StringComparison.Ordinal))
        {
            ViewManager.ClearScene(previousSceneId);
            StateStore.ClearScene(previousSceneId);
        }

        ViewManager.ClearScene(sceneId);
        StateStore.ClearScene(sceneId);
        SyncService.SetCurrentScene(sceneId);
    }

    public void OnLeaveScene(string sceneId)
    {
        EnsureInitialized();

        if (string.IsNullOrWhiteSpace(sceneId))
        {
            return;
        }

        ViewManager.ClearScene(sceneId);
        StateStore.ClearScene(sceneId);

        if (string.Equals(SyncService.CurrentSceneId, sceneId, StringComparison.Ordinal))
        {
            SyncService.ClearCurrentScene();
        }
    }

    public void ResetConnectionState()
    {
        EnsureInitialized();
        PickupService.ResetConnectionState();
        ErrorHandler.ResetState();
        ServerTimeService.Reset();
    }

    public void Tick()
    {
        if (initialized && !disposed)
        {
            PickupService.UpdateTimeouts();

            float refreshInterval = Mathf.Max(
                0.05f,
                dependencies.Settings.OwnershipRefreshInterval);
            if (Time.unscaledTime >= nextOwnershipRefreshTime)
            {
                nextOwnershipRefreshTime = Time.unscaledTime + refreshInterval;
                ViewManager.RefreshOwnershipStates(
                    StateStore.GetByScene(SyncService.CurrentSceneId));
            }
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        if (!initialized)
        {
            return;
        }

        MessageReceiver.Dispose();
        Unsubscribe();
        ViewManager.ClearAll();
        ViewPool.Clear();
        StateStore.ClearAll();
        ServerTimeService.Reset();
        initialized = false;

        MoveCloserRequested = null;
        WorldResyncRequested = null;
        PlayerWorldStateCheckRequested = null;
    }

    private void Subscribe()
    {
        MessageReceiver.SnapshotReceived += HandleSnapshotReceived;
        MessageReceiver.UpsertReceived += HandleUpsertReceived;
        MessageReceiver.RemoveReceived += HandleRemoveReceived;
        MessageReceiver.PickupResultReceived += HandlePickupResultReceived;
        MessageReceiver.InventorySnapshotReceived += HandleInventorySnapshotReceived;
        MessageReceiver.ProtocolErrorReceived += HandleProtocolErrorReceived;

        SyncService.SceneSnapshotApplied += ViewManager.RebuildScene;
        SyncService.SceneSnapshotApplied += ErrorHandler.HandleSceneSnapshotApplied;
        SyncService.LootUpsertApplied += ViewManager.CreateOrUpdate;
        SyncService.LootRemoved += ViewManager.Remove;

        PickupService.PickupSucceeded += HandlePickupSucceeded;
        PickupService.PickupFailed += ErrorHandler.HandlePickupFailure;
        PickupService.PickupResultRejected += HandlePickupResultRejected;
        PickupService.PickupTimedOut += HandlePickupTimedOut;
        PickupService.InventoryReconciliationRequested += HandleInventoryReconciliationRequested;

        InteractionController.MoveCloserRequested += HandleMoveCloserRequested;
        ErrorHandler.MoveCloserRequested += HandleMoveCloserRequested;
        ErrorHandler.WorldResyncRequested += HandleWorldResyncRequested;
        ErrorHandler.PlayerWorldStateCheckRequested += HandlePlayerWorldStateCheckRequested;
    }

    private void Unsubscribe()
    {
        MessageReceiver.SnapshotReceived -= HandleSnapshotReceived;
        MessageReceiver.UpsertReceived -= HandleUpsertReceived;
        MessageReceiver.RemoveReceived -= HandleRemoveReceived;
        MessageReceiver.PickupResultReceived -= HandlePickupResultReceived;
        MessageReceiver.InventorySnapshotReceived -= HandleInventorySnapshotReceived;
        MessageReceiver.ProtocolErrorReceived -= HandleProtocolErrorReceived;

        SyncService.SceneSnapshotApplied -= ViewManager.RebuildScene;
        SyncService.SceneSnapshotApplied -= ErrorHandler.HandleSceneSnapshotApplied;
        SyncService.LootUpsertApplied -= ViewManager.CreateOrUpdate;
        SyncService.LootRemoved -= ViewManager.Remove;

        PickupService.PickupSucceeded -= HandlePickupSucceeded;
        PickupService.PickupFailed -= ErrorHandler.HandlePickupFailure;
        PickupService.PickupResultRejected -= HandlePickupResultRejected;
        PickupService.PickupTimedOut -= HandlePickupTimedOut;
        PickupService.InventoryReconciliationRequested -= HandleInventoryReconciliationRequested;

        InteractionController.MoveCloserRequested -= HandleMoveCloserRequested;
        ErrorHandler.MoveCloserRequested -= HandleMoveCloserRequested;
        ErrorHandler.WorldResyncRequested -= HandleWorldResyncRequested;
        ErrorHandler.PlayerWorldStateCheckRequested -= HandlePlayerWorldStateCheckRequested;
    }

    private void HandleSnapshotReceived(LootSnapshotMessageDto message)
    {
        SyncService.ApplySnapshot(message);
    }

    private void HandleUpsertReceived(LootUpsertMessageDto message)
    {
        SyncService.ApplyUpsert(message);
    }

    private void HandleRemoveReceived(LootRemoveMessageDto message)
    {
        SyncService.ApplyRemove(message);
    }

    private void HandlePickupResultReceived(LootPickupResultMessageDto message)
    {
        PickupService.HandlePickupResult(message);
    }

    private void HandleInventorySnapshotReceived(InventorySnapshotResultMessageDto message)
    {
        if (message != null)
        {
            ServerTimeService.Synchronize(message.serverTime);
        }

        dependencies.RequestInventoryReconciliation();
    }

    private void HandleProtocolErrorReceived(NetErrorMessageDto message)
    {
        if (message != null)
        {
            ServerTimeService.Synchronize(message.serverTime);
        }

        PickupService.HandleProtocolError(message);
    }

    private void HandlePickupSucceeded(LootPickupResultMessageDto message)
    {
        dependencies.ApplyInventoryPickupResult(message);
        dependencies.FeedbackPresenter.ShowPickupSuccess(message);
    }

    private void HandleInventoryReconciliationRequested()
    {
        dependencies.RequestInventoryReconciliation();
    }

    private void HandlePickupResultRejected(LootPickupResultMessageDto message)
    {
        Debug.LogWarning(
            "Loot pickup result rejected. sequence="
            + (message == null ? 0 : message.requestSequence)
            + " lootId=" + (message == null ? "<null>" : message.lootId));
    }

    private void HandlePickupTimedOut(LootPickupRequestState requestState)
    {
        dependencies.FeedbackPresenter.ShowError("拾取结果超时，正在同步背包");
    }

    private void HandleMoveCloserRequested(LootState loot)
    {
        if (loot != null)
        {
            MoveCloserRequested?.Invoke(loot);
        }
    }

    private void HandleMoveCloserRequested(string lootId)
    {
        if (StateStore.TryGet(lootId, out LootState loot))
        {
            MoveCloserRequested?.Invoke(loot);
        }
    }

    private void HandleWorldResyncRequested()
    {
        WorldResyncRequested?.Invoke();
    }

    private void HandlePlayerWorldStateCheckRequested()
    {
        PlayerWorldStateCheckRequested?.Invoke();
    }

    private void ValidateDependencies()
    {
        if (dependencies.NetworkManager == null)
        {
            throw new InvalidOperationException("LootModule requires NetworkManager.");
        }

        if (dependencies.MessageDispatcher == null)
        {
            throw new InvalidOperationException("LootModule requires NetMessageDispatcher.");
        }

        if (dependencies.Settings == null)
        {
            throw new InvalidOperationException("LootModule requires LootSystemSettings.");
        }

        if (dependencies.FeedbackPresenter == null)
        {
            throw new InvalidOperationException("LootModule requires LootFeedbackPresenter.");
        }

        if (dependencies.GetClientTick == null)
        {
            throw new InvalidOperationException("LootModule requires a client tick provider.");
        }

        if (dependencies.GetSelfCharacterDbId == null)
        {
            throw new InvalidOperationException("LootModule requires a character ID provider.");
        }

        if (dependencies.ApplyInventoryPickupResult == null)
        {
            throw new InvalidOperationException("LootModule requires an inventory pickup result sink.");
        }
        if (dependencies.RequestInventoryReconciliation == null)
        {
            throw new InvalidOperationException("LootModule requires a V2 inventory reconciliation sink.");
        }
    }

    private void EnsureInitialized()
    {
        if (!initialized || disposed)
        {
            throw new InvalidOperationException("LootModule is not initialized.");
        }
    }
}
