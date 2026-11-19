using System;

namespace Game.Client.BattleSync.Protocol
{
    [Serializable]
    public sealed class StateFrameMessageDto
    {
        public string msgId;
        public string type;
        public string sceneId;
        public string sceneSessionId;
        public long serverTick;
        public long serverTime;
        public long frameSequence;
        public CompactPlayerStateDto[] players = Array.Empty<CompactPlayerStateDto>();
        public CompactMonsterStateDto[] monsters = Array.Empty<CompactMonsterStateDto>();
        public MonsterStateDelta[] monsterDeltas = Array.Empty<MonsterStateDelta>();
        public MonsterPathUpdate[] monsterPaths = Array.Empty<MonsterPathUpdate>();
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

    public sealed class MonsterStateDelta
    {
        public const int Position = 1, Behavior = 2, Combat = 4;
        public long baseFrameSequence;
        public int mask;
        public CompactMonsterStateDto fields;

        public CompactMonsterStateDto Merge(CompactMonsterStateDto baseline)
        {
            bool position = (mask & Position) != 0;
            bool behavior = (mask & Behavior) != 0;
            bool combat = (mask & Combat) != 0;
            return new CompactMonsterStateDto
            {
                netId = fields.netId, visibilityRevision = fields.visibilityRevision,
                x = position ? fields.x : baseline.x, y = position ? fields.y : baseline.y,
                z = position ? fields.z : baseline.z, yaw = position ? fields.yaw : baseline.yaw,
                aiState = behavior ? fields.aiState : baseline.aiState, flags = behavior ? fields.flags : baseline.flags,
                hp = combat ? fields.hp : baseline.hp,
                combatRevision = combat ? fields.combatRevision : baseline.combatRevision
            };
        }
    }

    [Serializable]
    public sealed class MonsterPathDto
    {
        public long startTick, startTime;
        public float speed, x, y, z, endX, endZ;

        public bool IsValid
        {
            get
            {
                double dx = (double)endX - x, dz = (double)endZ - z;
                double length = Math.Sqrt(dx * dx + dz * dz);
                return startTick >= 0 && startTime > 0 && speed > 0 && !float.IsInfinity(speed)
                    && !float.IsNaN(y) && !float.IsInfinity(y)
                    && length > 0 && length <= speed * .5 + .03;
            }
        }
    }

    public sealed class MonsterPathUpdate
    {
        public int netId;
        public long visibilityRevision;
        public MonsterPathDto path;
    }
}
