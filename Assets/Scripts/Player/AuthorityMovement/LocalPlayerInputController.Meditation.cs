using Game.UI.Notice;
using UnityEngine;

public sealed partial class LocalPlayerInputController
{
    private const PlayerInputKeys MeditationDirectionKeys =
        PlayerInputKeys.W | PlayerInputKeys.A | PlayerInputKeys.S | PlayerInputKeys.D;
    private const PlayerInputActions MeditationMouseButtons =
        PlayerInputActions.MouseLeftHeld | PlayerInputActions.MouseRightHeld;
    private const long MeditationRetryIntervalMs = 2000;
    private const int MeditationMaxRetries = 2;

    private string meditationSceneSession;
    private long meditationConnectionEpoch;
    private long meditationRequestSequence;
    private long meditationRevision = -1;
    private bool isMeditating;
    private MeditationSetRequest pendingMeditationRequest;
    private long meditationRetryAt;
    private int meditationRetries;
    private PlayerInputKeys suppressedMeditationKeys;
    private bool suppressMeditationMouseMovement;

    private void RefreshMeditationScope()
    {
        var network = NetworkManager.Instance;
        long epoch = network?.ConnectionEpoch ?? 0;
        string sceneSession = network?.SceneSessionId;
        if (meditationConnectionEpoch == epoch && meditationSceneSession == sceneSession)
            return;
        meditationConnectionEpoch = epoch;
        meditationSceneSession = sceneSession;
        meditationRequestSequence = 0;
        meditationRevision = -1;
        EndMeditation();
    }

    private void ApplyMeditationInput(bool qPressed, PlayerInputKeys keys,
        PlayerInputActions actions, bool jumpPressed)
    {
        if (qPressed && IsMeditationBlockingInput())
        {
            TryRequestMeditation(false);
            return;
        }

        if (IsMeditationBlockingInput())
        {
            if (HasDirectionalInput(FilterMeditationKeys(keys))
                || HasBothMouseButtons(FilterMeditationActions(actions)) || jumpPressed)
                TryRequestMeditation(false);
            return;
        }

        if (!qPressed || pendingMeditationRequest != null || jumpPressed
            || predictedMotor == null || predictedMotor.BlocksMovementForAttack()
            || !StopMovementForMeditation())
            return;

        if (TryRequestMeditation(true))
        {
            combatController?.CancelCombatApproach();
            suppressedMeditationKeys = keys & MeditationDirectionKeys;
            suppressMeditationMouseMovement = HasBothMouseButtons(actions);
        }
    }

    private PlayerInputKeys FilterMeditationKeys(PlayerInputKeys keys) => keys & ~suppressedMeditationKeys;

    private PlayerInputActions FilterMeditationActions(PlayerInputActions actions) =>
        suppressMeditationMouseMovement ? actions & ~MeditationMouseButtons : actions;

    private bool TryRequestMeditation(bool sitting)
    {
        // Keep a single request in flight. Held movement must not replace the
        // pending stand request every frame or restart meditation while rising.
        if (pendingMeditationRequest != null || sitting == isMeditating)
            return false;

        var request = new MeditationSetRequest
        {
            msgId = "meditation_set",
            type = "meditation_set",
            sceneSessionId = meditationSceneSession,
            requestSequence = meditationRequestSequence + 1,
            isMeditating = sitting
        };
        if (NetworkManager.Instance?.SendMeditation(request, true) != true)
            return false;

        meditationRequestSequence = request.requestSequence;
        pendingMeditationRequest = request;
        meditationRetries = 0;
        meditationRetryAt = MovementNow + MeditationRetryIntervalMs;
        return true;
    }

