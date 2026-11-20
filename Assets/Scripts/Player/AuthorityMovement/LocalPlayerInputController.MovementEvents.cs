using System;
using System.Diagnostics;
using Game.Client.BattleSync;
using UnityEngine;

public sealed partial class LocalPlayerInputController
{
    private const MovementEventFlags DirectionFlags = MovementEventFlags.Forward | MovementEventFlags.Backward
        | MovementEventFlags.StrafeLeft | MovementEventFlags.StrafeRight;
    private const MovementEventFlags TurnFlags = MovementEventFlags.TurnLeft | MovementEventFlags.TurnRight;
    private const int MovementStepMilliseconds = 16;
    private const long TurnIdleStopDelayMilliseconds = 100;
    private static readonly Stopwatch MovementClock = Stopwatch.StartNew();
    private MovementEventControl movementControl;
    private MovementEventPolicy movementPolicy;
    private MovementEventFlags movementFlags;
    private MovementEventInfo lastSent;
    private Vector3 travelDirection;
    private float targetTurnYaw;
    private float airSpeed;
    private long stepAt, airStartedAt, lastAnalogFacingAt, controlAckAt;
    private long ownedServerTick, deathMovementEpoch;
    private bool traversalStopPending;
    private Vector3 blockedTraversalDirection, blockedTraversalPosition;
    private bool blockedTraversalGrounded;
    private long lookTargetChangedAt, lastTurnEventAt;
    private bool lookTargetTracked, hasTurnEvent;
    private static long MovementNow => MovementClock.ElapsedMilliseconds;

    internal void BeginMovement(MovementEventControl control, MovementEventPolicy policy)
    {
        movementPolicy = policy;
        movementControl = null;
        deathMovementEpoch = 0;
        hasTurnEvent = false;
        ApplyMovementControl(control);
    }

    internal bool ApplyMovementControl(MovementEventControl control)
    {
        var network = NetworkManager.Instance;
        if (movementPolicy?.IsValid != true || control?.IsValidFor(network?.SceneSessionId) != true
            || (movementControl != null && control.sceneSessionId == movementControl.sceneSessionId
                && control.epoch < movementControl.epoch))
            return false;

        if (movementControl?.epoch != control.epoch || movementControl.sceneSessionId != control.sceneSessionId)
        {
            combatController?.CancelCombatApproach();
            movementControl = control;
            network.BeginMovementControl(control);
            network.MovementHeartbeatIntervalMs = movementPolicy.heartbeatIntervalMs;
            network.MovementGravity = control.gravity;
            movementFlags = control.flags;
            travelDirection = Vector3.zero;
            traversalStopPending = false;
            blockedTraversalDirection = Vector3.zero;
            lookTargetTracked = false;
            lastSent = null;
            controlAckAt = stepAt = airStartedAt = lastAnalogFacingAt = MovementNow;
            ownedServerTick = 0;
            rollReported = false;
            airSpeed = control.velocityY;
            ResetRollInput();
            reconciler.ApplyControl(control, control.reason == "movement_blocked" || control.reason == "movement_timeout"
                || control.reason == "movement_speed_exceeded" || control.reason == "movement_unsupported");
        }

        if (control.mode != "normal" || network.MovementReady)
            return true;
        return network.Send(new MovementControlAckRequest
        {
            sceneSessionId = control.sceneSessionId, movementEpoch = control.epoch, clientTimeMs = controlAckAt
        });
    }

    internal void ReceiveMovementControlResult(MovementControlEvaluation result)
    {
        if (result?.control == null || movementControl == null) return;
        var network = NetworkManager.Instance;
        if (result.control.sceneSessionId != network?.SceneSessionId) return;
        if (!result.success)
        {
            if (result.control.epoch > movementControl.epoch) ApplyMovementControl(result.control);
            return;
        }

        bool alreadyReady = network.MovementReady;
        if (network.AcceptMovementControl(result) && !alreadyReady)
            stepAt = MovementNow;
    }

    internal void RecordDeathMovementEpoch() => deathMovementEpoch = movementControl?.epoch ?? 0;

