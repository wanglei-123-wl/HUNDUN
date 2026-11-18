using System;
using System.Collections.Generic;

namespace Game.Client.BattleSync
{
    [Flags]
    public enum InitialSnapshotKind
    {
        None = 0,
        World = 1 << 0,
        Monster = 1 << 1,
        Loot = 1 << 2,
        Inventory = 1 << 3,
        Combat = 1 << 4,
        All = World | Monster | Loot | Inventory | Combat
    }

    public enum InitialSnapshotSource
    {
        SnapshotMessage = 0,
        EnterWorldResult = 1
    }

    public readonly struct InitialSnapshotReceipt
    {
        public long ConnectionEpoch { get; }

        public string SceneId { get; }

        public InitialSnapshotKind Kind { get; }

        public long SnapshotRevision { get; }

        public long ServerTick { get; }

        public long ServerTime { get; }

        public string Reason { get; }

        public long AppliedAtMs { get; }
        public InitialSnapshotSource Source { get; }

        public InitialSnapshotReceipt(
            long connectionEpoch,
            string sceneId,
            InitialSnapshotKind kind,
            long snapshotRevision,
            long serverTick,
            long serverTime,
            string reason,
            long appliedAtMs,
            InitialSnapshotSource source = InitialSnapshotSource.SnapshotMessage)
        {
            ConnectionEpoch = connectionEpoch;
            SceneId = sceneId ?? string.Empty;
            Kind = kind;
            SnapshotRevision = snapshotRevision;
            ServerTick = serverTick;
            ServerTime = serverTime;
            Reason = reason ?? string.Empty;
            AppliedAtMs = appliedAtMs;
            Source = source;
        }
    }

    public enum InitialSnapshotBarrierStatus
    {
        Applied = 0,
        Completed = 1,
        Duplicate = 2,
        NewerBatchStarted = 3,
        NewerBatchStartedAndCompleted = 4,
        InvalidArguments = 5,
        NotActive = 6,
        SessionMismatch = 7,
        SceneMismatch = 8,
        OlderRevision = 9,
        BatchMetadataMismatch = 10,
        AlreadyInvalidated = 11,
        SourceUpgraded = 12
    }

    public sealed class InitialSnapshotBarrierResult
    {
        public InitialSnapshotBarrierStatus Status { get; }

        public long PreviousSnapshotRevision { get; }

        public long CurrentSnapshotRevision { get; }

        public InitialSnapshotKind AppliedKinds { get; }

        public InitialSnapshotKind MissingKinds { get; }

        public bool IsComplete { get; }

        public bool RequiresSnapshotRepair { get; }

        public string Detail { get; }

        public bool IsAccepted =>
            Status == InitialSnapshotBarrierStatus.Applied
            || Status == InitialSnapshotBarrierStatus.Completed
            || Status == InitialSnapshotBarrierStatus.Duplicate
            || Status == InitialSnapshotBarrierStatus.SourceUpgraded
            || Status == InitialSnapshotBarrierStatus.NewerBatchStarted
            || Status == InitialSnapshotBarrierStatus.NewerBatchStartedAndCompleted;

        public InitialSnapshotBarrierResult(
            InitialSnapshotBarrierStatus status,
            long previousSnapshotRevision,
            long currentSnapshotRevision,
            InitialSnapshotKind appliedKinds,
            InitialSnapshotKind missingKinds,
            bool isComplete,
            bool requiresSnapshotRepair,
            string detail)
        {
            Status = status;
            PreviousSnapshotRevision = previousSnapshotRevision;
            CurrentSnapshotRevision = currentSnapshotRevision;
            AppliedKinds = appliedKinds;
            MissingKinds = missingKinds;
            IsComplete = isComplete;
            RequiresSnapshotRepair = requiresSnapshotRepair;
            Detail = detail ?? string.Empty;
        }
    }

