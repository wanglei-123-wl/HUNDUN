using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public sealed partial class PredictedCharacterMotor : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float runSpeed = 8f;
    [SerializeField] private float gravity = -18f;
    [SerializeField] private float jumpSpeed = 7f;
    [SerializeField] private float turnSpeed = 180f;

    [Header("Animation")]
    [SerializeField] private Animator animator;
    [SerializeField] private string moveBoolName = "IsMoving";
    [SerializeField] private string attackTriggerName = "Attack";
    [SerializeField] private string attackStateName = "Attack";
    [SerializeField] private string attackStateTag = "Attack";
    [SerializeField] private float attackEnterTimeout = 0.75f;

    private const float AttackMovementRecoverySeconds = 0.3f;
    private const float BasicAttackIntervalSeconds = 0.2f;
    private const string RunningParameter = "IsRunning";
    private const string BackwardParameter = "IsMovingBackward";
    private const string GroundedParameter = "IsGrounded";
    private const string JumpParameter = "Jump";
    private const string LocomotionTag = "Locomotion";
    internal const string PotionTrigger = "UsePotion";
    private static readonly int PotionState = Animator.StringToHash("\u5403\u836f\u52a8\u753b");
    private const float PotionEnterTimeout = 0.75f;

    private CharacterController controller;
    private Vector3 velocity;
    private bool grounded;
    private bool hasMoveBoolParameter;
    private bool hasAttackTriggerParameter;
    private bool hasRunningParameter;
    private bool hasBackwardParameter;
    private bool hasGroundedParameter;
    private bool hasJumpParameter;
    private bool running;
    private string groundActionTrigger;
    private bool potionRequested;
    private bool potionEntered;
    private float potionRequestedAt;
    private bool authoritativeAttackLocked;
    private bool attackAnimationLocked;
    private bool attackStateEntered;
    private float attackRequestTime;
    private float attackMovementUnlockTime = float.NegativeInfinity;
    private float nextBasicAttackTime = float.NegativeInfinity;
    private uint completedBasicAttackCount;

    internal Animator VisualAnimator => animator;
    internal uint CompletedBasicAttackCount
    {
        get
        {
            RefreshAttackMovementLock();
            return completedBasicAttackCount;
        }
    }
    internal bool CanStartBasicAttack
    {
        get
        {
            RefreshAttackMovementLock();
            return CanStartPredictedAttack && Time.unscaledTime >= nextBasicAttackTime;
        }
    }
    public Vector3 Velocity => velocity;
    public bool IsGrounded => grounded;
    internal bool IsMovingBackward =>
        Vector3.Dot(new Vector3(velocity.x, 0f, velocity.z), transform.forward) < -0.05f;
    public float Gravity => gravity;

    private bool AttackMovementLocked => authoritativeAttackLocked || attackAnimationLocked
        || Time.unscaledTime < attackMovementUnlockTime;
    private bool CanStartPredictedAttack => animator != null && hasAttackTriggerParameter
        && !authoritativeAttackLocked && !attackAnimationLocked && !BlocksActionsForPotion;

    internal bool BlocksActionsForPotion
    {
        get
        {
            if (animator == null || !animator.isActiveAndEnabled)
            {
                ResetGroundActionTrigger();
                return false;
            }

            // Include both blends: input is locked before the trigger is
            // consumed and until the outgoing potion state has fully left.
            if (animator.GetCurrentAnimatorStateInfo(0).shortNameHash == PotionState
                || (animator.IsInTransition(0)
                    && animator.GetNextAnimatorStateInfo(0).shortNameHash == PotionState))
            {
                potionEntered = true;
                return true;
            }

            if (potionEntered || (potionRequested
                && Time.unscaledTime - potionRequestedAt >= PotionEnterTimeout))
                ResetGroundActionTrigger();
            return potionRequested;
        }
    }

    private void Awake()
    {
        controller = GetComponent<CharacterController>();

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>(true);
        }

        CacheAnimatorParameters();
    }

    private void Update()
    {
        RefreshAttackMovementLock();
    }

    public void BindAnimator(Animator newAnimator)
    {
        ResetGroundActionTrigger();
        animator = newAnimator;
        CacheAnimatorParameters();
        RefreshMovementAnimation();
    }

    public bool BlocksMovementForAttack(PlayerInputActions actions = PlayerInputActions.None)
    {
        RefreshAttackMovementLock();
        return AttackMovementLocked
            || ((actions & PlayerInputActions.PrimaryAction) != 0 && CanStartPredictedAttack);
    }

    public void ApplyMovementSettings(MovementEventControl control)
    {
        moveSpeed = control.walkSpeed;
        runSpeed = control.runSpeed;
        jumpSpeed = control.jumpSpeed;
        gravity = control.gravity;
        turnSpeed = control.turnSpeed;
        controller.radius = control.radius;
        controller.height = control.height;
        controller.center = new Vector3(0f, control.height * 0.5f, 0f);
        controller.minMoveDistance = 0f;
    }

    public void ApplyMovementControl(MovementEventControl control)
    {
        ApplyMovementSettings(control);
        SetTransform(control.Position, control.Rotation);
        grounded = control.grounded;
        velocity = Vector3.up * control.velocityY;
        running = false;
        ResetAttackMovementLock();
        RefreshMovementAnimation();
    }

    public Vector3 MovementDirection(PlayerInputFrame input) =>
        GetCameraRelativeDirection(ReadMovementInput(input), input.CameraYaw);

    public float TurnStep(float targetYaw, float deltaSeconds) =>
        Mathf.MoveTowardsAngle(transform.eulerAngles.y, targetYaw, turnSpeed * deltaSeconds);

    public float Speed(bool run) => run ? runSpeed : moveSpeed;
    public float JumpSpeed => jumpSpeed;

    public void SetHeading(float yaw) => transform.rotation = Quaternion.Euler(0f, yaw, 0f);

    public void SetLocalMotion(Vector3 horizontal, float vertical, bool onGround)
    {
        velocity = horizontal + Vector3.up * vertical;
        grounded = onGround;
    }

    public void PresentPredictedInput(PlayerInputFrame input, bool jumpStarted = false)
    {
        running = input.IsRunning;
        if (input.HasAction(PlayerInputActions.PrimaryAction))
        {
            TryStartPredictedAttack();
        }

        RefreshMovementAnimation();
        if (jumpStarted && hasJumpParameter && animator != null)
        {
            animator.SetTrigger(JumpParameter);
        }
    }

    public void RefreshMovementAnimation()
    {
        if (animator == null)
        {
            return;
        }

        bool isMoving = new Vector2(velocity.x, velocity.z).sqrMagnitude > 0.0025f
            && !AttackMovementLocked;
        if (hasMoveBoolParameter)
        {
            animator.SetBool(moveBoolName, isMoving);
        }
        if (hasRunningParameter)
        {
            animator.SetBool(RunningParameter, isMoving && running);
        }
        if (hasBackwardParameter)
        {
            animator.SetBool(BackwardParameter, isMoving && grounded && IsMovingBackward);
        }
        if (hasGroundedParameter)
        {
            animator.SetBool(GroundedParameter, grounded);
        }
        if (hasJumpParameter && grounded)
        {
            animator.ResetTrigger(JumpParameter);
        }
        if (!grounded)
        {
            ResetGroundActionTrigger();
        }
    }

    internal bool CanPlayGroundActionAnimation(string triggerName)
    {
        if (animator == null || !animator.isActiveAndEnabled || !grounded || AttackMovementLocked
            || BlocksActionsForPotion
            || string.IsNullOrWhiteSpace(triggerName)
            || !animator.GetCurrentAnimatorStateInfo(0).IsTag(LocomotionTag)
            || (animator.IsInTransition(0)
                && !animator.GetNextAnimatorStateInfo(0).IsTag(LocomotionTag)))
        {
            return false;
        }

        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.type != AnimatorControllerParameterType.Trigger
                || parameter.name != triggerName)
            {
                continue;
            }

            return true;
        }

        return false;
    }

    internal bool PlayGroundActionAnimation(string triggerName)
    {
        if (!CanPlayGroundActionAnimation(triggerName)) return false;
        ResetGroundActionTrigger();
        groundActionTrigger = triggerName;
        if (triggerName == PotionTrigger)
        {
            potionRequested = true;
            potionRequestedAt = Time.unscaledTime;
        }
        animator.SetTrigger(triggerName);
        return true;
    }

    private void ResetGroundActionTrigger()
    {
        potionRequested = false;
        potionEntered = false;
        if (groundActionTrigger != null)
        {
            if (animator != null)
            {
                animator.ResetTrigger(groundActionTrigger);
            }
            groundActionTrigger = null;
        }
    }

    private static Vector2 ReadMovementInput(PlayerInputFrame input)
    {
        float moveX = 0f;
        float moveZ = 0f;
        if (input.HasKey(PlayerInputKeys.W)) moveZ += 1f;
        if (input.HasKey(PlayerInputKeys.S)) moveZ -= 1f;
        if (input.HasKey(PlayerInputKeys.A)) moveX -= 1f;
        if (input.HasKey(PlayerInputKeys.D)) moveX += 1f;

        bool bothMouseButtonsHeld = input.HasAction(PlayerInputActions.MouseLeftHeld)
            && input.HasAction(PlayerInputActions.MouseRightHeld);
        if (bothMouseButtonsHeld)
        {
            moveZ += 1f;
        }

        Vector2 movementInput = new Vector2(moveX, moveZ);
        return movementInput.sqrMagnitude > 1f ? movementInput.normalized : movementInput;
    }

    private static Vector3 GetCameraRelativeDirection(Vector2 movementInput, float cameraYaw)
    {
        float yawRadians = cameraYaw * Mathf.Deg2Rad;
        Vector3 cameraForward = new Vector3(Mathf.Sin(yawRadians), 0f, Mathf.Cos(yawRadians));
        Vector3 cameraRight = new Vector3(Mathf.Cos(yawRadians), 0f, -Mathf.Sin(yawRadians));
        Vector3 desiredDirection = cameraForward * movementInput.y
            + cameraRight * movementInput.x;
        return desiredDirection.sqrMagnitude > 0.0001f
            ? desiredDirection.normalized
            : Vector3.zero;
    }

    public void SetAuthoritativeState(PlayerAuthoritativeState state)
    {
        if (state == null)
        {
            return;
        }

        SetTransform(state.GetPosition(), state.GetRotation());
        velocity = state.GetVelocity();
        grounded = state.grounded;
        authoritativeAttackLocked = state.actionState != 0;
    }

    public void ResetAtSpawn(Vector3 position, Quaternion rotation)
    {
        SetTransform(position, rotation);
        velocity = Vector3.zero;
        grounded = controller != null && controller.enabled
            && (controller.Move(Vector3.down * 0.001f) & CollisionFlags.Below) != 0;
        running = false;
        ResetAttackMovementLock();
        ResetGroundActionTrigger();
        RefreshMovementAnimation();
    }

    public void StopForDeath()
    {
        velocity = Vector3.zero;
        grounded = false;
        running = false;
        ResetAttackMovementLock();

        if (animator == null)
        {
            return;
        }

        RefreshMovementAnimation();
        ResetGroundActionTrigger();
        if (hasJumpParameter)
        {
            animator.ResetTrigger(JumpParameter);
        }

        if (hasAttackTriggerParameter)
        {
            animator.ResetTrigger(attackTriggerName);
        }
    }

    private void SetTransform(Vector3 position, Quaternion rotation)
    {
        bool controllerWasEnabled = controller != null && controller.enabled;

        if (controllerWasEnabled)
        {
            controller.enabled = false;
        }

        transform.SetPositionAndRotation(position, rotation);

        if (controllerWasEnabled)
        {
            controller.enabled = true;
        }
    }

    private void TryStartPredictedAttack()
    {
        if (!CanStartPredictedAttack)
        {
            return;
        }

        attackAnimationLocked = true;
        attackStateEntered = false;
        attackRequestTime = Time.unscaledTime;
        ResetGroundActionTrigger();

        if (hasMoveBoolParameter)
        {
            animator.SetBool(moveBoolName, false);
        }

        animator.ResetTrigger(attackTriggerName);
        animator.SetTrigger(attackTriggerName);
    }

    private void RefreshAttackMovementLock()
    {
        if (animator == null)
        {
            return;
        }

        AnimatorStateInfo currentState = animator.GetCurrentAnimatorStateInfo(0);
        bool animatorInAttack = IsAttackState(currentState);

        if (!animatorInAttack && animator.IsInTransition(0))
        {
            animatorInAttack = IsAttackState(animator.GetNextAnimatorStateInfo(0));
        }

        if (animatorInAttack)
        {
            attackAnimationLocked = true;
            attackStateEntered = true;
            return;
        }

        if (attackStateEntered)
        {
            attackAnimationLocked = false;
            attackStateEntered = false;
            completedBasicAttackCount++;
            nextBasicAttackTime = Time.unscaledTime + BasicAttackIntervalSeconds;
            attackMovementUnlockTime = Time.unscaledTime + AttackMovementRecoverySeconds;
            return;
        }

        if (attackAnimationLocked
            && Time.unscaledTime - attackRequestTime >= attackEnterTimeout)
        {
            attackAnimationLocked = false;
            animator.ResetTrigger(attackTriggerName);
        }
    }

    private void ResetAttackMovementLock()
    {
        authoritativeAttackLocked = false;
        attackAnimationLocked = false;
        attackStateEntered = false;
        attackMovementUnlockTime = float.NegativeInfinity;
        nextBasicAttackTime = float.NegativeInfinity;
    }

    private bool IsAttackState(AnimatorStateInfo stateInfo)
    {
        if (!string.IsNullOrEmpty(attackStateTag) && stateInfo.IsTag(attackStateTag))
        {
            return true;
        }

        return !string.IsNullOrEmpty(attackStateName)
            && stateInfo.shortNameHash == Animator.StringToHash(attackStateName);
    }

    private void CacheAnimatorParameters()
    {
        hasMoveBoolParameter = false;
        hasAttackTriggerParameter = false;
        hasRunningParameter = false;
        hasBackwardParameter = false;
        hasGroundedParameter = false;
        hasJumpParameter = false;

        if (animator == null)
        {
            return;
        }

        AnimatorControllerParameter[] parameters = animator.parameters;

        for (int index = 0; index < parameters.Length; index++)
        {
            AnimatorControllerParameter parameter = parameters[index];

            if (parameter.type == AnimatorControllerParameterType.Bool && parameter.name == moveBoolName)
            {
                hasMoveBoolParameter = true;
            }

            if (parameter.type == AnimatorControllerParameterType.Trigger && parameter.name == attackTriggerName)
            {
                hasAttackTriggerParameter = true;
            }
            if (parameter.type == AnimatorControllerParameterType.Bool && parameter.name == RunningParameter)
            {
                hasRunningParameter = true;
            }
            if (parameter.type == AnimatorControllerParameterType.Bool && parameter.name == BackwardParameter)
            {
                hasBackwardParameter = true;
            }
            if (parameter.type == AnimatorControllerParameterType.Bool && parameter.name == GroundedParameter)
            {
                hasGroundedParameter = true;
            }
            if (parameter.type == AnimatorControllerParameterType.Trigger && parameter.name == JumpParameter)
            {
                hasJumpParameter = true;
            }
        }
    }
}
