using System;
using UnityEngine;
using UnityEngine.EventSystems;

public sealed partial class LocalPlayerInputController : MonoBehaviour
{
    private PredictedCharacterMotor predictedMotor;
    private GameSession movementSession;
    private Animator meditationAnimator;
    private PlayerEffectStateSource effectStateSource;
    private PlayerInputKeys previousDirectionTap;
    private double previousDirectionTapTime;
    private PlayerInputKeys rollDirection;
    private float rollCameraYaw;
    private double rollRequestedAt;
    private bool rollEntered;
    private LocalPlayerReconciler reconciler;
    private LocalPlayerVisualSmoother visualSmoother;
    private LocalPlayerCombatController combatController;
    private ThirdPersonCameraFollow cameraFollow;
    private long clientTick;
    private bool inputEnabled;
    private bool pendingMouseLeftDown;
    private bool pendingJumpPressed;
    private bool trackingMouseLeftClick;
    private bool suppressLeftClickAttack;
    private CombatTargetSelection rightClickTarget;
    private float mouseLeftDragDistance;
    private readonly BasicAttackMovementGate basicAttackMovementGate =
        new BasicAttackMovementGate();

    private static readonly int MeditatingParameter = Animator.StringToHash("IsMeditating");
    private const double DirectionDoubleTapSeconds = 0.25;
    private const double RollAnimationEnterTimeoutSeconds = 0.25;
    // Let normal movement braking finish as the roll animation returns to standing.
    private const float RollBrakingStartNormalizedTime = 0.68f;
    private const string RollTrigger = "Roll";
    private const string RollTag = "Roll";
    private const string MeditationTag = "Meditation";
    private const string LocomotionTag = "Locomotion";
    private const float MouseClickDragThreshold = 4f;
    private const float MovingHorizontalSpeedSquared = 0.0025f;

    public long CurrentClientTick => Math.Max(1L, clientTick);
    public bool IsInputEnabled => inputEnabled;

