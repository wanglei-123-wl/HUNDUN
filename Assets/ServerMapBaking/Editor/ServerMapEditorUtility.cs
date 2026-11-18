using System.IO;
using UnityEngine;

namespace ServerMapBaking.Editor
{
    internal static class ServerMapEditorUtility
    {
        public static Float3 ToData(Vector3 value)
        {
            return new Float3(value.x, value.y, value.z);
        }

        public static JsonFloat3 ToJsonData(Vector3 value)
        {
            return new JsonFloat3(value.x, value.y, value.z);
        }

        public static QuaternionData ToData(Quaternion value)
        {
            return new QuaternionData(value.x, value.y, value.z, value.w);
        }

        public static Vector3 ToVector3(Float3 value)
        {
            return new Vector3(value.X, value.Y, value.Z);
        }

        public static Vector3 ToVector3(JsonFloat3 value)
        {
            return new Vector3(value.x, value.y, value.z);
        }

        public static Quaternion ToQuaternion(QuaternionData value)
        {
            return new Quaternion(value.X, value.Y, value.Z, value.W);
        }

        public static Vector3 Abs(Vector3 value)
        {
            return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
        }

        public static bool LayerIncluded(int layer, LayerMask layerMask)
        {
            return (layerMask.value & (1 << layer)) != 0;
        }

        public static string GetHierarchyPath(Transform transform)
        {
            string path = transform.name;
            while (transform.parent != null)
            {
                transform = transform.parent;
                path = transform.name + "/" + path;
            }

            return path;
        }

        public static float NormalizeDegrees(float degrees)
        {
            return Mathf.Repeat(degrees, 360f);
        }

        public static Float3 ToNormalizedEulerData(Quaternion rotation)
        {
            Vector3 euler = rotation.eulerAngles;
            return new Float3(
                NormalizeDegrees(euler.x),
                NormalizeDegrees(euler.y),
                NormalizeDegrees(euler.z));
        }

        public static string SanitizePathSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "map";
            }

            char[] invalidChars = Path.GetInvalidFileNameChars();
            for (int index = 0; index < invalidChars.Length; index++)
            {
                value = value.Replace(invalidChars[index], '_');
            }

            return value.Trim();
        }
    }
}
