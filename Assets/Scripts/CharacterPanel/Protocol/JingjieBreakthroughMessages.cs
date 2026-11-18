using System;
using Game.Inventory.Protocol.V2;

namespace Game.CharacterPanel.Protocol
{
    public static class JingjieBreakthroughMessageIds
    {
        public const string Request = "jingjie_breakthrough";
        public const string Result = "jingjie_breakthrough_result";
    }

    public static class JingjieNextRealmFields
    {
        public static bool IsValid(bool hasNext, string id, int stage, string name)
        {
            return hasNext
                ? !string.IsNullOrWhiteSpace(id)
                    && !string.IsNullOrWhiteSpace(name) && stage >= 0
                : id == string.Empty && name == string.Empty && stage == 0;
        }
    }

    [Serializable]
    public sealed class JingjieBreakthroughRequestDto : NetMessageBase
    {
        public string jingjieOperationSessionId;
        public long requestSequence;
        public string clientRequestId;

        public JingjieBreakthroughRequestDto(string sessionId, long sequence,
            string requestId)
        {
            msgId = type = JingjieBreakthroughMessageIds.Request;
            jingjieOperationSessionId = sessionId;
            requestSequence = sequence;
            clientRequestId = requestId;
        }
    }

    [Serializable]
    public sealed class JingjieBreakthroughResultDto : NetMessageBase
    {
        public string jingjieOperationSessionId;
        public long requestSequence;
        public string clientRequestId;
        public bool success;
        public bool idempotentReplay;
        public long characterDbId;
        public string jingjieId;
        public int jingjieStage;
        public string jingjieName;
        public bool hasNextJingjie;
        public string nextJingjieId;
        public int nextJingjieStage;
        public string nextJingjieName;
        public long jingjieExp;
        public long requiredJingjieExp;
        public long totalJingjieExp;
        public long inventoryRevision;
        public bool requiresInventorySnapshot;
        public InventoryV2StackChangeDto[] stackItemChanges;
        public string errorCode;
        public string message;
        public long serverTime;
    }
}
