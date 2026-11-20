using System;
using UnityEngine;

public readonly struct PlayerEffectState
{
    public readonly bool IsKnown;
    public readonly bool IsAlive;
    public readonly bool IsMeditating;

    public PlayerEffectState(bool alive, bool meditating)
    {
        IsKnown = true;
        IsAlive = alive;
        IsMeditating = alive && meditating;
    }
}

// Runtime state belongs to this actor, never to the shared configuration asset.
// An actor without a state publisher keeps unknown state.
[DisallowMultipleComponent]
public sealed class PlayerEffectStateSource : MonoBehaviour
{
    public PlayerEffectState Current { get; private set; }
    public event Action Changed;

    internal void Publish(PlayerEffectState state)
    {
        if (!isActiveAndEnabled) state = default;
        if (Current.IsKnown == state.IsKnown && Current.IsAlive == state.IsAlive
            && Current.IsMeditating == state.IsMeditating) return;
        Current = state;
        Changed?.Invoke();
    }

    internal void Clear() => Publish(default);

    private void OnDisable() => Clear();
    private void OnDestroy()
    {
        Clear();
        Changed = null;
    }
}
