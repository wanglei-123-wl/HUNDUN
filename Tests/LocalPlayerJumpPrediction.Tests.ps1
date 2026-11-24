$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$motor = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/Player/AuthorityMovement/PredictedCharacterMotor.cs') -Raw
$inputFrame = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/Player/AuthorityMovement/PlayerInputFrame.cs') -Raw
$stubs = @'
using System;
namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Class)] public sealed class RequireComponent : Attribute
    {
        public RequireComponent(Type type) {}
    }
    [AttributeUsage(AttributeTargets.Field)] public sealed class SerializeField : Attribute {}
    [AttributeUsage(AttributeTargets.Field)] public sealed class HeaderAttribute : Attribute
    {
        public HeaderAttribute(string title) {}
    }
    [AttributeUsage(AttributeTargets.Field)] public sealed class MinAttribute : Attribute
    {
        public MinAttribute(float minimum) {}
    }
    public static class Debug
    {
        public static void LogWarning(string message) {}
    }
    public static class Time { public static float unscaledTime; }
    public static class Mathf
    {
        public const float Deg2Rad = (float)(Math.PI / 180d);
        public const float Rad2Deg = (float)(180d / Math.PI);
        public static float Sin(float value) { return (float)Math.Sin(value); }
        public static float Cos(float value) { return (float)Math.Cos(value); }
        public static float Atan2(float y, float x) { return (float)Math.Atan2(y, x); }
        public static float Max(float a, float b) { return Math.Max(a, b); }
        public static float Abs(float value) { return Math.Abs(value); }
        public static float MoveTowardsAngle(float a, float b, float step)
        {
            return MoveTowards(a, b, step);
        }
        public static float MoveTowards(float a, float b, float step)
        {
            return Math.Abs(b - a) <= step ? b : a + Math.Sign(b - a) * step;
        }
    }
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public float sqrMagnitude { get { return x * x + y * y; } }
        public Vector2 normalized
        {
            get { float length = (float)Math.Sqrt(sqrMagnitude);
                return length > 0 ? new Vector2(x / length, y / length) : new Vector2(); }
        }
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero { get { return new Vector3(); } }
        public static Vector3 up { get { return new Vector3(0, 1, 0); } }
        public static Vector3 down { get { return new Vector3(0, -1, 0); } }
        public float sqrMagnitude { get { return x*x + y*y + z*z; } }
        public Vector3 normalized
        {
            get { float length = (float)Math.Sqrt(sqrMagnitude);
                return length > 0 ? this * (1f / length) : zero; }
        }
        public static Vector3 operator +(Vector3 a, Vector3 b)
        { return new Vector3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static Vector3 operator -(Vector3 a, Vector3 b)
        { return new Vector3(a.x - b.x, a.y - b.y, a.z - b.z); }
        public static Vector3 operator *(Vector3 a, float b)
        { return new Vector3(a.x * b, a.y * b, a.z * b); }
        public static float Dot(Vector3 a, Vector3 b)
        { return a.x*b.x + a.y*b.y + a.z*b.z; }
        public static float Distance(Vector3 a, Vector3 b)
        { return (float)Math.Sqrt((a - b).sqrMagnitude); }
        public static Vector3 MoveTowards(Vector3 a, Vector3 b, float step)
        {
            Vector3 delta = b - a;
            float length = (float)Math.Sqrt(delta.sqrMagnitude);
            return length <= step || length == 0 ? b : a + delta * (step / length);
        }
    }
    public struct Quaternion
    {
        public float yaw;
        public static Quaternion identity { get { return new Quaternion(); } }
        public static Quaternion Euler(float x, float y, float z)
        { return new Quaternion { yaw = y }; }
    }
    public class Transform
    {
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 eulerAngles { get { return new Vector3(0, rotation.yaw, 0); } }
        public Vector3 forward
        {
            get
            {
                float radians = rotation.yaw * (float)(Math.PI / 180d);
                return new Vector3((float)Math.Sin(radians), 0, (float)Math.Cos(radians));
            }
        }
        public void SetPositionAndRotation(Vector3 pos, Quaternion rot)
        { position = pos; rotation = rot; }
    }
    [Flags] public enum CollisionFlags { None = 0, Sides = 1, Above = 2, Below = 4 }
    public class CharacterController
    {
        public Transform transform;
        public bool enabled = true;
        public bool isGrounded;
        public float radius, height, minMoveDistance;
        public Vector3 center;
        public CollisionFlags Move(Vector3 delta)
        {
            Vector3 next = transform.position + delta;
            CollisionFlags flags = CollisionFlags.None;
            if (next.y <= 0f)
            {
                next.y = 0f;
                flags |= CollisionFlags.Below;
            }
            transform.position = next;
            isGrounded = (flags & CollisionFlags.Below) != 0;
            return flags;
        }
    }
    public class MonoBehaviour
    {
        public Transform transform = new Transform();
        public object testComponent;
        public T GetComponent<T>() where T : class { return testComponent as T; }
        public T GetComponentInChildren<T>(bool includeInactive) where T : class { return null; }
    }
    public enum AnimatorControllerParameterType { Bool, Trigger }
    public struct AnimatorControllerParameter
    {
        public AnimatorControllerParameterType type;
        public string name;
    }
    public struct AnimatorStateInfo
    {
        public int shortNameHash;
        public string Tag;
        public bool IsTag(string value) { return Tag == value; }
    }
    public class Animator
    {
        public bool isActiveAndEnabled = true;
        public AnimatorControllerParameter[] parameters = new AnimatorControllerParameter[0];
        public bool IsMoving;
        public bool AttackTriggered;
        public AnimatorStateInfo CurrentState;
        public AnimatorStateInfo NextState;
        public bool InTransition;
        public static int StringToHash(string value) { return value.GetHashCode(); }
        public AnimatorStateInfo GetCurrentAnimatorStateInfo(int layer) { return CurrentState; }
        public AnimatorStateInfo GetNextAnimatorStateInfo(int layer) { return NextState; }
        public bool IsInTransition(int layer) { return InTransition; }
        public void SetBool(string name, bool value)
        { if (name == "IsMoving") IsMoving = value; }
        public void ResetTrigger(string name)
        { if (name == "Attack") AttackTriggered = false; }
        public void SetTrigger(string name)
        { if (name == "Attack") AttackTriggered = true; }
    }
}
public class MovementEventControl
{
    public float walkSpeed = 5, runSpeed = 8, jumpSpeed = 7, gravity = -18, radius = .5f, height = 2, turnSpeed = 180;
    public float velocityY;
    public bool grounded = true;
    public UnityEngine.Vector3 Position;
    public UnityEngine.Quaternion Rotation;
}
public class PlayerAuthoritativeState
{
    public UnityEngine.Vector3 position, velocity;
    public bool grounded;
    public int actionState, movementState;
    public long lastProcessedInputSequence;
    public float y { get { return position.y; } }
    public float velocityY { get { return velocity.y; } }
    public UnityEngine.Vector3 GetPosition() { return position; }
    public UnityEngine.Vector3 GetVelocity() { return velocity; }
    public UnityEngine.Quaternion GetRotation() { return UnityEngine.Quaternion.identity; }
}
'@
$tests = @'
using System;
using UnityEngine;
public static class JumpTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
    public static void Run()
    {
        var motor = new PredictedCharacterMotor();
        motor.testComponent = new CharacterController { transform = motor.transform };
        typeof(PredictedCharacterMotor).GetMethod("Awake",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(motor, null);
        motor.ApplyMovementControl(new MovementEventControl { jumpSpeed = 8f, radius = .45f });
        Check(motor.IsGrounded && motor.JumpSpeed == 8f && motor.Gravity == -18f,
            "Server physics must apply before local movement.");
        Check(((CharacterController)motor.testComponent).radius == .45f,
            "The local collision body must use the server radius.");
        motor.SetLocalMotion(Vector3.zero, motor.JumpSpeed, false);
        motor.MoveLocally(Vector3.up * .2f);
        Check(motor.transform.position.y > 0 && !motor.IsGrounded, "Local takeoff uses CharacterController.");
        Check((motor.MoveLocally(Vector3.down) & CollisionFlags.Below) != 0, "Local landing detects ground.");
        var backward = motor.MovementDirection(new PlayerInputFrame { Keys = PlayerInputKeys.S });
        Check(backward.z < 0 && motor.TurnStep(90, .05f) == 9f, "Camera-relative direction and turn speed remain valid.");
    }
}
'@
$sources = @($stubs, $inputFrame, $motor, $tests) -join "`n"
$sources = "using System;`nusing UnityEngine;`n" `
    + ($sources -replace '(?m)^using (System|UnityEngine);[ \t]*\r?\n', '')
Add-Type -TypeDefinition $sources -IgnoreWarnings
[JumpTests]::Run()
$controller = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/Player/AuthorityMovement/LocalPlayerInputController.MovementEvents.cs') -Raw
if ($controller -notmatch 'Report\("jump", now\)' -or $controller -notmatch 'edge = "land"') {
    throw 'Jump and landing must publish movement events at their transitions.'
}
Write-Output 'PASS: movement control, local jump and landing events.'
