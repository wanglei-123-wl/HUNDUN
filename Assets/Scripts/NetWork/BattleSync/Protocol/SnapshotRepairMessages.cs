using System;
using Game.Client.BattleSync;

namespace Game.Client.BattleSync.Protocol
{
    [Serializable]
    public sealed class SnapshotRepairRequestDto
    {
        public string msgId;
        public string type;
        public string sceneSessionId;
        public long requestSequence;
        public string reason;
        public string entityType;
        public string entityId;
        public long clientSnapshotRevision;

        public SnapshotRepairRequestDto()
        {
            msgId = BattleSyncProtocolConstants.MessageIds.SnapshotRepairRequest;
            type = BattleSyncProtocolConstants.MessageIds.SnapshotRepairRequest;
        }
    }

    [Serializable]
    public sealed class BattleOutboxAckDto
    {
        public string msgId;
        public string type;
        public long reliableMessageId;

        public BattleOutboxAckDto()
        {
            msgId = BattleSyncProtocolConstants.MessageIds.BattleOutboxAck;
            type = BattleSyncProtocolConstants.MessageIds.BattleOutboxAck;
        }
    }
}
