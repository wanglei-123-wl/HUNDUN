using UnityEngine;

public class FlyingMonsterAnimatorDriver : MonoBehaviour, IMonsterAnimatorDriver
{
    private static readonly int LocomotionModeHash = Animator.StringToHash("LocomotionMode");
    private static readonly int MoveSpeedHash = Animator.StringToHash("MoveSpeed");
    private static readonly int ActionSlotHash = Animator.StringToHash("ActionSlot");
    private static readonly int ActionSpeedHash = Animator.StringToHash("ActionSpeed");
    private static readonly int ActionHash = Animator.StringToHash("Action");
    private static readonly int DeadHash = Animator.StringToHash("Dead");
    private static readonly int GroundIdleStateHash = Animator.StringToHash("Base Layer.GroundIdle");
    private static readonly int TakeOffStateHash = Animator.StringToHash("Base Layer.TakeOff");
    private static readonly int FlyingStateHash = Animator.StringToHash("Base Layer.Flying");
    private static readonly int LandingStateHash = Animator.StringToHash("Base Layer.Landing");
    private static readonly int ActionStateHash = Animator.StringToHash("Base Layer.Action00");
    private static readonly int DeathStateHash = Animator.StringToHash("Base Layer.Death2");

    [SerializeField] private Animator animator;
    [SerializeField] private float minimumActionSpeed = 0.1f;
    [SerializeField] private float maximumActionSpeed = 3f;
    [SerializeField] private bool logMissingParameters = true;

    private bool hasLocomotionMode;
    private bool hasMoveSpeed;
    private bool hasActionSlot;
    private bool hasActionSpeed;
    private bool hasActionTrigger;
    private bool hasDead;
    private bool isDead;

    private void Awake()
    {
        ResolveAnimator();
        CacheParameters();
    }

