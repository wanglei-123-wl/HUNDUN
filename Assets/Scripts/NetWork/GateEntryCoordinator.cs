using System;
using Game.Chat.Runtime;
using Game.Chat.Transport;
using UnityEngine;

public sealed class GateEntryCoordinator : IDisposable
{
    private readonly NetworkManager network;
    private readonly NetMessageDispatcher dispatcher;
    private readonly long characterDbId;
    private readonly string characterId;
    private readonly Action onAccepted;
    private readonly Action<string> onFailed;
    private readonly double deadline;
    private readonly bool retryConnectionLoss;
    private readonly float responseTimeout;
    private double responseDeadline;
    private bool reconnectAfterTimeout;
    private AuthSession auth;
    private bool awaitingResult;
    private double retryAt;
    private float retryDelay = 1f;
    private bool completed;
    private bool disposed;
    private long sentEpoch;
    private int receivedMessages;
    private string lastMessageId = "<none>";

    public bool IsCompleted => completed;

    public GateEntryCoordinator(NetworkManager network, NetMessageDispatcher dispatcher,
        long characterDbId, string characterId, float timeoutSeconds,
        Action onAccepted, Action<string> onFailed)
        : this(network, dispatcher, characterDbId, characterId, timeoutSeconds,
            onAccepted, onFailed, false, timeoutSeconds)
    {
    }

    public GateEntryCoordinator(NetworkManager network, NetMessageDispatcher dispatcher,
        long characterDbId, string characterId, float timeoutSeconds,
        Action onAccepted, Action<string> onFailed, bool retryConnectionLoss, float responseTimeout)
    {
        this.network = network;
        this.dispatcher = dispatcher;
        this.characterDbId = characterDbId;
        this.characterId = characterId;
        this.onAccepted = onAccepted;
        this.onFailed = onFailed;
        this.retryConnectionLoss = retryConnectionLoss;
        this.responseTimeout = Mathf.Max(1f, responseTimeout);
        deadline = Time.realtimeSinceStartupAsDouble + Mathf.Max(0.1f, timeoutSeconds);
    }

    public void Begin()
    {
        auth = AuthSession.Instance;
        if (network == null || dispatcher == null || characterDbId <= 0
            || string.IsNullOrWhiteSpace(characterId)
            || auth == null || !auth.HasValidGameSession)
        {
            Fail("游戏会话缺失或已过期，请重试进入。");
            return;
        }

        WorldChatTransportInbox inbox = WorldChatTransportInbox.Ensure(network, dispatcher);
        inbox.PrepareEnterWorld();
        WorldChatSessionRuntime.Ensure(network, inbox).BeginSession();
        GateEntryHandoff.BeginAttempt();
        dispatcher.BeginDeferredWorldMessages();
        dispatcher.DeferredDispatchOverflow += HandleOverflow;
        dispatcher.Register("enter_world_result", HandleResult);
        auth.GameSessionEnded += HandleGameSessionEnded;
        network.OnConnected += HandleConnected;
        network.OnDisconnected += HandleDisconnected;
        network.OnMessageReceived += ObserveIncomingMessage;
        if (network.IsConnected) HandleConnected();
        else network.Connect();
    }

    public void CheckTimeout()
    {
        if (completed || disposed || (auth != null && auth.IsLoggingOut)) return;
        double now = Time.realtimeSinceStartupAsDouble;
        if (now >= deadline)
        {
            Debug.LogWarning("[GateEntry][TIMEOUT] sentEpoch=" + sentEpoch
                + ", currentEpoch=" + (network == null ? 0 : network.ConnectionEpoch)
                + ", connected=" + (network != null && network.IsConnected)
                + ", receivedMessages=" + receivedMessages
                + ", lastMsgId=" + lastMessageId
                + ", dispatcherSubscribed=" + (dispatcher != null
                    && dispatcher.HasNetworkSubscription));
            Fail("等待游戏服务器确认进场超时。");
            return;
        }
        if (retryConnectionLoss && awaitingResult && now >= responseDeadline)
        {
            ScheduleRetry();
            reconnectAfterTimeout = true;
            network.Disconnect();
            return;
        }
        if (awaitingResult || now < retryAt) return;
        if (network.IsConnected)
            HandleConnected();
        else if (reconnectAfterTimeout)
        {
            reconnectAfterTimeout = false;
            network.Connect();
        }
    }

    private void ObserveIncomingMessage(string json)
    {
        if (completed || disposed) return;
        receivedMessages++;
        try
        {
            NetMessageBase header = JsonUtility.FromJson<NetMessageBase>(json);
            lastMessageId = header == null || string.IsNullOrWhiteSpace(header.msgId)
                ? "<missing>" : header.msgId;
        }
        catch (Exception)
        {
            lastMessageId = "<invalid-json>";
        }

        if (receivedMessages <= 16)
        {
            Debug.Log("[GateEntry][RX] epoch=" + network.ConnectionEpoch
                + ", msgId=" + lastMessageId);
        }
    }

    private void HandleConnected()
    {
        if (completed || disposed || !network.IsConnected || (auth != null && auth.IsLoggingOut)) return;
        long epoch = network.ConnectionEpoch;
        if (awaitingResult && sentEpoch == epoch) return;
        if (!retryConnectionLoss && sentEpoch > 0 && sentEpoch != epoch)
        {
            Fail("进场时与游戏服务器的连接已变化。");
            return;
        }

        if (auth == null || !auth.HasValidGameSession || string.IsNullOrWhiteSpace(auth.UserId))
        {
            Fail("游戏会话缺失或已过期，请重新登录。");
            return;
        }

        if (!auth.TrySendEnterWorld(network, dispatcher, characterDbId, characterId))
        {
            if (retryConnectionLoss) ScheduleRetry();
            else Fail("进场请求未能发送到游戏服务器。");
            return;
        }

        sentEpoch = epoch;
        awaitingResult = true;
        responseDeadline = Time.realtimeSinceStartupAsDouble + responseTimeout;
        Debug.Log("Send enter_world: characterDbId=" + characterDbId
            + ", characterId=" + characterId
            + ", epoch=" + sentEpoch
            + ", dispatcherSubscribed=" + dispatcher.HasNetworkSubscription);
    }

