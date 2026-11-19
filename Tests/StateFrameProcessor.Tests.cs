using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Game.Client.BattleSync;
using Game.Client.BattleSync.Protocol;
using UnityEngine;

namespace UnityEngine
{
    internal static class Debug
    {
        internal static readonly List<string> Warnings = new List<string>();
        public static void LogWarning(string text) { Warnings.Add(text); }
    }
}

namespace Game.Client.BattleSync
{
    public interface IBattleSyncJsonCodec
    {
        bool TryDeserialize<T>(string json, out T message, out string error) where T : class;
        bool TrySerialize<T>(T message, out string json, out string error) where T : class;
    }

    public interface IBattleSyncClock { long NowMilliseconds { get; } }
    public interface IBattleSyncTransport
    {
        bool IsConnected { get; }
        long ConnectionEpoch { get; }
        bool SendJson(string json);
    }
    public interface IPlayerBattleSyncModule
    {
        BattleSyncApplyResult ApplyPlayerDynamicState(DecodedPlayerState state, long tick, long time);
    }
    public interface IMonsterBattleSyncModule
    {
        BattleSyncApplyResult ApplyMonsterDynamicState(DecodedMonsterState state, long tick, long time);
    }
}

internal sealed class TestClock : IBattleSyncClock
{
    public long Current = 1000;
    public long NowMilliseconds => Current;
}

internal sealed class TestTransport : IBattleSyncTransport
{
    public bool IsConnected => true;
    public long ConnectionEpoch => 1;
    public readonly List<string> Sent = new List<string>();
    public bool SendJson(string json) { Sent.Add(json); return true; }
}

internal sealed class TestCodec : IBattleSyncJsonCodec
{
    public bool TryDeserialize<T>(string json, out T message, out string error) where T : class
    {
        message = null;
        error = "Not used for state frames.";
        return false;
    }
    public bool TrySerialize<T>(T message, out string json, out string error) where T : class
    {
        json = JsonSerializer.Serialize(message, new JsonSerializerOptions { IncludeFields = true });
        error = string.Empty;
        return true;
    }
}

internal sealed class TestPlayers : IPlayerBattleSyncModule
{
    public int Calls;
    public DecodedPlayerState Last;
    public BattleSyncApplyResult Next = BattleSyncApplyResult.Applied;
    public RemotePlayerSync View;
    public BattleSyncApplyResult ApplyPlayerDynamicState(DecodedPlayerState state, long tick, long time)
    {
        Calls++;
        Last = state;
        if (Next != BattleSyncApplyResult.Applied) return Next;
        return View == null || View.ApplyDynamicState(state, tick)
            ? BattleSyncApplyResult.Applied : BattleSyncApplyResult.IgnoredStale;
    }
}

internal sealed class TestMonsters : IMonsterBattleSyncModule
{
    public int Calls;
    public DecodedMonsterState Last;
    public BattleSyncApplyResult Next = BattleSyncApplyResult.Applied;
    public CombatStateStore Store;
    public BattleSyncApplyResult ApplyMonsterDynamicState(DecodedMonsterState state, long tick, long time)
    {
        Calls++;
        Last = state;
        if (Next == BattleSyncApplyResult.Applied && Store != null
            && state.HasHp && state.HasCombatRevision)
            Store.MergeDynamicMonsterHealth("world_001", state.EntityId, state.Hp,
                state.CombatRevision, tick, 1);
        return Next;
    }
}

internal sealed class StateFrameHarness
{
    public readonly BattleEntityRegistry Registry = new BattleEntityRegistry();
    public readonly TestClock Clock = new TestClock();
    public readonly TestTransport Transport = new TestTransport();
    public readonly TestCodec Codec = new TestCodec();
    public readonly TestPlayers Players = new TestPlayers();
    public readonly TestMonsters Monsters = new TestMonsters();
    public readonly SnapshotRepairService Repair;
    public readonly StateFrameProcessor Processor;

    public StateFrameHarness(bool registerEntities = true, bool diagnosticsEnabled = true)
    {
        UnityEngine.Debug.Warnings.Clear();
        Registry.BeginSession(1, "world_001");
        Repair = new SnapshotRepairService(Transport, Codec, Clock);
        Repair.BeginSession(1, "world_001", 1);
        Processor = new StateFrameProcessor(Codec, new CompactStateDecoder(),
            Registry, Players, Monsters, Repair, Clock, diagnosticsEnabled);
        Processor.BeginSession(1, "world_001");
        if (registerEntities) Register();
    }

