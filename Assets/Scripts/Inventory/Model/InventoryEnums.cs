namespace Game.Inventory
{
    public enum InventoryItemCategory
    {
        All = 0,
        Equipment = 1,
        Consumable = 2,
        Material = 3,
        Quest = 4,
        Other = 5,
        Pill = 6
    }

    public enum InventoryLoadState
    {
        Uninitialized = 0,
        Loading = 1,
        Ready = 2,
        Failed = 3
    }
}
