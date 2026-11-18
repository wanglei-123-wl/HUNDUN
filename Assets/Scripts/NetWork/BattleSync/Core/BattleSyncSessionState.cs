using System;
using System.Collections.Generic;

namespace Game.Client.BattleSync
{
    public enum BattleSyncSessionPhase
    {
        Disconnected = 0,
        Connected = 1,
        EnterWorldSent = 2,
        InitializingSnapshots = 3,
        Ready = 4,
        Repairing = 5
    }

    public enum BattleSyncSessionTransitionStatus
    {
        Applied = 0,
        AlreadyInState = 1,
        InvalidArguments = 2,
        NoActiveSession = 3,
        SessionMismatch = 4,
        SceneMismatch = 5,
        InvalidPhase = 6,
        SnapshotRevisionMismatch = 7,
        StaleFrameSequence = 8
    }

    public sealed class BattleSyncSessionTransitionResult
    {
        public BattleSyncSessionTransitionStatus Status { get; }

        public BattleSyncSessionPhase PreviousPhase { get; }

        public BattleSyncSessionPhase CurrentPhase { get; }

        public long ConnectionEpoch { get; }

        public string Detail { get; }

        public bool IsSuccess =>
            Status == BattleSyncSessionTransitionStatus.Applied
            || Status == BattleSyncSessionTransitionStatus.AlreadyInState;

        public BattleSyncSessionTransitionResult(
            BattleSyncSessionTransitionStatus status,
            BattleSyncSessionPhase previousPhase,
            BattleSyncSessionPhase currentPhase,
            long connectionEpoch,
            string detail)
        {
            Status = status;
            PreviousPhase = previousPhase;
            CurrentPhase = currentPhase;
            ConnectionEpoch = connectionEpoch;
            Detail = detail ?? string.Empty;
        }
    }

    public sealed class BattleSyncSessionState
    {
        public BattleSyncSessionPhase Phase { get; private set; }

        public long ConnectionEpoch { get; private set; }

        public string SceneId { get; private set; }

        public long CharacterDbId { get; private set; }

        public long CommittedSnapshotRevision { get; private set; }

        public long PendingSnapshotRevision { get; private set; }

        public long LastStateFrameSequence { get; private set; }

        public bool HasActiveConnection =>
            Phase != BattleSyncSessionPhase.Disconnected
            && ConnectionEpoch > 0;

        public bool HasCommittedSnapshot =>
            CommittedSnapshotRevision > 0;

        public bool HasPendingSnapshotBatch =>
            PendingSnapshotRevision > 0;

        public bool IsReady =>
            Phase == BattleSyncSessionPhase.Ready;

        public BattleSyncSessionState()
        {
            ResetState();
        }

        public bool IsCurrentConnection(long connectionEpoch)
        {
            return HasActiveConnection
                && connectionEpoch > 0
                && connectionEpoch == ConnectionEpoch;
        }

        public bool IsCurrentScene(string sceneId)
        {
            return HasActiveConnection
                && !string.IsNullOrWhiteSpace(sceneId)
                && string.Equals(SceneId, sceneId, StringComparison.Ordinal);
        }

        public bool CanAcceptSnapshots(
            long connectionEpoch,
            string sceneId)
        {
            return IsCurrentConnection(connectionEpoch)
                && IsCurrentScene(sceneId)
                && (Phase == BattleSyncSessionPhase.InitializingSnapshots
                    || Phase == BattleSyncSessionPhase.Repairing);
        }

        public bool CanProcessStateFrames(
            long connectionEpoch,
            string sceneId)
        {
            return IsCurrentConnection(connectionEpoch)
                && IsCurrentScene(sceneId)
                && Phase == BattleSyncSessionPhase.Ready;
        }

        public BattleSyncSessionTransitionResult BeginConnection(
            long connectionEpoch)
        {
            BattleSyncSessionPhase previousPhase = Phase;

            if (connectionEpoch <= 0)
            {
                return CreateResult(
                    BattleSyncSessionTransitionStatus.InvalidArguments,
                    previousPhase,
                    "Connection epoch must be greater than zero.");
            }

            if (HasActiveConnection && connectionEpoch <= ConnectionEpoch)
            {
                return CreateResult(
                    BattleSyncSessionTransitionStatus.SessionMismatch,
                    previousPhase,
                    "New connection epoch must be newer than the active connection epoch.");
            }

            Phase = BattleSyncSessionPhase.Connected;
            ConnectionEpoch = connectionEpoch;
            SceneId = string.Empty;
            CharacterDbId = 0;
            CommittedSnapshotRevision = 0;
            PendingSnapshotRevision = 0;
            LastStateFrameSequence = 0;

            return CreateResult(
                BattleSyncSessionTransitionStatus.Applied,
                previousPhase,
                "Connection session started.");
        }

