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
            var download = Addressables.DownloadDependenciesAsync(locations, false);
            try
            {
                while (!download.IsDone)
                {
                    reportProgress?.Invoke(download.GetDownloadStatus().DownloadedBytes, PendingBytes);
                    yield return null;
                }
                if (download.Status != AsyncOperationStatus.Succeeded)
                {
                    Error = "Full resource download failed: " + download.OperationException;
                    yield break;
                }
            }
            finally { if (download.IsValid()) Addressables.Release(download); }
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
