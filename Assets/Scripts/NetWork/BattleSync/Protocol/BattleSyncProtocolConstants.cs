namespace Game.Client.BattleSync
{
    public static class BattleSyncProtocolConstants
    {
        public static class MessageIds
        {
            public const string EnterWorld = "enter_world";
            public const string EnterWorldResult = "enter_world_result";
            public const string Heartbeat = "heartbeat";
            public const string HeartbeatResult = "heartbeat_result";
            public const string Error = "error";
            public const string WorldSnapshot = "world_snapshot";
            public const string MonsterSnapshot = "monster_snapshot";
            public const string LootSnapshot = "loot_snapshot";
            public const string CombatSnapshot = "combat_snapshot";
            public const string InventorySnapshot = "inventory_snapshot";
            public const string InventorySnapshotResult = "inventory_snapshot_result";
            public const string PlayerEnterBroadcast = "player_enter_broadcast";
            public const string PlayerLeaveBroadcast = "player_leave_broadcast";
            public const string MonsterEnter = "monster_enter";
            public const string MonsterRemove = "monster_remove";
            public const string LootUpsert = "loot_upsert";
            public const string LootRemove = "loot_remove";
            public const string CombatEvent = "combat_event";
            public const string CombatEventBatch = "combat_event_batch";
            public const string BattleOutboxAck = "battle_outbox_ack";
            public const string StateFrame = "state_frame";
            public const string PlayerAuthoritativeState = "player_authoritative_state";
            public const string CombatAction = "combat_action";
            public const string Respawn = "respawn";
            public const string DeathTimeoutKick = "death_timeout_kick";
            public const string LootPickup = "loot_pickup";
            public const string SnapshotRepairRequest = "snapshot_repair_request";
        }

        public static class LegacyMessageIds
        {
            public const string PlayerStateBroadcast = "player_state_broadcast";
            public const string PlayerStateFrame = "player_state_frame";
            public const string MonsterState = "monster_state";
            public const string MonsterStateFrame = "monster_state_frame";
            public const string MonsterUpsert = "monster_upsert";
            public const string CombatStateUpsert = "combat_state_upsert";
        }

        public static class EntityTypes
        {
            public const string Player = "player";
            public const string Monster = "monster";
            public const string Loot = "loot";
        }

        public static class RepairReasons
        {
            public const string UnknownEntityState = "client_unknown_entity_state";
            public const string RevisionGap = "client_revision_gap";
            public const string MissingEntity = "client_missing_entity";
            public const string InventoryMismatch = "client_inventory_mismatch";
            public const string GenerationMismatch = "client_generation_mismatch";
            public const string SnapshotMismatch = "client_snapshot_mismatch";
            public const string ManualRequest = "client_manual_request";
        }

        public static class StateFlags
        {
            public const ushort Position = 1;
            public const ushort Velocity = 2;
            public const ushort MovementState = 4;
            public const ushort ActionState = 8;
            public const ushort Hp = 16;
            public const ushort LifeState = 32;
            public const ushort CombatRevision = 64;
        }

        public static class Quantization
        {
            public const float PositionToMeters = 0.01f;
            public const float VelocityToMetersPerSecond = 0.01f;
            public const float YawToDegrees = 360f / 65535f;
        }

        public static class MonsterAiStates
        {
            public const byte Unknown = 0;
            public const byte Idle = 1;
            public const byte TakeOff = 2;
            public const byte Chase = 3;
            public const byte Attack = 4;
            public const byte Return = 5;
            public const byte Landing = 6;
            public const byte Dead = 7;
            public const byte Patrol = 8;
            public const byte Hover = 9;
        }

        public static class Limits
        {
            public const int RepairReasonMaxLength = 64;
            public const int EntityTypeMaxLength = 32;
            public const int EntityIdMaxLength = 128;
            public const float RepairRequestMinimumIntervalSeconds = 1f;
        }
    }
}
