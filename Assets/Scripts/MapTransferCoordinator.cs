using System;
using System.Collections;
using Game.UI.Notice;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

[DisallowMultipleComponent]
public sealed class MapTransferCoordinator : MonoBehaviour
{
    private enum TransferState { Idle, CheckingResources, Requested, Preparing, AwaitingResult, Committed }

    private const float RequestTimeoutSeconds = 5f;
    private const float PreparationTimeoutSeconds = 25f;
    private const float ResultTimeoutSeconds = 10f;
    private const float ResourceCheckTimeoutSeconds = 10f;
    private const string MissingResources = "地图资源不完整，请重启游戏完成更新后重试";

    private GameSession session;
    private NetworkManager network;
    private NetMessageDispatcher dispatcher;
    private VillageTeleportPanel panel;
    private bool panelHandlerBound;
    private Action<MapTransferResult> commitHandler;
    private TransferState state;
    private MapTransferRequest request;
    private Game.Inventory.Protocol.V2.InventoryV2ItemUseRequestDto stoneRequest;
    private MapTransferReadyRequest ready;
    private string transferId;
    private string destinationSceneId;
    private string destinationMapId;
    private string destinationAddress;
    private long connectionEpoch;
    private float deadline;
    private int retries;
    private Coroutine preload;
    private AsyncOperationHandle<long> dependencies;
    private MapTransferStatusView statusView;
    private string preparationFailure;
    private bool recoveryStatus;

    public bool CanCompletePresentation => statusView != null && statusView.IsVisible && statusView.MinimumDisplayElapsed;

    public void ReportPreparationStatus(string message) => ShowStatus(message);

    public void Initialize(GameSession owner)
    {
        if (session == owner && dispatcher != null)
            return;

        Shutdown();
        session = owner;
        network = NetworkManager.Instance;
        dispatcher = NetMessageDispatcher.Instance;
        if (network == null || dispatcher == null)
        {
            Debug.LogError("Map transfer requires the Gate network and dispatcher.", this);
            return;
        }

        dispatcher.Register("map_transfer_prepare", HandlePrepare);
        dispatcher.Register("map_transfer_result", HandleResult);
        dispatcher.Register("error", HandleError);
        network.OnDisconnected += HandleDisconnected;
        BindPanel();
    }

    public void SetCommitHandler(Action<MapTransferResult> handler)
    {
        commitHandler = handler;
        if (handler == null && state != TransferState.Idle
            && state != TransferState.Committed)
        {
            if (state == TransferState.CheckingResources)
                AbortPreflight("传送已取消");
            else RecoverUncertain("Transfer destination handler was removed.");
        }
        BindPanel();
    }

    public void CompleteCommittedTransfer()
    {
        if (state != TransferState.Committed || !CanCompletePresentation)
            return;
        ClearPending();
        BindPanel();
    }

    public void Shutdown()
    {
        if (panel != null)
        {
            panel.SetTransferHandler(null);
            panel.Hide();
        }
        panel = null;
        panelHandlerBound = false;
        if (dispatcher != null)
        {
            dispatcher.Unregister("map_transfer_prepare", HandlePrepare);
            dispatcher.Unregister("map_transfer_result", HandleResult);
            dispatcher.Unregister("error", HandleError);
        }
        if (network != null)
            network.OnDisconnected -= HandleDisconnected;
        ClearPending();
        if (statusView != null) statusView.Release();
        statusView = null;
        dispatcher = null;
        network = null;
        session = null;
        commitHandler = null;
    }

    private void OnDestroy()
    {
        Shutdown();
    }

