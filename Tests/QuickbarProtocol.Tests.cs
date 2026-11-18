using System;
using System.Linq;
using System.Text.Json;
using Game.Inventory.Protocol.V2;

namespace UnityEngine
{
    public readonly struct Vector3
    {
        public Vector3(float x, float y, float z) { }
    }

    public readonly struct Quaternion
    {
        public static Quaternion Euler(float x, float y, float z) => default;
    }
}

internal static class QuickbarProtocolTests
{
    private static readonly JsonSerializerOptions Options = new() { IncludeFields = true };

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void CheckRequest(object request, string messageId, params string[] fields)
    {
        using JsonDocument json = JsonDocument.Parse(
            JsonSerializer.Serialize(request, request.GetType(), Options));
        string[] actual = json.RootElement.EnumerateObject()
            .Select(property => property.Name).OrderBy(name => name).ToArray();
        string[] expected = new[] { "msgId", "type" }
            .Concat(fields).OrderBy(name => name).ToArray();
        Check(actual.SequenceEqual(expected),
            messageId + " fields differ: " + string.Join(",", actual));
        Check(json.RootElement.GetProperty("msgId").GetString() == messageId
            && json.RootElement.GetProperty("type").GetString() == messageId,
            messageId + " message identifiers differ.");
    }

    private static void Main()
    {
        const string sessionId = "018f0000-acde-7000-8000-000000000001";
        EnterWorldResult enter = JsonSerializer.Deserialize<EnterWorldResult>(
            "{\"quickbarOperationSessionId\":\"" + sessionId
            + "\",\"itemUseOperationSessionId\":\"item-session\","
            + "\"quickbarSlotCount\":8,\"quickbarProtocolVersion\":1}", Options);
        Check(enter.quickbarOperationSessionId == sessionId
            && enter.itemUseOperationSessionId == "item-session"
            && enter.quickbarSlotCount == QuickbarProtocol.SlotCount
            && enter.quickbarProtocolVersion == QuickbarProtocol.Version,
            "Enter-world quickbar fields were not parsed independently.");

        const string startedJson = "{\"msgId\":\"skill_cast_started\","
            + "\"type\":\"skill_cast_started\",\"requestSequence\":1,"
            + "\"skillId\":\"fireball_01\",\"casterId\":\"81\","
            + "\"targetType\":\"monster\",\"targetId\":\"monster_01\","
            + "\"targetX\":177.108,\"targetY\":0,\"targetZ\":95.941,"
            + "\"hitTick\":125,\"cooldownUntilTick\":160,"
            + "\"currentMp\":90,\"maxMp\":100,"
            + "\"serverTick\":100,\"serverTime\":1791252000000}";
        SkillCastStarted started =
            JsonSerializer.Deserialize<SkillCastStarted>(startedJson, Options);
        Check(started.msgId == "skill_cast_started"
            && started.type == "skill_cast_started"
            && started.requestSequence == 1
            && started.skillId == "fireball_01"
            && started.casterId == "81"
            && started.targetType == "monster"
            && started.targetId == "monster_01"
            && Math.Abs(started.targetX - 177.108) < 0.00000001
            && started.targetY == 0d
            && Math.Abs(started.targetZ - 95.941) < 0.00000001
            && started.hitTick == 125
            && started.cooldownUntilTick == 160
            && started.currentMp == 90 && started.maxMp == 100
            && started.serverTick == 100
            && started.serverTime == 1791252000000,
            "Skill cast started fields were not parsed.");
        Check(typeof(SkillCastStarted).GetField("targetX").FieldType == typeof(double)
            && typeof(SkillCastStarted).GetField("targetY").FieldType == typeof(double)
            && typeof(SkillCastStarted).GetField("targetZ").FieldType == typeof(double)
            && typeof(SkillCastStarted).GetField("success") == null
            && typeof(SkillCastStarted).GetField("reliableMessageId") == null,
            "Skill cast started contract has an unexpected field type or ACK field.");

        var snapshot = new QuickbarSnapshotRequestDto(1);
        CheckRequest(snapshot, QuickbarProtocol.Snapshot, "requestSequence");
        Check(snapshot.requestSequence == 1, "Snapshot sequence was not stored.");

        var assign = new QuickbarAssignRequestDto(sessionId, 2, 3, 10, 1, 12345);
        CheckRequest(assign, QuickbarProtocol.Assign,
            "quickbarOperationSessionId", "requestSequence",
            "expectedQuickbarRevision", "expectedInventoryRevision",
            "slotIndex", "stackId");
        Check(assign.quickbarOperationSessionId == sessionId
            && assign.requestSequence == 2 && assign.expectedQuickbarRevision == 3
            && assign.expectedInventoryRevision == 10
            && assign.slotIndex == 1 && assign.stackId == 12345,
            "Assign payload fields differ.");

        var clear = new QuickbarClearRequestDto(sessionId, 3, 4, 10, 1);
        CheckRequest(clear, QuickbarProtocol.Clear,
            "quickbarOperationSessionId", "requestSequence",
            "expectedQuickbarRevision", "expectedInventoryRevision", "slotIndex");
        Check(clear.quickbarOperationSessionId == sessionId
            && clear.requestSequence == 3 && clear.expectedQuickbarRevision == 4
            && clear.expectedInventoryRevision == 10 && clear.slotIndex == 1,
            "Clear payload fields differ.");

        var move = new QuickbarMoveRequestDto(sessionId, 4, 5, 10, 2, 8);
        CheckRequest(move, QuickbarProtocol.Move,
            "quickbarOperationSessionId", "requestSequence",
            "expectedQuickbarRevision", "expectedInventoryRevision",
            "sourceSlotIndex", "targetSlotIndex");
        Check(move.quickbarOperationSessionId == sessionId
            && move.requestSequence == 4 && move.expectedQuickbarRevision == 5
            && move.expectedInventoryRevision == 10
            && move.sourceSlotIndex == 2 && move.targetSlotIndex == 8,
            "Move payload fields differ.");

        QuickbarSnapshotResultDto snapshotResult =
            JsonSerializer.Deserialize<QuickbarSnapshotResultDto>(
                "{\"msgId\":\"quickbar_snapshot_result\",\"success\":true,"
                + "\"characterDbId\":81,\"requestSequence\":1,"
                + "\"protocolVersion\":1,\"slotCount\":8,"
                + "\"quickbarRevision\":5,\"inventoryRevision\":10,"
                + "\"slots\":[{\"slotIndex\":1,\"stackId\":12345,"
                + "\"itemId\":\"mp_potion_01\",\"count\":2,\"isBound\":true},"
                + "{\"slotIndex\":2,\"stackId\":0}]}",
                Options);
        Check(snapshotResult.success && snapshotResult.slots.Length == 2
            && snapshotResult.slots[0].stackId == 12345
            && snapshotResult.slots[0].count == 2
            && snapshotResult.slots[0].isBound
            && snapshotResult.slots[1].stackId == 0,
            "Snapshot slot fields were not parsed.");

        QuickbarWriteResultDto writeResult =
            JsonSerializer.Deserialize<QuickbarWriteResultDto>(
                "{\"msgId\":\"quickbar_move_result\",\"success\":true,"
                + "\"quickbarOperationSessionId\":\"" + sessionId + "\","
                + "\"requestSequence\":4,\"quickbarRevision\":6,"
                + "\"inventoryRevision\":10,\"idempotentReplay\":true,"
                + "\"requiresSnapshot\":true,\"slots\":[]}", Options);
        Check(writeResult.quickbarOperationSessionId == sessionId
            && writeResult.requestSequence == 4 && writeResult.quickbarRevision == 6
            && writeResult.idempotentReplay && writeResult.requiresSnapshot
            && writeResult.slots.Length == 0,
            "Write result replay and revision fields were not parsed.");

        QuickbarSessionTests.Run();
        Console.WriteLine("Quickbar protocol and session checks passed.");
    }
}
