using System;
using System.Collections.Generic;
using System.Text.Json;
using Game.Inventory.Integration.V2;
using Game.Inventory.Model.V2;
using Game.Inventory.Protocol.V2;

public sealed class NetworkManager
{
    public bool IsConnected = true;
    public readonly List<string> Sent = new();
    public bool Send<T>(T request)
    {
        if (!IsConnected) return false;
        Sent.Add(UnityEngine.JsonUtility.ToJson(request));
        return true;
    }
}

public sealed class NetMessageDispatcher
{
    private readonly Dictionary<string, Action<string>> handlers = new();

    public void Register(string id, Action<string> handler)
    {
        handlers.TryGetValue(id, out Action<string> previous);
        handlers[id] = previous + handler;
    }

    public void Unregister(string id, Action<string> handler)
    {
        if (handlers.TryGetValue(id, out Action<string> previous))
            handlers[id] = previous - handler;
    }

    public void Dispatch(object message)
    {
        string json = UnityEngine.JsonUtility.ToJson(message);
        string id = JsonDocument.Parse(json).RootElement.GetProperty("msgId").GetString();
        if (handlers.TryGetValue(id, out Action<string> handler)) handler?.Invoke(json);
    }
}

namespace UnityEngine
{
    public static class Time
    {
        public static float unscaledTime;
    }

    public static class Debug
    {
        public static void LogWarning(string message) { }
    }

    public static class JsonUtility
    {
        private static readonly JsonSerializerOptions Options = new() { IncludeFields = true };
        public static string ToJson(object value) =>
            JsonSerializer.Serialize(value, value.GetType(), Options);
        public static T FromJson<T>(string json) =>
            JsonSerializer.Deserialize<T>(json, Options);
    }
}

namespace Game.Items
{
    public enum ItemCategory { Other, Consumable, Equipment, SkillBook }

    public sealed class ItemDefinition
    {
        public ItemCategory Category;
    }

    public sealed class ItemDefinitionTable
    {
        public bool TryGet(string id, out ItemDefinition definition)
        {
            definition = id == "mp_potion_01"
                ? new ItemDefinition { Category = ItemCategory.Consumable }
                : id == "skill_book_01"
                    ? new ItemDefinition { Category = ItemCategory.SkillBook }
                    : null;
            return definition != null;
        }
    }
}

