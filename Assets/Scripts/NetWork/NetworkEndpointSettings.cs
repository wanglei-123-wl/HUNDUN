using System;
using System.Text;
using UnityEngine;

public sealed class NetworkEndpointSettings : ScriptableObject
{
    public const string ResourcePath = "NetworkEndpointSettings";

    [SerializeField] private string serverHost;
    [SerializeField] private int tcpPort;
    [SerializeField] private int httpPort;
    [SerializeField] private string httpBaseUrl;
    [SerializeField] private string selectedProfileName;

    public string ServerHost => serverHost;
    public int TcpPort => tcpPort;
    public int HttpPort => httpPort;
    public string HttpBaseUrl => httpBaseUrl;
    public string SelectedProfileName => selectedProfileName;

    public static NetworkEndpointSettings Load()
    {
        return Resources.Load<NetworkEndpointSettings>(ResourcePath);
    }

    public static bool IsValid(NetworkEndpointSettings settings)
    {
        return settings != null
            && !string.IsNullOrWhiteSpace(settings.SelectedProfileName)
            && IsValid(settings.ServerHost, settings.TcpPort, settings.HttpPort)
            && NetworkEndpointProfile.TryParseHttpBaseUrl(settings.HttpBaseUrl, out Uri httpUri)
            && httpUri.Port == settings.HttpPort;
    }

    public static bool IsValid(string host, int gamePort, int authPort)
    {
        return !string.IsNullOrWhiteSpace(host)
            && host == host.Trim()
            && Uri.CheckHostName(host) != UriHostNameType.Unknown
            && gamePort >= 1 && gamePort <= 65535
            && authPort >= 1 && authPort <= 65535;
    }

#if UNITY_EDITOR
    public void SetFromScene(NetworkEndpointProfile profile)
    {
        serverHost = profile.TcpHost;
        tcpPort = profile.TcpPort;
        httpPort = profile.HttpPort;
        httpBaseUrl = profile.HttpBaseUrl;
        selectedProfileName = profile.Name;
    }
#endif
}

[Serializable]
public sealed class NetworkEndpointProfile
{
    [SerializeField, InspectorName("环境名称")] private string name;
    [SerializeField, InspectorName("TCP 地址"), Tooltip("填写主机名或 IP 地址。")]
    private string tcpHost;
    [SerializeField, InspectorName("TCP 端口")] private int tcpPort;
    [SerializeField, InspectorName("HTTP 基础地址"), Tooltip("填写完整地址，包含协议和端口。")]
    private string httpBaseUrl;

    public string Name => name;
    public string TcpHost => tcpHost;
    public int TcpPort => tcpPort;
    public string HttpBaseUrl => httpBaseUrl;
    public int HttpPort => Uri.TryCreate(httpBaseUrl, UriKind.Absolute, out Uri uri) ? uri.Port : 0;

    public bool TryValidate(out string error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(name) || name != name.Trim())
        {
            error = "Environment name is missing or has leading/trailing spaces.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(tcpHost) || tcpHost != tcpHost.Trim()
            || Uri.CheckHostName(tcpHost) == UriHostNameType.Unknown
            || tcpPort < 1 || tcpPort > 65535)
        {
            error = "TCP host or port is invalid.";
            return false;
        }

        if (!TryParseHttpBaseUrl(httpBaseUrl, out _))
        {
            error = "HTTP base URL must be an http(s) address without credentials, query, or fragment.";
            return false;
        }

        return true;
    }

    public static bool TryParseHttpBaseUrl(string value, out Uri uri)
    {
        uri = null;
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim()
            || !Uri.TryCreate(value, UriKind.Absolute, out Uri parsed)
            || (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
            || string.IsNullOrEmpty(parsed.Host) || parsed.Port < 1 || parsed.Port > 65535
            || !string.IsNullOrEmpty(parsed.UserInfo)
            || !string.IsNullOrEmpty(parsed.Query) || !string.IsNullOrEmpty(parsed.Fragment))
        {
            return false;
        }

        uri = parsed;
        return true;
    }
}

public sealed class NetworkEndpointSnapshot
{
    public string ServerHost { get; }
    public int TcpPort { get; }
    public int HttpPort { get; }
    public string HttpBaseUrl { get; }
    public string SelectedProfileName { get; }
    public string Identity { get; }

    public NetworkEndpointSnapshot(NetworkEndpointSettings settings)
    {
        ServerHost = settings.ServerHost;
        TcpPort = settings.TcpPort;
        HttpPort = settings.HttpPort;
        HttpBaseUrl = settings.HttpBaseUrl;
        SelectedProfileName = settings.SelectedProfileName;

        StringBuilder identity = new StringBuilder();
        AppendIdentityPart(identity, SelectedProfileName);
        AppendIdentityPart(identity, ServerHost);
        AppendIdentityPart(identity, TcpPort.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AppendIdentityPart(identity, HttpBaseUrl);
        Identity = identity.ToString();
    }

    private static void AppendIdentityPart(StringBuilder identity, string value)
    {
        identity.Append(value.Length).Append(':').Append(value);
    }
}

public static class NetworkEndpointRuntime
{
    private static NetworkEndpointSnapshot active;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetAtStartup()
    {
        active = null;
    }

    public static NetworkEndpointSnapshot Load()
    {
        if (active != null)
            return active;

        NetworkEndpointSettings settings = NetworkEndpointSettings.Load();
        if (!NetworkEndpointSettings.IsValid(settings))
            return null;

        active = new NetworkEndpointSnapshot(settings);
        return active;
    }
}
