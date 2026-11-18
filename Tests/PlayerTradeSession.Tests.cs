using System;
using System.Collections.Generic;
using System.Text.Json;
using Game.PlayerTrade.Protocol;

public class NetMessageBase
{
    public string msgId;
    public string type;
}

namespace UnityEngine
{
    public static class JsonUtility
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            IncludeFields = true
        };

        public static string ToJson(object value) =>
            JsonSerializer.Serialize(value, value.GetType(), Options);

        public static T FromJson<T>(string value) =>
            JsonSerializer.Deserialize<T>(value, Options);
    }

    public static class PlayerPrefs
    {
        private static readonly Dictionary<string, string> Values = new();

        public static string GetString(string key, string fallback) =>
            Values.TryGetValue(key, out string value) ? value : fallback;

        public static void SetString(string key, string value) => Values[key] = value;
        public static void DeleteKey(string key) => Values.Remove(key);
        public static void Save() { }
    }

    public static class Debug
    {
        public static void LogWarning(string message) { }
        public static void LogError(string message) { }
    }
}

internal static class PlayerTradeSessionTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Main()
    {
        PlayerTradeRequestSequence.Suspend();
        var invalid = new PlayerTradeCreateRequestDto();
        Check(!PlayerTradeRequestSequence.TryPrepare(invalid),
            "A trade write without an enter-world session must be rejected.");

        string firstSession = Guid.NewGuid().ToString("D");
        PlayerTradeRequestSequence.EnterWorld(firstSession, 101);
        var create = new PlayerTradeCreateRequestDto
        {
            assetKind = "stack", stackId = 7, quantity = 2, totalPrice = 200
        };
        Check(PlayerTradeRequestSequence.TryPrepare(create)
            && create.requestSequence == 1
            && create.playerTradeOperationSessionId == firstSession
            && Guid.TryParseExact(create.clientRequestId, "D", out _),
            "Create must use the first session sequence and a client UUID.");
        Check(UnityEngine.PlayerPrefs.GetString(
                "player_trade_in_flight_101", "").Contains(create.clientRequestId),
            "The complete in-flight operation must survive disconnect or restart.");
        string original = UnityEngine.JsonUtility.ToJson(create);
        Check(!PlayerTradeRequestSequence.TryPrepare(
                new PlayerTradeCancelRequestDto { orderId = "other" })
            && original == UnityEngine.JsonUtility.ToJson(create),
            "Concurrent operations must not consume another sequence or alter a retry.");
        Check(!PlayerTradeRequestSequence.IsCurrent(create,
                firstSession, Guid.NewGuid().ToString("D"))
            && PlayerTradeRequestSequence.IsCurrent(create,
                firstSession, create.clientRequestId),
            "A response with a different client ID must not complete the request.");
        PlayerTradeRequestSequence.Complete(create, firstSession,
            create.clientRequestId);
        PlayerTradeRequestSequence.EnterWorld(firstSession, 101);

        var buy = new PlayerTradeBuyRequestDto(0, "order-1");
        Check(PlayerTradeRequestSequence.TryPrepare(buy)
            && buy.requestSequence == 2
            && buy.clientRequestId != create.clientRequestId,
            "New operation types share the sequence and get fresh IDs.");
        PlayerTradeRequestSequence.Suspend();
        string secondSession = Guid.NewGuid().ToString("D");
        PlayerTradeRequestSequence.EnterWorld(secondSession, 101);
        Check(!PlayerTradeRequestSequence.TryPrepare(
                new PlayerTradeClaimRequestDto { claimId = "claim-1" }),
            "An unknown result must survive a new session and block new writes.");
        Check(PlayerTradeRequestSequence.ConfirmNotAppliedAfterReconciliation(
                buy.clientRequestId),
            "A verified external reconciliation must unlock the saved operation.");
        var cancel = new PlayerTradeCancelRequestDto { orderId = "order-1" };
        Check(PlayerTradeRequestSequence.TryPrepare(cancel)
            && cancel.requestSequence == 1
            && cancel.playerTradeOperationSessionId == secondSession,
            "The next confirmed new session must start at sequence one.");
        PlayerTradeRequestSequence.Complete(cancel, secondSession,
            cancel.clientRequestId);
        var claim = new PlayerTradeClaimRequestDto { claimId = "claim-1" };
        Check(PlayerTradeRequestSequence.TryPrepare(claim)
            && claim.requestSequence == 2
            && claim.clientRequestId != cancel.clientRequestId,
            "Claim uses the same sequence as create, buy and cancel.");
        PlayerTradeRequestSequence.Complete(claim, secondSession,
            claim.clientRequestId);
        Console.WriteLine("PASS: trade session, shared sequence, retry identity and unknown-result recovery.");
    }
}
