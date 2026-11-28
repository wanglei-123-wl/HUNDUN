using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.AddressableAssets;
using UnityEngine.Networking;

public sealed class StartupCatalogSynchronization
{
    private readonly bool allowEditorAssetDatabase;
    private readonly Dictionary<string, string> confirmedHashes = new Dictionary<string, string>(StringComparer.Ordinal);
    public bool Succeeded { get; private set; }
    public bool SkippedEditorAssetDatabase { get; private set; }
    public string Error { get; private set; }
    public string FailureStage { get; private set; }
    public int CatalogCount => confirmedHashes.Count;

    public StartupCatalogSynchronization(bool allowEditorAssetDatabase = false)
    {
        this.allowEditorAssetDatabase = allowEditorAssetDatabase;
    }

    public IEnumerator Run()
    {
        Succeeded = false;
        Error = null;
        FailureStage = "check";
        SkippedEditorAssetDatabase = false;
        confirmedHashes.Clear();
        var check = Addressables.CheckForCatalogUpdates(false);
        List<string> catalogs = null;
        try
        {
            yield return check;
            if (check.Status != UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded
                || check.Result == null || check.Result.Any(string.IsNullOrWhiteSpace))
            {
                Error = "Catalog update check failed: " + check.OperationException;
                yield break;
            }
            catalogs = check.Result.Distinct(StringComparer.Ordinal).ToList();
        }
        finally { if (check.IsValid()) Addressables.Release(check); }

        if (catalogs.Count > 0)
        {
            FailureStage = "update";
            // Keep all valid bundle caches when replacing the catalog.
            var update = Addressables.UpdateCatalogs(false, catalogs, false);
            try
            {
                yield return update;
                if (update.Status != UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded
                    || update.Result == null || update.Result.Count == 0)
                {
                    Error = "Catalog replacement failed: " + update.OperationException;
                    yield break;
                }
            }
            finally { if (update.IsValid()) Addressables.Release(update); }
        }

        var current = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var locator in Addressables.ResourceLocators)
        {
            var info = Addressables.GetLocatorInfo(locator);
            if (info == null || !info.CanUpdateContent) continue;
            string url = Addressables.ResourceManager.TransformInternalId(info.HashLocation);
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                Error = "Catalog hash must use the selected HTTP resource server.";
                yield break;
            }
            if (!TryNormalizeHash(info.LocalHash, out string hash))
            {
                Error = "Loaded catalog has an invalid version hash.";
                yield break;
            }
            if (current.TryGetValue(url, out string previous) && previous != hash)
            {
                Error = "Loaded catalogs disagree about the same remote hash URL.";
                yield break;
            }
            current[url] = hash;
        }
        if (current.Count == 0)
        {
            if (!allowEditorAssetDatabase)
            {
                Error = "The Player has no remotely updatable catalog; refusing to trust cached content offline.";
                yield break;
            }
            SkippedEditorAssetDatabase = true;
            Succeeded = true;
            yield break;
        }
        FailureStage = "check";
        yield return ProbeHashes(current);
        if (Error != null) yield break;
        foreach (var pair in current) confirmedHashes.Add(pair.Key, pair.Value);
        Succeeded = true;
    }

    public IEnumerator ConfirmBeforeLogin()
    {
        if (!Succeeded) yield break;
        Succeeded = false;
        FailureStage = "confirm";
        yield return ProbeHashes(confirmedHashes);
        Succeeded = Error == null;
    }

    private IEnumerator ProbeHashes(IDictionary<string, string> expected)
    {
        foreach (var pair in expected)
        {
            using (var request = CreateHashRequest(pair.Key))
            {
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success
                    || request.responseCode != 200
                    || !TryNormalizeHash(request.downloadHandler?.text, out string actual))
                {
                    Error = "Fresh catalog hash request failed or returned invalid data: " + request.error;
                    yield break;
                }
                if (actual != pair.Value)
                {
                    FailureStage = "changed";
                    Error = "The remote resource version changed; retry startup before entering login.";
                    yield break;
                }
            }
        }
    }

    public static UnityWebRequest CreateHashRequest(string url)
    {
        var builder = new UriBuilder(url);
        string query = builder.Query.TrimStart('?');
        builder.Query = (query.Length > 0 ? query + "&" : string.Empty) + "startup_check=" + Guid.NewGuid().ToString("N");
        var request = UnityWebRequest.Get(builder.Uri.AbsoluteUri);
        request.timeout = 15;
        request.SetRequestHeader("Cache-Control", "no-cache, no-store");
        Addressables.ResourceManager.WebRequestOverride?.Invoke(request);
        return request;
    }

    public static bool TryNormalizeHash(string value, out string hash)
    {
        hash = value?.Trim().TrimStart('\uFEFF');
        if (hash == null || hash.Length != 32
            || hash.Any(character => !((character >= '0' && character <= '9')
                || (character >= 'a' && character <= 'f') || (character >= 'A' && character <= 'F'))))
        {
            hash = null;
            return false;
        }
        hash = hash.ToLowerInvariant();
        return true;
    }
}
