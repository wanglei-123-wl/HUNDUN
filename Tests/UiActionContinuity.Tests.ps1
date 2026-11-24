$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$base = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'LocalPlayerJumpPrediction.Tests.ps1') -Raw -Encoding UTF8
$base = $base -replace '(?m)^\$root = Split-Path \$PSScriptRoot -Parent\r?\n', ''
Invoke-Expression $base.Substring(0, $base.IndexOf('Add-Type -TypeDefinition $sources'))
$fixture = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'ContinuousBasicAttack.Tests.ps1') -Raw -Encoding UTF8
$fixtureStart = $fixture.IndexOf('$input =')
Invoke-Expression $fixture.Substring($fixtureStart, $fixture.IndexOf('$sources =') - $fixtureStart)

$controllerSource = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/Player/AuthorityMovement/LocalPlayerInputController.cs') -Raw -Encoding UTF8
$actualKeys = (Read-Block $controllerSource 'private PlayerInputKeys ReadKeys(')
$actualHeld = (Read-Block $controllerSource 'private static PlayerInputActions ReadHeldActions(')
$extraMethods = @(
    (Read-Block $controllerSource 'private PlayerInputKeys ReadDirectionalKeys(')
    (Read-Block $controllerSource 'private bool WorldKeyActive(')
    (Read-Block $controllerSource 'private static bool SkillKeyPressed(')
    (Read-Block $controllerSource 'private void HandleManualCombatCancellation(')
    ([regex]::Match($controllerSource, '(?s)private bool DirectionKeyActive\(.*?;').Value)
) -join "`n"
if ($controllerSource -match 'Input.anyKeyDown' -or
    (Read-Block $controllerSource 'private void ApplyCombatApproachInput(') -match 'GameplayUiInputGuard.BlocksGameplayInput') {
    throw 'UI shortcuts and UI pointer state must not cancel an existing combat approach.'
}
foreach ($slot in 1..7) {
    if (!$controllerSource.Contains("!keyboardBlocked && Input.GetKeyDown(KeyCode.Alpha$slot)")) {
        throw 'Skill keyboard input must be isolated while editing text.'
    }
}
$harness = $harness.Replace('public sealed class ContinuousAttackInputHarness', 'public sealed partial class ContinuousAttackInputHarness')
$harness = $harness.Replace('private PlayerInputKeys ReadKeys() => PlayerInputKeys.None;', $actualKeys)
$harness = $harness.Replace('private PlayerInputActions ReadHeldActions() => PlayerInputActions.None;', $actualHeld)
$pointerStub = @'
public static bool BlocksWorldKeyboardInput;
    public static int CapturedMouseButtons;
    public static bool BlocksCameraInput => BlocksGameplayInput || BlocksWorldKeyboardInput || BlocksWorldActions;
    public static bool IsWorldMouseButtonHeld(int button) => Input.GetMouseButton(button) && (CapturedMouseButtons & (1 << button)) == 0;
'@
$harness = $harness.Replace('public static bool BlocksCameraInput => BlocksGameplayInput;', $pointerStub)
$harness = $harness.Replace('public void CancelCombatApproach(string reason = null) { }', 'public int Cancellations; public void CancelCombatApproach(string reason = null) { Cancellations++; }')

