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

    [Header("Target Lock")]
    [SerializeField, Min(0.01f)] private float lockBlendTime = 0.35f;
    [SerializeField, Min(1f)] private float lockOrbitTurnSpeed = 360f;
    [SerializeField, Min(1f)] private float lockViewTurnSpeed = 540f;

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
    private const float LockOrbitMinimumDistance = 0.4f;
    private readonly RaycastHit[] collisionHits = new RaycastHit[CollisionBufferSize];

    private LocalPlayerCombatController combatController;
    private PredictedCharacterMotor movementMotor;
    private float movementYaw;
    private bool hasMovementYaw;
    private bool alignWithForwardMovement;
    private float movementAlignVelocity;
    private Transform trackedMonster;
    private float trackingBlend;
    private float? lockedOrbitYaw;
    private Quaternion unlockViewOffset = Quaternion.identity;
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
        rightDragging && !GameplayUiInputGuard.BlocksGameplayInput;
    public float CameraYaw
    {
        get
        {
            if (lockedOrbitYaw.HasValue)
                return transform.eulerAngles.y;

            return unlockViewOffset == Quaternion.identity
                ? yaw
                : (Quaternion.Euler(pitch, yaw, 0f) * unlockViewOffset).eulerAngles.y;
        }
    }

    private void Start()
    {
        Transform activeRotationTarget = GetRotationTarget();
        yaw = activeRotationTarget != null ? activeRotationTarget.eulerAngles.y : transform.eulerAngles.y;
        characterLookYaw = yaw;
        movementYaw = yaw;
        hasMovementYaw = activeRotationTarget != null;
        movementMotor = activeRotationTarget == null
            ? null : activeRotationTarget.GetComponent<PredictedCharacterMotor>();
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
        trackedMonster = null;
        trackingBlend = 0f;
        lockedOrbitYaw = null;
        unlockViewOffset = Quaternion.identity;
        alignWithForwardMovement = false;
        movementAlignVelocity = 0f;
        leftDragging = rightDragging = false;
        SetCursorCaptured(false);
    }

    internal float ReadMovementYaw()
    {
        // A/D can turn the model toward travel without redefining "forward".
        // Retain the same input basis across key releases and direction changes;
        // only explicit right-drag steering or a control reset replaces it.
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

    public void SampleInput()
    {
        if (lastSampledInputFrame == Time.frameCount || GetFollowTarget() == null)
            return;

        lastSampledInputFrame = Time.frameCount;
        bool uiBlocked = GameplayUiInputGuard.BlocksGameplayInput;
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

    private void LateUpdate()
    {
        Transform activeFollowTarget = GetFollowTarget();
        Vector3 basePosition = transform.position - appliedShakeOffset;
        if (activeFollowTarget == null)
        {
            if (appliedShakeOffset != Vector3.zero)
                transform.position = basePosition;
            appliedShakeOffset = Vector3.zero;
            trackedMonster = null;
            trackingBlend = 0f;
            lockedOrbitYaw = null;
            unlockViewOffset = Quaternion.identity;
            return;
        }

        SampleInput();
        Transform activeRotationTarget = GetRotationTarget();
        CombatTargetSelection lockedTarget = combatController == null
            ? default
            : combatController.LockedTarget;
        Transform lockedMonster = lockedTarget.TargetTransform;
        if (movementMotor == null && activeRotationTarget != null)
            activeRotationTarget.TryGetComponent(out movementMotor);
        Vector3 velocity = movementMotor == null ? Vector3.zero : movementMotor.Velocity;
        bool moving = velocity.x * velocity.x + velocity.z * velocity.z > 0.0025f;
        bool manualOrbit = leftDragging || rightDragging;
        bool trackMonster = lockedMonster != null && !manualOrbit && !moving;
        bool stoppedTracking = lockedOrbitYaw.HasValue && !trackMonster;
        Vector3 followPosition = activeFollowTarget.position;
        UpdateLockedOrbit(trackMonster ? lockedTarget : default, followPosition);

        bool uiBlocked = GameplayUiInputGuard.BlocksGameplayInput;
        if (moving && alignWithForwardMovement
            && !manualOrbit && !uiBlocked && activeRotationTarget != null)
        {
            yaw = Mathf.SmoothDampAngle(yaw, activeRotationTarget.eulerAngles.y,
                ref movementAlignVelocity, Mathf.Max(0.01f, movementAlignSmoothTime),
                Mathf.Max(1f, movementAlignTurnSpeed), Time.unscaledDeltaTime);
        }
        else
        {
            movementAlignVelocity = 0f;
        }
        float scroll = uiBlocked ? 0f : Input.mouseScrollDelta.y;
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
            lockedMonster
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
            if (lockedMonster != null)
            {
                // Following the previous orbit can swing the camera across a wall.
                Vector3 followPath = targetPosition - lookPoint;
                float followDistance = followPath.magnitude;
                float clearDistance = FindCollisionDistance(lookPoint, followPath,
                    followDistance, activeFollowTarget, activeRotationTarget, lockedMonster);
                if (clearDistance < followDistance)
                {
                    targetPosition = lookPoint + followPath * (clearDistance / followDistance);
                    currentDistance = Mathf.Min(currentDistance, clearDistance);
                    obstructed = true;
                }
            }
        }

        appliedShakeOffset = Vector3.zero;
        if (!obstructed && shakeFrame == Time.frameCount && shakeOffset != Vector3.zero)
        {
            Vector3 shakenPath = targetPosition + shakeOffset - lookPoint;
            float shakenDistance = shakenPath.magnitude;
            float clearDistance = FindCollisionDistance(lookPoint, shakenPath,
                shakenDistance, activeFollowTarget, activeRotationTarget, lockedMonster);
            if (clearDistance >= shakenDistance)
                appliedShakeOffset = shakeOffset;
        }

        transform.position = targetPosition + appliedShakeOffset;
        transform.rotation = ResolveViewRotation(orbitRotation, trackMonster ? lockedMonster : null,
            targetPosition, stoppedTracking);
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
        if (changedRotationTarget)
        {
            hasMovementYaw = newRotationTarget != null;
            if (newRotationTarget != null) ResetMovementYaw(newRotationTarget.eulerAngles.y);
            alignWithForwardMovement = false;
            movementAlignVelocity = 0f;
        }
    }

    private void UpdateLockedOrbit(CombatTargetSelection lockedTarget, Vector3 followPosition)
    {
        if (!lockedTarget.IsValid)
        {
            trackedMonster = null;
            trackingBlend = 0f;
            lockedOrbitYaw = null;
            return;
        }

        if (trackedMonster != lockedTarget.TargetTransform)
        {
            trackedMonster = lockedTarget.TargetTransform;
            trackingBlend = 0f;
        }
        trackingBlend = Mathf.MoveTowards(trackingBlend, 1f, Time.unscaledDeltaTime / Mathf.Max(0.01f, lockBlendTime));

        float orbitYaw = lockedOrbitYaw ?? yaw;
        if (lockedTarget.TryGetHorizontalYaw(followPosition, out float targetYaw, LockOrbitMinimumDistance))
            orbitYaw = Mathf.MoveTowardsAngle(orbitYaw, targetYaw,
                lockOrbitTurnSpeed * Time.unscaledDeltaTime * Mathf.SmoothStep(0f, 1f, trackingBlend));

        lockedOrbitYaw = orbitYaw;
        yaw = orbitYaw;
    }

    private Quaternion ResolveViewRotation(Quaternion orbitRotation, Transform lockedMonster,
        Vector3 cameraPosition, bool stoppedTracking)
    {
        float turnStep = lockViewTurnSpeed * Time.unscaledDeltaTime;
        if (lockedMonster == null)
        {
            if (stoppedTracking)
                unlockViewOffset = Quaternion.Inverse(orbitRotation) * transform.rotation;

            unlockViewOffset = Quaternion.RotateTowards(unlockViewOffset, Quaternion.identity, turnStep);
            return orbitRotation * unlockViewOffset;
        }

        Vector3 focus = lockedMonster.position + Vector3.up * targetHeight;
        if (lockedMonster.TryGetComponent(out CapsuleCollider capsule) && capsule.enabled)
            focus = capsule.bounds.center;

        Vector3 aim = focus - cameraPosition;
        if (aim.sqrMagnitude <= 0.0001f) return transform.rotation;

        float horizontalDistance = new Vector2(aim.x, aim.z).magnitude;
        float viewYaw = horizontalDistance > 0.01f
            ? Mathf.Atan2(aim.x, aim.z) * Mathf.Rad2Deg
            : transform.eulerAngles.y;
        float viewPitch = Mathf.Atan2(-aim.y, horizontalDistance) * Mathf.Rad2Deg;
        Quaternion targetRotation = Quaternion.Euler(Mathf.Clamp(viewPitch, minPitch, maxPitch), viewYaw, 0f);
        return Quaternion.RotateTowards(transform.rotation, targetRotation,
            turnStep * Mathf.SmoothStep(0f, 1f, trackingBlend));
    }

    private float FindCollisionDistance(
        Vector3 origin,
        Vector3 direction,
        float distance,
        Transform activeFollowTarget,
        Transform activeRotationTarget,
        Transform lockedMonster)
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
                || IsSameHierarchy(hitCollider.transform, lockedMonster))
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
