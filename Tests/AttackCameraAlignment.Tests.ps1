$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$camera = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/Player/ThirdPersonCameraFollow.cs') -Raw
$combat = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/Combat/LocalPlayerCombatController.cs') -Raw
$selection = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/Combat/CombatTargetSelection.cs') -Raw

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

if ($camera -match 'lockedOrbitYaw|UpdateLockedOrbit|ResolveViewRotation|trackMonster') {
    throw 'Persistent target-driven camera rotation must be removed.'
}
$late = Read-Block $camera 'private void LateUpdate('
if ($late -notmatch 'UpdateFacingAlignment\(activeRotationTarget, moving, manualOrbit, uiBlocked, automaticallyAvoidingObstacle\)' -or
    $late -notmatch 'movementInput\.IsAutomaticallyAvoidingObstacle' -or
    $late -notmatch 'transform.rotation = orbitRotation;' -or
    $late -match 'TryGetHorizontalYaw') {
    throw 'The rendered view must use the free orbit and the explicit facing alignment.'
}
foreach ($signature in @('public bool TrySendBasicAttack(', 'public bool TrySendSkillCast(')) {
    $sendMethod = Read-Block $combat $signature
    if ($sendMethod.IndexOf('FaceAttackTarget(target)') -lt $sendMethod.IndexOf('if (!networkManager.Send')) {
        throw 'Attack-facing camera alignment must only be requested after the network send succeeds.'
    }
}
$cameraMethods = @(
    (Read-Block $camera 'internal void AlignWithAttackFacing(')
    (Read-Block $camera 'private void UpdateFacingAlignment(')
    (Read-Block $camera 'internal void ResetMovementYaw(')
    (Read-Block $camera 'internal float ReadMovementYaw(')
) -join "`n"
$face = Read-Block $combat 'private void FaceAttackTarget('

