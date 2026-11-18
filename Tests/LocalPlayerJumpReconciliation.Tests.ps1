$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$base = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'LocalPlayerJumpPrediction.Tests.ps1') -Raw
$base = $base -replace '(?m)^\$root = Split-Path \$PSScriptRoot -Parent\r?\n', ''
Invoke-Expression $base.Substring(0, $base.IndexOf('Add-Type -TypeDefinition $sources'))
$reconciler = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/Player/AuthorityMovement/LocalPlayerReconciler.cs') -Raw
$visualStub = @'
public class LocalPlayerVisualSmoother
{
    public UnityEngine.Vector3 VisualWorldPosition;
    public UnityEngine.Quaternion VisualWorldRotation;
    public void ApplyCorrection(UnityEngine.Vector3 position, UnityEngine.Quaternion rotation,
        float correctionDistance) {}
    public void StopJumpPresentation() {}
    public void ResetVisual() {}
}
'@
$tests = @'
using System;
using UnityEngine;
public static class JumpReconciliationTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
    private static PredictedCharacterMotor NewMotor()
    {
        var motor = new PredictedCharacterMotor();
        motor.testComponent = new CharacterController { transform = motor.transform };
        typeof(PredictedCharacterMotor).GetMethod("Awake",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .Invoke(motor, null);
        motor.ResetAtSpawn(Vector3.zero, Quaternion.identity);
        return motor;
    }
    private static LocalPlayerReconciler NewReconciler(PredictedCharacterMotor motor)
    {
        var reconciler = new LocalPlayerReconciler();
        reconciler.transform = motor.transform;
        reconciler.Bind(motor, null, 0.05f);
        return reconciler;
    }
    public static void Run()
    {
        var motor = NewMotor();
        var reconciler = NewReconciler(motor);
        var jump = new PlayerInputFrame { Sequence = 1, JumpPressed = true };
        motor.Simulate(jump, 0.05f, true);
        reconciler.RecordPredictedInput(jump, true);
        float predictedHeight = motor.transform.position.y;

        var staleAirborne = new PlayerAuthoritativeState {
            position = new Vector3(0.1f, 0, 0), velocity = Vector3.zero, grounded = false,
            lastProcessedInputSequence = 0 };
        reconciler.ApplyAuthoritativeState(staleAirborne);
        Check(Math.Abs(motor.transform.position.y - predictedHeight) < 0.001f
            && motor.Velocity.y > 0f
            && Math.Abs(motor.transform.position.x - 0.1f) < 0.001f,
            "Pre-jump response must not erase the launched flight, even if replay cannot jump.");

        var next = new PlayerInputFrame { Sequence = 2 };
        motor.Simulate(next, 0.05f, true);
        reconciler.RecordPredictedInput(next);
        float secondHeight = motor.transform.position.y;
        reconciler.ApplyAuthoritativeState(staleAirborne);
        Check(Math.Abs(motor.transform.position.y - secondHeight) < 0.001f,
            "Consecutive pre-jump responses must not rewind the vertical trajectory.");

        var accepted = new PlayerAuthoritativeState {
            position = new Vector3(0, predictedHeight, 0),
            velocity = new Vector3(0, 6.1f, 0), grounded = false,
            lastProcessedInputSequence = 1 };
        reconciler.ApplyAuthoritativeState(accepted);
        Check(Math.Abs(motor.transform.position.y - secondHeight) < 0.001f,
            "Acknowledged airborne jump should retain the ongoing predicted arc.");

        var landed = new PlayerAuthoritativeState {
            position = Vector3.zero, velocity = Vector3.zero, grounded = true,
            lastProcessedInputSequence = 2 };
        reconciler.ApplyAuthoritativeState(landed);
        Check(!motor.IsGrounded && motor.transform.position.y == secondHeight,
            "Early grounded authority must not interrupt the local jump.");
        for (int i = 0; i < 40 && !motor.IsGrounded; i++)
        {
            motor.Simulate(new PlayerInputFrame { Sequence = i + 3 }, 0.05f, true);
        }
        Check(motor.IsGrounded && reconciler.IsJumpActive,
            "Local landing must finish even if authority has not settled this jump.");
        reconciler.ApplyAuthoritativeState(accepted);
        Check(motor.IsGrounded && reconciler.IsJumpActive,
            "Late airborne authority must not restart or settle a completed local jump.");
        reconciler.ApplyAuthoritativeState(landed);
        Check(motor.IsGrounded && motor.transform.position.y == 0f && !reconciler.IsJumpActive,
            "Authoritative grounded state after local landing must settle the jump.");

        var rejectedMotor = NewMotor();
        var rejected = NewReconciler(rejectedMotor);
        rejectedMotor.Simulate(jump, 0.05f, true);
        rejected.RecordPredictedInput(jump, true);
        rejected.ApplyAuthoritativeState(new PlayerAuthoritativeState {
            position = Vector3.zero, velocity = Vector3.zero, grounded = true,
            lastProcessedInputSequence = 1 });
        Check(!rejectedMotor.IsGrounded && rejectedMotor.transform.position.y > 0f,
            "A grounded reply cannot prove denial while the local jump is still in flight.");
        for (int i = 0; i < 40 && !rejectedMotor.IsGrounded; i++)
        {
            rejectedMotor.Simulate(new PlayerInputFrame { Sequence = i + 2 }, 0.05f, true);
        }
        rejected.ApplyAuthoritativeState(new PlayerAuthoritativeState {
            position = Vector3.zero, velocity = Vector3.zero, grounded = true,
            lastProcessedInputSequence = 1 });
        Check(rejectedMotor.IsGrounded && rejectedMotor.transform.position.y == 0f,
            "Server position must take over once the local flight is complete.");

        var divergentMotor = NewMotor();
        var divergent = NewReconciler(divergentMotor);
        divergentMotor.Simulate(jump, 0.05f, true);
        divergent.RecordPredictedInput(jump, true);
        for (int i = 0; i < 2; i++)
        {
            divergentMotor.Simulate(new PlayerInputFrame { Sequence = i + 2 }, 0.05f, true);
        }
        float airborneHeight = divergentMotor.transform.position.y;
        divergent.ApplyAuthoritativeState(new PlayerAuthoritativeState {
            position = new Vector3(0, 0.1f, 0),
            velocity = new Vector3(0, 0, 0), grounded = false,
            lastProcessedInputSequence = 3 });
        Check(Math.Abs(airborneHeight - divergentMotor.transform.position.y) < 0.001f,
            "An airborne response must not cut short the local jump because of height divergence.");

        var delayedMotor = NewMotor();
        var delayed = NewReconciler(delayedMotor);
        delayedMotor.Simulate(jump, 0.05f, true);
        delayed.RecordPredictedInput(jump, true);
        for (int i = 0; i < 40 && !delayedMotor.IsGrounded; i++)
        {
            delayedMotor.Simulate(new PlayerInputFrame { Sequence = i + 2 }, 0.05f, true);
        }
        Check(delayedMotor.IsGrounded, "The client must finish the predicted flight locally.");
        delayed.ApplyAuthoritativeState(staleAirborne);
        Check(delayedMotor.IsGrounded && delayedMotor.transform.position.y == 0f,
            "Pre-jump response after local landing must not launch the same jump again.");
        delayed.ApplyAuthoritativeState(new PlayerAuthoritativeState {
            position = new Vector3(0.1f, predictedHeight, 0),
            velocity = new Vector3(0, 6.1f, 0), grounded = false,
            lastProcessedInputSequence = 1 });
        Check(delayedMotor.IsGrounded && delayedMotor.transform.position.y == 0f,
            "Late airborne acknowledgement must not restart an already completed local jump.");
        delayed.ApplyAuthoritativeState(landed);
        Check(delayedMotor.IsGrounded && delayedMotor.transform.position.y == 0f,
            "Late authoritative landing should complete the flight without bouncing.");

        var farMotor = NewMotor();
        var far = NewReconciler(farMotor);
        farMotor.Simulate(jump, 0.05f, true);
        far.RecordPredictedInput(jump, true);
        far.ApplyAuthoritativeState(new PlayerAuthoritativeState {
            position = new Vector3(10, 0, 0), velocity = Vector3.zero,
            grounded = true, lastProcessedInputSequence = 0 });
        Check(farMotor.transform.position.x == 10 && farMotor.transform.position.y > 0f,
            "Horizontal authority remains in force while local vertical flight completes.");

        var resetMotor = NewMotor();
        var reset = NewReconciler(resetMotor);
        resetMotor.Simulate(jump, 0.05f, true);
        reset.RecordPredictedInput(jump, true);
        reset.ClearPendingInputs();
        reset.ApplyAuthoritativeState(landed);
        Check(resetMotor.transform.position.y == 0f,
            "Disconnect or death must clear the jump protection.");

        var walkingMotor = NewMotor();
        var walking = NewReconciler(walkingMotor);
        walking.ApplyAuthoritativeState(new PlayerAuthoritativeState {
            position = new Vector3(1, 0.5f, 0), velocity = Vector3.zero,
            grounded = false, lastProcessedInputSequence = 0 });
        Check(walkingMotor.transform.position.x == 1f
            && walkingMotor.transform.position.y == 0.5f,
            "Normal reconciliation must remain unchanged outside a jump.");
    }
}
'@
$sources = "using System;`nusing UnityEngine;`n" `
    + ((@($stubs, $inputFrame, $motor, $visualStub, $reconciler, $tests) -join "`n") `
        -replace '(?m)^using (System|UnityEngine|System.Collections.Generic);[ \t]*\r?\n', '')
$sources = "using System.Collections.Generic;`n" + $sources
Add-Type -TypeDefinition $sources -IgnoreWarnings
[JumpReconciliationTests]::Run()
Write-Output 'Local player jump reconciliation tests passed.'
