using System;

namespace Game.Alchemy.Protocol
{
    public sealed class AlchemyTaskClock
    {
        private readonly double durationMilliseconds;
        private readonly double remainingAtSyncMilliseconds;
        private readonly double syncedAtSeconds;

        private AlchemyTaskClock(double duration, double remaining, double syncedAt)
        {
            durationMilliseconds = duration;
            remainingAtSyncMilliseconds = remaining;
            syncedAtSeconds = syncedAt;
        }

        public static bool TryCreate(AlchemyTaskDto task, long serverTime,
            double monotonicSeconds, out AlchemyTaskClock clock)
        {
            clock = null;
            if (task == null || serverTime <= 0
                || double.IsNaN(monotonicSeconds)
                || double.IsInfinity(monotonicSeconds)
                || task.startedAtUnixMilliseconds <= 0
                || task.endsAtUnixMilliseconds <= task.startedAtUnixMilliseconds)
                return false;
            double duration = (double)task.endsAtUnixMilliseconds
                - task.startedAtUnixMilliseconds;
            clock = new AlchemyTaskClock(duration,
                Math.Max(0d, (double)task.endsAtUnixMilliseconds - serverTime),
                monotonicSeconds);
            return true;
        }

        public long RemainingSeconds(double monotonicSeconds)
        {
            return (long)Math.Ceiling(RemainingMilliseconds(monotonicSeconds)
                / 1000d);
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
