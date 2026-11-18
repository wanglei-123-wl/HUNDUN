using System;
using System.Collections.Generic;
using UnityEngine;

public class CombatStateStore : MonoBehaviour
{
    private readonly Dictionary<CombatEntityKey, CombatStateData> states =
        new Dictionary<CombatEntityKey, CombatStateData>();

    public string ActiveSceneId { get; private set; }

    public event Action<CombatEntityKey, CombatStateData> StateChanged;

    public void BeginScene(string sceneId)
    {
        if (string.IsNullOrWhiteSpace(sceneId))
        {
            return;
        }

        if (!string.Equals(ActiveSceneId, sceneId, StringComparison.Ordinal))
        {
            states.Clear();
        }

        ActiveSceneId = sceneId;
    }

    public void ReplaceSnapshot(string sceneId, CombatStateMessage[] incomingStates)
    {
        TryReplaceSnapshot(sceneId, incomingStates);
    }

    public bool TryReplaceSnapshot(string sceneId, CombatStateMessage[] incomingStates, long serverTick = 0)
    {
        if (string.IsNullOrWhiteSpace(sceneId)) return false;
        Dictionary<CombatEntityKey, CombatStateData> replacement = new Dictionary<CombatEntityKey, CombatStateData>();
        CombatStateMessage[] source = incomingStates ?? Array.Empty<CombatStateMessage>();
        for (int index=0; index<source.Length; index++)
        {
            CombatStateMessage incoming=source[index];
            if (!TryCreateKey(incoming==null?null:incoming.entity,out CombatEntityKey key) || replacement.ContainsKey(key)) return false;
            CombatStateData next = CreateState(incoming, serverTick);
            if (key.EntityType == "monster" && states.TryGetValue(key, out CombatStateData current))
            {
                if (next.SpawnGeneration < current.SpawnGeneration
                    || (next.SpawnGeneration == current.SpawnGeneration
                        && next.CombatRevision <= current.CombatRevision))
                    next = current;
                else if (next.SpawnGeneration == current.SpawnGeneration)
                    next = new CombatStateData(next.Hp, next.MaxHp, next.LifeState,
                        next.CombatRevision, next.SpawnGeneration, next.LastServerTick,
                        current.GenerationServerTick > 0 ? current.GenerationServerTick : serverTick);
            }
            replacement.Add(key, next);
        }
        states.Clear(); foreach(var pair in replacement){states.Add(pair.Key,pair.Value);StateChanged?.Invoke(pair.Key,pair.Value);}
        ActiveSceneId=sceneId; return true;
    }

    public bool MergeMonsterSpawnState(string sceneId, string entityId, int hp, int maxHp,
        string lifeState, long combatRevision, long spawnGeneration, long serverTick)
    {
        if (!IsActiveScene(sceneId) || string.IsNullOrWhiteSpace(entityId)
            || spawnGeneration < 0 || combatRevision < 0 || serverTick <= 0)
            return false;
        var key = new CombatEntityKey("monster", entityId);
        states.TryGetValue(key, out CombatStateData current);
        if (current != null && (spawnGeneration < current.SpawnGeneration
            || (spawnGeneration == current.SpawnGeneration && combatRevision < current.CombatRevision)))
            return false;
        int safeMaxHp = Mathf.Max(0, maxHp);
        long generationTick = current != null && current.SpawnGeneration == spawnGeneration
            && current.GenerationServerTick > 0
                ? Math.Min(current.GenerationServerTick, serverTick) : serverTick;
        var next = current != null && current.SpawnGeneration == spawnGeneration
            && current.CombatRevision == combatRevision
                ? new CombatStateData(current.Hp, current.MaxHp, current.LifeState,
                    current.CombatRevision, spawnGeneration, current.LastServerTick, generationTick)
                : new CombatStateData(
                    safeMaxHp > 0 ? Mathf.Clamp(hp, 0, safeMaxHp) : Mathf.Max(0, hp),
                    safeMaxHp, lifeState, combatRevision, spawnGeneration, serverTick, generationTick);
        states[key] = next;
        if (current == null || !current.HasSameValues(next)) StateChanged?.Invoke(key, next);
        return true;
    }

