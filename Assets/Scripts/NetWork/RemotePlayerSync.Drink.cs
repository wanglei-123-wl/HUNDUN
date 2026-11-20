using UnityEngine;

public partial class RemotePlayerSync
{
    private const string DrinkLocomotionTag = "Locomotion";
    private static readonly int RemoteDrinkState = Animator.StringToHash("Base Layer.吃药动画");
    private static readonly int DrinkGroundedParameter = Animator.StringToHash("IsGrounded");

    internal bool TryPlayDrinkAnimation()
    {
        if (!initialized || !isActiveAndEnabled || IsDead || remoteMeditating || remoteRollPlaying
            || movement?.mode != "normal" || animator == null || !animator.isActiveAndEnabled
            || animator.runtimeAnimatorController == null || animator.speed <= 0f
            || !movementGroundedParameter || !animator.GetBool(DrinkGroundedParameter)
            || !animator.HasState(0, RemoteDrinkState)
            || !animator.GetCurrentAnimatorStateInfo(0).IsTag(DrinkLocomotionTag)
            || (animator.IsInTransition(0) && !animator.GetNextAnimatorStateInfo(0).IsTag(DrinkLocomotionTag)))
            return false;

        // Enter the existing state now so a trigger cannot linger until a later grounded frame.
        animator.CrossFadeInFixedTime(RemoteDrinkState, 0.08f, 0, 0f);
        return true;
    }
}
