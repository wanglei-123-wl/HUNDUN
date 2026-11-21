using System;

namespace Game.Client.BattleSync
{
    public enum BattleSyncSnapshotRoutingMode
    {
        LegacySelfRegistered = 0,
        CoordinatorOwned = 1
    }

    public interface IBattleSyncSnapshotRoutingControl
    {
        void SetSnapshotRoutingMode(BattleSyncSnapshotRoutingMode mode);
    }

    public interface IBattleSyncJsonCodec
    {
        bool TryDeserialize<T>(string json, out T message, out string error)
            where T : class;

        bool TrySerialize<T>(T message, out string json, out string error)
            where T : class;
    }

    public readonly struct BattleSyncModuleSessionContext
    {
        public long ConnectionEpoch { get; }

        public string SceneId { get; }

        public long CharacterDbId { get; }

        public long SnapshotRevision { get; }

        public long StartedAtMs { get; }

        public BattleSyncModuleSessionContext(
            long connectionEpoch,
            string sceneId,
            long characterDbId,
            long snapshotRevision,
            long startedAtMs)
        {
            ConnectionEpoch = connectionEpoch;
            SceneId = sceneId ?? string.Empty;
            CharacterDbId = characterDbId;
            SnapshotRevision = snapshotRevision;
            StartedAtMs = startedAtMs;
        }

        public bool IsValid =>
            ConnectionEpoch > 0
            && !string.IsNullOrWhiteSpace(SceneId)
            && CharacterDbId > 0
            && SnapshotRevision > 0
            && StartedAtMs >= 0;
    }

    public readonly struct BattleSyncEnterWorldContext
    {
        public long ConnectionEpoch { get; }

        public string SceneId { get; }

        public long CharacterDbId { get; }

        public long SnapshotRevision { get; }

        public long ServerTime { get; }

        public string Reason { get; }
        public string SceneSessionId { get; }
        public int MonsterSyncVersion { get; }

        public BattleSyncEnterWorldContext(
            long connectionEpoch,
            string sceneId,
            long characterDbId,
            long snapshotRevision,
            long serverTime,
            string reason, string sceneSessionId = "", int monsterSyncVersion = 0)
        {
            ConnectionEpoch = connectionEpoch;
            SceneId = sceneId ?? string.Empty;
            CharacterDbId = characterDbId;
            SnapshotRevision = snapshotRevision;
            ServerTime = serverTime;
            Reason = reason ?? string.Empty;
            SceneSessionId = sceneSessionId ?? string.Empty;
            MonsterSyncVersion = monsterSyncVersion;
        }

        public bool IsValid =>
            ConnectionEpoch > 0
            && !string.IsNullOrWhiteSpace(SceneId)
            && CharacterDbId > 0
            && SnapshotRevision > 0
            && ServerTime > 0;
    }

    public readonly struct BattleSyncReadyContext
    {
        public long ConnectionEpoch { get; }
        public string SceneId { get; }
        public long CharacterDbId { get; }
        public long SnapshotRevision { get; }
        public long ServerTick { get; }
        public long ServerTime { get; }
        public string Reason { get; }
        public long ReadyAtMs { get; }

        public BattleSyncReadyContext(long connectionEpoch, string sceneId,
            long characterDbId, long snapshotRevision, long serverTick,
            long serverTime, string reason, long readyAtMs)
        {
            ConnectionEpoch = connectionEpoch;
            SceneId = sceneId ?? string.Empty;
            CharacterDbId = characterDbId;
            SnapshotRevision = snapshotRevision;
            ServerTick = serverTick;
            ServerTime = serverTime;
            Reason = reason ?? string.Empty;
            ReadyAtMs = readyAtMs;
        }
    }

    public interface IInventorySnapshotCompatibilityMirror
    {
        BattleSyncApplyResult ApplyInventorySnapshotMirror(
            Game.Client.BattleSync.Protocol.InventorySnapshotResultMessageDto message);
    }

    public interface IBattleSyncClock
    {
        long NowMilliseconds { get; }
    }

    public interface IBattleSyncTransport
    {
        bool IsConnected { get; }

        long ConnectionEpoch { get; }

        bool SendJson(string json);
    }

    public interface IBattleSyncMessageRouter
    {
        void Register(string msgId, Action<string> handler);

