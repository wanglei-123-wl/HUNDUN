using UnityEngine;

public static class GateTransportBootstrap
{
    public static bool EnsureTransport(out string error)
    {
        error = null;
        NetworkEndpointSnapshot settings = NetworkEndpointRuntime.Load();
        if (settings == null)
        {
            error = "Gate server configuration is missing or invalid. Save WorldShell / NetworkSystem and retry.";
            return false;
        }

        NetworkManager network = NetworkManager.Instance;
        if (network != null && !network.TryConfigureServerEndpoint(settings, out error))
        {
            return false;
        }

        GameObject transport = null;
        if (network == null)
        {
            transport = new GameObject("GateTransport");
            network = transport.AddComponent<NetworkManager>();
            if (!network.TryConfigureServerEndpoint(settings, out error))
            {
                Object.Destroy(transport);
                return false;
            }
        }

        if (NetMessageDispatcher.Instance == null)
        {
            if (transport == null)
            {
                transport = new GameObject("GateTransport");
            }

            transport.AddComponent<NetMessageDispatcher>();
        }

        return true;
    }
}
