using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;

public sealed class StartupResourceCacheVerification
{
    private readonly StartupResourceDownloadPlan plan;
    private readonly Action<int, int> reportProgress;
    public bool Succeeded { get; private set; }
    public string Error { get; private set; }

    public StartupResourceCacheVerification(StartupResourceDownloadPlan plan, Action<int, int> reportProgress)
    {
        this.plan = plan ?? throw new ArgumentNullException(nameof(plan));
        this.reportProgress = reportProgress;
    }

    public IEnumerator Run()
    {
        Succeeded = false;
        Error = null;
        int checkedBundles = 0;
        reportProgress?.Invoke(0, plan.RemoteBundles.Count);
        foreach (var bundle in plan.RemoteBundles)
        {
            IResourceLocation verification;
            try { verification = CreateVerificationLocation(bundle); }
            catch (Exception error) { Error = error.Message; yield break; }
            bool verified = false;
            string failure = null;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                var operation = Addressables.DownloadDependenciesAsync(new[] { verification }, false);
                try
                {
                    yield return operation;
                    verified = operation.Status == AsyncOperationStatus.Succeeded;
                    if (!verified) failure = operation.OperationException?.ToString();
                }
                finally { if (operation.IsValid()) Addressables.Release(operation); }
                if (verified) break;
                if (attempt == 0)
                {
                    // Native cache recovery can fail while its old download
                    // handler is still closing the damaged entry. Release the
                    // operation and cross a frame before a bounded fresh try.
                    yield return null;
                    var options = (AssetBundleRequestOptions)verification.Data;
                    Caching.ClearCachedVersion(options.BundleName, Hash128.Parse(options.Hash));
                    yield return null;
                }
            }
            if (!verified)
            {
                Error = "Cached bundle could not be validated or repaired: " + bundle.InternalId + " " + failure;
                yield break;
            }
            checkedBundles++;
            reportProgress?.Invoke(checkedBundles, plan.RemoteBundles.Count);
        }
        var remaining = Addressables.GetDownloadSizeAsync(plan.DownloadLocations);
        try
        {
            yield return remaining;
            if (remaining.Status != AsyncOperationStatus.Succeeded || remaining.Result != 0)
            {
                Error = "Resource cache is incomplete after verification.";
                yield break;
            }
        }
        finally { if (remaining.IsValid()) Addressables.Release(remaining); }
        Succeeded = true;
    }

    public static IResourceLocation CreateVerificationLocation(IResourceLocation bundle)
    {
        var original = bundle?.Data as AssetBundleRequestOptions;
        if (original == null || original.Crc == 0 || original.BundleSize <= 0
            || string.IsNullOrWhiteSpace(original.BundleName)
            || !StartupCatalogSynchronization.TryNormalizeHash(original.Hash, out string hash))
            throw new InvalidOperationException("Remote bundle has no usable size, name, version hash or CRC.");
        // Isolate each bundle: no scene/assets are loaded, shared dependencies are
        // not repeatedly checked, and the catalog's original options stay intact.
        var options = new AssetBundleRequestOptions
        {
            BundleName = original.BundleName, Hash = hash, BundleSize = original.BundleSize,
            Crc = original.Crc, Timeout = original.Timeout, RetryCount = original.RetryCount,
            RedirectLimit = original.RedirectLimit, UseCrcForCachedBundle = true,
            ClearOtherCachedVersionsWhenLoaded = false
        };
        return new ResourceLocationBase("startup/cache-check", bundle.InternalId, bundle.ProviderId,
            typeof(IAssetBundleResource)) { Data = options };
    }
}