    public sealed class InitialSnapshotBarrier
    {
        private readonly Dictionary<InitialSnapshotKind, InitialSnapshotReceipt>
            appliedReceipts =
                new Dictionary<InitialSnapshotKind, InitialSnapshotReceipt>();

        public bool IsActive { get; private set; }

        public bool IsComplete { get; private set; }

        public long ConnectionEpoch { get; private set; }

        public string SceneId { get; private set; }

        public long SnapshotRevision { get; private set; }

        public long BatchServerTick { get; private set; }

        public long BatchServerTime { get; private set; }

        public string Reason { get; private set; }

        public long StartedAtMs { get; private set; }

        public long LastAppliedAtMs { get; private set; }

        public InitialSnapshotKind RequiredKinds { get; private set; }

        public InitialSnapshotKind AppliedKinds { get; private set; }

        public InitialSnapshotKind MissingKinds =>
            RequiredKinds & ~AppliedKinds;

        public int AppliedCount =>
            CountKinds(AppliedKinds);

        public InitialSnapshotBarrier()
            : this(InitialSnapshotKind.All)
        {
        }

        public InitialSnapshotBarrier(InitialSnapshotKind requiredKinds)
        {
            if (!IsValidRequiredKinds(requiredKinds))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(requiredKinds),
                    "Required kinds must contain only supported snapshot kinds and cannot be empty.");
            }

