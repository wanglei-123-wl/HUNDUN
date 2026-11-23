using UnityEngine;

public sealed class TyranopodeMonsterAnimatorDriver : MonoBehaviour, IMonsterAnimatorDriver
{
    private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");
    private static readonly int AttackHash = Animator.StringToHash("Attack");
    private static readonly int DeadHash = Animator.StringToHash("Dead");
    private static readonly int IdleStateHash = Animator.StringToHash("Base Layer.待机");
    private static readonly int WalkStateHash = Animator.StringToHash("Base Layer.向前爬行");
    private static readonly int AttackStateHash = Animator.StringToHash("Base Layer.咬击");

    [SerializeField] private Animator animator;
    [SerializeField] private MonsterNetworkView monsterNetworkView;

    private bool hasIsMoving;
    private bool hasAttack;
    private bool hasDead;
    private bool isDead;

    private void Awake()
    {
        ResolveAnimator();
        CacheParameters();
    }

    private void OnEnable()
    {
        if (monsterNetworkView == null)
        {
            monsterNetworkView = GetComponentInParent<MonsterNetworkView>();
        }

        if (monsterNetworkView != null)
        {
            monsterNetworkView.OnHealthChanged += HandleHealthChanged;
        }
    }

    private void OnDisable()
    {
        if (monsterNetworkView != null)
        {
            monsterNetworkView.OnHealthChanged -= HandleHealthChanged;
        }
    }

    public void RebindAnimator()
    {
        ResolveAnimator();
        if (animator == null)
        {
            return;
        }

        animator.Rebind();
        animator.Update(0f);
        CacheParameters();
        ResetView();
    }

    public void BootstrapState(string state)
    {
        if (isDead)
        {
            return;
        }

        switch (string.IsNullOrEmpty(state) ? "" : state.Trim().ToLowerInvariant())
        {
            case "dead":
                SetDead();
                break;
            case "attack":
                PlayAction(0, 1f);
                break;
            case "chase":
            case "chasing":
            case "return":
            case "returning":
            case "fly":
            case "flying":
                SetMoving(true);
                PlayState(WalkStateHash);
                break;
            default:
                SetMoving(false);
                PlayState(IdleStateHash);
                break;
        }
    }

    public void ResetView()
    {
        isDead = false;
        if (animator == null)
        {
            return;
        }

        if (hasAttack)
        {
            animator.ResetTrigger(AttackHash);
        }

        if (hasDead)
        {
            animator.SetBool(DeadHash, false);
        }

        SetMoving(false);
        PlayState(IdleStateHash);
    }

    public void SetLocomotion(MonsterLocomotionMode mode)
    {
        SetMoving(mode == MonsterLocomotionMode.Flying);
    }

    public void SetMoveSpeed(float normalizedSpeed)
    {
        // The current dynamic snapshot can omit speed; locomotion is the stop/start signal.
    }

    public void PlayAction(int actionSlot, float actionSpeed)
    {
        if (isDead || actionSlot != 0 || animator == null || !hasAttack)
        {
            return;
        }

        if (animator.HasState(0, AttackStateHash)
            && animator.GetCurrentAnimatorStateInfo(0).fullPathHash == AttackStateHash)
        {
            animator.ResetTrigger(AttackHash);
            animator.Play(AttackStateHash, 0, 0f);
            return;
        }

        animator.ResetTrigger(AttackHash);
        animator.SetTrigger(AttackHash);
    }

    private void HandleHealthChanged(int hp, int maxHp)
    {
        if (maxHp > 0 && hp <= 0)
        {
            SetDead();
        }
    }

    private void SetDead()
    {
        if (isDead)
        {
            return;
        }

        SetMoving(false);
        isDead = true;
        if (animator == null)
        {
            return;
        }

        if (hasAttack)
        {
            animator.ResetTrigger(AttackHash);
        }

        if (hasDead)
        {
            animator.SetBool(DeadHash, true);
        }
    }

    private void SetMoving(bool moving)
    {
        if (!isDead && animator != null && hasIsMoving)
        {
            animator.SetBool(IsMovingHash, moving);
        }
    }

    private void PlayState(int stateHash)
    {
        if (animator != null && animator.HasState(0, stateHash))
        {
            animator.Play(stateHash, 0, 0f);
        }
    }

    private void ResolveAnimator()
    {
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>(true);
        }
    }

    private void CacheParameters()
    {
        hasIsMoving = false;
        hasAttack = false;
        hasDead = false;

        if (animator == null || animator.runtimeAnimatorController == null)
        {
            Debug.LogError("TyranopodeMonsterAnimatorDriver requires an Animator with a controller.", this);
            return;
        }

        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.nameHash == IsMovingHash && parameter.type == AnimatorControllerParameterType.Bool)
            {
                hasIsMoving = true;
            }
            else if (parameter.nameHash == AttackHash && parameter.type == AnimatorControllerParameterType.Trigger)
            {
                hasAttack = true;
            }
            else if (parameter.nameHash == DeadHash && parameter.type == AnimatorControllerParameterType.Bool)
            {
                hasDead = true;
            }
        }

        if (!hasIsMoving || !hasAttack || !hasDead)
        {
            Debug.LogError("Tyranopode Animator must define IsMoving (Bool), Attack (Trigger), and Dead (Bool).", this);
        }
    }
}
