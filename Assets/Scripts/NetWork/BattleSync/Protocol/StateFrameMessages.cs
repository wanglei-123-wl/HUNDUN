using System;

namespace Game.Client.BattleSync.Protocol
{
    [Serializable]
    public sealed class StateFrameMessageDto
    {
        public string msgId;
        public string type;
        public string sceneId;
        public long serverTick;
        public long serverTime;
        public long frameSequence;
        public CompactPlayerStateDto[] players = Array.Empty<CompactPlayerStateDto>();
        public CompactMonsterStateDto[] monsters = Array.Empty<CompactMonsterStateDto>();
    }

    [Serializable]
    public sealed class CompactPlayerStateDto
    {
        public int netId;
        public int x;
        public int y;
        public int z;
        public int yaw;
        public int vx;
        public int vy;
        public int vz;
        public int move;
        public int action;
        public int flags;
        public long inputSeq;
        public long visibilityRevision;
    }

    [Serializable]
    public sealed class CompactMonsterStateDto
    {
        public int netId;
        public int x;
        public int y;
        public int z;
        public int yaw;
        public int aiState;
        public int flags;
        public int hp;
        public long combatRevision;
        public long visibilityRevision;
    }
}