        void Unregister(string msgId, Action<string> handler);
    }

    public interface IBattleSyncModuleLifecycle
    {
        BattleSyncApplyResult BeginSession(
            BattleSyncModuleSessionContext context);

        void EndSession(long connectionEpoch);
    }

    public interface IPlayerBattleSyncModule
        : IBattleSyncModuleLifecycle
    {
        BattleSyncApplyResult ApplyWorldSnapshot(
            Game.Client.BattleSync.Protocol.WorldSnapshotMessageDto message);

        BattleSyncApplyResult ApplyPlayerEnter(
            Game.Client.BattleSync.Protocol.PlayerEnterMessageDto message);

        BattleSyncApplyResult ApplyPlayerLeave(
            Game.Client.BattleSync.Protocol.PlayerLeaveMessageDto message);

        BattleSyncApplyResult ApplyPlayerDynamicState(
            DecodedPlayerState state,
            long serverTick,
            long serverTime);
    }

    public interface IMonsterBattleSyncModule
        : IBattleSyncModuleLifecycle
    {
        BattleSyncApplyResult ApplyMonsterSnapshot(
            Game.Client.BattleSync.Protocol.MonsterSnapshotMessageDto message);

        BattleSyncApplyResult ApplyMonsterEnter(
            Game.Client.BattleSync.Protocol.MonsterEnterMessageDto message);

        BattleSyncApplyResult ApplyMonsterUpsert(
            Game.Client.BattleSync.Protocol.MonsterUpsertMessageDto message);

        BattleSyncApplyResult ApplyMonsterRemove(
            Game.Client.BattleSync.Protocol.MonsterRemoveMessageDto message);

        BattleSyncApplyResult ApplyMonsterDynamicState(
            DecodedMonsterState state,
            long serverTick,
            long serverTime);
    }

    public interface ILootBattleSyncModule
        : IBattleSyncModuleLifecycle
    {
        BattleSyncApplyResult SwitchScene(BattleSyncModuleSessionContext context);

        BattleSyncApplyResult ApplyLootSnapshot(
            Game.Client.BattleSync.Protocol.LootSnapshotMessageDto message);

        BattleSyncApplyResult ApplyLootUpsert(
            Game.Client.BattleSync.Protocol.LootUpsertMessageDto message);

        BattleSyncApplyResult ApplyLootRemove(
            Game.Client.BattleSync.Protocol.LootRemoveMessageDto message);
    }

    public interface ICombatBattleSyncModule
        : IBattleSyncModuleLifecycle
    {
        BattleSyncApplyResult ApplyCombatSnapshot(
            Game.Client.BattleSync.Protocol.CombatSnapshotMessageDto message);

        BattleSyncApplyResult ApplyCombatEvent(
            Game.Client.BattleSync.Protocol.CombatEventMessageDto message);
    }

    public interface IInventoryBattleSyncModule
        : IBattleSyncModuleLifecycle
    {
        BattleSyncApplyResult ApplyInventorySnapshot(
            Game.Client.BattleSync.Protocol.InventorySnapshotResultMessageDto message);
    }

    public sealed class BattleSyncModuleBindings
    {
        public IPlayerBattleSyncModule Player { get; }

        public IMonsterBattleSyncModule Monster { get; }

        public ILootBattleSyncModule Loot { get; }

        public ICombatBattleSyncModule Combat { get; }

        public IInventoryBattleSyncModule Inventory { get; }
        public IInventorySnapshotCompatibilityMirror InventoryCompatibilityMirror { get; }

        public BattleSyncModuleBindings(
            IPlayerBattleSyncModule player,
            IMonsterBattleSyncModule monster,
            ILootBattleSyncModule loot,
            ICombatBattleSyncModule combat,
            IInventoryBattleSyncModule inventory,
            IInventorySnapshotCompatibilityMirror inventoryCompatibilityMirror = null)
        {
            Player = player;
            Monster = monster;
            Loot = loot;
            Combat = combat;
            Inventory = inventory;
            InventoryCompatibilityMirror = inventoryCompatibilityMirror;
        }

        public static BattleSyncModuleBindings Empty =>
            new BattleSyncModuleBindings(
                null,
                null,
                null,
                null,
                null);
    }
}
