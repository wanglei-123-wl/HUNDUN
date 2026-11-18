using UnityEngine;

internal static class GroundRingSurfaceFilter
{
    internal static bool Allows(Collider collider)
    {
        return !(collider is CapsuleCollider)
            || (collider.GetComponentInParent<MonsterNetworkView>() == null
                && collider.GetComponentInParent<RemotePlayerSync>() == null
                && collider.GetComponentInParent<VillageTeleporter>() == null);
    }
}
