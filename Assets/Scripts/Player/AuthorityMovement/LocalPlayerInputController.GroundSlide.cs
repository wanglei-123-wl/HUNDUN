using Game.Client.MapTraversal;
using UnityEngine;

public sealed partial class LocalPlayerInputController
{
    private const float GroundSlideSpeedRatio = .5f;
    private const float GroundSlideProbeDistance = .32f;
    private const float GroundSlideInputAngle = 4f;
    private const long GroundSlideTurnIntervalMs = 64;
    private const long GroundSlideForwardProbeIntervalMs = 100;
    private static readonly float[] GroundSlideAngles = { 20f, 40f, 60f, 80f, 90f };

    private Vector3 groundSlideIntent;
    private int groundSlideSide;
    private bool groundSlideWantsRun;
    private long groundSlideLastTurnAt;
    private long groundSlideForwardProbeAt;

    private bool IsGroundSliding => groundSlideSide != 0;
    private MapTraversalQuery MovementMap => movementSession == null ? null : movementSession.CurrentMapTraversal;

    private bool CanSlideForInput(PlayerInputFrame input) =>
        movementControl?.mode == "normal" && predictedMotor.IsGrounded
        && (movementFlags & MovementEventFlags.Falling) == 0 && !input.JumpPressed
        && !IsMeditationBlockingInput() && rollDirection == PlayerInputKeys.None && !IsRollAnimationPlaying()
        && combatController?.HasCombatApproach != true
        && (HasDirectionalInput(input.Keys) || HasBothMouseButtons(input.Actions));

    private void ClearGroundSlide()
    {
        groundSlideIntent = Vector3.zero;
        groundSlideSide = 0;
    }

    private bool CanSlideAlong(MapTraversalQuery query, Vector3 direction)
    {
        Vector3 from = transform.position;
        Vector3 endpoint = from + direction * GroundSlideProbeDistance;
        return query != null && query.CanTraverseGroundSegment(from.x, from.y, from.z,
            endpoint.x, endpoint.z, movementControl.radius, movementControl.height, out _);
    }

    private bool TryChooseGroundSlide(MapTraversalQuery query, long now, out Vector3 direction,
        Vector3 blockNormal = default)
    {
        direction = Vector3.zero;
        if (groundSlideIntent == Vector3.zero || query == null
            || (IsGroundSliding && now - groundSlideLastTurnAt < GroundSlideTurnIntervalMs))
            return false;

        if (blockNormal != Vector3.zero)
        {
            Vector3 tangent = Vector3.Cross(Vector3.up, blockNormal);
            // At a real capsule contact, choose a direction that does not
            // push back into that face, even if the baked query has more skin room.
            float forward = Vector3.Dot(tangent, groundSlideIntent);
            if (forward >= -0.001f && TryUseGroundSlideDirection(query, tangent, now, out direction))
                return true;
            if (forward <= 0.001f && TryUseGroundSlideDirection(query, -tangent, now, out direction))
                return true;
        }

        foreach (float angle in GroundSlideAngles)
        {
            if (groundSlideSide >= 0 && TryAtAngle(angle, out direction)) return true;
            if (groundSlideSide <= 0 && TryAtAngle(-angle, out direction)) return true;
        }
        return false;

        bool TryAtAngle(float angle, out Vector3 selected)
        {
            Vector3 candidate = Quaternion.AngleAxis(angle, Vector3.up) * groundSlideIntent;
            selected = Vector3.zero;
            return Vector3.Dot(candidate, blockNormal) >= -.001f
                && TryUseGroundSlideDirection(query, candidate, now, out selected);
        }
    }

    private bool TryUseGroundSlideDirection(MapTraversalQuery query, Vector3 candidate, long now, out Vector3 direction)
    {
        direction = Vector3.zero;
        int side = Vector3.Cross(groundSlideIntent, candidate).y >= 0f ? 1 : -1;
        if ((IsGroundSliding && (groundSlideSide != side
                || Vector3.Angle(travelDirection, candidate) <= GroundSlideInputAngle))
            || !CanSlideAlong(query, candidate))
            return false;

        groundSlideSide = side;
        groundSlideLastTurnAt = now;
        groundSlideForwardProbeAt = now + GroundSlideForwardProbeIntervalMs;
        direction = candidate;
        return true;
    }

    private bool ResumeGroundSlideForward(MapTraversalQuery query, long now)
    {
        if (!IsGroundSliding || groundSlideIntent == Vector3.zero || now < groundSlideForwardProbeAt)
            return true;

        groundSlideForwardProbeAt = now + GroundSlideForwardProbeIntervalMs;
        if (!CanSlideAlong(query, groundSlideIntent)) return true;

        groundSlideSide = 0;
        return SendGroundSlideTurn(groundSlideIntent, groundSlideWantsRun, now);
    }

    private bool SendGroundSlideTurn(Vector3 direction, bool run, long now)
    {
        bool changeMode = ((movementFlags & MovementEventFlags.Walking) != 0) == run;
        if (changeMode) movementFlags ^= MovementEventFlags.Walking;
        SetTravelDirection(direction);
        // This position closes the previous straight segment before moving on
        // the new heading. A walk/run event can carry the same turn.
        return Report(changeMode ? (run ? "run" : "walk") : "facing", now);
    }
}
