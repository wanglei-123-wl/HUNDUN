using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Runs after reconciliation smoothing, without moving the logical player or
// changing the camera's follow target. The model keeps its authored scale.
[DefaultExecutionOrder(-40)]
[DisallowMultipleComponent]
public sealed class LocalPlayerVisualGrounding : MonoBehaviour
{
    [SerializeField, Min(.05f)] private float surfaceRange = .5f;
    [SerializeField, Min(.01f)] private float smoothTime = .08f;
    [SerializeField, Range(0f, 75f)] private float maxGroundSlope = 55f;
    [SerializeField] private LayerMask groundLayers = 1;

    private readonly RaycastHit[] hits = new RaycastHit[32];
    private GameSession session;
    private PredictedCharacterMotor motor;
    private GameObject model;
    private Animator animator;
    private Transform pivot;
    private Transform leftFoot, rightFoot;
    private float leftSoleHeight, rightSoleHeight;
    private Vector3 neutralSole;
    private float worldOffset, offsetVelocity;
    private bool calibrated, offsetInitialized;

    internal Transform PresentationRoot => pivot;

    internal void Bind(GameSession owner, PredictedCharacterMotor characterMotor,
        Transform smoothedRoot, GameObject visual, Animator visualAnimator)
    {
        session = owner;
        motor = characterMotor;
        if (smoothedRoot == null || visual == null) return;
        if (pivot == null)
        {
            pivot = new GameObject("LocalCharacterGroundOffset").transform;
            pivot.SetParent(smoothedRoot, false);
        }
        else if (pivot.parent != smoothedRoot)
            pivot.SetParent(smoothedRoot, false);

        bool changed = model != visual || animator != visualAnimator;
        model = visual;
        animator = visualAnimator;
        if (model.transform.parent != pivot) model.transform.SetParent(pivot, false);
        if (!changed) return;
        pivot.localPosition = Vector3.zero;
        worldOffset = offsetVelocity = 0f;
        calibrated = offsetInitialized = false;
    }

    private void LateUpdate()
    {
        if (session == null || !session.IsWorldReady || session.IsLocalPlayerDead
            || motor == null || model == null || pivot == null || pivot.parent == null)
            return;
        if (!calibrated)
        {
            // Calibrate a supported standing pose, not a running stride or a
            // jump/roll/death pose whose feet are intentionally off the floor.
            Vector3 velocity = motor.Velocity;
            if (!motor.IsGrounded || velocity.x * velocity.x + velocity.z * velocity.z > .0025f
                || animator == null || !animator.isActiveAndEnabled || !animator.isInitialized
                || !animator.isHuman || animator.avatar == null || !animator.avatar.isValid
                || animator.IsInTransition(0) || !animator.GetCurrentAnimatorStateInfo(0).IsTag("Locomotion"))
                return;
            if (!TryCalibrateSole()) return;
        }

        float soleHeight = pivot.TransformVector(neutralSole).y;
        float desiredOffset = -soleHeight;
        if (motor.IsGrounded && TryGroundHeight(out float groundY))
        {
            float unshiftedSoleY = pivot.parent.position.y + soleHeight;
            Vector3 motion = motor.Velocity;
            if (leftFoot != null && rightFoot != null && motion.x * motion.x + motion.z * motion.z <= .0025f
                && animator != null && animator.isActiveAndEnabled && !animator.IsInTransition(0)
                && animator.GetCurrentAnimatorStateInfo(0).IsTag("Locomotion"))
                // Cancel only the previously applied display translation. The
                // remaining foot motion includes idle-pose retargeting drift.
                unshiftedSoleY = Mathf.Min(leftFoot.position.y - leftSoleHeight,
                    rightFoot.position.y - rightSoleHeight) - worldOffset;
            desiredOffset = Mathf.Clamp(groundY - unshiftedSoleY,
                -soleHeight - surfaceRange, -soleHeight + surfaceRange);
        }
        if (!offsetInitialized)
        {
            worldOffset = desiredOffset;
            offsetInitialized = true;
        }
        else
            worldOffset = Mathf.SmoothDamp(worldOffset, desiredOffset, ref offsetVelocity,
                smoothTime, Mathf.Infinity, Time.unscaledDeltaTime);
        pivot.localPosition = pivot.parent.InverseTransformVector(Vector3.up * worldOffset);
    }

