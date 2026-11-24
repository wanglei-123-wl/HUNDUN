using UnityEngine;

public class ThirdPersonCameraFollow : MonoBehaviour
{
    public Transform target;
    public Transform followTarget;
    public Transform rotationTarget;

    [Header("Position")]
    public Vector3 offset = new Vector3(0f, 0f, -5f);
    public float targetHeight = 1.6f;
    public float followSmooth = 12f;

    [Header("Rotation")]
    public float mouseXSpeed = 3f;
    [Range(0.01f, 10f)] public float mouseYSpeed = 2f;
    [Min(0f)] public float mouseDragThreshold = 4f;
    public float defaultPitch = 20f;
    public float minPitch = -80f;
    public float maxPitch = 80f;

    [Header("Movement Follow")]
    [SerializeField, Min(0.01f)] private float movementAlignSmoothTime = 0.35f;
    [SerializeField, Min(1f)] private float movementAlignTurnSpeed = 360f;

    [Header("Attack Follow")]
    [SerializeField, Min(0.01f)] private float attackAlignSmoothTime = 0.25f;
    [SerializeField, Min(1f)] private float attackAlignTurnSpeed = 360f;

    [Header("Zoom")]
    public float minDistance = 0.6f;
    public float maxDistance = 8f;
    public float zoomSpeed = 2f;
    public float zoomSmooth = 12f;

    [Header("Collision")]
    public LayerMask collisionMask = ~0;
    [Min(0.01f)] public float collisionRadius = 0.25f;
    [Min(0f)] public float collisionPadding = 0.12f;
    public float collisionRecoverySpeed = 8f;

    private const int CollisionBufferSize = 24;
    private readonly RaycastHit[] collisionHits = new RaycastHit[CollisionBufferSize];

    private LocalPlayerCombatController combatController;
    private PredictedCharacterMotor movementMotor;
    private LocalPlayerInputController movementInput;
    private float movementYaw;
    private bool hasMovementYaw;
    private bool alignWithForwardMovement;
    private float movementAlignVelocity;
    private float? attackFacingYaw;
    private float attackAlignVelocity;
    private float yaw;
    private float pitch = 20f;
    private float desiredDistance;
    private float currentDistance;
    private Vector3 shakeOffset;
    private Vector3 appliedShakeOffset;
    private int shakeFrame = -1;
    private Vector3 offsetDirection;
    private float characterLookYaw;
    private float leftDragDistance;
    private float rightDragDistance;
    private bool leftDragging;
    private bool rightDragging;
    private int lastSampledInputFrame = -1;

    public float CharacterLookYaw => characterLookYaw;
    internal bool IsRightMouseDragging => rightDragging;
    public bool CharacterLookActive =>
        rightDragging && !GameplayUiInputGuard.BlocksCameraInput;
    public float CameraYaw => yaw;

    private void Start()
    {
        Transform activeRotationTarget = GetRotationTarget();
        yaw = activeRotationTarget != null ? activeRotationTarget.eulerAngles.y : transform.eulerAngles.y;
        characterLookYaw = yaw;
        movementYaw = yaw;
        hasMovementYaw = activeRotationTarget != null;
        movementMotor = activeRotationTarget == null
            ? null : activeRotationTarget.GetComponent<PredictedCharacterMotor>();
        movementInput = activeRotationTarget == null
            ? null : activeRotationTarget.GetComponent<LocalPlayerInputController>();
        float scenePitch = NormalizePitch(transform.eulerAngles.x);
        pitch = Mathf.Approximately(scenePitch, 0f) ? defaultPitch : scenePitch;
        pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

        float initialDistance = Mathf.Max(0.01f, offset.magnitude);
        offsetDirection = offset / initialDistance;
        desiredDistance = Mathf.Clamp(initialDistance, minDistance, maxDistance);
        currentDistance = desiredDistance;

        SetCursorCaptured(false);
    }

    private void OnDisable()
    {
        transform.position -= appliedShakeOffset;
        appliedShakeOffset = Vector3.zero;
        shakeFrame = -1;
        attackFacingYaw = null;
        attackAlignVelocity = 0f;
        alignWithForwardMovement = false;
        movementAlignVelocity = 0f;
        leftDragging = rightDragging = false;
        SetCursorCaptured(false);
    }

