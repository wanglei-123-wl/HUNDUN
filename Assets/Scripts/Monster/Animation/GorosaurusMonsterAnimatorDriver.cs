using UnityEngine;

public sealed class GorosaurusMonsterAnimatorDriver : MonoBehaviour, IMonsterAnimatorDriver
{
    private const float WalkStartSpeed = .10f;
    private const float WalkStopSpeed = .05f;
    private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");
    private static readonly int AttackHash = Animator.StringToHash("Attack");
    private static readonly int DeadHash = Animator.StringToHash("Dead");
    private static readonly int IdleStateHash = Animator.StringToHash("Base Layer.待机");
    private static readonly int AttackStateHash = Animator.StringToHash("Base Layer.攻击");

    [SerializeField] private Animator animator;
    [SerializeField] private MonsterNetworkView monsterNetworkView;

    private bool hasIsMoving;
    private bool hasAttack;
    private bool hasDead;
    private bool isDead;
    private bool allowWalk;
    private MonsterTransformInterpolator motion;

    private void Awake()
    {
        ResolveAnimator();
        motion = GetComponentInParent<MonsterTransformInterpolator>();
        CacheParameters();
    }

    private void Update()
    {
        if (!allowWalk || isDead || motion == null || animator == null || !hasIsMoving)
            return;

        // Chase also covers standing in range while the server waits for the next attack.
        Vector3 velocity = motion.CurrentVelocity;
        float threshold = animator.GetBool(IsMovingHash) ? WalkStopSpeed : WalkStartSpeed;
        SetMoving(velocity.x * velocity.x + velocity.z * velocity.z >= threshold * threshold);
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
            case "patrol":
            case "chase":
            case "chasing":
            case "return":
            case "returning":
            case "fly":
            case "flying":
                SetLocomotion(MonsterLocomotionMode.Flying);
                break;
            default:
                SetLocomotion(MonsterLocomotionMode.Grounded);
                break;
        }
    }

    public void ResetView()
    {
        isDead = false;
        allowWalk = false;
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
        allowWalk = mode == MonsterLocomotionMode.Flying;
        if (!allowWalk) SetMoving(false);
    }

    public void SetMoveSpeed(float normalizedSpeed)
    {
        // Walking follows the network interpolator's displayed movement.
    }

    public void PlayAction(int actionSlot, float actionSpeed)
    {
        if (isDead || actionSlot != 0 || animator == null || !hasAttack)
        {
            return;
        }

        SetMoving(false);

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
        allowWalk = false;
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
            Debug.LogError("GorosaurusMonsterAnimatorDriver requires an Animator with a controller.", this);
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
            Debug.LogError("Gorosaurus Animator must define IsMoving (Bool), Attack (Trigger), and Dead (Bool).", this);
        }
    }
}
