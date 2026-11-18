namespace Game.Inventory
{
    public static class InventoryProtocolConstants
    {
        public const string SnapshotRequest = "inventory_snapshot";
        public const string SnapshotResult = "inventory_snapshot_result";
        public const string LootPickupResult = "loot_pickup_result";
        public const string Error = "error";

        public static class PickupStatuses
        {
            public const string Pending = "pending";
            public const string Succeeded = "succeeded";
            public const string Rejected = "rejected";
        }
    }
}
