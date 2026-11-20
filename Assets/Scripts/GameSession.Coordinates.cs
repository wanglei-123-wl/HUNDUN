using UnityEngine;

public partial class GameSession
{
    public bool TryGetLocalPlayerCoordinates(out Vector2 coordinates)
    {
        coordinates = default;
        if (!IsWorldReady || mapTransferPaused
            || networkManager == null || !networkManager.IsConnected
            || localPlayerRootObject == null || !localPlayerRootObject.activeInHierarchy) return false;

        // The movement root already uses the map's Unity world coordinates.
        // Visual smoothing, foot offsets and animation do not change this root.
        Vector3 position = localPlayerRootObject.transform.position;
        if (float.IsNaN(position.x) || float.IsInfinity(position.x)
            || float.IsNaN(position.z) || float.IsInfinity(position.z)) return false;
        coordinates = new Vector2(position.x, position.z);
        return true;
    }
}
