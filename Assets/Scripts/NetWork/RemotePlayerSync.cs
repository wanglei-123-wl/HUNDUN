using System.Collections.Generic;
using TMPro;
using Game.Client.BattleSync;
using UnityEngine;

public partial class RemotePlayerSync : MonoBehaviour
{
    private const string RunningParameter = "IsRunning";

    [Header("Identity")]
    [SerializeField] private long characterDbId;
    [SerializeField] private string characterId;
    [SerializeField] private string displayName;

    [Header("Visual")]
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private Animator animator;

    [Header("Sync")]
    [SerializeField] private float positionLerpSpeed = 10f;
    [SerializeField] private float rotationLerpSpeed = 12f;
    [SerializeField] private float snapDistance = 5f;

    [Header("Animation Mapping")]
    [SerializeField] private string moveBoolName = "IsMoving";
    [SerializeField] private string idleStateName = "Idle";
    [SerializeField] private string runStateName = "Run";
    [SerializeField] private string attackTriggerName = "Attack";
    [SerializeField] private string attackStateName = "Attack";

    private Vector3 targetPosition;
    private Quaternion targetRotation;
    private string currentAnim;
    private bool initialized;
    private bool hasReceivedState;
    private bool hasMoveBoolParameter;
    private bool hasRunningParameter;
    private bool hasAttackTriggerParameter;
    private bool attackEventsDriveAnimation;
    private long lastServerTick;
    private RemoteMovementSnapshot movement;
    private double movementReceivedAt;
    private bool movementGroundedParameter, movementJumpParameter;
    public long VisibilityRevision { get; private set; }
    private long lastAttackActionSequence;
    private long lastAttackEventId;
    private long lifeCombatRevision = -1;
    private readonly Dictionary<Collider, bool> deathColliderStates = new Dictionary<Collider, bool>();
    public bool IsDead { get; private set; }

    public long CharacterDbId => characterDbId;
    public string CharacterId => characterId;
    public long LastServerTick => lastServerTick;
    public Animator VisualAnimator => animator;
    public Transform SkillReleasePoint { get; private set; }
    internal GameObject SelectionRing { get; set; }

    internal void SetSelectionRingVisible(bool visible)
    {
        if (SelectionRing != null)
            SelectionRing.SetActive(visible);
    }

    private void Awake()
    {
        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        targetPosition = transform.position;
        targetRotation = transform.rotation;
        CacheAnimatorParameters();
        PlayerDeathAnimation.SetDead(animator, IsDead, alreadyDeadAtSpawn: true);
    }

