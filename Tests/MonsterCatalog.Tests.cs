#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using Game.Monsters.Config;
using Game.Monsters.Editor;

internal static class Program
{
    private static void Main()
    {
        var catalogAsset = new MonsterCatalog();
        UnityEditor.AssetDatabase.Register(catalogAsset, "Assets/Data/Monsters/怪物总表.asset");
        var prefab = new UnityEngine.GameObject { IsPrefab = true };
        prefab.Components.Add(new MonsterNetworkView());
        prefab.Components.Add(new MonsterViewRoot());
        prefab.Components.Add(new MonsterTransformInterpolator());
        prefab.Components.Add(new FakeAnimatorDriver());

        var first = MakeAsset("Ls002", "刺龟", prefab, "Assets/Data/Monsters/Ls002.asset");
        Check(MonsterCatalogEditor.TryRegister(first, out var publishMessage)
              && catalogAsset.MonsterAssets.Count == 1
              && publishMessage.Contains("已发布"),
            "Publishing must add the definition to the catalog.");
        Check(MonsterCatalogEditor.TryRegister(first, out publishMessage)
              && catalogAsset.MonsterAssets.Count == 1
              && publishMessage.Contains("已更新"),
            "Republishing must not duplicate a definition.");
        Check(catalogAsset.TryGet("Ls002", out var found) && ReferenceEquals(found, first),
            "Expected exact prefab ID lookup.");
        Check(!catalogAsset.TryGet("ls002", out _), "Lookup must be case-sensitive.");
        Check(MonsterCatalogValidation.TryValidate(catalogAsset.MonsterAssets, out _),
            "Valid monster should pass validation.");
        var dragon = MakeAsset("Ls001", "龙神", prefab, "Assets/Data/Monsters/Ls001.asset");
        Check(MonsterCatalogEditor.TryRegister(dragon, out _)
              && catalogAsset.TryGet("Ls001", out var registeredDragon)
              && ReferenceEquals(registeredDragon, dragon)
              && catalogAsset.TryGet("Ls002", out _),
            "Publishing another monster must preserve the first registration.");

        var duplicate = MakeAsset("Ls002", "重复", prefab, "Assets/Data/Monsters/Duplicate.asset");
        Check(!MonsterCatalogValidation.TryValidate(new[] { first, duplicate }, out var report)
              && report.Contains("重复"),
            "Duplicate IDs should be rejected.");
        Check(!MonsterCatalogEditor.TryRegister(duplicate, out publishMessage)
              && catalogAsset.MonsterAssets.Count == 2,
            "A duplicate ID must not be published.");

        var whitespace = MakeAsset(" Ls003 ", "空格", prefab, "Assets/Data/Monsters/Spaces.asset");
        Check(!MonsterCatalogValidation.TryValidate(new[] { whitespace }, out report)
              && report.Contains("空格"), "IDs with leading or trailing spaces should be rejected.");

        var missingRoot = MakeAsset("Ls003", "缺少组件",
            new UnityEngine.GameObject { IsPrefab = true }, "Assets/Data/Monsters/Missing.asset");
        Check(!MonsterCatalogValidation.TryValidate(new[] { missingRoot }, out report)
              && report.Contains("MonsterNetworkView") && report.Contains("动画驱动"),
            "Incomplete prefab should be rejected.");

        var missingPrefab = MakeAsset("Ls004", "无预制体", null, "Assets/Data/Monsters/NoPrefab.asset");
        Check(!MonsterCatalogValidation.TryValidate(new[] { missingPrefab }, out report)
              && report.Contains("预制体不能为空"), "Missing prefab should be rejected.");
        var unnamed = MakeAsset("Ls005", "", prefab, "Assets/Data/Monsters/Unnamed.asset");
        Check(!MonsterCatalogEditor.TryRegister(unnamed, out report)
              && report.Contains("显示名称不能为空")
              && catalogAsset.MonsterAssets.Count == 2,
            "Publishing a monster without a display name must fail.");
        var sceneInstance = MakeAsset("Ls006", "场景物体",
            new UnityEngine.GameObject(), "Assets/Data/Monsters/SceneInstance.asset");
        Check(!MonsterCatalogValidation.TryValidate(new[] { sceneInstance }, out report)
              && report.Contains("预制体资源"), "Scene objects must not be accepted as prefabs.");
        Console.WriteLine("PASS: 怪物总表发布、精确查找、重复校验和预制体校验。");
    }

