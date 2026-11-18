using System;
using Game.Client.BattleSync.Protocol;

namespace Game.Client.BattleSync
{
    public enum SnapshotRepairReason
    {
        UnknownEntityState=0,
        RevisionGap=1,
        MissingEntity=2,
        InventoryMismatch=3,
        GenerationMismatch=4,
        SnapshotMismatch=5,
        ManualRequest=6
    }

    public sealed class SnapshotRepairService
    {
        private const long MinimumSendIntervalMs=1000;
        private const long RequestTimeoutMs=3000;
        private const int MaximumAttempts=3;
        private readonly IBattleSyncTransport transport;
        private readonly IBattleSyncJsonCodec jsonCodec;
        private readonly IBattleSyncClock clock;
        private readonly Func<string> sceneSessionId;
        private bool isActive;
        private long activeConnectionEpoch;
        private string activeSceneId=string.Empty;
        private long activeSnapshotRevision;
        private long nextRequestSequence=1;
        private long lastGlobalSendAtMs=-MinimumSendIntervalMs;
        private PendingRepair pending;

        public event Action RepairQueued;
        public event Action RepairAttemptsExhausted;
        public bool IsActive=>isActive;
        public bool HasPendingRepair=>pending!=null;

        public SnapshotRepairService(IBattleSyncTransport transport,IBattleSyncJsonCodec jsonCodec,IBattleSyncClock clock,
            Func<string> sceneSessionId=null)
        {
            this.transport=transport??throw new ArgumentNullException(nameof(transport));
            this.jsonCodec=jsonCodec??throw new ArgumentNullException(nameof(jsonCodec));
            this.clock=clock??throw new ArgumentNullException(nameof(clock));
            this.sceneSessionId=sceneSessionId;
        }

        public void BeginSession(long connectionEpoch,string sceneId,long snapshotRevision)
        {
            if(connectionEpoch<=0)throw new ArgumentOutOfRangeException(nameof(connectionEpoch));
            if(string.IsNullOrWhiteSpace(sceneId))throw new ArgumentException("Scene ID is required.",nameof(sceneId));
            if(snapshotRevision<0)throw new ArgumentOutOfRangeException(nameof(snapshotRevision));
            pending=null;activeConnectionEpoch=connectionEpoch;activeSceneId=sceneId;
            activeSnapshotRevision=snapshotRevision;lastGlobalSendAtMs=-MinimumSendIntervalMs;isActive=true;
        }

        public void EndSession(long connectionEpoch)
        {
            if(!isActive||connectionEpoch!=activeConnectionEpoch)return;
            pending=null;activeConnectionEpoch=0;activeSceneId=string.Empty;
            activeSnapshotRevision=0;isActive=false;
        }

        public void RequestRepair(SnapshotRepairReason reason,BattleEntityKind entityKind,string entityId,long clientSnapshotRevision)
        {
            if(!isActive||clientSnapshotRevision<0)return;
            string entityType=ToEntityType(entityKind);
            string safeEntityId=entityId??string.Empty;
            if(safeEntityId.Length>128)safeEntityId=safeEntityId.Substring(0,128);
            if(pending==null)
            {
                pending=new PendingRepair(nextRequestSequence++,reason,entityType,safeEntityId,clientSnapshotRevision);
                RepairQueued?.Invoke();
                return;
            }
            if(pending.Reason==reason&&string.Equals(pending.EntityType,entityType,StringComparison.Ordinal)
                &&string.Equals(pending.EntityId,safeEntityId,StringComparison.Ordinal))return;
            pending.Reason=SnapshotRepairReason.SnapshotMismatch;
            pending.EntityType=string.Empty;pending.EntityId=string.Empty;
            pending.ClientSnapshotRevision=Math.Max(pending.ClientSnapshotRevision,clientSnapshotRevision);
        }

        public void Update()
        {
            if(!isActive||pending==null)return;
            long nowMs=clock.NowMilliseconds;
            if(pending.AttemptCount>=MaximumAttempts)
            {
                if(nowMs-pending.LastAttemptAtMs>=RequestTimeoutMs)
                {pending=null;RepairAttemptsExhausted?.Invoke();}
                return;
            }
            bool first=pending.AttemptCount==0;
            bool timedOut=!first&&nowMs-pending.LastAttemptAtMs>=RequestTimeoutMs;
            if((first||timedOut)&&nowMs-lastGlobalSendAtMs>=MinimumSendIntervalMs)SendPending(nowMs);
        }

        public void NotifySnapshotBatchCompleted(long snapshotRevision)
        {
            if(!isActive||snapshotRevision<=activeSnapshotRevision)return;
            activeSnapshotRevision=snapshotRevision;pending=null;
        }

        private void SendPending(long nowMs)
        {
            string currentSceneSessionId=sceneSessionId==null?null:sceneSessionId();
            if(sceneSessionId!=null&&string.IsNullOrWhiteSpace(currentSceneSessionId))return;
            SnapshotRepairRequestDto request=new SnapshotRepairRequestDto
            {
                sceneSessionId=currentSceneSessionId,
                requestSequence=pending.RequestSequence,
                reason=ToReason(pending.Reason),
                entityType=pending.EntityType,
                entityId=pending.EntityId,
                clientSnapshotRevision=pending.ClientSnapshotRevision
            };
            pending.AttemptCount++;pending.LastAttemptAtMs=nowMs;lastGlobalSendAtMs=nowMs;
            if(!transport.IsConnected||transport.ConnectionEpoch!=activeConnectionEpoch)return;
            if(!jsonCodec.TrySerialize(request,out string json,out _))return;
            transport.SendJson(json);
        }

        private static string ToReason(SnapshotRepairReason reason)
        {
            switch(reason)
            {
                case SnapshotRepairReason.UnknownEntityState:return BattleSyncProtocolConstants.RepairReasons.UnknownEntityState;
                case SnapshotRepairReason.RevisionGap:return BattleSyncProtocolConstants.RepairReasons.RevisionGap;
                case SnapshotRepairReason.MissingEntity:return BattleSyncProtocolConstants.RepairReasons.MissingEntity;
                case SnapshotRepairReason.InventoryMismatch:return BattleSyncProtocolConstants.RepairReasons.InventoryMismatch;
                case SnapshotRepairReason.GenerationMismatch:return BattleSyncProtocolConstants.RepairReasons.GenerationMismatch;
                case SnapshotRepairReason.ManualRequest:return BattleSyncProtocolConstants.RepairReasons.ManualRequest;
                default:return BattleSyncProtocolConstants.RepairReasons.SnapshotMismatch;
            }
        }

        private static string ToEntityType(BattleEntityKind kind)
        {
            switch(kind)
            {
                case BattleEntityKind.Player:return BattleSyncProtocolConstants.EntityTypes.Player;
                case BattleEntityKind.Monster:return BattleSyncProtocolConstants.EntityTypes.Monster;
                case BattleEntityKind.Loot:return BattleSyncProtocolConstants.EntityTypes.Loot;
                default:return string.Empty;
            }
        }

        private sealed class PendingRepair
        {
            public long RequestSequence;
            public SnapshotRepairReason Reason;
            public string EntityType;
            public string EntityId;
            public long ClientSnapshotRevision;
            public int AttemptCount;
            public long LastAttemptAtMs;
            public PendingRepair(long sequence,SnapshotRepairReason reason,string entityType,string entityId,long revision)
            {RequestSequence=sequence;Reason=reason;EntityType=entityType;EntityId=entityId;ClientSnapshotRevision=revision;}
        }
    }
}
