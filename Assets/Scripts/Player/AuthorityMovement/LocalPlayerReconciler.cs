using UnityEngine;

// Applies the server-owned movement segment and visual correction at control handoff.
public sealed class LocalPlayerReconciler : MonoBehaviour
{
    private PredictedCharacterMotor predictedMotor;
    private LocalPlayerVisualSmoother visualSmoother;

    public void Bind(PredictedCharacterMotor motor, LocalPlayerVisualSmoother smoother)
    {
        predictedMotor = motor;
        visualSmoother = smoother;
    }

    public void ApplyControl(MovementEventControl control, bool smooth)
    {
        Vector3 oldPosition = transform.position;
        Vector3 oldVisual = visualSmoother == null ? oldPosition : visualSmoother.VisualWorldPosition;
        Quaternion oldRotation = visualSmoother == null ? transform.rotation : visualSmoother.VisualWorldRotation;
        visualSmoother?.StopJumpPresentation();
        predictedMotor.ApplyMovementControl(control);
        if (smooth)
            visualSmoother?.ApplyCorrection(oldVisual, oldRotation, Vector3.Distance(oldPosition, transform.position));
        else
            visualSmoother?.ResetVisual();
    }

    public void ApplyAuthoritativeState(PlayerAuthoritativeState state)
    {
        visualSmoother?.BeginLogicStep();
        predictedMotor?.SetAuthoritativeState(state);
        visualSmoother?.EndLogicStep();
        predictedMotor?.RefreshMovementAnimation();
    }

    public void ResetAtSpawn(Vector3 position, Quaternion rotation)
    {
        visualSmoother?.StopJumpPresentation();
        predictedMotor?.ResetAtSpawn(position, rotation);
        visualSmoother?.ResetVisual();
    }
}
