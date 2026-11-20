using UnityEngine;

public partial class RemotePlayerSync
{
    private const string RollClipName = "HumanF@Roll01";
    private static readonly int RemoteRollState = Animator.StringToHash("Base Layer.翻滚冲刺");
    private static readonly int RollExitLocomotion = Animator.StringToHash("Base Layer.待机状态");

    private AnimationClip remoteRollClip;
    private long lastRemoteRollId;
    private bool remoteRollPlaying;
    private double remoteRollEndsAt;
    private int remoteRollPlayFrame;

    private void CacheRemoteRollClip()
    {
        remoteRollClip = null;
        if (animator == null || animator.runtimeAnimatorController == null
            || !animator.HasState(0, RemoteRollState)) return;

        var overrides = animator.runtimeAnimatorController as AnimatorOverrideController;
        var source = overrides == null ? animator.runtimeAnimatorController : overrides.runtimeAnimatorController;
        foreach (AnimationClip clip in source.animationClips)
        {
            if (clip == null || clip.name != RollClipName) continue;
            remoteRollClip = overrides == null ? clip : overrides[clip] ?? clip;
            // The moving root follows accepted network positions.
            if (animator.applyRootMotion) animator.applyRootMotion = false;
            break;
        }
    }

    private void ApplyRemoteRoll(RemoteRollSnapshot roll)
    {
        if (roll == null || IsDead)
        {
            EndRemoteRoll();
            return;
        }
        if (roll.id <= lastRemoteRollId) return;

        lastRemoteRollId = roll.id;
        if (remoteRollClip == null || !animator.isActiveAndEnabled || animator.speed <= 0f)
        {
            EndRemoteRoll();
            return;
        }

        float clipOffset = roll.elapsedMs * 0.001f * animator.speed;
        if (clipOffset >= remoteRollClip.length)
        {
            EndRemoteRoll();
            return;
        }

        // The current clip's state speed is 1. Use the actual clip or its override.
        animator.PlayInFixedTime(RemoteRollState, 0, clipOffset);
        remoteRollPlaying = true;
        remoteRollPlayFrame = Time.frameCount;
        remoteRollEndsAt = Time.realtimeSinceStartupAsDouble
            + (remoteRollClip.length - clipOffset) / animator.speed;
    }

    private void UpdateRemoteRoll()
    {
        if (remoteRollPlaying && Time.realtimeSinceStartupAsDouble >= remoteRollEndsAt)
            EndRemoteRoll();
    }

    internal void EndRemoteRoll()
    {
        if (!remoteRollPlaying) return;
        remoteRollPlaying = false;
        if (IsDead || animator == null || !animator.isActiveAndEnabled
            || !animator.HasState(0, RollExitLocomotion)) return;

        if (remoteRollPlayFrame == Time.frameCount)
        {
            // A start and its end may be received before Animator evaluates.
            animator.Play(RollExitLocomotion, 0, 0f);
            return;
        }

        if (animator.GetCurrentAnimatorStateInfo(0).fullPathHash == RemoteRollState
            || (animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).fullPathHash == RemoteRollState))
            animator.CrossFadeInFixedTime(RollExitLocomotion, 0.06f, 0, 0f);
    }
}