        public BattleSyncSessionTransitionResult MarkEnterWorldSent(
            long connectionEpoch)
        {
            BattleSyncSessionPhase previousPhase = Phase;
            BattleSyncSessionTransitionResult validation =
                ValidateConnection(connectionEpoch, previousPhase);

            if (validation != null)
            {
                return validation;
            }

            if (Phase == BattleSyncSessionPhase.EnterWorldSent)
            {
                return CreateResult(
                    BattleSyncSessionTransitionStatus.AlreadyInState,
                    previousPhase,
                    "Enter-world has already been marked as sent.");
            }

            if (Phase != BattleSyncSessionPhase.Connected)
            {
                return CreateResult(
                    BattleSyncSessionTransitionStatus.InvalidPhase,
                    previousPhase,
                    "Enter-world can only be sent from the connected phase.");
            }

            Phase = BattleSyncSessionPhase.EnterWorldSent;

            return CreateResult(
                BattleSyncSessionTransitionStatus.Applied,
                previousPhase,
                "Enter-world was marked as sent.");
        }

        public BattleSyncSessionTransitionResult BeginInitialSnapshotBatch(
            long connectionEpoch,
            string sceneId,
            long characterDbId,
            long snapshotRevision)
        {
            BattleSyncSessionPhase previousPhase = Phase;

            if (connectionEpoch <= 0
                || string.IsNullOrWhiteSpace(sceneId)
                || characterDbId <= 0
                || snapshotRevision <= 0)
            {
                return CreateResult(
                    BattleSyncSessionTransitionStatus.InvalidArguments,
                    previousPhase,
                    "Connection epoch, scene, character and snapshot revision must be valid.");
            }

            BattleSyncSessionTransitionResult validation =
                ValidateConnection(connectionEpoch, previousPhase);

            if (validation != null)
            {
                return validation;
            }

            if (Phase != BattleSyncSessionPhase.EnterWorldSent)
            {
                return CreateResult(
                    BattleSyncSessionTransitionStatus.InvalidPhase,
                    previousPhase,
                    "Initial snapshots can only begin after enter-world is sent.");
            }

            Phase = BattleSyncSessionPhase.InitializingSnapshots;
            SceneId = sceneId;
            CharacterDbId = characterDbId;
            CommittedSnapshotRevision = 0;
            PendingSnapshotRevision = snapshotRevision;
            LastStateFrameSequence = 0;

            return CreateResult(
                BattleSyncSessionTransitionStatus.Applied,
                previousPhase,
                "Initial snapshot batch started.");
        }

        public BattleSyncSessionTransitionResult BeginMapTransferSnapshotBatch(
            long connectionEpoch, string sceneId, long characterDbId,
            long snapshotRevision)
        {
            BattleSyncSessionPhase previousPhase = Phase;
            if (connectionEpoch <= 0 || string.IsNullOrWhiteSpace(sceneId)
                || characterDbId <= 0 || snapshotRevision <= 0)
                return CreateResult(BattleSyncSessionTransitionStatus.InvalidArguments,
                    previousPhase, "Map transfer snapshot context is invalid.");

            BattleSyncSessionTransitionResult validation =
                ValidateConnection(connectionEpoch, previousPhase);
            if (validation != null) return validation;
            if (Phase != BattleSyncSessionPhase.Ready
                || CharacterDbId != characterDbId)
                return CreateResult(BattleSyncSessionTransitionStatus.InvalidPhase,
                    previousPhase, "Map transfer requires the ready character.");

            Phase = BattleSyncSessionPhase.InitializingSnapshots;
            SceneId = sceneId;
            CommittedSnapshotRevision = 0;
            PendingSnapshotRevision = snapshotRevision;
            LastStateFrameSequence = 0;
            return CreateResult(BattleSyncSessionTransitionStatus.Applied,
                previousPhase, "Map transfer snapshot batch started.");
        }

