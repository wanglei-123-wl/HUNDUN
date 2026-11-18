using UnityEngine;

public readonly struct CombatTargetSelection
{
    public string EntityType { get; }
    public string EntityId { get; }
    public Transform TargetTransform { get; }

    public bool IsValid =>
        !string.IsNullOrWhiteSpace(EntityType)
        && !string.IsNullOrWhiteSpace(EntityId)
        && TargetTransform != null;

    public CombatTargetSelection(string entityType, string entityId, Transform targetTransform)
    {
        EntityType = entityType ?? string.Empty;
        EntityId = entityId ?? string.Empty;
        TargetTransform = targetTransform;
    }

    internal bool TryGetHorizontalYaw(Vector3 origin, out float yaw, float minimumDistance = 0.01f)
    {
        yaw = 0f;
        if (!IsValid) return false;

        Vector3 direction = TargetTransform.position - origin;
        direction.y = 0f;
        if (direction.sqrMagnitude <= minimumDistance * minimumDistance) return false;

        yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        return true;
    }
}
