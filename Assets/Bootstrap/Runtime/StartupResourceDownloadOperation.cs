using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;

// Downloads to persistent Addressables cache; never clears already valid bundles.
public sealed class StartupResourceDownloadOperation
{
    private readonly IList<IResourceLocation> locations;
    private readonly Action<long, long> reportProgress;
    public bool Succeeded { get; private set; }
    public long PendingBytes { get; private set; }
    public string Error { get; private set; }

    public StartupResourceDownloadOperation(IList<IResourceLocation> locations, Action<long, long> reportProgress)
    {
        this.locations = locations ?? throw new ArgumentNullException(nameof(locations));
        this.reportProgress = reportProgress;
    }

    public IEnumerator Run()
    {
        Succeeded = false;
        Error = null;
        PendingBytes = 0;
        var size = Addressables.GetDownloadSizeAsync(locations);
        try
        {
            yield return size;
            if (size.Status != AsyncOperationStatus.Succeeded)
            {
                Error = "Full resource download size could not be determined: " + size.OperationException;
                yield break;
            }
            PendingBytes = size.Result;
        }
        finally { if (size.IsValid()) Addressables.Release(size); }

        reportProgress?.Invoke(0, PendingBytes);
        if (PendingBytes > 0)
        {
            string failure = null;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                bool downloaded = false;
                var download = Addressables.DownloadDependenciesAsync(locations, false);
                try
                {
                    while (!download.IsDone)
                    {
                        reportProgress?.Invoke(download.GetDownloadStatus().DownloadedBytes, PendingBytes);
                        yield return null;
                    }
                    downloaded = download.Status == AsyncOperationStatus.Succeeded;
                    if (!downloaded) failure = download.OperationException?.ToString();
                }
                finally { if (download.IsValid()) Addressables.Release(download); }
                if (downloaded) break;
                // A second client can finish writing this cache entry while
                // Unity is moving our temporary download into the same path.
                // Release the failed provider before checking its committed cache.
                yield return null;
                var concurrent = Addressables.GetDownloadSizeAsync(locations);
                bool completedByAnotherClient;
                try
                {
                    yield return concurrent;
                    completedByAnotherClient = concurrent.Status == AsyncOperationStatus.Succeeded && concurrent.Result == 0;
                }
                finally { if (concurrent.IsValid()) Addressables.Release(concurrent); }
                if (completedByAnotherClient) break;
                if (attempt == 1)
                {
                    Error = "Full resource download failed: " + failure;
                    yield break;
                }
            }
        }

        // A provider reporting success is insufficient if a required bundle is
        // still missing or cannot persist in the cache. Never open login early.
        var remaining = Addressables.GetDownloadSizeAsync(locations);
        try
        {
            yield return remaining;
            if (remaining.Status != AsyncOperationStatus.Succeeded || remaining.Result != 0)
            {
                Error = "The full resource cache is incomplete after download: "
                    + (remaining.Status == AsyncOperationStatus.Succeeded ? remaining.Result.ToString()
                        : remaining.OperationException?.ToString());
                yield break;
            }
        }
        finally { if (remaining.IsValid()) Addressables.Release(remaining); }
        reportProgress?.Invoke(PendingBytes, PendingBytes);
        Succeeded = true;
    }
}
