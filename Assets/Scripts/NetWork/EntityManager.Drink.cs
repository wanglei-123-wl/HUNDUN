using System;
using System.Collections.Generic;
using UnityEngine;

public partial class EntityManager
{
    private const int MaxSeenDrinkActions = 4096;
    private const double SeenDrinkActionSeconds = 2;

    private readonly RemoteSkillCastTimeline drinkTimeline = new RemoteSkillCastTimeline();
    private readonly Queue<(Guid ActionId, double ReceivedAt)> drinkActionOrder = new Queue<(Guid, double)>();
    private readonly HashSet<Guid> seenDrinkActions = new HashSet<Guid>();
    private string drinkSceneSessionId;

    private void BeginDrinkSession(long serverTime)
    {
        drinkSceneSessionId = NetworkManager.Instance?.SceneSessionId;
        if (!string.IsNullOrEmpty(drinkSceneSessionId))
            drinkTimeline.BeginSession(activeConnectionEpoch, serverTime, Time.realtimeSinceStartupAsDouble);
    }

    private void ClearDrinkSession()
    {
        drinkTimeline.EndSession(activeConnectionEpoch);
        drinkSceneSessionId = null;
        drinkActionOrder.Clear();
        seenDrinkActions.Clear();
    }

    private bool IsCurrentDrinkSession(NetworkManager network) =>
        network != null && network.IsConnected && activeConnectionEpoch > 0
        && network.ConnectionEpoch == activeConnectionEpoch && !string.IsNullOrEmpty(drinkSceneSessionId)
        && network.SceneSessionId == drinkSceneSessionId;

    private void ObserveDrinkServerTime(long serverTime)
    {
        if (IsCurrentDrinkSession(NetworkManager.Instance))
            drinkTimeline.ObserveServerTime(activeConnectionEpoch, serverTime, Time.realtimeSinceStartupAsDouble);
    }

    private void HandleDrinkHeartbeat(string json)
    {
        if (!IsCurrentDrinkSession(NetworkManager.Instance)) return;
        try
        {
            var heartbeat = JsonUtility.FromJson<HeartbeatAckMessage>(json);
            if (heartbeat != null) ObserveDrinkServerTime(heartbeat.serverTime);
        }
        catch (ArgumentException)
        {
            // An invalid clock sample does not refresh the scene clock.
        }
    }

    private void HandlePlayerDrinkEvent(string json)
    {
        NetworkManager network = NetworkManager.Instance;
        if (!IsCurrentDrinkSession(network) || network.SceneOperationsPaused
            || !PlayerDrinkEventMessage.TryParse(json, out PlayerDrinkEventMessage drink)
            || drink.SceneId != activeSceneId || drink.SceneSessionId != drinkSceneSessionId
            || drink.CharacterDbId == localCharacterDbId)
            return;

        double now = Time.realtimeSinceStartupAsDouble;
        if (!drinkTimeline.TryGetMinimumElapsedSeconds(activeConnectionEpoch, drink.ServerTime, now, out double age)
            || age >= PlayerDrinkEventMessage.MaxAgeSeconds || !RememberDrinkAction(drink.ActionId, now))
            return;

        if (remotePlayers.TryGetValue(drink.CharacterDbId, out RemotePlayerSync player)
            && player != null && player.VisibilityRevision == drink.VisibilityRevision)
            player.TryPlayDrinkAnimation();
    }

    private bool RememberDrinkAction(Guid actionId, double now)
    {
        while (drinkActionOrder.Count > 0 && now - drinkActionOrder.Peek().ReceivedAt >= SeenDrinkActionSeconds)
            seenDrinkActions.Remove(drinkActionOrder.Dequeue().ActionId);

        if (seenDrinkActions.Count >= MaxSeenDrinkActions || !seenDrinkActions.Add(actionId))
            return false;

        drinkActionOrder.Enqueue((actionId, now));
        return true;
    }
}
