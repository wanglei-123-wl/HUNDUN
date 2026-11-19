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
    public void BeginLogicStep() {}
    public void EndLogicStep() {}
}
'@
$tests = @'
using System;
using UnityEngine;
public static class LocalPlayerMovementAnimationTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static PlayerAuthoritativeState State(long sequence, float speed, int actionState = 0)
    {
        return new PlayerAuthoritativeState {
            position = Vector3.zero, velocity = new Vector3(0f, 0f, speed),
            grounded = true, lastProcessedInputSequence = sequence,
            movementState = speed > 0f ? 1 : 0, actionState = actionState
        };
    }

    public static void Run()
    {
        var motor = new PredictedCharacterMotor();
        motor.testComponent = new CharacterController { transform = motor.transform };
        typeof(PredictedCharacterMotor).GetMethod("Awake",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .Invoke(motor, null);
        var animator = new Animator {
            parameters = new[] {
                new AnimatorControllerParameter { name = "IsMoving", type = AnimatorControllerParameterType.Bool },
                new AnimatorControllerParameter { name = "Attack", type = AnimatorControllerParameterType.Trigger }
            }
        };
        motor.BindAnimator(animator);
        motor.ResetAtSpawn(Vector3.zero, Quaternion.identity);
        var reconciler = new LocalPlayerReconciler { transform = motor.transform };
        reconciler.Bind(motor, null);
        motor.SetLocalMotion(new Vector3(0, 0, 1), 0, true);
        motor.PresentPredictedInput(new PlayerInputFrame { Keys = PlayerInputKeys.W });
        Check(animator.IsMoving, "Local walk should animate immediately.");
        motor.SetLocalMotion(Vector3.zero, 0, true);
        motor.PresentPredictedInput(new PlayerInputFrame());
        Check(!animator.IsMoving, "Releasing movement must stop the animation.");
        reconciler.ApplyAuthoritativeState(State(1, 1f, 1));
        Check(!animator.IsMoving, "A server-owned attack lock suppresses walking.");
        reconciler.ApplyAuthoritativeState(State(2, 1f));
        Check(animator.IsMoving, "Server-owned movement can animate after the lock.");
        var attackInput = new PlayerInputFrame { Actions = PlayerInputActions.PrimaryAction };
        motor.PresentPredictedInput(attackInput);
        Check(animator.AttackTriggered && !animator.IsMoving,
            "Locally predicted attack must trigger attack and suppress walking.");
        motor.SetAuthoritativeState(State(5, 0f));
        Check(motor.BlocksMovementForAttack(),
            "An idle server snapshot must preserve the lock while the attack trigger is pending.");

        var attackState = new AnimatorStateInfo { shortNameHash = Animator.StringToHash("Attack") };
        Time.unscaledTime = 0.1f;
        animator.NextState = attackState;
        animator.InTransition = true;
        Check(motor.BlocksMovementForAttack(), "Movement must be blocked during the transition into attack.");

        Time.unscaledTime = 1f;
        animator.CurrentState = attackState;
        animator.InTransition = false;
        Check(motor.BlocksMovementForAttack(), "Movement must be blocked for the entire attack animation.");

        animator.CurrentState = default;
        Check(motor.BlocksMovementForAttack(), "Ending the attack must start the movement recovery.");
        motor.SetAuthoritativeState(State(5, 0f));
        Time.unscaledTime = 1.29f;
        Check(motor.BlocksMovementForAttack(), "Movement must stay blocked during the recovery.");
        Time.unscaledTime = 1.3f;
        Check(!motor.BlocksMovementForAttack(), "Movement must resume after the recovery elapses.");
        Check(motor.BlocksMovementForAttack(PlayerInputActions.PrimaryAction),
            "A frame starting another attack must already block movement.");
    }
}
'@
$sources = "using System;`nusing UnityEngine;`n" `
    + ((@($stubs, $inputFrame, $motor, $visualStub, $reconciler, $tests) -join "`n") `
        -replace '(?m)^using (System|UnityEngine|System.Collections.Generic);[ \t]*\r?\n', '')
$sources = "using System.Collections.Generic;`n" + $sources
Add-Type -TypeDefinition $sources -IgnoreWarnings
[LocalPlayerMovementAnimationTests]::Run()
Write-Output 'Local player movement animation tests passed.'
