using UnityEngine;

[DefaultExecutionOrder(-50)]
public sealed class LocalPlayerVisualSmoother : MonoBehaviour
{
    [SerializeField, Min(0.01f)] private float positionSmoothTime = 0.12f;
    [SerializeField, Min(0.01f)] private float rotationSmoothTime = 0.1f;
    [SerializeField, Min(0f)] private float hardSnapDistance = 2f;

    private Transform logicRoot;
    private Transform visualRoot;
    private Quaternion? combatFacing;
    private Vector3 localPositionVelocity;
    private Vector3 capturedLogicWorldPosition;
    private Vector3 capturedVisualWorldPosition;
    private Quaternion capturedVisualWorldRotation;
    private bool hasCapturedLogicStep;
    private bool jumpPresentationActive;
    private bool jumpGrounded;
    private float jumpVelocityY;
    private float jumpGravity;
    private float jumpStepDuration;
    private float jumpStepTime;

    public Vector3 VisualWorldPosition => visualRoot == null ? transform.position : visualRoot.position;
    public Quaternion VisualWorldRotation => visualRoot == null ? transform.rotation : visualRoot.rotation;

    private void LateUpdate()
    {
        if (visualRoot == null || visualRoot == logicRoot)
        {
            return;
        }

        if (jumpPresentationActive && logicRoot != null)
        {
            Vector3 horizontalOffset = visualRoot.localPosition;
            horizontalOffset.y = 0f;
            localPositionVelocity.y = 0f;
            horizontalOffset = Vector3.SmoothDamp(
                horizontalOffset, Vector3.zero, ref localPositionVelocity, positionSmoothTime);

            float elapsed = Mathf.Clamp(Time.unscaledTime - jumpStepTime, 0f, jumpStepDuration);
            float visualY = logicRoot.position.y;
            if (!jumpGrounded)
            {
                visualY += jumpVelocityY * elapsed + jumpGravity * elapsed * elapsed;
            }

            horizontalOffset.y = visualY - logicRoot.position.y;
            visualRoot.localPosition = horizontalOffset;
        }
        else
        {
            visualRoot.localPosition = Vector3.SmoothDamp(
                visualRoot.localPosition,
                Vector3.zero,
                ref localPositionVelocity,
                positionSmoothTime
            );
        }

        float rotationBlend = 1f - Mathf.Exp(
            -3f * Time.unscaledDeltaTime / Mathf.Max(0.01f, rotationSmoothTime));
        if (combatFacing.HasValue)
        {
            visualRoot.rotation = Quaternion.Slerp(visualRoot.rotation, combatFacing.Value, rotationBlend);
        }
        else
        {
            visualRoot.localRotation = Quaternion.Slerp(visualRoot.localRotation, Quaternion.identity, rotationBlend);
        }
    }

    internal void FaceCombatTargetImmediately(float yaw)
    {
        if (visualRoot == null || visualRoot == logicRoot) return;

        combatFacing = Quaternion.Euler(0f, yaw, 0f);
        visualRoot.rotation = combatFacing.Value;
    }

    internal void ClearCombatFacing()
    {
        combatFacing = null;
    }

    public void Bind(Transform newLogicRoot, Transform newVisualRoot)
    {
        logicRoot = newLogicRoot;
        visualRoot = newVisualRoot;
        combatFacing = null;
        localPositionVelocity = Vector3.zero;
        hasCapturedLogicStep = false;
        jumpPresentationActive = false;
    }

    public void UpdateJumpPresentation(
        float verticalVelocity,
        float gravity,
        float stepDuration,
        bool grounded)
    {
        jumpPresentationActive = true;
        jumpGrounded = grounded;
        jumpVelocityY = verticalVelocity;
        jumpGravity = gravity;
        jumpStepDuration = Mathf.Max(0f, stepDuration);
        jumpStepTime = Time.unscaledTime;
    }

    public void StopJumpPresentation()
    {
        jumpPresentationActive = false;
        localPositionVelocity.y = 0f;
    }

    public void BeginLogicStep()
    {
        if (visualRoot == null || visualRoot == logicRoot || logicRoot == null)
        {
            hasCapturedLogicStep = false;
            return;
        }

        capturedLogicWorldPosition = logicRoot.position;
        capturedVisualWorldPosition = visualRoot.position;
        capturedVisualWorldRotation = visualRoot.rotation;
        hasCapturedLogicStep = true;
    }

    public void EndLogicStep()
    {
        if (!hasCapturedLogicStep)
        {
            return;
        }

        hasCapturedLogicStep = false;

        if (visualRoot == null || visualRoot == logicRoot || logicRoot == null)
        {
            return;
        }

        float movementDistance = Vector3.Distance(capturedLogicWorldPosition, logicRoot.position);
        if (hardSnapDistance > 0f && movementDistance >= hardSnapDistance)
        {
            ResetVisual();
            return;
        }

        visualRoot.SetPositionAndRotation(capturedVisualWorldPosition, capturedVisualWorldRotation);
    }

    public void ApplyCorrection(
        Vector3 previousVisualWorldPosition,
        Quaternion previousVisualWorldRotation,
        float correctionDistance)
    {
        if (visualRoot == null || visualRoot == logicRoot)
        {
            return;
        }

        if (hardSnapDistance > 0f && correctionDistance >= hardSnapDistance)
        {
            bool keepJumpPresentation = jumpPresentationActive;
            ResetVisual();
            jumpPresentationActive = keepJumpPresentation;
            return;
        }

        visualRoot.SetPositionAndRotation(previousVisualWorldPosition, previousVisualWorldRotation);
    }

    public void ResetVisual()
    {
        localPositionVelocity = Vector3.zero;
        hasCapturedLogicStep = false;
        jumpPresentationActive = false;

        if (visualRoot == null || visualRoot == logicRoot)
        {
            return;
        }

        visualRoot.localPosition = Vector3.zero;
        visualRoot.localRotation = Quaternion.identity;
        if (combatFacing.HasValue)
            visualRoot.rotation = combatFacing.Value;
    }
}