    private void Update()
    {
        if (session == null || network == null)
            return;

        if (state == TransferState.Idle && recoveryStatus && session.IsWorldReady
            && network.IsConnected && !network.SceneOperationsPaused && CanCompletePresentation)
        {
            statusView.Hide();
            recoveryStatus = false;
        }

        if (state == TransferState.Idle)
            BindPanel();
        if (state == TransferState.Idle || state == TransferState.Committed)
            return;

        if (!network.IsConnected || network.ConnectionEpoch != connectionEpoch)
        {
            ClearPending(false);
            ShowFailure("服务器连接已变化，请等待重新连接后重试", true);
            BindPanel();
            GameNoticeService.Show("服务器连接已变化，请等待重新连接后重试");
            return;
        }
        if (Time.realtimeSinceStartup < deadline)
            return;

        if (state == TransferState.CheckingResources)
        {
            AbortPreflight("地图资源检查超时，请重试");
            return;
        }
        if (state == TransferState.Preparing)
        {
            StopPreload();
            preparationFailure = "地图资源检查超时，请重试";
            SendReady(false);
            return;
        }

        if (retries >= 1)
        {
            RecoverUncertain("Map transfer response timed out.");
            return;
        }

        retries++;
        bool sent = state == TransferState.Requested
            ? stoneRequest != null ? network.Send(stoneRequest) : network.Send(request)
            : network.Send(ready);
        if (!sent)
        {
            RecoverUncertain("Map transfer retry could not be sent.");
            return;
        }
        deadline = Time.realtimeSinceStartup
            + (state == TransferState.Requested
                ? RequestTimeoutSeconds : ResultTimeoutSeconds);
    }

    private void BindPanel()
    {
        if (session == null)
            return;
        if (panel == null)
        {
            panel = FindObjectOfType<VillageTeleportPanel>(true);
            panelHandlerBound = false;
        }

        bool available = commitHandler != null && state == TransferState.Idle
            && (statusView == null || !statusView.IsVisible)
            && session.IsWorldReady && network != null && network.IsConnected
            && !network.SceneOperationsPaused
            && !string.IsNullOrWhiteSpace(session.SceneSessionId);
        if (panel != null && panelHandlerBound != available)
        {
            panel.SetTransferHandler(available ? BeginTransfer : (Action<MapTransferRoute>)null);
            panelHandlerBound = available;
        }
    }

    private void BeginTransfer(MapTransferRoute route)
    {
        if (state != TransferState.Idle || commitHandler == null
            || (statusView != null && statusView.IsVisible)
            || route == null || string.IsNullOrWhiteSpace(route.routeId)
            || route.routeId.Length > 64
            || session == null || !session.IsWorldReady || network == null
            || !network.IsConnected || string.IsNullOrWhiteSpace(session.SceneSessionId))
            return;

        if (statusView == null) statusView = MapTransferStatusView.Create(transform);
        statusView.BeginDestination(route.sceneId, route.mapId);
        recoveryStatus = false;
        if (!WorldMapSceneCatalog.TryGetAddress(route.sceneId, route.mapId, out _))
        {
            ShowFailure("目标地图暂不可用", false);
            GameNoticeService.Show("目标地图暂不可用");
            return;
        }
        if (session.IsCurrentMap(route.sceneId, route.mapId))
        {
            ShowFailure("已在当前地图，无法传送", false);
            GameNoticeService.Show("已在当前地图，无法传送");
            return;
        }
        destinationSceneId = route.sceneId;
        destinationMapId = route.mapId;
        request = new MapTransferRequest
        {
            requestId = Guid.NewGuid().ToString("D"),
            routeId = route.routeId,
            sceneSessionId = session.SceneSessionId
        };
        connectionEpoch = network.ConnectionEpoch;
        state = TransferState.CheckingResources;
        deadline = Time.realtimeSinceStartup + ResourceCheckTimeoutSeconds;
        BindPanel();
        ShowStatus("正在检查地图资源，请稍后");
        preload = StartCoroutine(CheckBeforeTransfer());
    }

