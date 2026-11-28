#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using UnityEngine;

internal static class BootstrapResourceDiagnostics
{
    private static readonly HashSet<string> loggedAddresses = new HashSet<string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() => loggedAddresses.Clear();

    public static void LogEndpoint(string built, string selected)
    {
        Debug.Log("[GrassDiag][Endpoint] built=" + built + " selected=" + (selected ?? built)
            + " unity=" + Application.unityVersion + " buildGuid=" + Application.buildGUID);
    }

    public static void LogResolvedAddress(string original, string resolved)
    {
        if (!string.IsNullOrEmpty(resolved)
            && (resolved.Contains("catalog_") || resolved.Contains("remotegamecontent_scenes_all"))
            && loggedAddresses.Add(original + "\n" + resolved))
            Debug.Log("[GrassDiag][Address] original=" + original + " resolved=" + resolved);
    }
}
#endif