    public void Register()
    {
        Register(BattleEntityKind.Player, "1001", 101, 5);
        Register(BattleEntityKind.Monster, "monster-501", 501, 3);
    }

    public void Register(BattleEntityKind kind, string id, int netId, long visibility, long epoch = 1)
    {
        BattleEntityRegistryResult result = Registry.RegisterOrUpdate(
            new BattleEntityRegistration(epoch, "world_001", kind, id, netId, visibility, 100,
                kind == BattleEntityKind.Monster ? 1 : 0), Clock.NowMilliseconds);
        if (!result.IsSuccess) throw new Exception("Test registration failed: " + result.Status);
    }

    public long Tick(int netId)
    {
        if (!Registry.TryGetByNetId(netId, out BattleEntityRegistryEntry entry))
            throw new Exception("Test entity not registered: " + netId);
        return entry.LastStateServerTick;
    }
}

internal static class StateFrameProcessorTests
{
    private const string Player = "[101,19820,3000,9510,16565,10,0,0,1,0,15,729,5]";
    private const string Monster = "[501,21050,2967,7600,32768,3,85,80,3,3]";

    private static void Main()
    {
        RejectMalformedTailBeforeApplying();
        ApplyAndAdvanceSequence();
        RejectRepeatedAndSkippedSequences();
        RepairUnknownEntitiesWithoutCreatingThem();
        PreserveTickWhenModuleFails();
        RespectSceneEpochAndRevision();
        ApplyArraysToPlayerPresentation();
        PreserveNewerMovementEventOverPositionFrame();
        KeepNewerMonsterHealthWhenOldArrayArrives();
        Console.WriteLine("State frame processor tests passed.");
    }

    private static void RejectMalformedTailBeforeApplying()
    {
        StateFrameHarness harness = new StateFrameHarness();
        string invalid = Frame(101, 1, Player, Monster.Replace(",3,85,", ",3.5,85,"));
        Check(harness.Processor.Process(1, invalid) == BattleSyncApplyResult.InvalidMessage
            && harness.Players.Calls == 0 && harness.Monsters.Calls == 0
            && harness.Tick(101) == 100 && harness.Tick(501) == 100,
            "Malformed tail partially applied the frame.");
        Check(UnityEngine.Debug.Warnings.Count == 1
            && UnityEngine.Debug.Warnings[0].Contains("sceneId=world_001")
            && UnityEngine.Debug.Warnings[0].Contains("frameSequence=1")
            && UnityEngine.Debug.Warnings[0].Contains("field=monsters[0][5]"),
            "Malformed frame diagnostic omitted context.");
        Check(harness.Processor.Process(1, invalid) == BattleSyncApplyResult.InvalidMessage
            && UnityEngine.Debug.Warnings.Count == 1, "Repeated format errors were not throttled.");
        harness.Clock.Current += 1000;
        harness.Processor.Process(1, invalid);
        Check(UnityEngine.Debug.Warnings.Count == 2, "Format diagnostic remained suppressed indefinitely.");
        Check(harness.Processor.Process(1, Frame(101, 1)) == BattleSyncApplyResult.Applied
            && harness.Players.Calls == 1 && harness.Monsters.Calls == 1,
            "A malformed frame consumed its sequence.");
        string legacy = Frame(102, 2, "{\"netId\":101}", "");
        Check(harness.Processor.Process(1, legacy) == BattleSyncApplyResult.InvalidMessage
            && harness.Tick(101) == 101 && harness.Players.Calls == 1,
            "Old object-form state was accepted.");
        StateFrameHarness defaultDiagnostics = new StateFrameHarness(diagnosticsEnabled: false);
        Check(defaultDiagnostics.Processor.Process(1, invalid) == BattleSyncApplyResult.InvalidMessage
            && UnityEngine.Debug.Warnings.Count == 1,
            "Malformed frame was silent when optional detailed diagnostics were off.");
    }