    internal bool TryUseStone(Game.Inventory.Protocol.V2.InventoryV2ItemUseRequestDto itemUse)
    {
        if (state != TransferState.Idle || commitHandler == null || session == null || network == null ||
            !network.IsConnected || !session.TryPauseForMapTransfer())
            return false;

        itemUse.teleportRequestId = Guid.NewGuid().ToString("D");
        stoneRequest = itemUse;
        request = new MapTransferRequest
        {
            requestId = itemUse.teleportRequestId,
            sceneSessionId = session.SceneSessionId
        };
        state = TransferState.Requested;
        connectionEpoch = network.ConnectionEpoch;
        retries = 0;
        deadline = Time.realtimeSinceStartup + RequestTimeoutSeconds;
        if (!network.Send(stoneRequest))
        {
            session.ResumeAfterMapTransferFailure(null);
            ClearPending();
            return false;
        }
        BindPanel();
        ShowStatus("正在使用传送石，请稍后");
        return true;
    }

    private IEnumerator CheckBeforeTransfer()
    {
        if (!TryBeginCacheCheck())
        {
            AbortPreflight(MissingResources);
            yield break;
        }
        yield return dependencies;
        bool cached = dependencies.Status == AsyncOperationStatus.Succeeded && dependencies.Result == 0;
        ReleaseDependencies();
        preload = null;
        if (state != TransferState.CheckingResources) yield break;
        if (!cached)
        {
            AbortPreflight(MissingResources);
            yield break;
        }
        if (session == null || !session.IsWorldReady || network == null || !network.IsConnected
            || network.ConnectionEpoch != connectionEpoch || session.SceneSessionId != request.sceneSessionId
            || network.SceneOperationsPaused || commitHandler == null || !session.TryPauseForMapTransfer())
        {
            AbortPreflight("当前无法传送，请稍后重试");
            yield break;
        }

        state = TransferState.Requested;
        retries = 0;
        deadline = Time.realtimeSinceStartup + RequestTimeoutSeconds;
        BindPanel();
        ShowStatus("正在请求传送，请稍后");
        if (!network.Send(request))
        {
            session.ResumeAfterMapTransferFailure(null);
            ClearPending(false);
            ShowFailure("传送请求发送失败，请重试", false);
            BindPanel();
            GameNoticeService.Show("传送请求发送失败");
            yield break;
        }

        if (panel != null)
            panel.Hide();
    }

    private void HandlePrepare(string json)
    {
        if (state == TransferState.Idle || state == TransferState.CheckingResources || state == TransferState.Committed
            || network == null || network.ConnectionEpoch != connectionEpoch)
            return;

        MapTransferPrepare message;
        try { message = JsonUtility.FromJson<MapTransferPrepare>(json); }
        catch (Exception) { return; }
        if (message == null || message.requestId != request.requestId)
            return;
        if (state != TransferState.Requested)
        {
            if (message.transferId != transferId
                || message.sceneId != destinationSceneId
                || message.mapId != destinationMapId)
                RecoverUncertain("Conflicting transfer preparation.");
            return;
        }

        if (stoneRequest != null)
        {
            destinationSceneId = message.sceneId;
            destinationMapId = message.mapId;
        }
        if (!Guid.TryParseExact(message.transferId, "D", out _)
            || message.expiresAt <= 0
            || message.sceneId != destinationSceneId
            || message.mapId != destinationMapId
            || !WorldMapSceneCatalog.TryGetAddress(message.sceneId, message.mapId,
                out destinationAddress))
        {
            RecoverUncertain("Gate returned an invalid transfer destination.");
            return;
        }

        transferId = message.transferId;
        state = TransferState.Preparing;
        if (stoneRequest != null && session.IsCurrentMap(message.sceneId, message.mapId))
        {
            SendReady(true);
            return;
        }
        deadline = Time.realtimeSinceStartup + PreparationTimeoutSeconds;
        ShowStatus("正在准备地图，请稍后");
        preload = StartCoroutine(PreloadDestination());
    }

