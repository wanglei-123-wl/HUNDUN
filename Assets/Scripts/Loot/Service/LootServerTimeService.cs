using System;
using UnityEngine;

public sealed class LootServerTimeService
{
    private readonly Func<double> getMonotonicTimeSeconds;
    private long synchronizedServerTimeMs;
    private double synchronizedLocalTimeSeconds;
    private bool synchronized;

    public LootServerTimeService()
        : this(() => Time.realtimeSinceStartupAsDouble)
    {
    }

    public LootServerTimeService(Func<double> getMonotonicTimeSeconds)
    {
        this.getMonotonicTimeSeconds = getMonotonicTimeSeconds
            ?? throw new ArgumentNullException(nameof(getMonotonicTimeSeconds));
    }

    public void Synchronize(long serverTimeMs)
    {
        if (serverTimeMs <= 0)
        {
            return;
        }

        long estimatedServerTimeMs = GetEstimatedServerTimeMs();
        if (synchronized && serverTimeMs < estimatedServerTimeMs)
        {
            return;
        }

        synchronizedServerTimeMs = serverTimeMs;
        synchronizedLocalTimeSeconds = getMonotonicTimeSeconds();
        synchronized = true;
    }

    public long GetEstimatedServerTimeMs()
    {
        if (!synchronized)
        {
            return 0;
        }

        double elapsedSeconds = getMonotonicTimeSeconds() - synchronizedLocalTimeSeconds;
        if (elapsedSeconds <= 0d)
        {
            return synchronizedServerTimeMs;
        }

        return synchronizedServerTimeMs + (long)(elapsedSeconds * 1000d);
    }

    public void Reset()
    {
        synchronizedServerTimeMs = 0;
        synchronizedLocalTimeSeconds = 0d;
        synchronized = false;
    }
}