    private void Update()
    {
        if (!inputEnabled || NetworkManager.Instance?.MovementReady != true)
        {
            return;
        }

        if (cameraFollow == null)
        {
            cameraFollow = FindObjectOfType<ThirdPersonCameraFollow>();
        }
        cameraFollow?.SampleInput();

        bool gameplayBlocked = GameplayUiInputGuard.BlocksGameplayInput;
        PlayerInputKeys heldKeys = ReadKeys();
        PlayerInputActions heldActions = ReadHeldActions();
        bool jumpRequested = Input.GetKeyDown(KeyCode.Space);
        if (!gameplayBlocked && rollDirection == PlayerInputKeys.None && !IsRollAnimationPlaying())
            ApplyMeditationInput(Input.GetKeyDown(KeyCode.Q), heldKeys, heldActions, jumpRequested);

        gameplayBlocked |= IsMeditationBlockingInput();
        PlayerInputKeys taps = jumpRequested ? PlayerInputKeys.None : ReadDirectionalKeys(pressedThisFrame: true);
        UpdateRollInput(taps, Time.unscaledTimeAsDouble, !gameplayBlocked);
        gameplayBlocked |= rollDirection != PlayerInputKeys.None || IsRollAnimationPlaying();
        if (gameplayBlocked)
        {
            pendingMouseLeftDown = false;
            pendingJumpPressed = false;
            trackingMouseLeftClick = false;
            suppressLeftClickAttack = false;
            mouseLeftDragDistance = 0f;
        }

        basicAttackMovementGate.Observe(
            IsMovingForBasicAttack(heldKeys, heldActions), Time.unscaledTime);

        HandleTargetRightClick(gameplayBlocked);

        if (!gameplayBlocked && Input.GetMouseButtonDown(0))
        {
            trackingMouseLeftClick = true;
            suppressLeftClickAttack = false;
            mouseLeftDragDistance = 0f;
        }

        float mouseDragDelta = new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")).magnitude;
        if (trackingMouseLeftClick && Input.GetMouseButton(0))
            mouseLeftDragDistance += mouseDragDelta;

        if (trackingMouseLeftClick && Input.GetMouseButton(0) && Input.GetMouseButton(1))
            suppressLeftClickAttack = true;

        if (!gameplayBlocked && trackingMouseLeftClick && Input.GetMouseButtonUp(0))
        {
            pendingMouseLeftDown |= !suppressLeftClickAttack
                && mouseLeftDragDistance <= MouseClickDragThreshold
                && basicAttackMovementGate.CanAttack(Time.unscaledTime);
            trackingMouseLeftClick = false;
        }

        if (!gameplayBlocked && jumpRequested
            && predictedMotor != null && !predictedMotor.BlocksMovementForAttack())
        {
            pendingJumpPressed = true;
        }

        if (!UpdateMovement(CreateInputFrame(), MovementNow)) return;

        if (!gameplayBlocked && Input.GetKeyDown(KeyCode.Alpha1))
        {
            ResolveCombatController();
            combatController?.TrySendSkillSlotCast(1, CurrentClientTick);
        }

        if (!gameplayBlocked && Input.GetKeyDown(KeyCode.Alpha2))
        {
            ResolveCombatController();
            combatController?.TrySendSkillSlotCast(2, CurrentClientTick);
        }

        if (!gameplayBlocked && Input.GetKeyDown(KeyCode.Alpha3))
        {
            ResolveCombatController();
            combatController?.TrySendSkillSlotCast(3, CurrentClientTick);
        }

        if (!gameplayBlocked && Input.GetKeyDown(KeyCode.Alpha4))
        {
            ResolveCombatController();
            combatController?.TrySendSkillSlotCast(4, CurrentClientTick);
        }

        if (!gameplayBlocked && Input.GetKeyDown(KeyCode.Alpha5))
        {
            ResolveCombatController();
            combatController?.TrySendSkillSlotCast(5, CurrentClientTick);
        }

        if (!gameplayBlocked && Input.GetKeyDown(KeyCode.Alpha6))
        {
            ResolveCombatController();
            combatController?.TrySendSkillSlotCast(6, CurrentClientTick);
        }

        if (!gameplayBlocked && Input.GetKeyDown(KeyCode.Alpha7))
        {
            ResolveCombatController();
            combatController?.TrySendSkillSlotCast(7, CurrentClientTick);
        }

    }

    private void LateUpdate() => RefreshEffectState();

    private void OnDisable() => EndMeditation();

    private void RefreshEffectState()
    {
        if (effectStateSource == null) return;
        if (!isActiveAndEnabled || movementSession == null || !movementSession.IsWorldReady)
        {
            effectStateSource.Clear();
            return;
        }

        bool alive = !movementSession.IsLocalPlayerDead;
        // The intent closes the effect immediately on cancellation, while the
        // animation tag prevents showing it before the sitting transition starts.
        bool meditating = alive && inputEnabled && NetworkManager.Instance?.MovementReady == true
            && meditationAnimator != null && meditationAnimator.isActiveAndEnabled
            && meditationAnimator.GetBool(MeditatingParameter)
            && (meditationAnimator.GetCurrentAnimatorStateInfo(0).IsTag(MeditationTag)
                || (meditationAnimator.IsInTransition(0)
                    && meditationAnimator.GetNextAnimatorStateInfo(0).IsTag(MeditationTag)));
        effectStateSource.Publish(new PlayerEffectState(alive, meditating));
    }