    private bool TryBeginCacheCheck()
    {
        try
        {
            var locations = MapTransferResourcePreflight.Resolve(destinationSceneId, destinationMapId,
                Addressables.ResourceLocators);
            dependencies = Addressables.GetDownloadSizeAsync((IEnumerable)locations);
            return true;
        }
        catch (Exception error)
        {
            Debug.LogWarning("Map cache check failed: " + error, this);
            return false;
        }
    }

    private IEnumerator PreloadDestination()
    {
        // The startup scene owns downloads. A cache loss during the handshake
        // cancels the reservation rather than starting a long network download.
        if (!TryBeginCacheCheck())
        {
            preload = null;
            preparationFailure = MissingResources;
            SendReady(false);
            yield break;
        }

        yield return dependencies;
        bool loaded = dependencies.Status == AsyncOperationStatus.Succeeded && dependencies.Result == 0;
        if (!loaded)
            Debug.LogWarning("Map dependencies failed to load: "
                + dependencies.OperationException, this);
        if (!loaded) preparationFailure = MissingResources;
        ReleaseDependencies();
        preload = null;
        if (state != TransferState.Preparing
            || network == null || !network.IsConnected
            || network.ConnectionEpoch != connectionEpoch)
            yield break;

        SendReady(loaded && commitHandler != null);
    }

    private void SendReady(bool loaded)
    {
        if (state != TransferState.Preparing)
            return;

        ready = new MapTransferReadyRequest
        {
            transferId = transferId,
            sceneSessionId = request.sceneSessionId,
            loaded = loaded
        };
        state = TransferState.AwaitingResult;
        ShowStatus(loaded ? "正在等待传送，请稍后" : "正在取消传送，请稍后");
        retries = 0;
        deadline = Time.realtimeSinceStartup + ResultTimeoutSeconds;
        if (network == null || !network.Send(ready))
            RecoverUncertain("Map transfer readiness could not be sent.");
    }

    private void HandleResult(string json)
    {
        if (state == TransferState.Idle || state == TransferState.CheckingResources || state == TransferState.Committed
            || network == null || network.ConnectionEpoch != connectionEpoch)
            return;

        MapTransferResult result;
        try { result = JsonUtility.FromJson<MapTransferResult>(json); }
        catch (Exception) { return; }
        if (result == null || result.requestId != request.requestId)
            return;
        if (!string.IsNullOrEmpty(transferId)
            && !string.Equals(result.transferId, transferId, StringComparison.Ordinal))
        {
            RecoverUncertain("Conflicting map transfer result.");
            return;
        }

        if (!result.success)
        {
            if (!string.IsNullOrWhiteSpace(result.sceneSessionId)
                && result.sceneSessionId != request.sceneSessionId)
            {
                RecoverUncertain("Map transfer failed with another scene session.");
                return;
            }

            string failure = result.errorCode == "client_load_failed" && preparationFailure != null
                ? preparationFailure : DescribeFailure(result.errorCode);
            ClearPending(false);
            ShowFailure(failure, false);
            session?.ResumeAfterMapTransferFailure(result.movementControl);
            BindPanel();
            GameNoticeService.Show(failure);
            return;
        }

        if (state != TransferState.AwaitingResult || ready == null || !ready.loaded
            || result.sceneId != destinationSceneId
            || result.mapId != destinationMapId
            || string.IsNullOrWhiteSpace(result.sceneSessionId)
            || result.sceneSessionId == request.sceneSessionId
            || result.self == null || result.movementControl?.IsValidFor(result.sceneSessionId) != true
            || result.snapshotRevision <= 0 || commitHandler == null
            || (stoneRequest != null && (result.itemUse == null
                || result.itemUse.itemUseOperationSessionId != stoneRequest.itemUseOperationSessionId
                || result.itemUse.requestSequence != stoneRequest.requestSequence
                || result.itemUse.stackId != stoneRequest.stackId
                || result.itemUse.remainingUses < 0 || result.itemUse.remainingUses >= 50)))
        {
            RecoverUncertain("Map transfer committed with incomplete destination data.");
            return;
        }

        StopPreload();
        state = TransferState.Committed;
        ShowStatus("正在进入地图，请稍后");
        BindPanel();
        try { commitHandler(result); }
        catch (Exception error)
        {
            Debug.LogError("Map transfer destination failed: " + error, this);
            NotifyWorldRecovery();
            session?.RecoverWorldSession("map_transfer_commit_failed");
        }
    }

