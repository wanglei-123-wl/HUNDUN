public static class LootProtocolConstants
{
    public const string Snapshot = "loot_snapshot";
    public const string Upsert = "loot_upsert";
    public const string Remove = "loot_remove";
    public const string Pickup = "loot_pickup";
    public const string PickupResult = "loot_pickup_result";
    public const string BattleOutboxAck = "battle_outbox_ack";
    public const string InventorySnapshot = "inventory_snapshot";
    public const string InventorySnapshotResult = "inventory_snapshot_result";
    public const string Error = "error";

    public static class PickupStatuses
    {
        public const string Pending = "pending";
        public const string Succeeded = "succeeded";
        public const string Rejected = "rejected";
    }

    public static class OwnerTypes
    {
        public const string Player = "player";
    }

    public static class States
    {
        public const string Active = "active";
    }

    public static class RemoveReasons
    {
        public const string OutOfView = "out_of_view";
        public const string Picked = "picked";
        public const string Expired = "expired";
        public const string ClaimedByOther = "claimed_by_other";
        public const string GrantRejected = "grant_rejected";
    }

    public static class ErrorCodes
    {
        public const string PickupDuplicateOrStale = "loot_pickup_duplicate_or_stale";
        public const string PickupSequenceGap = "loot_pickup_sequence_gap";
        public const string PlayerNotInWorld = "player_not_in_world";
        public const string PlayerDead = "player_dead";
        public const string LootNotFound = "loot_not_found";
        public const string LootAlreadyPicked = "loot_already_picked";
        public const string LootExpired = "loot_expired";
        public const string LootNotOwner = "loot_not_owner";
        public const string LootTooFar = "loot_too_far";
        public const string ServerBusy = "server_busy";
        public const string RewardNotAvailable = "reward_not_available";
        public const string InvalidLootPickup = "invalid_loot_pickup";
        public const string InventoryFull = "inventory_full";
        public const string AlreadyClaimed = "already_claimed";
        public const string WalletLimitExceeded = "wallet_limit_exceeded";
        public const string CharacterNotFound = "character_not_found";
        public const string ItemNotFound = "item_not_found";
        public const string ItemNotGrantable = "item_not_grantable";
        public const string RewardSourceConflict = "reward_source_conflict";
        public const string GrantIdConflict = "grant_id_conflict";
        public const string InvalidGrantRequest = "invalid_grant_request";
        public const string InvalidGrantSource = "invalid_grant_source";
        public const string DatabaseError = "database_error";
        public const string DatabaseUnavailable = "database_unavailable";
        public const string DatabaseOperationCancelled = "database_operation_cancelled";
        public const string GrantNotFound = "grant_not_found";
    }
}
