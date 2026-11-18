using System;
using UnityEngine;

public sealed class LootState
{
    public string LootId { get; }
    public string SceneId { get; }
    public string ItemId { get; }
    public int Count { get; }
    public Vector3 Position { get; }
    public string OwnerType { get; }
    public string OwnerId { get; }
    public long OwnerExpireAtMs { get; }
    public long ExpireAtMs { get; }
    public long AoiRevision { get; }
    public string State { get; }

    public LootState(
        string lootId,
        string sceneId,
        string itemId,
        int count,
        Vector3 position,
        string ownerType,
        string ownerId,
        long ownerExpireAtMs,
        long expireAtMs,
        long aoiRevision,
        string state)
    {
        if (string.IsNullOrWhiteSpace(lootId))
        {
            throw new ArgumentException("Loot ID cannot be empty.", nameof(lootId));
        }

        LootId = lootId;
        SceneId = sceneId;
        ItemId = itemId;
        Count = count;
        Position = position;
        OwnerType = ownerType;
        OwnerId = ownerId;
        OwnerExpireAtMs = ownerExpireAtMs;
        ExpireAtMs = expireAtMs;
        AoiRevision = aoiRevision;
        State = state;
    }

    public static LootState FromDto(LootDataDto dto)
    {
        if (dto == null)
        {
            throw new ArgumentNullException(nameof(dto));
        }

        return new LootState(
            dto.lootId,
            dto.sceneId,
            dto.itemId,
            dto.count,
            new Vector3(dto.x, dto.y, dto.z),
            dto.ownerType,
            dto.ownerId,
            dto.ownerExpireAtMs,
            dto.expireAtMs,
            dto.aoiRevision,
            dto.state);
    }
}