    private static void ApplyAndAdvanceSequence()
    {
        StateFrameHarness harness = new StateFrameHarness();
        Check(harness.Processor.Process(1, Frame(101, 1)) == BattleSyncApplyResult.Applied
            && harness.Tick(101) == 101 && harness.Tick(501) == 101
            && harness.Players.Last.EntityId == "1001"
            && harness.Monsters.Last.EntityId == "monster-501"
            && Math.Abs(harness.Players.Last.X - 198.2f) < 0.001f
            && Math.Abs(harness.Monsters.Last.Y - 29.67f) < 0.001f,
            "Named decoded states did not reach existing modules.");
        Check(harness.Processor.Process(1, Frame(102, 2,
            Player.Replace(",10,0,0,1,0,", ",0,0,0,0,0,"), "")) == BattleSyncApplyResult.Applied
            && harness.Players.Last.HasVelocity && harness.Players.Last.VelocityX == 0
            && harness.Players.Last.HasMovementState && harness.Players.Last.MovementState == 0,
            "Stopped player did not retain valid zero fields.");
    }

    private static void RejectRepeatedAndSkippedSequences()
    {
        StateFrameHarness harness = new StateFrameHarness();
        harness.Processor.Process(1, Frame(101, 1));
        Check(harness.Processor.Process(1, Frame(102, 1)) == BattleSyncApplyResult.IgnoredStale
            && harness.Processor.Process(1, Frame(102, 0)) == BattleSyncApplyResult.InvalidMessage
            && harness.Players.Calls == 1, "Repeated frame sequence applied again.");
        Check(harness.Processor.Process(1, Frame(102, 5, "", "")) == BattleSyncApplyResult.IgnoredStale
            && harness.Processor.Process(1, Frame(103, 5)) == BattleSyncApplyResult.IgnoredStale
            && harness.Players.Calls == 1 && harness.Registry.ActiveEntityCount == 2
            && !harness.Repair.HasPendingRepair,
            "A valid empty frame must advance the sequence, without treating gaps as errors.");
        Check(harness.Processor.Process(1, Frame(103, 6)) == BattleSyncApplyResult.Applied
            && harness.Players.Calls == 2, "A frame following a gap was rejected.");
    }

    private static void RepairUnknownEntitiesWithoutCreatingThem()
    {
        StateFrameHarness harness = new StateFrameHarness(false);
        Check(harness.Processor.Process(1, Frame(101, 1, Player, "")) == BattleSyncApplyResult.IgnoredStale
            && harness.Repair.HasPendingRepair && harness.Registry.ActiveEntityCount == 0
            && harness.Players.Calls == 0, "Unknown netId was not skipped and queued for repair.");
        harness.Repair.Update();
        Check(harness.Transport.Sent.Count == 1
            && harness.Transport.Sent[0].Contains("client_unknown_entity_state"),
            "Unknown netId did not use the established repair request reason.");
        harness.Register(BattleEntityKind.Player, "1001", 101, 5);
        Check(harness.Processor.Process(1, Frame(101, 1, Player, "")) == BattleSyncApplyResult.IgnoredStale
            && harness.Processor.Process(1, Frame(101, 2, Player, "")) == BattleSyncApplyResult.Applied,
            "Valid unknown-entity frame failed to advance the sequence.");
    }

    private static void PreserveTickWhenModuleFails()
    {
        StateFrameHarness harness = new StateFrameHarness();
        harness.Players.Next = BattleSyncApplyResult.RetryableFailure;
        Check(harness.Processor.Process(1, Frame(101, 1, Player, "")) == BattleSyncApplyResult.IgnoredStale
            && harness.Tick(101) == 100 && harness.Repair.HasPendingRepair,
            "Failed application advanced entity Tick or did not request repair.");
        harness.Players.Next = BattleSyncApplyResult.Applied;
        Check(harness.Processor.Process(1, Frame(101, 2, Player, "")) == BattleSyncApplyResult.Applied
            && harness.Tick(101) == 101, "Later valid frame with the same Tick should be applicable.");
        harness.Monsters.Next = BattleSyncApplyResult.InvalidMessage;
        Check(harness.Processor.Process(1, Frame(102, 3, "", Monster)) == BattleSyncApplyResult.IgnoredStale
            && harness.Tick(501) == 100, "Invalid module result advanced monster Tick.");
    }

