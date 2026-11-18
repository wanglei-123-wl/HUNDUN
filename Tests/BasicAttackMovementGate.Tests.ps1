$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$base = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'LocalPlayerJumpPrediction.Tests.ps1') -Raw
$base = $base -replace '(?m)^\$root = Split-Path \$PSScriptRoot -Parent\r?\n', ''
Invoke-Expression $base.Substring(0, $base.IndexOf('Add-Type -TypeDefinition $sources'))

$gate = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/Player/AuthorityMovement/BasicAttackMovementGate.cs') -Raw
$controller = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/Player/AuthorityMovement/LocalPlayerInputController.cs') -Raw
$methods = [regex]::Match($controller,
    '(?s)    private static bool HasDirectionalInput\(.*?(?=\r?\n\})')
if (-not $methods.Success -or
    $controller.IndexOf('basicAttackMovementGate.Observe(', $controller.IndexOf('private PlayerInputFrame CreateInputFrame()')) -lt 0 -or
    $controller.IndexOf('basicAttackMovementGate.CanAttack(Time.unscaledTime)', $controller.IndexOf('private PlayerInputFrame CreateInputFrame()')) -lt 0 -or
    $controller.IndexOf('basicAttackMovementGate.CanAttack(Time.unscaledTime)') -lt
        $controller.IndexOf('if (!uiBlocked && trackingMouseLeftClick && Input.GetMouseButtonUp(0))')) {
    throw 'Attack gate must reject clicks at release and before creating the predicted input.'
}

$tests = @'
using System;
using UnityEngine;
public sealed class AttackInputHarness
{
    private PredictedCharacterMotor predictedMotor;
    private const float MovingHorizontalSpeedSquared = 0.0025f;
    public AttackInputHarness(PredictedCharacterMotor motor) { predictedMotor = motor; }
    public bool Moving(PlayerInputKeys keys, PlayerInputActions actions)
    { return IsMovingForBasicAttack(keys, actions); }
__METHODS__
}
public static class BasicAttackMovementGateTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    public static void Run()
    {
        var gate = new BasicAttackMovementGate();
        Check(gate.CanAttack(0f), "Spawned idle player should be able to attack.");
        gate.Observe(true, 1f);
        Check(!gate.CanAttack(10f), "Continued movement must keep attacks blocked.");
        gate.Observe(false, 10f);
        Check(!gate.CanAttack(10.19f), "Stopping for less than 0.2 seconds is insufficient.");
        Check(gate.CanAttack(10.21f), "Standing still for 0.2 seconds must allow a new click.");
        gate.Observe(true, 11f);
        gate.Observe(false, 11.05f);
        Check(!gate.CanAttack(11.2f), "Starting movement again must reset the stop delay.");
        gate.Reset();
        Check(gate.CanAttack(12f), "Respawn/input reset must not retain the old movement delay.");

        var motor = new PredictedCharacterMotor();
        motor.testComponent = new CharacterController { transform = motor.transform };
        typeof(PredictedCharacterMotor).GetMethod("Awake",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .Invoke(motor, null);
        motor.ResetAtSpawn(Vector3.zero, Quaternion.identity);
        var input = new AttackInputHarness(motor);
        Check(input.Moving(PlayerInputKeys.W, PlayerInputActions.None)
            && input.Moving(PlayerInputKeys.A, PlayerInputActions.None)
            && input.Moving(PlayerInputKeys.S, PlayerInputActions.None)
            && input.Moving(PlayerInputKeys.D, PlayerInputActions.None),
            "Any directional key must block basic attack.");
        Check(input.Moving(PlayerInputKeys.None,
                PlayerInputActions.MouseLeftHeld | PlayerInputActions.MouseRightHeld),
            "Both mouse buttons used for movement must block basic attack.");
        Check(!input.Moving(PlayerInputKeys.Shift, PlayerInputActions.MouseRightHeld),
            "Shift or right mouse button alone must not count as movement.");

        var walk = new PlayerInputFrame { Keys = PlayerInputKeys.W };
        for (int i = 0; i < 3; i++) motor.Simulate(walk, 0.05f, true);
        var idle = new PlayerInputFrame();
        motor.Simulate(idle, 0.05f, true);
        Check(input.Moving(PlayerInputKeys.None, PlayerInputActions.None),
            "Releasing the key must not allow attacking while the motor is decelerating.");
        gate.Observe(true, 20f);
        motor.Simulate(idle, 0.05f, true);
        Check(!input.Moving(PlayerInputKeys.None, PlayerInputActions.None),
            "The movement gate should start its timer only after horizontal velocity stops.");
        gate.Observe(false, 20.05f);
        Check(!gate.CanAttack(20.24f) && gate.CanAttack(20.26f),
            "The 0.2 second delay must begin after the motor has stopped.");
    }
}
'@.Replace('__METHODS__', $methods.Value)

$sources = "using System;`nusing UnityEngine;`n" `
    + ((@($stubs, $inputFrame, $motor, $gate, $tests) -join "`n") `
        -replace '(?m)^using (System|UnityEngine);[ \t]*\r?\n', '')
Add-Type -TypeDefinition $sources -IgnoreWarnings
[BasicAttackMovementGateTests]::Run()
Write-Output 'Basic attack movement gate tests passed.'
