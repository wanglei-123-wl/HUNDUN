$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$base = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'LocalPlayerJumpPrediction.Tests.ps1') -Raw
$base = $base -replace '(?m)^\$root = Split-Path \$PSScriptRoot -Parent\r?\n', ''
Invoke-Expression $base.Substring(0, $base.IndexOf('Add-Type -TypeDefinition $sources'))
$input = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/Player/AuthorityMovement/LocalPlayerInputController.cs') -Raw
$movement = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/Player/AuthorityMovement/LocalPlayerInputController.MovementEvents.cs') -Raw
$selection = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/Combat/CombatTargetSelection.cs') -Raw
$gate = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/Player/AuthorityMovement/BasicAttackMovementGate.cs') -Raw

function Read-Block([string] $source, [string] $signature) {
    $start = $source.IndexOf($signature)
    if ($start -lt 0) { throw "Missing production block: $signature" }
    $body = $source.IndexOf('{', $start)
    $depth = 1
    for ($end = $body + 1; $end -lt $source.Length; $end++) {
        if ($source[$end] -eq '{') { $depth++ }
        if ($source[$end] -eq '}') { $depth-- }
        if ($depth -eq 0) { return $source.Substring($start, $end - $start + 1) }
    }
    throw "Unclosed production block: $signature"
}

$methods = @(
    (Read-Block $input 'private void CancelCombatIntent(')
    (Read-Block $input 'private void RecordBasicAttackRequest(')
    (Read-Block $input 'private bool CanRepeatBasicAttack(')
    (Read-Block $input 'private PlayerInputFrame CreateInputFrame(')
    (Read-Block $input 'private static bool HasDirectionalInput(')
    (Read-Block $input 'private static bool HasBothMouseButtons(')
    (Read-Block $input 'private bool IsMovingForBasicAttack(')
) -join "`n"
$send = Read-Block $movement 'if (input.HasAction(PlayerInputActions.PrimaryAction))'