    internal bool RestoreRespawnControl()
    {
        if (movementControl?.reason != "respawn" || movementControl.mode != "normal"
            || movementControl.epoch <= deathMovementEpoch
            || movementControl.sceneSessionId != NetworkManager.Instance?.SceneSessionId)
            return false;
        reconciler.ApplyControl(movementControl, false);
        return true;
    }

    internal bool ApplyOwnedMovement(PlayerAuthoritativeState state)
    {
        if (movementControl == null || state.movementEpoch != movementControl.epoch
            || state.movementMode != movementControl.mode || state.movementMode == "normal"
            || state.serverTick <= ownedServerTick)
            return false;
        ownedServerTick = state.serverTick;
        reconciler.ApplyAuthoritativeState(state);
        return true;
    }

    private bool UpdateMovement(PlayerInputFrame input, long now)
    {
        if (predictedMotor == null || movementControl == null || !AdvanceMovement(now)) return false;
        // Refresh after advancing the previous segment, then use the same
        // movement events, map checks and heartbeat schedule as manual input.
        ApplyCombatApproachInput(input);
        clientTick = input.ClientTick;
        // Do not emit walk/run changes in response to suppressed meditation input.
        bool run = IsMeditationBlockingInput()
            ? (movementFlags & MovementEventFlags.Walking) == 0 : input.IsRunning;
        Vector3 direction = predictedMotor.MovementDirection(input);
        bool groundedForTraversal = (movementFlags & MovementEventFlags.Falling) == 0;
        if (blockedTraversalDirection != Vector3.zero)
        {
            if (direction == Vector3.zero || input.JumpPressed || groundedForTraversal != blockedTraversalGrounded
                || (transform.position - blockedTraversalPosition).sqrMagnitude > .0025f
                || Vector3.Angle(direction, blockedTraversalDirection) > 4f)
                blockedTraversalDirection = Vector3.zero;
            else
                direction = Vector3.zero;
        }
        if (direction != Vector3.zero && ((movementFlags & DirectionFlags) == 0
            || Vector3.Angle(travelDirection, direction) > 4f))
        {
            float probeSpeed = predictedMotor.Speed(run) * .995f;
            Vector3 from = transform.position;
            Vector3 probe = from + direction * (probeSpeed * MovementStepMilliseconds * .001f);
            if (!PredictedCharacterMotor.TryTraversalTarget(movementSession == null ? null : movementSession.CurrentMapTraversal,
                from, probe, movementControl.radius, movementControl.height, groundedForTraversal, out _, out _))
                direction = Vector3.zero;
        }
        MovementEventFlags directional = ReadDirectionFlags(input, direction);
        if (direction == Vector3.zero && HasDirectionalInput(input.Keys) && combatController?.HasCombatApproach == true)
            combatController.CancelCombatApproach("前方道路受阻，自动追击已停止");
        MovementEventFlags turning = ResolveTurnFlags(input, direction, now);

        bool wasWalking = (movementFlags & MovementEventFlags.Walking) != 0;
        if (wasWalking == run)
        {
            movementFlags ^= MovementEventFlags.Walking;
            predictedMotor.SetLocalMotion(travelDirection * MovementSpeed, predictedMotor.Velocity.y, predictedMotor.IsGrounded);
            if (!Report(run ? "run" : "walk", now)) return false;
        }

        MovementEventFlags oldDirection = movementFlags & DirectionFlags;
        bool changedDirection = oldDirection != directional;
        if (changedDirection)
        {
            SetTravelDirection(direction);
            movementFlags = (movementFlags & ~DirectionFlags) | directional;
            string kind = (directional & ~oldDirection) != 0 ? "start" : "stop";
            if (!Report(kind, now, travelDirection * MovementSpeed)) return false;
        }
        else if (direction != Vector3.zero && now - lastAnalogFacingAt >= 50
            && Vector3.Angle(travelDirection, direction) > 4f)
        {
            SetTravelDirection(direction);
            lastAnalogFacingAt = now;
            if (!Report("facing", now, direction * MovementSpeed)) return false;
        }

        MovementEventFlags oldTurning = movementFlags & TurnFlags;
        if (oldTurning != turning)
        {
            movementFlags = (movementFlags & ~TurnFlags) | turning;
            string kind = turning == MovementEventFlags.None ? "turn_stop" : "turn_start";
            if (!Report(kind, now)) return false;
        }

        bool jump = input.JumpPressed && predictedMotor.IsGrounded;
        if (jump)
        {
            airStartedAt = now;
            airSpeed = predictedMotor.JumpSpeed;
            movementFlags |= MovementEventFlags.Falling;
            predictedMotor.SetLocalMotion(travelDirection * MovementSpeed, airSpeed, false);
            if (!Report("jump", now)) return false;
        }

        if (!PublishRollTransition(now)) return false;

        if (HasDirectionalInput(input.Keys) || HasBothMouseButtons(input.Actions) || input.FaceCamera)
            visualSmoother?.ClearCombatFacing();
        if (input.HasAction(PlayerInputActions.PrimaryAction)
            && combatController?.TrySendBasicAttack(input.ClientTick, input.CameraYaw) != true)
            input.Actions &= ~PlayerInputActions.PrimaryAction;
        predictedMotor.PresentPredictedInput(input, jump);
        return true;
    }

