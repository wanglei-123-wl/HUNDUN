using System;
using System.Collections.Generic;
using System.Diagnostics;

internal sealed class HeartbeatLatencyTracker
{
    private const int MaxPending = 8;

    private readonly object sync = new object();
    private readonly Dictionary<string, long> pending = new Dictionary<string, long>(StringComparer.Ordinal);
    private readonly long maxAgeTicks;
    private long connectionEpoch;
    private long lastSampleSendTicks;
    private long lastSampleReceiveTicks;
    private double lastRoundTripMs;
    private bool hasSample;

    internal HeartbeatLatencyTracker(double heartbeatIntervalSeconds)
    {
        maxAgeTicks = (long)(Stopwatch.Frequency * Math.Max(1d, heartbeatIntervalSeconds * 3d));
    }

    internal void Reset(long epoch)
    {
        lock (sync)
        {
            connectionEpoch = epoch;
            pending.Clear();
            hasSample = false;
            lastSampleSendTicks = 0;
            lastSampleReceiveTicks = 0;
            lastRoundTripMs = 0;
        }
    }

    internal void Begin(long epoch, string pingId, long sendTicks)
    {
        lock (sync)
        {
            if (epoch <= 0 || epoch != connectionEpoch || string.IsNullOrEmpty(pingId))
                return;

            string oldestId = null;
            long oldestTicks = long.MaxValue;
            List<string> expired = null;
            foreach (KeyValuePair<string, long> entry in pending)
            {
                if (sendTicks - entry.Value > maxAgeTicks)
                {
                    if (expired == null) expired = new List<string>();
                    expired.Add(entry.Key);
                }
                else if (entry.Value < oldestTicks)
                {
                    oldestId = entry.Key;
                    oldestTicks = entry.Value;
                }
            }

            if (expired != null)
            {
                foreach (string id in expired) pending.Remove(id);
            }
            if (pending.Count >= MaxPending && oldestId != null)
                pending.Remove(oldestId);

            pending[pingId] = sendTicks;
        }
    }

    internal void Forget(long epoch, string pingId)
    {
        lock (sync)
        {
            if (epoch == connectionEpoch && !string.IsNullOrEmpty(pingId))
                pending.Remove(pingId);
        }
    }

    internal bool Complete(long epoch, string pingId, long receiveTicks)
    {
        lock (sync)
        {
            long sendTicks;
            if (epoch <= 0 || epoch != connectionEpoch || string.IsNullOrEmpty(pingId)
                || !pending.TryGetValue(pingId, out sendTicks))
                return false;

            pending.Remove(pingId);
            long elapsedTicks = receiveTicks - sendTicks;
            if (elapsedTicks < 0 || elapsedTicks > maxAgeTicks
                || (hasSample && sendTicks < lastSampleSendTicks))
                return false;

            lastSampleSendTicks = sendTicks;
            lastSampleReceiveTicks = receiveTicks;
            lastRoundTripMs = elapsedTicks * 1000d / Stopwatch.Frequency;
            hasSample = true;
            return true;
        }
    }

    internal bool TryGet(long epoch, long nowTicks, out double roundTripMs)
    {
        lock (sync)
        {
            roundTripMs = 0;
            if (epoch <= 0 || epoch != connectionEpoch || !hasSample
                || nowTicks < lastSampleReceiveTicks
                || nowTicks - lastSampleReceiveTicks > maxAgeTicks)
                return false;

            roundTripMs = lastRoundTripMs;
            return true;
        }
    }
}
