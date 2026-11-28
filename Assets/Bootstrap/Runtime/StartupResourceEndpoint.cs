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

    public static string ResolveCodeManifestAddress(string configured)
    {
        if (selectedAddress == null) return configured;
        var target = new Uri(selectedAddress);
        var manifest = new UriBuilder(configured) { Scheme = target.Scheme, Host = target.Host,
            Port = target.IsDefaultPort ? -1 : target.Port };
        return manifest.Uri.AbsoluteUri;
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
        BootstrapResourceDiagnostics.LogEndpoint(BuiltAddress, normalized);
#endif
        return true;
    }

    private static string Transform(IResourceLocation location)
    {
        string internalId = previousTransform == null
            ? location.InternalId : previousTransform(location);
        string resolved = StartupResourceUrl.Redirect(internalId, BuiltAddress, selectedAddress);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        BootstrapResourceDiagnostics.LogResolvedAddress(internalId, resolved);
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
        // A migrated base has an empty local catalog, so its server selector
        // cannot depend on a baked remote catalog/hash. Keep existing Inspector
        // profiles usable; only their authority selects the signed code channel.
        try
        {
            var asset = Resources.Load<TextAsset>("Startup/HotUpdateBootstrap");
            if (asset != null)
            {
                var config = JsonUtility.FromJson<QiDao.Bootstrap.CodeUpdate.CodeUpdateConfiguration>(asset.text);
                var manifest = QiDao.Bootstrap.CodeUpdate.CodeUpdateProtocol.ManifestUri(config);
                return manifest.GetLeftPart(UriPartial.Authority) + "/StandaloneWindows64";
            }
        }
        catch (Exception error) { Debug.LogWarning("[CodeUpdate] Cannot read built-in resource server: " + error.Message); }
        return null;
    }
}
