using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Text.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SerializeField : Attribute { }
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class InspectorNameAttribute : Attribute
    {
        public InspectorNameAttribute(string name) { }
    }
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class TooltipAttribute : Attribute
    {
        public TooltipAttribute(string text) { }
    }
    public enum RuntimeInitializeLoadType { SubsystemRegistration }
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType type) { }
    }
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class HeaderAttribute : Attribute
    {
        public HeaderAttribute(string value) { }
    }
    public class ScriptableObject { }
    public sealed class Coroutine { }
    public sealed class GameObject
    {
        public UnityEngine.SceneManagement.Scene scene;
        public int GetInstanceID() => 1;
    }
    public class MonoBehaviour
    {
        public GameObject gameObject = new GameObject();
        public int GetInstanceID() => 2;
        protected Coroutine StartCoroutine(IEnumerator routine)
        {
            while (routine.MoveNext()) { }
            return new Coroutine();
        }
    }
    public sealed class WaitForSeconds
    {
        public WaitForSeconds(float seconds) { }
    }
    public static class SystemInfo { public static string deviceUniqueIdentifier => "test-device"; }
    public static class Resources
    {
        public static NetworkEndpointSettings Settings;
        public static T Load<T>(string path) where T : class => Settings as T;
    }
    public static class JsonUtility
    {
        public static string ToJson<T>(T value) =>
            JsonSerializer.Serialize(value, new JsonSerializerOptions { IncludeFields = true });
        public static T FromJson<T>(string json) =>
            JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions { IncludeFields = true });
    }
    public static class Debug
    {
        public static readonly List<string> Messages = new List<string>();
        public static void Log(object message) => Messages.Add(message.ToString());
        public static void LogWarning(object message) => Messages.Add(message.ToString());
        public static void LogError(object message) => Messages.Add(message.ToString());
    }
}
namespace UnityEngine.SceneManagement
{
    public struct Scene { public string name; }
}

namespace UnityEngine.Networking
{
    public class DownloadHandler { public string text; }
    public sealed class DownloadHandlerBuffer : DownloadHandler { }
    public sealed class UploadHandlerRaw
    {
        public readonly byte[] data;
        public UploadHandlerRaw(byte[] data) { this.data = data; }
    }
    public sealed class UnityWebRequest : IDisposable
    {
        public enum Result { InProgress, Success, ConnectionError, ProtocolError }
        public static Action<UnityWebRequest> OnSend;
        public static UnityWebRequest LastRequest;
        public readonly string url;
        public readonly string method;
        public UploadHandlerRaw uploadHandler;
        public DownloadHandler downloadHandler;
        public int timeout;
        public long responseCode;
        public Result result = Result.InProgress;
        public string error;
        public readonly Dictionary<string, string> headers = new Dictionary<string, string>();
        public UnityWebRequest(string url, string method)
        {
            this.url = url;
            this.method = method;
            LastRequest = this;
        }
        public void SetRequestHeader(string key, string value) { headers[key] = value; }
        public object SendWebRequest()
        {
            OnSend?.Invoke(this);
            return new object();
        }
        public void Dispose() { }
    }
}

public sealed class AuthSession
{
    public static AuthSession Instance;
    public bool IsLoggedIn;
    public string GetAuthorizationHeader() => "";
}
public sealed class CharacterData
{
    public long characterDbId;
    public string characterId;
}

internal static class AuthLoginDiagnosticsTests
{
    private const string Password = "do-not-log-password";
    private const string SecretToken = "do-not-log-token";
    private const string SecretBody = "do-not-log-response-body";

