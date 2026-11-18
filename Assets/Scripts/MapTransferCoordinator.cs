using System;
using System.Collections;
using Game.UI.Notice;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

[DisallowMultipleComponent]
public sealed class MapTransferCoordinator : MonoBehaviour
{
    private enum TransferState { Idle, Requested, Preparing, AwaitingResult, Committed }

    private const float RequestTimeoutSeconds = 5f;
    private const float PreparationTimeoutSeconds = 25f;
    private const float ResultTimeoutSeconds = 10f;

    private GameSession session;
    private NetworkManager network;
    private NetMessageDispatcher dispatcher;
    private VillageTeleportPanel panel;
    private bool panelHandlerBound;
    private Action<MapTransferResult> commitHandler;
    private TransferState state;
    private MapTransferRequest request;
    private MapTransferReadyRequest ready;
    private string transferId;
    private string destinationSceneId;
    private string destinationMapId;
    private string destinationAddress;
    private long connectionEpoch;
    private float deadline;
    private int retries;
    private Coroutine preload;
    private AsyncOperationHandle dependencies;

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
            RecoverUncertain("Transfer destination handler was removed.");
        BindPanel();
    }

    public void CompleteCommittedTransfer()
    {
        if (state != TransferState.Committed)
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

        if (state == TransferState.Idle)
            BindPanel();
        if (state == TransferState.Idle || state == TransferState.Committed)
            return;

        if (!network.IsConnected || network.ConnectionEpoch != connectionEpoch)
        {
            ClearPending();
            BindPanel();
            return;
        }
        if (Time.realtimeSinceStartup < deadline)
            return;

        if (state == TransferState.Preparing)
        {
            StopPreload();
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
            ? network.Send(request) : network.Send(ready);
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
            || route == null || string.IsNullOrWhiteSpace(route.routeId)
            || route.routeId.Length > 64
            || session == null || !session.IsWorldReady || network == null
            || !network.IsConnected || string.IsNullOrWhiteSpace(session.SceneSessionId))
            return;

        if (!WorldMapSceneCatalog.TryGetAddress(route.sceneId, route.mapId, out _))
        {
            GameNoticeService.Show("目标地图暂不可用");
            return;
        }
        if (session.IsCurrentMap(route.sceneId, route.mapId))
        {
            GameNoticeService.Show("已在当前地图，无法传送");
            return;
        }
        if (!session.TryPauseForMapTransfer())
            return;

        destinationSceneId = route.sceneId;
        destinationMapId = route.mapId;
        request = new MapTransferRequest
        {
            requestId = Guid.NewGuid().ToString("D"),
            routeId = route.routeId,
            sceneSessionId = session.SceneSessionId
        };
        connectionEpoch = network.ConnectionEpoch;
        state = TransferState.Requested;
        retries = 0;
        deadline = Time.realtimeSinceStartup + RequestTimeoutSeconds;
        BindPanel();
        if (!network.Send(request))
        {
            session.ResumeAfterMapTransferFailure(0);
            ClearPending();
            BindPanel();
            GameNoticeService.Show("传送请求发送失败");
            return;
        }

        if (panel != null)
            panel.Hide();
    }

    private void HandlePrepare(string json)
    {
        if (state == TransferState.Idle || state == TransferState.Committed
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
        deadline = Time.realtimeSinceStartup + PreparationTimeoutSeconds;
        preload = StartCoroutine(PreloadDestination(destinationAddress));
    }

    private IEnumerator PreloadDestination(string address)
    {
        try
        {
            dependencies = Addressables.DownloadDependenciesAsync(address);
        }
        catch (Exception error)
        {
            Debug.LogWarning("Map dependencies could not be requested: " + error, this);
            preload = null;
            SendReady(false);
            yield break;
        }

        yield return dependencies;
        bool loaded = dependencies.Status == AsyncOperationStatus.Succeeded;
        if (!loaded)
            Debug.LogWarning("Map dependencies failed to load: "
                + dependencies.OperationException, this);
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
        retries = 0;
        deadline = Time.realtimeSinceStartup + ResultTimeoutSeconds;
        if (network == null || !network.Send(ready))
            RecoverUncertain("Map transfer readiness could not be sent.");
    }

    private void HandleResult(string json)
    {
        if (state == TransferState.Idle || state == TransferState.Committed
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

            long nextSequence = result.nextInputSequence;
            ClearPending();
            session?.ResumeAfterMapTransferFailure(nextSequence);
            BindPanel();
            GameNoticeService.Show(DescribeFailure(result.errorCode));
            return;
        }

        if (state != TransferState.AwaitingResult || ready == null || !ready.loaded
            || result.sceneId != destinationSceneId
            || result.mapId != destinationMapId
            || string.IsNullOrWhiteSpace(result.sceneSessionId)
            || result.sceneSessionId == request.sceneSessionId
            || result.self == null || result.nextInputSequence <= 0
            || result.snapshotRevision <= 0 || commitHandler == null)
        {
            RecoverUncertain("Map transfer committed with incomplete destination data.");
            return;
        }

        StopPreload();
        state = TransferState.Committed;
        BindPanel();
        try { commitHandler(result); }
        catch (Exception error)
        {
            Debug.LogError("Map transfer destination failed: " + error, this);
            session?.RecoverWorldSession("map_transfer_commit_failed");
        }
    }

    private void HandleError(string json)
    {
        if (state == TransferState.Idle || state == TransferState.Committed)
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
        if (panel != null)
            panel.Hide();
        ClearPending();
        BindPanel();
    }

    private void RecoverUncertain(string reason)
    {
        Debug.LogWarning(reason, this);
        ClearPending();
        session?.RecoverWorldSession("map_transfer_uncertain");
        BindPanel();
    }

    private void ClearPending()
    {
        StopPreload();
        request = null;
        ready = null;
        transferId = null;
        destinationSceneId = null;
        destinationMapId = null;
        destinationAddress = null;
        connectionEpoch = 0;
        state = TransferState.Idle;
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
            case "too_far": return "请靠近传送员";
            case "player_dead": return "死亡状态无法传送";
            case "player_airborne": return "落地后再传送";
            case "route_unavailable":
            case "wrong_source_map": return "路线暂不可用，请重新打开菜单";
            case "client_load_failed": return "目标地图资源加载失败";
            case "preparation_expired": return "传送准备超时，请重试";
            default: return "传送失败，请稍后重试";
        }
    }
}
