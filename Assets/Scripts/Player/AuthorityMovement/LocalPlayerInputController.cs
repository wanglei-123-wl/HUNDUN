using System;
using System.Collections.Generic;
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
    private bool rollReported;
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
    private CombatTargetSelection repeatingAttackTarget;
    private uint repeatingAttackCompletionCount;
    private long repeatingAttackConnectionEpoch;
    private string repeatingAttackSceneSessionId;
    private readonly HashSet<KeyCode> worldMovementKeys = new HashSet<KeyCode>();
    private readonly HashSet<KeyCode> uiMovementKeys = new HashSet<KeyCode>();

    private static readonly int MeditatingParameter = Animator.StringToHash("IsMeditating");
    private static readonly int SeatedLoopState = Animator.StringToHash("Base Layer.打坐中");
    private const double DirectionDoubleTapSeconds = 0.25;
    private const double RollAnimationEnterTimeoutSeconds = 0.25;
    // Let normal movement braking finish as the roll animation returns to standing.
    private const float RollBrakingStartNormalizedTime = 0.68f;
    private const string RollTrigger = "Roll";
    private const string RollTag = "Roll";
    private const string MeditationTag = "Meditation";
    private const float MouseClickDragThreshold = 4f;
    private const float MovingHorizontalSpeedSquared = 0.0025f;

    public long CurrentClientTick => Math.Max(1L, clientTick);
    public bool IsInputEnabled => inputEnabled;

    private void Update()
    {
        UpdateMeditationRequest();
        if (!inputEnabled || NetworkManager.Instance?.MovementReady != true)
        {
            CancelCombatIntent();
            return;
        }

        if (cameraFollow == null)
        {
            cameraFollow = FindObjectOfType<ThirdPersonCameraFollow>();
        }
        cameraFollow?.SampleInput();

        if (predictedMotor?.BlocksActionsForPotion == true)
        {
            HoldMovementForPotion(MovementNow);
            return;
        }

        bool gameplayBlocked = GameplayUiInputGuard.BlocksWorldActions;
        bool pointerBlocked = GameplayUiInputGuard.BlocksWorldPointerInput;
        bool keyboardBlocked = GameplayUiInputGuard.BlocksWorldKeyboardInput;
        PlayerInputKeys heldKeys = ReadKeys();
        PlayerInputActions heldActions = ReadHeldActions();
        bool jumpRequested = !keyboardBlocked && WorldKeyActive(KeyCode.Space, pressedThisFrame: true);
        HandleManualCombatCancellation(gameplayBlocked, keyboardBlocked, pointerBlocked,
            heldKeys, heldActions, jumpRequested);
        if (!gameplayBlocked && !keyboardBlocked && rollDirection == PlayerInputKeys.None && !IsRollAnimationPlaying())
            ApplyMeditationInput(Input.GetKeyDown(KeyCode.Q), heldKeys, heldActions, jumpRequested);

        heldKeys = FilterMeditationKeys(heldKeys);
        heldActions = FilterMeditationActions(heldActions);
        PlayerInputKeys taps = jumpRequested || keyboardBlocked ? PlayerInputKeys.None : ReadDirectionalKeys(pressedThisFrame: true);
        UpdateRollInput(FilterMeditationKeys(taps), Time.unscaledTimeAsDouble,
            !gameplayBlocked && !IsMeditationBlockingInput());
        gameplayBlocked |= IsMeditationBlockingInput()
            || rollDirection != PlayerInputKeys.None || IsRollAnimationPlaying();
        if (gameplayBlocked) CancelCombatIntent();
        if (gameplayBlocked)
        {
            ClearPendingGameplayInput();
        }
        else if (pointerBlocked)
        {
            pendingMouseLeftDown = false;
            trackingMouseLeftClick = false;
            suppressLeftClickAttack = false;
            mouseLeftDragDistance = 0f;
            rightClickTarget = default;
        }

        basicAttackMovementGate.Observe(
            IsMovingForBasicAttack(heldKeys, heldActions), Time.unscaledTime);

        HandleTargetRightClick(gameplayBlocked || pointerBlocked);

        if (!gameplayBlocked && !pointerBlocked && Input.GetMouseButtonDown(0))
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

        if (!gameplayBlocked && !pointerBlocked && trackingMouseLeftClick && Input.GetMouseButtonUp(0))
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

        if (!gameplayBlocked && basicAttackMovementGate.CanAttack(Time.unscaledTime)
            && !predictedMotor.BlocksMovementForAttack())
            combatController?.CompleteApproachSkill(CurrentClientTick);

        if (!gameplayBlocked && !keyboardBlocked && Input.GetKeyDown(KeyCode.Alpha1))
        {
            ResolveCombatController();
            combatController?.TrySendSkillSlotCast(1, CurrentClientTick);
        }

        if (!gameplayBlocked && !keyboardBlocked && Input.GetKeyDown(KeyCode.Alpha2))
        {
            ResolveCombatController();
            combatController?.TrySendSkillSlotCast(2, CurrentClientTick);
        }

        if (!gameplayBlocked && !keyboardBlocked && Input.GetKeyDown(KeyCode.Alpha3))
        {
            ResolveCombatController();
            combatController?.TrySendSkillSlotCast(3, CurrentClientTick);
        }

        if (!gameplayBlocked && !keyboardBlocked && Input.GetKeyDown(KeyCode.Alpha4))
        {
            ResolveCombatController();
            combatController?.TrySendSkillSlotCast(4, CurrentClientTick);
        }

        if (!gameplayBlocked && !keyboardBlocked && Input.GetKeyDown(KeyCode.Alpha5))
        {
            ResolveCombatController();
            combatController?.TrySendSkillSlotCast(5, CurrentClientTick);
        }

        if (!gameplayBlocked && !keyboardBlocked && Input.GetKeyDown(KeyCode.Alpha6))
        {
            ResolveCombatController();
            combatController?.TrySendSkillSlotCast(6, CurrentClientTick);
        }

        if (!gameplayBlocked && !keyboardBlocked && Input.GetKeyDown(KeyCode.Alpha7))
        {
            ResolveCombatController();
            combatController?.TrySendSkillSlotCast(7, CurrentClientTick);
        }

    }

    private void LateUpdate() => RefreshEffectState();

    private void OnDisable()
    {
        worldMovementKeys.Clear();
        uiMovementKeys.Clear();
        cameraFollow?.SetForwardMovement(false);
        CancelCombatIntent();
        EndMeditation();
        rollReported = false;
        ResetRollInput();
        ClearGroundSlide();
    }

    private void RefreshEffectState()
    {
        if (effectStateSource == null) return;
        if (!isActiveAndEnabled || movementSession == null || !movementSession.IsWorldReady)
        {
            effectStateSource.Clear();
            return;
        }

        bool alive = !movementSession.IsLocalPlayerDead;
        // The sit-down and stand-up clips share the Meditation tag; only the
        // fully entered seated loop may show the meditation effect.
        bool meditating = alive && isMeditating && inputEnabled && NetworkManager.Instance?.MovementReady == true
            && meditationAnimator != null && meditationAnimator.isActiveAndEnabled
            && !meditationAnimator.IsInTransition(0)
            && meditationAnimator.GetCurrentAnimatorStateInfo(0).fullPathHash == SeatedLoopState;
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

        SetMeditationPosture(isMeditating);
        reconciler = playerReconciler;
        visualSmoother = smoother;
        ResolveCombatController();
        CancelCombatIntent();
        if (NetworkManager.Instance != null)
        {
            NetworkManager.Instance.FlushLocalMovement = FlushMovement;
            NetworkManager.Instance.LocalPlayerActionBlocked = IsLocalPlayerActionBlocked;
            if (movementControl?.IsValidFor(NetworkManager.Instance.SceneSessionId) == true)
                predictedMotor.ApplyMovementSettings(movementControl);
        }
        RefreshEffectState();
    }

    public void SetInputEnabled(bool enabled, bool resetSequence = false)
    {
        worldMovementKeys.Clear();
        uiMovementKeys.Clear();
        inputEnabled = enabled;
        pendingMouseLeftDown = false;
        pendingJumpPressed = false;
        trackingMouseLeftClick = false;
        suppressLeftClickAttack = false;
        rightClickTarget = default;
        mouseLeftDragDistance = 0f;
        basicAttackMovementGate.Reset();
        CancelCombatIntent();
        cameraFollow?.SetForwardMovement(false);
        cameraFollow = null;
        ResolveCombatController();
        combatController?.SetCombatEnabled(enabled, resetSequence);

        if (!enabled)
        {
            RefreshMeditationScope();
            if (movementSession?.IsLocalPlayerDead == true)
            {
                EndMeditation();
                rollReported = false;
            }
            RefreshEffectState();
            ResetRollInput();
            ClearGroundSlide();
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

    internal bool PlayPotionAnimation()
    {
        if (!isActiveAndEnabled || !inputEnabled || movementControl == null
            || NetworkManager.Instance?.MovementReady != true
            || NetworkManager.Instance.SceneOperationsPaused || IsMeditationBlockingInput()
            || predictedMotor?.CanPlayGroundActionAnimation(PredictedCharacterMotor.PotionTrigger) != true)
            return false;

        // Commit the last movement segment and stop before arming the local
        // action lock, so the server cannot continue the previous walk/turn.
        if (!StopMovementForMeditation()) return false;
        HoldMovementForPotion(MovementNow);
        return predictedMotor.PlayGroundActionAnimation(PredictedCharacterMotor.PotionTrigger);
    }

    private void ClearPendingGameplayInput()
    {
        pendingMouseLeftDown = false;
        pendingJumpPressed = false;
        trackingMouseLeftClick = false;
        suppressLeftClickAttack = false;
        mouseLeftDragDistance = 0f;
        rightClickTarget = default;
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
            && tappedDirections != PlayerInputKeys.D)
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
        rollCameraYaw = ReadMovementYaw();
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

    private void ApplyCombatApproachInput(PlayerInputFrame input)
    {
        if (combatController == null || !combatController.HasCombatApproach) return;
        if (GameplayUiInputGuard.BlocksWorldActions || IsMeditationBlockingInput()
            || predictedMotor.BlocksActionsForPotion
            || rollDirection != PlayerInputKeys.None || IsRollAnimationPlaying())
        {
            CancelCombatIntent();
            return;
        }
        if (predictedMotor.BlocksMovementForAttack()) return;
        if (!combatController.TryReadCombatApproach(out Vector3 direction, out bool basicAttack)) return;
        input.Keys = direction == Vector3.zero ? PlayerInputKeys.None : PlayerInputKeys.W | PlayerInputKeys.Shift;
        input.Actions &= ~(PlayerInputActions.PrimaryAction | PlayerInputActions.MouseLeftHeld | PlayerInputActions.MouseRightHeld);
        input.IsRunning = direction != Vector3.zero;
        input.FaceCamera = false;
        input.JumpPressed = false;
        if (direction != Vector3.zero)
            input.LookYaw = input.CameraYaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        else if (basicAttack && basicAttackMovementGate.CanAttack(Time.unscaledTime))
            input.Actions |= PlayerInputActions.PrimaryAction;
    }

    private void CancelCombatIntent(string reason = null)
    {
        repeatingAttackTarget = default;
        repeatingAttackConnectionEpoch = 0;
        repeatingAttackSceneSessionId = null;
        combatController?.CancelCombatApproach(reason);
    }

    private void HandleManualCombatCancellation(bool gameplayBlocked, bool keyboardBlocked, bool pointerBlocked,
        PlayerInputKeys heldKeys, PlayerInputActions heldActions, bool jumpRequested)
    {
        // Only a world command replaces an existing intent. UI shortcuts,
        // typing, scrolling and clicking must not cancel ongoing combat.
        if (gameplayBlocked || (!keyboardBlocked && (jumpRequested || SkillKeyPressed() || Input.GetKeyDown(KeyCode.Q)))
            || HasDirectionalInput(heldKeys) || HasBothMouseButtons(heldActions)
            || (!pointerBlocked && Input.mouseScrollDelta.sqrMagnitude > 0f)
            || (cameraFollow != null && cameraFollow.CharacterLookActive))
            CancelCombatIntent();
        else if (!pointerBlocked && Input.GetMouseButtonDown(0))
            combatController?.CancelCombatApproach();
    }

    private void RecordBasicAttackRequest()
    {
        CombatTargetSelection target = combatController.LockedTarget;
        NetworkManager network = NetworkManager.Instance;
        // Only an explicitly selected monster enables continuous attacks.
        if (target.EntityType != "monster" || !MonsterTargetingController.IsValidTarget(target)
            || network == null)
        {
            repeatingAttackTarget = default;
            return;
        }

        repeatingAttackTarget = target;
        repeatingAttackCompletionCount = predictedMotor.CompletedBasicAttackCount;
        repeatingAttackConnectionEpoch = network.ConnectionEpoch;
        repeatingAttackSceneSessionId = network.SceneSessionId;
    }

    private bool CanRepeatBasicAttack()
    {
        if (!repeatingAttackTarget.IsValid) return false;
        NetworkManager network = NetworkManager.Instance;
        CombatTargetSelection target = combatController != null ? combatController.LockedTarget : default;
        if (!inputEnabled || movementSession == null || !movementSession.IsWorldReady
            || movementSession.IsLocalPlayerDead || network == null || !network.IsConnected
            || !network.MovementReady || network.SceneOperationsPaused || network.AreLocalPlayerActionsBlocked
            || network.ConnectionEpoch != repeatingAttackConnectionEpoch
            || network.SceneSessionId != repeatingAttackSceneSessionId
            || target.TargetTransform != repeatingAttackTarget.TargetTransform
            || target.EntityType != repeatingAttackTarget.EntityType
            || target.EntityId != repeatingAttackTarget.EntityId
            || !MonsterTargetingController.IsValidTarget(repeatingAttackTarget))
        {
            CancelCombatIntent();
            return false;
        }

        // No repeat before the requested animation has actually entered and ended.
        return predictedMotor.CompletedBasicAttackCount != repeatingAttackCompletionCount
            && predictedMotor.CanStartBasicAttack;
    }

    private PlayerInputFrame CreateInputFrame()
    {
        bool meditationBlocked = IsMeditationBlockingInput();
        bool potionBlocked = predictedMotor?.BlocksActionsForPotion == true;
        AnimatorStateInfo rollState = ReadRollAnimatorState();
        bool rollPlaying = rollState.IsTag(RollTag);
        bool rollBlocking = rollPlaying || rollDirection != PlayerInputKeys.None;
        bool gameplayPaused = GameplayUiInputGuard.BlocksWorldActions;
        bool rollMoving = rollPlaying && rollDirection != PlayerInputKeys.None && !gameplayPaused && !potionBlocked
            && rollState.normalizedTime < RollBrakingStartNormalizedTime;
        bool actionsBlocked = gameplayPaused || rollBlocking || meditationBlocked || potionBlocked;
        PlayerInputKeys keys = rollMoving ? rollDirection | PlayerInputKeys.Shift
            : actionsBlocked ? PlayerInputKeys.None : FilterMeditationKeys(ReadKeys());
        PlayerInputActions actions = actionsBlocked ? PlayerInputActions.None : FilterMeditationActions(ReadHeldActions());
        basicAttackMovementGate.Observe(
            IsMovingForBasicAttack(keys, actions), Time.unscaledTime);

        bool repeatAttack = !actionsBlocked && CanRepeatBasicAttack();
        if (!actionsBlocked && ((pendingMouseLeftDown && !GameplayUiInputGuard.BlocksWorldPointerInput) || repeatAttack)
            && basicAttackMovementGate.CanAttack(Time.unscaledTime)
            && predictedMotor.CanStartBasicAttack)
        {
            actions |= PlayerInputActions.PrimaryAction;
        }

        pendingMouseLeftDown = false;
        bool movementLocked = meditationBlocked || potionBlocked || predictedMotor.BlocksMovementForAttack(actions);
        bool jumpPressed = !actionsBlocked && !movementLocked && pendingJumpPressed;
        pendingJumpPressed = false;

        if (movementLocked)
        {
            keys = PlayerInputKeys.None;
            actions &= ~(PlayerInputActions.MouseLeftHeld | PlayerInputActions.MouseRightHeld);
        }

        bool hasMovementInput = HasDirectionalInput(keys)
            || HasBothMouseButtons(actions);
        float cameraYaw = rollMoving ? rollCameraYaw : ReadMovementYaw();

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

    private float ReadMovementYaw() => cameraFollow == null
        ? transform.eulerAngles.y : cameraFollow.ReadMovementYaw();

    private PlayerInputKeys ReadDirectionalKeys(bool pressedThisFrame)
    {
        PlayerInputKeys keys = PlayerInputKeys.None;
        if (DirectionKeyActive(KeyCode.W, KeyCode.UpArrow, pressedThisFrame)) keys |= PlayerInputKeys.W;
        if (DirectionKeyActive(KeyCode.A, KeyCode.LeftArrow, pressedThisFrame)) keys |= PlayerInputKeys.A;
        if (DirectionKeyActive(KeyCode.S, KeyCode.DownArrow, pressedThisFrame)) keys |= PlayerInputKeys.S;
        if (DirectionKeyActive(KeyCode.D, KeyCode.RightArrow, pressedThisFrame)) keys |= PlayerInputKeys.D;
        return keys;
    }

    private bool DirectionKeyActive(KeyCode key, KeyCode arrow, bool pressedThisFrame) =>
        WorldKeyActive(key, pressedThisFrame) | WorldKeyActive(arrow, pressedThisFrame);

    private bool WorldKeyActive(KeyCode key, bool pressedThisFrame = false)
    {
        if (!Input.GetKey(key))
        {
            worldMovementKeys.Remove(key);
            uiMovementKeys.Remove(key);
            return false;
        }
        if (GameplayUiInputGuard.BlocksWorldKeyboardInput && !worldMovementKeys.Contains(key))
            uiMovementKeys.Add(key);
        if (uiMovementKeys.Contains(key)) return false;
        worldMovementKeys.Add(key);
        return !pressedThisFrame || Input.GetKeyDown(key);
    }

    private static bool SkillKeyPressed()
    {
        return Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Alpha2)
            || Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Alpha4)
            || Input.GetKeyDown(KeyCode.Alpha5) || Input.GetKeyDown(KeyCode.Alpha6)
            || Input.GetKeyDown(KeyCode.Alpha7);
    }

    private PlayerInputKeys ReadKeys()
    {
        PlayerInputKeys keys = ReadDirectionalKeys(pressedThisFrame: false);
        if (WorldKeyActive(KeyCode.LeftShift) | WorldKeyActive(KeyCode.RightShift))
            keys |= PlayerInputKeys.Shift;
        if (WorldKeyActive(KeyCode.Space)) keys |= PlayerInputKeys.Space;
        return keys;
    }

    private static PlayerInputActions ReadHeldActions()
    {
        PlayerInputActions actions = PlayerInputActions.None;
        if (GameplayUiInputGuard.IsWorldMouseButtonHeld(0)) actions |= PlayerInputActions.MouseLeftHeld;
        if (GameplayUiInputGuard.IsWorldMouseButtonHeld(1)) actions |= PlayerInputActions.MouseRightHeld;
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