    public bool MergeState(string sceneId, CombatStateMessage incoming)
    {
        if (!IsActiveScene(sceneId)
            || incoming == null
            || !TryCreateKey(incoming.entity, out CombatEntityKey key))
        {
            return false;
        }

        if (!states.TryGetValue(key, out CombatStateData current))
        {
            CombatStateData created = CreateState(incoming);
            states[key] = created;
            StateChanged?.Invoke(key, created);
            return true;
        }

        if (incoming.spawnGeneration < current.SpawnGeneration)
        {
            return false;
        }

        if (incoming.spawnGeneration == current.SpawnGeneration
            && incoming.combatRevision < current.CombatRevision)
        {
            return false;
        }
        if (key.EntityType == "monster" && incoming.spawnGeneration == current.SpawnGeneration
            && incoming.combatRevision == current.CombatRevision)
            return true;

        CombatStateData merged = CreateState(incoming);
        if (key.EntityType == "monster" && current.GenerationServerTick > 0
            && incoming.spawnGeneration == current.SpawnGeneration)
            merged = new CombatStateData(merged.Hp, merged.MaxHp, merged.LifeState,
                merged.CombatRevision, merged.SpawnGeneration, merged.LastServerTick,
                current.GenerationServerTick);
        bool changed = !current.HasSameValues(merged);
        states[key] = merged;

        if (changed)
        {
            StateChanged?.Invoke(key, merged);
        }

        return true;
    }

    public bool MergeEventHealth(
        string sceneId,
        CombatEntityMessage entity,
        int hp,
        int maxHp,
        string lifeState,
        long combatRevision,
        long serverTick=0)
    {
        if (!IsActiveScene(sceneId) || !TryCreateKey(entity, out CombatEntityKey key))
        {
            return false;
        }

        states.TryGetValue(key, out CombatStateData current);
        if (key.EntityType == "monster"
            && (current == null || (serverTick > 0 && current.GenerationServerTick > 0
                && serverTick < current.GenerationServerTick)))
            return false;
        if (current != null && combatRevision <= current.CombatRevision)
        {
            return false;
        }

        long spawnGeneration = current == null ? 0 : current.SpawnGeneration;
        int safeMaxHp = Mathf.Max(0, maxHp);
        int safeHp = safeMaxHp > 0 ? Mathf.Clamp(hp, 0, safeMaxHp) : Mathf.Max(0, hp);
        string mergedLifeState = string.IsNullOrEmpty(lifeState) && current != null
            ? current.LifeState
            : lifeState;
        CombatStateData merged = new CombatStateData(
            safeHp,
            safeMaxHp,
            mergedLifeState,
            combatRevision,
            spawnGeneration,
            serverTick,
            current == null ? 0 : current.GenerationServerTick);

        bool changed = current == null || !current.HasSameValues(merged);
        states[key] = merged;

        if (changed)
        {
            StateChanged?.Invoke(key, merged);
        }

        return true;
    }

    public bool MergeDynamicMonsterHealth(string sceneId,string entityId,int hp,long combatRevision,
        long serverTick,long spawnGeneration)
    {
        if(!TryGetState("monster",entityId,out CombatStateData current)||current==null
            ||current.SpawnGeneration!=spawnGeneration
            ||(current.GenerationServerTick>0&&serverTick<current.GenerationServerTick))
            return false;
        return MergeEventHealth(sceneId,new CombatEntityMessage{entityType="monster",entityId=entityId},
            hp,current.MaxHp,hp<=0?"Dead":current.LifeState,combatRevision,serverTick);
    }