    private static void RespectSceneEpochAndRevision()
    {
        StateFrameHarness harness = new StateFrameHarness();
        Check(harness.Processor.Process(2, Frame(101, 1)) == BattleSyncApplyResult.IgnoredWrongScene
            && harness.Processor.Process(1, Frame(101, 1, Player, Monster, "other_scene"))
                == BattleSyncApplyResult.IgnoredWrongScene
            && harness.Players.Calls == 0, "Wrong epoch or scene was processed.");
        string staleVisibility = Player.Replace(",729,5]", ",729,4]");
        Check(harness.Processor.Process(1, Frame(101, 1, staleVisibility, "")) == BattleSyncApplyResult.IgnoredStale
            && harness.Tick(101) == 100 && harness.Players.Calls == 0,
            "Older visibility revision was applied.");
        Check(harness.Processor.Process(1, Frame(101, 2, Player, "")) == BattleSyncApplyResult.Applied,
            "State after stale visibility frame was rejected.");
        Check(harness.Processor.Process(1, Frame(101, 3, Player, "")) == BattleSyncApplyResult.IgnoredStale
            && harness.Tick(101) == 101, "Duplicate entity Tick was applied.");
        harness.Processor.EndSession(1);
        harness.Registry.EndSession(1);
        harness.Registry.BeginSession(2, "world_001");
        harness.Processor.BeginSession(2, "world_001");
        Check(harness.Processor.Process(2, Frame(102, 1, Player, "")) == BattleSyncApplyResult.IgnoredStale
            && harness.Registry.ActiveEntityCount == 0, "Reconnection reused old netId mapping or frame sequence.");
        harness.Register(BattleEntityKind.Player, "2002", 201, 5, 2);
        Check(harness.Processor.Process(2, Frame(102, 2, Player.Replace("[101,", "[201,"), ""))
            == BattleSyncApplyResult.Applied && harness.Players.Last.EntityId == "2002",
            "Reconnection did not resolve the new netId mapping.");
    }

    private static void ApplyArraysToPlayerPresentation()
    {
        StateFrameHarness harness = new StateFrameHarness();
        RemotePlayerSync view = new RemotePlayerSync();
        Animator animator = new Animator();
        typeof(RemotePlayerSync).GetField("animator", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(view, animator);
        typeof(RemotePlayerSync).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(view, null);
        view.Init(1001, "player-1001", "Remote");
        harness.Players.View = view;

        Check(harness.Processor.Process(1, Frame(101, 1, Player, "")) == BattleSyncApplyResult.Applied
            && animator.GetBool("IsMoving"), "Array move did not reach remote player animation.");
        typeof(RemotePlayerSync).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(view, null);
        Check(
            Math.Abs(view.transform.position.x - 198.2f) < 0.001f
            && Math.Abs(view.transform.position.y - 30f) < 0.001f,
            "Array move did not reach remote player position.");

        string stopped = Player.Replace(",10,0,0,1,0,", ",0,0,0,0,0,");
        Check(harness.Processor.Process(1, Frame(102, 2, stopped, "")) == BattleSyncApplyResult.Applied
            && !animator.GetBool("IsMoving") && harness.Players.Last.HasVelocity
            && harness.Players.Last.VelocityX == 0, "Zero-valued stop did not reach player animation.");

        string noPosition = Player.Replace(",15,729,", ",6,729,")
            .Replace(",10,0,0,1,0,", ",0,0,0,0,0,")
            .Replace(",19820,3000,9510,", ",-1200,3000,9510,");
        Check(harness.Processor.Process(1, Frame(103, 3, noPosition, "")) == BattleSyncApplyResult.Applied
            && !harness.Players.Last.HasPosition,
            "Unflagged position was treated as valid.");
        typeof(RemotePlayerSync).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(view, null);
        Check(Math.Abs(view.transform.position.x - 198.2f) < 0.001f,
            "Unflagged position overwrote remote player transform.");

        string negative = Player.Replace(",19820,3000,9510,16565,10,0,0,",
            ",-1200,3000,-9510,65535,-10,0,-20,");
        Check(harness.Processor.Process(1, Frame(104, 4, negative, "")) == BattleSyncApplyResult.Applied
            && Math.Abs(harness.Players.Last.X + 12f) < 0.001f
            && Math.Abs(harness.Players.Last.Z + 95.1f) < 0.001f
            && Math.Abs(harness.Players.Last.VelocityX + 0.1f) < 0.001f
            && Math.Abs(harness.Players.Last.VelocityZ + 0.2f) < 0.001f,
            "Signed coordinates or velocities changed on the way to the player view.");
        typeof(RemotePlayerSync).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(view, null);
        string wrapped = negative.Replace(",65535,", ",0,");
        Check(harness.Processor.Process(1, Frame(105, 5, wrapped, "")) == BattleSyncApplyResult.Applied,
            "Wrapped yaw frame rejected.");
        typeof(RemotePlayerSync).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(view, null);
        Check(Quaternion.LastSlerpDistance < 1f && !float.IsNaN(view.transform.rotation.Yaw),
            "Yaw near 360/0 took the long turn in the presentation harness.");
    }

    private static void PreserveNewerMovementEventOverPositionFrame()
    {
        var harness = new StateFrameHarness();
        var view = new RemotePlayerSync();
        view.Init(1001, "player-1001", "Remote");
        harness.Players.View = view;
        Check(view.ApplyServerState(new PlayerNetworkState
        {
            characterDbId = 1001, visibilityRevision = 8,
            movement = Movement(1, 10f, 2f)
        }) && view.VisibilityRevision == 8, "Movement must seed from the visible player snapshot.");
        Check(view.ApplyMovementEvent(Movement(2, 20f, 0f)), "New stop must apply.");
        Check(!view.ApplyMovementEvent(Movement(1, 7f, 2f)), "Old movement event changed the pose.");
        Check(harness.Processor.Process(1, Frame(101, 1, Player, "")) == BattleSyncApplyResult.Applied,
            "Generic state frame should still be processed for battle data.");
        typeof(RemotePlayerSync).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)
            .Invoke(view, null);
        Check(view.transform.position.x == 20f, "Older position frame overrode an accepted stop.");

        static RemoteMovementSnapshot Movement(long revision, float x, float speed) => new RemoteMovementSnapshot
        {
            epoch = 1, revision = revision, mode = "normal",
            info = new MovementEventInfo { x = x, velocityX = speed, flags = MovementEventFlags.Walking }
        };
    }

