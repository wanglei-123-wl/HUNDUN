using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Game.PlayerTrade.Protocol
{
    public static class PlayerTradeMessageIds
    {
        public const string OrderList = "player_trade_order_list";
        public const string OrderListResult = "player_trade_order_list_result";
        public const string Create = "player_trade_create";
        public const string CreateResult = "player_trade_create_result";
        public const string Buy = "player_trade_buy";
        public const string BuyResult = "player_trade_buy_result";
        public const string Cancel = "player_trade_cancel";
        public const string CancelResult = "player_trade_cancel_result";
        public const string Reject = "player_trade_reject";
        public const string ClaimList = "player_trade_claim_list";
        public const string ClaimListResult = "player_trade_claim_list_result";
        public const string Claim = "player_trade_claim";
        public const string ClaimResult = "player_trade_claim_result";
        public const string OrderNotice = "player_trade_order_notice";
        public const string OrderChanged = "player_trade_order_changed";
    }

    [Serializable]
    public sealed class PlayerTradeOrderListRequestDto : NetMessageBase
    {
        public string listType = "market";
        public string cursor = string.Empty;
        public int pageSize = 20;
        public string[] itemIds = new string[0];
        public long? minimumPrice;
        public long? maximumPrice;
        public string sort = "newest";

        public PlayerTradeOrderListRequestDto()
        {
            msgId = type = PlayerTradeMessageIds.OrderList;
        }
    }

    [Serializable]
    public class PlayerTradeWriteRequestDto : NetMessageBase
    {
        public string playerTradeOperationSessionId;
        public long requestSequence;
        public string clientRequestId;
    }

    [Serializable]
    public sealed class PlayerTradeBuyRequestDto : PlayerTradeWriteRequestDto
    {
        public string orderId;

        public PlayerTradeBuyRequestDto(long sequence, string id)
        {
            msgId = type = PlayerTradeMessageIds.Buy;
            requestSequence = sequence;
            orderId = id ?? string.Empty;
        }
    }

    [Serializable]
    public sealed class PlayerTradeCreateRequestDto : PlayerTradeWriteRequestDto
    {
        public string visibility = "public";
        public string targetNickname = string.Empty;
        public string assetKind;
        public long stackId;
        public string itemInstanceId = string.Empty;
        public int quantity;
        public long totalPrice;

        public PlayerTradeCreateRequestDto()
        {
            msgId = type = PlayerTradeMessageIds.Create;
        }
    }

    [Serializable]
    public sealed class PlayerTradeCreateResultDto : NetMessageBase
    {
        public string playerTradeOperationSessionId;
        public string clientRequestId;
        public bool success;
        public string status;
        public string operationId;
        public long requestSequence;
        public string errorCode;
        public PlayerTradeOrderDto order;
        public long inventoryRevision;
        public bool requiresSnapshot;
        public PlayerTradeInventoryChangeDto[] inventoryChanges;
        public long serverTimeUnixMilliseconds;
    }

    [Serializable]
    public sealed class PlayerTradeCancelRequestDto : PlayerTradeWriteRequestDto
    {
        public string orderId;

        public PlayerTradeCancelRequestDto()
        {
            msgId = type = PlayerTradeMessageIds.Cancel;
        }
    }

    [Serializable]
    public sealed class PlayerTradeCancelResultDto : NetMessageBase
    {
        public string playerTradeOperationSessionId;
        public string clientRequestId;
        public bool success;
        public string status;
        public string operationId;
        public long requestSequence;
        public string errorCode;
        public PlayerTradeOrderDto order;
        public PlayerTradeClaimDto claim;
        public long inventoryRevision;
        public bool requiresSnapshot;
        public PlayerTradeInventoryChangeDto[] inventoryChanges;
        public long serverTimeUnixMilliseconds;
    }

    [Serializable]
    public sealed class PlayerTradeRejectRequestDto : PlayerTradeWriteRequestDto
    {
        public string orderId;

        public PlayerTradeRejectRequestDto()
        {
            msgId = type = PlayerTradeMessageIds.Reject;
        }
    }

    [Serializable]
    public sealed class PlayerTradeClaimListRequestDto : NetMessageBase
    {
        public string cursor = string.Empty;
        public int pageSize = 20;
        public bool includeClaimed;

        public PlayerTradeClaimListRequestDto()
        {
            msgId = type = PlayerTradeMessageIds.ClaimList;
        }
    }

    [Serializable]
    public sealed class PlayerTradeClaimListResultDto : NetMessageBase
    {
        public bool success;
        public string cursor;
        public string nextCursor;
        public PlayerTradeClaimDto[] claims;
        public string errorCode;
        public long serverTimeUnixMilliseconds;
    }

    [Serializable]
    public sealed class PlayerTradeClaimRequestDto : PlayerTradeWriteRequestDto
    {
        public string claimId;

        public PlayerTradeClaimRequestDto()
        {
            msgId = type = PlayerTradeMessageIds.Claim;
        }
    }

    [Serializable]
    public sealed class PlayerTradeClaimResultDto : NetMessageBase
    {
        public string playerTradeOperationSessionId;
        public string clientRequestId;
        public bool success;
        public string status;
        public string operationId;
        public long requestSequence;
        public string errorCode;
        public PlayerTradeClaimDto claim;
        public long inventoryRevision;
        public bool requiresSnapshot;
        public PlayerTradeBalanceDto balance;
        public PlayerTradeInventoryChangeDto[] inventoryChanges;
        public long serverTimeUnixMilliseconds;
    }

    [Serializable]
    public sealed class PlayerTradeOrderListResultDto : NetMessageBase
    {
        public bool success;
        public string listType;
        public string cursor;
        public string nextCursor;
        public PlayerTradeOrderDto[] orders;
        public string errorCode;
        public string message;
        public long serverTimeUnixMilliseconds;
    }

    [Serializable]
    public sealed class PlayerTradeBuyResultDto : NetMessageBase
    {
        public string playerTradeOperationSessionId;
        public string clientRequestId;
        public bool success;
        public string status;
        public string operationId;
        public long requestSequence;
        public string errorCode;
        public string message;
        public PlayerTradeOrderDto order;
        public PlayerTradeClaimDto claim;
        public long inventoryRevision;
        public bool requiresSnapshot;
        public PlayerTradeBalanceDto balance;
        public PlayerTradeInventoryChangeDto[] inventoryChanges;
        public long serverTimeUnixMilliseconds;
    }

    [Serializable]
    public sealed class PlayerTradeOrderNoticeDto : NetMessageBase
    {
        public string eventId;
        public string eventType;
        public PlayerTradeOrderDto order;
        public long serverTimeUnixMilliseconds;
    }

    [Serializable]
    public sealed class PlayerTradeOrderChangedDto : NetMessageBase
    {
        public string eventId;
        public string eventType;
        public PlayerTradeOrderDto order;
        public long serverTimeUnixMilliseconds;
    }

    [Serializable]
    public sealed class PlayerTradeOrderDto
    {
        public string orderId;
        public long sellerCharacterDbId;
        public string sellerNickname;
        public string visibility;
        public long targetCharacterDbId;
        public string targetNickname;
        public PlayerTradeAssetDto asset;
        public string currencyId;
        public long totalPrice;
        public string status;
        public long buyerCharacterDbId;
        public string buyerNickname;
        public long createdAtUnixMilliseconds;
        public long expiresAtUnixMilliseconds;
        public long completedAtUnixMilliseconds;
        public long revision;
    }

    [Serializable]
    public sealed class PlayerTradeAssetDto
    {
        public string kind;
        public string itemId;
        public int quantity;
        public bool isBound;
        public string itemInstanceId;
        public string equipmentId;
        public int enhanceLevel;
        public int durability;
        public int maximumStack;
    }

    [Serializable]
    public sealed class PlayerTradeClaimDto
    {
        public string claimId;
        public string sourceOrderId;
        public string reason;
        public string kind;
        public string currencyId;
        public PlayerTradeAssetDto asset;
        public long originalAmount;
        public long remainingAmount;
        public string status;
        public long createdAtUnixMilliseconds;
        public long claimedAtUnixMilliseconds;
    }

    [Serializable]
    public sealed class PlayerTradeBalanceDto
    {
        public string currencyId;
        public long balance;
    }

    [Serializable]
    public sealed class PlayerTradeInventoryChangeDto
    {
        public string type;
        public long stackId;
        public string itemId;
        public int count;
        public bool isBound;
        public string itemInstanceId;
        public string equipmentId;
        public int slotCode;
        public string currencyId;
        public long balance;
    }

    public static class PlayerTradeRequestJson
    {
        [Serializable]
        private sealed class StringArrayWrapper
        {
            public string[] values;
        }

        public static string ToOrderListJson(PlayerTradeOrderListRequestDto request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            string itemIdsJson = JsonUtility.ToJson(new StringArrayWrapper
            {
                values = request.itemIds ?? new string[0]
            });
            int valuesStart = itemIdsJson.IndexOf(':') + 1;
            itemIdsJson = itemIdsJson.Substring(valuesStart,
                itemIdsJson.Length - valuesStart - 1);

            StringBuilder builder = new StringBuilder(256);
            builder.Append("{\"msgId\":").Append(Quote(request.msgId));
            builder.Append(",\"listType\":").Append(Quote(request.listType));
            builder.Append(",\"cursor\":").Append(Quote(request.cursor));
            builder.Append(",\"pageSize\":").Append(request.pageSize.ToString(CultureInfo.InvariantCulture));
            builder.Append(",\"itemIds\":").Append(itemIdsJson);
            builder.Append(",\"minimumPrice\":").Append(request.minimumPrice.HasValue
                ? request.minimumPrice.Value.ToString(CultureInfo.InvariantCulture)
                : "null");
            builder.Append(",\"maximumPrice\":").Append(request.maximumPrice.HasValue
                ? request.maximumPrice.Value.ToString(CultureInfo.InvariantCulture)
                : "null");
            builder.Append(",\"sort\":").Append(Quote(request.sort));
            builder.Append('}');
            return builder.ToString();
        }

        private static string Quote(string value)
        {
            value = value ?? string.Empty;
            StringBuilder builder = new StringBuilder(value.Length + 2);
            builder.Append('"');
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                switch (character)
                {
                    case '\\': builder.Append("\\\\"); break;
                    case '"': builder.Append("\\\""); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (character < 32)
                            builder.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            builder.Append(character);
                        break;
                }
            }
            builder.Append('"');
            return builder.ToString();
        }
    }
}
