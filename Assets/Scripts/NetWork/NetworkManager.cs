using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
using Game.Client.BattleSync;
using Stopwatch = System.Diagnostics.Stopwatch;

[DefaultExecutionOrder(-200)]
public class NetworkManager
    : MonoBehaviour,
      Game.Client.BattleSync.IBattleSyncTransport
{
    private const int MaxFrameBytes = 65536;

    public static NetworkManager Instance { get; private set; }

    [Header("服务器")]
    [SerializeField, InspectorName("环境列表"), Tooltip("可添加多套服务器地址，并在下方选择当前使用的环境。")]
    private List<NetworkEndpointProfile> endpointProfiles = new List<NetworkEndpointProfile>();
    [SerializeField] private string selectedProfileName;
    [SerializeField, InspectorName("启动时连接")] private bool connectOnStart = false;
    [SerializeField, InspectorName("切换场景时保留")] private bool dontDestroyOnLoad = true;

    [Header("连接设置")]
    [SerializeField, InspectorName("心跳间隔"), Tooltip("单位：秒。")]
    private float heartbeatInterval = 10f;
    [SerializeField, InspectorName("重连间隔"), Tooltip("单位：秒。")]
    private float reconnectDelay = 3f;
    [SerializeField, InspectorName("最大重连次数"), Tooltip("0 表示不限次数。")]
    private int maxReconnectAttempts = 0;

    [Header("主线程消息处理")]
    [SerializeField, Min(1), InspectorName("每帧最多处理数")]
    private int maxActionsPerFrame = 64;
    [SerializeField, Min(0.1f), InspectorName("每帧处理时限"), Tooltip("单位：毫秒。")]
    private float maxDispatchTimeMs = 2f;

    public event Action OnConnected;
    public event Action<string> OnMessageReceived;
    public event Action<string> OnDisconnected;
    public event Action<long> OnConnectedWithEpoch;
    public event Action<long, string> OnDisconnectedWithEpoch;

    private TcpClient client;
    private NetworkStream stream;
    private Thread receiveThread;

    private readonly object sendLock = new object();
    private readonly Queue<Action> mainThreadQueue = new Queue<Action>();

    private volatile bool shouldRun;
    private volatile bool isConnecting;
    private bool manuallyClosed;
    private bool disconnectHandled;
    private Coroutine heartbeatCoroutine;
    private Coroutine reconnectCoroutine;
    private int reconnectAttempts;
    private long connectionEpoch;
    private long receiveSequence;
    private long sceneSessionEpoch;
    private string sceneSessionId;
    private long movementEpoch, movementSequence;
    private bool movementReady;
    internal Func<bool> FlushLocalMovement { get; set; }
    internal int MovementHeartbeatIntervalMs { get; set; }
    internal float MovementGravity { get; set; }
    internal bool MovementReady => movementReady && !string.IsNullOrEmpty(SceneSessionId);
    private bool sceneOperationsPaused;
    private bool localPlayerActionsBlocked;
    private HeartbeatLatencyTracker heartbeatLatency;
    private string serverHost;
    private int serverPort;
    private int httpPort;
    private string appliedHttpBaseUrl;
    private string appliedProfileName;

    public long ConnectionEpoch =>
        Interlocked.Read(ref connectionEpoch);
    public string SceneSessionId => IsConnected && sceneSessionEpoch == ConnectionEpoch
        ? sceneSessionId : null;
    public bool SceneOperationsPaused => sceneOperationsPaused;
    public string ServerHost => serverHost;
    public int ServerPort => serverPort;
    public int HttpPort => httpPort;
    public string EndpointIdentity { get; private set; }
    public bool TryGetHeartbeatRoundTripMs(out double roundTripMs)
    {
        roundTripMs = 0;
        return shouldRun && IsConnected && heartbeatLatency != null
            && heartbeatLatency.TryGet(ConnectionEpoch, Stopwatch.GetTimestamp(), out roundTripMs);
    }

    public bool TryGetSelectedEndpointProfile(out NetworkEndpointProfile selected, out string error)
    {
        selected = null;
        error = null;
        if (endpointProfiles == null || endpointProfiles.Count == 0)
        {
            error = "Add at least one server environment.";
            return false;
        }

        HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (NetworkEndpointProfile profile in endpointProfiles)
        {
            if (profile == null || !profile.TryValidate(out error))
            {
                if (profile == null)
                    error = "Server environment is missing.";
                return false;
            }

            if (!names.Add(profile.Name))
            {
                error = "Server environment names must be unique.";
                return false;
            }

            if (string.Equals(profile.Name, selectedProfileName, StringComparison.Ordinal))
                selected = profile;
        }

        if (selected == null)
        {
            error = "Select a valid server environment.";
            return false;
        }

        return true;
    }

    public LootPickupSessionState LootPickupSession { get; } = new LootPickupSessionState();

    public bool IsConnected
    {
        get
        {
            return client != null && client.Connected && stream != null;
        }
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            enabled = false;
            Destroy(this);
            return;
        }

        Instance = this;
        if (!TryConfigureServerEndpoint(NetworkEndpointRuntime.Load(), out string error))
            Debug.LogError("Gate server configuration failed: " + error);
        heartbeatLatency = new HeartbeatLatencyTracker(heartbeatInterval);

        if (dontDestroyOnLoad)
        {
            DontDestroyOnLoad(gameObject);
        }
    }

    private void Start()
    {
        if (connectOnStart)
        {
            Connect();
        }
    }

    private void Update()
    {
        int processed = 0;
        long updateStartedAtMs = BattleSyncDiagnosticsLogger.NowMilliseconds;
        float deadline = Time.realtimeSinceStartup + maxDispatchTimeMs / 1000f;
        bool budgetReached = false;
        while (processed < maxActionsPerFrame
            && Time.realtimeSinceStartup < deadline)
        {
            Action action = null;

            lock (mainThreadQueue)
            {
                if (mainThreadQueue.Count > 0)
                {
                    action = mainThreadQueue.Dequeue();
                }
            }

            if (action == null)
            {
                break;
            }

            try
            {
                action.Invoke();
            }
            catch (Exception exception)
            {
                BattleSyncDiagnosticsLogger.LogDispatchException(
                    "<unknown>",
                    0,
                    exception.Message);
                throw;
            }
            processed++;
        }

        if (processed >= maxActionsPerFrame
            || Time.realtimeSinceStartup >= deadline)
        {
            budgetReached = true;
        }

        int queueDepth = GetMainThreadQueueCount();
        BattleSyncDiagnosticsLogger.LogQueueMetrics(
            ConnectionEpoch,
            queueDepth,
            processed,
            BattleSyncDiagnosticsLogger.NowMilliseconds - updateStartedAtMs,
            maxActionsPerFrame,
            maxDispatchTimeMs,
            budgetReached);
    }

    private void OnDestroy()
    {
        if (Instance != this)
        {
            return;
        }

        Disconnect();
        Instance = null;
    }

    public void Connect()
    {
        if (!TryConfigureServerEndpoint(NetworkEndpointRuntime.Load(), out string error))
        {
            Debug.LogError("Gate connection skipped: " + error);
            return;
        }

        Connect(serverHost, serverPort);
    }

    public bool TryConfigureServerEndpoint(NetworkEndpointSnapshot settings, out string error)
    {
        error = null;
        if (settings == null)
        {
            error = "Gate server configuration is missing or invalid.";
            return false;
        }

        if (IsConnected || isConnecting || reconnectCoroutine != null)
        {
            if (serverHost != settings.ServerHost || serverPort != settings.TcpPort
                || appliedHttpBaseUrl != settings.HttpBaseUrl
                || appliedProfileName != settings.SelectedProfileName)
            {
                error = "An existing Gate connection uses another server. Restart the game to apply the new endpoint.";
                return false;
            }

            return true;
        }

        serverHost = settings.ServerHost;
        serverPort = settings.TcpPort;
        httpPort = settings.HttpPort;
        appliedHttpBaseUrl = settings.HttpBaseUrl;
        appliedProfileName = settings.SelectedProfileName;
        EndpointIdentity = settings.Identity;
        return true;
    }

    private void Connect(string host, int port)
    {
        if (!NetworkEndpointSettings.IsValid(host, port, httpPort))
        {
            Debug.LogError("Gate connection skipped: server host or port is not configured.");
            return;
        }

        if (IsConnected || isConnecting)
        {
            return;
        }

        serverHost = host;
        serverPort = port;
        manuallyClosed = false;
        shouldRun = true;
        isConnecting = true;

        receiveThread = new Thread(ConnectAndReceiveLoop);
        receiveThread.IsBackground = true;
        receiveThread.Start();
    }

    public void SuspendAutoReconnect()
    {
        manuallyClosed = true;
        StopReconnect();
    }

    public void Disconnect()
    {
        SuspendAutoReconnect();
        shouldRun = false;
        sceneSessionId = null;
        ResetMovementSession();
        sceneSessionEpoch = 0;
        sceneOperationsPaused = false;
        localPlayerActionsBlocked = false;

        long disconnectedEpoch = ConnectionEpoch;

        StopHeartbeat();
        heartbeatLatency?.Reset(0);
        CloseSocket();
        BattleSyncDiagnosticsLogger.LogConnection(
            "client_disconnect",
            disconnectedEpoch,
            serverHost,
            serverPort,
            "Client disconnected");

        EnqueueMainThread(() =>
        {
            OnDisconnected?.Invoke("Client disconnected");
            OnDisconnectedWithEpoch?.Invoke(
                disconnectedEpoch,
                "Client disconnected");
        });
    }

    public bool Send<T>(T message)
    {
        bool transferRequest = message is MapTransferRequest;
        bool playerOperation = message is MovementReferencedRequest
            || message is Game.Inventory.Protocol.V2.InventoryV2ItemUseRequestDto;
        if (localPlayerActionsBlocked && playerOperation)
            return false;
        if (playerOperation && sceneOperationsPaused && !transferRequest)
            return false;

        string currentSceneSessionId = SceneSessionId;
        if (message is MovementReferencedRequest operation)
        {
            // Committed/prepared transfer retries retain the original request and reference.
            if (transferRequest && operation.movementEpoch > 0)
            {
                if (operation.sceneSessionId != currentSceneSessionId) return false;
            }
            else
            {
                if (!TryFlushMovement()) return false;
                operation.sceneSessionId = currentSceneSessionId;
                operation.movementEpoch = movementEpoch;
                operation.movementSequence = movementSequence;
            }
        }
        else if (message is RespawnRequest respawn)
            respawn.sceneSessionId = currentSceneSessionId;
        else if (message is Game.Inventory.Protocol.V2.InventoryV2ItemUseRequestDto itemUse)
            itemUse.sceneSessionId = currentSceneSessionId;

        if ((playerOperation || message is RespawnRequest) && string.IsNullOrEmpty(currentSceneSessionId))
            return false;
        if (message is MapTransferReadyRequest ready
            && string.IsNullOrWhiteSpace(ready.sceneSessionId))
            return false;

        string json = JsonUtility.ToJson(message);
        return SendJson(json);
    }

    internal bool SendMeditation(MeditationSetRequest request, bool firstSend)
    {
        if (string.IsNullOrEmpty(SceneSessionId) || request.sceneSessionId != SceneSessionId
            || sceneOperationsPaused || localPlayerActionsBlocked)
            return false;
        if (firstSend && request.isMeditating)
        {
            if (!MovementReady) return false;
            request.movementEpoch = movementEpoch;
            request.movementSequence = movementSequence;
        }
        return Send(request);
    }

    public bool SetSceneSessionId(long epoch, string value)
    {
        if (!IsConnected || epoch <= 0 || epoch != ConnectionEpoch
            || string.IsNullOrWhiteSpace(value))
            return false;

        if (sceneSessionEpoch != epoch || sceneSessionId != value) ResetMovementSession();
        sceneSessionEpoch = epoch;
        sceneSessionId = value;
        return true;
    }

    public bool SetSceneOperationsPaused(long epoch, bool paused)
    {
        if (!IsConnected || epoch <= 0 || epoch != ConnectionEpoch)
            return false;

        sceneOperationsPaused = paused;
        return true;
    }

    internal void SetLocalPlayerActionsBlocked(bool blocked)
    {
        localPlayerActionsBlocked = blocked;
    }

    internal bool TryFlushMovement() =>
        MovementReady && (sceneOperationsPaused || FlushLocalMovement?.Invoke() == true);

    internal void ResetMovementSession()
    {
        movementEpoch = movementSequence = 0;
        movementReady = false;
        MovementHeartbeatIntervalMs = 0;
        MovementGravity = 0f;
    }

    internal void BeginMovementControl(MovementEventControl control)
    {
        movementEpoch = control.epoch;
        movementSequence = control.nextSequence - 1;
        movementReady = false;
    }

    internal bool AcceptMovementControl(MovementControlEvaluation result)
    {
        if (result?.success != true || result.needsControlAck || result.control?.mode != "normal"
            || result.control.sceneSessionId != SceneSessionId || result.control.epoch != movementEpoch
            || result.acceptedSequence != movementSequence)
            return false;
        movementReady = true;
        return true;
    }

    internal bool SendMovementEvent(MovementEventRequest message)
    {
        if (!MovementReady || sceneOperationsPaused || localPlayerActionsBlocked)
            return false;
        message.sceneSessionId = SceneSessionId;
        message.movementEpoch = movementEpoch;
        message.sequence = movementSequence + 1;
        if (!Send(message)) return false;
        movementSequence = message.sequence;
        BattleSyncDiagnosticsLogger.LogMovementEventSent(ConnectionEpoch, message);
        return true;
    }

    public bool SendCombatAction(CombatActionRequest message)
    {
        if (!IsConnected
            || message == null
            || message.actionSequence <= 0
            || message.clientTick <= 0
            || string.IsNullOrWhiteSpace(message.actionId))
        {
            return false;
        }

        bool hasTargetType = !string.IsNullOrWhiteSpace(message.targetType);
        bool hasTargetId = !string.IsNullOrWhiteSpace(message.targetId);
        if (hasTargetType != hasTargetId)
        {
            return false;
        }

        if (hasTargetType
            && !string.Equals(message.targetType, "player", StringComparison.Ordinal)
            && !string.Equals(message.targetType, "monster", StringComparison.Ordinal))
        {
            return false;
        }

        return Send(message);
    }

    public bool SendSkillCast(SkillCastRequest message)
    {
        if (!IsConnected
            || message == null
            || message.requestSequence <= 0
            || message.clientTick <= 0
            || string.IsNullOrWhiteSpace(message.skillId)
            || (message.targetType != "monster" && message.targetType != "player")
            || string.IsNullOrWhiteSpace(message.targetId))
        {
            return false;
        }

        return Send(message);
    }

    public bool SendRespawn(RespawnRequest message)
    {
        if (!IsConnected
            || message == null
            || message.requestSequence <= 0
            || message.clientTick <= 0)
        {
            return false;
        }

        return Send(message);
    }

    public bool SendJson(string json)
    {
        return SendJson(json, null);
    }

    private bool SendJson(string json, Action<long> beforeWrite)
    {
        if (!IsConnected || string.IsNullOrEmpty(json))
        {
            return false;
        }

        long sendEpoch = ConnectionEpoch;
        try
        {
            string payload = json.EndsWith("\n", StringComparison.Ordinal)
                ? json
                : json + "\n";
            byte[] body = Encoding.UTF8.GetBytes(payload);
            if (body.Length > MaxFrameBytes)
            {
                throw new Exception("Message too large: " + body.Length);
            }

            lock (sendLock)
            {
                beforeWrite?.Invoke(sendEpoch);
                stream.Write(body, 0, body.Length);
                stream.Flush();
            }

            return true;
        }
        catch (Exception ex)
        {
            BattleSyncDiagnosticsLogger.LogSendFailure(
                ConnectionEpoch,
                json == null ? 0 : Encoding.UTF8.GetByteCount(json),
                ex.Message);
            EnqueueMainThread(() =>
            {
                if (!ShouldHandleConnectionFailure(sendEpoch))
                    return;

                HandleDisconnected("Send failed: " + ex.Message);
            });

            return false;
        }
    }

    private void ConnectAndReceiveLoop()
    {
        long connectedEpoch = 0;
        try
        {
            client = new TcpClient();
            client.NoDelay = true;
            client.SendTimeout = 3000;
            client.Connect(serverHost, serverPort);

            stream = client.GetStream();
            if (!shouldRun)
            {
                isConnecting = false;
                CloseSocket();
                return;
            }

            disconnectHandled = false;
            reconnectAttempts = 0;

            connectedEpoch = Interlocked.Increment(ref connectionEpoch);
            heartbeatLatency?.Reset(connectedEpoch);
            isConnecting = false;

            BattleSyncDiagnosticsLogger.LogConnection(
                "connected",
                connectedEpoch,
                serverHost,
                serverPort,
                string.Empty);

            EnqueueMainThread(() =>
            {
                if (!ShouldDispatchReceivedMessage(connectedEpoch))
                    return;

                StartHeartbeat();
                OnConnected?.Invoke();
                OnConnectedWithEpoch?.Invoke(
                    connectedEpoch);
            });

            while (shouldRun && client != null && client.Connected)
            {
                string message = ReadNewlineDelimitedMessage(stream);
                if (string.IsNullOrEmpty(message))
                {
                    throw new Exception("Server closed connection");
                }

                long receivedAtTicks = Stopwatch.GetTimestamp();
                if (message.IndexOf("heartbeat_result", StringComparison.Ordinal) >= 0)
                {
                    try
                    {
                        HeartbeatAckMessage reply = JsonUtility.FromJson<HeartbeatAckMessage>(message);
                        if (reply != null && string.Equals(reply.msgId, "heartbeat_result", StringComparison.Ordinal))
                            heartbeatLatency?.Complete(connectedEpoch, reply.clientPingId, receivedAtTicks);
                    }
                    catch (Exception)
                    {
                        // The normal message dispatcher will report malformed JSON.
                    }
                }

                long receivedAtMs = BattleSyncDiagnosticsLogger.NowMilliseconds;
                long messageSequence = Interlocked.Increment(ref receiveSequence);
                int byteCount = Encoding.UTF8.GetByteCount(message);

                EnqueueMainThread(() =>
                {
                    if (!ShouldDispatchReceivedMessage(connectedEpoch))
                        return;

                    BattleSyncDiagnosticsLogger.MarkMessageDequeued(
                        connectedEpoch,
                        messageSequence,
                        receivedAtMs,
                        byteCount,
                        GetMainThreadQueueCount());
                    OnMessageReceived?.Invoke(message);
                });
            }
        }
        catch (Exception ex)
        {
            if (connectedEpoch > 0
                && (connectedEpoch != ConnectionEpoch || isConnecting))
                return;

            isConnecting = false;

            if (shouldRun)
            {
                heartbeatLatency?.Reset(0);
                long failedEpoch = connectedEpoch > 0
                    ? connectedEpoch : ConnectionEpoch;
                BattleSyncDiagnosticsLogger.LogConnection(
                    "receive_error",
                    failedEpoch,
                    serverHost,
                    serverPort,
                    ex.Message);
                EnqueueMainThread(() =>
                {
                    if (!ShouldHandleConnectionFailure(failedEpoch))
                        return;

                    HandleDisconnected("Connection lost: " + ex.Message);
                });
            }
        }
    }

    private static string ReadNewlineDelimitedMessage(NetworkStream networkStream)
    {
        using (MemoryStream messageBuffer = new MemoryStream())
        {
            while (true)
            {
                int value = networkStream.ReadByte();
                if (value < 0)
                {
                    return null;
                }

                if (value == '\n')
                {
                    break;
                }

                if (value != '\r')
                {
                    messageBuffer.WriteByte((byte)value);
                }

                if (messageBuffer.Length > MaxFrameBytes)
                {
                    throw new Exception("Message too large: " + messageBuffer.Length);
                }
            }

            return Encoding.UTF8.GetString(messageBuffer.ToArray());
        }
    }

    private bool ShouldDispatchReceivedMessage(long receivedEpoch)
    {
        return shouldRun && !isConnecting
            && receivedEpoch > 0 && receivedEpoch == ConnectionEpoch;
    }

    private bool ShouldHandleConnectionFailure(long failedEpoch)
    {
        return shouldRun && !isConnecting
            && failedEpoch == ConnectionEpoch;
    }

    private void HandleDisconnected(string reason)
    {
        if (disconnectHandled)
        {
            return;
        }

        disconnectHandled = true;
        shouldRun = false;
        sceneSessionId = null;
        ResetMovementSession();
        sceneSessionEpoch = 0;
        sceneOperationsPaused = false;
        localPlayerActionsBlocked = false;

        long disconnectedEpoch = ConnectionEpoch;

        StopHeartbeat();
        heartbeatLatency?.Reset(0);
        CloseSocket();

        BattleSyncDiagnosticsLogger.LogConnection(
            "disconnected",
            disconnectedEpoch,
            serverHost,
            serverPort,
            reason);

        OnDisconnected?.Invoke(reason);
        OnDisconnectedWithEpoch?.Invoke(
            disconnectedEpoch,
            reason);

        if (!manuallyClosed)
        {
            StartReconnect();
        }
    }

    private void StartHeartbeat()
    {
        StopHeartbeat();
        heartbeatCoroutine = StartCoroutine(HeartbeatLoop());
    }

    private void StopHeartbeat()
    {
        if (heartbeatCoroutine != null)
        {
            StopCoroutine(heartbeatCoroutine);
            heartbeatCoroutine = null;
        }
    }

    private IEnumerator HeartbeatLoop()
    {
        while (IsConnected)
        {
            yield return new WaitForSeconds(heartbeatInterval);

            HeartbeatMessage message = new HeartbeatMessage();
            message.msgId = "heartbeat";
            message.type = "heartbeat";
            message.time = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            message.clientPingId = Guid.NewGuid().ToString("N");

            string json = JsonUtility.ToJson(message);
            bool sent = SendJson(json, epoch =>
                heartbeatLatency?.Begin(epoch, message.clientPingId, Stopwatch.GetTimestamp()));
            if (!sent)
                heartbeatLatency?.Forget(ConnectionEpoch, message.clientPingId);
        }
    }

    private void StartReconnect()
    {
        if (reconnectCoroutine != null)
        {
            return;
        }

        reconnectCoroutine = StartCoroutine(ReconnectLoop());
    }

    private void StopReconnect()
    {
        if (reconnectCoroutine != null)
        {
            StopCoroutine(reconnectCoroutine);
            reconnectCoroutine = null;
        }
    }

    private IEnumerator ReconnectLoop()
    {
        while (!manuallyClosed && !IsConnected)
        {
            if (maxReconnectAttempts > 0 && reconnectAttempts >= maxReconnectAttempts)
            {
                break;
            }

            reconnectAttempts++;
            yield return new WaitForSecondsRealtime(reconnectDelay);

            Connect(serverHost, serverPort);

            while (isConnecting)
            {
                yield return null;
            }

            if (IsConnected)
            {
                break;
            }
        }

        reconnectCoroutine = null;
    }

    private void CloseSocket()
    {
        try
        {
            stream?.Close();
        }
        catch
        {
        }

        try
        {
            client?.Close();
        }
        catch
        {
        }

        stream = null;
        client = null;
    }

    private void EnqueueMainThread(Action action)
    {
        lock (mainThreadQueue)
        {
            mainThreadQueue.Enqueue(action);
        }
    }

    private int GetMainThreadQueueCount()
    {
        lock (mainThreadQueue)
        {
            return mainThreadQueue.Count;
        }
    }

    [Serializable]
    private class HeartbeatMessage
    {
        public string msgId;
        public string type;
        public long time;
        public string clientPingId;
    }
}
