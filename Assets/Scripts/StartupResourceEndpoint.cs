using System;
using System.IO;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.Initialization;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.ResourceLocations;

internal static class StartupResourceEndpoint
{
    private const string RemoteHashKey = "AddressablesMainContentCatalogRemoteHash";

    private static string builtAddress;
    private static string selectedAddress;
    private static Func<IResourceLocation, string> previousTransform;
    private static readonly Func<IResourceLocation, string> transform = Transform;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset()
    {
        if (Addressables.InternalIdTransformFunc == transform)
            Addressables.InternalIdTransformFunc = previousTransform;
        builtAddress = null;
        selectedAddress = null;
        previousTransform = null;
    }

    public static string BuiltAddress
    {
        get
        {
            if (builtAddress == null)
                builtAddress = ReadBuiltAddress();
            return builtAddress;
        }
    }

    public static bool TryApply(string input, out string error)
    {
        string configured = string.IsNullOrWhiteSpace(input) ? BuiltAddress : input;
        if (!StartupResourceUrl.TryNormalize(configured, BuiltAddress,
            out string normalized, out error))
            return false;

        selectedAddress = string.Equals(normalized, BuiltAddress,
            StringComparison.OrdinalIgnoreCase) ? null : normalized;
        if (selectedAddress == null)
        {
            if (Addressables.InternalIdTransformFunc == transform)
                Addressables.InternalIdTransformFunc = previousTransform;
        }
        else if (Addressables.InternalIdTransformFunc != transform)
        {
            previousTransform = Addressables.InternalIdTransformFunc;
            Addressables.InternalIdTransformFunc = transform;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        GrassRenderDiagnostics.LogEndpoint(BuiltAddress, normalized);
#endif
        return true;
    }

    private static string Transform(IResourceLocation location)
    {
        string internalId = previousTransform == null
            ? location.InternalId : previousTransform(location);
        string resolved = StartupResourceUrl.Redirect(internalId, BuiltAddress, selectedAddress);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        GrassRenderDiagnostics.LogResolvedAddress(internalId, resolved);
#endif
        return resolved;
    }

    private static string ReadBuiltAddress()
    {
        try
        {
            string path = Path.Combine(Addressables.RuntimePath, "settings.json");
            if (File.Exists(path))
            {
                var data = JsonUtility.FromJson<ResourceManagerRuntimeData>(
                    File.ReadAllText(path));
                foreach (ResourceLocationData location in data.CatalogLocations)
                {
                    if (Array.IndexOf(location.Keys, RemoteHashKey) < 0
                        || !Uri.TryCreate(location.InternalId, UriKind.Absolute,
                            out Uri hashUri))
                        continue;
                    return new Uri(hashUri, ".").AbsoluteUri.TrimEnd('/');
                }
            }
        }
        catch (Exception error)
        {
            Debug.LogWarning("[StartupContentUpdate] Cannot read bundled resource address: "
                + error.Message);
        }

#if UNITY_EDITOR
        var settings = UnityEditor.AddressableAssets
            .AddressableAssetSettingsDefaultObject.Settings;
        if (settings != null)
        {
            string value = settings.profileSettings.GetValueByName(
                settings.activeProfileId,
                UnityEditor.AddressableAssets.Settings.AddressableAssetSettings.kRemoteLoadPath);
            return settings.profileSettings.EvaluateString(settings.activeProfileId, value);
        }
#endif
        return null;
    }
}
