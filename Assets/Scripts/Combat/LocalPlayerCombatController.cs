using System.Collections.Generic;
using System.Globalization;
using Game.UI.Notice;
using UnityEngine;
using UnityEngine.Serialization;

[DisallowMultipleComponent]
public sealed class LocalPlayerCombatController : MonoBehaviour
{
    private const int RecentAttackCapacity = 128;

    private sealed class PendingSkillRequest
    {
        public string SkillId;
        public string TargetType;
        public string TargetId;
        public Transform TargetTransform;
        public long CharacterDbId;
        public long ConnectionEpoch;
        public float SentAt;
        public float StartedAt;
        public SkillCastStarted StartedMessage;
        public GameObject EffectInstance;
        public bool AcceptanceTimeoutLogged;
        public bool ResultTimeoutLogged;
    }

    [SerializeField] private MonsterTargetingController targetingController;
    [Header("玩家技能释放节点")]
    [SerializeField] private Transform 技能释放节点;
    [SerializeField] private 技能目标瞄准器 技能目标瞄准器;
    [SerializeField] private 技能注册表 技能配置表;
    [SerializeField] private 技能栏控制器 技能栏;
    [SerializeField] private string basicAttackActionId = "basic_attack";
    [Header("技能配置")]
    [SerializeField, FormerlySerializedAs("skillId")] private string 火球术技能ID = "fireball_01";
    [SerializeField] private string 斩灵术技能ID = "slash_01";
    [SerializeField] private 节能技能特效 火球术特效;
    [SerializeField] private 节能技能特效 斩灵术特效;
    [SerializeField] private 技能粒子特效 爆破粒子特效;
    [Header("本地技能冷却")]
    [SerializeField, Min(0.01f)] private float 技能统一冷却秒数 = 3.2f;
    [SerializeField, Min(1f)] private float 服务端技能Tick每秒 = 20f;
    [Header("技能请求等待")]
    [SerializeField, Min(1f)] private float 技能受理等待超时秒数 = 5f;
    [SerializeField, Min(1f)] private float 技能结果网络余量秒数 = 5f;

    private long nextActionSequence;
    private long nextSkillRequestSequence;
    private bool combatEnabled;
    private LocalPlayerVisualSmoother visualSmoother;
    private CombatMessageReceiver combatReceiver;
    private MonsterSnapshotManager monsterManager;
    private readonly Queue<(string ActionId, long Sequence)> recentAttacks =
        new Queue<(string ActionId, long Sequence)>();
    private long recentAttackEpoch;
    private string recentAttackSceneSessionId;
    private NetMessageDispatcher registeredDispatcher;
    private readonly Dictionary<string, float> skillCooldownDeadlines =
        new Dictionary<string, float>(System.StringComparer.Ordinal);
    private readonly Dictionary<string, long> latestSkillCooldownSequences =
        new Dictionary<string, long>(System.StringComparer.Ordinal);
    private readonly Dictionary<long, PendingSkillRequest> pendingSkillRequests =
        new Dictionary<long, PendingSkillRequest>();
    private readonly Dictionary<string, long> awaitingSkillAcceptance =
        new Dictionary<string, long>(System.StringComparer.Ordinal);
    private readonly List<GameObject> activeSkillEffectInstances =
        new List<GameObject>();
    private long lastSkillManaServerTick = -1;
    private long lastSkillManaServerTime = -1;
    private long lastSkillManaRequestSequence = -1;