    private void UpdateMeditationRequest()
    {
        RefreshMeditationScope();
        if (suppressedMeditationKeys != PlayerInputKeys.None) suppressedMeditationKeys &= ReadKeys();
        if (suppressMeditationMouseMovement)
            suppressMeditationMouseMovement = (ReadHeldActions() & MeditationMouseButtons) != 0;
        if (pendingMeditationRequest == null || MovementNow < meditationRetryAt
            || NetworkManager.Instance?.SceneOperationsPaused == true) return;
        if (meditationRetries >= MeditationMaxRetries)
        {
            ClearMeditationRequest();
            GameNoticeService.Show("打坐操作确认超时，请重试。");
            return;
        }

        meditationRetries++;
        meditationRetryAt = MovementNow + MeditationRetryIntervalMs;
        NetworkManager.Instance?.SendMeditation(pendingMeditationRequest, false);
    }

    internal void ReceiveMeditationResult(MeditationResultMessage result)
    {
        RefreshMeditationScope();
        if (result.sceneSessionId != meditationSceneSession
            || result.requestSequence != pendingMeditationRequest?.requestSequence)
            return;

        ApplyMeditationState(result.meditation);
        ClearMeditationRequest();
        if (!result.success) ShowMeditationError(result.errorCode);
    }

    internal void ReceiveMeditationError(ServerErrorMessage error)
    {
        RefreshMeditationScope();
        if (pendingMeditationRequest == null
            || error.requestSequence != pendingMeditationRequest.requestSequence)
            return;
        ClearMeditationRequest();
        ShowMeditationError(error.errorCode);
    }

    internal bool ApplyMeditationState(MeditationSnapshot snapshot)
    {
        RefreshMeditationScope();
        if (snapshot == null || snapshot.revision < 0 || snapshot.revision <= meditationRevision
            || string.IsNullOrEmpty(meditationSceneSession))
            return false;
        meditationRevision = snapshot.revision;
        SetMeditationPosture(snapshot.isMeditating && movementSession?.IsLocalPlayerDead != true);
        return true;
    }

    internal void SeedMeditationState(MeditationSnapshot snapshot)
    {
        RefreshMeditationScope();
        if (snapshot != null) ApplyMeditationState(snapshot);
    }

    private bool IsMeditationBlockingInput()
    {
        if (pendingMeditationRequest != null || isMeditating) return true;
        if (meditationAnimator == null || !meditationAnimator.isActiveAndEnabled) return false;
        // The server can confirm standing before the stand-up clip finishes.
        // Include the outgoing transition so its last blended frames stay locked.
        return meditationAnimator.GetCurrentAnimatorStateInfo(0).IsTag(MeditationTag)
            || (meditationAnimator.IsInTransition(0)
                && meditationAnimator.GetNextAnimatorStateInfo(0).IsTag(MeditationTag));
    }

    private bool IsLocalPlayerActionBlocked() =>
        isActiveAndEnabled && inputEnabled && (IsMeditationBlockingInput()
            || predictedMotor?.BlocksActionsForPotion == true);

    private void ClearMeditationRequest()
    {
        if (!isMeditating)
        {
            suppressedMeditationKeys = PlayerInputKeys.None;
            suppressMeditationMouseMovement = false;
        }
        pendingMeditationRequest = null;
    }

    private void EndMeditation()
    {
        SetMeditationPosture(false);
        ClearMeditationRequest();
    }

    private static void ShowMeditationError(string code)
    {
        Debug.LogWarning("[MEDITATION] 打坐操作失败 " + code);
        GameNoticeService.Show(code switch
        {
            "player_airborne" => "落地后才能打坐。",
            "player_moving" => "停下来后再打坐。",
            "player_action_pending" => "请等待当前动作结束后再打坐。",
            "player_dead" => "死亡时无法打坐。",
            _ => "暂时无法打坐，请重试。"
        });
    }

    private void SetMeditationPosture(bool sitting)
    {
        isMeditating = sitting;
        if (meditationAnimator != null && meditationAnimator.isActiveAndEnabled)
            meditationAnimator.SetBool(MeditatingParameter, sitting);
        RefreshEffectState();
    }
}