    private static void Main()
    {
        AuthApiClient client = new AuthApiClient();
        AuthResult callback = null;
        Resources.Settings = null;
        client.Login("tester", Password, result => callback = result);
        Check(callback != null && !callback.success
            && callback.message == "服务器配置缺失或无效。请保存 WorldShell / NetworkSystem 后重试。"
            && Has("stage=invalid_endpoint"),
            "Missing endpoint did not report why no request was sent.");

        Resources.Settings = new NetworkEndpointSettings();
        SetSetting("serverHost", "192.168.31.42");
        SetSetting("tcpPort", 9000);
        SetSetting("httpPort", 8080);
        SetSetting("httpBaseUrl", "http://192.168.31.42:8080");
        SetSetting("selectedProfileName", "Current");
        typeof(AuthApiClient).GetField("useMockServer", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(client, true);
        Debug.Messages.Clear();
        UnityWebRequest.LastRequest = null;
        client.Login("tester", Password, result => callback = result);
        Check(Has("stage=mock_mode") && UnityWebRequest.LastRequest == null
            && callback.success && callback.message == "成功",
            "Mock mode was not identifiable as a request that never reached HTTP.");

        typeof(AuthApiClient).GetField("useMockServer", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(client, false);
        UnityWebRequest.OnSend = request =>
        {
            request.result = UnityWebRequest.Result.Success;
            request.responseCode = 200;
            request.downloadHandler.text = "{\"code\":0,\"token\":\"" + SecretToken
                + "\",\"message\":\"" + SecretBody + "\"}";
        };
        Debug.Messages.Clear();
        client.Login("tester", Password, result => callback = result);
        UnityWebRequest sent = UnityWebRequest.LastRequest;
        Check(sent.url == "http://192.168.31.42:8080/api/auth/login" && sent.method == "POST"
            && sent.headers["Content-Type"] == "application/json"
            && Encoding.UTF8.GetString(sent.uploadHandler.data).Contains(Password)
            && callback.success && Has("stage=http_send") && Has("stage=http_complete status=200")
            && Has("parsedSuccess=True"),
            "Real login request path, response, or trace markers changed.");
        CheckNoSecrets();

        SetSetting("serverHost", "gate.example.com");
        SetSetting("tcpPort", 9001);
        SetSetting("httpPort", 443);
        SetSetting("httpBaseUrl", "https://auth.example.com/gateway");
        SetSetting("selectedProfileName", "Remote");
        Debug.Messages.Clear();
        client.Login("tester", Password, result => callback = result);
        sent = UnityWebRequest.LastRequest;
        Check(sent.url == "http://192.168.31.42:8080/api/auth/login",
            "Changing the asset during a session must not mix HTTP and TCP environments.");

        ResetActiveEndpoint();
        Debug.Messages.Clear();
        client.Login("tester", Password, result => callback = result);
        sent = UnityWebRequest.LastRequest;
        Check(sent.url == "https://auth.example.com/gateway/api/auth/login"
            && Has("stage=http_coroutine_start host=auth.example.com port=443")
            && callback.success,
            "HTTP must use the selected HTTP base URL, independently from the TCP host.");
        CheckNoSecrets();

        SetSetting("httpBaseUrl", "ftp://auth.example.com/gateway");
        ResetActiveEndpoint();
        UnityWebRequest.LastRequest = null;
        Debug.Messages.Clear();
        client.Login("tester", Password, result => callback = result);
        Check(!callback.success && UnityWebRequest.LastRequest == null
            && Has("stage=invalid_endpoint"),
            "Invalid selected HTTP settings must not send a request.");

        SetSetting("serverHost", "192.168.31.42");
        SetSetting("tcpPort", 9000);
        SetSetting("httpPort", 8080);
        SetSetting("httpBaseUrl", "http://192.168.31.42:8080");
        SetSetting("selectedProfileName", "Current");
        ResetActiveEndpoint();

        UnityWebRequest.OnSend = request =>
        {
            request.result = UnityWebRequest.Result.ProtocolError;
            request.responseCode = 401;
            request.error = "HTTP 401 Unauthorized";
            request.downloadHandler.text = "{\"message\":\"" + SecretBody + "\"}";
        };
        Debug.Messages.Clear();
        client.Login("tester", Password, result => callback = result);
        Check(!callback.success && Has("stage=http_complete status=401")
            && Has("result=ProtocolError"), "HTTP failure status was not logged.");
        CheckNoSecrets();

        UnityWebRequest.OnSend = request =>
        {
            request.result = UnityWebRequest.Result.ConnectionError;
            request.responseCode = 0;
            request.error = "Cannot connect";
            request.downloadHandler.text = "";
        };
        Debug.Messages.Clear();
        client.Login("tester", Password, result => callback = result);
        Check(!callback.success && callback.message == "服务器连接失败：Cannot connect"
            && Has("result=ConnectionError")
            && Has("networkError=Cannot connect"),
            "A connection failure did not identify the network stage.");
        CheckNoSecrets();
        Console.WriteLine("Auth login diagnostics tests passed.");
    }

    private static void SetSetting(string name, object value)
    {
        typeof(NetworkEndpointSettings).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(Resources.Settings, value);
    }
    private static void ResetActiveEndpoint()
    {
        typeof(NetworkEndpointRuntime).GetMethod("ResetAtStartup",
            BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
    }
    private static bool Has(string fragment) =>
        Debug.Messages.Exists(message => message.Contains(fragment, StringComparison.Ordinal));
    private static void CheckNoSecrets()
    {
        Check(!Has(Password) && !Has(SecretToken) && !Has(SecretBody),
            "Login diagnostics leaked a credential or response body.");
    }
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
