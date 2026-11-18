using System;

namespace Game.Alchemy.Protocol
{
    public enum AlchemySnapshotApplyResult
    {
        Invalid,
        NotProvided,
        Stale,
        Unchanged,
        Applied
    }

    public sealed class AlchemySnapshotState
    {
        public AlchemySnapshotDto Snapshot { get; private set; }
        public long LastServerTime { get; private set; }
        public bool HasCurrentTask => Snapshot?.currentTask != null;

        public void Reset()
        {
            Snapshot = null;
            LastServerTime = 0;
        }

        public AlchemySnapshotApplyResult Apply(string json,
            AlchemySnapshotDto value, long expectedCharacterDbId, long serverTime)
        {
            if (!AlchemySnapshotWireState.TryRead(json,
                out bool hasSnapshot, out bool snapshotIsNull,
                out bool hasCurrentTask, out bool currentTaskIsNull))
                return AlchemySnapshotApplyResult.Invalid;
            if (!hasSnapshot || snapshotIsNull)
                return AlchemySnapshotApplyResult.NotProvided;
            if (expectedCharacterDbId <= 0 || serverTime <= 0 || value == null
                || value.characterDbId != expectedCharacterDbId
                || value.revision < 0 || value.learnedRecipeIds == null
                || !hasCurrentTask)
                return AlchemySnapshotApplyResult.Invalid;

            if (currentTaskIsNull)
                value.currentTask = null;
            else if (!AlchemySnapshotWireState.TryReadSucceeded(json,
                out bool hasSucceeded, out bool? succeeded)
                || value.currentTask == null || !hasSucceeded
                || string.IsNullOrWhiteSpace(value.currentTask.taskId)
                || string.IsNullOrWhiteSpace(value.currentTask.recipeId)
                || (value.currentTask.status != "active"
                    && value.currentTask.status != "ready")
                || (value.currentTask.status == "active" && succeeded != null)
                || (value.currentTask.status == "ready" && succeeded == null)
                || value.currentTask.startedAtUnixMilliseconds <= 0
                || value.currentTask.endsAtUnixMilliseconds
                    < value.currentTask.startedAtUnixMilliseconds)
                return AlchemySnapshotApplyResult.Invalid;
            else
                value.currentTask.SetSucceeded(succeeded);

            if (Snapshot != null && value.revision < Snapshot.revision)
                return AlchemySnapshotApplyResult.Stale;
            if (Snapshot != null && value.revision == Snapshot.revision)
            {
                if (serverTime < LastServerTime)
                    return AlchemySnapshotApplyResult.Stale;
                if ((Snapshot.currentTask == null) != currentTaskIsNull)
                {
                    if (serverTime == LastServerTime || !currentTaskIsNull)
                        return AlchemySnapshotApplyResult.Invalid;
                    Snapshot = value;
                    LastServerTime = serverTime;
                    return AlchemySnapshotApplyResult.Applied;
                }
                LastServerTime = serverTime;
                return AlchemySnapshotApplyResult.Unchanged;
            }

            Snapshot = value;
            LastServerTime = serverTime;
            return AlchemySnapshotApplyResult.Applied;
        }
    }
}
