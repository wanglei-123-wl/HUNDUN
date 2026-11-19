using Game.Client.MapTraversal;
using UnityEngine;

public sealed partial class PredictedCharacterMotor
{
    private const int TraversalSearchIterations = 10;

    // Grounded motion follows the server support sheet. Airborne motion sweeps
    // the same capsule, allowing jumps over obstacles without ignoring walls.
    internal static bool TryTraversalTarget(MapTraversalQuery query, Vector3 from, Vector3 requested,
        float radius, float height, bool onGround, out Vector3 target, out bool landed)
    {
        target = requested;
        landed = false;
        if (query == null) return false;
        if (onGround)
            return query.CanTraverseGroundSegment(from.x, from.y, from.z, requested.x, requested.z,
                radius, height, out _);

        if (!query.IsInsideBounds(from.x, from.z) || !query.IsInsideBounds(requested.x, requested.z)
            || !query.TryGetGroundHeight(requested.x, requested.z, out double groundY)) return false;
        if (requested.y <= from.y && query.TryGetStandingHeight(requested.x, requested.z, radius, height, out double standingY))
            groundY = standingY;
        if (requested.y <= groundY && requested.y <= from.y)
        {
            // A falling character cannot land on a sheet above its current feet.
            if (groundY > from.y + .001) return false;
            target.y = (float)groundY;
            landed = true;
        }
        return !query.HasCollisionAlongSegment(from.x, from.y, from.z, target.x, target.y, target.z,
            radius, height);
    }

    internal CollisionFlags MoveLocally(Vector3 displacement, MapTraversalQuery query,
        float radius, float height, bool onGround, out bool horizontalBlocked)
    {
        Vector3 from = transform.position;
        Vector3 requested = from + displacement;
        Vector3 target = requested;
        bool landed = false;
        bool allowed = TryTraversalTarget(query, from, requested, radius, height, onGround, out target, out landed);
        horizontalBlocked = false;
        CollisionFlags mapCollisions = CollisionFlags.None;

        if (!allowed)
        {
            horizontalBlocked = displacement.x != 0f || displacement.z != 0f;
            // Every accepted prefix is checked from the original position. Do
            // not shortcut across a corner or assume that endpoints are enough.
            target = from;
            if (onGround)
            {
                target.y = requested.y; // Keep the CharacterController ground probe.
                float low = 0f, high = 1f;
                for (int i = 0; query != null && horizontalBlocked && i < TraversalSearchIterations; i++)
                {
                    float middle = (low + high) * .5f;
                    Vector3 candidate = Vector3.LerpUnclamped(from, requested, middle);
                    candidate.y = requested.y;
                    if (TryTraversalTarget(query, from, candidate, radius, height, true, out _, out _))
                    { low = middle; target = candidate; }
                    else high = middle;
                }
            }
            else
            {
                // First preserve the jump/fall trajectory with a shorter
                // horizontal step. A side collision must not cancel gravity.
                Vector3 verticalTarget = new Vector3(from.x, requested.y, from.z);
                if (TryTraversalTarget(query, from, verticalTarget, radius, height, false, out target, out landed))
                {
                    float low = 0f, high = 1f;
                    for (int i = 0; horizontalBlocked && i < TraversalSearchIterations; i++)
                    {
                        float middle = (low + high) * .5f;
                        Vector3 candidate = Vector3.LerpUnclamped(verticalTarget, requested, middle);
                        if (TryTraversalTarget(query, from, candidate, radius, height, false,
                            out Vector3 accepted, out bool candidateLanded))
                        { low = middle; target = accepted; landed = candidateLanded; }
                        else high = middle;
                    }
                }
                else
                {
                    target = from;
                    landed = false;
                    float low = 0f, high = 1f;
                    for (int i = 0; query != null && i < TraversalSearchIterations; i++)
                    {
                        float middle = (low + high) * .5f;
                        Vector3 candidate = Vector3.LerpUnclamped(from, verticalTarget, middle);
                        if (TryTraversalTarget(query, from, candidate, radius, height, false,
                            out Vector3 accepted, out bool candidateLanded))
                        { low = middle; target = accepted; landed = candidateLanded; }
                        else high = middle;
                    }
                    if (query != null)
                        mapCollisions |= displacement.y > 0f ? CollisionFlags.Above : CollisionFlags.Below;
                }
            }
        }

        if (landed) mapCollisions |= CollisionFlags.Below;
        CollisionFlags collisions = controller.Move(target - from) | mapCollisions;
        Vector3 actual = transform.position;
        // Unity can slide away from the requested segment during overlap or
        // stair recovery. Validate that result before exposing/sending it.
        bool actualValid = TryTraversalTarget(query, from, actual, radius, height, onGround,
            out Vector3 checkedTarget, out _);
        if (!onGround && actualValid)
            actualValid = Mathf.Abs(checkedTarget.y - actual.y) < .001f;
        if (actual != from && !actualValid)
        {
            SetTransform(from, transform.rotation);
            horizontalBlocked |= displacement.x != 0f || displacement.z != 0f;
            collisions = onGround || displacement.y < 0f ? CollisionFlags.Below
                : displacement.y > 0f ? CollisionFlags.Above : CollisionFlags.None;
        }
        return collisions;
    }
}
