public enum LootRemoveReason
{
    Unknown = 0,
    OutOfView = 1,
    Picked = 2,
    Expired = 3,
    ClaimedByOther = 4,
    GrantRejected = 5
}

public enum LootOwnershipState
{
    Free = 0,
    OwnedBySelf = 1,
    ProtectedByOther = 2
}

public enum LootSyncResult
{
    Applied = 0,
    IgnoredScene = 1,
    IgnoredOldRevision = 2,
    IgnoredInvalidData = 3,
    IgnoredNotFound = 4
}

public enum LootPickupRequestResult
{
    Sent = 0,
    Suspended = 1,
    LootNotFound = 2,
    WrongScene = 3,
    AlreadyPending = 4,
    InvalidLootId = 5,
    InvalidClientTick = 6,
    SequenceExhausted = 7,
    NetworkSendFailed = 8
}

public enum LootPickupRequestPhase
{
    RequestSent = 0,
    Pending = 1,
    Reconciling = 2
}

public enum InventorySyncResult
{
    Applied = 0,
    DuplicateOrOld = 1,
    RevisionGap = 2,
    InconsistentPreviousValue = 3,
    InvalidData = 4
}

public enum LootInteractionResult
{
    PickupRequested = 0,
    InvalidTarget = 1,
    LootNotFound = 2,
    WrongScene = 3,
    AlreadyPending = 4,
    ProtectedByOther = 5,
    MoveCloserRequested = 6,
    PickupRejected = 7
}
