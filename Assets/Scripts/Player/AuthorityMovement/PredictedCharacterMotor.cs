using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public sealed class PredictedCharacterMotor : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float runSpeed = 8f;
    [SerializeField] private float acceleration = 20f;
    [SerializeField] private float deceleration = 30f;
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
    private const string RunningParameter = "IsRunning";
    private const string GroundedParameter = "IsGrounded";
    private const string JumpParameter = "Jump";
    private const string LocomotionTag = "Locomotion";

    private CharacterController controller;
    private Vector3 velocity;
    private float verticalVelocity;
    private bool grounded;
    private bool hasMoveBoolParameter;
    private bool hasAttackTriggerParameter;
    private bool hasRunningParameter;
    private bool hasGroundedParameter;
    private bool hasJumpParameter;
    private bool running;
    private string groundActionTrigger;
    private bool authoritativeAttackLocked;
    private bool attackAnimationLocked;
    private bool attackStateEntered;
    private float attackRequestTime;
    private float attackMovementUnlockTime = float.NegativeInfinity;

    internal Animator VisualAnimator => animator;
    public Vector3 Velocity => velocity;
    public bool IsGrounded => grounded;
    public float Gravity => gravity;

    private bool AttackMovementLocked => authoritativeAttackLocked || attackAnimationLocked
        || Time.unscaledTime < attackMovementUnlockTime;
    private bool CanStartPredictedAttack => animator != null && hasAttackTriggerParameter
        && !authoritativeAttackLocked && !attackAnimationLocked;

    public struct PredictionState
    {
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 Velocity;
        public float VerticalVelocity;
        public bool Grounded;
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

    public void Simulate(PlayerInputFrame input, float fixedDeltaTime, bool isReplay)
    {
        if (input == null || controller == null || fixedDeltaTime <= 0f)
        {
            return;
        }

        Vector2 movementInput = ReadMovementInput(input);
        bool movementLocked = BlocksMovementForAttack(input.Actions);
        bool hasMovementInput = !movementLocked && movementInput.sqrMagnitude > 0.0001f;
        float maxTurnDelta = turnSpeed * fixedDeltaTime;
        Vector3 desiredDirection = Vector3.zero;

        if (hasMovementInput)
        {
            desiredDirection = GetCameraRelativeDirection(movementInput, input.CameraYaw);
            float targetYaw = Mathf.Atan2(desiredDirection.x, desiredDirection.z) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0f, targetYaw, 0f);
        }
        else if (!movementLocked && input.FaceCamera)
        {
            RotateTowards(input.CameraYaw, maxTurnDelta);
        }

        Vector3 targetHorizontalVelocity = Vector3.zero;
        if (hasMovementInput)
        {
            float targetSpeed = input.IsRunning ? runSpeed : moveSpeed;
            targetHorizontalVelocity = desiredDirection * targetSpeed;
        }

        Vector3 horizontalVelocity = new Vector3(velocity.x, 0f, velocity.z);
        float velocityChangeRate = hasMovementInput ? acceleration : deceleration;
        horizontalVelocity = Vector3.MoveTowards(
            horizontalVelocity,
            targetHorizontalVelocity,
            velocityChangeRate * fixedDeltaTime
        );
        if (grounded && verticalVelocity < 0f)
        {
            verticalVelocity = 0f;
        }

        bool jumpRequestedOnGround = grounded && input.JumpPressed;
        if (jumpRequestedOnGround)
        {
            verticalVelocity = jumpSpeed;
        }

        verticalVelocity += gravity * fixedDeltaTime;
        CollisionFlags collisions = controller.Move(
            (horizontalVelocity + Vector3.up * verticalVelocity) * fixedDeltaTime
        );
        grounded = (collisions & CollisionFlags.Below) != 0;

        if (grounded && verticalVelocity < 0f)
        {
            verticalVelocity = 0f;
        }
        else if ((collisions & CollisionFlags.Above) != 0 && verticalVelocity > 0f)
        {
            verticalVelocity = 0f;
        }

        velocity = new Vector3(horizontalVelocity.x, verticalVelocity, horizontalVelocity.z);

        if (!isReplay)
        {
            PresentPredictedInput(input, jumpRequestedOnGround && !grounded && velocity.y > 0f);
        }
    }

    public PredictionState CapturePredictionState()
    {
        return new PredictionState
        {
            Position = transform.position,
            Rotation = transform.rotation,
            Velocity = velocity,
            VerticalVelocity = verticalVelocity,
            Grounded = grounded
        };
    }

    public void RestorePredictionState(PredictionState state)
    {
        SetTransform(state.Position, state.Rotation);
        velocity = state.Velocity;
        verticalVelocity = state.VerticalVelocity;
        grounded = state.Grounded;
    }

    public void PreservePredictedVertical(PredictionState state)
    {
        Vector3 position = transform.position;
        position.y = state.Position.y;
        SetTransform(position, transform.rotation);
        velocity.y = state.Velocity.y;
        verticalVelocity = state.VerticalVelocity;
        grounded = state.Grounded;
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

    internal bool PlayGroundActionAnimation(string triggerName)
    {
        if (animator == null || !grounded || AttackMovementLocked
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

            ResetGroundActionTrigger();
            groundActionTrigger = triggerName;
            animator.SetTrigger(triggerName);
            return true;
        }

        return false;
    }

    private void ResetGroundActionTrigger()
    {
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

    private void RotateTowards(float targetYaw, float maxTurnDelta)
    {
        float yaw = Mathf.MoveTowardsAngle(transform.eulerAngles.y, targetYaw, maxTurnDelta);
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
    }

    public void SetAuthoritativeState(PlayerAuthoritativeState state)
    {
        if (state == null)
        {
            return;
        }

        SetTransform(state.GetPosition(), state.GetRotation());
        velocity = state.GetVelocity();
        verticalVelocity = velocity.y;
        grounded = state.grounded;
        authoritativeAttackLocked = state.actionState != 0;
    }

    public void ResetAtSpawn(Vector3 position, Quaternion rotation)
    {
        SetTransform(position, rotation);
        velocity = Vector3.zero;
        verticalVelocity = 0f;
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
        verticalVelocity = 0f;
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
