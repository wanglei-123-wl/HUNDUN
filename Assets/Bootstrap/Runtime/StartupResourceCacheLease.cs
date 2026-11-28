using System;
using System.Collections;
using System.IO;
using System.Text;
using System.Threading;
using QiDao.Bootstrap.CodeUpdate;
using UnityEngine;

// Unity's bundle cache cannot atomically commit concurrent downloads of the
// same version from different processes. Coordinate startup writes, then allow
// normal gameplay/cache reads to proceed independently.
public sealed class StartupResourceCacheLease : IDisposable
{
    private readonly Mutex mutex;
    private bool acquired;

    public StartupResourceCacheLease()
    {
        var cache = Caching.currentCacheForWriting;
        if (!cache.valid) throw new IOException("The resource cache is unavailable.");
        string path = Path.GetFullPath(cache.path).ToUpperInvariant();
        mutex = new Mutex(false, "Local\\QiDao.ResourceStartup." + CodeUpdateProtocol.Hash(Encoding.UTF8.GetBytes(path)));
    }

    public IEnumerator Acquire()
    {
        while (!acquired)
        {
            try { acquired = mutex.WaitOne(0); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) yield return null;
        }
    }

    public void Dispose()
    {
        if (acquired) { mutex.ReleaseMutex(); acquired = false; }
        mutex.Dispose();
    }
}
