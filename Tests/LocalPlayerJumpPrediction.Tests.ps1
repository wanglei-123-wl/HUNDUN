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
        public bool IsTag(string value) { return false; }
    }
    public class Animator
    {
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
public class PlayerInputRequest
{
    public long sequence, clientTick, clientTimeMs;
    public int keys, actions;
    public float lookYaw, cameraYaw;
    public bool faceCamera, jumpPressed, isRunning;
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
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Invoke(motor, null);
        motor.ResetAtSpawn(Vector3.zero, Quaternion.identity);
        Check(motor.IsGrounded, "Spawn on ground should allow an immediate jump.");

        var jump = new PlayerInputFrame { JumpPressed = true };
        var idle = new PlayerInputFrame();
        motor.Simulate(jump, 0.05f, true);
        float firstHeight = motor.transform.position.y;
        Check(firstHeight > 0f && motor.Velocity.y > 0f,
            "Grounded jump must rise in its first simulation step.");

        var oldState = new PlayerAuthoritativeState {
            position = Vector3.zero, velocity = Vector3.zero, grounded = true };
        motor.SetAuthoritativeState(oldState);
        motor.Simulate(jump, 0.05f, true);
        Check(Math.Abs(motor.transform.position.y - firstHeight) < 0.001f,
            "Replaying an unacknowledged jump after a grounded state must reproduce the launch.");

        var airborne = new PlayerAuthoritativeState {
            position = motor.transform.position, velocity = motor.Velocity, grounded = false };
        motor.SetAuthoritativeState(airborne);
        float lastHeight = motor.transform.position.y;
        bool landed = false;
        for (int i = 0; i < 40; i++)
        {
            motor.Simulate(idle, 0.05f, true);
            float height = motor.transform.position.y;
            Check(height >= 0f, "Landing must not sink below the floor.");
            if (motor.IsGrounded) { landed = true; break; }
            lastHeight = height;
        }
        Check(landed && motor.Velocity.y == 0f, "Fall must finish grounded with no downward velocity.");
        motor.Simulate(jump, 0.05f, true);
        Check(motor.transform.position.y > 0f, "A second jump must work immediately after landing.");

        motor.SetAuthoritativeState(oldState);
        Check(motor.IsGrounded && motor.transform.position.y == 0f,
            "A grounded server correction must restore grounded state after a jump.");
        motor.Simulate(idle, 0.05f, true);
        Check(motor.IsGrounded && motor.transform.position.y == 0f,
            "Replayed idle input must not bounce above the floor.");
        motor.Simulate(jump, 0.05f, true);
        Check(motor.Velocity.y > 0f, "Another jump after the grounded correction must launch.");

        var saved = motor.CapturePredictionState();
        motor.Simulate(idle, 0.05f, true);
        motor.RestorePredictionState(saved);
        Check(Math.Abs(motor.transform.position.y - saved.Position.y) < 0.001f
            && Math.Abs(motor.Velocity.y - saved.Velocity.y) < 0.001f
            && motor.IsGrounded == saved.Grounded,
            "Failed send must restore the exact pre-send motion state.");

        motor.ResetAtSpawn(Vector3.zero, Quaternion.identity);
        var backward = new PlayerInputFrame { Keys = PlayerInputKeys.S, CameraYaw = 0f };
        motor.Simulate(backward, 0.05f, true);
        Check(Math.Abs(Math.Abs(motor.transform.eulerAngles.y) - 180f) < 0.001f,
            "Backward input must face backward in its first prediction step.");
        Check(motor.transform.position.z < 0f && motor.Velocity.z < 0f,
            "Backward input must move immediately instead of waiting for a turn.");

        motor.SetAuthoritativeState(oldState);
        motor.Simulate(backward, 0.05f, true);
        Check(Math.Abs(Math.Abs(motor.transform.eulerAngles.y) - 180f) < 0.001f
            && motor.transform.position.z < 0f,
            "Replaying backward input must reproduce the same immediate turn and movement.");

        motor.ResetAtSpawn(Vector3.zero, Quaternion.identity);
        motor.Simulate(new PlayerInputFrame { FaceCamera = true, CameraYaw = 90f }, 0.05f, true);
        Check(Math.Abs(motor.transform.eulerAngles.y - 9f) < 0.001f,
            "Facing the camera without movement must keep its existing gradual turn.");
    }
}
'@
$sources = @($stubs, $inputFrame, $motor, $tests) -join "`n"
$sources = "using System;`nusing UnityEngine;`n" `
    + ($sources -replace '(?m)^using (System|UnityEngine);[ \t]*\r?\n', '')
Add-Type -TypeDefinition $sources -IgnoreWarnings
[JumpTests]::Run()
$controller = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/Player/AuthorityMovement/LocalPlayerInputController.cs') -Raw
$predictionIndex = $controller.IndexOf('predictedMotor.Simulate(input, fixedDeltaTime, true);')
$sendIndex = $controller.IndexOf('networkManager.SendPlayerInput(input.ToRequest())')
if ($predictionIndex -lt 0 -or $sendIndex -le $predictionIndex `
    -or $controller -notmatch 'inputAccumulator = Mathf.Max\(inputAccumulator, FixedInputDeltaTime\)') {
    throw 'Jump input must be simulated before the send and must not wait for the next input tick.'
}
Write-Output 'Local player jump prediction tests passed.'