    private bool TryCalibrateSole()
    {
        Transform left = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        Transform right = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        if (left == null || right == null) return false;
        SkinnedMeshRenderer[] renderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(false);
        bool footwearAvailable = false;
        foreach (var skin in renderers)
            footwearAvailable |= skin.enabled && skin.sharedMesh != null
                && IsFootwear(skin.name) && HasFootBones(skin, left, right);

        float radius = Mathf.Max(.1f, .35f * Mathf.Abs(model.transform.lossyScale.y));
        float bottom = float.PositiveInfinity;
        Mesh scratch = null;
        var vertices = new List<Vector3>();
        try
        {
            foreach (var skin in renderers)
            {
                if (!skin.enabled || skin.sharedMesh == null || !HasFootBones(skin, left, right)
                    || (footwearAvailable && !IsFootwear(skin.name))) continue;
                if (scratch == null) scratch = new Mesh();
                // Bake once per model, with scale applied exactly once by
                // TransformPoint. Imported meshes need not be made readable.
                skin.BakeMesh(scratch, false);
                vertices.Clear();
                scratch.GetVertices(vertices);
                foreach (Vector3 vertex in vertices)
                {
                    Vector3 world = skin.transform.TransformPoint(vertex);
                    if (NearFoot(world, left.position, radius) || NearFoot(world, right.position, radius))
                        bottom = Mathf.Min(bottom, world.y);
                }
            }
        }
        catch (UnityException error)
        {
            Debug.LogWarning("[VisualGrounding] Foot mesh calibration unavailable: " + error.Message);
        }
        finally { if (scratch != null) Destroy(scratch); }

        bool meshMeasured = !float.IsInfinity(bottom) && !float.IsNaN(bottom);
        if (!meshMeasured)
        {
            // Avatar foot-bottom heights are a fallback for characters without
            // a separately measurable skinned foot/boot mesh.
            bottom = Mathf.Min(left.position.y - animator.leftFeetBottomHeight,
                right.position.y - animator.rightFeetBottomHeight);
        }
        if (float.IsNaN(bottom) || float.IsInfinity(bottom)) return false;
        Vector3 sole = pivot.position;
        sole.y = bottom;
        neutralSole = pivot.InverseTransformPoint(sole);
        leftFoot = left;
        rightFoot = right;
        leftSoleHeight = left.position.y - bottom;
        rightSoleHeight = right.position.y - bottom;
        calibrated = true;
        Debug.Log("[VisualGrounding] calibrated model=" + model.name
            + " soleLocalY=" + neutralSole.y + " meshMeasured=" + meshMeasured);
        return true;
    }

    private static bool NearFoot(Vector3 point, Vector3 foot, float radius)
    {
        float dx = point.x - foot.x, dz = point.z - foot.z;
        return dx * dx + dz * dz <= radius * radius && point.y <= foot.y + .02f
            && point.y >= foot.y - radius * 1.5f;
    }

    private static bool HasFootBones(SkinnedMeshRenderer skin, Transform left, Transform right)
    {
        foreach (Transform bone in skin.bones)
            if (bone != null && (bone == left || bone == right || bone.IsChildOf(left) || bone.IsChildOf(right)))
                return true;
        return false;
    }

    private static bool IsFootwear(string name) => name.IndexOf("shoe", StringComparison.OrdinalIgnoreCase) >= 0
        || name.IndexOf("boot", StringComparison.OrdinalIgnoreCase) >= 0
        || name.IndexOf("foot", StringComparison.OrdinalIgnoreCase) >= 0
        || name.Contains("鞋") || name.Contains("靴");

    private bool TryGroundHeight(out float height)
    {
        height = 0f;
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded) return false;
        Vector3 expected = transform.position;
        int count = scene.GetPhysicsScene().Raycast(expected + Vector3.up * surfaceRange, Vector3.down,
            hits, surfaceRange * 2f, groundLayers, QueryTriggerInteraction.Ignore);
        float nearest = float.PositiveInfinity;
        float minNormalY = Mathf.Cos(maxGroundSlope * Mathf.Deg2Rad);
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = hits[i];
            Collider collider = hit.collider;
            if (collider == null || collider.gameObject.scene != scene || hit.normal.y < minNormalY
                || collider is CharacterController || collider.transform.IsChildOf(transform)
                || collider.GetComponentInParent<RemotePlayerSync>() != null
                || collider.GetComponentInParent<MonsterNetworkView>() != null
                || collider.GetComponentInParent<VillageTeleporter>() != null
                || !GroundRingSurfaceFilter.Allows(collider)) continue;
            float distance = Mathf.Abs(hit.point.y - expected.y);
            if (distance >= nearest) continue;
            nearest = distance;
            height = hit.point.y;
            found = true;
        }
        return found;
    }
}
