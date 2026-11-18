using System;
using System.Collections.Generic;

public sealed class LootErrorTextProvider
{
    private const string GenericErrorText = "拾取失败，请稍后重试";

    private readonly Dictionary<string, string> messages =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { LootProtocolConstants.ErrorCodes.PickupDuplicateOrStale, "拾取序号已失效，请重新进入游戏" },
            { LootProtocolConstants.ErrorCodes.PickupSequenceGap, "拾取序号不同步，请重新进入游戏" },
            { LootProtocolConstants.ErrorCodes.PlayerNotInWorld, "当前角色尚未进入世界，请重新进入游戏" },
            { LootProtocolConstants.ErrorCodes.PlayerDead, "死亡状态无法拾取" },
            { LootProtocolConstants.ErrorCodes.LootNotFound, "掉落物已经消失" },
            { LootProtocolConstants.ErrorCodes.LootAlreadyPicked, "掉落物已被拾取" },
            { LootProtocolConstants.ErrorCodes.LootExpired, "掉落物已经过期" },
            { LootProtocolConstants.ErrorCodes.LootNotOwner, "掉落物仍处于归属保护中" },
            { LootProtocolConstants.ErrorCodes.LootTooFar, "距离掉落物太远" },
            { LootProtocolConstants.ErrorCodes.ServerBusy, "服务器繁忙，请稍后重试" },
            { LootProtocolConstants.ErrorCodes.RewardNotAvailable, "奖励服务暂不可用" },
            { LootProtocolConstants.ErrorCodes.InvalidLootPickup, "拾取请求无效，请重新进入游戏" },
            { LootProtocolConstants.ErrorCodes.InventoryFull, "背包空间不足" },
            { LootProtocolConstants.ErrorCodes.AlreadyClaimed, "掉落物已被其他玩家领取" },
            { LootProtocolConstants.ErrorCodes.WalletLimitExceeded, "钱包已达到上限" },
            { LootProtocolConstants.ErrorCodes.CharacterNotFound, "角色数据不存在" },
            { LootProtocolConstants.ErrorCodes.ItemNotFound, "服务端物品配置不存在" },
            { LootProtocolConstants.ErrorCodes.ItemNotGrantable, "该物品当前不可发放" },
            { LootProtocolConstants.ErrorCodes.RewardSourceConflict, "奖励来源状态冲突" },
            { LootProtocolConstants.ErrorCodes.GrantIdConflict, "奖励流水冲突" },
            { LootProtocolConstants.ErrorCodes.InvalidGrantRequest, "奖励请求无效" },
            { LootProtocolConstants.ErrorCodes.InvalidGrantSource, "奖励来源无效" },
            { LootProtocolConstants.ErrorCodes.DatabaseError, "奖励数据处理失败" },
            { LootProtocolConstants.ErrorCodes.DatabaseUnavailable, "奖励数据库暂不可用" },
            { LootProtocolConstants.ErrorCodes.DatabaseOperationCancelled, "奖励处理已取消" },
            { LootProtocolConstants.ErrorCodes.GrantNotFound, "未找到奖励发放记录" }
        };

    public string GetMessage(string errorCode)
    {
        if (!string.IsNullOrWhiteSpace(errorCode)
            && messages.TryGetValue(errorCode, out string message))
        {
            return message;
        }

        return GenericErrorText;
    }

    public bool IsKnown(string errorCode)
    {
        return !string.IsNullOrWhiteSpace(errorCode)
            && messages.ContainsKey(errorCode);
    }
}
