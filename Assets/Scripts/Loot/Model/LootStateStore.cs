using System;
using System.Collections.Generic;

public sealed class LootStateStore
{
    private readonly Dictionary<string, LootState> lootsById =
        new Dictionary<string, LootState>(StringComparer.Ordinal);

    private readonly Dictionary<string, long> latestRevisionsById =
        new Dictionary<string, long>(StringComparer.Ordinal);

    private readonly Dictionary<string, string> revisionScenesById =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public bool TryGet(string lootId, out LootState loot)
    {
        if (string.IsNullOrWhiteSpace(lootId))
        {
            loot = null;
            return false;
        }

        return lootsById.TryGetValue(lootId, out loot);
    }

    public bool TryGetLatestRevision(string lootId, out long revision)
    {
        if (string.IsNullOrWhiteSpace(lootId))
        {
            revision = 0;
            return false;
        }

        return latestRevisionsById.TryGetValue(lootId, out revision);
    }

    public IReadOnlyCollection<LootState> GetAll()
    {
        LootState[] result = new LootState[lootsById.Count];
        lootsById.Values.CopyTo(result, 0);
        return result;
    }

    public IReadOnlyCollection<LootState> GetByScene(string sceneId)
    {
        if (string.IsNullOrWhiteSpace(sceneId))
        {
            return Array.Empty<LootState>();
        }

        List<LootState> result = new List<LootState>();
        foreach (LootState loot in lootsById.Values)
        {
            if (string.Equals(loot.SceneId, sceneId, StringComparison.Ordinal))
            {
                result.Add(loot);
            }
        }

        return result.ToArray();
    }

    public bool Upsert(LootState incoming)
    {
        if (incoming == null || string.IsNullOrWhiteSpace(incoming.LootId))
        {
            return false;
        }

        if (latestRevisionsById.TryGetValue(incoming.LootId, out long latestRevision)
            && latestRevision > incoming.AoiRevision)
        {
            return false;
        }

        lootsById[incoming.LootId] = incoming;
        latestRevisionsById[incoming.LootId] = incoming.AoiRevision;
        revisionScenesById[incoming.LootId] = incoming.SceneId;
        return true;
    }

    public bool Remove(string lootId, long aoiRevision)
    {
        if (string.IsNullOrWhiteSpace(lootId))
        {
            return false;
        }

        if (latestRevisionsById.TryGetValue(lootId, out long latestRevision)
            && latestRevision > aoiRevision)
        {
            return false;
        }

        bool removed = lootsById.Remove(lootId);
        latestRevisionsById[lootId] = aoiRevision;
        return removed;
    }

    public void ReplaceSceneSnapshot(string sceneId, IEnumerable<LootState> loots)
    {
        if (string.IsNullOrWhiteSpace(sceneId))
        {
            return;
        }

        ClearScene(sceneId);

        if (loots == null)
        {
            return;
        }

        Dictionary<string, LootState> snapshotById =
            new Dictionary<string, LootState>(StringComparer.Ordinal);

        foreach (LootState loot in loots)
        {
            if (loot == null
                || string.IsNullOrWhiteSpace(loot.LootId)
                || !string.Equals(loot.SceneId, sceneId, StringComparison.Ordinal))
            {
                continue;
            }

            if (!snapshotById.TryGetValue(loot.LootId, out LootState existing)
                || existing.AoiRevision <= loot.AoiRevision)
            {
                snapshotById[loot.LootId] = loot;
            }
        }

        foreach (LootState loot in snapshotById.Values)
        {
            lootsById[loot.LootId] = loot;
            latestRevisionsById[loot.LootId] = loot.AoiRevision;
            revisionScenesById[loot.LootId] = loot.SceneId;
        }
    }

    public void ClearScene(string sceneId)
    {
        if (string.IsNullOrWhiteSpace(sceneId))
        {
            return;
        }

        List<string> lootIdsToRemove = new List<string>();
        foreach (KeyValuePair<string, string> pair in revisionScenesById)
        {
            if (string.Equals(pair.Value, sceneId, StringComparison.Ordinal))
            {
                lootIdsToRemove.Add(pair.Key);
            }
        }

        for (int index = 0; index < lootIdsToRemove.Count; index++)
        {
            string lootId = lootIdsToRemove[index];
            lootsById.Remove(lootId);
            latestRevisionsById.Remove(lootId);
            revisionScenesById.Remove(lootId);
        }
    }

    public void ClearAll()
    {
        lootsById.Clear();
        latestRevisionsById.Clear();
        revisionScenesById.Clear();
    }
}
