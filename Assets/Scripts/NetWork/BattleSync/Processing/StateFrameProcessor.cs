using System;
using System.Globalization;
using System.Collections.Generic;
using Game.Client.BattleSync.Protocol;

namespace Game.Client.BattleSync
{
    public sealed class StateFrameProcessor
    {
        private readonly CompactStateDecoder decoder;
        private readonly BattleEntityRegistry entityRegistry;
        private readonly IPlayerBattleSyncModule playerModule;
        private readonly IMonsterBattleSyncModule monsterModule;
        private readonly SnapshotRepairService repairService;
        private readonly IBattleSyncClock clock;
        private readonly bool diagnosticsEnabled;
        private readonly Dictionary<string,long> diagnosticLogTimes = new Dictionary<string,long>();
        private bool isActive;
        private long activeConnectionEpoch;
        private string activeSceneId=string.Empty;
        private long highestFrameSequence;

        public StateFrameProcessor(IBattleSyncJsonCodec jsonCodec,CompactStateDecoder decoder,
            BattleEntityRegistry entityRegistry,IPlayerBattleSyncModule playerModule,
            IMonsterBattleSyncModule monsterModule,SnapshotRepairService repairService,
            IBattleSyncClock clock = null,bool diagnosticsEnabled = false)
        {
            if(jsonCodec==null)throw new ArgumentNullException(nameof(jsonCodec));
            this.decoder=decoder??throw new ArgumentNullException(nameof(decoder));
            this.entityRegistry=entityRegistry??throw new ArgumentNullException(nameof(entityRegistry));
            this.playerModule=playerModule??throw new ArgumentNullException(nameof(playerModule));
            this.monsterModule=monsterModule??throw new ArgumentNullException(nameof(monsterModule));
            this.repairService=repairService??throw new ArgumentNullException(nameof(repairService));
            this.clock=clock;
            this.diagnosticsEnabled=diagnosticsEnabled;
        }

        public void BeginSession(long connectionEpoch,string sceneId)
        {
            if(connectionEpoch<=0)throw new ArgumentOutOfRangeException(nameof(connectionEpoch));
            if(string.IsNullOrWhiteSpace(sceneId))throw new ArgumentException("Scene ID is required.",nameof(sceneId));
            diagnosticLogTimes.Clear();
            activeConnectionEpoch=connectionEpoch;activeSceneId=sceneId;highestFrameSequence=0;isActive=true;
        }

        public void EndSession(long connectionEpoch)
        {
            if(!isActive||connectionEpoch!=activeConnectionEpoch)return;
            diagnosticLogTimes.Clear();
            activeConnectionEpoch=0;activeSceneId=string.Empty;highestFrameSequence=0;isActive=false;
        }

        public BattleSyncApplyResult Process(long connectionEpoch,string json)
        {
            if(!isActive||connectionEpoch!=activeConnectionEpoch)return BattleSyncApplyResult.IgnoredWrongScene;
            if(!StateFrameArrayParser.TryParse(json,out StateFrameMessageDto message,
                out StateFrameParseError parseError))
            {
                LogMalformedFrame(parseError);
                return BattleSyncApplyResult.InvalidMessage;
            }
            if(!string.Equals(message.sceneId,activeSceneId,StringComparison.Ordinal))
                return BattleSyncApplyResult.IgnoredWrongScene;
            if(message.frameSequence<=highestFrameSequence)return BattleSyncApplyResult.IgnoredStale;

            DecodedPlayerState[] players=new DecodedPlayerState[message.players.Length];
            for(int index=0;index<players.Length;index++)
            {
                if(decoder.TryDecodePlayer(message.players[index],out players[index],out _))continue;
                LogMalformedFrame(new StateFrameParseError("players["+index+"]","Invalid player state.",
                    message.sceneId,message.frameSequence));
                return BattleSyncApplyResult.InvalidMessage;
            }
            DecodedMonsterState[] monsters=new DecodedMonsterState[message.monsters.Length];
            for(int index=0;index<monsters.Length;index++)
            {
                if(decoder.TryDecodeMonster(message.monsters[index],out monsters[index],out _))continue;
                LogMalformedFrame(new StateFrameParseError("monsters["+index+"]","Invalid monster state.",
                    message.sceneId,message.frameSequence));
                return BattleSyncApplyResult.InvalidMessage;
            }

            bool applied=false;
            for(int index=0;index<players.Length;index++)
            {
                DecodedPlayerState state=players[index];
                BattleEntityStateObservation observation=new BattleEntityStateObservation(
                    connectionEpoch,message.sceneId,BattleEntityKind.Player,state.NetId,state.VisibilityRevision,message.serverTick);
                BattleEntityRegistryResult observed=entityRegistry.ObserveState(observation,false);
                if(observed.Status==BattleEntityRegistryStatus.StateAccepted)
                {
                    state=state.WithEntityId(observed.Entry.EntityId);
                    BattleSyncApplyResult result=playerModule.ApplyPlayerDynamicState(state,message.serverTick,message.serverTime);
                    bool succeeded=result==BattleSyncApplyResult.Applied||result==BattleSyncApplyResult.AlreadyApplied;
                    if(succeeded)entityRegistry.CommitStateTick(observation,observed.Entry);
                    applied|=succeeded;
                    if(result==BattleSyncApplyResult.RetryableFailure)
                        repairService.RequestRepair(SnapshotRepairReason.MissingEntity,BattleEntityKind.Player,
                            "characterDbId:"+state.EntityId,0);
                }
                else RequestRepairIfNeeded(observed,BattleEntityKind.Player,state.NetId);
            }
            for(int index=0;index<monsters.Length;index++)
            {
                DecodedMonsterState state=monsters[index];
                BattleEntityStateObservation observation=new BattleEntityStateObservation(
                    connectionEpoch,message.sceneId,BattleEntityKind.Monster,state.NetId,state.VisibilityRevision,message.serverTick);
                BattleEntityRegistryResult observed=entityRegistry.ObserveState(observation,false);
                if(observed.Status==BattleEntityRegistryStatus.StateAccepted)
                {
                    state=state.WithEntityId(observed.Entry.EntityId);
                    BattleSyncApplyResult result=monsterModule.ApplyMonsterDynamicState(state,message.serverTick,message.serverTime);
                    bool succeeded=result==BattleSyncApplyResult.Applied||result==BattleSyncApplyResult.AlreadyApplied;
                    if(succeeded)entityRegistry.CommitStateTick(observation,observed.Entry);
                    applied|=succeeded;
                    if(result==BattleSyncApplyResult.RetryableFailure)
                        repairService.RequestRepair(SnapshotRepairReason.MissingEntity,BattleEntityKind.Monster,
                            "monsterInstanceId:"+state.EntityId,0);
                }
                else RequestRepairIfNeeded(observed,BattleEntityKind.Monster,state.NetId);
            }
            highestFrameSequence=message.frameSequence;
            return applied?BattleSyncApplyResult.Applied:BattleSyncApplyResult.IgnoredStale;
        }