        public BattleSyncSessionTransitionResult BeginRepair(
            long connectionEpoch)
        {
            BattleSyncSessionPhase previousPhase = Phase;
            BattleSyncSessionTransitionResult validation =
                ValidateConnection(connectionEpoch, previousPhase);

            if (validation != null)
            {
                return validation;
            }

            if (Phase == BattleSyncSessionPhase.Repairing)
            {
                return CreateResult(
                    BattleSyncSessionTransitionStatus.AlreadyInState,
                    previousPhase,
                    "Snapshot repair is already active.");
            }

            if (Phase != BattleSyncSessionPhase.Ready)
            {
                return CreateResult(
                    BattleSyncSessionTransitionStatus.InvalidPhase,
                    previousPhase,
                    "Snapshot repair can only begin from the ready phase.");
            }

            Phase = BattleSyncSessionPhase.Repairing;
            PendingSnapshotRevision = 0;

            return CreateResult(
                BattleSyncSessionTransitionStatus.Applied,
                previousPhase,
                "Snapshot repair started.");
        }

        public BattleSyncSessionTransitionResult AdoptPendingSnapshotRevision(
            long connectionEpoch,
            string sceneId,
            long snapshotRevision)
        {
            BattleSyncSessionPhase previousPhase = Phase;

            if (connectionEpoch <= 0
                || string.IsNullOrWhiteSpace(sceneId)
                || snapshotRevision <= 0)
            {
                return CreateResult(
                    BattleSyncSessionTransitionStatus.InvalidArguments,
                    previousPhase,
                    "Connection epoch, scene and snapshot revision must be valid.");
            }

            BattleSyncSessionTransitionResult validation =
                ValidateConnectionAndScene(
                    connectionEpoch,
                    sceneId,
                    previousPhase);

            if (validation != null)
            {
                return validation;
            }

            if (Phase != BattleSyncSessionPhase.InitializingSnapshots
                && Phase != BattleSyncSessionPhase.Repairing)
            {
                return CreateResult(
                    BattleSyncSessionTransitionStatus.InvalidPhase,
                    previousPhase,
                    "Pending revisions can only be adopted while initializing or repairing.");
            }

            if (snapshotRevision <= CommittedSnapshotRevision)
            {
                return CreateResult(
                    BattleSyncSessionTransitionStatus.SnapshotRevisionMismatch,
                    previousPhase,
                    "Pending snapshot revision must be newer than the committed revision.");
            }

            if (snapshotRevision < PendingSnapshotRevision)
            {
                return CreateResult(
                    BattleSyncSessionTransitionStatus.SnapshotRevisionMismatch,
                    previousPhase,
                    "Pending snapshot revision cannot move backwards.");
            }

            if (snapshotRevision == PendingSnapshotRevision)
            {
                return CreateResult(
                    BattleSyncSessionTransitionStatus.AlreadyInState,
                    previousPhase,
                    "Pending snapshot revision is already current.");
            }

            PendingSnapshotRevision = snapshotRevision;

            return CreateResult(
                BattleSyncSessionTransitionStatus.Applied,
                previousPhase,
                "Pending snapshot revision adopted.");
        }

        public BattleSyncSessionTransitionResult CompleteSnapshotBatch(
            long connectionEpoch,
            string sceneId,
            long snapshotRevision)
        {
            BattleSyncSessionPhase previousPhase = Phase;

            if (connectionEpoch <= 0
                || string.IsNullOrWhiteSpace(sceneId)
                || snapshotRevision <= 0)
            {
                return CreateResult(
                    BattleSyncSessionTransitionStatus.InvalidArguments,
                    previousPhase,
                    "Connection epoch, scene and snapshot revision must be valid.");
            }

            BattleSyncSessionTransitionResult validation =
                ValidateConnectionAndScene(
                    connectionEpoch,
                    sceneId,
                    previousPhase);

            if (validation != null)
            {
                return validation;
            }

            if (Phase != BattleSyncSessionPhase.InitializingSnapshots
                && Phase != BattleSyncSessionPhase.Repairing)
            {
                return CreateResult(
                    BattleSyncSessionTransitionStatus.InvalidPhase,
                    previousPhase,
                    "Snapshot batches can only complete while initializing or repairing.");
            }

            if (PendingSnapshotRevision <= 0
                || snapshotRevision != PendingSnapshotRevision
                || snapshotRevision < CommittedSnapshotRevision)
            {
                return CreateResult(
                    BattleSyncSessionTransitionStatus.SnapshotRevisionMismatch,
                    previousPhase,
                    "Completed snapshot revision does not match the pending revision.");
            }

            CommittedSnapshotRevision = snapshotRevision;
            PendingSnapshotRevision = 0;
            Phase = BattleSyncSessionPhase.Ready;

            return CreateResult(
                BattleSyncSessionTransitionStatus.Applied,
                previousPhase,
                "Snapshot batch committed.");
        }