$harness = @'
using System;
using UnityEngine;
namespace UnityEngine
{
    public static class Time { public static float unscaledDeltaTime = .016f; }
    public static class Mathf
    {
        public const float Rad2Deg = (float)(180d / Math.PI);
        public static float LastSmoothTarget;
        public static float Repeat(float value, float length) => value - (float)Math.Floor(value / length) * length;
        public static float Max(float a, float b) => Math.Max(a, b);
        public static float Abs(float value) => Math.Abs(value);
        public static float Atan2(float y, float x) => (float)Math.Atan2(y, x);
        public static float DeltaAngle(float from, float to)
        { float delta = Repeat(to - from, 360); return delta > 180 ? delta - 360 : delta; }
        // A deterministic stand-in for Unity's smoothing. The tests exercise
        // alignment requests, cancellation and angle wrapping, not Unity's math.
        public static float SmoothDampAngle(float current, float target, ref float velocity,
            float smoothTime, float maxSpeed, float deltaTime)
        {
            LastSmoothTarget = target;
            float delta = DeltaAngle(current, target) * .2f;
            float step = Math.Max(-maxSpeed * deltaTime, Math.Min(maxSpeed * deltaTime, delta));
            velocity = step / deltaTime;
            return current + step;
        }
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public float sqrMagnitude => x*x + y*y + z*z;
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x-b.x, a.y-b.y, a.z-b.z);
    }
    public struct Quaternion
    {
        public float yaw;
        public static Quaternion Euler(float x, float y, float z) => new Quaternion { yaw = y };
    }
    public sealed class Transform
    {
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 eulerAngles => new Vector3(0, rotation.yaw, 0);
    }
}
public sealed class ThirdPersonCameraFollow
{
    public static ThirdPersonCameraFollow Current;
    public Transform Character = new Transform();
    public bool CharacterLookActive;
    private float yaw, movementYaw, characterLookYaw;
    private bool hasMovementYaw, alignWithForwardMovement;
    private float? attackFacingYaw;
    private float attackAlignVelocity, movementAlignVelocity;
    private float attackAlignSmoothTime = .25f, attackAlignTurnSpeed = 360;
    private float movementAlignSmoothTime = .35f, movementAlignTurnSpeed = 360;
    public float Yaw { get => yaw; set => yaw = value; }
    public bool Pending => attackFacingYaw.HasValue;
    private Transform GetRotationTarget() => Character;
    public float MovementAlignVelocity => movementAlignVelocity;
    public void Tick(bool manual = false, bool blocked = false, bool moving = false, bool forward = false,
        bool avoiding = false)
    { alignWithForwardMovement = forward; UpdateFacingAlignment(Character, moving, manual, blocked, avoiding); }
    __CAMERA__
}
public sealed class LocalPlayerVisualSmoother
{
    public Vector3 VisualWorldPosition;
    public float FacingYaw;
    public void FaceCombatTargetImmediately(float yaw) { FacingYaw = yaw; }
}
public sealed class CombatFacingHarness
{
    public LocalPlayerVisualSmoother visualSmoother = new LocalPlayerVisualSmoother();
    private ThirdPersonCameraFollow cameraFollow;
    public Transform transform = new Transform();
    private static T FindObjectOfType<T>() where T : class => ThirdPersonCameraFollow.Current as T;
    public void Attack(CombatTargetSelection target) => FaceAttackTarget(target);
    __FACE__
}
public static class AttackCameraAlignmentTests
{
    private static void Check(bool value, string message)
    { if (!value) throw new Exception(message); }
    private static bool Near(float value, float expected) => Math.Abs(Mathf.DeltaAngle(value, expected)) < .001f;
    public static void Run()
    {
        var camera = ThirdPersonCameraFollow.Current = new ThirdPersonCameraFollow();
        var combat = new CombatFacingHarness();
        var monster = new Transform { position = new Vector3(5, 0, 0) };
        var target = new CombatTargetSelection("monster", "monster-1", monster);
        for (int frame = 0; frame < 10; frame++) camera.Tick();
        Check(Near(camera.Yaw, 0), "A selected target without an attack must not rotate the view.");
        combat.Attack(target);
        Check(Near(combat.visualSmoother.FacingYaw, 90), "The attacking character must face the monster.");
        Check(camera.Pending && Near(camera.Yaw, 0), "An attack must request a smooth turn without snapping the camera.");
        Check(Near(camera.ReadMovementYaw(), 90), "Forward input must use the new attack facing, not the interpolating view.");
        camera.Tick();
        Check(camera.Yaw > 0 && camera.Yaw < 90, "The rendered view must advance gradually toward the attack facing.");
        for (int frame = 0; frame < 160; frame++) camera.Tick();
        Check(Near(camera.Yaw, 90) && !camera.Pending, "The camera must settle behind the attacker and release alignment.");
        monster.position = new Vector3(-5, 0, 0);
        camera.Tick();
        Check(Near(camera.Yaw, 90), "A monster moving after the attack must not keep steering the camera.");
        combat.Attack(target);
        Check(Near(combat.visualSmoother.FacingYaw, 270) && camera.Pending,
            "The next attack must refresh both character facing and the camera destination.");
        camera.Tick(manual: true);
        camera.Yaw = 140;
        camera.Tick();
        Check(!camera.Pending && Near(camera.Yaw, 140), "Mouse orbit must take ownership and must not be pulled back afterward.");

        camera.Yaw = 350;
        camera.AlignWithAttackFacing(10);
        camera.Tick();
        Check(Mathf.DeltaAngle(350, camera.Yaw) > 0 && Mathf.DeltaAngle(350, camera.Yaw) < 20,
            "Alignment across north must take the short rotation path.");
        camera.AlignWithAttackFacing(90);
        float previousYaw = camera.Yaw;
        camera.Tick(blocked: true);
        Check(camera.Pending && Near(camera.Yaw, previousYaw), "UI must hold the view without discarding the existing alignment.");
        camera.Tick();
        Check(camera.Pending && !Near(camera.Yaw, previousYaw), "Existing camera alignment must resume after leaving UI.");
        camera.Character.rotation = Quaternion.Euler(0, 180, 0);
        camera.AlignWithAttackFacing(90);
        camera.Tick(moving: true, forward: true);
        Check(!camera.Pending && Near(Mathf.LastSmoothTarget, 180), "Forward movement must replace attack alignment with movement following.");
        camera.AlignWithAttackFacing(90);
        previousYaw = camera.Yaw;
        camera.Tick(moving: true);
        Check(!camera.Pending && Near(camera.Yaw, previousYaw), "Strafing must cancel the old attack turn without recentering the view.");

        camera.Yaw = 35;
        camera.Character.rotation = Quaternion.Euler(0, 120, 0);
        camera.Tick(moving: true, forward: true);
        Check(camera.MovementAlignVelocity != 0, "Normal forward movement must still recenter the camera.");
        previousYaw = camera.Yaw;
        for (int frame = 0; frame < 60; frame++)
        {
            camera.Character.rotation = Quaternion.Euler(0, frame % 2 == 0 ? 120 : 300, 0);
            camera.Tick(moving: true, forward: true, avoiding: true);
            Check(Near(camera.Yaw, previousYaw) && camera.MovementAlignVelocity == 0,
                "Automatic left/right obstacle turns must preserve view yaw and discard recentering momentum.");
        }
        camera.AlignWithAttackFacing(180);
        camera.Tick(moving: true, forward: true, avoiding: true);
        Check(!camera.Pending && Near(camera.Yaw, previousYaw), "Obstacle avoidance must not retain an old attack camera turn.");
        camera.Yaw = 70;
        camera.Tick(manual: true, moving: true, forward: true, avoiding: true);
        Check(Near(camera.Yaw, 70), "Mouse orbit must remain free during obstacle avoidance.");
        camera.Character.rotation = Quaternion.Euler(0, 120, 0);
        camera.Tick(moving: true, forward: true);
        Check(camera.Yaw > 70 && camera.Yaw < 120, "After avoidance ends, normal forward following must resume smoothly.");

        camera = ThirdPersonCameraFollow.Current = new ThirdPersonCameraFollow();
        combat = new CombatFacingHarness();
        combat.Attack(new CombatTargetSelection("player", "player-1", monster));
        Check(!camera.Pending, "Existing player-target facing must not enable monster attack camera alignment.");
        combat.Attack(default);
        Check(!camera.Pending, "An invalid target must not move the camera.");
        camera.Character = null;
        camera.AlignWithAttackFacing(90);
        Check(!camera.Pending, "An unbound camera must ignore attack alignment.");
    }
}
'@.Replace('__CAMERA__', $cameraMethods).Replace('__FACE__', $face)
$sources = "using System;`nusing UnityEngine;`n" + ((@($selection, $harness) -join "`n") `
    -replace '(?m)^using (System|UnityEngine);[ \t]*\r?\n', '')
Add-Type -TypeDefinition $sources -IgnoreWarnings -WarningAction SilentlyContinue
[AttackCameraAlignmentTests]::Run()
Write-Output 'PASS: free target selection, attack alignment, stable obstacle-avoidance camera, manual orbit and movement handoff.'
