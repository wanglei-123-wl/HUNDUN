using System;
using System.Collections.Generic;
using System.Globalization;

namespace Game.Client.BattleSync
{
    public enum BattleEntityKind
    {
        Unknown = 0,
        Player = 1,
        Monster = 2,
        Loot = 3
    }

    public readonly struct BattleEntityKey : IEquatable<BattleEntityKey>
    {
        public BattleEntityKind Kind { get; }
        public string EntityId { get; }

        public BattleEntityKey(BattleEntityKind kind, string entityId)
        {
            Kind = kind;
            EntityId = entityId ?? string.Empty;
        }

        public bool IsValid =>
            (Kind == BattleEntityKind.Player
                || Kind == BattleEntityKind.Monster
                || Kind == BattleEntityKind.Loot)
            && !string.IsNullOrWhiteSpace(EntityId);

        public bool Equals(BattleEntityKey other)
        {
            return Kind == other.Kind
                && string.Equals(EntityId, other.EntityId, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is BattleEntityKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((int)Kind * 397)
                    ^ StringComparer.Ordinal.GetHashCode(EntityId ?? string.Empty);
            }
        }

        public override string ToString()
        {
            return Kind + ":" + EntityId;
        }
    }

    public sealed class BattleEntityRegistryEntry
    {
        public BattleEntityKind Kind { get; }
        public string EntityId { get; }
        public int NetId { get; internal set; }
        public long ConnectionEpoch { get; internal set; }
        public string SceneId { get; internal set; }
        public long VisibilityRevision { get; internal set; }
        public long LastStateServerTick { get; internal set; }
        public long SpawnGeneration { get; internal set; }

        internal BattleEntityRegistryEntry(
            BattleEntityKind kind,
            string entityId,
            int netId,
            long connectionEpoch,
            string sceneId,
            long visibilityRevision,
            long lastStateServerTick,
            long spawnGeneration)
        {
            Kind = kind;
            EntityId = entityId ?? string.Empty;
            NetId = netId;
            ConnectionEpoch = connectionEpoch;
            SceneId = sceneId ?? string.Empty;
            VisibilityRevision = visibilityRevision;
            LastStateServerTick = lastStateServerTick;
            SpawnGeneration = spawnGeneration;
        }
    }

    public readonly struct BattleEntityRegistration
    {
        public long ConnectionEpoch { get; }
        public string SceneId { get; }
        public BattleEntityKind Kind { get; }
        public string EntityId { get; }
        public int NetId { get; }
        public long VisibilityRevision { get; }
        public long ServerTick { get; }
        public long SpawnGeneration { get; }

        public BattleEntityRegistration(
            long connectionEpoch,
            string sceneId,
            BattleEntityKind kind,
            string entityId,
            int netId,
            long visibilityRevision,
            long serverTick,
            long spawnGeneration)
        {
            ConnectionEpoch = connectionEpoch;
            SceneId = sceneId ?? string.Empty;
            Kind = kind;
            EntityId = entityId ?? string.Empty;
            NetId = netId;
            VisibilityRevision = visibilityRevision;
            ServerTick = serverTick;
            SpawnGeneration = spawnGeneration;
        }
    }

    public readonly struct BattleEntityRemoval
    {
        public long ConnectionEpoch { get; }
        public string SceneId { get; }
        public BattleEntityKind Kind { get; }
        public string EntityId { get; }
        public int NetId { get; }
        public long VisibilityRevision { get; }
        public long ServerTick { get; }
        public long SpawnGeneration { get; }
        public string Reason { get; }

        public BattleEntityRemoval(
            long connectionEpoch,
            string sceneId,
            BattleEntityKind kind,
            string entityId,
            int netId,
            long visibilityRevision,
            long serverTick,
            long spawnGeneration,
            string reason)
        {
            ConnectionEpoch = connectionEpoch;
            SceneId = sceneId ?? string.Empty;
            Kind = kind;
            EntityId = entityId ?? string.Empty;
            NetId = netId;
            VisibilityRevision = visibilityRevision;
            ServerTick = serverTick;
            SpawnGeneration = spawnGeneration;
            Reason = reason ?? string.Empty;
        }
    }

    public readonly struct BattleEntityStateObservation
    {
        public long ConnectionEpoch { get; }
        public string SceneId { get; }
        public BattleEntityKind ExpectedKind { get; }
        public int NetId { get; }
        public long VisibilityRevision { get; }
        public long ServerTick { get; }

        public BattleEntityStateObservation(
            long connectionEpoch,
            string sceneId,
            BattleEntityKind expectedKind,
            int netId,
            long visibilityRevision,
            long serverTick)
        {
            ConnectionEpoch = connectionEpoch;
            SceneId = sceneId ?? string.Empty;
            ExpectedKind = expectedKind;
            NetId = netId;
            VisibilityRevision = visibilityRevision;
            ServerTick = serverTick;
        }
    }

    public enum BattleEntityRegistryStatus
    {
        Invalid = 0,
        Registered = 1,
        Updated = 2,
        Rebound = 3,
        AlreadyRegistered = 4,
        Removed = 5,
        AlreadyRemoved = 6,
        TombstoneCreated = 7,
        StateAccepted = 8,
        StaleVisibilityRevision = 9,
        StaleServerTick = 10,
        StaleSpawnGeneration = 11,
        VisibilityRevisionGap = 12,
        UnknownNetId = 13,
        UnknownEntity = 14,
        NetIdConflict = 15,
        EntityBindingConflict = 16,
        EntityKindMismatch = 17,
        TombstoneRejected = 18,
        SessionMismatch = 19,
        SceneMismatch = 20,
        RegistryNotActive = 21,
        GenerationMismatch = 22
    }

    public sealed class BattleEntityRegistryResult
    {
        public BattleEntityRegistryStatus Status { get; }
        public BattleEntityRegistryEntry Entry { get; }
        public bool RequiresSnapshotRepair { get; }
        public string RepairReason { get; }
        public string RepairEntityType { get; }
        public string RepairEntityId { get; }
        public string Detail { get; }

