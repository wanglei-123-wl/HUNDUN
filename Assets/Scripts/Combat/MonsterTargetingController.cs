using System;
using System.Globalization;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class MonsterTargetingController : MonoBehaviour
{
    [SerializeField] private Camera targetCamera;
    [SerializeField, Min(0.1f)] private float maxTargetDistance = 100f;
    [SerializeField, Min(0.1f)] private float fallbackTargetDistance = 12f;
    [SerializeField, Range(0f, 180f)] private float fallbackHalfAngle = 60f;
    [SerializeField] private LayerMask targetLayers = ~0;

    private CombatTargetSelection currentTarget;
    private MonsterNetworkView targetMonster;

    public CombatTargetSelection CurrentTarget => IsValidTarget(currentTarget)
        && (currentTarget.EntityType != "monster" || (targetMonster != null
            && targetMonster.IsTargetable && targetMonster.EntityId == currentTarget.EntityId))
        ? currentTarget
        : default;

    public CombatTargetSelection LockedMonsterTarget => targetMonster != null && targetMonster.SelectionRing != null
        ? CurrentTarget
        : default;

    private void LateUpdate()
    {
        if (currentTarget.EntityId != null && !CurrentTarget.IsValid)
            ClearTarget();
    }

    private void OnDisable()
    {
        ClearTarget();
    }

    public bool TryResolveCombatTarget(out CombatTargetSelection target)
    {
        target = default;
        if (!isActiveAndEnabled) return false;

        CombatTargetSelection lockedTarget = LockedMonsterTarget;
        if (lockedTarget.IsValid)
        {
            target = lockedTarget;
            return true;
        }

        CombatTargetSelection previousTarget = CurrentTarget;
        if (currentTarget.EntityId != null && !previousTarget.IsValid)
            ClearTarget();

        ResolveCamera();
        if (targetCamera == null)
        {
            ClearTarget();
            return false;
        }

        if (TryRaycastTarget(out target))
            return true;

        if (TryGetFallbackMetrics(previousTarget, out _, out _))
        {
            target = previousTarget;
            return true;
        }

        if (TryFindBestFallbackTarget(out target))
            return true;

        ClearTarget();
        return false;
    }

    public void ClearTarget()
    {
        if (targetMonster != null)
            targetMonster.SetSelectionRingVisible(false);

        targetMonster = null;
        currentTarget = default;
    }

    internal bool TryPickMonsterUnderPointer(out CombatTargetSelection target)
    {
        target = default;
        if (!isActiveAndEnabled) return false;
        ResolveCamera();
        if (targetCamera == null) return false;

        foreach (RaycastHit hit in GetPointerHits(~0))
        {
            Collider collider = hit.collider;
            MonsterNetworkView monster = collider.GetComponentInParent<MonsterNetworkView>();
            if ((targetLayers.value & (1 << collider.gameObject.layer)) != 0
                && monster != null && monster.SelectionRing != null
                && TryCreateMonsterTarget(monster, out target))
                return true;

            if (!collider.isTrigger)
                break;
        }

        return false;
    }

    internal bool TryLockMonster(MonsterNetworkView monster)
    {
        if (!isActiveAndEnabled || monster == null || monster.SelectionRing == null
            || !TryCreateMonsterTarget(monster, out CombatTargetSelection target))
            return false;

        ConfirmCombatTarget(target);
        return true;
    }

    internal void ConfirmCombatTarget(CombatTargetSelection target)
    {
        if (!isActiveAndEnabled || !IsValidTarget(target)) return;
        if (currentTarget.TargetTransform == target.TargetTransform
            && currentTarget.EntityId == target.EntityId && currentTarget.EntityType == target.EntityType)
            return;

        MonsterNetworkView monster = null;
        if (target.EntityType == "monster"
            && (!target.TargetTransform.TryGetComponent(out monster)
                || !monster.IsTargetable || monster.EntityId != target.EntityId))
            return;

        ClearTarget();
        currentTarget = target;
        targetMonster = monster;
        if (targetMonster != null)
            targetMonster.SetSelectionRingVisible(true);
    }

    private void ResolveCamera()
    {
        if (targetCamera != null) return;
        targetCamera = Camera.main;
        if (targetCamera != null) return;

        ThirdPersonCameraFollow cameraFollow = FindObjectOfType<ThirdPersonCameraFollow>();
        if (cameraFollow != null) targetCamera = cameraFollow.GetComponent<Camera>();
        if (targetCamera != null) return;

        Camera[] cameras = Camera.allCameras;
        for (int index = 0; index < cameras.Length; index++)
        {
            if (cameras[index] != null && cameras[index].isActiveAndEnabled)
            {
                targetCamera = cameras[index];
                return;
            }
        }
    }

    private bool TryRaycastTarget(out CombatTargetSelection target)
    {
        target = default;
        RaycastHit[] hits = GetPointerHits(targetLayers);

        for (int index = 0; index < hits.Length; index++)
        {
            RemotePlayerSync player = hits[index].collider.GetComponentInParent<RemotePlayerSync>();
            // Model-side sync components can still be present until the end of their spawn frame.
            while (player != null)
            {
                if (TryCreatePlayerTarget(player, out target)) return true;
                Transform parent = player.transform.parent;
                player = parent == null ? null : parent.GetComponentInParent<RemotePlayerSync>();
            }

            MonsterNetworkView monster = hits[index].collider.GetComponentInParent<MonsterNetworkView>();
            if (TryCreateMonsterTarget(monster, out target)) return true;
        }

        return false;
    }

    private RaycastHit[] GetPointerHits(int layers)
    {
        Ray ray = targetCamera.ScreenPointToRay(Input.mousePosition);
        RaycastHit[] hits = Physics.RaycastAll(ray, maxTargetDistance, layers, QueryTriggerInteraction.Collide);
        Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
        return hits;
    }

    private bool TryFindBestFallbackTarget(out CombatTargetSelection target)
    {
        target = default;
        float bestScore = float.PositiveInfinity;

        RemotePlayerSync[] players = FindObjectsOfType<RemotePlayerSync>();
        for (int index = 0; index < players.Length; index++)
        {
            if (TryCreatePlayerTarget(players[index], out CombatTargetSelection candidate))
            {
                ConsiderFallback(candidate, ref target, ref bestScore);
            }
        }

        MonsterNetworkView[] monsters = FindObjectsOfType<MonsterNetworkView>();
        for (int index = 0; index < monsters.Length; index++)
        {
            if (TryCreateMonsterTarget(monsters[index], out CombatTargetSelection candidate))
            {
                ConsiderFallback(candidate, ref target, ref bestScore);
            }
        }

        return target.IsValid;
    }

    private void ConsiderFallback(CombatTargetSelection candidate, ref CombatTargetSelection target, ref float bestScore)
    {
        if (!TryGetFallbackMetrics(candidate, out float distance, out float angle)) return;
        float score = distance + angle * 0.1f;
        if (score >= bestScore) return;
        bestScore = score;
        target = candidate;
    }

    private bool TryGetFallbackMetrics(CombatTargetSelection target, out float distance, out float angle)
    {
        distance = 0f;
        angle = 0f;
        if (!IsValidTarget(target) || targetCamera == null) return false;

        Vector3 offset = target.TargetTransform.position - transform.position;
        offset.y = 0f;
        distance = offset.magnitude;
        if (distance <= 0.001f || distance > fallbackTargetDistance) return false;

        Vector3 forward = targetCamera.transform.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude <= 0.001f)
        {
            forward = transform.forward;
            forward.y = 0f;
        }

        angle = Vector3.Angle(forward, offset);
        return angle <= fallbackHalfAngle;
    }

    private static bool IsValidTarget(CombatTargetSelection target)
    {
        return target.IsValid && target.TargetTransform.gameObject.activeInHierarchy;
    }

    private static bool TryCreatePlayerTarget(RemotePlayerSync player, out CombatTargetSelection target)
    {
        if (player == null || !player.isActiveAndEnabled || player.CharacterDbId <= 0)
        {
            target = default;
            return false;
        }

        target = new CombatTargetSelection(
            "player",
            player.CharacterDbId.ToString(CultureInfo.InvariantCulture),
            player.transform);
        return true;
    }

    private static bool TryCreateMonsterTarget(MonsterNetworkView monster, out CombatTargetSelection target)
    {
        if (monster == null || !monster.IsTargetable)
        {
            target = default;
            return false;
        }

        target = new CombatTargetSelection("monster", monster.EntityId, monster.transform);
        return true;
    }
}
