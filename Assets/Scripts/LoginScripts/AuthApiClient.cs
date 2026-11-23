using System;
using System.Collections;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;

public class AuthApiClient : MonoBehaviour
{
    [Header("HTTP Server")]
    [SerializeField] private bool useMockServer = false;
    [SerializeField] private float mockDelay = 0.5f;
    [SerializeField] private int requestTimeout = 10;
    private long nextLoginTraceId;

    private void Awake()
    {
        Debug.Log("[AUTH_LIFECYCLE] stage=auth_client_awake clientId=" + GetInstanceID()
            + " objectId=" + gameObject.GetInstanceID()
            + " scene=" + gameObject.scene.name);
    }

    private void OnDestroy()
    {
        Debug.Log("[AUTH_LIFECYCLE] stage=auth_client_destroy clientId=" + GetInstanceID()
            + " objectId=" + gameObject.GetInstanceID()
            + " scene=" + gameObject.scene.name);
    }

    public void Login(string username, string password, Action<AuthResult> onComplete)
    {
        AuthLoginRequest request = new AuthLoginRequest();
        request.username = username;
        request.password = password;
        request.channel = "Account";

        SendAuthRequest("/api/auth/login", request, onComplete);
    }

    public void Register(string username, string password, Action<AuthResult> onComplete)
    {
        AuthRegisterRequest request = new AuthRegisterRequest();
        request.username = username;
        request.password = password;
        request.channel = "Account";

        SendAuthRequest("/api/auth/register", request, onComplete);
    }

    public void SendPhoneCode(string phone, Action<AuthResult> onComplete)
    {
        AuthCodeRequest request = new AuthCodeRequest();
        request.channel = "Phone";
        request.target = phone;

        SendAuthRequest("/api/v1/auth/send-code", request, onComplete);
    }

    public void SendEmailCode(string email, Action<AuthResult> onComplete)
    {
        AuthCodeRequest request = new AuthCodeRequest();
        request.channel = "Email";
        request.target = email;

        SendAuthRequest("/api/v1/auth/send-code", request, onComplete);
    }

    public void RegisterByPhone(string phone, string code, string password, Action<AuthResult> onComplete)
    {
        AuthPhoneRegisterRequest request = new AuthPhoneRegisterRequest();
        request.phone = phone;
        request.code = code;
        request.password = password;
        request.deviceId = SystemInfo.deviceUniqueIdentifier;

        SendAuthRequest("/api/v1/auth/register/phone", request, onComplete);
    }

    public void RegisterByEmail(string email, string code, string password, Action<AuthResult> onComplete)
    {
        AuthEmailRegisterRequest request = new AuthEmailRegisterRequest();
        request.email = email;
        request.code = code;
        request.password = password;
        request.deviceId = SystemInfo.deviceUniqueIdentifier;

        SendAuthRequest("/api/v1/auth/register/email", request, onComplete);
    }

    public void GuestLogin(Action<AuthResult> onComplete)
    {
        AuthGuestLoginRequest request = new AuthGuestLoginRequest();
        request.deviceId = SystemInfo.deviceUniqueIdentifier;

        SendAuthRequest("/api/v1/auth/guest-login", request, onComplete);
    }

    internal void LogoutGameSession(string token, Action<AuthResult> onComplete, int timeoutSeconds = 0)
    {
        SendAuthRequest("/api/auth/logout", new LogoutGameSessionRequest { token = token }, onComplete,
            timeoutSeconds: timeoutSeconds);
    }

    internal void ReturnToCharacterSelect(string token, string requestId, Action<AuthResult> onComplete)
    {
        SendAuthRequest("/api/auth/return-to-character-select",
            new ReturnToCharacterSelectRequest { token = token, requestId = requestId }, onComplete,
            timeoutSeconds: 4, parseResponse: ParseCharacterSelectReturn);
    }

    [Serializable]
    private sealed class LogoutGameSessionRequest
    {
        public string token;
    }

    [Serializable]
    private sealed class ReturnToCharacterSelectRequest
    {
        public string token;
        public string requestId;
    }

    [Serializable]
    private sealed class ReturnToCharacterSelectResponse
    {
        public bool success = false;
        public string code = null;
        public string message = null;
        public string token = null;
        public long expiresAt = 0;
    }

    public void SendAuthorizedRequest<T>(string path, T requestData, Action<AuthResult> onComplete)
    {
        SendAuthRequest(path, requestData, onComplete, true);
    }

