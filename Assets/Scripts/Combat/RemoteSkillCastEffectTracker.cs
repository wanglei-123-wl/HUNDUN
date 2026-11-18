using System.Collections.Generic;
using UnityEngine;

public sealed class RemoteSkillCastEffectTracker
{
    private readonly Dictionary<long, List<GameObject>> casterEffects =
        new Dictionary<long, List<GameObject>>();
    private readonly List<GameObject> fixedEffects = new List<GameObject>();
    private readonly List<long> emptyCasters = new List<long>();

    public void Track(long casterId, bool removeWithCaster, GameObject instance)
    {
        if (instance == null) return;
        if (!removeWithCaster)
        {
            fixedEffects.Add(instance);
            return;
        }

        if (!casterEffects.TryGetValue(casterId, out List<GameObject> effects))
        {
            effects = new List<GameObject>();
            casterEffects.Add(casterId, effects);
        }
        effects.Add(instance);
    }

    public void RemoveCaster(long casterId)
    {
        if (!casterEffects.TryGetValue(casterId, out List<GameObject> effects))
            return;
        DestroyAll(effects);
        casterEffects.Remove(casterId);
    }

    public void Prune()
    {
        fixedEffects.RemoveAll(effect => effect == null);
        emptyCasters.Clear();
        foreach (KeyValuePair<long, List<GameObject>> entry in casterEffects)
        {
            entry.Value.RemoveAll(effect => effect == null);
            if (entry.Value.Count == 0) emptyCasters.Add(entry.Key);
        }
        foreach (long casterId in emptyCasters)
            casterEffects.Remove(casterId);
    }

    public void Clear()
    {
        foreach (List<GameObject> effects in casterEffects.Values)
            DestroyAll(effects);
        DestroyAll(fixedEffects);
        casterEffects.Clear();
        fixedEffects.Clear();
        emptyCasters.Clear();
    }

    private static void DestroyAll(List<GameObject> effects)
    {
        foreach (GameObject effect in effects)
            if (effect != null) UnityEngine.Object.Destroy(effect);
    }
}