    private void HandleError(string json)
    {
        if (state == TransferState.Idle || state == TransferState.CheckingResources || state == TransferState.Committed)
            return;
        ServerErrorMessage error;
        try { error = JsonUtility.FromJson<ServerErrorMessage>(json); }
        catch (Exception) { return; }
        if (error != null && error.requestMsgId == "map_transfer"
            && error.errorCode == "map_transfer_busy")
            RecoverUncertain("Gate reported an unresolved map transfer.");
    }

    private void HandleDisconnected(string reason)
    {
        bool interrupted = state != TransferState.Idle;
        if (panel != null)
            panel.Hide();
        ClearPending(!interrupted);
        if (interrupted) ShowFailure("传送失败，服务器连接中断，正在等待重新连接", true);
        BindPanel();
        if (interrupted) GameNoticeService.Show("传送失败，服务器连接失败，请等待重新连接");
    }

    private void RecoverUncertain(string reason)
    {
        Debug.LogWarning(reason, this);
        ClearPending(false);
        ShowFailure("传送未完成，正在重新连接，请稍后", true);
        GameNoticeService.Show("传送未完成，正在重新连接，请稍后");
        session?.RecoverWorldSession("map_transfer_uncertain");
        BindPanel();
    }

    private void ClearPending(bool hideStatus = true)
    {
        StopPreload();
        if (hideStatus && statusView != null) statusView.Hide();
        preparationFailure = null;
        request = null;
        stoneRequest = null;
        ready = null;
        transferId = null;
        destinationSceneId = null;
        destinationMapId = null;
        destinationAddress = null;
        connectionEpoch = 0;
        state = TransferState.Idle;
    }

    private void AbortPreflight(string message)
    {
        ClearPending(false);
        ShowFailure(message, false);
        BindPanel();
        GameNoticeService.Show(message);
    }

    private void ShowStatus(string message)
    {
        if (statusView == null) statusView = MapTransferStatusView.Create(transform);
        statusView.Show(message);
    }

    private void ShowFailure(string message, bool recovering)
    {
        if (statusView == null) statusView = MapTransferStatusView.Create(transform);
        recoveryStatus = recovering;
        statusView.ShowFailure(message, recovering);
    }

    public void NotifyWorldRecovery(string message = null)
    {
        if (state == TransferState.Idle) return;
        ClearPending(false);
        ShowFailure(message ?? "地图传送未完成，正在重新连接，请稍后", true);
        BindPanel();
        GameNoticeService.Show("地图传送未完成，正在重新连接，请稍后");
    }

    private void StopPreload()
    {
        if (preload != null)
        {
            StopCoroutine(preload);
            preload = null;
        }
        ReleaseDependencies();
    }

    private void ReleaseDependencies()
    {
        if (dependencies.IsValid())
            Addressables.Release(dependencies);
        dependencies = default;
    }

    private static string DescribeFailure(string errorCode)
    {
        switch (errorCode)
        {
            case "too_far": return "请靠近传送点";
            case "player_dead": return "死亡时无法传送";
            case "player_airborne": return "落地后再传送";
            case "route_unavailable":
            case "wrong_source_map": return "路线暂不可用，请重新选择";
            case "client_load_failed": return "目标地图资源加载失败";
            case "preparation_expired": return "传送准备超时，请重试";
            case "unsafe_destination": return "没有找到安全的传送位置";
            case "teleport_stone_unavailable": return "传送石已不可用，请刷新背包";
            default: return "传送失败，请稍后重试";
        }
    }
}
