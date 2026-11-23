// AuthSession.cs
// 这个脚本负责统一保存、读取、清除登录状态，例如 token、userId、username。
// 登录成功后，其他脚本应该统一调用这里保存登录状态。
// 以后进入游戏、请求角色、请求背包、请求服务器数据时，也应该从这里读取 token。
// 这个脚本不负责登录请求、不负责注册请求、不负责 UI 显示、不负责判断账号密码是否正确。

using System;
using System.Collections;
using System.Globalization;
using UnityEngine; // 使用 Unity 的 MonoBehaviour 和 PlayerPrefs。

public class AuthSession : MonoBehaviour // 登录状态管理脚本，挂在 Scene/AuthSystem 上。
{
    public static AuthSession Instance { get; private set; } // 全局访问入口，方便其他脚本调用 AuthSession.Instance。

    private const string TokenKey = "auth_token"; // 本地保存 token 的键名。
    private const string UserIdKey = "auth_user_id"; // 本地保存 userId 的键名。
    private const string UsernameKey = "auth_username"; // 本地保存 username 的键名。
    private const string GameTokenKey = "auth_game_session_token";
    private const string GameTokenExpiryKey = "auth_game_session_expires_at";
    private const string GameReconnectWindowKey = "auth_game_session_reconnect_window";
    private const string EndpointIdentityKey = "auth_endpoint_identity";
    private const string SelectedCharacterIdKey = "selected_character_id";
    private const string SelectedCharacterDbIdKey = "selected_character_db_id";

    public string Token { get; private set; } // 当前登录 token，只能由 AuthSession 内部修改。
    public string LoginToken => Token;
    public string GameSessionToken { get; private set; }
    public long GameSessionExpiresAt { get; private set; }
    public int ReconnectWindowSeconds { get; private set; }
    public bool IsLoggingOut { get; private set; }
    public bool HasValidGameSession =>
        !IsLoggingOut && !string.IsNullOrWhiteSpace(GameSessionToken)
        && GameSessionExpiresAt > EstimatedServerUnixSeconds;

    public event Action<string> GameSessionEnded;

    private NetworkManager boundNetwork;
    private NetMessageDispatcher boundDispatcher;
    private long boundEpoch;
    private long boundCharacterDbId;
    private string boundToken;
    private bool entryAccepted;
    private int ticketGeneration;
    private NetworkManager lastEntryNetwork;
    private long lastEntryEpoch;
    private int lastEntryTicketGeneration;
    private long lastServerTimeMs;
    private double serverTimeReceivedAt;
    private bool quitAfterLogout;
    private bool isReturningToCharacterSelect;
    private bool openCharacterSelectOnLoad;

    private double EstimatedServerUnixSeconds => lastServerTimeMs > 0
        ? lastServerTimeMs / 1000d + (Time.realtimeSinceStartupAsDouble - serverTimeReceivedAt)
        : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000d;

    public static string MaskToken(string token)
    {
        if (string.IsNullOrEmpty(token)) return "<empty>";
        if (token.Length <= 11) return "<redacted-short-token>";
        return token.Substring(0, 5) + "..." + token.Substring(token.Length - 6);
    }

    public bool SaveGameSession(string token, long expiresAt)
    {
        ResetGameSession();
        // Selecting a character revokes the HTTP login token on the server.
        Token = "";
        PlayerPrefs.DeleteKey(TokenKey);
        GameSessionToken = token ?? "";
        GameSessionExpiresAt = expiresAt;
        PlayerPrefs.SetString(GameTokenKey, GameSessionToken);
        PlayerPrefs.SetString(GameTokenExpiryKey,
            expiresAt.ToString(System.Globalization.CultureInfo.InvariantCulture));
        SaveEndpointIdentity();
        PlayerPrefs.Save();
        if (HasValidGameSession) return true;
        ClearSession();
        return false;
    }
    public string UserId { get; private set; } // 当前登录用户 ID，只能由 AuthSession 内部修改。
    public string Username { get; private set; } // 当前登录用户名，只能由 AuthSession 内部修改。

    public bool IsLoggedIn => !string.IsNullOrEmpty(Token); // 判断当前是否有登录 token。

