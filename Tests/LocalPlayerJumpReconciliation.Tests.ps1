$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$base = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'LocalPlayerJumpPrediction.Tests.ps1') -Raw
$base = $base -replace '(?m)^\$root = Split-Path \$PSScriptRoot -Parent\r?\n', ''
Invoke-Expression $base.Substring(0, $base.IndexOf('Add-Type -TypeDefinition $sources'))
$reconciler = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/Player/AuthorityMovement/LocalPlayerReconciler.cs') -Raw
$tests = @'
public class LocalPlayerVisualSmoother
{
    public UnityEngine.Vector3 VisualWorldPosition;
    public UnityEngine.Quaternion VisualWorldRotation;
    public void ApplyCorrection(UnityEngine.Vector3 p, UnityEngine.Quaternion r, float distance) {}
    public void ResetVisual() {}
    public void StopJumpPresentation() {}
    public void BeginLogicStep() {}
    public void EndLogicStep() {}
}
public static class MovementControlTests
{
    public static void Run()
    {
        var motor = new PredictedCharacterMotor();
        motor.testComponent = new CharacterController { transform = motor.transform };
        typeof(PredictedCharacterMotor).GetMethod("Awake",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(motor, null);
        var owned = new LocalPlayerReconciler { transform = motor.transform };
        owned.Bind(motor, null);
        owned.ApplyControl(new MovementEventControl {
            grounded = false, velocityY = -4f, Position = new Vector3(3, 2, 5)
        }, false);
        if (motor.IsGrounded || motor.Velocity.y != -4f || motor.transform.position.y != 2)
            throw new Exception("Server recovery must override the airborne pose.");
        owned.ApplyAuthoritativeState(new PlayerAuthoritativeState {
            position = new Vector3(3, 1, 5), grounded = false, velocity = new Vector3(0, -6, 0)
        });
        if (motor.transform.position.y != 1 || motor.Velocity.y != -6f)
            throw new Exception("Server-owned states must advance recovery.");
        owned.ApplyControl(new MovementEventControl {
            grounded = true, Position = new Vector3(3, 0, 5)
        }, false);
        if (!motor.IsGrounded || motor.Velocity.y != 0 || motor.transform.position.y != 0)
            throw new Exception("A new control point ends recovery at the server position.");
    }
}
'@
$sources = "using System;`nusing UnityEngine;`n" + ((@($stubs, $inputFrame, $motor, $reconciler, $tests) -join "`n") -replace '(?m)^using (System|UnityEngine);[ \t]*\r?\n', '')
Add-Type -TypeDefinition $sources -IgnoreWarnings
[MovementControlTests]::Run()
Write-Output 'PASS: server-owned landing and recovery control points.'