    private static MonsterDefinitionAsset MakeAsset(string id, string displayName,
        UnityEngine.GameObject? prefab, string path)
    {
        var asset = new MonsterDefinitionAsset { name = id };
        typeof(MonsterDefinitionAsset).GetField("id", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(asset, id);
        typeof(MonsterDefinitionAsset).GetField("displayName", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(asset, displayName);
        typeof(MonsterDefinitionAsset).GetField("prefab", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(asset, prefab);
        UnityEditor.AssetDatabase.Register(asset, path);
        return asset;
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }
}

namespace UnityEngine
{
    public class Object
    {
        public string name = "";
    }

    public class ScriptableObject : Object { }
    public class MonoBehaviour : Object { }

    public sealed class GameObject : Object
    {
        public bool IsPrefab;
        public List<MonoBehaviour> Components = new List<MonoBehaviour>();

        public T? GetComponent<T>() where T : MonoBehaviour
        {
            foreach (MonoBehaviour component in Components)
                if (component is T result)
                    return result;
            return null;
        }

        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : MonoBehaviour
        {
            var result = new List<T>();
            foreach (MonoBehaviour component in Components)
                if (component is T match)
                    result.Add(match);
            return result.ToArray();
        }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class CreateAssetMenuAttribute : Attribute
    {
        public string fileName = "";
        public string menuName = "";
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SerializeField : Attribute { }

    public static class Debug
    {
        public static void LogError(string message, Object context) { }
    }

    public sealed class GUIContent
    {
        public GUIContent(string text, string? tooltip = null) { }
    }

    public static class GUILayout
    {
        public static bool Button(string label, params object[] options) => false;
        public static bool Button(GUIContent label, params object[] options) => false;
        public static object Width(float width) => new object();
    }
}

namespace UnityEditor
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class CustomEditor : Attribute
    {
        public CustomEditor(Type type) { }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class MenuItem : Attribute
    {
        public MenuItem(string path) { }
    }

    public class Editor
    {
        public UnityEngine.Object target = new UnityEngine.Object();
        public SerializedObject serializedObject = new SerializedObject(null);
        public virtual void OnInspectorGUI() { }
    }

    public static class EditorGUI
    {
        public sealed class DisabledScope : IDisposable
        {
            public DisabledScope(bool disabled) { }
            public void Dispose() { }
        }
    }

    public static class EditorGUILayout
    {
        public sealed class HorizontalScope : IDisposable
        {
            public void Dispose() { }
        }

        public static void PropertyField(SerializedProperty property,
            UnityEngine.GUIContent label) { }
        public static void LabelField(string label, object style) { }
        public static void Space() { }
    }

    public static class EditorStyles
    {
        public static readonly object boldLabel = new object();
    }

    public static class EditorUtility
    {
        public static bool DisplayDialog(string title, string message, string button) => true;
    }

    public static class EditorGUIUtility
    {
        public static void PingObject(UnityEngine.Object asset) { }
    }

    public static class Selection
    {
        public static UnityEngine.Object? activeObject;
    }

    public static class Undo
    {
        public static void RecordObject(UnityEngine.Object asset, string label) { }
    }

    public static class AssetDatabase
    {
        private static readonly Dictionary<UnityEngine.Object, string> Paths =
            new Dictionary<UnityEngine.Object, string>();
        private static readonly Dictionary<string, UnityEngine.Object> Assets =
            new Dictionary<string, UnityEngine.Object>();

        public static void Register(UnityEngine.Object asset, string path)
        {
            Paths[asset] = path;
            Assets[path] = asset;
        }
        public static string GetAssetPath(UnityEngine.Object asset) =>
            Paths.TryGetValue(asset, out string? path) ? path : "";
        public static T? LoadAssetAtPath<T>(string path) where T : UnityEngine.Object =>
            Assets.TryGetValue(path, out UnityEngine.Object? asset) ? asset as T : null;
        public static void SaveAssetIfDirty(UnityEngine.Object asset) { }
    }

    public static class PrefabUtility
    {
        public static bool IsPartOfPrefabAsset(UnityEngine.Object asset) =>
            asset is UnityEngine.GameObject prefab && prefab.IsPrefab;
    }

    public sealed class SerializedObject
    {
        private readonly UnityEngine.Object? asset;
        private readonly List<MonsterDefinitionAsset>? entries;

        public SerializedObject(UnityEngine.Object? asset)
        {
            this.asset = asset;
            if (asset is MonsterCatalog catalog)
                entries = new List<MonsterDefinitionAsset>(catalog.MonsterAssets);
        }

        public void Update() { }
        public SerializedProperty? FindProperty(string name) =>
            name == "monsterAssets" && entries != null
                ? new SerializedProperty(entries) : new SerializedProperty(null);

        public bool ApplyModifiedProperties()
        {
            if (asset is not MonsterCatalog catalog || entries == null)
                return false;
            typeof(MonsterCatalog).GetField("monsterAssets", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(catalog, entries);
            return true;
        }
    }

    public sealed class SerializedProperty
    {
        private readonly List<MonsterDefinitionAsset>? entries;
        private readonly int index;

        public SerializedProperty(List<MonsterDefinitionAsset>? entries, int index = -1)
        {
            this.entries = entries;
            this.index = index;
        }

        public int arraySize
        {
            get => entries!.Count;
            set
            {
                while (entries!.Count < value) entries.Add(null!);
                while (entries.Count > value) entries.RemoveAt(entries.Count - 1);
            }
        }
        public SerializedProperty GetArrayElementAtIndex(int at) => new SerializedProperty(entries, at);
        public UnityEngine.Object? objectReferenceValue
        {
            get => entries![index];
            set => entries![index] = (MonsterDefinitionAsset)value!;
        }
        public void DeleteArrayElementAtIndex(int at)
        {
            if (entries![at] != null) entries[at] = null!;
            else entries.RemoveAt(at);
        }
    }
}

public sealed class MonsterNetworkView : UnityEngine.MonoBehaviour { }
public sealed class MonsterViewRoot : UnityEngine.MonoBehaviour { }
public sealed class MonsterTransformInterpolator : UnityEngine.MonoBehaviour { }
public interface IMonsterAnimatorDriver { }
public sealed class FakeAnimatorDriver : UnityEngine.MonoBehaviour, IMonsterAnimatorDriver { }