    private void SendAuthRequest<T>(string path, T requestData, Action<AuthResult> onComplete,
        bool requireAuth = false, int timeoutSeconds = 0, Func<string, AuthResult> parseResponse = null)
    {
        bool traceLogin = string.Equals(path, "/api/auth/login", StringComparison.Ordinal);
        long traceId = traceLogin ? Interlocked.Increment(ref nextLoginTraceId) : 0;
        if (useMockServer && parseResponse == null && timeoutSeconds == 0)
        {
            if (traceLogin)
                Debug.LogWarning("[AUTH_LOGIN] id=" + traceId + " stage=mock_mode no_http_request=true");
            StartCoroutine(MockAuthRequest(requestData, onComplete));
            return;
        }

        NetworkEndpointSnapshot settings = NetworkEndpointRuntime.Load();
        if (settings == null)
        {
            if (traceLogin)
                Debug.LogError("[AUTH_LOGIN] id=" + traceId
                    + " stage=invalid_endpoint settingsFound=" + (settings != null));
            onComplete?.Invoke(AuthResult.Fail(
                "服务器配置缺失或无效。请保存 WorldShell / NetworkSystem 后重试。"));
            return;
        }

        if (traceLogin)
        {
            Uri httpUri = new Uri(settings.HttpBaseUrl);
            Debug.Log("[AUTH_LOGIN] id=" + traceId + " stage=http_coroutine_start host="
                + httpUri.Host + " port=" + httpUri.Port);
        }
        StartCoroutine(SendHttpJson(path, requestData, onComplete, requireAuth, settings, traceId,
            timeoutSeconds > 0 ? timeoutSeconds : requestTimeout, parseResponse ?? ParseAuthResult));
    }

    private IEnumerator MockAuthRequest<T>(T requestData, Action<AuthResult> onComplete)
    {
        yield return new WaitForSeconds(mockDelay);

        AuthResult result = new AuthResult();
        result.success = true;
        result.message = "成功";
        result.userId = "mock_user_001";
        result.username = GetMockUsername(requestData);
        result.token = "mock_token_" + DateTime.UtcNow.Ticks;

        onComplete?.Invoke(result);
    }

    private IEnumerator SendHttpJson<T>(string path, T requestData, Action<AuthResult> onComplete,
        bool requireAuth, NetworkEndpointSnapshot settings, long traceId, int timeoutSeconds,
        Func<string, AuthResult> parseResponse)
    {
        string json = JsonUtility.ToJson(requestData);
        byte[] body = Encoding.UTF8.GetBytes(json);

        using (UnityWebRequest request = new UnityWebRequest(BuildUrl(path, settings), "POST"))
        {
            request.uploadHandler = new UploadHandlerRaw(body);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Accept", "application/json");

            if (requireAuth)
            {
                string authorization = GetAuthorizationHeader();
                if (string.IsNullOrEmpty(authorization))
                {
                    onComplete?.Invoke(AuthResult.Fail("登录令牌缺失"));
                    yield break;
                }

                request.SetRequestHeader("Authorization", authorization);
            }

            request.timeout = timeoutSeconds;

            if (traceId > 0)
                Debug.Log("[AUTH_LOGIN] id=" + traceId + " stage=http_send method=POST url="
                    + request.url + " bytes=" + body.Length + " timeoutSeconds=" + timeoutSeconds);
            yield return request.SendWebRequest();

            string responseText = request.downloadHandler != null ? request.downloadHandler.text : "";
            AuthResult result = parseResponse(responseText);
            if (traceId > 0)
                Debug.Log("[AUTH_LOGIN] id=" + traceId + " stage=http_complete status="
                    + request.responseCode + " result=" + request.result
                    + " networkError=" + (string.IsNullOrEmpty(request.error) ? "<none>" : request.error)
                    + " responseBytes=" + Encoding.UTF8.GetByteCount(responseText)
                    + " parsed=" + (result != null)
                    + " parsedSuccess=" + (result != null && result.success));

            if (request.result != UnityWebRequest.Result.Success)
            {
                if (result != null)
                {
                    onComplete?.Invoke(result);
                    yield break;
                }

                onComplete?.Invoke(AuthResult.Fail("服务器连接失败：" + request.error));
                yield break;
            }

            onComplete?.Invoke(result ?? AuthResult.Fail("服务器响应无效"));
        }
    }

    private string GetAuthorizationHeader()
    {
        if (AuthSession.Instance == null || !AuthSession.Instance.IsLoggedIn)
        {
            return "";
        }

        return AuthSession.Instance.GetAuthorizationHeader();
    }

    private static string BuildUrl(string path, NetworkEndpointSnapshot settings)
    {
        UriBuilder builder = new UriBuilder(settings.HttpBaseUrl);
        builder.Path = builder.Path.TrimEnd('/') + "/" + path.TrimStart('/');
        return builder.Uri.AbsoluteUri;
    }