$harness = @'
public sealed class GameSession
{
    public bool IsWorldReady = true;
    public bool IsLocalPlayerDead;
}
public sealed class NetworkManager
{
    public static NetworkManager Instance;
    public bool IsConnected = true, MovementReady = true;
    public bool SceneOperationsPaused, AreLocalPlayerActionsBlocked;
    public long ConnectionEpoch = 1;
    public string SceneSessionId = "scene-1";
}
public static class MonsterTargetingController
{
    public static readonly System.Collections.Generic.HashSet<Transform> Dead =
        new System.Collections.Generic.HashSet<Transform>();
    public static bool IsValidTarget(CombatTargetSelection target) =>
        target.IsValid && !Dead.Contains(target.TargetTransform);
}
public static class GameplayUiInputGuard
{
    public static bool BlocksGameplayInput;
    public static bool BlocksWorldActions;
    public static bool BlocksCameraInput => BlocksGameplayInput;
    public static bool BlocksWorldPointerInput => BlocksCameraInput;
}
public sealed class ThirdPersonCameraFollow
{
    public bool CharacterLookActive;
    public float ReadMovementYaw() => 0;
}
public sealed class LocalPlayerCombatController
{
    public CombatTargetSelection LockedTarget;
    public bool SendSucceeds = true;
    public int Requests;
    public readonly System.Collections.Generic.List<string> TargetIds =
        new System.Collections.Generic.List<string>();
    public bool TrySendBasicAttack(long tick, float yaw)
    {
        if (!SendSucceeds) return false;
        Requests++;
        TargetIds.Add(LockedTarget.EntityId);
        return true;
    }
    public void CancelCombatApproach(string reason = null) { }
}
public sealed class ContinuousAttackInputHarness
{
    public readonly PredictedCharacterMotor predictedMotor;
    public readonly LocalPlayerCombatController combatController;
    public readonly GameSession movementSession = new GameSession();
    public bool inputEnabled = true;
    private readonly BasicAttackMovementGate basicAttackMovementGate = new BasicAttackMovementGate();
    private CombatTargetSelection repeatingAttackTarget;
    private uint repeatingAttackCompletionCount;
    private long repeatingAttackConnectionEpoch;
    private string repeatingAttackSceneSessionId;
    private bool pendingMouseLeftDown, pendingJumpPressed;
    private long clientTick;
    private PlayerInputKeys rollDirection;
    private float rollCameraYaw;
    private ThirdPersonCameraFollow cameraFollow;
    private const string RollTag = "Roll";
    private const float RollBrakingStartNormalizedTime = .68f;
    private const float MovingHorizontalSpeedSquared = .0025f;
    private bool IsMeditationBlockingInput() => false;
    private AnimatorStateInfo ReadRollAnimatorState() => default;
    private PlayerInputKeys FilterMeditationKeys(PlayerInputKeys keys) => keys;
    private PlayerInputActions FilterMeditationActions(PlayerInputActions actions) => actions;
    private PlayerInputKeys ReadKeys() => PlayerInputKeys.None;
    private PlayerInputActions ReadHeldActions() => PlayerInputActions.None;
    private float ReadMovementYaw() => 0f;
    public ContinuousAttackInputHarness(PredictedCharacterMotor motor, LocalPlayerCombatController combat)
    { predictedMotor = motor; combatController = combat; }
    public void Stop() => CancelCombatIntent();
    public void Pump(bool click = false)
    {
        pendingMouseLeftDown = click;
        PlayerInputFrame input = CreateInputFrame();
        __SEND__
        predictedMotor.PresentPredictedInput(input);
        clientTick = input.ClientTick;
    }
    __METHODS__
}
public static class ContinuousBasicAttackTests
{
    private static void Check(bool value, string message)
    { if (!value) throw new Exception(message); }
    private static readonly AnimatorStateInfo Attack = new AnimatorStateInfo {
        shortNameHash = Animator.StringToHash("Attack")
    };
    private static ContinuousAttackInputHarness Create(out Animator animator)
    {
        Time.unscaledTime = 0;
        GameplayUiInputGuard.BlocksGameplayInput = false;
        MonsterTargetingController.Dead.Clear();
        NetworkManager.Instance = new NetworkManager();
        var motor = new PredictedCharacterMotor();
        motor.testComponent = new CharacterController { transform = motor.transform };
        typeof(PredictedCharacterMotor).GetMethod("Awake",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(motor, null);
        animator = new Animator { parameters = new[] {
            new AnimatorControllerParameter { name = "Attack", type = AnimatorControllerParameterType.Trigger }
        }};
        motor.BindAnimator(animator);
        motor.ResetAtSpawn(Vector3.zero, Quaternion.identity);
        var combat = new LocalPlayerCombatController {
            LockedTarget = new CombatTargetSelection("monster", "monster-1", new Transform())
        };
        return new ContinuousAttackInputHarness(motor, combat);
    }
    private static void Enter(ContinuousAttackInputHarness input, Animator animator, float now)
    {
        Time.unscaledTime = now;
        animator.CurrentState = Attack;
        input.Pump();
    }
    private static void End(ContinuousAttackInputHarness input, Animator animator, float now)
    {
        Time.unscaledTime = now;
        animator.CurrentState = default;
        animator.NextState = default;
        animator.InTransition = false;
        input.Pump();
    }
    public static void Run()
    {
        var input = Create(out var animator);
        input.Pump();
        Check(input.combatController.Requests == 0, "Selection alone must not start attacks.");
        input.Pump(true);
        Check(input.combatController.Requests == 1 && animator.AttackTriggered,
            "One click must send exactly one request and trigger the predicted animation.");
        Time.unscaledTime = .02f;
        input.Pump(true);
        Check(input.combatController.Requests == 1, "Extra clicks must not send during attack entry.");
        Enter(input, animator, .03f);
        Time.unscaledTime = 2f;
        input.Pump(true);
        Check(input.combatController.Requests == 1, "A long active animation must not produce repeated requests.");
        animator.InTransition = true;
        animator.NextState = default;
        Time.unscaledTime = 2.1f;
        input.Pump();
        Check(input.combatController.Requests == 1, "The outgoing blend must finish before the interval starts.");
        End(input, animator, 2.2f);
        Time.unscaledTime = 2.399f;
        input.Pump(true);
        Check(input.combatController.Requests == 1, "Manual clicks must not bypass the 0.2 second interval.");
        Time.unscaledTime = 2.401f;
        input.Pump();
        Check(input.combatController.Requests == 2, "Attack completion plus 0.2 seconds must send the second request.");
        Check(input.predictedMotor.BlocksMovementForAttack(), "Continuous attacks must preserve the movement lock.");
        input.Pump();
        Check(input.combatController.Requests == 2, "Another rendered frame must not duplicate that request.");
        Enter(input, animator, 2.42f);
        End(input, animator, 2.5f);
        Time.unscaledTime = 2.701f;
        input.Pump();
        Check(input.combatController.Requests == 3, "A shorter animation must repeat based on its actual end.");
        Check(input.combatController.TargetIds.TrueForAll(id => id == "monster-1"),
            "All automatic requests must retain the selected target.");

        input = Create(out animator);
        input.Pump(true);
        Time.unscaledTime = 1f;
        input.Pump();
        Time.unscaledTime = 2f;
        input.Pump();
        Check(input.combatController.Requests == 1, "An animation that never enters must not create a request loop.");

        Action<ContinuousAttackInputHarness>[] cancellations = {
            value => value.Stop(),
            value => value.combatController.LockedTarget = default,
            value => value.combatController.LockedTarget = new CombatTargetSelection("monster", "monster-2", new Transform()),
            value => MonsterTargetingController.Dead.Add(value.combatController.LockedTarget.TargetTransform),
            value => value.movementSession.IsLocalPlayerDead = true,
            value => NetworkManager.Instance.IsConnected = false,
            value => NetworkManager.Instance.ConnectionEpoch++,
            value => NetworkManager.Instance.SceneSessionId = "scene-2",
            value => NetworkManager.Instance.SceneOperationsPaused = true,
            value => NetworkManager.Instance.AreLocalPlayerActionsBlocked = true
        };
        foreach (var cancel in cancellations)
        {
            input = Create(out animator);
            input.Pump(true);
            Enter(input, animator, .02f);
            End(input, animator, .2f);
            cancel(input);
            Time.unscaledTime = .401f;
            input.Pump();
            Check(input.combatController.Requests == 1, "Cancelled/invalid combat context must not repeat.");
        }

        input = Create(out animator);
        input.combatController.LockedTarget = default;
        input.Pump(true);
        Enter(input, animator, .02f);
        End(input, animator, .2f);
        Time.unscaledTime = .401f;
        input.Pump();
        Check(input.combatController.Requests == 1, "An unselected attack must remain a single attack.");

        input = Create(out animator);
        input.combatController.LockedTarget = new CombatTargetSelection("player", "player-1", new Transform());
        input.Pump(true);
        Enter(input, animator, .02f);
        End(input, animator, .2f);
        Time.unscaledTime = .401f;
        input.Pump();
        Check(input.combatController.Requests == 1, "Player-target attacks must not silently enable monster auto attack.");

        input = Create(out animator);
        input.combatController.SendSucceeds = false;
        input.Pump(true);
        Check(input.combatController.Requests == 0 && !animator.AttackTriggered,
            "A failed request must not animate or enable continuous attack.");
    }
}
'@.Replace('__SEND__', $send).Replace('__METHODS__', $methods)

# The production input frame reads normalized time for rolling; the shared stub only needs that field here.
$stubs = $stubs.Replace('public int shortNameHash;', 'public int shortNameHash; public float normalizedTime;')
$sources = "using System;`nusing UnityEngine;`n" + ((@($stubs, $inputFrame, $motor, $selection, $gate, $harness) -join "`n") `
    -replace '(?m)^using (System|UnityEngine);[ \t]*\r?\n', '')
Add-Type -TypeDefinition $sources -IgnoreWarnings -WarningAction SilentlyContinue
[ContinuousBasicAttackTests]::Run()
Write-Output 'PASS: continuous basic attack timing, request gating, animation completion and cancellation.'
