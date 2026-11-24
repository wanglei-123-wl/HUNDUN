using Game.Client.BattleSync.Protocol;

namespace Game.Client.BattleSync
{
    public readonly struct DecodedPlayerState
    {
        public int NetId { get; }
        public string EntityId { get; }
        public long VisibilityRevision { get; }
        public long InputSequence { get; }
        public ushort Flags { get; }
        public bool HasPosition { get; }
        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public float YawDegrees { get; }
        public bool HasVelocity { get; }
        public float VelocityX { get; }
        public float VelocityY { get; }
        public float VelocityZ { get; }
        public bool HasMovementState { get; }
        public byte MovementState { get; }
        public bool HasActionState { get; }
        public byte ActionState { get; }

        public DecodedPlayerState(int netId,long visibilityRevision,long inputSequence,ushort flags,
            bool hasPosition,float x,float y,float z,float yawDegrees,bool hasVelocity,
            float velocityX,float velocityY,float velocityZ,bool hasMovementState,byte movementState,
            bool hasActionState,byte actionState,string entityId="")
        {
            NetId=netId;EntityId=entityId??string.Empty;VisibilityRevision=visibilityRevision;InputSequence=inputSequence;Flags=flags;
            HasPosition=hasPosition;X=x;Y=y;Z=z;YawDegrees=yawDegrees;HasVelocity=hasVelocity;
            VelocityX=velocityX;VelocityY=velocityY;VelocityZ=velocityZ;
            HasMovementState=hasMovementState;MovementState=movementState;
            HasActionState=hasActionState;ActionState=actionState;
        }

        public DecodedPlayerState WithEntityId(string entityId)=>new DecodedPlayerState(NetId,VisibilityRevision,
            InputSequence,Flags,HasPosition,X,Y,Z,YawDegrees,HasVelocity,VelocityX,VelocityY,VelocityZ,
            HasMovementState,MovementState,HasActionState,ActionState,entityId);
    }

    public readonly struct DecodedMonsterState
    {
        public int NetId { get; }
        public string EntityId { get; }
        public long VisibilityRevision { get; }
        public ushort Flags { get; }
        public bool HasPosition { get; }
        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public float YawDegrees { get; }
        public bool HasAiState { get; }
        public byte AiState { get; }
        public bool HasHp { get; }
        public int Hp { get; }
        public bool HasCombatRevision { get; }
        public long CombatRevision { get; }
        public bool HasPathUpdate { get; }
        public MonsterPathDto Path { get; }

        public DecodedMonsterState(int netId,long visibilityRevision,ushort flags,bool hasPosition,
            float x,float y,float z,float yawDegrees,bool hasAiState,byte aiState,bool hasHp,int hp,
            bool hasCombatRevision,long combatRevision,string entityId="",bool hasPathUpdate=false,MonsterPathDto path=null)
        {
            NetId=netId;EntityId=entityId??string.Empty;VisibilityRevision=visibilityRevision;Flags=flags;HasPosition=hasPosition;
            X=x;Y=y;Z=z;YawDegrees=yawDegrees;HasAiState=hasAiState;AiState=aiState;
            HasHp=hasHp;Hp=hp;HasCombatRevision=hasCombatRevision;CombatRevision=combatRevision;
            HasPathUpdate=hasPathUpdate;Path=path;
        }

        public DecodedMonsterState WithEntityId(string entityId)=>new DecodedMonsterState(NetId,VisibilityRevision,
            Flags,HasPosition,X,Y,Z,YawDegrees,HasAiState,AiState,HasHp,Hp,HasCombatRevision,CombatRevision,entityId,HasPathUpdate,Path);

        public DecodedMonsterState WithPath(MonsterPathDto path)=>new DecodedMonsterState(NetId,VisibilityRevision,
            Flags,HasPosition,X,Y,Z,YawDegrees,HasAiState,AiState,HasHp,Hp,HasCombatRevision,CombatRevision,EntityId,true,path);

        public DecodedMonsterState WithChangedGroups(int mask)=>new DecodedMonsterState(NetId,VisibilityRevision,
            Flags,HasPosition&&(mask&MonsterStateDelta.Position)!=0,X,Y,Z,YawDegrees,
            HasAiState&&(mask&MonsterStateDelta.Behavior)!=0,AiState,
            HasHp&&(mask&MonsterStateDelta.Combat)!=0,Hp,
            HasCombatRevision&&(mask&MonsterStateDelta.Combat)!=0,CombatRevision,EntityId,HasPathUpdate,Path);
    }

    public sealed class CompactStateDecoder
    {
        private const float PositionScale=0.01f;
        private const float VelocityScale=0.01f;
        private const float YawScale=360f/65535f;

        public bool TryDecodePlayer(CompactPlayerStateDto source,out DecodedPlayerState state,out string error)
        {
            state=default(DecodedPlayerState);
            if(source==null||source.netId<=0||source.yaw<0||source.yaw>65535
                ||!IsShort(source.vx)||!IsShort(source.vy)||!IsShort(source.vz)
                ||!IsByte(source.move)||!IsByte(source.action)||!IsUShort(source.flags)
                ||source.visibilityRevision<0||source.inputSeq<0)
            {error="Compact player state is invalid.";return false;}
            ushort flags=(ushort)source.flags;
            state=new DecodedPlayerState(source.netId,source.visibilityRevision,source.inputSeq,flags,
                Has(flags,BattleSyncProtocolConstants.StateFlags.Position),source.x*PositionScale,
                source.y*PositionScale,source.z*PositionScale,source.yaw*YawScale,
                Has(flags,BattleSyncProtocolConstants.StateFlags.Velocity),source.vx*VelocityScale,
                source.vy*VelocityScale,source.vz*VelocityScale,
                Has(flags,BattleSyncProtocolConstants.StateFlags.MovementState),(byte)source.move,
                Has(flags,BattleSyncProtocolConstants.StateFlags.ActionState),(byte)source.action);
            error=string.Empty;return true;
        }

        public bool TryDecodeMonster(CompactMonsterStateDto source,out DecodedMonsterState state,out string error)
        {
            state=default(DecodedMonsterState);
            if(source==null||source.netId<=0||source.yaw<0||source.yaw>65535
                ||!IsByte(source.aiState)||!IsUShort(source.flags)||source.visibilityRevision<0
                ||source.combatRevision<0||source.hp<0)
            {error="Compact monster state is invalid.";return false;}
            ushort flags=(ushort)source.flags;
            bool hasAi=Has(flags,BattleSyncProtocolConstants.StateFlags.MovementState);
            if(hasAi&&source.aiState>BattleSyncProtocolConstants.MonsterAiStates.Hover){error="Compact monster AI state is invalid.";return false;}
            state=new DecodedMonsterState(source.netId,source.visibilityRevision,flags,
                Has(flags,BattleSyncProtocolConstants.StateFlags.Position),source.x*PositionScale,
                source.y*PositionScale,source.z*PositionScale,source.yaw*YawScale,
                hasAi,(byte)source.aiState,Has(flags,BattleSyncProtocolConstants.StateFlags.Hp),source.hp,
                Has(flags,BattleSyncProtocolConstants.StateFlags.CombatRevision),source.combatRevision);
            error=string.Empty;return true;
        }

        private static bool Has(ushort flags,ushort value)=>(flags&value)!=0;
        private static bool IsShort(int value)=>value>=short.MinValue&&value<=short.MaxValue;
        private static bool IsByte(int value)=>value>=byte.MinValue&&value<=byte.MaxValue;
        private static bool IsUShort(int value)=>value>=ushort.MinValue&&value<=ushort.MaxValue;
    }
}