    private void HandleResult(string json)
    {
        if (completed || disposed || !awaitingResult || auth == null
            || !auth.IsCurrentGameEntry(network, sentEpoch)) return;
        awaitingResult = false;

        EnterWorldResult result;
        try { result = JsonUtility.FromJson<EnterWorldResult>(json); }
        catch (Exception) { Fail("游戏服务器返回的进场资料格式有误。"); return; }

        if (result == null || !result.success)
        {
            if (AuthSession.IsRetryableGameSessionReason(result?.reason))
            {
                ScheduleRetry();
                return;
            }

            if (AuthSession.IsTerminalGameSessionReason(result?.reason))
            {
                auth.ClearSession();
                Fail(AuthSession.GameSessionEndedMessage(result.reason));
                return;
            }

            Fail(result == null || string.IsNullOrWhiteSpace(result.message)
                ? "游戏服务器拒绝了进场请求。" : result.message);
            return;
        }

        if (!MovementEventPolicy.Accepts(result))
        {
            Fail("游戏服务器未提供移动协议 V2，请更新服务端。");
            return;
        }

        long resultDbId = result.characterDbId > 0
            ? result.characterDbId : (result.self == null ? 0 : result.self.characterDbId);
        string userId = auth.UserId;
        if (resultDbId != characterDbId || result.self == null
            || (result.self.characterDbId > 0 && result.self.characterDbId != characterDbId)
            || (!string.IsNullOrWhiteSpace(result.userId) && result.userId != userId)
            || (!string.IsNullOrWhiteSpace(result.self.userId) && result.self.userId != userId)
            || (!string.IsNullOrWhiteSpace(result.characterId)
                && result.characterId != characterId)
            || (!string.IsNullOrWhiteSpace(result.self.characterId)
                && result.self.characterId != characterId)
            || (!string.IsNullOrWhiteSpace(result.self.sceneId)
                && result.self.sceneId != result.sceneId)
            || string.IsNullOrWhiteSpace(result.sceneSessionId)
            || result.snapshotRevision <= 0
            || result.serverTime <= 0)
        {
            Fail("游戏服务器返回的角色或出生点资料不完整或不匹配。");
            return;
        }

        if (!WorldMapSceneCatalog.TryGetAddress(result.sceneId, result.mapId, out _))
        {
            Fail("服务器指定的地图在当前客户端不可用，请检查客户端资源。");
            return;
        }

        if (!auth.AcceptGameEntry(network, sentEpoch, result))
        {
            Fail("游戏会话已变化，请重新登录。");
            return;
        }

        completed = true;
        GateEntryHandoff.Store(sentEpoch, characterDbId, result.sceneId, result.mapId, json);
        Debug.Log("[GateEntry] enter_world_result success; scene=" + result.sceneId
            + ", map=" + result.mapId + ", characterDbId=" + resultDbId
            + ", epoch=" + sentEpoch);
        onAccepted?.Invoke();
    }

    private void HandleDisconnected(string reason)
    {
        if (completed && GateEntryHandoff.HasPending)
        {
            InvalidateAccepted("加载地图时与游戏服务器的连接已中断。" + reason);
        }
        else if (!completed && !disposed && (auth == null || !auth.IsLoggingOut))
        {
            if (retryConnectionLoss)
            {
                if (!reconnectAfterTimeout) ScheduleRetry();
            }
            else Fail("进场时与游戏服务器的连接已中断。" + reason);
        }
    }

    private void ScheduleRetry()
    {
        awaitingResult = false;
        retryAt = Time.realtimeSinceStartupAsDouble + retryDelay;
        retryDelay = Mathf.Min(5f, retryDelay * 2f);
    }

    private void HandleGameSessionEnded(string reason)
    {
        if (disposed) return;
        string message = AuthSession.GameSessionEndedMessage(reason);
        if (completed) InvalidateAccepted(message);
        else Fail(message);
    }

    private void HandleOverflow()
    {
        if (completed && GateEntryHandoff.HasPending)
            InvalidateAccepted("加载时收到的世界消息超出队列容量。");
        else
            Fail("加载时收到的世界消息超出队列容量。");
    }

    private void InvalidateAccepted(string message)
    {
        GateEntryHandoff.Invalidate();
        GateEntryHandoff.SetFailure(message);
        dispatcher?.ClearDeferredWorldMessages();
        if (network != null && network.IsConnected) network.Disconnect();
    }

    private void Fail(string message)
    {
        if (completed || disposed) return;
        completed = true;
        GateEntryHandoff.Clear();
        dispatcher?.ClearDeferredWorldMessages();
        Dispose();
        network?.Disconnect();
        onFailed?.Invoke(message);
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (dispatcher != null)
        {
            dispatcher.Unregister("enter_world_result", HandleResult);
            dispatcher.DeferredDispatchOverflow -= HandleOverflow;
            if (!GateEntryHandoff.HasPending) dispatcher.ClearDeferredWorldMessages();
        }
        if (auth != null) auth.GameSessionEnded -= HandleGameSessionEnded;
        if (network != null)
        {
            network.OnConnected -= HandleConnected;
            network.OnDisconnected -= HandleDisconnected;
            network.OnMessageReceived -= ObserveIncomingMessage;
        }
    }
}
