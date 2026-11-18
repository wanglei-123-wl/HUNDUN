using System;

public sealed class RemoteSkillCastTimeline
{
    private long activeEpoch;
    private long anchorServerTimeMs;
    private double anchorLocalTimeSeconds;

    public void BeginSession(long connectionEpoch, long serverTimeMs,
        double receivedAtSeconds)
    {
        Clear();
        if (connectionEpoch <= 0) return;
        activeEpoch = connectionEpoch;
        ObserveServerTime(connectionEpoch, serverTimeMs, receivedAtSeconds);
    }

    public void EndSession(long connectionEpoch)
    {
        if (connectionEpoch == activeEpoch) Clear();
    }

    public bool TryGetMinimumElapsedSeconds(long connectionEpoch,
        long eventServerTimeMs, double nowSeconds, out double elapsedSeconds)
    {
        elapsedSeconds = 0d;
        if (connectionEpoch != activeEpoch || activeEpoch <= 0
            || anchorServerTimeMs <= 0 || eventServerTimeMs <= 0)
            return false;

        double lowerBoundServerTimeMs = anchorServerTimeMs
            + Math.Max(0d, nowSeconds - anchorLocalTimeSeconds) * 1000d;
        elapsedSeconds = Math.Max(0d,
            (lowerBoundServerTimeMs - eventServerTimeMs) / 1000d);
        return true;
    }

    public void ObserveServerTime(long connectionEpoch, long serverTimeMs,
        double receivedAtSeconds)
    {
        if (connectionEpoch != activeEpoch || activeEpoch <= 0
            || serverTimeMs <= 0) return;

        if (anchorServerTimeMs > 0)
        {
            double currentLowerBound = anchorServerTimeMs
                + Math.Max(0d, receivedAtSeconds - anchorLocalTimeSeconds) * 1000d;
            if (serverTimeMs <= currentLowerBound) return;
        }

        anchorServerTimeMs = serverTimeMs;
        anchorLocalTimeSeconds = receivedAtSeconds;
    }

    private void Clear()
    {
        activeEpoch = 0;
        anchorServerTimeMs = 0;
        anchorLocalTimeSeconds = 0d;
    }
}