internal static class QuickbarSessionTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static InventoryOwnershipSnapshot Inventory(long revision, int count)
    {
        var stacks = new List<InventoryStackState>
        {
            new InventoryStackState(12345, "mp_potion_01", count, false),
            new InventoryStackState(54321, "skill_book_01", 1, false)
        };
        return new InventoryOwnershipSnapshot(81, revision, 0, 40, 1, 1, stacks,
            new List<InventoryEquipmentState>(), new Dictionary<string, long>(),
            new InventoryAttributeBonusState(0, 0, 0));
    }

    private static QuickbarSlotDto[] Slots(int first, int second, int count)
    {
        var slots = new QuickbarSlotDto[QuickbarProtocol.SlotCount];
        for (int index = 0; index < slots.Length; index++)
            slots[index] = new QuickbarSlotDto { slotIndex = index + 1 };
        if (first > 0)
        {
            slots[first - 1].stackId = 12345;
            slots[first - 1].itemId = "mp_potion_01";
            slots[first - 1].count = count;
        }
        if (second > 0)
        {
            slots[second - 1].stackId = 12345;
            slots[second - 1].itemId = "mp_potion_01";
            slots[second - 1].count = count;
        }
        return slots;
    }

    private static long Sequence(string json) =>
        JsonDocument.Parse(json).RootElement.GetProperty("requestSequence").GetInt64();

    internal static void Run()
    {
        const string sessionId = "018f0000-acde-7000-8000-000000000001";
        var network = new NetworkManager();
        var dispatcher = new NetMessageDispatcher();
        InventoryOwnershipSnapshot inventory = null;
        int refreshes = 0;
        UnityEngine.Time.unscaledTime = 0;
        var session = new QuickbarSession(network, dispatcher, () => inventory,
            () => refreshes++, new Game.Items.ItemDefinitionTable());
        session.BeginSession(81, sessionId, QuickbarProtocol.Version, QuickbarProtocol.SlotCount);
        session.Tick();
        Check(network.Sent.Count == 0, "Quickbar requested before inventory was available.");
        inventory = Inventory(10, 2);
        session.Tick();
        Check(network.Sent.Count == 1
            && network.Sent[0].Contains("\"msgId\":\"quickbar_snapshot\"")
            && Sequence(network.Sent[0]) == 1,
            "Initial quickbar snapshot was not requested.");

        dispatcher.Dispatch(new QuickbarSnapshotResultDto
        {
            msgId = QuickbarProtocol.SnapshotResult,
            characterDbId = 81, requestSequence = 1, success = true,
            protocolVersion = 1, slotCount = 8, quickbarRevision = 0,
            inventoryRevision = 10, slots = Slots(1, 0, 2)
        });
        Check(session.IsReady && session.GetSlot(1).stackId == 12345
            && session.GetSlot(1).count == 2,
            "Initial quickbar layout was not applied.");
        Check(!session.TryAssign(2, 54321),
            "A skill book was accepted by the consumable quickbar.");

        Check(session.TryAssign(2, 12345) && session.TryClear(1)
            && session.TryMove(2, 3), "Write intent was rejected.");
        UnityEngine.Time.unscaledTime = 0.2f;
        session.Tick();
        Check(network.Sent.Count == 2
            && network.Sent[1].Contains("\"msgId\":\"quickbar_assign\"")
            && Sequence(network.Sent[1]) == 1,
            "Write requests were not queued behind one in-flight operation.");
        UnityEngine.Time.unscaledTime = 10.3f;
        session.Tick();
        Check(network.Sent.Count == 3 && network.Sent[1] == network.Sent[2],
            "Timeout retry changed the original request.");
        dispatcher.Dispatch(new QuickbarWriteResultDto
        {
            msgId = QuickbarProtocol.AssignResult, characterDbId = 81,
            quickbarOperationSessionId = "previous-session", requestSequence = 1,
            success = true, protocolVersion = 1, slotCount = 8,
            quickbarRevision = 1, inventoryRevision = 10, slots = Slots(1, 2, 2)
        });
        Check(session.QuickbarRevision == 0, "Stale session response was accepted.");
        dispatcher.Dispatch(new QuickbarWriteResultDto
        {
            msgId = QuickbarProtocol.AssignResult, characterDbId = 81,
            quickbarOperationSessionId = sessionId, requestSequence = 1,
            success = true, protocolVersion = 1, slotCount = 8,
            quickbarRevision = 1, inventoryRevision = 10, slots = Slots(1, 2, 2)
        });
        Check(session.QuickbarRevision == 1 && session.GetSlot(2).stackId == 12345,
            "Authoritative write result did not replace the layout.");
        UnityEngine.Time.unscaledTime = 10.5f;
        session.Tick();
        Check(network.Sent.Count == 4
            && network.Sent[3].Contains("\"msgId\":\"quickbar_clear\"")
            && Sequence(network.Sent[3]) == 2,
            "Queued clear did not reuse the independent write sequence.");
        dispatcher.Dispatch(new QuickbarWriteResultDto
        {
            msgId = QuickbarProtocol.ClearResult, characterDbId = 81,
            quickbarOperationSessionId = sessionId, requestSequence = 2,
            success = true, protocolVersion = 1, slotCount = 8,
            quickbarRevision = 2, inventoryRevision = 10, slots = Slots(2, 0, 2)
        });
        UnityEngine.Time.unscaledTime = 10.7f;
        session.Tick();
        Check(network.Sent.Count == 5
            && network.Sent[4].Contains("\"msgId\":\"quickbar_move\"")
            && Sequence(network.Sent[4]) == 3,
            "Queued move was not sent after clear completed.");
        dispatcher.Dispatch(new QuickbarWriteResultDto
        {
            msgId = QuickbarProtocol.MoveResult, characterDbId = 81,
            quickbarOperationSessionId = sessionId, requestSequence = 3,
            success = false, requiresSnapshot = true, errorCode = "quickbar_revision_conflict",
            slots = Array.Empty<QuickbarSlotDto>()
        });
        Check(session.GetSlot(2).stackId == 12345,
            "Rejected empty slots erased the authoritative layout.");
        UnityEngine.Time.unscaledTime = 11f;
        session.Tick();
        Check(network.Sent.Count == 6
            && network.Sent[5].Contains("\"msgId\":\"quickbar_snapshot\"")
            && Sequence(network.Sent[5]) == 2,
            "Revision conflict did not request a new quickbar snapshot.");

        dispatcher.Dispatch(new QuickbarSnapshotResultDto
        {
            msgId = QuickbarProtocol.SnapshotResult,
            characterDbId = 81, requestSequence = 2, success = true,
            protocolVersion = 1, slotCount = 8, quickbarRevision = 3,
            inventoryRevision = 11, slots = Slots(2, 0, 1)
        });
        Check(!session.IsReady && refreshes == 1,
            "Quickbar newer than inventory did not request inventory recovery.");
        inventory = Inventory(11, 1);
        session.Tick();
        Check(session.IsReady && session.GetSlot(2).count == 1,
            "Quickbar did not project a recovered inventory snapshot.");
        inventory = Inventory(12, 0);
        session.Tick();
        Check(session.IsReady && session.GetSlot(2).stackId == 0
            && session.GetSlot(2).count == 0,
            "Quickbar kept a binding after its stack was exhausted.");
        session.Reset();
        Check(!session.IsReady && session.GetSlot(2) == null,
            "Old character quickbar survived reset.");
        dispatcher.Dispatch(new QuickbarSnapshotResultDto
        {
            msgId = QuickbarProtocol.SnapshotResult, characterDbId = 81,
            requestSequence = 2, success = true, slots = Slots(1, 0, 2)
        });
        Check(!session.IsReady, "Late old-session snapshot was applied after reset.");
        session.Dispose();

        TestRateLimitAndInvalidLayout();
        TestStoneCharges();
        TestAuthoritativeUseEligibility();
    }

    private static void TestAuthoritativeUseEligibility()
    {
        const string sessionId = "018f0000-acde-7000-8000-000000000003";
        var network = new NetworkManager();
        var dispatcher = new NetMessageDispatcher();
        InventoryOwnershipSnapshot inventory = Inventory(10, 2);
        bool usable = false;
        UnityEngine.Time.unscaledTime = 30f;
        var session = new QuickbarSession(network, dispatcher, () => inventory,
            () => { }, new Game.Items.ItemDefinitionTable(),
            itemId => usable && itemId == "mp_potion_01");
        session.BeginSession(81, sessionId, 1, 8);
        session.Tick();
        dispatcher.Dispatch(new QuickbarSnapshotResultDto
        {
            msgId = QuickbarProtocol.SnapshotResult,
            characterDbId = 81, requestSequence = 1, success = true,
            protocolVersion = 1, slotCount = 8, quickbarRevision = 0,
            inventoryRevision = 10, slots = Slots(0, 0, 0)
        });
        Check(!session.TryAssign(1, 12345) && !session.TryAssign(1, 54321),
            "Quickbar accepted an item rejected by authoritative use rules.");
        usable = true;
        Check(session.TryAssign(1, 12345),
            "Quickbar did not allow an item authorized for use.");
        session.Dispose();
    }

    private static void TestStoneCharges()
    {
        var network = new NetworkManager();
        var dispatcher = new NetMessageDispatcher();
        const string itemId = "suiji_stone_01";
        static InventoryOwnershipSnapshot StoneInventory(long revision, int charges) =>
            new InventoryOwnershipSnapshot(81, revision, 0, 40, 1, 1,
                charges < 0 ? new List<InventoryStackState>() :
                    new List<InventoryStackState> { new InventoryStackState(12345, itemId, 1, false, charges) },
                new List<InventoryEquipmentState>(), new Dictionary<string, long>(),
                new InventoryAttributeBonusState(0, 0, 0));
        InventoryOwnershipSnapshot inventory = StoneInventory(10, 0);
        var session = new QuickbarSession(network, dispatcher, () => inventory,
            () => { }, new Game.Items.ItemDefinitionTable());
        session.BeginSession(81, "018f0000-acde-7000-8000-000000000003", 1, 8);
        session.Tick();
        QuickbarSlotDto[] slots = Slots(4, 0, 1);
        slots[3].itemId = itemId;
        dispatcher.Dispatch(new QuickbarSnapshotResultDto
        {
            msgId = QuickbarProtocol.SnapshotResult,
            characterDbId = 81, requestSequence = 1, success = true,
            protocolVersion = 1, slotCount = 8, quickbarRevision = 0,
            inventoryRevision = 10, slots = slots
        });
        Check(session.GetSlot(4).count == 50, "A fresh stone must show 50 uses on the quickbar.");
        inventory = StoneInventory(11, 49);
        session.Tick();
        Check(session.GetSlot(4).count == 49, "The quickbar must read the changed stone charge.");
        inventory = StoneInventory(12, 1);
        session.Tick();
        Check(session.GetSlot(4).count == 1, "The last use must stay in the quickbar.");
        inventory = StoneInventory(13, -1);
        session.Tick();
        Check(session.GetSlot(4).stackId == 0, "The quickbar must clear the exhausted stone.");
        session.Dispose();
    }

    private static void TestRateLimitAndInvalidLayout()
    {
        const string sessionId = "018f0000-acde-7000-8000-000000000002";
        var network = new NetworkManager();
        var dispatcher = new NetMessageDispatcher();
        InventoryOwnershipSnapshot inventory = Inventory(10, 2);
        UnityEngine.Time.unscaledTime = 20f;
        var session = new QuickbarSession(network, dispatcher, () => inventory,
            () => { }, new Game.Items.ItemDefinitionTable());
        session.BeginSession(81, sessionId, 1, 8);
        session.Tick();
        dispatcher.Dispatch(new QuickbarSnapshotResultDto
        {
            msgId = QuickbarProtocol.SnapshotResult,
            characterDbId = 81, requestSequence = 1, success = true,
            protocolVersion = 1, slotCount = 8, quickbarRevision = 0,
            inventoryRevision = 10, slots = Slots(1, 0, 2)
        });
        Check(session.TryAssign(2, 12345), "Assign could not be queued.");
        UnityEngine.Time.unscaledTime = 20.2f;
        session.Tick();
        dispatcher.Dispatch(new QuickbarWriteResultDto
        {
            msgId = QuickbarProtocol.AssignResult, characterDbId = 81,
            quickbarOperationSessionId = sessionId, requestSequence = 1,
            errorCode = "quickbar_rate_limited", success = false
        });
        UnityEngine.Time.unscaledTime = 21.2f;
        session.Tick();
        Check(network.Sent.Count == 3 && network.Sent[1] == network.Sent[2],
            "Rate limit retry did not keep the same request and sequence.");
        dispatcher.Dispatch(new QuickbarWriteResultDto
        {
            msgId = QuickbarProtocol.AssignResult, characterDbId = 81,
            quickbarOperationSessionId = sessionId, requestSequence = 1,
            success = true, protocolVersion = 1, slotCount = 8,
            quickbarRevision = 1, inventoryRevision = 10,
            slots = Array.Empty<QuickbarSlotDto>()
        });
        Check(session.GetSlot(1).stackId == 12345,
            "Invalid success payload erased the previous authoritative layout.");
        UnityEngine.Time.unscaledTime = 21.8f;
        session.Tick();
        Check(network.Sent.Count == 4
            && network.Sent[3].Contains("\"msgId\":\"quickbar_snapshot\""),
            "Invalid success payload did not request a fresh snapshot.");
        dispatcher.Dispatch(new QuickbarSnapshotResultDto
        {
            msgId = QuickbarProtocol.SnapshotResult,
            characterDbId = 81, requestSequence = 2, success = true,
            protocolVersion = 1, slotCount = 8, quickbarRevision = 1,
            inventoryRevision = 10, slots = Slots(1, 0, 2)
        });
        Check(session.TryAssign(2, 12345), "Assign after snapshot was rejected.");
        UnityEngine.Time.unscaledTime = 22f;
        session.Tick();
        dispatcher.Dispatch(new QuickbarWriteResultDto
        {
            msgId = QuickbarProtocol.AssignResult, characterDbId = 81,
            quickbarOperationSessionId = sessionId, requestSequence = 2,
            success = false, errorCode = "quickbar_operation_session_invalid"
        });
        Check(!session.IsReady && !session.TryAssign(2, 12345),
            "Invalid quickbar session continued to accept writes.");
        session.Dispose();
    }
}