    private bool PublishRollTransition(long now)
    {
        if ((movementFlags & MovementEventFlags.Falling) != 0 || !predictedMotor.IsGrounded)
            return true;

        bool playing = (rollReported || (rollEntered && rollDirection != PlayerInputKeys.None))
            && IsRollAnimationPlaying();
        if (playing == rollReported) return true;
        if (!Report(playing ? "roll_start" : "roll_end", now)) return false;
        rollReported = playing;
        return true;
    }

    private float MovementSpeed => Mathf.Max(0f,
        predictedMotor.Speed((movementFlags & MovementEventFlags.Walking) == 0) * .995f);

    private MovementEventFlags ResolveTurnFlags(PlayerInputFrame input, Vector3 direction, long now)
    {
        // Release/UI/movement stops turning immediately. Mouse samples arriving
        // between rendered frames must not create start/stop pairs every frame.
        if (!input.FaceCamera || direction != Vector3.zero)
        {
            lookTargetTracked = false;
            return MovementEventFlags.None;
        }

        if (!lookTargetTracked || Mathf.Abs(Mathf.DeltaAngle(targetTurnYaw, input.CameraYaw)) > .01f)
            lookTargetChangedAt = now;
        targetTurnYaw = input.CameraYaw;
        lookTargetTracked = true;

        MovementEventFlags oldTurning = movementFlags & TurnFlags;
        float angle = Mathf.DeltaAngle(transform.eulerAngles.y, targetTurnYaw);
        MovementEventFlags wanted = Mathf.Abs(angle) > .25f
            ? (angle < 0 ? MovementEventFlags.TurnLeft : MovementEventFlags.TurnRight)
            : (now - lookTargetChangedAt < TurnIdleStopDelayMilliseconds ? oldTurning : MovementEventFlags.None);
        // Reserve half the advertised event budget for movement edges and
        // heartbeats. Keep this clock across correction epochs so a correction
        // cannot restart the same event burst.
        int eventBudget = Math.Max(1, movementPolicy.maxEventsPerSecond);
        long minimumInterval = Math.Max(50L, (2000L + eventBudget - 1) / eventBudget);
        if (wanted != oldTurning && hasTurnEvent && now - lastTurnEventAt < minimumInterval)
            return oldTurning;
        return wanted;
    }

    private void SetTravelDirection(Vector3 direction)
    {
        travelDirection = direction;
        if (direction != Vector3.zero)
            predictedMotor.SetHeading(Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg);
        predictedMotor.SetLocalMotion(direction * MovementSpeed, predictedMotor.Velocity.y, predictedMotor.IsGrounded);
    }

