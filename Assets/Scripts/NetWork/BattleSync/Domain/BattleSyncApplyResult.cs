namespace Game.Client.BattleSync
{
    public enum BattleSyncApplyResult
    {
        Applied = 0,
        AlreadyApplied = 1,
        IgnoredStale = 2,
        IgnoredWrongScene = 3,
        RetryableFailure = 4,
        InvalidMessage = 5
    }
}