    private AuthResult ParseAuthResult(string json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        try
        {
            bool hasCodeField = json.Contains("\"code\"");
            AuthResult result = JsonUtility.FromJson<AuthResult>(json);
            if (result == null)
            {
                return AuthResult.Fail("服务器响应无效");
            }

            result.NormalizeFromServer(hasCodeField);
            return result;
        }
        catch (Exception)
        {
            return AuthResult.Fail("服务器响应无效");
        }
    }

    private static AuthResult ParseCharacterSelectReturn(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            var response = JsonUtility.FromJson<ReturnToCharacterSelectResponse>(json);
            if (response == null) return null;

            return new AuthResult
            {
                success = response.success && !string.IsNullOrWhiteSpace(response.token) && response.expiresAt > 0,
                errorCode = response.code,
                message = response.message,
                token = response.token,
                expiresAt = response.expiresAt
            };
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private string GetMockUsername<T>(T requestData)
    {
        string json = JsonUtility.ToJson(requestData);
        AuthMockNameData nameData = JsonUtility.FromJson<AuthMockNameData>(json);

        if (!string.IsNullOrEmpty(nameData.username))
            return nameData.username;

        if (!string.IsNullOrEmpty(nameData.phone))
            return nameData.phone;

        if (!string.IsNullOrEmpty(nameData.email))
            return nameData.email;

        return "Guest";
    }
}

[Serializable]
public class AuthResult
{
    public string gameSessionToken;
    public long expiresAt;
    public bool success;
    public int code;
    public string message;
    public string errorCode;
    public string token;
    public string accessToken;
    public string refreshToken;
    public string userId;
    public string accountId;
    public string username;
    public int expiresIn;
    public long characterDbId;
    public string characterId;
    public CharacterData character;
    public CharacterData[] characters;
    public AuthResultData data;

    public void NormalizeFromServer(bool hasCodeField)
    {
        if (data != null)
        {
            if (string.IsNullOrEmpty(token))
                token = string.IsNullOrEmpty(data.token) ? data.accessToken : data.token;

            if (string.IsNullOrEmpty(accessToken))
                accessToken = data.accessToken;

            if (string.IsNullOrEmpty(refreshToken))
                refreshToken = data.refreshToken;

            if (string.IsNullOrEmpty(userId))
                userId = string.IsNullOrEmpty(data.userId) ? data.accountId : data.userId;

            if (string.IsNullOrEmpty(accountId))
                accountId = data.accountId;

            if (string.IsNullOrEmpty(username))
                username = data.username;

            if (expiresIn == 0)
                expiresIn = data.expiresIn;

            if (characterDbId <= 0)
                characterDbId = data.characterDbId;

            if (string.IsNullOrEmpty(characterId))
                characterId = data.characterId;

            if (character == null)
                character = data.character;

            if (characters == null || characters.Length == 0)
                characters = data.characters;
        }

        if (hasCodeField && code == 0)
        {
            success = true;
        }

        if (string.IsNullOrEmpty(token))
        {
            token = accessToken;
        }

        if (string.IsNullOrEmpty(userId))
        {
            userId = accountId;
        }

        if (string.IsNullOrEmpty(characterId) && character != null)
        {
            characterId = character.characterId;
        }

        if (characterDbId <= 0 && character != null)
        {
            characterDbId = character.characterDbId;
        }

        if (string.IsNullOrEmpty(message))
        {
            message = success ? "成功" : "请求失败";
        }
    }

    public static AuthResult Fail(string message)
    {
        AuthResult result = new AuthResult();
        result.success = false;
        result.message = message;
        result.token = "";
        result.userId = "";
        result.username = "";
        return result;
    }
}

[Serializable]
public class AuthResultData
{
    public string token;
    public string accessToken;
    public string refreshToken;
    public string userId;
    public string accountId;
    public string username;
    public int expiresIn;
    public long characterDbId;
    public string characterId;
    public CharacterData character;
    public CharacterData[] characters;
}

[Serializable]
public class AuthLoginRequest
{
    public string username;
    public string password;
    public string channel;
}

[Serializable]
public class AuthRegisterRequest
{
    public string username;
    public string password;
    public string channel;
}

[Serializable]
public class AuthCodeRequest
{
    public string channel;
    public string target;
}

[Serializable]
public class AuthPhoneRegisterRequest
{
    public string phone;
    public string code;
    public string password;
    public string deviceId;
}

[Serializable]
public class AuthEmailRegisterRequest
{
    public string email;
    public string code;
    public string password;
    public string deviceId;
}

[Serializable]
public class AuthGuestLoginRequest
{
    public string deviceId;
}

[Serializable]
public class AuthMockNameData
{
    public string username;
    public string phone;
    public string email;
}
