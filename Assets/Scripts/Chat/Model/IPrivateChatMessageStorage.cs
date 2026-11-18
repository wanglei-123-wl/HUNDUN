using System.Collections.Generic;
using Game.Chat.Protocol;

namespace Game.Chat.Model
{
    public interface IPrivateChatMessageStorage
    {
        bool TryLoad(out List<WorldChatMessage> messages);
        bool TryWrite(long partnerCharacterDbId, WorldChatMessage message);
    }
}