    private bool AdvanceMovement(long now)
    {
        if (traversalStopPending)
        {
            if (!Report("stop", stepAt, Vector3.zero)) return false;
            traversalStopPending = false;
        }
        if (now - stepAt >= movementPolicy.movingTimeoutMs && IsActive)
            return false; // The server will supply a fresh control point for the interrupted segment.

        if (!IsActive)
        {
            stepAt = now;
            return true;
        }

        while (stepAt < now)
        {
            long end = Math.Min(stepAt + MovementStepMilliseconds, now);
            if (!StepMovement(end)) return false;
            if (!IsActive)
            {
                stepAt = now;
                break;
            }
        }
        if (now - (lastSent?.clientTimeMs ?? controlAckAt) >= movementPolicy.heartbeatIntervalMs)
            return Report("heartbeat", now);
        return true;
    }

    private bool StepMovement(long end)
    {
        float dt = (end - stepAt) * .001f;
        bool onGround = (movementFlags & MovementEventFlags.Falling) == 0;
        if ((movementFlags & TurnFlags) != 0)
            predictedMotor.SetHeading(predictedMotor.TurnStep(targetTurnYaw, dt));
        // An idle grounded actor does not call CharacterController.Move either.
        // A turn must keep that exact position/support state, rather than run a
        // downward probe through overlap recovery and map-height correction.
        if (onGround && (movementFlags & DirectionFlags) == 0)
        {
            stepAt = end;
            predictedMotor.SetLocalMotion(Vector3.zero, 0f, true);
            return true;
        }
        float age = (end - airStartedAt) * .001f;
        float vertical = onGround ? -.02f : airSpeed + predictedMotor.Gravity * age;
        float deltaY = onGround ? -.015f : (vertical - .5f * predictedMotor.Gravity * dt) * dt;
        Vector3 before = transform.position;
        Vector3 requestedDisplacement = travelDirection * (MovementSpeed * dt) + Vector3.up * deltaY;
        CollisionFlags collisions = predictedMotor.MoveLocally(requestedDisplacement,
            movementSession == null ? null : movementSession.CurrentMapTraversal,
            movementControl.radius, movementControl.height, onGround, out bool horizontalBlocked);
        stepAt = end;

        Vector3 displacement = transform.position - before;
        Vector3 horizontalVelocity = Vector3.ClampMagnitude(new Vector3(displacement.x, 0f, displacement.z) / dt, MovementSpeed);
        string edge = null;
        if (onGround && (collisions & CollisionFlags.Below) == 0)
        {
            movementFlags |= MovementEventFlags.Falling;
            airStartedAt = end;
            airSpeed = vertical = 0f;
            edge = "fall";
        }
        else if (!onGround && (collisions & CollisionFlags.Below) != 0 && vertical <= 0)
        {
            movementFlags &= ~MovementEventFlags.Falling;
            airSpeed = vertical = 0f;
            edge = "land";
        }
        else if (!onGround && (collisions & CollisionFlags.Above) != 0 && deltaY > 0)
        {
            airStartedAt = end;
            airSpeed = vertical = 0f;
            edge = "ceiling";
        }

        onGround = (movementFlags & MovementEventFlags.Falling) == 0;
        predictedMotor.SetLocalMotion(horizontalVelocity, onGround ? 0f : vertical, onGround);
        bool edgeReported = edge == null || Report(edge, end);
        if (!horizontalBlocked || (movementFlags & DirectionFlags) == 0) return edgeReported;

        combatController?.CancelCombatApproach("前方道路受阻，自动追击已停止");

        blockedTraversalDirection = travelDirection;
        blockedTraversalPosition = transform.position;
        blockedTraversalGrounded = onGround;
        var query = movementSession == null ? null : movementSession.CurrentMapTraversal;
        BattleSyncDiagnosticsLogger.LogLocalTraversalBlocked(NetworkManager.Instance.ConnectionEpoch,
            movementControl.epoch, query?.SceneId, query?.MapId, query?.PackageFingerprint,
            before, before + requestedDisplacement, transform.position, movementControl.radius, movementControl.height, onGround);
        movementFlags &= ~DirectionFlags;
        SetTravelDirection(Vector3.zero);
        traversalStopPending = true;
        if (!edgeReported || !Report("stop", end, Vector3.zero)) return false;
        traversalStopPending = false;
        return true;
    }