    private static void KeepNewerMonsterHealthWhenOldArrayArrives()
    {
        StateFrameHarness harness = new StateFrameHarness();
        var store = new CombatStateStore();
        store.BeginScene("world_001");
        Check(store.MergeMonsterSpawnState("world_001", "monster-501",
            100, 100, "Alive", 1, 1, 100), "Monster seed failed.");
        harness.Monsters.Store = store;
        Check(harness.Processor.Process(1, Frame(101, 1, "", Monster)) == BattleSyncApplyResult.Applied
            && store.TryGetState("monster", "monster-501", out CombatStateData state)
            && state.Hp == 80 && state.CombatRevision == 3
            && Math.Abs(harness.Monsters.Last.Y - 29.67f) < 0.001f,
            "Array monster height or current HP did not reach its consumer.");
        string oldHealth = Monster.Replace(",80,3,3]", ",10,2,3]");
        Check(harness.Processor.Process(1, Frame(102, 2, "", oldHealth)) == BattleSyncApplyResult.Applied
            && store.TryGetState("monster", "monster-501", out state)
            && state.Hp == 80 && state.CombatRevision == 3
            && harness.Tick(501) == 102, "Old combat revision rolled back HP or blocked newer position Tick.");

        string noHealth = Monster.Replace(",85,80,3,3]", ",5,0,0,3]");
        Check(harness.Processor.Process(1, Frame(103, 3, "", noHealth)) == BattleSyncApplyResult.Applied
            && !harness.Monsters.Last.HasHp && !harness.Monsters.Last.HasCombatRevision
            && store.TryGetState("monster", "monster-501", out state) && state.Hp == 80,
            "Unflagged HP was applied to the combat state.");
    }

    private static string Frame(long tick, long sequence, string players = Player,
        string monsters = Monster, string scene = "world_001")
    {
        return "{\"msgId\":\"state_frame\",\"type\":\"state_frame\",\"sceneId\":\"" + scene
            + "\",\"serverTick\":" + tick.ToString(CultureInfo.InvariantCulture)
            + ",\"serverTime\":1784713000100,\"frameSequence\":"
            + sequence.ToString(CultureInfo.InvariantCulture)
            + ",\"players\":[" + players + "],\"monsters\":[" + monsters + "]}";
    }

    private static void Check(bool condition, string reason)
    {
        if (!condition) throw new Exception(reason);
    }
}
