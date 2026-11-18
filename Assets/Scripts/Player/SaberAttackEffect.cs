using Hovl;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
public sealed class SaberAttackEffect : MonoBehaviour
{
    [SerializeField] private HS_SwordMeshTrail attackTrail;

    private Animator animator;
    private bool playedThisAttack;

    private void Awake()
    {
        animator = GetComponent<Animator>();
    }

    // Animation events bound to the actual swing in the normal attack clip.
    public void PlaySaberAttackEffect()
    {
        if (!isActiveAndEnabled || attackTrail == null || playedThisAttack || !IsAttacking())
            return;

        playedThisAttack = true;
        attackTrail.StartTrail();
    }

    public void StopSaberAttackEffect()
    {
        if (playedThisAttack && attackTrail != null)
            attackTrail.StopTrail();
    }

    private void LateUpdate()
    {
        if (!playedThisAttack || IsAttacking()) return;

        StopSaberAttackEffect();
        playedThisAttack = false;
    }

    private void OnDisable()
    {
        playedThisAttack = false;
        if (attackTrail == null) return;

        attackTrail.StopTrail();
        attackTrail.ClearTrail();
    }

    private bool IsAttacking()
    {
        if (animator == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null)
            return false;

        AnimatorStateInfo state = animator.IsInTransition(0)
            ? animator.GetNextAnimatorStateInfo(0) : animator.GetCurrentAnimatorStateInfo(0);
        return state.IsTag("Attack");
    }
}