    public void Bind(
        PredictedCharacterMotor motor,
        LocalPlayerReconciler playerReconciler,
        LocalPlayerVisualSmoother smoother,
        GameSession session)
    {
        ResetRollInput();
        predictedMotor = motor;
        movementSession = session;
        if (effectStateSource == null && !TryGetComponent(out effectStateSource))
            effectStateSource = gameObject.AddComponent<PlayerEffectStateSource>();
        meditationAnimator = null;
        Animator animator = motor?.VisualAnimator;
        if (animator != null)
        {
            foreach (AnimatorControllerParameter parameter in animator.parameters)
            {
                if (parameter.type == AnimatorControllerParameterType.Bool && parameter.nameHash == MeditatingParameter)
                {
                    meditationAnimator = animator;
                    break;
                }
            }
        }

        reconciler = playerReconciler;
        visualSmoother = smoother;
        ResolveCombatController();
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.FlushLocalMovement = FlushMovement;
            if (movementControl?.IsValidFor(NetworkManager.Instance.SceneSessionId) == true)
                predictedMotor.ApplyMovementSettings(movementControl);
        }
        RefreshEffectState();
    }

    public void SetInputEnabled(bool enabled, bool resetSequence = false)
    {
        inputEnabled = enabled;
        pendingMouseLeftDown = false;
        pendingJumpPressed = false;
        trackingMouseLeftClick = false;
        suppressLeftClickAttack = false;
        rightClickTarget = default;
        mouseLeftDragDistance = 0f;
        basicAttackMovementGate.Reset();
        cameraFollow = null;
        ResolveCombatController();
        combatController?.SetCombatEnabled(enabled, resetSequence);

        if (!enabled)
        {
            EndMeditation();
            ResetRollInput();
            return;
        }

        if (resetSequence)
        {
            clientTick = 0;
        }
        RefreshEffectState();
    }

    private void ResolveCombatController()
    {
        if (combatController == null)
        {
            combatController = GetComponent<LocalPlayerCombatController>();
        }

        if (combatController == null)
        {
            combatController = gameObject.AddComponent<LocalPlayerCombatController>();
        }
    }

    private void HandleTargetRightClick(bool uiBlocked)
    {
        if (uiBlocked || Input.GetMouseButton(0)
            || (cameraFollow != null && cameraFollow.IsRightMouseDragging))
        {
            rightClickTarget = default;
            return;
        }

        if (Input.GetMouseButtonDown(1))
        {
            rightClickTarget = default;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;

            LootClickBridge loot = FindObjectOfType<LootClickBridge>();
            if (loot != null && loot.isActiveAndEnabled && loot.TryGetLootUnderPointer(out _))
                return;

            if (MapTeleporterInteraction.FindTeleporterUnderPointer(Camera.main, Input.mousePosition) != null)
                return;

            ResolveCombatController();
            combatController?.TryPickTargetUnderPointer(out rightClickTarget);
        }

        if (!Input.GetMouseButtonUp(1))
            return;

        if (rightClickTarget.IsValid)
            combatController?.ToggleClickedTargetLock(rightClickTarget);
        rightClickTarget = default;
    }

    private void UpdateRollInput(PlayerInputKeys tappedDirections, double now, bool inputAvailable)
    {
        bool rollPlaying = IsRollAnimationPlaying();
        if (rollDirection != PlayerInputKeys.None)
        {
            if (!inputAvailable || !predictedMotor.IsGrounded || predictedMotor.BlocksMovementForAttack()
                || (!rollPlaying && (rollEntered || now > rollRequestedAt + RollAnimationEnterTimeoutSeconds)))
                ResetRollInput();
            else
                rollEntered |= rollPlaying;

            return;
        }

        if (!inputAvailable || rollPlaying)
        {
            previousDirectionTap = PlayerInputKeys.None;
            return;
        }

        if (tappedDirections == PlayerInputKeys.None) return;
        if (tappedDirections != PlayerInputKeys.W && tappedDirections != PlayerInputKeys.A
            && tappedDirections != PlayerInputKeys.S && tappedDirections != PlayerInputKeys.D)
        {
            previousDirectionTap = PlayerInputKeys.None;
            return;
        }

        bool doubleTapped = tappedDirections == previousDirectionTap
            && now <= previousDirectionTapTime + DirectionDoubleTapSeconds;
        previousDirectionTap = doubleTapped ? PlayerInputKeys.None : tappedDirections;
        previousDirectionTapTime = now;
        if (!doubleTapped || predictedMotor?.PlayGroundActionAnimation(RollTrigger) != true)
            return;

        rollDirection = tappedDirections;
        rollRequestedAt = now;
        rollCameraYaw = ReadCameraYaw();
    }

    private bool IsRollAnimationPlaying() => ReadRollAnimatorState().IsTag(RollTag);

    private AnimatorStateInfo ReadRollAnimatorState()
    {
        Animator animator = predictedMotor?.VisualAnimator;
        if (animator == null || !animator.isActiveAndEnabled)
            return default;

        return animator.IsInTransition(0)
            ? animator.GetNextAnimatorStateInfo(0) : animator.GetCurrentAnimatorStateInfo(0);
    }

    private void ResetRollInput()
    {
        Animator animator = predictedMotor?.VisualAnimator;
        if (rollDirection != PlayerInputKeys.None && animator != null && animator.isActiveAndEnabled)
            animator.ResetTrigger(RollTrigger);

        previousDirectionTap = PlayerInputKeys.None;
        rollDirection = PlayerInputKeys.None;
        rollEntered = false;
    }

    private void ApplyMeditationInput(bool sitPressed, PlayerInputKeys keys,
        PlayerInputActions actions, bool jumpPressed)
    {
        if (sitPressed)
            combatController?.ClearCombatTarget();

        if (meditationAnimator == null || !meditationAnimator.isActiveAndEnabled)
            return;

        if (HasDirectionalInput(keys) || HasBothMouseButtons(actions) || jumpPressed)
        {
            EndMeditation();
            return;
        }

        if (!sitPressed || !predictedMotor.IsGrounded || predictedMotor.BlocksMovementForAttack()
            || IsMeditationBlockingInput()
            || !meditationAnimator.GetCurrentAnimatorStateInfo(0).IsTag(LocomotionTag)
            || (meditationAnimator.IsInTransition(0)
                && !meditationAnimator.GetNextAnimatorStateInfo(0).IsTag(LocomotionTag)))
            return;

        meditationAnimator.SetBool(MeditatingParameter, true);
    }

    private void EndMeditation()
    {
        if (meditationAnimator != null && meditationAnimator.isActiveAndEnabled
            && meditationAnimator.GetBool(MeditatingParameter))
            meditationAnimator.SetBool(MeditatingParameter, false);
        RefreshEffectState();
    }

    private bool IsMeditationBlockingInput()
    {
        return meditationAnimator != null && meditationAnimator.isActiveAndEnabled
            && (meditationAnimator.GetBool(MeditatingParameter)
                || meditationAnimator.GetCurrentAnimatorStateInfo(0).IsTag(MeditationTag)
                || (meditationAnimator.IsInTransition(0)
                    && meditationAnimator.GetNextAnimatorStateInfo(0).IsTag(MeditationTag)));
    }

    private PlayerInputFrame CreateInputFrame()
    {
        bool meditationBlocked = IsMeditationBlockingInput();
        AnimatorStateInfo rollState = ReadRollAnimatorState();
        bool rollPlaying = rollState.IsTag(RollTag);
        bool rollBlocking = rollPlaying || rollDirection != PlayerInputKeys.None;
        bool gameplayPaused = GameplayUiInputGuard.BlocksGameplayInput || meditationBlocked;
        bool rollMoving = rollPlaying && rollDirection != PlayerInputKeys.None && !gameplayPaused
            && rollState.normalizedTime < RollBrakingStartNormalizedTime;
        bool uiBlocked = gameplayPaused || rollBlocking;
        PlayerInputKeys keys = rollMoving ? rollDirection | PlayerInputKeys.Shift
            : uiBlocked ? PlayerInputKeys.None : ReadKeys();
        PlayerInputActions actions = uiBlocked ? PlayerInputActions.None : ReadHeldActions();
        basicAttackMovementGate.Observe(
            IsMovingForBasicAttack(keys, actions), Time.unscaledTime);

        if (!uiBlocked && pendingMouseLeftDown
            && basicAttackMovementGate.CanAttack(Time.unscaledTime))
        {
            actions |= PlayerInputActions.PrimaryAction;
        }

        pendingMouseLeftDown = false;
        bool movementLocked = meditationBlocked || predictedMotor.BlocksMovementForAttack(actions);
        bool jumpPressed = !uiBlocked && !movementLocked && pendingJumpPressed;
        pendingJumpPressed = false;

        if (movementLocked)
        {
            keys = PlayerInputKeys.None;
            actions &= ~(PlayerInputActions.MouseLeftHeld | PlayerInputActions.MouseRightHeld);
        }

        float cameraYaw = rollMoving ? rollCameraYaw : ReadCameraYaw();
        bool hasMovementInput = HasDirectionalInput(keys)
            || HasBothMouseButtons(actions);

        return new PlayerInputFrame
        {
            ClientTick = clientTick + 1,
            Keys = keys,
            Actions = actions,
            LookYaw = cameraYaw,
            CameraYaw = cameraYaw,
            FaceCamera = cameraFollow != null
                && cameraFollow.CharacterLookActive
                && !movementLocked
                && !rollBlocking
                && !hasMovementInput,
            JumpPressed = jumpPressed,
            IsRunning = (keys & PlayerInputKeys.Shift) != 0
        };
    }

    private float ReadCameraYaw() => cameraFollow == null
        ? transform.eulerAngles.y : cameraFollow.CameraYaw;

    private static PlayerInputKeys ReadDirectionalKeys(bool pressedThisFrame)
    {
        PlayerInputKeys keys = PlayerInputKeys.None;
        if (DirectionKeyActive(KeyCode.W, KeyCode.UpArrow, pressedThisFrame)) keys |= PlayerInputKeys.W;
        if (DirectionKeyActive(KeyCode.A, KeyCode.LeftArrow, pressedThisFrame)) keys |= PlayerInputKeys.A;
        if (DirectionKeyActive(KeyCode.S, KeyCode.DownArrow, pressedThisFrame)) keys |= PlayerInputKeys.S;
        if (DirectionKeyActive(KeyCode.D, KeyCode.RightArrow, pressedThisFrame)) keys |= PlayerInputKeys.D;
        return keys;
    }

    private static bool DirectionKeyActive(KeyCode key, KeyCode arrow, bool pressedThisFrame) =>
        pressedThisFrame ? Input.GetKeyDown(key) || Input.GetKeyDown(arrow)
            : Input.GetKey(key) || Input.GetKey(arrow);

    private static PlayerInputKeys ReadKeys()
    {
        PlayerInputKeys keys = ReadDirectionalKeys(pressedThisFrame: false);
        if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            keys |= PlayerInputKeys.Shift;
        if (Input.GetKey(KeyCode.Space)) keys |= PlayerInputKeys.Space;
        return keys;
    }

    private static PlayerInputActions ReadHeldActions()
    {
        PlayerInputActions actions = PlayerInputActions.None;
        if (Input.GetMouseButton(0)) actions |= PlayerInputActions.MouseLeftHeld;
        if (Input.GetMouseButton(1)) actions |= PlayerInputActions.MouseRightHeld;
        return actions;
    }

    private static bool HasDirectionalInput(PlayerInputKeys keys)
    {
        const PlayerInputKeys movementKeys = PlayerInputKeys.W
            | PlayerInputKeys.A
            | PlayerInputKeys.S
            | PlayerInputKeys.D;
        return (keys & movementKeys) != 0;
    }

    private static bool HasBothMouseButtons(PlayerInputActions actions)
    {
        const PlayerInputActions bothHeld = PlayerInputActions.MouseLeftHeld
            | PlayerInputActions.MouseRightHeld;
        return (actions & bothHeld) == bothHeld;
    }

    private bool IsMovingForBasicAttack(PlayerInputKeys keys, PlayerInputActions actions)
    {
        if (HasDirectionalInput(keys) || HasBothMouseButtons(actions))
        {
            return true;
        }

        if (predictedMotor == null)
        {
            return false;
        }

        Vector3 velocity = predictedMotor.Velocity;
        return velocity.x * velocity.x + velocity.z * velocity.z
            > MovingHorizontalSpeedSquared;
    }
}