    private bool IsActive => (movementFlags & (DirectionFlags | TurnFlags | MovementEventFlags.Falling)) != 0;

    private bool StopMovementForMeditation()
    {
        return predictedMotor != null && predictedMotor.IsGrounded
            && (movementFlags & MovementEventFlags.Falling) == 0
            && StopMovementForTransfer() && predictedMotor.IsGrounded
            && (movementFlags & MovementEventFlags.Falling) == 0;
    }

    internal bool StopMovementForTransfer()
    {
        if (!FlushMovement()) return false;
        long now = stepAt;
        if ((movementFlags & DirectionFlags) != 0)
        {
            SetTravelDirection(Vector3.zero);
            movementFlags &= ~DirectionFlags;
            if (!Report("stop", now)) return false;
        }
        if ((movementFlags & TurnFlags) != 0)
        {
            movementFlags &= ~TurnFlags;
            if (!Report("turn_stop", now)) return false;
        }
        return true;
    }

    private bool FlushMovement()
    {
        if (movementControl == null || !inputEnabled) return true;
        long now = MovementNow;
        if (!AdvanceMovement(now)) return false;
        if (lastSent == null && !IsActive) return true;
        if (lastSent != null && lastSent.Position == transform.position
            && Mathf.Abs(Mathf.DeltaAngle(lastSent.yaw, transform.eulerAngles.y)) < .001f) return true;
        return Report("heartbeat", now);
    }

    private bool Report(string kind, long now, Vector3? horizontal = null)
    {
        Vector3 position = transform.position;
        if (!horizontal.HasValue)
            horizontal = predictedMotor.Velocity;
        Vector3 velocity = horizontal.Value;
        var info = new MovementEventInfo
        {
            x = position.x, y = position.y, z = position.z,
            yaw = Mathf.Repeat(transform.eulerAngles.y, 360f),
            clientTimeMs = now, flags = movementFlags, velocityX = velocity.x, velocityZ = velocity.z
        };
        if ((movementFlags & MovementEventFlags.Falling) != 0)
        {
            info.velocityY = predictedMotor.Velocity.y;
            info.fallTimeMs = now - airStartedAt;
            info.jumpSpeed = airSpeed;
        }

        if (!NetworkManager.Instance.SendMovementEvent(new MovementEventRequest { @event = kind, info = info }))
            return false;
        lastSent = info;
        if ((kind == "jump" || kind == "fall") && (rollReported || rollDirection != PlayerInputKeys.None))
        {
            rollReported = false;
            ResetRollInput();
        }
        if (kind == "turn_start" || kind == "turn_stop")
        {
            lastTurnEventAt = now;
            hasTurnEvent = true;
        }
        return true;
    }

    private static MovementEventFlags ReadDirectionFlags(PlayerInputFrame input, Vector3 direction)
    {
        if (direction == Vector3.zero) return MovementEventFlags.None;
        int z = (input.HasKey(PlayerInputKeys.W) ? 1 : 0) - (input.HasKey(PlayerInputKeys.S) ? 1 : 0)
            + (HasBothMouseButtons(input.Actions) ? 1 : 0);
        int x = (input.HasKey(PlayerInputKeys.D) ? 1 : 0) - (input.HasKey(PlayerInputKeys.A) ? 1 : 0);
        var forward = z > 0 ? MovementEventFlags.Forward : z < 0 ? MovementEventFlags.Backward : MovementEventFlags.None;
        var strafe = x > 0 ? MovementEventFlags.StrafeRight : x < 0 ? MovementEventFlags.StrafeLeft : MovementEventFlags.None;
        return forward | strafe;
    }

    private void OnDestroy()
    {
        if (effectStateSource != null) effectStateSource.Clear();
        var network = NetworkManager.Instance;
        if (network != null && network.FlushLocalMovement == FlushMovement)
            network.FlushLocalMovement = null;
        if (network != null && network.LocalPlayerActionBlocked == IsMeditationActionBlocked)
            network.LocalPlayerActionBlocked = null;
    }
}