        public bool IsSuccess =>
            Status == BattleEntityRegistryStatus.Registered
            || Status == BattleEntityRegistryStatus.Updated
            || Status == BattleEntityRegistryStatus.Rebound
            || Status == BattleEntityRegistryStatus.AlreadyRegistered
            || Status == BattleEntityRegistryStatus.Removed
            || Status == BattleEntityRegistryStatus.AlreadyRemoved
            || Status == BattleEntityRegistryStatus.TombstoneCreated
            || Status == BattleEntityRegistryStatus.StateAccepted;

        public BattleEntityRegistryResult(
            BattleEntityRegistryStatus status,
            BattleEntityRegistryEntry entry,
            bool requiresSnapshotRepair,
            string repairReason,
            string repairEntityType,
            string repairEntityId,
            string detail)
        {
            Status = status;
            Entry = entry;
            RequiresSnapshotRepair = requiresSnapshotRepair;
            RepairReason = repairReason ?? string.Empty;
            RepairEntityType = repairEntityType ?? string.Empty;
            RepairEntityId = repairEntityId ?? string.Empty;
            Detail = detail ?? string.Empty;
        }
    }

    public sealed class BattleEntityTombstone
    {
        public BattleEntityKind Kind { get; }
        public string EntityId { get; }
        public int NetId { get; }
        public long ConnectionEpoch { get; }
        public string SceneId { get; }
        public long VisibilityRevision { get; }
        public long ServerTick { get; }
        public long SpawnGeneration { get; }
        public string Reason { get; }
        public long CreatedAtMs { get; }

        internal BattleEntityTombstone(
            BattleEntityKind kind,
            string entityId,
            int netId,
            long connectionEpoch,
            string sceneId,
            long visibilityRevision,
            long serverTick,
            long spawnGeneration,
            string reason,
            long createdAtMs)
        {
            Kind = kind;
            EntityId = entityId ?? string.Empty;
            NetId = netId;
            ConnectionEpoch = connectionEpoch;
            SceneId = sceneId ?? string.Empty;
            VisibilityRevision = visibilityRevision;
            ServerTick = serverTick;
            SpawnGeneration = spawnGeneration;
            Reason = reason ?? string.Empty;
            CreatedAtMs = createdAtMs;
        }
    }

    public sealed class BattleEntitySnapshotPlan
    {
        public long ConnectionEpoch { get; }
        public string SceneId { get; }
        public BattleEntityKind Kind { get; }
        public long SnapshotRevision { get; }
        public IReadOnlyList<BattleEntityRegistration> Registrations { get; }
        public bool IsValid { get; internal set; }

        internal BattleEntitySnapshotPlan(long connectionEpoch, string sceneId,
            BattleEntityKind kind, long snapshotRevision,
            IReadOnlyList<BattleEntityRegistration> registrations)
        {
            ConnectionEpoch=connectionEpoch; SceneId=sceneId??string.Empty; Kind=kind;
            SnapshotRevision=snapshotRevision; Registrations=registrations;
            IsValid=true;
        }
    }

    public sealed class BattleEntityRegistry
    {
        private const int DefaultTombstoneCapacity = 4096;
        private const long DefaultTombstoneLifetimeMs = 60000;

        private readonly Dictionary<int, BattleEntityRegistryEntry> entriesByNetId =
            new Dictionary<int, BattleEntityRegistryEntry>();
        private readonly Dictionary<BattleEntityKey, BattleEntityRegistryEntry> entriesByEntity =
            new Dictionary<BattleEntityKey, BattleEntityRegistryEntry>();
        private readonly Dictionary<int, BattleEntityTombstone> tombstonesByNetId =
            new Dictionary<int, BattleEntityTombstone>();
        private readonly Dictionary<BattleEntityKey, BattleEntityTombstone> tombstonesByEntity =
            new Dictionary<BattleEntityKey, BattleEntityTombstone>();

        private readonly int tombstoneCapacity;
        private readonly long tombstoneLifetimeMs;
        private long activeConnectionEpoch;
        private string activeSceneId = string.Empty;
        private bool isActive;

        public bool IsActive => isActive;
        public long ActiveConnectionEpoch => activeConnectionEpoch;
        public string ActiveSceneId => activeSceneId;
        public int ActiveEntityCount => entriesByNetId.Count;
        public int TombstoneCount => tombstonesByEntity.Count;

        public BattleEntityRegistry(
            int tombstoneCapacity = DefaultTombstoneCapacity,
            long tombstoneLifetimeMs = DefaultTombstoneLifetimeMs)
        {
            if (tombstoneCapacity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(tombstoneCapacity));
            }

            if (tombstoneLifetimeMs < 1000)
            {
                throw new ArgumentOutOfRangeException(nameof(tombstoneLifetimeMs));
            }

            this.tombstoneCapacity = tombstoneCapacity;
            this.tombstoneLifetimeMs = tombstoneLifetimeMs;
        }

        public void BeginSession(long connectionEpoch, string sceneId)
        {
            if (connectionEpoch <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(connectionEpoch));
            }

            if (string.IsNullOrWhiteSpace(sceneId))
            {
                throw new ArgumentException("Scene id is required.", nameof(sceneId));
            }

