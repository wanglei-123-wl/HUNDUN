using System;
using UnityEngine;

public class MonsterNetworkView : MonoBehaviour
{
    [SerializeField] private MonsterViewRoot viewRoot;

    private long lastStateSequence;
    private long lastEventSequence;
    private bool hasStateSequence;
    private bool hasEventSequence;
    private bool receivedDeathState;

    public string EntityId => viewRoot == null ? "" : viewRoot.EntityId;
    public long LastStateSequence => lastStateSequence;
    public long LastEventSequence => lastEventSequence;
    public int CurrentHp { get; private set; }
    public int MaxHp { get; private set; }
    internal bool IsTargetable => isActiveAndEnabled && !receivedDeathState && MaxHp > 0 && CurrentHp > 0
        && !string.IsNullOrWhiteSpace(EntityId);
    internal GameObject SelectionRing { get; set; }

    public event Action<int, int> OnHealthChanged;

    private void Awake()
    {
        ResolveViewRoot();
    }

    internal void SetSelectionRingVisible(bool visible)
    {
        if (SelectionRing != null)
            SelectionRing.SetActive(visible);
    }

    public void Initialize(MonsterViewState initialState)
    {
        ResolveViewRoot();

        lastStateSequence = initialState.stateSequence;
        hasStateSequence = true;
        lastEventSequence = 0;
        hasEventSequence = false;
        receivedDeathState = false;

        viewRoot?.Initialize(initialState.entityId, initialState);
        ApplyHealth(initialState.hp, initialState.maxHp);
    }

    public void RebindAnimator()
    {
        ResolveViewRoot();
        viewRoot?.RebindAnimator();
    }

    public void BootstrapState(string state)
    {
        if (string.Equals(state, "Dead", StringComparison.OrdinalIgnoreCase))
            receivedDeathState = true;

        ResolveViewRoot();
        viewRoot?.BootstrapState(state);
    }

    public bool ApplyState(MonsterViewState state)
    {
        ResolveViewRoot();

        if (viewRoot == null || !IsMatchingEntity(state.entityId))
        {
            return false;
        }

        if (hasStateSequence && state.stateSequence <= lastStateSequence)
        {
            return false;
        }

        lastStateSequence = state.stateSequence;
        hasStateSequence = true;
        viewRoot.ApplyState(state);
        ApplyHealth(state.hp, state.maxHp);
        return true;
    }

    public bool PlayAction(MonsterActionData action)
    {
        ResolveViewRoot();

        if (viewRoot == null || !IsMatchingEntity(action.entityId))
        {
            return false;
        }

        if (hasEventSequence && action.eventSequence <= lastEventSequence)
        {
            return false;
        }

        lastEventSequence = action.eventSequence;
        hasEventSequence = true;
        viewRoot.PlayAction(action);
        return true;
    }

    public void ResetView()
    {
        ResolveViewRoot();

        lastStateSequence = 0;
        lastEventSequence = 0;
        hasStateSequence = false;
        hasEventSequence = false;
        CurrentHp = 0;
        MaxHp = 0;
        viewRoot?.ResetView();
    }

    public void StopNetworkInterpolation()
    {
        ResolveViewRoot();
        viewRoot?.TransformInterpolator?.ResetView();
    }

    public void ClearPendingSnapshots()
    {
        lastStateSequence = 0;
        lastEventSequence = 0;
        hasStateSequence = false;
        hasEventSequence = false;
        viewRoot?.ResetView();
    }

    public void ApplyHealth(int currentHp, int maxHp)
    {
        int safeMaxHp = Mathf.Max(0, maxHp);
        int safeCurrentHp = safeMaxHp > 0
            ? Mathf.Clamp(currentHp, 0, safeMaxHp)
            : Mathf.Max(0, currentHp);

        if (CurrentHp == safeCurrentHp && MaxHp == safeMaxHp)
        {
            return;
        }

        if (CurrentHp == 0 && safeCurrentHp > 0)
            receivedDeathState = false;

        CurrentHp = safeCurrentHp;
        MaxHp = safeMaxHp;
        OnHealthChanged?.Invoke(CurrentHp, MaxHp);
    }

    private bool IsMatchingEntity(string incomingEntityId)
    {
        return string.IsNullOrEmpty(incomingEntityId)
            || string.IsNullOrEmpty(EntityId)
            || incomingEntityId == EntityId;
    }

    private void ResolveViewRoot()
    {
        if (viewRoot == null)
        {
            viewRoot = GetComponent<MonsterViewRoot>();
        }

        if (viewRoot == null)
        {
            Debug.LogError("MonsterNetworkView requires a MonsterViewRoot.", this);
        }
    }
}