    private void Awake() // 脚本启动时执行。
    {
        Debug.Log("[AUTH_LIFECYCLE] stage=session_awake sessionId=" + GetInstanceID()
            + " objectId=" + gameObject.GetInstanceID()
            + " existingSessionId=" + (Instance != null ? Instance.GetInstanceID() : 0));
        if (Instance != null && Instance != this) // 如果场景里已经有另一个 AuthSession。
        {
            Debug.LogWarning("[AUTH_LIFECYCLE] stage=duplicate_auth_system_destroy sessionId="
                + GetInstanceID() + " objectId=" + gameObject.GetInstanceID()
                + " existingSessionId=" + Instance.GetInstanceID());
            Destroy(gameObject); // 删除重复的 AuthSystem，避免登录状态管理混乱。
            return; // 停止后续逻辑。
        }

        Instance = this; // 把当前脚本设置为全局唯一实例。
        DontDestroyOnLoad(gameObject); // 切换场景时保留 AuthSystem。
#if !UNITY_EDITOR
        Application.wantsToQuit += HandleWantsToQuit;
#endif
        LoadSession(); // 启动时从本地读取已经保存的登录状态。
    }

    private void OnDestroy()
    {
#if !UNITY_EDITOR
        Application.wantsToQuit -= HandleWantsToQuit;
#endif
        UnbindGameConnection();
        Debug.Log("[AUTH_LIFECYCLE] stage=session_destroy sessionId=" + GetInstanceID()
            + " objectId=" + gameObject.GetInstanceID()
            + " wasCurrent=" + (Instance == this));
    }

    public void SaveSession(string token, string userId, string username) // 保存登录状态。
    {
        ResetGameSession();
        Token = token ?? ""; // 保存 token，避免 null。
        UserId = userId ?? ""; // 保存 userId，避免 null。
        Username = username ?? ""; // 保存 username，避免 null。

        PlayerPrefs.SetString(TokenKey, Token); // 把 token 写入本地。
        PlayerPrefs.SetString(UserIdKey, UserId); // 把 userId 写入本地。
        PlayerPrefs.SetString(UsernameKey, Username); // 把 username 写入本地。
        SaveEndpointIdentity();
        PlayerPrefs.Save(); // 立刻保存，避免程序退出时丢失。
    }

    public void LoadSession() // 从本地读取登录状态。
    {
        UnbindGameConnection();
        ticketGeneration++;
        lastServerTimeMs = 0;
        NetworkEndpointSnapshot endpoint = NetworkEndpointRuntime.Load();
        if (endpoint == null
            || PlayerPrefs.GetString(EndpointIdentityKey, "") != endpoint.Identity)
        {
            ClearSession();
            PlayerPrefs.DeleteKey(SelectedCharacterIdKey);
            PlayerPrefs.DeleteKey(SelectedCharacterDbIdKey);
            GateEntryHandoff.Clear();
            if (endpoint == null)
                PlayerPrefs.DeleteKey(EndpointIdentityKey);
            else
                PlayerPrefs.SetString(EndpointIdentityKey, endpoint.Identity);
            PlayerPrefs.Save();
        }

        GameSessionToken = PlayerPrefs.GetString(GameTokenKey, "");
        long.TryParse(PlayerPrefs.GetString(GameTokenExpiryKey, ""),
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out long expiresAt);
        GameSessionExpiresAt = expiresAt;
        int.TryParse(PlayerPrefs.GetString(GameReconnectWindowKey, ""),
            NumberStyles.Integer, CultureInfo.InvariantCulture, out int reconnectWindow);
        ReconnectWindowSeconds = Math.Max(0, reconnectWindow);
        Token = PlayerPrefs.GetString(TokenKey, ""); // 读取本地 token。
        UserId = PlayerPrefs.GetString(UserIdKey, ""); // 读取本地 userId。
        Username = PlayerPrefs.GetString(UsernameKey, ""); // 读取本地 username。
    }

    private static void SaveEndpointIdentity()
    {
        NetworkEndpointSnapshot endpoint = NetworkEndpointRuntime.Load();
        if (endpoint != null)
            PlayerPrefs.SetString(EndpointIdentityKey, endpoint.Identity);
    }

