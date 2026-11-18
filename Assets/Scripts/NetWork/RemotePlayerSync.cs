using TMPro;
using Game.Client.BattleSync;
using UnityEngine;

public class RemotePlayerSync : MonoBehaviour
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
    private long lastAttackActionSequence;
    private long lastAttackEventId;
    private long lifeCombatRevision = -1;
    public bool IsDead { get; private set; }

    public long CharacterDbId => characterDbId;
    public string CharacterId => characterId;
    public long LastServerTick => lastServerTick;
    public Animator VisualAnimator => animator;
    public Transform SkillReleasePoint { get; private set; }

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

        float distance = Vector3.Distance(transform.position, targetPosition);

        if (distance > snapDistance)
        {
            transform.position = targetPosition;
        }
        else
        {
            transform.position = Vector3.Lerp(
                transform.position,
                targetPosition,
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

        foreach (RemotePlayerSync modelSync in visual.GetComponentsInChildren<RemotePlayerSync>(true))
        {
            modelSync.enabled = false;
            Destroy(modelSync);
        }
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
            IsDead = false;
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
        if (state == null)
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

    public bool ApplyDynamicState(DecodedPlayerState state,long serverTick)
    {
        if(!initialized||serverTick<=lastServerTick)return false;
        lastServerTick=serverTick;
        if(state.HasPosition)
        {
            targetPosition=new Vector3(state.X,state.Y,state.Z);
            targetRotation=Quaternion.Euler(0f,state.YawDegrees,0f);
            hasReceivedState=true;
        }
        bool moving=state.HasMovementState&&state.MovementState!=0;
        if(state.HasVelocity) moving|=new Vector2(state.VelocityX,state.VelocityZ).sqrMagnitude>0.0025f;
        if(state.HasMovementState||state.HasVelocity)ApplyAnimation(moving?runStateName:idleStateName);
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
        if (IsDead == dead)
            return;

        IsDead = dead;
        currentAnim = "";
        PlayerDeathAnimation.SetDead(animator, dead, alreadyDeadAtSpawn);
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
        lastServerTick = 0;
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

        if (currentAnim == animName)
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

        if (animator == null)
        {
            return;
        }

        AnimatorControllerParameter[] parameters = animator.parameters;

        for (int i = 0; i < parameters.Length; i++)
        {
            AnimatorControllerParameter parameter = parameters[i];

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
