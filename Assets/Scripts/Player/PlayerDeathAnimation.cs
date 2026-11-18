using UnityEngine;

internal static class PlayerDeathAnimation
{
    private static readonly int DeadParameter = Animator.StringToHash("IsDead");
    private static readonly int DeathState = Animator.StringToHash("Base Layer.死亡状态");

    internal static void SetDead(Animator animator, bool dead, bool alreadyDeadAtSpawn = false)
    {
        if (animator == null || !animator.isActiveAndEnabled
            || animator.runtimeAnimatorController == null || !animator.HasState(0, DeathState)
            || animator.GetBool(DeadParameter) == dead)
            return;

        animator.SetBool(DeadParameter, dead);
        if (!dead)
            return;

        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.type == AnimatorControllerParameterType.Trigger)
                animator.ResetTrigger(parameter.nameHash);
            else if (parameter.type == AnimatorControllerParameterType.Bool
                && (parameter.name == "IsMoving" || parameter.name == "IsRunning"
                    || parameter.name == "IsMeditating"))
                animator.SetBool(parameter.nameHash, false);
        }

        animator.Play(DeathState, 0, alreadyDeadAtSpawn ? 1f : 0f);
    }
}
