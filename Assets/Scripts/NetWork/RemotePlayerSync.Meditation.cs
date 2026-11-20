using UnityEngine;

public partial class RemotePlayerSync
{
    private static readonly int RemoteMeditatingParameter = Animator.StringToHash("IsMeditating");
    private static readonly int SeatedLoopState = Animator.StringToHash("Base Layer.打坐中");

    private long meditationRevision = -1;
    private bool remoteMeditating;
    private bool meditationAnimatorParameter;
    private PlayerEffectStateSource remoteEffectState;

    internal bool ApplyMeditationState(MeditationSnapshot state, bool initialState = false)
    {
        if (state == null || state.revision < 0 || state.revision <= meditationRevision) return false;
        meditationRevision = state.revision;
        SetRemoteMeditationPosture(state.isMeditating && !IsDead);
        if (initialState && remoteMeditating && meditationAnimatorParameter
            && animator.isActiveAndEnabled && animator.HasState(0, SeatedLoopState))
            animator.Play(SeatedLoopState, 0, 0f);
        return true;
    }

    internal void ClearMeditationPresentation() => SetRemoteMeditationPosture(false);

    private void SetRemoteMeditationPosture(bool seated)
    {
        if (remoteMeditating != seated) currentAnim = "";
        remoteMeditating = seated;
        if (animator != null && animator.isActiveAndEnabled)
        {
            if (meditationAnimatorParameter) animator.SetBool(RemoteMeditatingParameter, seated);
            if (seated)
            {
                if (hasMoveBoolParameter) animator.SetBool(moveBoolName, false);
                if (hasRunningParameter) animator.SetBool(RunningParameter, false);
            }
        }
        if (!seated) PublishRemoteMeditationEffect();
    }

    private void LateUpdate() => PublishRemoteMeditationEffect();

    private void OnDisable()
    {
        EndRemoteRoll();
        remoteEffectState?.Clear();
    }

    private void PublishRemoteMeditationEffect()
    {
        if (remoteEffectState == null) TryGetComponent(out remoteEffectState);
        if (remoteEffectState == null) return;
        if (!isActiveAndEnabled || !hasReceivedState)
        {
            remoteEffectState.Clear();
            return;
        }

        bool showMeditating = remoteMeditating && meditationAnimatorParameter
            && animator != null && animator.isActiveAndEnabled
            && !animator.IsInTransition(0)
            && animator.GetCurrentAnimatorStateInfo(0).fullPathHash == SeatedLoopState;
        remoteEffectState.Publish(new PlayerEffectState(!IsDead, showMeditating));
    }
}