    private void Update()
    {
        if (!initialized || !hasReceivedState)
        {
            return;
        }

        UpdateRemoteRoll();
        Vector3 destination = targetPosition;
        if (!IsDead && movement?.mode == "normal")
        {
            var network = NetworkManager.Instance;
            float horizon = (network?.MovementHeartbeatIntervalMs ?? 0) * .001f;
            float age = Mathf.Clamp((float)(Time.realtimeSinceStartupAsDouble - movementReceivedAt), 0f, horizon);
            var velocity = movement.info.Velocity;
            destination += new Vector3(velocity.x, 0f, velocity.z) * age;
            if ((movement.info.flags & MovementEventFlags.Falling) != 0)
            {
                // Predict the ascent between samples. Downward movement follows received positions.
                float gravity = network?.MovementGravity ?? 0f;
                destination.y += Mathf.Max(0f, velocity.y * age + .5f * gravity * age * age);
            }
        }
        float distance = Vector3.Distance(transform.position, destination);

        if (distance > snapDistance)
        {
            transform.position = destination;
        }
        else
        {
            transform.position = Vector3.Lerp(
                transform.position,
                destination,
                positionLerpSpeed * Time.deltaTime
            );
        }

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            rotationLerpSpeed * Time.deltaTime
        );
    }

    public void BindPresentation(GameObject visual, Transform skillReleasePoint)
    {
        SkillReleasePoint = skillReleasePoint;
        if (TryGetComponent(out PlayerEffectPresenter effects))
            effects.SetVisual(visual);

        // Older player prefabs can carry their own sync and animation settings.
        if (visual.TryGetComponent(out RemotePlayerSync legacySync))
        {
            animator = legacySync.animator;
            nameText = legacySync.nameText;
            positionLerpSpeed = legacySync.positionLerpSpeed;
            rotationLerpSpeed = legacySync.rotationLerpSpeed;
            snapDistance = legacySync.snapDistance;
            moveBoolName = legacySync.moveBoolName;
            idleStateName = legacySync.idleStateName;
            runStateName = legacySync.runStateName;
            attackTriggerName = legacySync.attackTriggerName;
            attackStateName = legacySync.attackStateName;
        }

        if (animator == null)
        {
            animator = visual.GetComponentInChildren<Animator>(true);
        }
        CacheAnimatorParameters();

        foreach (RemotePlayerSync modelSync in visual.GetComponentsInChildren<RemotePlayerSync>(true))
        {
            modelSync.enabled = false;
            Destroy(modelSync);
        }
        if (IsDead)
            SetDeathCollidersDisabled(true);
    }

    public void Init(long remoteCharacterDbId, string remoteCharacterId, string remoteDisplayName)
    {
        bool identityChanged = !initialized || characterDbId != remoteCharacterDbId;
        characterDbId = remoteCharacterDbId;
        characterId = remoteCharacterId ?? "";
        displayName = string.IsNullOrEmpty(remoteDisplayName) ? characterId : remoteDisplayName;

        initialized = true;
        hasReceivedState = false;
        lastServerTick = 0;
        if (identityChanged)
        {
            lastAttackActionSequence = 0;
            lastAttackEventId = 0;
            lifeCombatRevision = -1;
            EndRemoteRoll();
            lastRemoteRollId = 0;
            meditationRevision = -1;
            SetRemoteMeditationPosture(false);
            IsDead = false;
            SetDeathCollidersDisabled(false);
            PlayerDeathAnimation.SetDead(animator, false);
        }

        if (nameText != null)
        {
            nameText.text = displayName;
        }

        gameObject.name = "RemotePlayer_" + characterDbId;
    }

    public bool ApplyServerState(PlayerNetworkState state)
    {
        if (state == null || (state.movement?.ActiveRoll != null && !state.movement.IsValid))
        {
            return false;
        }

        if (characterDbId > 0 && state.characterDbId != characterDbId)
        {
            return false;
        }

        long orderingTick = state.serverTick > 0 ? state.serverTick : state.sequence;
        if (orderingTick > 0 && orderingTick < lastServerTick)
        {
            return false;
        }

        if (!initialized)
        {
            Init(state.characterDbId, state.characterId, state.nickname);
        }

        lastServerTick = orderingTick > 0 ? orderingTick : lastServerTick;
        bool firstState = !hasReceivedState;
        if (firstState) VisibilityRevision = state.visibilityRevision;
        ApplyMeditationState(state.meditation, firstState);
        if (state.movement?.IsValid == true)
        {
            ApplyMovementEvent(state.movement);
            return true;
        }
        if (movement?.mode == "normal") return true;
        targetPosition = state.GetPosition();
        targetRotation = state.GetRotation();

        if (!hasReceivedState)
        {
            transform.position = targetPosition;
            transform.rotation = targetRotation;
            hasReceivedState = true;
        }

        Vector3 serverVelocity = state.GetVelocity();
        bool isMoving = state.movementState != 0
            || new Vector2(serverVelocity.x, serverVelocity.z).sqrMagnitude > 0.0025f;
        string animationName = string.IsNullOrEmpty(state.animation)
            ? (isMoving ? runStateName : idleStateName)
            : state.animation;
        if (!attackEventsDriveAnimation
            || !string.Equals(animationName, attackStateName,
                System.StringComparison.OrdinalIgnoreCase))
            ApplyAnimation(animationName);
        return true;
    }

    public bool ApplyMovementEvent(RemoteMovementSnapshot incoming)
    {
        if (!initialized || incoming?.IsValid != true
            || (movement != null && (incoming.epoch < movement.epoch || incoming.revision <= movement.revision)))
            return false;

        bool wasAirborne = movement != null && (movement.info.flags & MovementEventFlags.Falling) != 0;
        movement = incoming;
        movementReceivedAt = Time.realtimeSinceStartupAsDouble;
        targetPosition = movement.info.Position;
        targetRotation = Quaternion.Euler(0f, movement.info.yaw, 0f);
        if (!hasReceivedState)
            transform.SetPositionAndRotation(targetPosition, targetRotation);
        hasReceivedState = true;
        var v = movement.info.Velocity;
        var flags = movement.info.flags;
        bool walking = (flags & MovementEventFlags.Walking) != 0;
        bool airborne = (flags & MovementEventFlags.Falling) != 0;
        ApplyRemoteRoll(incoming.ActiveRoll);
        if (movementGroundedParameter) animator.SetBool("IsGrounded", !airborne);
        if (movementJumpParameter && airborne && !wasAirborne && movement.info.jumpSpeed > 0f && !IsDead)
            animator.SetTrigger("Jump");
        ApplyAnimation(v.x * v.x + v.z * v.z > .0025f ? (walking ? "Moving" : runStateName) : idleStateName);
        return true;
    }

    public bool ApplyDynamicState(DecodedPlayerState state,long serverTick)
    {
        if(!initialized||serverTick<=lastServerTick)return false;
        lastServerTick=serverTick;
        bool serverOwned = movement?.mode != "normal";
        if(serverOwned && state.HasPosition)
        {
            targetPosition=new Vector3(state.X,state.Y,state.Z);
            targetRotation=Quaternion.Euler(0f,state.YawDegrees,0f);
            hasReceivedState=true;
        }
        bool moving=state.HasMovementState&&state.MovementState!=0;
        if(state.HasVelocity) moving|=new Vector2(state.VelocityX,state.VelocityZ).sqrMagnitude>0.0025f;
        if(serverOwned && (state.HasMovementState||state.HasVelocity))ApplyAnimation(moving?runStateName:idleStateName);
        if(state.HasActionState&&state.ActionState!=0&&!attackEventsDriveAnimation)
            ApplyAnimation(attackStateName);
        return true;
    }

    internal void ApplyCombatLife(CombatStateData state, bool alreadyDeadAtSpawn = false)
    {
        bool dead = state.Hp <= 0
            || string.Equals(state.LifeState, "Dead", System.StringComparison.OrdinalIgnoreCase);
        if (state.CombatRevision < lifeCombatRevision
            || (state.CombatRevision == lifeCombatRevision && !dead))
            return;

        lifeCombatRevision = state.CombatRevision;
        SetDeathCollidersDisabled(dead);
        if (IsDead == dead)
            return;

        IsDead = dead;
        if (dead)
        {
            EndRemoteRoll();
            SetRemoteMeditationPosture(false);
        }
        currentAnim = "";
        PlayerDeathAnimation.SetDead(animator, dead, alreadyDeadAtSpawn);
    }

    private void SetDeathCollidersDisabled(bool disabled)
    {
        if (disabled)
        {
            foreach (Collider collider in GetComponentsInChildren<Collider>(true))
            {
                if (!deathColliderStates.ContainsKey(collider))
                    deathColliderStates.Add(collider, collider.enabled);
                collider.enabled = false;
            }
            return;
        }

        foreach (KeyValuePair<Collider, bool> saved in deathColliderStates)
        {
            if (saved.Key != null)
                saved.Key.enabled = saved.Value;
        }
        deathColliderStates.Clear();
    }

    public void StopNetworkInterpolation()
    {
        hasReceivedState = false;
        targetPosition = transform.position;
        targetRotation = transform.rotation;
    }

    public void SetAttackEventRoutingEnabled(bool enabled)
    {
        attackEventsDriveAnimation = enabled;
    }

    public bool TryPlayAttack(long actionSequence, long eventId)
    {
        if (!initialized || animator == null) return false;
        if (IsDead) return true;
        if ((actionSequence > 0 && actionSequence <= lastAttackActionSequence)
            || (eventId > 0 && eventId <= lastAttackEventId))
            return true;
        ApplyAnimation(attackStateName, true);
        if (actionSequence > 0) lastAttackActionSequence = actionSequence;
        if (eventId > 0) lastAttackEventId = eventId;
        return true;
    }

    public void ClearPendingSnapshots()
    {
        EndRemoteRoll();
        lastRemoteRollId = 0;
        lastServerTick = 0;
        movement = null;
        VisibilityRevision = 0;
        meditationRevision = -1;
        SetRemoteMeditationPosture(false);
        currentAnim = "";

        if (animator == null)
        {
            return;
        }

        if (hasMoveBoolParameter)
        {
            animator.SetBool(moveBoolName, false);
        }
        if (hasRunningParameter)
        {
            animator.SetBool(RunningParameter, false);
        }

        if (hasAttackTriggerParameter)
        {
            animator.ResetTrigger(attackTriggerName);
        }
    }

    private void ApplyAnimation(string animName, bool restartAttack = false)
    {
        if (IsDead || animator == null || string.IsNullOrEmpty(animName))
        {
            return;
        }

        bool isAttack = string.Equals(
            animName,
            attackStateName,
            System.StringComparison.OrdinalIgnoreCase
        );

        if (isAttack)
        {
            if (currentAnim == animName && !restartAttack)
            {
                return;
            }

            currentAnim = animName;

            if (hasMoveBoolParameter)
            {
                animator.SetBool(moveBoolName, false);
            }

            if (hasAttackTriggerParameter)
            {
                animator.ResetTrigger(attackTriggerName);
                animator.SetTrigger(attackTriggerName);
                return;
            }

            if (animator.HasState(0, Animator.StringToHash(attackStateName)))
            {
                animator.CrossFade(attackStateName, 0.05f);
            }

            return;
        }

        if (remoteMeditating || currentAnim == animName)
        {
            return;
        }

        currentAnim = animName;
        bool isRunning = string.Equals(animName, runStateName, System.StringComparison.OrdinalIgnoreCase);
        bool isMoving = isRunning
            || string.Equals(animName, "Moving", System.StringComparison.OrdinalIgnoreCase);

        if (hasRunningParameter)
        {
            animator.SetBool(RunningParameter, isRunning);
        }

        if (hasMoveBoolParameter)
        {
            animator.SetBool(moveBoolName, isMoving);
            return;
        }

        if (remoteRollPlaying) return;
        string targetState = isMoving ? runStateName : idleStateName;

        if (animator.HasState(0, Animator.StringToHash(targetState)))
        {
            animator.CrossFade(targetState, 0.1f);
        }
    }

    private void CacheAnimatorParameters()
    {
        hasMoveBoolParameter = false;
        hasRunningParameter = false;
        hasAttackTriggerParameter = false;
        movementGroundedParameter = movementJumpParameter = meditationAnimatorParameter = false;
        CacheRemoteRollClip();

        if (animator == null)
        {
            return;
        }

        AnimatorControllerParameter[] parameters = animator.parameters;

        for (int i = 0; i < parameters.Length; i++)
        {
            AnimatorControllerParameter parameter = parameters[i];
            meditationAnimatorParameter |= parameter.type == AnimatorControllerParameterType.Bool
                && parameter.nameHash == RemoteMeditatingParameter;
            movementGroundedParameter |= parameter.type == AnimatorControllerParameterType.Bool && parameter.name == "IsGrounded";
            movementJumpParameter |= parameter.type == AnimatorControllerParameterType.Trigger && parameter.name == "Jump";

            if (parameter.type == AnimatorControllerParameterType.Bool && parameter.name == moveBoolName)
            {
                hasMoveBoolParameter = true;
            }
            if (parameter.type == AnimatorControllerParameterType.Bool && parameter.name == RunningParameter)
            {
                hasRunningParameter = true;
            }

            if (parameter.type == AnimatorControllerParameterType.Trigger && parameter.name == attackTriggerName)
            {
                hasAttackTriggerParameter = true;
            }
        }
    }
}