            ClearAllInternal();
            activeConnectionEpoch = connectionEpoch;
            activeSceneId = sceneId;
            isActive = true;
        }

        public bool EndSession(long connectionEpoch)
        {
            if (!isActive || connectionEpoch != activeConnectionEpoch)
            {
                return false;
            }

            ClearAllInternal();
            activeConnectionEpoch = 0;
            activeSceneId = string.Empty;
            isActive = false;
            return true;
        }

        public BattleEntityRegistryResult TryPrepareSnapshotReplacement(
            long connectionEpoch, string sceneId, BattleEntityKind kind,
            long snapshotRevision, IReadOnlyList<BattleEntityRegistration> registrations,
            out BattleEntitySnapshotPlan plan)
        {
            plan=null;
            if(!isActive) return CreateResult(BattleEntityRegistryStatus.RegistryNotActive,null);
            if(connectionEpoch!=activeConnectionEpoch) return CreateResult(BattleEntityRegistryStatus.SessionMismatch,null);
            if(!string.Equals(sceneId,activeSceneId,StringComparison.Ordinal)) return CreateResult(BattleEntityRegistryStatus.SceneMismatch,null);
            if(snapshotRevision<=0 || registrations==null || kind==BattleEntityKind.Unknown) return CreateResult(BattleEntityRegistryStatus.Invalid,null);
            HashSet<int> netIds=new HashSet<int>(); HashSet<BattleEntityKey> keys=new HashSet<BattleEntityKey>();
            BattleEntityRegistration[] copy=new BattleEntityRegistration[registrations.Count];
            for(int i=0;i<registrations.Count;i++)
            {
                BattleEntityRegistration value=registrations[i]; copy[i]=value;
                BattleEntityKey key=new BattleEntityKey(value.Kind,value.EntityId);
                if(value.ConnectionEpoch!=connectionEpoch || value.Kind!=kind || value.NetId<=0
                    || !key.IsValid || value.VisibilityRevision<0 || value.ServerTick<=0
                    || (kind==BattleEntityKind.Monster
                        && (value.SpawnGeneration<0 || value.VisibilityRevision<=0))
                    || !string.Equals(value.SceneId,sceneId,StringComparison.Ordinal)
                    || !netIds.Add(value.NetId) || !keys.Add(key))
                    return CreateResult(BattleEntityRegistryStatus.Invalid,null);
                if(entriesByNetId.TryGetValue(value.NetId,out BattleEntityRegistryEntry existing)
                    && existing.Kind!=kind) return CreateResult(BattleEntityRegistryStatus.NetIdConflict,existing);
                if(kind==BattleEntityKind.Monster)
                {
                    if(entriesByEntity.TryGetValue(key,out BattleEntityRegistryEntry currentMonster))
                    {
                        if(value.SpawnGeneration<currentMonster.SpawnGeneration)
                            return CreateResult(BattleEntityRegistryStatus.StaleSpawnGeneration,currentMonster);
                        if(value.VisibilityRevision<currentMonster.VisibilityRevision)
                            return CreateResult(BattleEntityRegistryStatus.StaleVisibilityRevision,currentMonster);
                        if(value.SpawnGeneration==currentMonster.SpawnGeneration
                            && value.VisibilityRevision==currentMonster.VisibilityRevision
                            && value.NetId!=currentMonster.NetId)
                            return CreateRepairResult(BattleEntityRegistryStatus.EntityBindingConflict,
                                currentMonster,BattleSyncProtocolConstants.RepairReasons.SnapshotMismatch,
                                kind,value.NetId,"snapshot changes netId without a newer visibility revision");
                    }
                    else
                    {
                        BattleEntityRegistryResult rejected=ValidateRegistrationAgainstTombstone(key,value);
                        if(rejected!=null)return rejected;
                    }
                }
            }
            plan=new BattleEntitySnapshotPlan(connectionEpoch,sceneId,kind,snapshotRevision,copy);
            return CreateResult(BattleEntityRegistryStatus.StateAccepted,null);
        }

        public BattleEntityRegistryResult CommitSnapshotReplacement(BattleEntitySnapshotPlan plan,long nowMs)
        {
            if(plan==null || !plan.IsValid || nowMs<0) return CreateResult(BattleEntityRegistryStatus.Invalid,null);
            if(!isActive || plan.ConnectionEpoch!=activeConnectionEpoch) return CreateResult(BattleEntityRegistryStatus.SessionMismatch,null);
            HashSet<BattleEntityKey> keep=new HashSet<BattleEntityKey>();
            for(int i=0;i<plan.Registrations.Count;i++) keep.Add(new BattleEntityKey(plan.Kind,plan.Registrations[i].EntityId));
            List<BattleEntityRegistryEntry> remove=new List<BattleEntityRegistryEntry>();
            foreach(BattleEntityRegistryEntry entry in entriesByEntity.Values)
                if(entry.Kind==plan.Kind && !keep.Contains(new BattleEntityKey(entry.Kind,entry.EntityId))) remove.Add(entry);
            for(int i=0;i<remove.Count;i++)
            {
                BattleEntityRegistryEntry entry=remove[i];
                Remove(new BattleEntityRemoval(plan.ConnectionEpoch,plan.SceneId,entry.Kind,entry.EntityId,
                    entry.NetId,entry.VisibilityRevision+1,Math.Max(1,entry.LastStateServerTick),entry.SpawnGeneration,
                    "snapshot_replacement"),nowMs);
            }
            BattleEntityRegistryResult result=CreateResult(BattleEntityRegistryStatus.StateAccepted,null);
            for(int i=0;i<plan.Registrations.Count;i++)
            {
                result=RegisterOrUpdate(plan.Registrations[i],nowMs);
                if(!result.IsSuccess) return result;
            }
            plan.IsValid=false;
            return result;
        }

        public BattleEntityRegistryResult RegisterOrUpdate(
            BattleEntityRegistration registration,
            long nowMs)
        {
            BattleEntityRegistryResult validation = ValidateRegistration(registration, nowMs);
            if (validation != null)
            {
                return validation;
            }

            PruneTombstones(nowMs);

            BattleEntityKey key = new BattleEntityKey(registration.Kind, registration.EntityId);
            bool hasNetEntry = entriesByNetId.TryGetValue(
                registration.NetId,
                out BattleEntityRegistryEntry netEntry);
            bool hasEntityEntry = entriesByEntity.TryGetValue(
                key,
                out BattleEntityRegistryEntry entry);

            if (hasNetEntry
                && !new BattleEntityKey(netEntry.Kind, netEntry.EntityId).Equals(key))
            {
                return CreateRepairResult(
                    BattleEntityRegistryStatus.NetIdConflict,
                    netEntry,
                    BattleSyncProtocolConstants.RepairReasons.SnapshotMismatch,
                    registration.Kind,
                    registration.NetId,
                    "netId " + FormatNumber(registration.NetId)
                        + " is already bound to " + netEntry.Kind + ":" + netEntry.EntityId
                        + ", incoming entity is " + key);
            }

            if (hasNetEntry && (!hasEntityEntry || !ReferenceEquals(netEntry, entry)))
            {
                return CreateRepairResult(
                    BattleEntityRegistryStatus.EntityBindingConflict,
                    netEntry,
                    BattleSyncProtocolConstants.RepairReasons.SnapshotMismatch,
                    registration.Kind,
                    registration.NetId,
                    "active netId and entity indexes do not reference the same entry");
            }

            if (hasEntityEntry)
            {
                if (!entriesByNetId.TryGetValue(
                        entry.NetId,
                        out BattleEntityRegistryEntry boundEntry)
                    || !ReferenceEquals(entry, boundEntry))
                {
                    return CreateRepairResult(
                        BattleEntityRegistryStatus.EntityBindingConflict,
                        entry,
                        BattleSyncProtocolConstants.RepairReasons.SnapshotMismatch,
                        registration.Kind,
                        registration.NetId,
                        "active entity and netId indexes do not reference the same entry");
                }

                return UpdateExistingEntry(entry, registration, nowMs);
            }

            BattleEntityRegistryResult tombstoneResult =
                ValidateRegistrationAgainstTombstone(key, registration);
            if (tombstoneResult != null)
            {
                return tombstoneResult;
            }

            entry = new BattleEntityRegistryEntry(
                registration.Kind,
                registration.EntityId,
                registration.NetId,
                registration.ConnectionEpoch,
                registration.SceneId,
                registration.VisibilityRevision,
                registration.ServerTick,
                registration.SpawnGeneration);

            entriesByNetId.Add(registration.NetId, entry);
            entriesByEntity.Add(key, entry);
            RemoveEntityTombstone(key);
            tombstonesByNetId.Remove(registration.NetId);

            return CreateResult(BattleEntityRegistryStatus.Registered, entry);
        }

        public BattleEntityRegistryResult Remove(BattleEntityRemoval removal, long nowMs)
        {
            BattleEntityRegistryResult validation = ValidateRemoval(removal, nowMs);
            if (validation != null)
            {
                return validation;
            }

            PruneTombstones(nowMs);

            BattleEntityKey key = new BattleEntityKey(removal.Kind, removal.EntityId);
            bool hasNetEntry = entriesByNetId.TryGetValue(
                removal.NetId,
                out BattleEntityRegistryEntry netEntry);
            bool hasEntityEntry = entriesByEntity.TryGetValue(
                key,
                out BattleEntityRegistryEntry entry);

            if (hasNetEntry
                && !new BattleEntityKey(netEntry.Kind, netEntry.EntityId).Equals(key))
            {
                return CreateRepairResult(
                    BattleEntityRegistryStatus.EntityBindingConflict,
                    netEntry,
                    BattleSyncProtocolConstants.RepairReasons.SnapshotMismatch,
                    removal.Kind,
                    removal.NetId,
                    "remove binding conflicts with active entity "
                        + netEntry.Kind + ":" + netEntry.EntityId);
            }

            if (hasNetEntry && (!hasEntityEntry || !ReferenceEquals(netEntry, entry)))
            {
                return CreateRepairResult(
                    BattleEntityRegistryStatus.EntityBindingConflict,
                    netEntry,
                    BattleSyncProtocolConstants.RepairReasons.SnapshotMismatch,
                    removal.Kind,
                    removal.NetId,
                    "active netId and entity indexes do not reference the same entry");
            }

            if (hasEntityEntry)
            {
                if (!entriesByNetId.TryGetValue(
                        entry.NetId,
                        out BattleEntityRegistryEntry boundEntry)
                    || !ReferenceEquals(entry, boundEntry))
                {
                    return CreateRepairResult(
                        BattleEntityRegistryStatus.EntityBindingConflict,
                        entry,
                        BattleSyncProtocolConstants.RepairReasons.SnapshotMismatch,
                        removal.Kind,
                        removal.NetId,
                        "active entity and netId indexes do not reference the same entry");
                }

                if (removal.Kind == BattleEntityKind.Monster
                    && removal.SpawnGeneration < entry.SpawnGeneration)
                {
                    return CreateResult(BattleEntityRegistryStatus.StaleSpawnGeneration, entry);
                }

                if (removal.Kind == BattleEntityKind.Monster
                    && removal.SpawnGeneration > entry.SpawnGeneration)
                {
                    return CreateRepairResult(
                        BattleEntityRegistryStatus.GenerationMismatch,
                        entry,
                        BattleSyncProtocolConstants.RepairReasons.GenerationMismatch,
                        removal.Kind,
                        removal.NetId,
                        "remove references a newer monster generation than the active entry");
                }

                if (entry.NetId != removal.NetId)
                {
                    return CreateRepairResult(
                        BattleEntityRegistryStatus.EntityBindingConflict,
                        entry,
                        BattleSyncProtocolConstants.RepairReasons.SnapshotMismatch,
                        removal.Kind,
                        removal.NetId,
                        "remove netId does not match active binding");
                }

                if (removal.VisibilityRevision < entry.VisibilityRevision)
                {
                    return CreateResult(BattleEntityRegistryStatus.StaleVisibilityRevision, entry);
                }

                entriesByNetId.Remove(entry.NetId);
                entriesByEntity.Remove(key);
                BattleEntityTombstone tombstone = CreateTombstone(removal, nowMs);
                StoreEntityTombstone(key, tombstone);
                EnforceTombstoneCapacity();
                return CreateResult(BattleEntityRegistryStatus.Removed, entry);
            }

            if (TryGetRemovalTombstone(key, removal.NetId, out BattleEntityTombstone existingTombstone))
            {
                if (removal.Kind == BattleEntityKind.Monster
                    && removal.SpawnGeneration < existingTombstone.SpawnGeneration)
                {
                    return CreateResult(BattleEntityRegistryStatus.StaleSpawnGeneration, null);
                }

                bool covered = removal.Kind == BattleEntityKind.Monster
                    ? existingTombstone.SpawnGeneration > removal.SpawnGeneration
                        || existingTombstone.SpawnGeneration == removal.SpawnGeneration
                        && existingTombstone.VisibilityRevision >= removal.VisibilityRevision
                    : existingTombstone.VisibilityRevision >= removal.VisibilityRevision;

                if (covered)
                {
                    return CreateResult(BattleEntityRegistryStatus.AlreadyRemoved, null);
                }
            }

            BattleEntityTombstone createdTombstone = CreateTombstone(removal, nowMs);
            StoreEntityTombstone(key, createdTombstone);
            EnforceTombstoneCapacity();
            return CreateResult(BattleEntityRegistryStatus.TombstoneCreated, null);
        }

        public BattleEntityRegistryResult ObserveState(BattleEntityStateObservation observation,
            bool advanceTick = true)
        {
            BattleEntityRegistryResult validation = ValidateObservation(observation);
            if (validation != null)
            {
                return validation;
            }

            if (!entriesByNetId.TryGetValue(observation.NetId, out BattleEntityRegistryEntry entry))
            {
                if (tombstonesByNetId.TryGetValue(
                        observation.NetId,
                        out BattleEntityTombstone tombstone)
                    && observation.VisibilityRevision <= tombstone.VisibilityRevision)
                {
                    return CreateResult(BattleEntityRegistryStatus.TombstoneRejected, null);
                }

                return CreateRepairResult(
                    BattleEntityRegistryStatus.UnknownNetId,
                    null,
                    BattleSyncProtocolConstants.RepairReasons.UnknownEntityState,
                    observation.ExpectedKind,
                    observation.NetId,
                    "state references an unknown netId");
            }

            if (entry.Kind != observation.ExpectedKind)
            {
                return CreateRepairResult(
                    BattleEntityRegistryStatus.EntityKindMismatch,
                    entry,
                    BattleSyncProtocolConstants.RepairReasons.SnapshotMismatch,
                    observation.ExpectedKind,
                    observation.NetId,
                    "state kind does not match registered entity kind");
            }

            if (observation.VisibilityRevision < entry.VisibilityRevision)
            {
                return CreateResult(BattleEntityRegistryStatus.StaleVisibilityRevision, entry);
            }

            if (observation.VisibilityRevision > entry.VisibilityRevision)
            {
                return CreateRepairResult(
                    BattleEntityRegistryStatus.VisibilityRevisionGap,
                    entry,
                    BattleSyncProtocolConstants.RepairReasons.RevisionGap,
                    observation.ExpectedKind,
                    observation.NetId,
                    "state visibility revision is newer than the registered structure version");
            }

            if (observation.ServerTick <= entry.LastStateServerTick)
            {
                return CreateResult(BattleEntityRegistryStatus.StaleServerTick, entry);
            }

            if (advanceTick) entry.LastStateServerTick = observation.ServerTick;
            return CreateResult(BattleEntityRegistryStatus.StateAccepted, entry);
        }

        public bool CommitStateTick(BattleEntityStateObservation observation,
            BattleEntityRegistryEntry expectedEntry)
        {
            if (expectedEntry == null || ValidateObservation(observation) != null
                || !entriesByNetId.TryGetValue(observation.NetId, out BattleEntityRegistryEntry entry)
                || !ReferenceEquals(entry, expectedEntry)
                || entry.Kind != observation.ExpectedKind
                || entry.VisibilityRevision != observation.VisibilityRevision
                || observation.ServerTick <= entry.LastStateServerTick)
                return false;
            entry.LastStateServerTick = observation.ServerTick;
            return true;
        }

        public bool TryGetByNetId(int netId, out BattleEntityRegistryEntry entry)
        {
            if (!isActive || netId <= 0)
            {
                entry = null;
                return false;
            }

            return entriesByNetId.TryGetValue(netId, out entry);
        }

        public bool TryGetByEntity(
            BattleEntityKind kind,
            string entityId,
            out BattleEntityRegistryEntry entry)
        {
            BattleEntityKey key = new BattleEntityKey(kind, entityId);
            if (!isActive || !key.IsValid)
            {
                entry = null;
                return false;
            }

            return entriesByEntity.TryGetValue(key, out entry);
        }

        public bool TryGetTombstoneByNetId(
            int netId,
            out BattleEntityTombstone tombstone)
        {
            if (!isActive || netId <= 0)
            {
                tombstone = null;
                return false;
            }

            return tombstonesByNetId.TryGetValue(netId, out tombstone);
        }

        public bool TryGetTombstoneByEntity(
            BattleEntityKind kind,
            string entityId,
            out BattleEntityTombstone tombstone)
        {
            BattleEntityKey key = new BattleEntityKey(kind, entityId);
            if (!isActive || !key.IsValid)
            {
                tombstone = null;
                return false;
            }

            return tombstonesByEntity.TryGetValue(key, out tombstone);
        }

        public void PruneTombstones(long nowMs)
        {
            if (nowMs < 0
                || tombstonesByNetId.Count == 0 && tombstonesByEntity.Count == 0)
            {
                return;
            }

            List<BattleEntityTombstone> tombstones = GetUniqueTombstones();
            for (int index = 0; index < tombstones.Count; index++)
            {
                BattleEntityTombstone tombstone = tombstones[index];
                if (nowMs >= tombstone.CreatedAtMs
                    && nowMs - tombstone.CreatedAtMs >= tombstoneLifetimeMs)
                {
                    RemoveTombstone(tombstone);
                }
            }

            EnforceTombstoneCapacity();
        }

        public bool ValidateInternalConsistency(out string error)
        {
            if (isActive
                && (activeConnectionEpoch <= 0
                    || string.IsNullOrWhiteSpace(activeSceneId)))
            {
                error = "Active registry has invalid session identity.";
                return false;
            }

            if (!isActive
                && (activeConnectionEpoch != 0
                    || !string.IsNullOrEmpty(activeSceneId)))
            {
                error = "Inactive registry retains session identity.";
                return false;
            }

            if (entriesByNetId.Count != entriesByEntity.Count)
            {
                error = "Active mapping counts differ.";
                return false;
            }

            if (!isActive
                && (entriesByNetId.Count != 0
                    || entriesByEntity.Count != 0
                    || tombstonesByNetId.Count != 0
                    || tombstonesByEntity.Count != 0))
            {
                error = "Inactive registry contains state.";
                return false;
            }

            foreach (KeyValuePair<int, BattleEntityRegistryEntry> pair in entriesByNetId)
            {
                BattleEntityRegistryEntry entry = pair.Value;
                BattleEntityKey key = new BattleEntityKey(entry.Kind, entry.EntityId);
                if (pair.Key != entry.NetId
                    || !entriesByEntity.TryGetValue(key, out BattleEntityRegistryEntry entityEntry)
                    || !ReferenceEquals(entry, entityEntry))
                {
                    error = "Active netId mapping is inconsistent.";
                    return false;
                }

                if (!ValidateEntry(entry, out error))
                {
                    return false;
                }

                if (tombstonesByEntity.ContainsKey(key))
                {
                    error = "Active entity also has an entity tombstone.";
                    return false;
                }
            }

            foreach (KeyValuePair<BattleEntityKey, BattleEntityRegistryEntry> pair in entriesByEntity)
            {
                BattleEntityRegistryEntry entry = pair.Value;
                if (!pair.Key.Equals(new BattleEntityKey(entry.Kind, entry.EntityId))
                    || !entriesByNetId.TryGetValue(entry.NetId, out BattleEntityRegistryEntry netEntry)
                    || !ReferenceEquals(entry, netEntry))
                {
                    error = "Active entity mapping is inconsistent.";
                    return false;
                }
            }

            foreach (KeyValuePair<BattleEntityKey, BattleEntityTombstone> pair in tombstonesByEntity)
            {
                BattleEntityTombstone tombstone = pair.Value;
                if (!pair.Key.Equals(new BattleEntityKey(tombstone.Kind, tombstone.EntityId))
                    || tombstone.ConnectionEpoch != activeConnectionEpoch
                    || !string.Equals(tombstone.SceneId, activeSceneId, StringComparison.Ordinal))
                {
                    error = "Entity tombstone is inconsistent.";
                    return false;
                }
            }

            foreach (KeyValuePair<int, BattleEntityTombstone> pair in tombstonesByNetId)
            {
                BattleEntityTombstone tombstone = pair.Value;
                if (pair.Key != tombstone.NetId
                    || tombstone.ConnectionEpoch != activeConnectionEpoch
                    || !string.Equals(tombstone.SceneId, activeSceneId, StringComparison.Ordinal))
                {
                    error = "NetId tombstone is inconsistent.";
                    return false;
                }
            }

            error = string.Empty;
            return true;
        }

        private BattleEntityRegistryResult UpdateExistingEntry(
            BattleEntityRegistryEntry entry,
            BattleEntityRegistration registration,
            long nowMs)
        {
            bool newerGeneration = false;
            if (registration.Kind == BattleEntityKind.Monster)
            {
                if (registration.SpawnGeneration < entry.SpawnGeneration)
                {
                    return CreateResult(BattleEntityRegistryStatus.StaleSpawnGeneration, entry);
                }

                newerGeneration = registration.SpawnGeneration > entry.SpawnGeneration;
            }

            if (entry.NetId != registration.NetId)
            {
                if (registration.VisibilityRevision < entry.VisibilityRevision)
                {
                    return CreateResult(BattleEntityRegistryStatus.StaleVisibilityRevision, entry);
                }

                if (!newerGeneration
                    && registration.VisibilityRevision == entry.VisibilityRevision)
                {
                    return CreateRepairResult(
                        BattleEntityRegistryStatus.EntityBindingConflict,
                        entry,
                        BattleSyncProtocolConstants.RepairReasons.SnapshotMismatch,
                        registration.Kind,
                        registration.NetId,
                        "entity requested a new netId without a newer visibility revision");
                }

                int oldNetId = entry.NetId;
                BattleEntityTombstone oldNetIdTombstone = new BattleEntityTombstone(
                    entry.Kind,
                    entry.EntityId,
                    oldNetId,
                    entry.ConnectionEpoch,
                    entry.SceneId,
                    entry.VisibilityRevision,
                    entry.LastStateServerTick,
                    entry.SpawnGeneration,
                    "rebound",
                    nowMs);

                entriesByNetId.Remove(oldNetId);
                entry.NetId = registration.NetId;
                entry.VisibilityRevision = registration.VisibilityRevision;
                entry.LastStateServerTick = newerGeneration
                    ? registration.ServerTick
                    : Math.Max(entry.LastStateServerTick, registration.ServerTick);
                entry.SpawnGeneration = registration.SpawnGeneration;
                entriesByNetId.Add(entry.NetId, entry);
                tombstonesByNetId[oldNetId] = oldNetIdTombstone;
                tombstonesByNetId.Remove(entry.NetId);
                RemoveEntityTombstone(new BattleEntityKey(entry.Kind, entry.EntityId));
                EnforceTombstoneCapacity();
                return CreateResult(BattleEntityRegistryStatus.Rebound, entry);
            }

            if (registration.VisibilityRevision < entry.VisibilityRevision)
            {
                return CreateResult(BattleEntityRegistryStatus.StaleVisibilityRevision, entry);
            }

            if (!newerGeneration
                && registration.VisibilityRevision == entry.VisibilityRevision)
            {
                entry.LastStateServerTick = Math.Max(
                    entry.LastStateServerTick,
                    registration.ServerTick);
                return CreateResult(BattleEntityRegistryStatus.AlreadyRegistered, entry);
            }

            entry.VisibilityRevision = registration.VisibilityRevision;
            entry.LastStateServerTick = newerGeneration
                ? registration.ServerTick
                : Math.Max(entry.LastStateServerTick, registration.ServerTick);
            entry.SpawnGeneration = registration.SpawnGeneration;
            RemoveEntityTombstone(new BattleEntityKey(entry.Kind, entry.EntityId));
            tombstonesByNetId.Remove(entry.NetId);
            return CreateResult(BattleEntityRegistryStatus.Updated, entry);
        }

        private BattleEntityRegistryResult ValidateRegistrationAgainstTombstone(
            BattleEntityKey key,
            BattleEntityRegistration registration)
        {
            if (!tombstonesByEntity.TryGetValue(key, out BattleEntityTombstone tombstone))
            {
                return null;
            }

            if (registration.Kind == BattleEntityKind.Monster)
            {
                if (registration.SpawnGeneration < tombstone.SpawnGeneration)
                {
                    return CreateResult(BattleEntityRegistryStatus.StaleSpawnGeneration, null);
                }

                if (registration.SpawnGeneration == tombstone.SpawnGeneration
                    && registration.VisibilityRevision <= tombstone.VisibilityRevision)
                {
                    return CreateResult(BattleEntityRegistryStatus.TombstoneRejected, null);
                }

                return null;
            }

            return registration.VisibilityRevision <= tombstone.VisibilityRevision
                ? CreateResult(BattleEntityRegistryStatus.TombstoneRejected, null)
                : null;
        }

        private BattleEntityRegistryResult ValidateRegistration(
            BattleEntityRegistration registration,
            long nowMs)
        {
            BattleEntityRegistryResult sessionResult = ValidateSession(
                registration.ConnectionEpoch,
                registration.SceneId);
            if (sessionResult != null)
            {
                return sessionResult;
            }

            if (nowMs < 0
                || !IsValidEntity(registration.Kind, registration.EntityId)
                || registration.NetId <= 0
                || registration.VisibilityRevision <= 0
                || registration.ServerTick < 0)
            {
                return CreateResult(BattleEntityRegistryStatus.Invalid, null);
            }

            if ((registration.Kind == BattleEntityKind.Player
                    || registration.Kind == BattleEntityKind.Monster)
                && registration.ServerTick <= 0)
            {
                return CreateResult(BattleEntityRegistryStatus.Invalid, null);
            }

            if (registration.Kind == BattleEntityKind.Monster)
            {
                return registration.SpawnGeneration >= 0
                    ? null
                    : CreateResult(BattleEntityRegistryStatus.Invalid, null);
            }

            return registration.SpawnGeneration == 0
                ? null
                : CreateResult(BattleEntityRegistryStatus.Invalid, null);
        }

        private BattleEntityRegistryResult ValidateRemoval(
            BattleEntityRemoval removal,
            long nowMs)
        {
            BattleEntityRegistryResult sessionResult = ValidateSession(
                removal.ConnectionEpoch,
                removal.SceneId);
            if (sessionResult != null)
            {
                return sessionResult;
            }

            if (nowMs < 0
                || !IsValidEntity(removal.Kind, removal.EntityId)
                || removal.NetId <= 0
                || removal.VisibilityRevision <= 0
                || removal.ServerTick < 0
                || (removal.Kind != BattleEntityKind.Monster
                    && string.IsNullOrWhiteSpace(removal.Reason)))
            {
                return CreateResult(BattleEntityRegistryStatus.Invalid, null);
            }

            if ((removal.Kind == BattleEntityKind.Player
                    || removal.Kind == BattleEntityKind.Monster)
                && removal.ServerTick <= 0)
            {
                return CreateResult(BattleEntityRegistryStatus.Invalid, null);
            }

            if (removal.Kind == BattleEntityKind.Monster)
            {
                return removal.SpawnGeneration >= 0
                    ? null
                    : CreateResult(BattleEntityRegistryStatus.Invalid, null);
            }

            return removal.SpawnGeneration == 0
                ? null
                : CreateResult(BattleEntityRegistryStatus.Invalid, null);
        }

        private BattleEntityRegistryResult ValidateObservation(
            BattleEntityStateObservation observation)
        {
            BattleEntityRegistryResult sessionResult = ValidateSession(
                observation.ConnectionEpoch,
                observation.SceneId);
            if (sessionResult != null)
            {
                return sessionResult;
            }

            if ((observation.ExpectedKind != BattleEntityKind.Player
                    && observation.ExpectedKind != BattleEntityKind.Monster)
                || observation.NetId <= 0
                || observation.VisibilityRevision <= 0
                || observation.ServerTick <= 0)
            {
                return CreateResult(BattleEntityRegistryStatus.Invalid, null);
            }

            return null;
        }

        private BattleEntityRegistryResult ValidateSession(long connectionEpoch, string sceneId)
        {
            if (!isActive)
            {
                return CreateResult(BattleEntityRegistryStatus.RegistryNotActive, null);
            }

            if (connectionEpoch != activeConnectionEpoch)
            {
                return CreateResult(BattleEntityRegistryStatus.SessionMismatch, null);
            }

            if (!string.Equals(sceneId, activeSceneId, StringComparison.Ordinal))
            {
                return CreateResult(BattleEntityRegistryStatus.SceneMismatch, null);
            }

            return null;
        }

        private static bool IsValidEntity(BattleEntityKind kind, string entityId)
        {
            return (kind == BattleEntityKind.Player
                    || kind == BattleEntityKind.Monster
                    || kind == BattleEntityKind.Loot)
                && !string.IsNullOrWhiteSpace(entityId)
                && entityId.Length <= BattleSyncProtocolConstants.Limits.EntityIdMaxLength;
        }

        private bool ValidateEntry(BattleEntityRegistryEntry entry, out string error)
        {
            if (entry == null
                || !IsValidEntity(entry.Kind, entry.EntityId)
                || entry.NetId <= 0
                || entry.VisibilityRevision <= 0
                || entry.LastStateServerTick < 0
                || entry.ConnectionEpoch != activeConnectionEpoch
                || !string.Equals(entry.SceneId, activeSceneId, StringComparison.Ordinal))
            {
                error = "Active entry contains invalid identity or session data.";
                return false;
            }

            if (entry.Kind == BattleEntityKind.Monster)
            {
                if (entry.SpawnGeneration < 0)
                {
                    error = "Monster entry has an invalid spawn generation.";
                    return false;
                }
            }
            else if (entry.SpawnGeneration != 0)
            {
                error = "Non-monster entry has a spawn generation.";
                return false;
            }

            error = string.Empty;
            return true;
        }

        private static BattleEntityTombstone CreateTombstone(
            BattleEntityRemoval removal,
            long nowMs)
        {
            return new BattleEntityTombstone(
                removal.Kind,
                removal.EntityId,
                removal.NetId,
                removal.ConnectionEpoch,
                removal.SceneId,
                removal.VisibilityRevision,
                removal.ServerTick,
                removal.SpawnGeneration,
                removal.Reason,
                nowMs);
        }

        private void StoreEntityTombstone(
            BattleEntityKey key,
            BattleEntityTombstone tombstone)
        {
            RemoveEntityTombstone(key);
            tombstonesByEntity[key] = tombstone;
            tombstonesByNetId[tombstone.NetId] = tombstone;
        }

        private void RemoveEntityTombstone(BattleEntityKey key)
        {
            if (!tombstonesByEntity.TryGetValue(key, out BattleEntityTombstone tombstone))
            {
                return;
            }

            tombstonesByEntity.Remove(key);
            if (tombstonesByNetId.TryGetValue(tombstone.NetId, out BattleEntityTombstone netTombstone)
                && ReferenceEquals(tombstone, netTombstone))
            {
                tombstonesByNetId.Remove(tombstone.NetId);
            }
        }

        private bool TryGetRemovalTombstone(
            BattleEntityKey key,
            int netId,
            out BattleEntityTombstone tombstone)
        {
            if (tombstonesByEntity.TryGetValue(key, out tombstone))
            {
                return true;
            }

            return tombstonesByNetId.TryGetValue(netId, out tombstone)
                && tombstone.Kind == key.Kind
                && string.Equals(tombstone.EntityId, key.EntityId, StringComparison.Ordinal);
        }

        private List<BattleEntityTombstone> GetUniqueTombstones()
        {
            HashSet<BattleEntityTombstone> unique = new HashSet<BattleEntityTombstone>();
            foreach (BattleEntityTombstone tombstone in tombstonesByEntity.Values)
            {
                unique.Add(tombstone);
            }

            foreach (BattleEntityTombstone tombstone in tombstonesByNetId.Values)
            {
                unique.Add(tombstone);
            }

            return new List<BattleEntityTombstone>(unique);
        }

        private void EnforceTombstoneCapacity()
        {
            List<BattleEntityTombstone> tombstones = GetUniqueTombstones();
            while (tombstones.Count > tombstoneCapacity)
            {
                BattleEntityTombstone oldest = tombstones[0];
                for (int index = 1; index < tombstones.Count; index++)
                {
                    if (tombstones[index].CreatedAtMs < oldest.CreatedAtMs)
                    {
                        oldest = tombstones[index];
                    }
                }

                RemoveTombstone(oldest);
                tombstones.Remove(oldest);
            }
        }

        private void RemoveTombstone(BattleEntityTombstone tombstone)
        {
            BattleEntityKey key = new BattleEntityKey(tombstone.Kind, tombstone.EntityId);
            if (tombstonesByEntity.TryGetValue(key, out BattleEntityTombstone entityTombstone)
                && ReferenceEquals(tombstone, entityTombstone))
            {
                tombstonesByEntity.Remove(key);
            }

            if (tombstonesByNetId.TryGetValue(tombstone.NetId, out BattleEntityTombstone netTombstone)
                && ReferenceEquals(tombstone, netTombstone))
            {
                tombstonesByNetId.Remove(tombstone.NetId);
            }
        }

        private void ClearAllInternal()
        {
            entriesByNetId.Clear();
            entriesByEntity.Clear();
            tombstonesByNetId.Clear();
            tombstonesByEntity.Clear();
        }

        private static BattleEntityRegistryResult CreateResult(
            BattleEntityRegistryStatus status,
            BattleEntityRegistryEntry entry)
        {
            return new BattleEntityRegistryResult(
                status,
                entry,
                false,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty);
        }

        private static BattleEntityRegistryResult CreateRepairResult(
            BattleEntityRegistryStatus status,
            BattleEntityRegistryEntry entry,
            string repairReason,
            BattleEntityKind repairKind,
            int netId,
            string detail)
        {
            return new BattleEntityRegistryResult(
                status,
                entry,
                true,
                repairReason,
                GetEntityType(repairKind),
                "netId:" + FormatNumber(netId),
                detail);
        }

        private static string GetEntityType(BattleEntityKind kind)
        {
            switch (kind)
            {
                case BattleEntityKind.Player:
                    return BattleSyncProtocolConstants.EntityTypes.Player;
                case BattleEntityKind.Monster:
                    return BattleSyncProtocolConstants.EntityTypes.Monster;
                case BattleEntityKind.Loot:
                    return BattleSyncProtocolConstants.EntityTypes.Loot;
                default:
                    return string.Empty;
            }
        }

        private static string FormatNumber(long value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