            RequiredKinds = requiredKinds;
            ResetState();
        }

        public InitialSnapshotBarrierResult BeginBatch(
            long connectionEpoch,
            string sceneId,
            long snapshotRevision,
            long startedAtMs)
        {
            return BeginBatch(connectionEpoch, sceneId, snapshotRevision,
                startedAtMs, RequiredKinds);
        }

        public InitialSnapshotBarrierResult BeginBatch(
            long connectionEpoch,
            string sceneId,
            long snapshotRevision,
            long startedAtMs,
            InitialSnapshotKind requiredKinds)
        {
            long previousSnapshotRevision = SnapshotRevision;

            if (connectionEpoch <= 0
                || string.IsNullOrWhiteSpace(sceneId)
                || snapshotRevision <= 0
                || startedAtMs < 0 || !IsValidRequiredKinds(requiredKinds))
            {
                return CreateResult(
                    InitialSnapshotBarrierStatus.InvalidArguments,
                    previousSnapshotRevision,
                    false,
                    "Connection epoch, scene, revision and start time must be valid.");
            }

            RequiredKinds = requiredKinds;
            IsActive = true;
            IsComplete = false;
            ConnectionEpoch = connectionEpoch;
            SceneId = sceneId;
            SnapshotRevision = snapshotRevision;
            BatchServerTick = 0;
            BatchServerTime = 0;
            Reason = string.Empty;
            StartedAtMs = startedAtMs;
            LastAppliedAtMs = 0;
            AppliedKinds = InitialSnapshotKind.None;
            appliedReceipts.Clear();

            return CreateResult(
                InitialSnapshotBarrierStatus.Applied,
                previousSnapshotRevision,
                false,
                "Snapshot batch started.");
        }

        public InitialSnapshotBarrierResult Inspect(
            InitialSnapshotReceipt receipt)
        {
            long previousSnapshotRevision = SnapshotRevision;

            if (!IsActive)
            {
                return CreateResult(
                    InitialSnapshotBarrierStatus.NotActive,
                    previousSnapshotRevision,
                    false,
                    "There is no active snapshot batch.");
            }

            if (receipt.ConnectionEpoch <= 0)
            {
                return CreateResult(
                    InitialSnapshotBarrierStatus.InvalidArguments,
                    previousSnapshotRevision,
                    false,
                    "Receipt connection epoch must be greater than zero.");
            }

            if (receipt.ConnectionEpoch != ConnectionEpoch)
            {
                return CreateResult(
                    InitialSnapshotBarrierStatus.SessionMismatch,
                    previousSnapshotRevision,
                    false,
                    "Receipt connection epoch does not match the active batch.");
            }

            if (string.IsNullOrWhiteSpace(receipt.SceneId))
            {
                return CreateResult(
                    InitialSnapshotBarrierStatus.InvalidArguments,
                    previousSnapshotRevision,
                    false,
                    "Receipt scene must not be empty.");
            }

            if (!string.Equals(
                    receipt.SceneId,
                    SceneId,
                    StringComparison.Ordinal))
            {
                return CreateResult(
                    InitialSnapshotBarrierStatus.SceneMismatch,
                    previousSnapshotRevision,
                    false,
                    "Receipt scene does not match the active batch.");
            }

            bool isEnterWorldSeed =
                receipt.Source == InitialSnapshotSource.EnterWorldResult;

            if (!IsSingleRequiredKind(receipt.Kind)
                || receipt.SnapshotRevision <= 0
                || (isEnterWorldSeed
                    ? receipt.Kind != InitialSnapshotKind.World
                        || receipt.ServerTick != 0
                    : receipt.ServerTick <= 0)
                || receipt.ServerTime <= 0
                || string.IsNullOrWhiteSpace(receipt.Reason)
                || receipt.AppliedAtMs < 0)
            {
                return CreateResult(
                    InitialSnapshotBarrierStatus.InvalidArguments,
                    previousSnapshotRevision,
                    false,
                    "Receipt kind, revision, metadata and apply time must be valid.");
            }

            if (receipt.SnapshotRevision < SnapshotRevision)
            {
                return CreateResult(
                    InitialSnapshotBarrierStatus.OlderRevision,
                    previousSnapshotRevision,
                    false,
                    "Receipt snapshot revision is older than the active batch.");
            }

            if (receipt.SnapshotRevision > SnapshotRevision)
            {
                InitialSnapshotKind predictedKinds = receipt.Kind;
                bool predictedComplete =
                    (predictedKinds & RequiredKinds) == RequiredKinds;

                return CreateProjectedResult(
                    predictedComplete
                        ? InitialSnapshotBarrierStatus.NewerBatchStartedAndCompleted
                        : InitialSnapshotBarrierStatus.NewerBatchStarted,
                    previousSnapshotRevision,
                    receipt.SnapshotRevision,
                    predictedKinds,
                    predictedComplete,
                    false,
                    "Receipt starts a newer snapshot batch.");
            }

            if (receipt.Source == InitialSnapshotSource.SnapshotMessage
                && BatchServerTick > 0
                && !HasMatchingBatchMetadata(receipt))
            {
                return CreateResult(
                    InitialSnapshotBarrierStatus.BatchMetadataMismatch,
                    previousSnapshotRevision,
                    true,
                    "Receipt metadata does not match the active snapshot batch.");
            }

            if (appliedReceipts.ContainsKey(receipt.Kind))
            {
                InitialSnapshotReceipt existing = appliedReceipts[receipt.Kind];
                if (receipt.Kind == InitialSnapshotKind.World
                    && existing.Source == InitialSnapshotSource.EnterWorldResult
                    && receipt.Source == InitialSnapshotSource.SnapshotMessage)
                {
                    return CreateResult(
                        InitialSnapshotBarrierStatus.SourceUpgraded,
                        previousSnapshotRevision,
                        false,
                        "Formal world snapshot can replace the enter-world seed.");
                }

                return CreateResult(
                    InitialSnapshotBarrierStatus.Duplicate,
                    previousSnapshotRevision,
                    false,
                    "Snapshot kind has already been applied for this revision.");
            }

            InitialSnapshotKind appliedKinds = AppliedKinds | receipt.Kind;
            bool isComplete =
                (appliedKinds & RequiredKinds) == RequiredKinds;

            return CreateProjectedResult(
                isComplete
                    ? InitialSnapshotBarrierStatus.Completed
                    : InitialSnapshotBarrierStatus.Applied,
                previousSnapshotRevision,
                SnapshotRevision,
                appliedKinds,
                isComplete,
                false,
                isComplete
                    ? "Snapshot batch will be complete after applying this receipt."
                    : "Snapshot receipt can be applied to the active batch.");
        }

        public InitialSnapshotBarrierResult MarkApplied(
            InitialSnapshotReceipt receipt)
        {
            InitialSnapshotBarrierResult inspection = Inspect(receipt);

            if (!inspection.IsAccepted
                || inspection.Status == InitialSnapshotBarrierStatus.Duplicate)
            {
                return inspection;
            }

            bool startsNewerBatch =
                inspection.Status == InitialSnapshotBarrierStatus.NewerBatchStarted
                || inspection.Status
                    == InitialSnapshotBarrierStatus.NewerBatchStartedAndCompleted;

            if (startsNewerBatch)
            {
                SnapshotRevision = receipt.SnapshotRevision;
                BatchServerTick = 0;
                BatchServerTime = 0;
                Reason = string.Empty;
                StartedAtMs = receipt.AppliedAtMs;
                LastAppliedAtMs = 0;
                AppliedKinds = InitialSnapshotKind.None;
                IsComplete = false;
                appliedReceipts.Clear();
            }

            if (receipt.Source == InitialSnapshotSource.SnapshotMessage
                && BatchServerTick == 0)
            {
                BatchServerTick = receipt.ServerTick;
                BatchServerTime = receipt.ServerTime;
                Reason = receipt.Reason;
            }

            appliedReceipts[receipt.Kind] = receipt;
            AppliedKinds |= receipt.Kind;
            LastAppliedAtMs = receipt.AppliedAtMs;
            IsComplete =
                (AppliedKinds & RequiredKinds) == RequiredKinds;

            InitialSnapshotBarrierStatus status;

            if (startsNewerBatch)
            {
                status = IsComplete
                    ? InitialSnapshotBarrierStatus.NewerBatchStartedAndCompleted
                    : InitialSnapshotBarrierStatus.NewerBatchStarted;
            }
            else if (inspection.Status == InitialSnapshotBarrierStatus.SourceUpgraded)
            {
                status = InitialSnapshotBarrierStatus.SourceUpgraded;
            }
            else
            {
                status = IsComplete
                    ? InitialSnapshotBarrierStatus.Completed
                    : InitialSnapshotBarrierStatus.Applied;
            }

            return CreateResult(
                status,
                inspection.PreviousSnapshotRevision,
                false,
                IsComplete
                    ? "Snapshot batch completed."
                    : "Snapshot receipt was applied to the active batch.");
        }

        public bool HasTimedOut(long nowMs, long timeoutMs)
        {
            if (!IsActive
                || IsComplete
                || nowMs < 0
                || timeoutMs <= 0
                || nowMs < StartedAtMs)
            {
                return false;
            }

            return nowMs - StartedAtMs >= timeoutMs;
        }

        public bool Invalidate(long connectionEpoch)
        {
            if (!IsActive || connectionEpoch != ConnectionEpoch)
            {
                return false;
            }

            ResetState();
            return true;
        }

        public bool TryGetReceipt(
            InitialSnapshotKind kind,
            out InitialSnapshotReceipt receipt)
        {
            if (!IsSingleRequiredKind(kind))
            {
                receipt = default(InitialSnapshotReceipt);
                return false;
            }

            return appliedReceipts.TryGetValue(kind, out receipt);
        }

        public bool ResetCompletedBatch(
            long connectionEpoch,
            long snapshotRevision,
            InitialSnapshotKind? nextRequiredKinds = null)
        {
            if (!IsActive
                || !IsComplete
                || connectionEpoch != ConnectionEpoch
                || snapshotRevision != SnapshotRevision
                || (nextRequiredKinds.HasValue && !IsValidRequiredKinds(nextRequiredKinds.Value)))
            {
                return false;
            }

            ResetState();
            if (nextRequiredKinds.HasValue)
                RequiredKinds = nextRequiredKinds.Value;
            return true;
        }

        public bool ValidateInternalConsistency(out string error)
        {
            if (!IsValidRequiredKinds(RequiredKinds))
            {
                error = "Required kinds are invalid.";
                return false;
            }

            if (!IsActive)
            {
                if (IsComplete
                    || ConnectionEpoch != 0
                    || !string.IsNullOrEmpty(SceneId)
                    || SnapshotRevision != 0
                    || BatchServerTick != 0
                    || BatchServerTime != 0
                    || !string.IsNullOrEmpty(Reason)
                    || StartedAtMs != 0
                    || LastAppliedAtMs != 0
                    || AppliedKinds != InitialSnapshotKind.None
                    || appliedReceipts.Count != 0)
                {
                    error = "Inactive barrier contains active batch state.";
                    return false;
                }

                error = string.Empty;
                return true;
            }

            if (ConnectionEpoch <= 0
                || string.IsNullOrWhiteSpace(SceneId)
                || SnapshotRevision <= 0
                || StartedAtMs < 0)
            {
                error = "Active barrier identity or start time is invalid.";
                return false;
            }

            if ((AppliedKinds & ~RequiredKinds) != InitialSnapshotKind.None)
            {
                error = "Applied kinds contain kinds outside the required mask.";
                return false;
            }

            if (AppliedCount != appliedReceipts.Count)
            {
                error = "Applied kind count does not match receipt count.";
                return false;
            }

            InitialSnapshotKind receiptKinds = InitialSnapshotKind.None;
            bool lastAppliedTimeExists = appliedReceipts.Count == 0;

            foreach (KeyValuePair<InitialSnapshotKind, InitialSnapshotReceipt> pair
                in appliedReceipts)
            {
                InitialSnapshotReceipt receipt = pair.Value;

                if (!IsSingleRequiredKind(pair.Key)
                    || pair.Key != receipt.Kind)
                {
                    error = "Receipt dictionary contains an invalid kind mapping.";
                    return false;
                }

                if ((AppliedKinds & pair.Key) == InitialSnapshotKind.None)
                {
                    error = "Receipt kind is missing from the applied kind mask.";
                    return false;
                }

                if (receipt.ConnectionEpoch != ConnectionEpoch
                    || !string.Equals(
                        receipt.SceneId,
                        SceneId,
                        StringComparison.Ordinal)
                    || receipt.SnapshotRevision != SnapshotRevision)
                {
                    error = "Receipt batch identity does not match the active barrier.";
                    return false;
                }

                if ((receipt.Source == InitialSnapshotSource.SnapshotMessage
                        && (receipt.ServerTick != BatchServerTick
                            || receipt.ServerTime != BatchServerTime
                            || !string.Equals(receipt.Reason, Reason, StringComparison.Ordinal)))
                    || (receipt.Source == InitialSnapshotSource.EnterWorldResult
                        && (receipt.Kind != InitialSnapshotKind.World || receipt.ServerTick != 0))
                    || receipt.AppliedAtMs < 0)
                {
                    error = "Receipt metadata does not match the active barrier.";
                    return false;
                }

                if (receipt.AppliedAtMs == LastAppliedAtMs)
                {
                    lastAppliedTimeExists = true;
                }

                receiptKinds |= pair.Key;
            }

            if (receiptKinds != AppliedKinds)
            {
                error = "Applied kind mask does not exactly match stored receipts.";
                return false;
            }

            if (appliedReceipts.Count == 0)
            {
                if (BatchServerTick != 0
                    || BatchServerTime != 0
                    || !string.IsNullOrEmpty(Reason)
                    || LastAppliedAtMs != 0)
                {
                    error = "Empty batch contains established receipt metadata.";
                    return false;
                }
            }
            else if ((HasFormalSnapshotReceipt()
                    && (BatchServerTick <= 0 || BatchServerTime <= 0 || string.IsNullOrWhiteSpace(Reason)))
                || !lastAppliedTimeExists)
            {
                error = "Non-empty batch metadata or last apply time is invalid.";
                return false;
            }

            bool shouldBeComplete =
                (AppliedKinds & RequiredKinds) == RequiredKinds;

            if (IsComplete != shouldBeComplete)
            {
                error = "Completion flag does not match the applied kind mask.";
                return false;
            }

            if ((MissingKinds == InitialSnapshotKind.None) != IsComplete)
            {
                error = "Missing kind mask does not match completion state.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private bool HasMatchingBatchMetadata(
            InitialSnapshotReceipt receipt)
        {
            return receipt.ServerTick == BatchServerTick
                && receipt.ServerTime == BatchServerTime
                && string.Equals(
                    receipt.Reason,
                    Reason,
                    StringComparison.Ordinal);
        }

        private bool HasFormalSnapshotReceipt()
        {
            foreach (InitialSnapshotReceipt receipt in appliedReceipts.Values)
                if (receipt.Source == InitialSnapshotSource.SnapshotMessage) return true;
            return false;
        }

        private bool IsSingleRequiredKind(InitialSnapshotKind kind)
        {
            int value = (int)kind;

            return kind != InitialSnapshotKind.None
                && (kind & ~InitialSnapshotKind.All)
                    == InitialSnapshotKind.None
                && (kind & RequiredKinds) == kind
                && (value & (value - 1)) == 0;
        }

        private InitialSnapshotBarrierResult CreateResult(
            InitialSnapshotBarrierStatus status,
            long previousSnapshotRevision,
            bool requiresSnapshotRepair,
            string detail)
        {
            return CreateProjectedResult(
                status,
                previousSnapshotRevision,
                SnapshotRevision,
                AppliedKinds,
                IsComplete,
                requiresSnapshotRepair,
                detail);
        }

        private InitialSnapshotBarrierResult CreateProjectedResult(
            InitialSnapshotBarrierStatus status,
            long previousSnapshotRevision,
            long currentSnapshotRevision,
            InitialSnapshotKind appliedKinds,
            bool isComplete,
            bool requiresSnapshotRepair,
            string detail)
        {
            return new InitialSnapshotBarrierResult(
                status,
                previousSnapshotRevision,
                currentSnapshotRevision,
                appliedKinds,
                RequiredKinds & ~appliedKinds,
                isComplete,
                requiresSnapshotRepair,
                detail);
        }

        private static bool IsValidRequiredKinds(
            InitialSnapshotKind requiredKinds)
        {
            return requiredKinds != InitialSnapshotKind.None
                && (requiredKinds & ~InitialSnapshotKind.All)
                    == InitialSnapshotKind.None;
        }

        private static int CountKinds(InitialSnapshotKind kinds)
        {
            int count = 0;

            if ((kinds & InitialSnapshotKind.World) != 0)
            {
                count++;
            }

            if ((kinds & InitialSnapshotKind.Monster) != 0)
            {
                count++;
            }

            if ((kinds & InitialSnapshotKind.Loot) != 0)
            {
                count++;
            }

            if ((kinds & InitialSnapshotKind.Inventory) != 0)
            {
                count++;
            }

            if ((kinds & InitialSnapshotKind.Combat) != 0)
            {
                count++;
            }

            return count;
        }

        private void ResetState()
        {
            IsActive = false;
            IsComplete = false;
            ConnectionEpoch = 0;
            SceneId = string.Empty;
            SnapshotRevision = 0;
            BatchServerTick = 0;
            BatchServerTime = 0;
            Reason = string.Empty;
            StartedAtMs = 0;
            LastAppliedAtMs = 0;
            AppliedKinds = InitialSnapshotKind.None;
            appliedReceipts.Clear();
        }
    }
}
