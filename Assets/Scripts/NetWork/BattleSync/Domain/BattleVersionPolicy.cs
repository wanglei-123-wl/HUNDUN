namespace Game.Client.BattleSync
{
    public enum BattleVersionComparison
    {
        Invalid = 0,
        Older = 1,
        Same = 2,
        Newer = 3
    }

    public enum InventoryRevisionDecision
    {
        Invalid = 0,
        ApplyDelta = 1,
        IgnoreDuplicateOrStale = 2,
        RequestSnapshot = 3
    }

    public static class BattleVersionPolicy
    {
        public static BattleVersionComparison Compare(long incoming, long current)
        {
            if (incoming < 0 || current < 0)
            {
                return BattleVersionComparison.Invalid;
            }

            if (incoming < current)
            {
                return BattleVersionComparison.Older;
            }

            return incoming == current
                ? BattleVersionComparison.Same
                : BattleVersionComparison.Newer;
        }

        public static BattleVersionComparison CompareSnapshotRevision(long incoming, long current)
        {
            return Compare(incoming, current);
        }

        public static BattleVersionComparison CompareVisibilityRevision(long incoming, long current)
        {
            return Compare(incoming, current);
        }

        public static BattleVersionComparison CompareServerTick(long incoming, long current)
        {
            return Compare(incoming, current);
        }

        public static BattleVersionComparison CompareFrameSequence(long incoming, long current)
        {
            return Compare(incoming, current);
        }

        public static BattleVersionComparison CompareSpawnGeneration(long incoming, long current)
        {
            return Compare(incoming, current);
        }

        public static BattleVersionComparison CompareCombatRevision(long incoming, long current)
        {
            return Compare(incoming, current);
        }

        public static InventoryRevisionDecision EvaluateInventoryDelta(long incoming, long current)
        {
            if (incoming <= 0 || current < 0)
            {
                return InventoryRevisionDecision.Invalid;
            }

            if (incoming <= current)
            {
                return InventoryRevisionDecision.IgnoreDuplicateOrStale;
            }

            return incoming - current == 1
                ? InventoryRevisionDecision.ApplyDelta
                : InventoryRevisionDecision.RequestSnapshot;
        }
    }
}