    internal float ReadMovementYaw()
    {
        // A/D can turn the model toward travel without redefining "forward".
        // Retain the same input basis across key releases and direction changes;
        // right-drag steering, attack facing or a control reset replaces it.
        if (CharacterLookActive)
            ResetMovementYaw(characterLookYaw);
        else if (!hasMovementYaw)
        {
            Transform character = GetRotationTarget();
            if (character != null) ResetMovementYaw(character.eulerAngles.y);
        }

        return movementYaw;
    }

    internal void ResetMovementYaw(float characterYaw)
    {
        movementYaw = Mathf.Repeat(characterYaw, 360f);
        hasMovementYaw = true;
    }

    internal void SetForwardMovement(bool forwardOnly) =>
        alignWithForwardMovement = forwardOnly;

    internal void AlignWithAttackFacing(float characterYaw)
    {
        if (GetRotationTarget() == null) return;
        if (!attackFacingYaw.HasValue) attackAlignVelocity = 0f;
        attackFacingYaw = Mathf.Repeat(characterYaw, 360f);
        movementAlignVelocity = 0f;
        ResetMovementYaw(characterYaw);
    }

    private void UpdateFacingAlignment(Transform character, bool moving, bool manualOrbit, bool uiBlocked,
        bool automaticallyAvoidingObstacle)
    {
        if (uiBlocked)
        {
            movementAlignVelocity = 0f;
            attackAlignVelocity = 0f;
            return;
        }
        if (manualOrbit || moving || automaticallyAvoidingObstacle)
        {
            attackFacingYaw = null;
            attackAlignVelocity = 0f;
        }

        // Automatic detours may turn the model while forward input stays held.
        // Keep the observation angle and discard recentering momentum until the detour ends.
        if (manualOrbit || automaticallyAvoidingObstacle)
        {
            movementAlignVelocity = 0f;
            return;
        }

        if (attackFacingYaw.HasValue)
        {
            yaw = Mathf.SmoothDampAngle(yaw, attackFacingYaw.Value,
                ref attackAlignVelocity, Mathf.Max(0.01f, attackAlignSmoothTime),
                Mathf.Max(1f, attackAlignTurnSpeed), Time.unscaledDeltaTime);
            if (Mathf.Abs(Mathf.DeltaAngle(yaw, attackFacingYaw.Value)) < 0.1f)
            {
                yaw = attackFacingYaw.Value;
                attackFacingYaw = null;
                attackAlignVelocity = 0f;
            }
        }
        else if (moving && alignWithForwardMovement && character != null)
        {
            yaw = Mathf.SmoothDampAngle(yaw, character.eulerAngles.y,
                ref movementAlignVelocity, Mathf.Max(0.01f, movementAlignSmoothTime),
                Mathf.Max(1f, movementAlignTurnSpeed), Time.unscaledDeltaTime);
        }
        else
        {
            movementAlignVelocity = 0f;
        }
    }

