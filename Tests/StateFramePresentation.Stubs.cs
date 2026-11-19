using System;
using System.Collections.Generic;

namespace TMPro
{
    public sealed class TMP_Text { public string text; }
}

namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SerializeField : Attribute { }
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class HeaderAttribute : Attribute
    {
        public HeaderAttribute(string value) { }
    }

    public class MonoBehaviour
    {
        public bool enabled;
        public bool TryGetComponent<T>(out T value) where T : class { value = null; return false; }
        protected static void Destroy(object value) { }
        public readonly Transform transform = new Transform();
        public readonly GameObject gameObject = new GameObject();
        protected T GetComponentInChildren<T>() where T : class { return null; }
        public T[] GetComponentsInChildren<T>(bool includeInactive) => gameObject.GetComponentsInChildren<T>(includeInactive);
    }

    public sealed class GameObject
    {
        public string name;
        public bool activeSelf = true;
        public readonly List<object> TestComponents = new List<object>();
        public readonly List<object> InactiveTestComponents = new List<object>();
        public void SetActive(bool active) { activeSelf = active; }
        public bool TryGetComponent<T>(out T value) where T : class { value = null; return false; }
        public T GetComponentInChildren<T>(bool includeInactive) where T : class => null;
        public T[] GetComponentsInChildren<T>(bool includeInactive)
        {
            var result = new List<T>();
            foreach (object component in TestComponents)
                if (component is T typed) result.Add(typed);
            if (includeInactive)
                foreach (object component in InactiveTestComponents)
                    if (component is T typed) result.Add(typed);
            return result.ToArray();
        }
    }
    public class Collider { public bool enabled = true; }
    public sealed class CharacterController : Collider { }
    public sealed class Transform
    {
        public Vector3 position;
        public Quaternion rotation;
        public void SetPositionAndRotation(Vector3 p, Quaternion r) { position = p; rotation = r; }
    }
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public float sqrMagnitude => x * x + y * y;
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator *(Vector3 a, float b) => new Vector3(a.x * b, a.y * b, a.z * b);
        public static float Distance(Vector3 a, Vector3 b)
        {
            float x = a.x - b.x, y = a.y - b.y, z = a.z - b.z;
            return (float)Math.Sqrt(x * x + y * y + z * z);
        }
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t)
        {
            t = Math.Clamp(t, 0f, 1f);
            return new Vector3(a.x + (b.x - a.x) * t,
                a.y + (b.y - a.y) * t, a.z + (b.z - a.z) * t);
        }
    }
    public struct Quaternion
    {
        public float Yaw;
        public static float LastSlerpDistance;
        public static Quaternion Euler(float x, float y, float z) => new Quaternion { Yaw = y };
        public static Quaternion Slerp(Quaternion a, Quaternion b, float t)
        {
            float delta = (b.Yaw - a.Yaw + 540f) % 360f - 180f;
            LastSlerpDistance = Math.Abs(delta);
            return Euler(0f, a.Yaw + delta * Math.Clamp(t, 0f, 1f), 0f);
        }
    }
    public static class Time { public static float deltaTime = 1f / 12f; public static double realtimeSinceStartupAsDouble; }
    public enum AnimatorControllerParameterType { Bool, Trigger }
    public sealed class AnimatorControllerParameter
    {
        public AnimatorControllerParameterType type;
        public string name;
    }
    public sealed class Animator
    {
        private readonly Dictionary<string, bool> bools = new Dictionary<string, bool>();
        public AnimatorControllerParameter[] parameters =
        {
            new AnimatorControllerParameter { type = AnimatorControllerParameterType.Bool, name = "IsMoving" }
        };
        public void SetBool(string name, bool value) { bools[name] = value; }
        public bool GetBool(string name) => bools.TryGetValue(name, out bool value) && value;
        public void ResetTrigger(string name) { }
        public void SetTrigger(string name) { }
        public bool HasState(int layer, int hash) => true;
        public void CrossFade(string name, float duration) { }
        public static int StringToHash(string name) => name.GetHashCode();
    }
    public static class Mathf
    {
        public static float Max(float x, float y) => Math.Max(x, y);
        public static float Clamp(float value, float min, float max) => Math.Clamp(value, min, max);
        public static int Max(int x, int y) => Math.Max(x, y);
        public static int Clamp(int value, int min, int max) => Math.Clamp(value, min, max);
    }
}

public sealed class PlayerNetworkState
{
    public long characterDbId, serverTick, sequence, visibilityRevision;
    public RemoteMovementSnapshot movement;
    public string characterId, nickname, animation;
    public float x, y, z, rotY, velocityX, velocityY, velocityZ;
    public int movementState;
    public UnityEngine.Vector3 GetPosition() => new UnityEngine.Vector3(x, y, z);
    public UnityEngine.Vector3 GetVelocity() => new UnityEngine.Vector3(velocityX, velocityY, velocityZ);
    public UnityEngine.Quaternion GetRotation() => UnityEngine.Quaternion.Euler(0f, rotY, 0f);
}

public sealed class CombatEntityMessage
{
    public string entityType, entityId;
}
public sealed class CombatStateMessage
{
    public CombatEntityMessage entity;
    public int hp, maxHp;
    public string lifeState;
    public long combatRevision, spawnGeneration;
}

public class NetMessageBase { public string msgId, type; }
public class EnterWorldResult
{
    public string sceneSessionId;
    public int movementProtocolVersion;
    public MovementEventPolicy movementPolicy;
    public MovementEventControl movementControl;
}
public class PlayerEffectPresenter { public void SetVisual(UnityEngine.GameObject visual) {} }
public static class PlayerDeathAnimation
{
    public static void SetDead(UnityEngine.Animator animator, bool dead, bool alreadyDeadAtSpawn = false) {}
}
public class NetworkManager
{
    public static NetworkManager Instance;
    public int MovementHeartbeatIntervalMs;
    public float MovementGravity;
}
