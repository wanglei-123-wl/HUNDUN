using System;
using Game.PlayerTrade.Protocol;

namespace Game.PlayerTrade.Model
{
    public sealed class PlayerTradeMarketPage
    {
        public string Cursor { get; }
        public string NextCursor { get; }
        public PlayerTradeOrderDto[] Orders { get; }

        public PlayerTradeMarketPage(
            string cursor,
            string nextCursor,
            PlayerTradeOrderDto[] orders)
        {
            Cursor = cursor ?? string.Empty;
            NextCursor = nextCursor ?? string.Empty;
            Orders = orders ?? new PlayerTradeOrderDto[0];
        }
    }

    public static class PlayerTradeTimeFormatter
    {
        public static string FormatRemaining(long expiresAtUnixMilliseconds)
        {
            if (expiresAtUnixMilliseconds <= 0)
                return "--";

            long remainingMilliseconds = expiresAtUnixMilliseconds
                - DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (remainingMilliseconds <= 0)
                return "已过期";

            TimeSpan remaining = TimeSpan.FromMilliseconds(remainingMilliseconds);
            if (remaining.TotalDays >= 1)
                return string.Format("{0}天{1:00}:{2:00}",
                    (int)remaining.TotalDays, remaining.Hours, remaining.Minutes);
            return string.Format("{0}:{1:00}:{2:00}",
                (int)remaining.TotalHours, remaining.Minutes, remaining.Seconds);
        }
    }
}