    public void SampleInput()
    {
        if (lastSampledInputFrame == Time.frameCount || GetFollowTarget() == null)
            return;

        lastSampledInputFrame = Time.frameCount;
        bool uiBlocked = GameplayUiInputGuard.BlocksCameraInput;
        bool leftHeld = !uiBlocked && Input.GetMouseButton(0);
        bool rightHeld = !uiBlocked && Input.GetMouseButton(1);
        Vector2 mouseDelta = uiBlocked ? Vector2.zero
            : new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));

        if (!uiBlocked && Input.GetMouseButtonDown(0))
        {
            leftDragDistance = 0f;
            leftDragging = false;
        }

        if (!uiBlocked && Input.GetMouseButtonDown(1))
        {
            rightDragDistance = 0f;
            rightDragging = false;
        }

        if (leftHeld)
        {
            leftDragDistance += mouseDelta.magnitude;
            leftDragging |= leftDragDistance > mouseDragThreshold;
        }

        if (rightHeld)
        {
            rightDragDistance += mouseDelta.magnitude;
            rightDragging |= rightDragDistance > mouseDragThreshold;
        }

        if (!leftHeld)
        {
            leftDragging = false;
            leftDragDistance = 0f;
        }

        if (!rightHeld)
        {
            rightDragging = false;
            rightDragDistance = 0f;
        }

        bool rotatingCamera = leftDragging || rightDragging;

        SetCursorCaptured(rotatingCamera);

        if (rotatingCamera)
        {
            yaw += mouseDelta.x * mouseXSpeed;
            pitch -= mouseDelta.y * mouseYSpeed;
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
        }

        if (rightDragging)
        {
            characterLookYaw = yaw;
        }
    }

    private float ReadScrollInput() =>
        GameplayUiInputGuard.BlocksCameraInput ? 0f : Input.mouseScrollDelta.y;

    private void LateUpdate()
    {
        Transform activeFollowTarget = GetFollowTarget();
        Vector3 basePosition = transform.position - appliedShakeOffset;
        if (activeFollowTarget == null)
        {
            if (appliedShakeOffset != Vector3.zero)
                transform.position = basePosition;
            appliedShakeOffset = Vector3.zero;
            attackFacingYaw = null;
            attackAlignVelocity = 0f;
            return;
        }

        SampleInput();
        Transform activeRotationTarget = GetRotationTarget();
        // Selection is only a collision exclusion; it never drives the view.
        Transform collisionIgnoredTarget = combatController == null
            ? null : combatController.LockedTarget.TargetTransform;
        if (movementMotor == null && activeRotationTarget != null)
            activeRotationTarget.TryGetComponent(out movementMotor);
        if (movementInput == null && activeRotationTarget != null)
            activeRotationTarget.TryGetComponent(out movementInput);
        bool automaticallyAvoidingObstacle = movementInput != null && movementInput.IsAutomaticallyAvoidingObstacle;
        Vector3 velocity = movementMotor == null ? Vector3.zero : movementMotor.Velocity;
        bool moving = velocity.x * velocity.x + velocity.z * velocity.z > 0.0025f;
        bool manualOrbit = leftDragging || rightDragging;
        Vector3 followPosition = activeFollowTarget.position;

        bool uiBlocked = GameplayUiInputGuard.BlocksCameraInput;
        UpdateFacingAlignment(activeRotationTarget, moving, manualOrbit, uiBlocked, automaticallyAvoidingObstacle);
        float scroll = ReadScrollInput();
        if (Mathf.Abs(scroll) > 0.001f)
            desiredDistance = Mathf.Clamp(desiredDistance - scroll * zoomSpeed, minDistance, maxDistance);

        float zoomWeight = 1f - Mathf.Exp(-zoomSmooth * Time.unscaledDeltaTime);
        float unobstructedDistance = Mathf.Lerp(currentDistance, desiredDistance, zoomWeight);

        Quaternion orbitRotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 lookPoint = followPosition + Vector3.up * targetHeight;
        Vector3 castDirection = orbitRotation * offsetDirection;
        float collisionDistance = FindCollisionDistance(
            lookPoint,
            castDirection,
            desiredDistance,
            activeFollowTarget,
            activeRotationTarget,
            collisionIgnoredTarget
        );

        bool obstructed = collisionDistance < desiredDistance - 0.001f;
        if (obstructed)
        {
            // 碰墙时立即拉近，避免平滑插值期间相机仍留在墙外。
            currentDistance = Mathf.Min(unobstructedDistance, collisionDistance);
        }
        else
        {
            currentDistance = Mathf.MoveTowards(
                unobstructedDistance,
                desiredDistance,
                collisionRecoverySpeed * Time.unscaledDeltaTime
            );
        }

        Vector3 targetPosition = lookPoint + castDirection * currentDistance;
        if (!obstructed)
        {
            float followWeight = 1f - Mathf.Exp(-followSmooth * Time.unscaledDeltaTime);
            targetPosition = Vector3.Lerp(basePosition, targetPosition, followWeight);
            // A smoothed orbit must also keep its path clear while turning.
            Vector3 followPath = targetPosition - lookPoint;
            float followDistance = followPath.magnitude;
            float clearDistance = FindCollisionDistance(lookPoint, followPath,
                followDistance, activeFollowTarget, activeRotationTarget, collisionIgnoredTarget);
            if (clearDistance < followDistance)
            {
                targetPosition = lookPoint + followPath * (clearDistance / followDistance);
                currentDistance = Mathf.Min(currentDistance, clearDistance);
                obstructed = true;
            }
        }

        appliedShakeOffset = Vector3.zero;
        if (!obstructed && shakeFrame == Time.frameCount && shakeOffset != Vector3.zero)
        {
            Vector3 shakenPath = targetPosition + shakeOffset - lookPoint;
            float shakenDistance = shakenPath.magnitude;
            float clearDistance = FindCollisionDistance(lookPoint, shakenPath,
                shakenDistance, activeFollowTarget, activeRotationTarget, collisionIgnoredTarget);
            if (clearDistance >= shakenDistance)
                appliedShakeOffset = shakeOffset;
        }

        transform.position = targetPosition + appliedShakeOffset;
        transform.rotation = orbitRotation;
    }

    public void AddShakeOffset(Vector3 worldOffset)
    {
        if (shakeFrame != Time.frameCount)
        {
            shakeFrame = Time.frameCount;
            shakeOffset = Vector3.zero;
        }

        shakeOffset += worldOffset;
    }

    public void BindTargets(Transform newFollowTarget, Transform newRotationTarget)
    {
        bool changedRotationTarget = GetRotationTarget() != newRotationTarget;
        followTarget = newFollowTarget;
        rotationTarget = newRotationTarget;
        target = newRotationTarget;
        combatController = newRotationTarget == null
            ? null
            : newRotationTarget.GetComponent<LocalPlayerCombatController>();
        movementMotor = newRotationTarget == null
            ? null : newRotationTarget.GetComponent<PredictedCharacterMotor>();
        movementInput = newRotationTarget == null
            ? null : newRotationTarget.GetComponent<LocalPlayerInputController>();
        if (changedRotationTarget)
        {
            hasMovementYaw = newRotationTarget != null;
            if (newRotationTarget != null) ResetMovementYaw(newRotationTarget.eulerAngles.y);
            alignWithForwardMovement = false;
            movementAlignVelocity = 0f;
            attackFacingYaw = null;
            attackAlignVelocity = 0f;
        }
    }

    private float FindCollisionDistance(
        Vector3 origin,
        Vector3 direction,
        float distance,
        Transform activeFollowTarget,
        Transform activeRotationTarget,
        Transform collisionIgnoredTarget)
    {
        if (distance <= 0f || direction.sqrMagnitude <= 0.0001f)
            return distance;

        direction.Normalize();
        int hitCount = Physics.SphereCastNonAlloc(
            origin,
            collisionRadius,
            direction,
            collisionHits,
            distance,
            collisionMask,
            QueryTriggerInteraction.Ignore
        );

        float nearestDistance = distance;
        for (int index = 0; index < hitCount; index++)
        {
            Collider hitCollider = collisionHits[index].collider;
            if (hitCollider == null
                || IsPlayerCollider(hitCollider.transform, activeFollowTarget, activeRotationTarget)
                || IsSameHierarchy(hitCollider.transform, collisionIgnoredTarget))
                continue;

            nearestDistance = Mathf.Min(
                nearestDistance,
                Mathf.Max(0.05f, collisionHits[index].distance - collisionPadding)
            );
        }

        return nearestDistance;
    }

    private static bool IsPlayerCollider(Transform colliderTransform, Transform follow, Transform rotation)
    {
        return IsSameHierarchy(colliderTransform, follow) || IsSameHierarchy(colliderTransform, rotation);
    }

    private static bool IsSameHierarchy(Transform first, Transform second)
    {
        if (first == null || second == null)
            return false;

        return first == second || first.IsChildOf(second) || second.IsChildOf(first);
    }

    private void SetCursorCaptured(bool captured)
    {
        Cursor.lockState = captured ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !captured;
    }

    private static float NormalizePitch(float value)
    {
        return value > 180f ? value - 360f : value;
    }

    private Transform GetFollowTarget()
    {
        return followTarget != null ? followTarget : target;
    }

    private Transform GetRotationTarget()
    {
        return rotationTarget != null ? rotationTarget : target;
    }
}