        public BattleSyncSessionTransitionResult TryAdvanceStateFrameSequence(
            long connectionEpoch,
            string sceneId,
            long frameSequence)
        {
            BattleSyncSessionPhase previousPhase = Phase;

            if (connectionEpoch <= 0
                || string.IsNullOrWhiteSpace(sceneId)
                || frameSequence <= 0)
            {
                return CreateResult(
                    BattleSyncSessionTransitionStatus.InvalidArguments,
                    previousPhase,
                    "Connection epoch, scene and frame sequence must be valid.");
            }

            BattleSyncSessionTransitionResult validation =
                ValidateConnectionAndScene(
                    connectionEpoch,
                    sceneId,
                    previousPhase);

            if (validation != null)
            {
                return validation;
            }

            if (Phase != BattleSyncSessionPhase.Ready)
            {
                return CreateResult(
                    BattleSyncSessionTransitionStatus.InvalidPhase,
                    previousPhase,
                    "State frames can only advance while the session is ready.");
            }

            if (frameSequence <= LastStateFrameSequence)
            {
                return CreateResult(
                    BattleSyncSessionTransitionStatus.StaleFrameSequence,
                    previousPhase,
                    "State frame sequence is stale or duplicated.");
            }

            LastStateFrameSequence = frameSequence;

            return CreateResult(
                BattleSyncSessionTransitionStatus.Applied,
                previousPhase,
                "State frame sequence advanced.");
        }

        public BattleSyncSessionTransitionResult EndConnection(
            long connectionEpoch)
        {
            BattleSyncSessionPhase previousPhase = Phase;

            if (!HasActiveConnection)
            {
                return CreateResult(
                    BattleSyncSessionTransitionStatus.AlreadyInState,
                    previousPhase,
                    "Connection is already disconnected.");
            }

            if (connectionEpoch <= 0)
            {
                return CreateResult(
                    BattleSyncSessionTransitionStatus.InvalidArguments,
                    previousPhase,
                    "Connection epoch must be greater than zero.");
            }

            if (connectionEpoch != ConnectionEpoch)
            {
                return CreateResult(
                    BattleSyncSessionTransitionStatus.SessionMismatch,
                    previousPhase,
                    "Disconnect epoch does not match the active connection.");
            }

            ResetState();

            return CreateResult(
                BattleSyncSessionTransitionStatus.Applied,
                previousPhase,
                "Connection session ended.");
        }

        private BattleSyncSessionTransitionResult ValidateConnection(
            long connectionEpoch,
            BattleSyncSessionPhase previousPhase)
        {
            if (connectionEpoch <= 0)
            {
                return CreateResult(
                    BattleSyncSessionTransitionStatus.InvalidArguments,
                    previousPhase,
                    "Connection epoch must be greater than zero.");
            }

            if (!HasActiveConnection)
            {
                return CreateResult(
                    BattleSyncSessionTransitionStatus.NoActiveSession,
                    previousPhase,
                    "There is no active connection session.");
            }

            if (connectionEpoch != ConnectionEpoch)
            {
                return CreateResult(
                    BattleSyncSessionTransitionStatus.SessionMismatch,
                    previousPhase,
                    "Connection epoch does not match the active session.");
            }

            return null;
        }

        private BattleSyncSessionTransitionResult ValidateConnectionAndScene(
            long connectionEpoch,
            string sceneId,
            BattleSyncSessionPhase previousPhase)
        {
            BattleSyncSessionTransitionResult validation =
                ValidateConnection(connectionEpoch, previousPhase);

            if (validation != null)
            {
                return validation;
            }

            if (!string.Equals(SceneId, sceneId, StringComparison.Ordinal))
            {
                return CreateResult(
                    BattleSyncSessionTransitionStatus.SceneMismatch,
                    previousPhase,
                    "Scene does not match the active session.");
            }

            return null;
        }

        private BattleSyncSessionTransitionResult CreateResult(
            BattleSyncSessionTransitionStatus status,
            BattleSyncSessionPhase previousPhase,
            string detail)
        {
            return new BattleSyncSessionTransitionResult(
                status,
                previousPhase,
                Phase,
                ConnectionEpoch,
                detail);
        }

        private void ResetState()
        {
            Phase = BattleSyncSessionPhase.Disconnected;
            ConnectionEpoch = 0;
            SceneId = string.Empty;
            CharacterDbId = 0;
            CommittedSnapshotRevision = 0;
            PendingSnapshotRevision = 0;
            LastStateFrameSequence = 0;
        }
    }
}
