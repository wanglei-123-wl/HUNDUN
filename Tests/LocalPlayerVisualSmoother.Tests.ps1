$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/Player/AuthorityMovement/LocalPlayerVisualSmoother.cs') -Raw
$stubs = @'
namespace UnityEngine
{
    [System.AttributeUsage(System.AttributeTargets.Field)]
    public sealed class SerializeField : System.Attribute {}
    [System.AttributeUsage(System.AttributeTargets.Field)]
    public sealed class MinAttribute : System.Attribute
    {
        public MinAttribute(float minimum) {}
    }
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public sealed class DefaultExecutionOrder : System.Attribute
    {
        public DefaultExecutionOrder(int order) {}
    }
    public static class Time
    {
        public static float unscaledDeltaTime = 0.016f;
        public static float unscaledTime;
    }
    public static class Mathf
    {
        public static float Max(float first, float second) { return System.Math.Max(first, second); }
        public static float Clamp(float value, float min, float max)
        { return System.Math.Max(min, System.Math.Min(max, value)); }
        public static float Exp(float value) { return (float)System.Math.Exp(value); }
    }
    public struct Vector3
    {
        public float x, y, z;
        public static float SmoothDampWeight = 1f;
        public Vector3(float newX, float newY, float newZ) { x = newX; y = newY; z = newZ; }
        public static Vector3 zero { get { return new Vector3(); } }
        public static Vector3 operator +(Vector3 left, Vector3 right)
        {
            return new Vector3(left.x + right.x, left.y + right.y, left.z + right.z);
        }
        public static Vector3 operator -(Vector3 left, Vector3 right)
        {
            return new Vector3(left.x - right.x, left.y - right.y, left.z - right.z);
        }
        public static float Distance(Vector3 left, Vector3 right)
        {
            Vector3 difference = left - right;
            return (float)System.Math.Sqrt(difference.x * difference.x
                + difference.y * difference.y + difference.z * difference.z);
        }
        public static Vector3 SmoothDamp(Vector3 current, Vector3 target,
            ref Vector3 velocity, float smoothTime)
        {
            velocity = zero;
            return current + (target - current) * SmoothDampWeight;
        }
        public static Vector3 operator *(Vector3 value, float scale)
        {
            return new Vector3(value.x * scale, value.y * scale, value.z * scale);
        }
    }
    public struct Quaternion
    {
        public float yaw;
        public static Quaternion identity { get { return new Quaternion(); } }
        public static Quaternion Euler(float x, float y, float z)
        { return new Quaternion { yaw = y }; }
        public static Quaternion Slerp(Quaternion current, Quaternion target, float weight)
        {
            float difference = (target.yaw - current.yaw + 540f) % 360f - 180f;
            return new Quaternion { yaw = current.yaw + difference * weight };
        }
    }
    public class Transform
    {
        public Transform parent;
        private Vector3 relativePosition;
        public Vector3 localPosition
        {
            get { return relativePosition; }
            set { relativePosition = value; }
        }
        public Vector3 position
        {
            get { return parent == null ? relativePosition : parent.position + relativePosition; }
            set { relativePosition = parent == null ? value : value - parent.position; }
        }
        public Quaternion rotation;
        public Quaternion localRotation;
        public void SetPositionAndRotation(Vector3 newPosition, Quaternion newRotation)
        {
            position = newPosition;
            rotation = newRotation;
        }
    }
    public class MonoBehaviour { public Transform transform = new Transform(); }
}
public static class LocalPlayerVisualSmootherTests
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new System.Exception(message);
    }
    private static void Position(UnityEngine.Vector3 actual, float x, float y, float z, string message)
    {
        Check(System.Math.Abs(actual.x - x) < 0.001f
            && System.Math.Abs(actual.y - y) < 0.001f
            && System.Math.Abs(actual.z - z) < 0.001f, message);
    }
    private static void LateUpdate(LocalPlayerVisualSmoother smoother)
    {
        typeof(LocalPlayerVisualSmoother).GetMethod("LateUpdate",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .Invoke(smoother, null);
    }
    private static void CheckSlopeHeight()
    {
        var root = new UnityEngine.Transform();
        var visual = new UnityEngine.Transform { parent = root };
        var smoother = new LocalPlayerVisualSmoother();
        smoother.Bind(root, visual);
        UnityEngine.Vector3.SmoothDampWeight = 0.5f;

        smoother.BeginLogicStep();
        root.position = new UnityEngine.Vector3(0f, 0.4f, 0f);
        smoother.EndLogicStep();
        Position(visual.position, 0f, 0f, 0f,
            "Walking uphill must not snap the visual height at the prediction tick.");
        LateUpdate(smoother);
        Position(visual.position, 0f, 0.2f, 0f,
            "Walking uphill must advance visual height between prediction ticks.");

        smoother.BeginLogicStep();
        root.position = new UnityEngine.Vector3(0f, 0.1f, 0f);
        smoother.EndLogicStep();
        Position(visual.position, 0f, 0.2f, 0f,
            "Walking downhill must retain the prior rendered height until LateUpdate.");
        LateUpdate(smoother);
        Position(visual.position, 0f, 0.15f, 0f,
            "Walking downhill must interpolate toward the new ground height.");
        LateUpdate(smoother);
        Position(visual.position, 0f, 0.125f, 0f,
            "Stopping on a slope must keep converging without another prediction tick.");
        UnityEngine.Vector3.SmoothDampWeight = 1f;
        LateUpdate(smoother);
        Position(visual.position, 0f, 0.1f, 0f,
            "The rendered height must eventually reach the stopped ground height.");
    }
    public static void Run()
    {
        CheckSlopeHeight();
        var root = new UnityEngine.Transform();
        root.position = new UnityEngine.Vector3(10, 2, 20);
        var visual = new UnityEngine.Transform { parent = root };
        visual.position = new UnityEngine.Vector3(9.8f, 1.9f, 19.5f);
        var smoother = new LocalPlayerVisualSmoother();
        smoother.Bind(root, visual);

        smoother.BeginLogicStep();
        root.position = new UnityEngine.Vector3(10.4f, 2.35f, 20.2f);
        smoother.EndLogicStep();
        Position(visual.position, 9.8f, 1.9f, 19.5f,
            "Predicted ascent must preserve the rendered height and XZ until LateUpdate.");

        smoother.BeginLogicStep();
        root.position = new UnityEngine.Vector3(10.7f, 2.1f, 20.4f);
        smoother.EndLogicStep();
        Position(visual.position, 9.8f, 1.9f, 19.5f,
            "Predicted descent must preserve the rendered height until LateUpdate.");

        smoother.BeginLogicStep();
        root.position = new UnityEngine.Vector3(11f, 2.1f, 20.5f);
        smoother.EndLogicStep();
        Position(visual.position, 9.8f, 1.9f, 19.5f,
            "Horizontal prediction and stationary height must retain the old smoothing.");

        UnityEngine.Vector3 previousVisual = visual.position;
        root.position = new UnityEngine.Vector3(11.2f, 2.4f, 20.5f);
        smoother.ApplyCorrection(previousVisual, UnityEngine.Quaternion.identity, 0.4f);
        Position(visual.position, 9.8f, 1.9f, 19.5f,
            "Small authoritative corrections must still keep the old visual position.");
        smoother.BeginLogicStep();
        root.position = new UnityEngine.Vector3(11.2f, 2.6f, 20.5f);
        smoother.EndLogicStep();
        Position(visual.position, 9.8f, 1.9f, 19.5f,
            "Further predicted height changes must retain the visual correction until LateUpdate.");
        LateUpdate(smoother);
        Position(visual.position, 11.2f, 2.6f, 20.5f,
            "Correction offsets should still converge to the logic root.");

        smoother.BeginLogicStep();
        root.position = new UnityEngine.Vector3(11.2f, 5f, 20.5f);
        smoother.EndLogicStep();
        Position(visual.position, 11.2f, 5f, 20.5f,
            "Existing large-motion snap must remain intact.");
        root.position = new UnityEngine.Vector3(12f, 0f, 22f);
        smoother.ResetVisual();
        Position(visual.position, 12f, 0f, 22f,
            "Reset must clear offsets after teleport or respawn.");

        smoother.BeginLogicStep();
        root.position = new UnityEngine.Vector3(12f, 0.4f, 22f);
        smoother.EndLogicStep();
        UnityEngine.Time.unscaledTime = 4f;
        smoother.UpdateJumpPresentation(6.1f, -18f, 0.05f, false);
        LateUpdate(smoother);
        Position(visual.position, 12f, 0.4f, 22f,
            "A jump must bypass walking height smoothing in the same rendered frame.");
        smoother.BeginLogicStep();
        root.position = new UnityEngine.Vector3(12f, 0f, 22f);
        smoother.EndLogicStep();
        smoother.UpdateJumpPresentation(0f, -18f, 0.05f, true);
        LateUpdate(smoother);
        Position(visual.position, 12f, 0f, 22f,
            "Landing should not leave the visual above the logic root.");

        UnityEngine.Time.unscaledTime = 5f;
        root.position = new UnityEngine.Vector3(12f, 0.305f, 22f);
        smoother.UpdateJumpPresentation(6.1f, -18f, 0.05f, false);
        UnityEngine.Time.unscaledTime = 5.025f;
        LateUpdate(smoother);
        Position(visual.position, 12f, 0.44625f, 22f,
            "Render height must advance between fixed input steps during ascent.");
        UnityEngine.Time.unscaledTime = 5.05f;
        LateUpdate(smoother);
        Position(visual.position, 12f, 0.565f, 22f,
            "The interpolated height must meet the next semi-implicit motor step.");
        UnityEngine.Time.unscaledTime = 5.09f;
        LateUpdate(smoother);
        Position(visual.position, 12f, 0.565f, 22f,
            "Extrapolation must stop at the next input step's deadline.");

        root.position = new UnityEngine.Vector3(12f, 0.18f, 22f);
        UnityEngine.Time.unscaledTime = 5.1f;
        smoother.UpdateJumpPresentation(-2f, -18f, 0.05f, false);
        UnityEngine.Time.unscaledTime = 5.12f;
        LateUpdate(smoother);
        Position(visual.position, 12f, 0.1328f, 22f,
            "Descent must be rendered continuously between simulation steps.");
        root.position = new UnityEngine.Vector3(12f, 0f, 22f);
        smoother.UpdateJumpPresentation(0f, -18f, 0.05f, true);
        LateUpdate(smoother);
        Position(visual.position, 12f, 0f, 22f,
            "Landing must stop vertical extrapolation, including while waiting for the server.");

        UnityEngine.Time.unscaledTime = 6f;
        root.position = new UnityEngine.Vector3(12f, 0.305f, 22f);
        smoother.UpdateJumpPresentation(6.1f, -18f, 0.05f, false);
        smoother.ApplyCorrection(visual.position, UnityEngine.Quaternion.identity, 3f);
        UnityEngine.Time.unscaledTime = 6.025f;
        LateUpdate(smoother);
        Position(visual.position, 12f, 0.44625f, 22f,
            "Hard horizontal correction must not cancel an active jump's render motion.");

        smoother.StopJumpPresentation();
        smoother.ResetVisual();

        visual.localRotation = UnityEngine.Quaternion.Euler(0f, 180f, 0f);
        LateUpdate(smoother);
        Check(visual.localRotation.yaw > 0f && visual.localRotation.yaw < 180f,
            "The first visual frame must begin turning without completing a half-turn instantly.");
        float firstTurn = visual.localRotation.yaw;
        LateUpdate(smoother);
        Check(visual.localRotation.yaw > 0f && visual.localRotation.yaw < firstTurn,
            "Rotation must advance continuously between input ticks.");
        for (int i = 0; i < 30; i++) LateUpdate(smoother);
        Check(System.Math.Abs(visual.localRotation.yaw) < 0.001f,
            "The visual turn must converge without snapping on an input tick.");

        visual.localRotation = UnityEngine.Quaternion.Euler(0f, 350f, 0f);
        LateUpdate(smoother);
        Check(visual.localRotation.yaw > 350f && visual.localRotation.yaw < 360f,
            "Rotation across the zero-degree boundary must take the short path.");
    }
}
'@
Add-Type -TypeDefinition ($source + "`n" + $stubs) -IgnoreWarnings
[LocalPlayerVisualSmootherTests]::Run()
Write-Output 'Local player visual smoother tests passed.'
