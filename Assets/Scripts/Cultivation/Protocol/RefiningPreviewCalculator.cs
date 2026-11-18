using System.Globalization;

namespace Game.Cultivation.Protocol
{
    public static class RefiningPreviewCalculator
    {
        public const long MaximumDurationSeconds = 365L * 24 * 60 * 60;

        public static bool TryCalculate(RefiningRuleDto rule, int count,
            out long durationSeconds, out long cultivation)
        {
            durationSeconds = 0;
            cultivation = 0;
            if (rule == null || count <= 0 || rule.secondsPerItem <= 0
                || rule.secondsPerItem > MaximumDurationSeconds
                || count > MaximumDurationSeconds / rule.secondsPerItem
                || rule.cultivationPerItem < 0
                || rule.cultivationPerItem > long.MaxValue / count)
                return false;
            durationSeconds = rule.secondsPerItem * count;
            cultivation = rule.cultivationPerItem * count;
            return true;
        }

        public static string FormatDuration(long seconds)
        {
            long days = seconds / 86400;
            long hours = seconds / 3600 % 24;
            string time = hours.ToString("00", CultureInfo.InvariantCulture)
                + ":" + (seconds / 60 % 60).ToString("00", CultureInfo.InvariantCulture)
                + ":" + (seconds % 60).ToString("00", CultureInfo.InvariantCulture);
            return days > 0
                ? days.ToString(CultureInfo.InvariantCulture) + "天 " + time
                : time;
        }
    }
}
