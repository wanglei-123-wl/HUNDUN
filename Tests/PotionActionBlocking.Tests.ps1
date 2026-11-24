$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$base = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'LocalPlayerJumpPrediction.Tests.ps1') -Raw -Encoding UTF8
$base = $base -replace '(?m)^\$root = Split-Path \$PSScriptRoot -Parent\r?\n', ''
Invoke-Expression $base.Substring(0, $base.IndexOf('Add-Type -TypeDefinition $sources'))

function Read-Block([string]$text, [string]$signature) {
    $start = $text.IndexOf($signature)
    if ($start -lt 0) { throw "Missing production method: $signature" }
    $body = $text.IndexOf('{', $start)
    $depth = 1
    for ($end = $body + 1; $end -lt $text.Length; $end++) {
        if ($text[$end] -eq '{') { $depth++ }
        if ($text[$end] -eq '}') { $depth-- }
        if ($depth -eq 0) { return $text.Substring($start, $end - $start + 1) }
    }
    throw "Unclosed production method: $signature"
}

$network = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/NetWork/NetworkManager.cs') -Raw -Encoding UTF8
$controller = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/Player/AuthorityMovement/LocalPlayerInputController.cs') -Raw -Encoding UTF8
$movement = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/Player/AuthorityMovement/LocalPlayerInputController.MovementEvents.cs') -Raw -Encoding UTF8
$protocol = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/NetWork/MovementEventMessages.cs') -Raw -Encoding UTF8
$attackGate = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/Player/AuthorityMovement/BasicAttackMovementGate.cs') -Raw -Encoding UTF8
$messages = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/NetWork/NetMessages.cs') -Raw -Encoding UTF8
$networkMethods = @(
    (Read-Block $network 'public bool Send<T>(')
    (Read-Block $network 'internal bool SendMovementEvent(')
    (Read-Block $network 'public bool SendCombatAction(')
    (Read-Block $network 'public bool SendSkillCast(')
) -join "`n"
$inputMethods = @(
    (Read-Block $controller 'internal bool PlayPotionAnimation(')
    (Read-Block $controller 'private void ClearPendingGameplayInput(')
    (Read-Block $movement 'private void HoldMovementForPotion(')
) -join "`n"
$dtos = @(
    (Read-Block $protocol 'public enum MovementEventFlags')
    (Read-Block $protocol 'public class MovementReferencedRequest')
    (Read-Block $protocol 'public sealed class MovementEventInfo')
    (Read-Block $protocol 'public sealed class MovementEventRequest')
    (Read-Block $messages 'public class CombatActionRequest')
    (Read-Block $messages 'public class SkillCastRequest')
) -join "`n"
$update = Read-Block $controller 'private void Update()'
if ($update.IndexOf('HoldMovementForPotion(MovementNow)') -gt $update.IndexOf('UpdateMovement(CreateInputFrame()') -or
    $update -notmatch 'BlocksActionsForPotion' -or
    (Read-Block $movement 'private bool FlushMovement()') -notmatch 'BlocksActionsForPotion' -or
    (Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/Inventory/Integration/InventoryModuleRuntime.cs') -Raw -Encoding UTF8) -notmatch '\?\.PlayPotionAnimation\(\)') {
    throw 'Potion lock is not wired into the item callback and movement pump.'
}

$harness = @'
public class NetMessageBase { public string msgId, type; }
public class MapTransferRequest : MovementReferencedRequest { }
public class RespawnRequest { public string sceneSessionId; }
public class MapTransferReadyRequest { public string sceneSessionId; }
namespace Game.Inventory.Protocol.V2 { public class InventoryV2ItemUseRequestDto { public string sceneSessionId; } }
namespace UnityEngine { public static class JsonUtility { public static string ToJson<T>(T message) => "{}"; } }
public static class BattleSyncDiagnosticsLogger { public static void LogMovementEventSent(long epoch, MovementEventRequest message) { } }
public sealed class NetworkManager
{
    public static NetworkManager Instance;
    public bool IsConnected = true, MovementReady = true, sceneOperationsPaused;
    public bool SceneOperationsPaused => sceneOperationsPaused;
    public Func<bool> LocalPlayerActionBlocked;
    public bool AreLocalPlayerActionsBlocked => LocalPlayerActionBlocked?.Invoke() == true;
    public string SceneSessionId = "scene-test";
    public long ConnectionEpoch = 1, movementEpoch = 1, movementSequence;
    public int Writes, FlushCalls;
    private bool SendJson(string json) { Writes++; return true; }
    private bool TryFlushMovement() { FlushCalls++; return true; }
    __NETWORK_METHODS__
}
public sealed class PotionCamera { public bool Moving = true; public void SetForwardMovement(bool value) { Moving = value; } }
public sealed class PotionInputHarness
{
    public readonly PredictedCharacterMotor predictedMotor;
    public bool isActiveAndEnabled = true, inputEnabled = true;
    private object movementControl = new object();
    private static long MovementNow => 1000;
    private const MovementEventFlags DirectionFlags = MovementEventFlags.Forward | MovementEventFlags.Backward | MovementEventFlags.StrafeLeft | MovementEventFlags.StrafeRight;
    private const MovementEventFlags TurnFlags = MovementEventFlags.TurnLeft | MovementEventFlags.TurnRight;
    public MovementEventFlags movementFlags = MovementEventFlags.Forward | MovementEventFlags.TurnLeft;
    public Vector3 travelDirection = new Vector3(0, 0, 1), blockedTraversalDirection = new Vector3(0, 0, 1);
    public bool traversalStopPending = true, lookTargetTracked = true;
    public bool pendingMouseLeftDown = true, pendingJumpPressed = true, trackingMouseLeftClick = true, suppressLeftClickAttack = true;
    public float mouseLeftDragDistance = 10;
    public object rightClickTarget = new object();
    public bool QueuedCombat = true;
    public long stepAt;
    private readonly BasicAttackMovementGate basicAttackMovementGate = new BasicAttackMovementGate();
    public bool CanAttack => basicAttackMovementGate.CanAttack(Time.unscaledTime);
    public PotionCamera cameraFollow = new PotionCamera();
    public PotionInputHarness(PredictedCharacterMotor motor) { predictedMotor = motor; basicAttackMovementGate.Observe(true, 0); }
    private bool IsMeditationBlockingInput() => false;
    private bool StopMovementForMeditation() => NetworkManager.Instance.SendMovementEvent(new MovementEventRequest { @event = "stop", info = new MovementEventInfo() });
    private void CancelCombatIntent() { QueuedCombat = false; }
    private void ResetRollInput() { }
    private void ClearGroundSlide() { }
    public void Hold(long now) => HoldMovementForPotion(now);
    __INPUT_METHODS__
}
public static class PotionActionBlockingTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static readonly AnimatorStateInfo Idle = new AnimatorStateInfo { Tag = "Locomotion" };
    private static readonly AnimatorStateInfo Drink = new AnimatorStateInfo { shortNameHash = Animator.StringToHash("\u5403\u836f\u52a8\u753b") };
    private static PredictedCharacterMotor Create(out Animator animator)
    {
        Time.unscaledTime = 0;
        var motor = new PredictedCharacterMotor();
        motor.testComponent = new CharacterController { transform = motor.transform };
        typeof(PredictedCharacterMotor).GetMethod("Awake", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(motor, null);
        animator = new Animator { CurrentState = Idle, parameters = new[] {
            new AnimatorControllerParameter { name = "UsePotion", type = AnimatorControllerParameterType.Trigger },
            new AnimatorControllerParameter { name = "Attack", type = AnimatorControllerParameterType.Trigger },
            new AnimatorControllerParameter { name = "Roll", type = AnimatorControllerParameterType.Trigger }
        }};
        motor.BindAnimator(animator);
        motor.ResetAtSpawn(Vector3.zero, Quaternion.identity);
        return motor;
    }
    public static void Run()
    {
        Animator animator;
        var motor = Create(out animator);
        var network = NetworkManager.Instance = new NetworkManager { LocalPlayerActionBlocked = () => motor.BlocksActionsForPotion };
        var input = new PotionInputHarness(motor);
        motor.SetLocalMotion(new Vector3(0, 0, 1), 0, true);
        Check(input.PlayPotionAnimation(), "Grounded player must start potion animation.");
        Check(network.Writes == 1 && network.movementSequence == 1, "Stop must be sent before arming the potion lock.");
        Check(motor.BlocksActionsForPotion, "Trigger must lock input immediately, before Animator consumes it.");
        Check(!input.pendingMouseLeftDown && !input.pendingJumpPressed && !input.trackingMouseLeftClick
            && input.rightClickTarget == null && !input.QueuedCombat, "Buffered clicks, jumps and combat intent must be discarded.");
        Check(motor.Velocity.sqrMagnitude == 0 && input.travelDirection.sqrMagnitude == 0 && !input.cameraFollow.Moving,
            "Potion must immediately stop local movement and camera recentering.");
        Check(!input.PlayPotionAnimation() && !motor.PlayGroundActionAnimation("Roll"), "Repeated potions and roll must not interrupt the lock.");
        animator.InTransition = true; animator.NextState = Drink;
        Check(motor.BlocksActionsForPotion, "Incoming blend must stay locked.");
        animator.InTransition = false; animator.CurrentState = Drink;
        Time.unscaledTime = 10;
        Check(motor.BlocksActionsForPotion, "Actual potion state must not time out based on a guessed clip length.");
        motor.PresentPredictedInput(new PlayerInputFrame { Actions = PlayerInputActions.PrimaryAction });
        Check(!animator.AttackTriggered, "Predicted basic attack must not interrupt drinking.");
        for (int frame = 0; frame < 10; frame++)
        {
            input.Hold(1100 + frame);
            Check(!network.SendMovementEvent(new MovementEventRequest { @event = "start", info = new MovementEventInfo() }), "Movement entry point must be blocked.");
            Check(!network.Send(new MovementEventRequest { @event = "heartbeat" }), "Generic send must not bypass the movement lock.");
            Check(!network.SendCombatAction(new CombatActionRequest { actionSequence = 1, clientTick = 1, actionId = "basic" }), "Basic attack must not reach transport.");
            Check(!network.SendSkillCast(new SkillCastRequest { requestSequence = 1, clientTick = 1, skillId = "skill", targetType = "monster", targetId = "m1" }), "Skill attack must not reach transport.");
        }
        Check(network.Writes == 1 && network.movementSequence == 1 && network.FlushCalls == 0,
            "Blocked operations must not write, flush movement or consume movement sequences.");
        Check(input.stepAt == 1109 && !input.traversalStopPending && !input.lookTargetTracked,
            "Suppressed animation time and stale movement edges must not replay on resume.");
        Check(network.Send(new NetMessageBase { msgId = "heartbeat" }), "Connection heartbeat must continue while drinking.");
        animator.InTransition = true; animator.NextState = Idle;
        Check(motor.BlocksActionsForPotion, "Outgoing blend must stay locked.");
        animator.InTransition = false; animator.CurrentState = Idle;
        Check(!motor.BlocksActionsForPotion, "Returning to locomotion must release the lock.");
        Check(input.CanAttack, "Standing time during potion must count toward the normal basic-attack stop delay.");
        Check(network.SendMovementEvent(new MovementEventRequest { @event = "start", info = new MovementEventInfo() })
            && network.movementSequence == 2, "Fresh movement must resume with the next sequence.");
        Check(network.SendCombatAction(new CombatActionRequest { actionSequence = 1, clientTick = 1, actionId = "basic" }), "Fresh attack must resume after drinking.");
        Check(!input.QueuedCombat && !input.pendingMouseLeftDown, "Finishing potion must not replay blocked attacks.");

        motor = Create(out animator);
        Check(motor.PlayGroundActionAnimation("UsePotion"), "Potion should start again on a fresh player.");
        Time.unscaledTime = 1;
        Check(!motor.BlocksActionsForPotion, "An unconsumed trigger must not leave a permanent lock.");
        Time.unscaledTime = 2;
        Check(motor.PlayGroundActionAnimation("UsePotion"), "Potion can be retried after an animation-entry failure.");
        animator.isActiveAndEnabled = false;
        Check(!motor.BlocksActionsForPotion, "Disabled animator must release pending lock.");
        animator.isActiveAndEnabled = true;
        Check(motor.PlayGroundActionAnimation("UsePotion"), "Animator can be used again after enable.");
        motor.StopForDeath(); motor.ResetAtSpawn(Vector3.zero, Quaternion.identity);
        Check(!motor.BlocksActionsForPotion, "Death and respawn must not retain a pending potion lock.");
    }
}
'@.Replace('__NETWORK_METHODS__', $networkMethods).Replace('__INPUT_METHODS__', $inputMethods)
$sources = "using System;`nusing UnityEngine;`n" + ((@($stubs, $inputFrame, $motor, $attackGate, $dtos, $harness) -join "`n") -replace '(?m)^using (System|UnityEngine);[ \t]*\r?\n', '')
Add-Type -TypeDefinition $sources -IgnoreWarnings
[PotionActionBlockingTests]::Run()
Write-Output 'PASS: potion trigger/blends, input clearing, packet suppression, sequence preservation, completion and failure recovery.'