    public void BindAnimator(Animator newAnimator)
    {
        animator = newAnimator;
        CacheParameters();
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
        ResolveAnimator();

        if (animator == null || isDead)
        {
            return;
        }

        string normalizedState = string.IsNullOrEmpty(state)
            ? "idle"
            : state.Trim().ToLowerInvariant();

        switch (normalizedState)
        {
            case "takeoff":
            case "takingoff":
            case "take_off":
                SetLocomotion(MonsterLocomotionMode.TakingOff);
                SetMoveSpeed(0f);
                PlayState(TakeOffStateHash);
                break;

            case "chase":
            case "chasing":
            case "return":
            case "returning":
            case "fly":
            case "flying":
                SetLocomotion(MonsterLocomotionMode.Flying);
                SetMoveSpeed(1f);
                PlayState(FlyingStateHash);
                break;

            case "attack":
                SetLocomotion(MonsterLocomotionMode.Flying);
                SetMoveSpeed(1f);
                PlayState(ActionStateHash);
                break;

            case "landing":
            case "land":
                SetLocomotion(MonsterLocomotionMode.Landing);
                SetMoveSpeed(0f);
                PlayState(LandingStateHash);
                break;

            case "dead":
                SetDead();
                break;

            case "idle":
            default:
                SetLocomotion(MonsterLocomotionMode.Grounded);
                SetMoveSpeed(0f);
                PlayState(GroundIdleStateHash);
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

        if (hasDead)
        {
            animator.SetBool(DeadHash, false);
        }

        if (hasActionTrigger)
        {
            animator.ResetTrigger(ActionHash);
        }

        if (hasLocomotionMode)
        {
            animator.SetInteger(LocomotionModeHash, (int)MonsterLocomotionMode.Grounded);
        }

        if (hasMoveSpeed)
        {
            animator.SetFloat(MoveSpeedHash, 0f);
        }

        if (hasActionSlot)
        {
            animator.SetInteger(ActionSlotHash, 0);
        }

        if (hasActionSpeed)
        {
            animator.SetFloat(ActionSpeedHash, 1f);
        }
    }

    public void SetLocomotion(MonsterLocomotionMode mode)
    {
        if (!isDead && animator != null && hasLocomotionMode)
        {
            animator.SetInteger(LocomotionModeHash, (int)mode);
        }
    }

    public void SetMoveSpeed(float normalizedSpeed)
    {
        if (!isDead && animator != null && hasMoveSpeed)
        {
            animator.SetFloat(MoveSpeedHash, Mathf.Clamp01(normalizedSpeed));
        }
    }

    public void PlayAction(int actionSlot, float actionSpeed)
    {
        if (isDead || animator == null || !hasActionTrigger)
        {
            return;
        }

        if (hasActionSlot)
        {
            animator.SetInteger(ActionSlotHash, Mathf.Max(0, actionSlot));
        }

        if (hasActionSpeed)
        {
            animator.SetFloat(
                ActionSpeedHash,
                Mathf.Clamp(actionSpeed, minimumActionSpeed, maximumActionSpeed)
            );
        }

        animator.ResetTrigger(ActionHash);
        animator.SetTrigger(ActionHash);
    }

    private void SetDead()
    {
        isDead = true;
        if (animator == null)
        {
            return;
        }

        if (hasActionTrigger)
        {
            animator.ResetTrigger(ActionHash);
        }

        if (hasMoveSpeed)
        {
            animator.SetFloat(MoveSpeedHash, 0f);
        }

        if (hasDead)
        {
            animator.SetBool(DeadHash, true);
        }

        PlayState(DeathStateHash);
    }

    private void PlayState(int stateHash)
    {
        if (animator == null || !animator.HasState(0, stateHash))
        {
            return;
        }

        animator.Play(stateHash, 0, 0f);
        animator.Update(0f);
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
        ResolveAnimator();

        hasLocomotionMode = false;
        hasMoveSpeed = false;
        hasActionSlot = false;
        hasActionSpeed = false;
        hasActionTrigger = false;
        hasDead = false;

        if (animator == null)
        {
            if (logMissingParameters)
            {
                Debug.LogError("FlyingMonsterAnimatorDriver requires an Animator.", this);
            }

            return;
        }

        if (animator.runtimeAnimatorController == null)
        {
            if (logMissingParameters)
            {
                Debug.LogError(
                    "FlyingMonsterAnimatorDriver Animator requires a Runtime Animator Controller.",
                    this
                );
            }

            return;
        }

        AnimatorControllerParameter[] parameters = animator.parameters;

        for (int i = 0; i < parameters.Length; i++)
        {
            AnimatorControllerParameter parameter = parameters[i];

            if (parameter.nameHash == LocomotionModeHash && parameter.type == AnimatorControllerParameterType.Int)
            {
                hasLocomotionMode = true;
            }
            else if (parameter.nameHash == MoveSpeedHash && parameter.type == AnimatorControllerParameterType.Float)
            {
                hasMoveSpeed = true;
            }
            else if (parameter.nameHash == ActionSlotHash && parameter.type == AnimatorControllerParameterType.Int)
            {
                hasActionSlot = true;
            }
            else if (parameter.nameHash == ActionSpeedHash && parameter.type == AnimatorControllerParameterType.Float)
            {
                hasActionSpeed = true;
            }
            else if (parameter.nameHash == ActionHash && parameter.type == AnimatorControllerParameterType.Trigger)
            {
                hasActionTrigger = true;
            }
            else if (parameter.nameHash == DeadHash && parameter.type == AnimatorControllerParameterType.Bool)
            {
                hasDead = true;
            }
        }

        if (logMissingParameters)
        {
            LogMissingParameter(hasLocomotionMode, "LocomotionMode", "Int");
            LogMissingParameter(hasMoveSpeed, "MoveSpeed", "Float");
            LogMissingParameter(hasActionSlot, "ActionSlot", "Int");
            LogMissingParameter(hasActionSpeed, "ActionSpeed", "Float");
            LogMissingParameter(hasActionTrigger, "Action", "Trigger");
            LogMissingParameter(hasDead, "Dead", "Bool");
        }
    }

    private void LogMissingParameter(bool exists, string parameterName, string parameterType)
    {
        if (!exists)
        {
            Debug.LogWarning(
                "Animator is missing required parameter: " + parameterName + " (" + parameterType + ").",
                this
            );
        }
    }
}