    public void ClearSession() // 清除登录状态。
    {
        ResetGameSession();
        Token = ""; // 清空内存里的 token。
        UserId = ""; // 清空内存里的 userId。
        Username = ""; // 清空内存里的 username。

        PlayerPrefs.DeleteKey(TokenKey); // 删除本地 token。
        PlayerPrefs.DeleteKey(UserIdKey); // 删除本地 userId。
        PlayerPrefs.DeleteKey(UsernameKey); // 删除本地 username。
        PlayerPrefs.Save(); // 立刻保存删除结果。
    }

    internal static bool IsRetryableGameSessionReason(string reason) =>
        reason == "session_still_online" || reason == "session_service_unavailable";

    internal static bool IsTerminalGameSessionReason(string reason) =>
        reason == "token_invalid_or_expired" || reason == "token_format_invalid"
        || reason == "kicked_by_new_login" || reason == "logout" || reason == "return_to_character_select";

    internal static string GameSessionEndedMessage(string reason) =>
        reason == "kicked_by_new_login" ? "账号已在其他设备登录，请重新登录。"
        : reason == "logout" ? "游戏会话已退出，请重新登录。"
        : "游戏会话已失效，请重新登录。";

    internal bool TrySendEnterWorld(NetworkManager network, NetMessageDispatcher dispatcher,
        long characterDbId, string characterId)
    {
        if (network == null || dispatcher == null || !network.IsConnected || !HasValidGameSession
            || characterDbId <= 0 || string.IsNullOrWhiteSpace(UserId)
            || !MatchesEndpoint(network.EndpointIdentity))
            return false;

        long epoch = network.ConnectionEpoch;
        if (epoch <= 0 || (network == lastEntryNetwork && epoch == lastEntryEpoch
            && ticketGeneration != lastEntryTicketGeneration)) return false;

        var request = new EnterWorldRequest
        {
            characterDbId = characterDbId,
            characterId = characterId,
            token = GameSessionToken,
            time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };
        if (!network.Send(request)) return false;

        lastEntryNetwork = network;
        lastEntryEpoch = epoch;
        lastEntryTicketGeneration = ticketGeneration;
        UnbindGameConnection();
        boundNetwork = network;
        boundDispatcher = dispatcher;
        boundEpoch = epoch;
        boundCharacterDbId = characterDbId;
        boundToken = request.token;
        dispatcher.Register("heartbeat_result", HandleGameHeartbeat);
        dispatcher.Register("account_kicked", HandleAccountKicked);
        dispatcher.Register("error", HandleGameSessionError);
        return true;
    }

    internal bool IsCurrentGameEntry(NetworkManager network, long epoch) =>
        network != null && network == boundNetwork && network == NetworkManager.Instance
        && epoch > 0 && epoch == boundEpoch && epoch == network.ConnectionEpoch
        && !IsLoggingOut && boundToken == GameSessionToken && MatchesEndpoint(network.EndpointIdentity);

    internal bool AcceptGameEntry(NetworkManager network, long epoch, EnterWorldResult result)
    {
        if (!IsCurrentGameEntry(network, epoch) || !network.IsConnected || result == null || !result.success)
            return false;

        long resultCharacter = result.characterDbId > 0 ? result.characterDbId : result.self?.characterDbId ?? 0;
        if (resultCharacter != boundCharacterDbId
            || (result.self != null && result.self.characterDbId > 0 && result.self.characterDbId != boundCharacterDbId)
            || (!string.IsNullOrEmpty(result.userId) && result.userId != UserId)
            || (!string.IsNullOrEmpty(result.self?.userId) && result.self.userId != UserId))
            return false;

        entryAccepted = true;
        ApplyGameSessionTiming(result.serverTime, result.gameSessionExpiresAt, result.reconnectWindowSeconds);
        return true;
    }

    private void HandleGameHeartbeat(string json)
    {
        if (!entryAccepted || !IsCurrentGameEntry(boundNetwork, boundEpoch)) return;
        var reply = JsonUtility.FromJson<HeartbeatAckMessage>(json);
        if (reply == null || !reply.success) return;
        ApplyGameSessionTiming(reply.serverTime, reply.gameSessionExpiresAt, reply.reconnectWindowSeconds);
    }