$tests = @'
namespace UnityEngine
{
    public enum KeyCode { W,A,S,D,UpArrow,LeftArrow,DownArrow,RightArrow,LeftShift,RightShift,Space,Q,Alpha1,Alpha2,Alpha3,Alpha4,Alpha5,Alpha6,Alpha7,Tab,B,C,X,Z,Escape }
    public static class Input
    {
        public static readonly System.Collections.Generic.HashSet<KeyCode> Held = new System.Collections.Generic.HashSet<KeyCode>();
        public static readonly System.Collections.Generic.HashSet<KeyCode> Pressed = new System.Collections.Generic.HashSet<KeyCode>();
        public static int MouseHeld, MousePressed;
        public static Vector2 mouseScrollDelta;
        public static bool GetKey(KeyCode key) => Held.Contains(key);
        public static bool GetKeyDown(KeyCode key) => Pressed.Contains(key);
        public static bool GetMouseButton(int button) => (MouseHeld & (1 << button)) != 0;
        public static bool GetMouseButtonDown(int button) => (MousePressed & (1 << button)) != 0;
        public static void Clear() { Held.Clear(); Pressed.Clear(); MouseHeld = MousePressed = 0; mouseScrollDelta = new Vector2(); }
    }
}
public sealed partial class ContinuousAttackInputHarness
{
    private readonly System.Collections.Generic.HashSet<KeyCode> worldMovementKeys = new System.Collections.Generic.HashSet<KeyCode>();
    private readonly System.Collections.Generic.HashSet<KeyCode> uiMovementKeys = new System.Collections.Generic.HashSet<KeyCode>();
    public PlayerInputFrame Frame() => CreateInputFrame();
    public bool HasRepeat => repeatingAttackTarget.IsValid;
    public void ConsiderManualInput()
    {
        bool keyboard = GameplayUiInputGuard.BlocksWorldKeyboardInput;
        HandleManualCombatCancellation(GameplayUiInputGuard.BlocksWorldActions, keyboard,
            GameplayUiInputGuard.BlocksCameraInput, ReadKeys(), ReadHeldActions(),
            !keyboard && WorldKeyActive(KeyCode.Space, true));
    }
    __EXTRA_METHODS__
}
public static class UiActionContinuityTests
{
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    private static ContinuousAttackInputHarness Create(out Animator animator)
    {
        Input.Clear(); Time.unscaledTime = 0;
        GameplayUiInputGuard.BlocksGameplayInput = false;
        GameplayUiInputGuard.BlocksWorldActions = false;
        GameplayUiInputGuard.BlocksWorldKeyboardInput = false;
        GameplayUiInputGuard.CapturedMouseButtons = 0;
        MonsterTargetingController.Dead.Clear(); NetworkManager.Instance = new NetworkManager();
        var motor = new PredictedCharacterMotor();
        motor.testComponent = new CharacterController { transform = motor.transform };
        typeof(PredictedCharacterMotor).GetMethod("Awake", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(motor, null);
        animator = new Animator { parameters = new[] { new AnimatorControllerParameter { name = "Attack", type = AnimatorControllerParameterType.Trigger } } };
        motor.BindAnimator(animator); motor.ResetAtSpawn(Vector3.zero, Quaternion.identity);
        return new ContinuousAttackInputHarness(motor, new LocalPlayerCombatController {
            LockedTarget = new CombatTargetSelection("monster", "monster-1", new Transform()) });
    }
    public static void Run()
    {
        Animator animator;
        var input = Create(out animator);
        input.Pump(true);
        animator.CurrentState = new AnimatorStateInfo { shortNameHash = Animator.StringToHash("Attack") };
        Time.unscaledTime = .02f; input.Pump();
        animator.CurrentState = default; Time.unscaledTime = .2f; input.Pump();
        int previousCancellations = input.combatController.Cancellations;
        GameplayUiInputGuard.BlocksGameplayInput = true;
        GameplayUiInputGuard.CapturedMouseButtons = 1;
        Input.Pressed.Add(KeyCode.Tab); Input.MouseHeld = Input.MousePressed = 1;
        Input.mouseScrollDelta = new Vector2(0, 5);
        input.ConsiderManualInput();
        Time.unscaledTime = .401f; input.Pump();
        Check(input.combatController.Requests == 2 && input.HasRepeat,
            "Opening, clicking and scrolling UI must preserve real repeated attack requests.");
        Check(input.combatController.Cancellations == previousCancellations, "UI interaction must not cancel combat approach.");
        GameplayUiInputGuard.BlocksWorldKeyboardInput = true;
        Input.Held.Add(KeyCode.W); Input.Held.Add(KeyCode.Space);
        Input.Pressed.Add(KeyCode.Q); Input.Pressed.Add(KeyCode.Alpha1); Input.Pressed.Add(KeyCode.Space);
        input.ConsiderManualInput();
        Check(input.HasRepeat && input.Frame().Keys == PlayerInputKeys.None,
            "Chat letters, jump and skill keys must not move the player or cancel existing combat.");

        input = Create(out animator);
        Input.Held.Add(KeyCode.W); Input.Pressed.Add(KeyCode.W);
        Check(input.Frame().Keys == PlayerInputKeys.W, "Normal world movement must be accepted.");
        Input.Pressed.Clear(); GameplayUiInputGuard.BlocksGameplayInput = true;
        Check(input.Frame().Keys == PlayerInputKeys.W, "Hovering/opening UI must not stop the held world key.");
        GameplayUiInputGuard.BlocksWorldKeyboardInput = true;
        Input.Held.Add(KeyCode.A); Input.Held.Add(KeyCode.LeftShift); Input.Held.Add(KeyCode.Space);
        Check(input.Frame().Keys == PlayerInputKeys.W, "New typing keys must be isolated while the original W remains held.");
        Input.Held.Remove(KeyCode.W);
        Check(input.Frame().Keys == PlayerInputKeys.None, "Releasing the original world key must stop movement while chatting.");
        GameplayUiInputGuard.BlocksWorldKeyboardInput = false;
        Check(input.Frame().Keys == PlayerInputKeys.None, "Keys held from typing must stay isolated after focus leaves.");
        Input.Clear(); input.Frame(); Input.Held.Add(KeyCode.A); Input.Pressed.Add(KeyCode.A);
        Check(input.Frame().Keys == PlayerInputKeys.A, "A fresh world press after releasing typing keys must resume control.");

        input = Create(out animator);
        Input.Held.Add(KeyCode.W); input.Frame();
        GameplayUiInputGuard.BlocksWorldKeyboardInput = true;
        Input.Held.Add(KeyCode.UpArrow); input.Frame(); Input.Held.Remove(KeyCode.W);
        Check(input.Frame().Keys == PlayerInputKeys.None, "A newly typed arrow must not inherit ownership from the W alias.");

        input = Create(out animator);
        Input.MouseHeld = 3;
        Check(input.Frame().HasAction(PlayerInputActions.MouseLeftHeld) && input.Frame().HasAction(PlayerInputActions.MouseRightHeld), "World mouse movement must work.");
        GameplayUiInputGuard.BlocksGameplayInput = true;
        Check(input.Frame().HasAction(PlayerInputActions.MouseLeftHeld) && input.Frame().HasAction(PlayerInputActions.MouseRightHeld), "Opening UI must preserve world-owned held mouse movement.");
        GameplayUiInputGuard.CapturedMouseButtons = 3;
        Check(input.Frame().Actions == PlayerInputActions.None, "Buttons pressed on UI must not become mouse movement.");
        GameplayUiInputGuard.BlocksGameplayInput = false;
        Check(input.Frame().Actions == PlayerInputActions.None, "UI-owned drag must remain isolated after leaving UI.");
        GameplayUiInputGuard.BlocksGameplayInput = true;
        input.Pump(true);
        Check(input.combatController.Requests == 0, "A UI click must never become a new basic attack.");

        GameplayUiInputGuard.BlocksWorldActions = true;
        Input.Held.Add(KeyCode.W);
        input.ConsiderManualInput();
        Check(input.Frame().Keys == PlayerInputKeys.None && !input.HasRepeat, "Actual loading/action locks must still block world actions.");
    }
}
'@.Replace('__EXTRA_METHODS__', $extraMethods)
$sources = "using System;`nusing UnityEngine;`n" + ((@($stubs, $inputFrame, $motor, $selection, $gate, $harness, $tests) -join "`n") -replace '(?m)^using (System|UnityEngine);[ \t]*\r?\n', '')
Add-Type -TypeDefinition $sources -IgnoreWarnings -WarningAction SilentlyContinue
[UiActionContinuityTests]::Run()
Write-Output 'PASS: UI action continuity, chat key ownership, physical key aliases, pointer isolation and hard action locks.'
