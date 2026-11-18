using System.Collections.Generic;
using UnityEngine;

public sealed class LocalPlayerReconciler : MonoBehaviour
{
    [SerializeField, Min(1)] private int maxPendingInputs = 60;

    private readonly List<PlayerInputFrame> pendingInputs = new List<PlayerInputFrame>();
    private PredictedCharacterMotor predictedMotor;
    private LocalPlayerVisualSmoother visualSmoother;
    private float fixedInputDeltaTime = 0.05f;
    private long lastProcessedSequence;
    private long activeJumpSequence;

    public int PendingInputCount => pendingInputs.Count;
    public long LastProcessedSequence => lastProcessedSequence;
    public bool IsJumpActive => activeJumpSequence != 0;

    public void Bind(
        PredictedCharacterMotor motor,
        LocalPlayerVisualSmoother smoother,
        float inputDeltaTime)
    {
        predictedMotor = motor;
        visualSmoother = smoother;
        fixedInputDeltaTime = Mathf.Max(0.001f, inputDeltaTime);
    }

    public void RecordPredictedInput(PlayerInputFrame input, bool jumpStarted = false)
    {
        if (input == null)
        {
            return;
        }

        pendingInputs.Add(input);
        if (jumpStarted)
        {
            activeJumpSequence = input.Sequence;
        }

        if (pendingInputs.Count > maxPendingInputs)
        {
            int removeCount = pendingInputs.Count - maxPendingInputs;
            pendingInputs.RemoveRange(0, removeCount);
            Debug.LogWarning("Local prediction input queue overflow. Oldest inputs were removed: " + removeCount);
        }
    }

    public void ApplyAuthoritativeState(PlayerAuthoritativeState state)
    {
        if (state == null || predictedMotor == null)
        {
            return;
        }

        if (state.lastProcessedInputSequence < lastProcessedSequence)
        {
            return;
        }

        Vector3 oldPredictedPosition = transform.position;
        PredictedCharacterMotor.PredictionState predictedBeforeCorrection =
            predictedMotor.CapturePredictionState();
        Vector3 oldVisualPosition = visualSmoother == null
            ? oldPredictedPosition
            : visualSmoother.VisualWorldPosition;
        Quaternion oldVisualRotation = visualSmoother == null
            ? transform.rotation
            : visualSmoother.VisualWorldRotation;

        lastProcessedSequence = state.lastProcessedInputSequence;
        RemoveAcknowledgedInputs(lastProcessedSequence);
        predictedMotor.SetAuthoritativeState(state);

        for (int index = 0; index < pendingInputs.Count; index++)
        {
            predictedMotor.Simulate(pendingInputs[index], fixedInputDeltaTime, true);
        }

        if (activeJumpSequence != 0)
        {
            // Only a completed local flight and a processed, grounded server state end the jump.
            bool settled = predictedBeforeCorrection.Grounded
                && lastProcessedSequence >= activeJumpSequence
                && state.grounded;
            if (!settled)
            {
                predictedMotor.PreservePredictedVertical(predictedBeforeCorrection);
            }
            else
            {
                activeJumpSequence = 0;
                visualSmoother?.StopJumpPresentation();
            }
        }

        float correctionDistance = Vector3.Distance(oldPredictedPosition, transform.position);
        visualSmoother?.ApplyCorrection(oldVisualPosition, oldVisualRotation, correctionDistance);
        predictedMotor.RefreshMovementAnimation();
    }

    public void ResetAtSpawn(Vector3 position, Quaternion rotation)
    {
        pendingInputs.Clear();
        lastProcessedSequence = 0;
        activeJumpSequence = 0;
        visualSmoother?.StopJumpPresentation();
        predictedMotor?.ResetAtSpawn(position, rotation);
        visualSmoother?.ResetVisual();
    }

    public void ClearPendingInputs()
    {
        pendingInputs.Clear();
        lastProcessedSequence = 0;
        activeJumpSequence = 0;
        visualSmoother?.StopJumpPresentation();
    }

    private void RemoveAcknowledgedInputs(long acknowledgedSequence)
    {
        int removeCount = 0;

        while (removeCount < pendingInputs.Count
            && pendingInputs[removeCount].Sequence <= acknowledgedSequence)
        {
            removeCount++;
        }

        if (removeCount > 0)
        {
            pendingInputs.RemoveRange(0, removeCount);
        }
    }

}