    public long LastSentActionSequence => nextActionSequence;
    internal CombatTargetSelection LockedMonsterTarget => this != null && isActiveAndEnabled
        && combatEnabled && targetingController != null
        ? targetingController.LockedMonsterTarget
        : default;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        EnsureSkillMessageRegistration();
    }

    private void Update()
    {
        EnsureSkillMessageRegistration();
        CheckPendingSkillRequestTimeouts();
        activeSkillEffectInstances.RemoveAll(effect => effect == null);
    }

    private void OnDisable()
    {
        ClearCombatTarget();
        if (combatReceiver != null)
            combatReceiver.MonsterHitConfirmed -= HandleMonsterHitConfirmed;
        combatReceiver = null;
        RemoveSkillMessageRegistration();
        ClearPendingSkillRequests();
    }

    public void SetCombatEnabled(bool enabled, bool resetSequence = false)
    {
        combatEnabled = enabled;
        if (!enabled)
        {
            ClearCombatTarget();
            ClearPendingSkillRequests();
            return;
        }

        if (resetSequence)
        {
            nextActionSequence = 0;
            nextSkillRequestSequence = 0;
            ClearCombatTarget();
            ClearPendingSkillRequests();
        }
    }

    internal void ClearCombatTarget()
    {
        recentAttacks.Clear();
        if (targetingController != null)
            targetingController.ClearTarget();
        if (visualSmoother != null)
            visualSmoother.ClearCombatFacing();
    }

    internal bool TryPickMonsterUnderPointer(out CombatTargetSelection target)
    {
        target = default;
        return combatEnabled && isActiveAndEnabled && targetingController != null
            && targetingController.TryPickMonsterUnderPointer(out target);
    }

    internal void ToggleClickedMonsterLock(CombatTargetSelection target)
    {
        if (!combatEnabled || !isActiveAndEnabled || targetingController == null || !target.IsValid
            || !target.TargetTransform.TryGetComponent(out MonsterNetworkView monster)
            || monster.EntityId != target.EntityId)
            return;

        CombatTargetSelection lockedTarget = LockedMonsterTarget;
        if (lockedTarget.TargetTransform == target.TargetTransform && lockedTarget.EntityId == target.EntityId)
        {
            ClearCombatTarget();
            return;
        }

        if (targetingController.TryLockMonster(monster))
            recentAttacks.Clear();
    }

    public void BindSkillRelease(Transform releasePoint, 技能目标瞄准器 aimer)
    {
        技能释放节点 = releasePoint;
        技能目标瞄准器 = aimer;
    }

    public bool TrySendSkillSlotCast(int slotNumber, long clientTick)
    {
        ResolveReferences();
        if (技能栏 == null)
        {
            Debug.LogError(
                "[SkillCast] No 技能栏控制器 was found in the scene.",
                this);
            return false;
        }

        if (!技能栏.TryGetSkillId(slotNumber, out string skillId))
        {
            Debug.Log(
                "[SkillCast] Skill slot " + slotNumber + " is empty.",
                this);
            return false;
        }

        return TrySendSkillCast(clientTick, skillId);
    }

    public bool TrySendSkillCast(long clientTick, string requestedSkillId)
    {
        string normalizedSkillId = NormalizeSkillId(requestedSkillId);
        if (!combatEnabled
            || clientTick <= 0
            || nextSkillRequestSequence == long.MaxValue
            || string.IsNullOrEmpty(normalizedSkillId))
        {
            return false;
        }

        if (IsSkillOnLocalCooldown(normalizedSkillId))
        {
            显示技能冷却提示();
            return false;
        }

        if (awaitingSkillAcceptance.ContainsKey(normalizedSkillId))
        {
            return false;
        }

        ResolveReferences();
        if (技能配置表 == null
            || !技能配置表.IsRegisteredAndValid(normalizedSkillId))
        {
            Debug.LogError(
                "[SkillCast] Skill is not registered or valid: "
                + normalizedSkillId, this);
            return false;
        }

        CombatTargetSelection target = default;
        bool hasTarget = targetingController != null
            && targetingController.TryResolveCombatTarget(out target)
            && target.IsValid
            && string.Equals(target.EntityType, "monster", System.StringComparison.Ordinal);

        if (!hasTarget)
        {
            Debug.Log("[SkillCast] " + normalizedSkillId
                + " pressed but no monster target was selected.");
            return false;
        }

        NetworkManager networkManager = NetworkManager.Instance;
        GameSession session = FindObjectOfType<GameSession>();
        if (networkManager == null || !networkManager.IsConnected
            || networkManager.ConnectionEpoch <= 0
            || session == null || session.LocalCharacterDbId <= 0)
        {
            return false;
        }

        long requestSequence = nextSkillRequestSequence + 1L;
        SkillCastRequest request = new SkillCastRequest
        {
            requestSequence = requestSequence,
            clientTick = clientTick,
            skillId = normalizedSkillId,
            targetType = "monster",
            targetId = target.EntityId
        };

        if (!networkManager.SendSkillCast(request))
        {
            return false;
        }

        nextSkillRequestSequence = requestSequence;
        TrackAttackForLock(networkManager, request.skillId, requestSequence);
        FaceAttackTarget(target);
        latestSkillCooldownSequences[normalizedSkillId] = requestSequence;
        skillCooldownDeadlines[normalizedSkillId] =
            Time.unscaledTime + Mathf.Max(0.01f, 技能统一冷却秒数);
        pendingSkillRequests[requestSequence] = new PendingSkillRequest
        {
            SkillId = normalizedSkillId,
            TargetType = request.targetType,
            TargetId = request.targetId,
            TargetTransform = target.TargetTransform,
            CharacterDbId = session.LocalCharacterDbId,
            ConnectionEpoch = networkManager.ConnectionEpoch,
            SentAt = Time.unscaledTime
        };
        awaitingSkillAcceptance[normalizedSkillId] = requestSequence;
        Debug.Log("[SkillCast][TX] skillId=" + normalizedSkillId
            + " requestSequence=" + requestSequence
            + " targetType=" + target.EntityType
            + " targetId=" + target.EntityId);
        return true;
    }

    public bool TrySendBasicAttack(long clientTick, float aimYaw)
    {
        if (!combatEnabled || clientTick <= 0 || nextActionSequence == long.MaxValue)
        {
            return false;
        }

        ResolveReferences();
        CombatTargetSelection target = default;
        bool hasTarget = targetingController != null
            && targetingController.TryResolveCombatTarget(out target)
            && target.IsValid;

        if (hasTarget && target.EntityType == "monster"
            && target.TryGetHorizontalYaw(transform.position, out float targetYaw))
        {
            aimYaw = targetYaw;
        }

        NetworkManager networkManager = NetworkManager.Instance;
        if (networkManager == null || !networkManager.IsConnected)
        {
            return false;
        }

        long actionSequence = nextActionSequence + 1L;
        CombatActionRequest request = new CombatActionRequest
        {
            actionSequence = actionSequence,
            clientTick = clientTick,
            actionId = string.IsNullOrWhiteSpace(basicAttackActionId)
                ? "basic_attack"
                : basicAttackActionId.Trim(),
            aimYaw = Mathf.Repeat(aimYaw, 360f)
        };

        if (hasTarget)
        {
            request.targetType = target.EntityType;
            request.targetId = target.EntityId;
        }

        if (!networkManager.SendCombatAction(request))
        {
            return false;
        }

        nextActionSequence = actionSequence;
        TrackAttackForLock(networkManager, request.actionId, actionSequence);
        if (hasTarget)
        {
            if (target.EntityType == "player")
                targetingController.ConfirmCombatTarget(target);
            FaceAttackTarget(target);
        }
        return true;
    }

    private void FaceAttackTarget(CombatTargetSelection target)
    {
        if (visualSmoother != null && target.EntityType == "monster"
            && target.TryGetHorizontalYaw(visualSmoother.VisualWorldPosition, out float yaw))
        {
            visualSmoother.FaceCombatTargetImmediately(yaw);
        }
    }

    private void TrackAttackForLock(NetworkManager network, string actionId, long sequence)
    {
        if (combatReceiver == null)
        {
            combatReceiver = FindObjectOfType<CombatMessageReceiver>();
            if (combatReceiver != null)
                combatReceiver.MonsterHitConfirmed += HandleMonsterHitConfirmed;
        }

        if (recentAttackEpoch != network.ConnectionEpoch
            || recentAttackSceneSessionId != network.SceneSessionId)
        {
            recentAttacks.Clear();
            recentAttackEpoch = network.ConnectionEpoch;
            recentAttackSceneSessionId = network.SceneSessionId;
        }

        if (recentAttacks.Count >= RecentAttackCapacity)
            recentAttacks.Dequeue();
        recentAttacks.Enqueue((actionId, sequence));
    }

    private void HandleMonsterHitConfirmed(CombatEventMessage hit)
    {
        TryLockFromConfirmedHit(hit.actionId, hit.actionSequence, hit.source.entityId, hit.target.entityId);
    }

    private void TryLockFromConfirmedHit(string actionId, long sequence, string sourceId,
        string monsterInstanceId, Transform expectedTarget = null)
    {
        if (!combatEnabled || !isActiveAndEnabled || targetingController == null
            || LockedMonsterTarget.IsValid || !recentAttacks.Contains((actionId, sequence)))
            return;

        NetworkManager network = NetworkManager.Instance;
        GameSession session = FindObjectOfType<GameSession>();
        if (network == null || !network.IsConnected || network.ConnectionEpoch != recentAttackEpoch
            || network.SceneSessionId != recentAttackSceneSessionId || session == null || !session.IsWorldReady
            || session.LocalCharacterDbId <= 0
            || sourceId != session.LocalCharacterDbId.ToString(CultureInfo.InvariantCulture))
            return;

        if (monsterManager == null)
            monsterManager = FindObjectOfType<MonsterSnapshotManager>();
        if (monsterManager != null && monsterManager.TryGetTargetableMonster(monsterInstanceId, out var monster)
            && (expectedTarget == null || monster.transform == expectedTarget)
            && targetingController.TryLockMonster(monster))
            recentAttacks.Clear();
    }

    private void ResolveReferences()
    {
        if (targetingController == null)
        {
            targetingController = GetComponent<MonsterTargetingController>();
        }

        if (targetingController == null)
        {
            targetingController = gameObject.AddComponent<MonsterTargetingController>();
        }

        if (visualSmoother == null)
            TryGetComponent(out visualSmoother);

        if (技能配置表 == null)
        {
            技能配置表 = GetComponent<技能注册表>();
        }

        if (技能配置表 == null)
        {
            技能配置表 = FindObjectOfType<技能注册表>();
        }

        if (技能栏 == null)
        {
            技能栏 = FindObjectOfType<技能栏控制器>();
        }

        if (技能配置表 == null)
        {
            Debug.LogError(
                "[SkillCast] No persistent 技能注册表 was found. "
                + "Add 技能注册表 to the scene GameSession object.", this);
        }

        // 修复旧预制体可能把两个技能字段指向同一个斩灵术组件的问题。
        if (火球术特效 != null && 火球术特效 == 斩灵术特效)
        {
            火球术特效 = null;
        }

        if (火球术特效 == null || 斩灵术特效 == null)
        {
            节能技能特效[] effects = GetComponentsInChildren<节能技能特效>(true);
            for (int index = 0; index < effects.Length; index++)
            {
                节能技能特效 effect = effects[index];
                if (effect == null)
                {
                    continue;
                }

                string objectName = effect.gameObject.name;
                if (火球术特效 == null
                    && (objectName.Contains("火球")
                        || objectName.Contains("爆破")
                        || objectName.Contains("爆炸")
                        || objectName.IndexOf("blast",
                            System.StringComparison.OrdinalIgnoreCase) >= 0
                        || objectName.IndexOf("explosion",
                            System.StringComparison.OrdinalIgnoreCase) >= 0
                        || objectName.IndexOf("fireball",
                            System.StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    火球术特效 = effect;
                }

                if (斩灵术特效 == null
                    && (objectName.Contains("斩灵")
                        || objectName.IndexOf("slash",
                            System.StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    斩灵术特效 = effect;
                }
            }
        }

        if (爆破粒子特效 == null)
        {
            爆破粒子特效 = GetComponentInChildren<技能粒子特效>(true);
        }

    }

    private void EnsureSkillMessageRegistration()
    {
        NetMessageDispatcher currentDispatcher = NetMessageDispatcher.Instance;
        if (registeredDispatcher == currentDispatcher)
        {
            return;
        }

        RemoveSkillMessageRegistration();
        if (currentDispatcher == null)
        {
            return;
        }

        currentDispatcher.Register("skill_cast_started", HandleSkillCastStarted);
        currentDispatcher.Register("skill_cast_result", HandleSkillCastResult);
        registeredDispatcher = currentDispatcher;
    }

    private void RemoveSkillMessageRegistration()
    {
        if (registeredDispatcher == null)
        {
            return;
        }

        registeredDispatcher.Unregister("skill_cast_started", HandleSkillCastStarted);
        registeredDispatcher.Unregister("skill_cast_result", HandleSkillCastResult);
        registeredDispatcher = null;
    }

    private void HandleSkillCastStarted(string json)
    {
        SkillCastStarted started;
        try
        {
            started = JsonUtility.FromJson<SkillCastStarted>(json);
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning("[SkillCast] Failed to parse skill_cast_started: "
                + exception.Message);
            return;
        }

        if (started == null || started.msgId != "skill_cast_started"
            || started.type != "skill_cast_started"
            || started.requestSequence <= 0
            || !pendingSkillRequests.TryGetValue(
                started.requestSequence, out PendingSkillRequest pending)
            || pending.StartedMessage != null
            || !MatchesCurrentSkillSession(pending)
            || !string.Equals(started.casterId,
                pending.CharacterDbId.ToString(CultureInfo.InvariantCulture),
                System.StringComparison.Ordinal)
            || !string.Equals(started.skillId, pending.SkillId,
                System.StringComparison.Ordinal)
            || !string.Equals(started.targetType, pending.TargetType,
                System.StringComparison.Ordinal)
            || !string.Equals(started.targetId, pending.TargetId,
                System.StringComparison.Ordinal))
        {
            return;
        }

        pending.StartedMessage = started;
        pending.StartedAt = Time.unscaledTime;
        ReleaseAcceptanceLock(pending.SkillId, started.requestSequence);
        ApplyStartedCooldown(pending.SkillId, started);
        ApplyStartedMana(started);
        ResolveReferences();
        if (技能配置表 != null
            && 技能配置表.TryGet(pending.SkillId, out 技能释放配置 animationConfig))
        {
            GetComponent<PredictedCharacterMotor>()
                ?.PlayGroundActionAnimation(animationConfig.本地动画触发器);
        }
        Debug.Log("[SkillCast][STARTED] skillId=" + pending.SkillId
            + " requestSequence=" + started.requestSequence
            + " targetType=" + started.targetType
            + " targetId=" + started.targetId
            + " hitTick=" + started.hitTick);

        if (!TryGetStartedTargetPosition(started, out Vector3 targetPosition))
        {
            Debug.LogError("[SkillCast] Invalid target position in skill_cast_started"
                + " requestSequence=" + started.requestSequence, this);
            return;
        }

        pending.EffectInstance = 播放技能特效(
            pending.SkillId, pending.TargetType, pending.TargetId, targetPosition);
        if (pending.EffectInstance != null)
        {
            activeSkillEffectInstances.Add(pending.EffectInstance);
            Debug.Log("[SkillCast][FX] skillId=" + pending.SkillId
                + " requestSequence=" + started.requestSequence
                + " targetPosition=" + targetPosition);
        }
    }

    private void HandleSkillCastResult(string json)
    {
        SkillCastResult result;
        try
        {
            result = JsonUtility.FromJson<SkillCastResult>(json);
        }
        catch (System.Exception exception)
        {
            Debug.LogWarning("[SkillCast] Failed to parse skill_cast_result: " + exception.Message);
            return;
        }

        if (result == null
            || result.requestSequence <= 0
            || !pendingSkillRequests.TryGetValue(
                result.requestSequence,
                out PendingSkillRequest pending)
            || !MatchesCurrentSkillSession(pending)
            || (!string.IsNullOrEmpty(result.skillId)
                && !string.Equals(result.skillId, pending.SkillId,
                    System.StringComparison.Ordinal))
            || (!string.IsNullOrEmpty(result.casterId)
                && !string.Equals(result.casterId,
                    pending.CharacterDbId.ToString(CultureInfo.InvariantCulture),
                    System.StringComparison.Ordinal)))
        {
            return;
        }

        pendingSkillRequests.Remove(result.requestSequence);
        ReleaseAcceptanceLock(pending.SkillId, result.requestSequence);
        if (!result.success)
        {
            if (pending.StartedMessage == null
                && IsLatestSkillCooldownRequest(
                    pending.SkillId, result.requestSequence))
            {
                skillCooldownDeadlines.Remove(pending.SkillId);
            }
            else if (pending.StartedMessage != null)
            {
                StopPendingSkillEffect(pending);
                ApplyResultCooldown(pending.SkillId, result);
                ApplyAuthoritativeSkillMana(result.currentMp, result.maxMp,
                    result.serverTick, result.serverTime,
                    result.requestSequence);
            }

            Debug.LogWarning("[SkillCast] Server rejected skill "
                + result.skillId + ": " + result.errorCode + " " + result.message);
            GameNoticeService.Show(string.IsNullOrWhiteSpace(result.message)
                ? "技能释放失败，请稍后重试" : result.message.Trim());
            return;
        }

        ApplyResultCooldown(pending.SkillId, result);
        ApplyAuthoritativeSkillMana(result.currentMp, result.maxMp,
            result.serverTick, result.serverTime, result.requestSequence);
        if (result.damage > 0 && result.targetType == "monster"
            && result.targetId == pending.TargetId && pending.TargetTransform != null)
        {
            TryLockFromConfirmedHit(pending.SkillId, result.requestSequence,
                result.casterId, result.targetId, pending.TargetTransform);
        }

        Debug.Log("[SkillCast][RX] success=true"
            + " skillId=" + (result.skillId ?? pending.SkillId)
            + " requestSequence=" + result.requestSequence
            + " targetType=" + result.targetType
            + " targetId=" + result.targetId);
        if (pending.StartedMessage == null)
        {
            Debug.LogWarning("[SkillCast] Result arrived without skill_cast_started;"
                + " effect was not played. requestSequence=" + result.requestSequence);
        }
    }

    private string NormalizeSkillId(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private bool MatchesCurrentSkillSession(PendingSkillRequest pending)
    {
        NetworkManager networkManager = NetworkManager.Instance;
        GameSession session = FindObjectOfType<GameSession>();
        return combatEnabled && networkManager != null && networkManager.IsConnected
            && networkManager.ConnectionEpoch == pending.ConnectionEpoch
            && session != null && session.LocalCharacterDbId == pending.CharacterDbId;
    }

    private void ReleaseAcceptanceLock(string skillId, long requestSequence)
    {
        if (awaitingSkillAcceptance.TryGetValue(skillId, out long waitingSequence)
            && waitingSequence == requestSequence)
            awaitingSkillAcceptance.Remove(skillId);
    }

    private void ApplyStartedCooldown(string skillId, SkillCastStarted started)
    {
        ApplyServerCooldown(skillId, started.requestSequence,
            started.serverTick, started.cooldownUntilTick);
    }

    private void ApplyResultCooldown(string skillId, SkillCastResult result)
    {
        if (result.serverTick <= 0 || result.cooldownUntilTick <= 0)
            return;

        ApplyServerCooldown(skillId, result.requestSequence,
            result.serverTick, result.cooldownUntilTick);
    }

    private bool IsLatestSkillCooldownRequest(string skillId, long sequence)
    {
        return latestSkillCooldownSequences.TryGetValue(
            skillId, out long latestSequence) && latestSequence == sequence;
    }

    private void ApplyServerCooldown(
        string skillId, long sequence, long serverTick, long cooldownUntilTick)
    {
        if (!IsLatestSkillCooldownRequest(skillId, sequence))
            return;

        if (serverTick < 0 || cooldownUntilTick < 0)
        {
            Debug.LogWarning("[SkillCast] Invalid server cooldown tick for "
                + skillId + "; keeping the provisional cooldown.");
            return;
        }

        double remainingSeconds = System.Math.Max(
            0d, (double)cooldownUntilTick - serverTick)
            / Mathf.Max(1f, 服务端技能Tick每秒);
        skillCooldownDeadlines[skillId] =
            Time.unscaledTime + (float)System.Math.Min(remainingSeconds, float.MaxValue);
    }

    private void ApplyStartedMana(SkillCastStarted started)
    {
        ApplyAuthoritativeSkillMana(started.currentMp, started.maxMp,
            started.serverTick, started.serverTime, started.requestSequence);
    }

    private void ApplyAuthoritativeSkillMana(
        int currentMp, int maxMp, long serverTick, long serverTime, long sequence)
    {
        if (maxMp <= 0 || currentMp < 0
            || currentMp > maxMp || serverTick < 0 || serverTime <= 0
            || serverTick < lastSkillManaServerTick
            || (serverTick == lastSkillManaServerTick
                && (serverTime < lastSkillManaServerTime
                    || (serverTime == lastSkillManaServerTime
                        && sequence <= lastSkillManaRequestSequence))))
            return;

        GameSession session = FindObjectOfType<GameSession>();
        if (session == null || !session.ApplyItemUseMana(
            currentMp, maxMp))
            return;

        lastSkillManaServerTick = serverTick;
        lastSkillManaServerTime = serverTime;
        lastSkillManaRequestSequence = sequence;
    }

    private static bool TryGetStartedTargetPosition(
        SkillCastStarted started, out Vector3 position)
    {
        position = default;
        if (double.IsNaN(started.targetX) || double.IsInfinity(started.targetX)
            || System.Math.Abs(started.targetX) > float.MaxValue
            || double.IsNaN(started.targetY) || double.IsInfinity(started.targetY)
            || System.Math.Abs(started.targetY) > float.MaxValue
            || double.IsNaN(started.targetZ) || double.IsInfinity(started.targetZ)
            || System.Math.Abs(started.targetZ) > float.MaxValue)
        {
            return false;
        }

        position = new Vector3(
            (float)started.targetX, (float)started.targetY, (float)started.targetZ);
        return true;
    }

    private GameObject 播放技能特效(
        string requestedSkillId, string targetType, string targetId,
        Vector3 targetPosition)
    {
        技能释放配置 config;
        if (技能配置表 == null
            || !技能配置表.TryGet(requestedSkillId, out config))
        {
            Debug.LogError("[SkillCast] Skill is not registered: "
                + requestedSkillId, this);
            return null;
        }

        if (config.释放方式 == 技能释放方式.怪物目标位置)
        {
            return 播放目标位置特效(requestedSkillId, targetType, targetId,
                targetPosition, config.特效预制体);
        }

        return 播放玩家节点特效(
            requestedSkillId, targetId, targetPosition, config.特效预制体);
    }

    private GameObject 播放玩家节点特效(
        string requestedSkillId,
        string targetId,
        Vector3 targetPosition,
        GameObject effectPrefab)
    {
        if (技能释放节点 == null)
        {
            Debug.LogError(
                "[SkillCast] The local player skill release point is not bound. "
                + "Effect was not played.", this);
            return null;
        }

        if (技能目标瞄准器 == null)
        {
            Debug.LogError(
                "[SkillCast] 技能目标瞄准器 is missing. "
                + "Player-origin effect was not played.", this);
            return null;
        }

        if (!技能目标瞄准器.TryGetRotation(
            技能释放节点.position,
            targetPosition,
            out Quaternion aimRotation))
        {
            Debug.LogError(
                "[SkillCast] Cannot aim player-origin effect at target: "
                + targetId, this);
            return null;
        }

        if (effectPrefab != null)
        {
            return 播放预制体特效(effectPrefab,
                技能释放节点.position, aimRotation);
        }

        if (string.Equals(requestedSkillId, 斩灵术技能ID?.Trim(),
            System.StringComparison.Ordinal))
        {
            if (斩灵术特效 == null)
            {
                Debug.LogWarning("[SkillCast] No slash effect is mapped for skillId: "
                    + requestedSkillId);
                return null;
            }
            return 斩灵术特效.创建并释放到位置(
                技能释放节点.position, aimRotation);
        }

        if (string.Equals(requestedSkillId, 火球术技能ID?.Trim(),
            System.StringComparison.Ordinal))
        {
            if (火球术特效 == null)
            {
                Debug.LogWarning("[SkillCast] No player effect is mapped for skillId: "
                    + requestedSkillId);
                return null;
            }
            return 火球术特效.创建并释放到位置(
                技能释放节点.position, aimRotation);
        }

        Debug.LogWarning("[SkillCast] No player effect mapped for skillId: "
            + requestedSkillId);
        return null;
    }

    private GameObject 播放目标位置特效(
        string requestedSkillId, string targetType, string targetId,
        Vector3 targetPosition, GameObject effectPrefab)
    {
        if (!string.Equals(targetType, "monster",
            System.StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(targetId))
        {
            Debug.LogWarning("[SkillCast] Target effect has no valid monster target: "
                + requestedSkillId);
            return null;
        }

        if (effectPrefab != null)
        {
            return 播放预制体特效(effectPrefab,
                targetPosition, Quaternion.identity);
        }

        if (爆破粒子特效 != null)
        {
            return 爆破粒子特效.创建并释放到位置(
                targetPosition, Quaternion.identity);
        }

        Debug.LogWarning("[SkillCast] No target effect is mapped for skillId: "
            + requestedSkillId);
        return null;
    }

    private GameObject 播放预制体特效(
        GameObject effectPrefab, Vector3 position, Quaternion rotation)
    {
        if (effectPrefab == null)
        {
            return null;
        }

        GameObject instance = Instantiate(effectPrefab, position, rotation);
        技能粒子特效 particleEffect =
            instance.GetComponentInChildren<技能粒子特效>(true);
        if (particleEffect != null)
        {
            particleEffect.释放技能();
        }
        else
        {
            节能技能特效 customEffect =
                instance.GetComponentInChildren<节能技能特效>(true);
            if (customEffect != null)
            {
                customEffect.释放技能();
            }
            else
            {
                ParticleSystem[] particles =
                    instance.GetComponentsInChildren<ParticleSystem>(true);
                for (int index = 0; index < particles.Length; index++)
                {
                    particles[index]?.Play(true);
                }
            }
        }

        Destroy(instance, 5f);
        return instance;
    }

    private void CheckPendingSkillRequestTimeouts()
    {
        if (pendingSkillRequests.Count == 0)
            return;

        float now = Time.unscaledTime;
        foreach (KeyValuePair<long, PendingSkillRequest> entry in pendingSkillRequests)
        {
            PendingSkillRequest pending = entry.Value;
            if (pending.StartedMessage == null)
            {
                if (!pending.AcceptanceTimeoutLogged
                    && now - pending.SentAt >= 技能受理等待超时秒数)
                {
                    pending.AcceptanceTimeoutLogged = true;
                    Debug.LogWarning("[SkillCast][TIMEOUT] Awaiting acceptance"
                        + " skillId=" + pending.SkillId
                        + " requestSequence=" + entry.Key
                        + "; request was not retried.", this);
                }
                continue;
            }

            double ticksUntilHit = System.Math.Max(
                0d, (double)pending.StartedMessage.hitTick
                    - pending.StartedMessage.serverTick);
            double waitSeconds = ticksUntilHit
                / Mathf.Max(1f, 服务端技能Tick每秒)
                + Mathf.Max(1f, 技能结果网络余量秒数);
            if (!pending.ResultTimeoutLogged
                && now - pending.StartedAt >= waitSeconds)
            {
                pending.ResultTimeoutLogged = true;
                Debug.LogWarning("[SkillCast][TIMEOUT] Awaiting final result"
                    + " skillId=" + pending.SkillId
                    + " requestSequence=" + entry.Key
                    + " hitTick=" + pending.StartedMessage.hitTick
                    + "; request was not retried.", this);
            }
        }
    }

    private void StopPendingSkillEffect(PendingSkillRequest pending)
    {
        GameObject effect = pending.EffectInstance;
        pending.EffectInstance = null;
        if (effect == null)
            return;

        activeSkillEffectInstances.Remove(effect);
        Destroy(effect);
    }

    private void ClearPendingSkillRequests()
    {
        for (int index = 0; index < activeSkillEffectInstances.Count; index++)
        {
            GameObject effect = activeSkillEffectInstances[index];
            if (effect != null)
                Destroy(effect);
        }
        activeSkillEffectInstances.Clear();
        pendingSkillRequests.Clear();
        awaitingSkillAcceptance.Clear();
        latestSkillCooldownSequences.Clear();
        lastSkillManaServerTick = -1;
        lastSkillManaServerTime = -1;
        lastSkillManaRequestSequence = -1;
        skillCooldownDeadlines.Clear();
    }

    private bool IsSkillOnLocalCooldown(string skillId)
    {
        if (!skillCooldownDeadlines.TryGetValue(skillId, out float deadline))
        {
            return false;
        }

        if (Time.unscaledTime >= deadline)
        {
            skillCooldownDeadlines.Remove(skillId);
            return false;
        }

        return true;
    }

    private void 显示技能冷却提示()
    {
        GameNoticeService.Show("技能冷却中");
    }
}