    private void ApplyGameSessionTiming(long serverTime, long expiresAt, int reconnectWindow)
    {
        if (serverTime <= lastServerTimeMs) return;

        serverTimeReceivedAt = Time.realtimeSinceStartupAsDouble;
        lastServerTimeMs = serverTime;
        bool changed = false;
        if (expiresAt > 0 && expiresAt != GameSessionExpiresAt)
        {
            GameSessionExpiresAt = expiresAt;
            PlayerPrefs.SetString(GameTokenExpiryKey, expiresAt.ToString(CultureInfo.InvariantCulture));
            changed = true;
        }
        if (reconnectWindow > 0 && reconnectWindow != ReconnectWindowSeconds)
        {
            ReconnectWindowSeconds = reconnectWindow;
            PlayerPrefs.SetString(GameReconnectWindowKey, reconnectWindow.ToString(CultureInfo.InvariantCulture));
            changed = true;
        }
        if (changed) PlayerPrefs.Save();
    }

    private void HandleAccountKicked(string json)
    {
        var notice = JsonUtility.FromJson<AccountKickedMessage>(json);
        if (notice == null) return;

        if (notice.reason == "return_to_character_select" && isReturningToCharacterSelect)
        {
            NetworkManager network = boundNetwork;
            if (network != null && network == NetworkManager.Instance && network.IsConnected
                && network.ConnectionEpoch == boundEpoch && boundToken == GameSessionToken
                && MatchesEndpoint(network.EndpointIdentity))
                network.Disconnect();
            return;
        }

        if (notice.reason == "kicked_by_new_login" || notice.reason == "logout"
            || notice.reason == "return_to_character_select") EndBoundGameSession(notice.reason);
    }

    private void HandleGameSessionError(string json)
    {
        if (!entryAccepted) return;
        var error = JsonUtility.FromJson<GameSessionTransportError>(json);
        if (error != null && error.requestMsgId == "heartbeat" && IsTerminalGameSessionReason(error.errorCode))
            EndBoundGameSession(error.errorCode);
    }

    private void EndBoundGameSession(string reason)
    {
        NetworkManager network = boundNetwork;
        if (!IsCurrentGameEntry(network, boundEpoch)) return;
        network.SuspendAutoReconnect();
        ClearSession();
        GameSessionEnded?.Invoke(reason);
        if (network.IsConnected) network.Disconnect();
    }

    private static bool MatchesEndpoint(string identity) =>
        !string.IsNullOrEmpty(identity) && identity == PlayerPrefs.GetString(EndpointIdentityKey, "");

    private void UnbindGameConnection()
    {
        if (boundDispatcher != null)
        {
            boundDispatcher.Unregister("heartbeat_result", HandleGameHeartbeat);
            boundDispatcher.Unregister("account_kicked", HandleAccountKicked);
            boundDispatcher.Unregister("error", HandleGameSessionError);
        }
        boundDispatcher = null;
        boundNetwork = null;
        boundEpoch = 0;
        boundCharacterDbId = 0;
        boundToken = null;
        entryAccepted = false;
    }

    private void ResetGameSession()
    {
        UnbindGameConnection();
        ticketGeneration++;
        GameSessionToken = "";
        GameSessionExpiresAt = 0;
        ReconnectWindowSeconds = 0;
        lastServerTimeMs = 0;
        IsLoggingOut = false;
        isReturningToCharacterSelect = false;
        openCharacterSelectOnLoad = false;
        PlayerPrefs.DeleteKey(GameTokenKey);
        PlayerPrefs.DeleteKey(GameTokenExpiryKey);
        PlayerPrefs.DeleteKey(GameReconnectWindowKey);
    }

    internal void ReturnToCharacterSelect(Action<AuthResult> onComplete)
    {
        if (IsLoggingOut) return;

        string token = GameSessionToken;
        string endpoint = NetworkEndpointRuntime.Load()?.Identity;
        if (string.IsNullOrWhiteSpace(token) || !MatchesEndpoint(endpoint))
        {
            onComplete(new AuthResult { errorCode = "token_invalid_or_expired" });
            return;
        }

        IsLoggingOut = true;
        isReturningToCharacterSelect = true;
        SuspendGameConnection(endpoint);
        StartCoroutine(ExchangeGameToken(token, endpoint, ticketGeneration, onComplete));
    }

