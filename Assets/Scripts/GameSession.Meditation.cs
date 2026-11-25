using System.Globalization;
using Game.Client.BattleSync;
using UnityEngine;

public partial class GameSession
{
    private long meditationVitalsEpoch;
    private string meditationVitalsSession;
    private long lastMeditationVitalsRevision = -1;
    private CombatStateStore meditationCombatStore;
    private EntityManager meditationEntityManager;

    private void RegisterMeditationMessages()
    {
        messageDispatcher.Register("meditation_result", HandleMeditationResult);
        messageDispatcher.Register("meditation_state", HandleMeditationState);
        messageDispatcher.Register("meditation_vitals", HandleMeditationVitals);
        messageDispatcher.Register("error", HandleMeditationError);
    }

    private void UnregisterMeditationMessages()
    {
        messageDispatcher.Unregister("meditation_result", HandleMeditationResult);
        messageDispatcher.Unregister("meditation_state", HandleMeditationState);
        messageDispatcher.Unregister("meditation_vitals", HandleMeditationVitals);
        messageDispatcher.Unregister("error", HandleMeditationError);
    }

    private bool IsCurrentMeditationSession(string session) =>
        sessionStarted && gameSessionAccepted && !string.IsNullOrEmpty(session)
        && networkManager?.SceneSessionId == session;

    private void HandleMeditationResult(string json)
    {
        var result = JsonUtility.FromJson<MeditationResultMessage>(json);
        if (result != null && IsCurrentMeditationSession(result.sceneSessionId))
            localPlayerInputController?.ReceiveMeditationResult(result);
    }

    private void HandleMeditationError(string json)
    {
        var error = JsonUtility.FromJson<ServerErrorMessage>(json);
        if (gameSessionAccepted && error?.requestMsgId == "meditation_set")
            localPlayerInputController?.ReceiveMeditationError(error);
    }

    private void HandleMeditationState(string json)
    {
        var state = JsonUtility.FromJson<MeditationStateMessage>(json);
        if (state == null || !IsCurrentMeditationSession(state.sceneSessionId)
            || state.sceneId != sceneId || state.characterDbId <= 0
            || state.visibilityRevision < 0 || state.meditation == null
            || state.meditation.revision < 0)
            return;

        string identity = System.FormattableString.Invariant(
            $"meditation:{state.sceneSessionId}:{state.characterDbId}:{state.visibilityRevision}:{state.meditation.revision}:{state.meditation.isMeditating}");
        var context = new ReliableMessageContext(networkManager.ConnectionEpoch, state.sceneId,
            state.msgId, state.reliableMessageId, identity,
            (long)(Time.realtimeSinceStartupAsDouble * 1000), 0, 0);
        battleSyncRuntime?.Coordinator?.ProcessReliableMessage(context, () => ApplyMeditationState(state));
    }

    private BattleSyncApplyResult ApplyMeditationState(MeditationStateMessage state)
    {
        if (state.characterDbId == localCharacterDbId)
        {
            if (localPlayerInputController == null) return BattleSyncApplyResult.RetryableFailure;
            return localPlayerInputController.ApplyMeditationState(state.meditation)
                ? BattleSyncApplyResult.Applied : BattleSyncApplyResult.IgnoredStale;
        }

        if (meditationEntityManager == null && entityManagerObject != null)
            meditationEntityManager = entityManagerObject.GetComponent<EntityManager>();
        return meditationEntityManager == null ? BattleSyncApplyResult.RetryableFailure
            : meditationEntityManager.ApplyRemoteMeditation(state);
    }

    private void HandleMeditationVitals(string json)
    {
        var vitals = JsonUtility.FromJson<MeditationVitalsMessage>(json);
        if (vitals == null || !IsCurrentMeditationSession(vitals.sceneSessionId)
            || vitals.sceneId != sceneId || vitals.characterDbId != localCharacterDbId
            || localPlayerDead || vitals.combatRevision < 0 || vitals.currentHp <= 0
            || vitals.maxHp < vitals.currentHp || vitals.currentMp < 0 || vitals.maxMp < vitals.currentMp)
            return;

        long epoch = networkManager.ConnectionEpoch;
        if (meditationVitalsEpoch != epoch || meditationVitalsSession != vitals.sceneSessionId)
        {
            meditationVitalsEpoch = epoch;
            meditationVitalsSession = vitals.sceneSessionId;
            lastMeditationVitalsRevision = -1;
        }
        if (vitals.combatRevision <= lastMeditationVitalsRevision) return;

        if (meditationCombatStore == null) meditationCombatStore = FindObjectOfType<CombatStateStore>();
        if (meditationCombatStore == null) return;
        string id = vitals.characterDbId.ToString(CultureInfo.InvariantCulture);
        if (meditationCombatStore.TryGetState("player", id, out CombatStateData current)
            && (current.CombatRevision > vitals.combatRevision || current.Hp <= 0))
            return;

        meditationCombatStore.MergeEventHealth(sceneId,
            new CombatEntityMessage { entityType = "player", entityId = id },
            vitals.currentHp, vitals.maxHp, "Alive", vitals.combatRevision);
        ApplyItemUseMana(vitals.currentMp, vitals.maxMp, combatRevision: vitals.combatRevision);
        lastMeditationVitalsRevision = vitals.combatRevision;
    }
}
