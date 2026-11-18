using System;

namespace Game.Cultivation.Protocol
{
    public sealed class RefiningTaskClock
    {
        private readonly long durationMilliseconds;
        private readonly double remainingAtSyncMilliseconds;
        private readonly double syncedAtSeconds;

        private RefiningTaskClock(long duration, double remaining, double syncedAt)
        {
            durationMilliseconds = duration;
            remainingAtSyncMilliseconds = remaining;
            syncedAtSeconds = syncedAt;
        }

        public static bool TryCreate(RefiningTaskDto task, long serverTime,
            double monotonicSeconds, out RefiningTaskClock clock)
        {
            clock = null;
            if (task == null || serverTime <= 0
                || double.IsNaN(monotonicSeconds)
                || double.IsInfinity(monotonicSeconds)
                || task.startedAtUnixMilliseconds <= 0
                || task.endsAtUnixMilliseconds <= task.startedAtUnixMilliseconds)
                return false;
            long duration = task.endsAtUnixMilliseconds
                - task.startedAtUnixMilliseconds;
            if (duration > RefiningPreviewCalculator.MaximumDurationSeconds * 1000L)
                return false;
            clock = new RefiningTaskClock(duration,
                Math.Max(0d, (double)task.endsAtUnixMilliseconds - serverTime),
                monotonicSeconds);
            return true;
        }

        public long RemainingSeconds(double monotonicSeconds)
        {
            double remaining = RemainingMilliseconds(monotonicSeconds);
            return (long)Math.Ceiling(remaining / 1000d);
        }

        public float Progress(double monotonicSeconds)
        {
            return (float)Math.Max(0d, Math.Min(1d,
                1d - RemainingMilliseconds(monotonicSeconds)
                    / durationMilliseconds));
        }

        private double RemainingMilliseconds(double monotonicSeconds)
        {
            return Math.Max(0d, remainingAtSyncMilliseconds
                - Math.Max(0d, monotonicSeconds - syncedAtSeconds) * 1000d);
        }
    }
}