    private IEnumerator ExchangeGameToken(string token, string endpoint, int generation, Action<AuthResult> onComplete)
    {
        AuthApiClient api = GetAuthApiClient();
        string requestId = Guid.NewGuid().ToString("D");
        AuthResult result = null;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (!IsCurrentCharacterSelectReturn(endpoint, generation)) break;

            bool completed = false;
            api.ReturnToCharacterSelect(token, requestId, reply =>
            {
                result = reply;
                completed = true;
            });
            while (!completed && IsCurrentCharacterSelectReturn(endpoint, generation)) yield return null;
            if (result != null && (result.success || result.errorCode == "token_invalid_or_expired"
                || result.errorCode == "invalid_request_id")) break;

            if (attempt < 2) yield return new WaitForSecondsRealtime(0.25f * (attempt + 1));
        }

        if (!isReturningToCharacterSelect || ticketGeneration != generation) yield break;
        if (!IsCurrentCharacterSelectReturn(endpoint, generation)) result = null;
        isReturningToCharacterSelect = false;
        IsLoggingOut = false;

        if (result != null && result.success)
        {
            SaveSession(result.token, UserId, Username);
            openCharacterSelectOnLoad = true;
        }

        if (quitAfterLogout) LogoutGameSession(null);
        else onComplete(result);
    }

    private bool IsCurrentCharacterSelectReturn(string endpoint, int generation) =>
        isReturningToCharacterSelect && ticketGeneration == generation && MatchesEndpoint(endpoint)
        && NetworkEndpointRuntime.Load()?.Identity == endpoint;

    internal bool TryConsumeCharacterSelectReturn()
    {
        bool openSelection = openCharacterSelectOnLoad && IsLoggedIn
            && MatchesEndpoint(NetworkEndpointRuntime.Load()?.Identity);
        openCharacterSelectOnLoad = false;
        return openSelection;
    }

    internal void QuitGame()
    {
        quitAfterLogout = true;
        LogoutGameSession(null);
    }

    internal void LogoutGameSession(Action onComplete)
    {
        if (IsLoggingOut) return;
        string token = string.IsNullOrEmpty(GameSessionToken) ? LoginToken : GameSessionToken;
        string endpoint = NetworkEndpointRuntime.Load()?.Identity;
        NetworkManager network = SuspendGameConnection(endpoint);
        if (string.IsNullOrEmpty(token) || !MatchesEndpoint(endpoint))
        {
            FinishLogout(network, onComplete);
            return;
        }

        IsLoggingOut = true;
        int generation = ticketGeneration;
        GetAuthApiClient().LogoutGameSession(token, result =>
        {
            if (!IsLoggingOut || ticketGeneration != generation) return;
            if (result == null || !result.success)
                Debug.LogWarning("[Auth] Game session logout could not be confirmed.");
            FinishLogout(network, onComplete);
        }, quitAfterLogout ? 3 : 0);
    }

    private AuthApiClient GetAuthApiClient()
    {
        AuthApiClient api = GetComponent<AuthApiClient>();
        return api != null ? api : gameObject.AddComponent<AuthApiClient>();
    }

    private static NetworkManager SuspendGameConnection(string endpoint)
    {
        NetworkManager network = NetworkManager.Instance;
        if (network == null || network.EndpointIdentity != endpoint) return null;
        network.SuspendAutoReconnect();
        return network;
    }

    private void FinishLogout(NetworkManager network, Action onComplete)
    {
        ClearSession();
        if (network != null && network.IsConnected) network.Disconnect();
        if (quitAfterLogout) CloseGame();
        else onComplete?.Invoke();
    }

    internal static void CloseGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

#if !UNITY_EDITOR
    private bool HandleWantsToQuit()
    {
        if ((string.IsNullOrEmpty(GameSessionToken) && string.IsNullOrEmpty(LoginToken))
            || !MatchesEndpoint(NetworkEndpointRuntime.Load()?.Identity))
            return true;
        QuitGame();
        return false;
    }
#endif

    public string GetAuthorizationHeader() // 获取请求服务器时使用的 Authorization 内容。
    {
        if (string.IsNullOrEmpty(Token)) // 如果当前没有 token。
        {
            return ""; // 返回空字符串。
        }

        return "Bearer " + Token; // 返回标准 Bearer Token 格式。
    }
}