    public bool TryApplyItemUseHealth(string sceneId, string entityId, int hp,
        long expectedCombatRevision)
    {
        if (expectedCombatRevision < 0 || hp < 0 || !IsActiveScene(sceneId)
            || !TryGetState("player", entityId, out CombatStateData current)
            || current.CombatRevision != expectedCombatRevision
            || string.Equals(current.LifeState, "Dead", StringComparison.OrdinalIgnoreCase))
            return false;

        var key = new CombatEntityKey("player", entityId);
        var updated = new CombatStateData(
            current.MaxHp > 0 ? Mathf.Clamp(hp, 0, current.MaxHp) : hp,
            current.MaxHp, current.LifeState, current.CombatRevision,
            current.SpawnGeneration, current.LastServerTick);
        states[key] = updated;
        if (!current.HasSameValues(updated)) StateChanged?.Invoke(key, updated);
        return true;
    }

    public bool TryGetState(string entityType, string entityId, out CombatStateData state)
    {
        CombatEntityKey key = new CombatEntityKey(entityType, entityId);
        if (!key.IsValid)
        {
            state = null;
            return false;
        }

        return states.TryGetValue(key, out state);
    }

    public void Clear()
    {
        ActiveSceneId = string.Empty;
        states.Clear();
    }

    private bool IsActiveScene(string sceneId)
    {
        return !string.IsNullOrEmpty(sceneId)
            && string.Equals(ActiveSceneId, sceneId, StringComparison.Ordinal);
    }

    private static bool TryCreateKey(CombatEntityMessage entity, out CombatEntityKey key)
    {
        key = entity == null
            ? default(CombatEntityKey)
            : new CombatEntityKey(entity.entityType, entity.entityId);
        return key.IsValid;
    }

    private static CombatStateData CreateState(CombatStateMessage incoming, long serverTick = 0)
    {
        int maxHp = Mathf.Max(0, incoming.maxHp);
        int hp = maxHp > 0
            ? Mathf.Clamp(incoming.hp, 0, maxHp)
            : Mathf.Max(0, incoming.hp);

        return new CombatStateData(
            hp,
            maxHp,
            incoming.lifeState,
            incoming.combatRevision,
            incoming.spawnGeneration,
            0,
            serverTick);
    }
}

public struct CombatEntityKey : IEquatable<CombatEntityKey>
{
    public string EntityType { get; }
    public string EntityId { get; }
    public bool IsValid => !string.IsNullOrEmpty(EntityType) && !string.IsNullOrEmpty(EntityId);

    public CombatEntityKey(string entityType, string entityId)
    {
        EntityType = entityType ?? string.Empty;
        EntityId = entityId ?? string.Empty;
    }

    public bool Equals(CombatEntityKey other)
    {
        return string.Equals(EntityType, other.EntityType, StringComparison.Ordinal)
            && string.Equals(EntityId, other.EntityId, StringComparison.Ordinal);
    }

    public override bool Equals(object obj)
    {
        return obj is CombatEntityKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            return ((EntityType != null ? EntityType.GetHashCode() : 0) * 397)
                ^ (EntityId != null ? EntityId.GetHashCode() : 0);
        }
    }
}

public sealed class CombatStateData
{
    public int Hp { get; }
    public int MaxHp { get; }
    public string LifeState { get; }
    public long CombatRevision { get; }
    public long SpawnGeneration { get; }
    public long LastServerTick { get; }
    public long GenerationServerTick { get; }

    public CombatStateData(
        int hp,
        int maxHp,
        string lifeState,
        long combatRevision,
        long spawnGeneration,
        long lastServerTick=0,
        long generationServerTick=0)
    {
        Hp = hp;
        MaxHp = maxHp;
        LifeState = lifeState ?? string.Empty;
        CombatRevision = combatRevision;
        SpawnGeneration = spawnGeneration;
        LastServerTick=lastServerTick;
        GenerationServerTick=generationServerTick;
    }

    public bool HasSameValues(CombatStateData other)
    {
        return other != null
            && Hp == other.Hp
            && MaxHp == other.MaxHp
            && string.Equals(LifeState, other.LifeState, StringComparison.Ordinal)
            && CombatRevision == other.CombatRevision
            && SpawnGeneration == other.SpawnGeneration
            && LastServerTick==other.LastServerTick
            && GenerationServerTick==other.GenerationServerTick;
    }
}