        private void LogMalformedFrame(StateFrameParseError error)
        {
            string key="format|"+error.FieldPath+"|"+error.Reason;
            long nowMs=clock==null?0:clock.NowMilliseconds;
            if(diagnosticLogTimes.TryGetValue(key,out long lastMs)&&nowMs-lastMs<1000)return;
            if(diagnosticLogTimes.Count>=128)diagnosticLogTimes.Clear();
            diagnosticLogTimes[key]=nowMs;
            UnityEngine.Debug.LogWarning("[BATTLE_SYNC][STATE_FRAME_INVALID] sceneId="
                +(string.IsNullOrEmpty(error.SceneId)?activeSceneId:error.SceneId)
                +" frameSequence="+(error.FrameSequence.HasValue
                    ?error.FrameSequence.Value.ToString(CultureInfo.InvariantCulture):"unknown")
                +" field="+error.FieldPath+" reason="+error.Reason);
        }

        private void RequestRepairIfNeeded(BattleEntityRegistryResult result,BattleEntityKind kind,int netId)
        {
            if(result==null||!result.RequiresSnapshotRepair)return;
            if(diagnosticsEnabled)
            {
                string key=kind+"|"+netId.ToString(CultureInfo.InvariantCulture)+"|"
                    +result.Status+"|"+result.RepairReason;
                long nowMs=clock==null?0:clock.NowMilliseconds;
                if(!diagnosticLogTimes.TryGetValue(key,out long lastMs)||nowMs-lastMs>=1000)
                {
                    diagnosticLogTimes[key]=nowMs;
                    UnityEngine.Debug.LogWarning("[BATTLE_SYNC][STATE_REPAIR] kind="+kind
                        +" netId="+netId.ToString(CultureInfo.InvariantCulture)
                        +" registryStatus="+result.Status+" reason="+result.RepairReason);
                }
            }
            repairService.RequestRepair(MapReason(result.RepairReason),kind,
                "netId:"+netId.ToString(CultureInfo.InvariantCulture),0);
        }

        private static SnapshotRepairReason MapReason(string reason)
        {
            if(reason==BattleSyncProtocolConstants.RepairReasons.UnknownEntityState)return SnapshotRepairReason.UnknownEntityState;
            if(reason==BattleSyncProtocolConstants.RepairReasons.RevisionGap)return SnapshotRepairReason.RevisionGap;
            if(reason==BattleSyncProtocolConstants.RepairReasons.MissingEntity)return SnapshotRepairReason.MissingEntity;
            if(reason==BattleSyncProtocolConstants.RepairReasons.GenerationMismatch)return SnapshotRepairReason.GenerationMismatch;
            return SnapshotRepairReason.SnapshotMismatch;
        }
    }
}
