using System;
using System.Collections.Generic;
using System.Reflection;
using Game.Items;
using Game.Items.Editor;

internal static class ItemCatalogEditorTests
{
    public static void Run()
    {
        UnityEditor.AssetDatabase.Reset();
        UnityEngine.Debug.Errors.Clear();
        var table = new ItemDefinitionTable();
        SetField(table, "items", new List<ItemDefinition>
        {
            new ItemDefinition { ItemId = "legacy" }
        });
        UnityEditor.AssetDatabase.Register(
            "Assets/Data/Items/ItemDefinitionTable.asset", table);

        var first = MakeAsset("first");
        var second = MakeAsset("second");
        UnityEditor.AssetDatabase.Register("Assets/Data/Items/B/Second.asset", second);
        UnityEditor.AssetDatabase.Register("Assets/Data/Items/A/First.asset", first);

        Process(refresh: false);
        Check(UnityEditor.AssetDatabase.Saves == 0 && GetReferences(table).Count == 0,
            "Read-only validation must not change the table.");

        Process(refresh: true);
        Check(GetReferences(table).Count == 2
              && ReferenceEquals(GetReferences(table)[0], first)
              && ReferenceEquals(GetReferences(table)[1], second)
              && UnityEditor.AssetDatabase.Saves == 1,
            "Refresh must persist sorted references only to the target table.");

        Process(refresh: true);
        Check(UnityEditor.AssetDatabase.Saves == 1,
            "Refreshing an unchanged catalog must not write again.");

        SetField(second, "definition", new ItemDefinition { ItemId = "legacy" });
        Process(refresh: true);
        Check(UnityEditor.AssetDatabase.Saves == 1
              && GetReferences(table).Count == 2
              && UnityEngine.Debug.Errors.Exists(error => error.Contains("duplicate ItemId")),
            "A duplicate must abort refresh without changing existing references.");

        SetField(second, "definition", new ItemDefinition { ItemId = "second" });
        SetField(table, "itemAssets", new List<ItemDefinitionAsset>
        {
            MakeAsset("outside")
        });
        Process(refresh: true);
        Check(UnityEditor.AssetDatabase.Saves == 1
              && GetReferences(table).Count == 1
              && UnityEngine.Debug.Errors.Exists(error => error.Contains("outside Assets")),
            "An existing external reference must not be silently removed.");

        TestRegisterOne();
        TestPublishEquipment();
    }

    private static void TestRegisterOne()
    {
        UnityEditor.AssetDatabase.Reset();
        var table = new ItemDefinitionTable();
        SetField(table, "items", new List<ItemDefinition>
        {
            new ItemDefinition { ItemId = "legacy" }
        });
        UnityEditor.AssetDatabase.Register(
            "Assets/Data/Items/ItemDefinitionTable.asset", table);
        var first = MakeAsset("first");
        var second = MakeAsset("weapon_xingyun_001_item");
        UnityEditor.AssetDatabase.Register("Assets/Items/First.asset", first);
        UnityEditor.AssetDatabase.Register("Assets/Items/Second.asset", second);
        SetField(table, "itemAssets", new List<ItemDefinitionAsset> { first });

        var inspector = new ItemDefinitionAssetInspector
        {
            target = second,
            serializedObject = new UnityEditor.SerializedObject(second)
        };
        UnityEngine.GUILayout.ClickNextButton = true;
        inspector.OnInspectorGUI();
        Check(GetReferences(table).Count == 2
              && ReferenceEquals(GetReferences(table)[1], second)
              && UnityEditor.AssetDatabase.Saves == 2,
            "Inspector button must save only the selected asset and catalog.");
        Check(UnityEditor.EditorUtility.LastDialogMessage.Contains("weapon_xingyun_001_item"),
            "Successful registration must show the registered ID.");

        UnityEngine.GUILayout.ClickNextButton = true;
        inspector.OnInspectorGUI();
        Check(GetReferences(table).Count == 2 && UnityEditor.AssetDatabase.Saves == 3,
            "A second click may save the asset but must not add or save the table twice.");

        var duplicate = MakeAsset("legacy");
        UnityEditor.AssetDatabase.Register("Assets/Items/Duplicate.asset", duplicate);
        UnityEngine.GUILayout.ClickNextButton = true;
        new ItemDefinitionAssetInspector
        {
            target = duplicate,
            serializedObject = new UnityEditor.SerializedObject(duplicate)
        }.OnInspectorGUI();
        Check(GetReferences(table).Count == 2 && UnityEditor.AssetDatabase.Saves == 3
              && UnityEditor.EditorUtility.LastDialogMessage.Contains("duplicate ItemId"),
            "Duplicate IDs must not change the catalog.");

        var empty = MakeAsset("");
        UnityEditor.AssetDatabase.Register("Assets/Items/Empty.asset", empty);
        UnityEngine.GUILayout.ClickNextButton = true;
        new ItemDefinitionAssetInspector
        {
            target = empty,
            serializedObject = new UnityEditor.SerializedObject(empty)
        }.OnInspectorGUI();
        Check(GetReferences(table).Count == 2 && UnityEditor.AssetDatabase.Saves == 3
              && UnityEditor.EditorUtility.LastDialogMessage.Contains("ItemId is empty"),
            "Empty IDs must not change the catalog.");
    }

    private static void TestPublishEquipment()
    {
        UnityEditor.AssetDatabase.Reset();
        var table = new ItemDefinitionTable();
        UnityEditor.AssetDatabase.Register(
            "Assets/Data/Items/ItemDefinitionTable.asset", table);
        var oldEquipment = MakeAsset("old_equipment_item");
        oldEquipment.Definition.Category = ItemCategory.Equipment;
        SetField(table, "itemAssets", new List<ItemDefinitionAsset> { oldEquipment });
        UnityEditor.AssetDatabase.Register("Assets/Items/Old.asset", oldEquipment);

        var weapon = MakeAsset("weapon_item");
        weapon.Definition.Category = ItemCategory.Equipment;
        weapon.Definition.EquipmentId = "weapon";
        weapon.Definition.EquipmentCategory = EquipmentCategoryCode.Weapon;
        weapon.Definition.Icon = new UnityEngine.Sprite();
        UnityEditor.AssetDatabase.Register("Assets/Items/Weapon.asset", weapon);

        Check(ItemCatalogEditor.TryRegister(weapon, out string message)
              && GetReferences(table).Count == 2
              && table.TryGetByEquipmentId("weapon", out ItemDefinition found)
              && ReferenceEquals(found, weapon.Definition)
              && UnityEditor.AssetDatabase.Saves == 2,
            "A complete equipment definition must publish despite unmigrated entries.");
        Check(ItemCatalogEditor.TryRegister(weapon, out message)
              && GetReferences(table).Count == 2
              && UnityEditor.AssetDatabase.Saves == 3,
            "Publishing an existing asset must not add references or save the table.");
        Process(refresh: true);
        Check(GetReferences(table).Count == 2
              && UnityEditor.AssetDatabase.Saves == 3,
            "Refresh must tolerate already-published unmigrated equipment.");

        var duplicate = MakeAsset("other_weapon_item");
        duplicate.Definition.Category = ItemCategory.Equipment;
        duplicate.Definition.EquipmentId = "weapon";
        duplicate.Definition.EquipmentCategory = EquipmentCategoryCode.Weapon;
        duplicate.Definition.Icon = new UnityEngine.Sprite();
        UnityEditor.AssetDatabase.Register("Assets/Items/DuplicateEquipment.asset", duplicate);
        Check(!ItemCatalogEditor.TryRegister(duplicate, out message)
              && message.Contains("duplicate EquipmentId")
              && GetReferences(table).Count == 2
              && UnityEditor.AssetDatabase.Saves == 3,
            "Duplicate equipment IDs must not change the published references.");

        weapon.Definition.EquipmentId = "new_weapon";
        Check(ItemCatalogEditor.TryRegister(weapon, out message)
              && !table.TryGetByEquipmentId("weapon", out found)
              && table.TryGetByEquipmentId("new_weapon", out found)
              && ReferenceEquals(found, weapon.Definition)
              && GetReferences(table).Count == 2,
            "Republishing a changed definition must refresh the equipment lookup.");

        var incomplete = MakeAsset("missing_equipment");
        incomplete.Definition.Category = ItemCategory.Equipment;
        UnityEditor.AssetDatabase.Register("Assets/Items/Incomplete.asset", incomplete);
        Check(!ItemCatalogEditor.TryRegister(incomplete, out message)
              && message.Contains("EquipmentId is empty")
              && GetReferences(table).Count == 2,
            "Publishing incomplete equipment must leave the table unchanged.");
    }

    private static ItemDefinitionAsset MakeAsset(string id)
    {
        var asset = new ItemDefinitionAsset();
        SetField(asset, "definition", new ItemDefinition { ItemId = id });
        return asset;
    }

    private static void SetField(object target, string name, object value)
    {
        target.GetType().GetField(name,
            BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }

    private static List<ItemDefinitionAsset> GetReferences(ItemDefinitionTable table)
    {
        return (List<ItemDefinitionAsset>)typeof(ItemDefinitionTable)
            .GetField("itemAssets", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(table);
    }

    private static void Process(bool refresh)
    {
        typeof(ItemCatalogEditor).GetMethod("Process",
            BindingFlags.Static | BindingFlags.NonPublic)
            .Invoke(null, new object[] { refresh });
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}

namespace UnityEditor
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class CustomEditor : Attribute
    {
        public CustomEditor(Type type) { }
    }

    public class Editor
    {
        public SerializedObject serializedObject;
        public object target;
        public virtual void OnInspectorGUI() { }
        protected bool DrawDefaultInspector() { return true; }
    }

    public static class EditorGUI
    {
        public sealed class DisabledScope : IDisposable
        {
            public DisabledScope(bool disabled) { }
            public void Dispose() { }
        }

        public static void BeginChangeCheck() { }
        public static bool EndChangeCheck() { return false; }
    }

    public static class EditorStyles
    {
        public static readonly object boldLabel = new object();
    }

    public static class EditorGUILayout
    {
        public static readonly List<string> Labels = new List<string>();
        public static void PropertyField(SerializedProperty property,
            UnityEngine.GUIContent label, bool includeChildren = false)
        {
            Labels.Add(label.text);
        }
        public static int Popup(UnityEngine.GUIContent label,
            int selectedIndex, string[] displayedOptions)
        {
            Labels.Add(label.text);
            return selectedIndex;
        }
        public static void LabelField(string text, object style)
        {
            Labels.Add(text);
        }
        public static void Space() { }
    }

    public static class Undo
    {
        public static void RecordObject(object asset, string action) { }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class MenuItem : Attribute
    {
        public MenuItem(string name) { }
    }

    public static class EditorUtility
    {
        public static string LastDialogMessage;
        public static bool DisplayDialog(string title, string message, string ok)
        {
            LastDialogMessage = message;
            return true;
        }
    }

    public static class AssetDatabase
    {
        private static readonly Dictionary<string, object> Assets =
            new Dictionary<string, object>();
        public static int Saves { get; private set; }

        public static void Reset()
        {
            Assets.Clear();
            Saves = 0;
        }

        public static void Register(string path, object asset)
        {
            Assets[path] = asset;
        }

        public static T LoadAssetAtPath<T>(string path) where T : class
        {
            return Assets.TryGetValue(path, out object asset) ? asset as T : null;
        }

        public static string[] FindAssets(string filter, string[] folders)
        {
            var paths = new List<string>();
            foreach (KeyValuePair<string, object> entry in Assets)
                if (entry.Value is ItemDefinitionAsset) paths.Add(entry.Key);
            paths.Reverse();
            return paths.ToArray();
        }

        public static string GUIDToAssetPath(string guid) { return guid; }

        public static string GetAssetPath(object asset)
        {
            foreach (KeyValuePair<string, object> entry in Assets)
                if (ReferenceEquals(entry.Value, asset)) return entry.Key;
            return "Packages/Outside.asset";
        }

        public static void SaveAssetIfDirty(object asset) { Saves++; }
    }

    public sealed class SerializedObject
    {
        private readonly ItemDefinitionTable table;
        private readonly ItemDefinitionAsset itemAsset;
        private readonly List<ItemDefinition> legacy;
        private readonly List<ItemDefinitionAsset> references;

        public SerializedObject(ItemDefinitionAsset itemAsset)
        {
            this.itemAsset = itemAsset;
        }

        public SerializedObject(ItemDefinitionTable table)
        {
            this.table = table;
            legacy = (List<ItemDefinition>)typeof(ItemDefinitionTable)
                .GetField("items", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(table);
            references = new List<ItemDefinitionAsset>(
                (List<ItemDefinitionAsset>)typeof(ItemDefinitionTable)
                    .GetField("itemAssets", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(table));
        }

        public void Update() { }

        public SerializedProperty FindProperty(string name)
        {
            if (itemAsset != null)
                return name == "m_Script" || name == "definition"
                    ? new SerializedProperty(null, null, name, -1) : null;
            return name == "items" || name == "itemAssets"
                ? new SerializedProperty(legacy, references, name, -1)
                : null;
        }

        public bool ApplyModifiedProperties()
        {
            if (itemAsset != null) return false;
            typeof(ItemDefinitionTable).GetField("itemAssets",
                BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(table, references);
            return true;
        }
    }

    public sealed class SerializedProperty
    {
        private readonly List<ItemDefinition> legacy;
        private readonly List<ItemDefinitionAsset> references;
        private readonly string kind;
        private readonly int index;

        public SerializedProperty(List<ItemDefinition> legacy,
            List<ItemDefinitionAsset> references, string kind, int index)
        {
            this.legacy = legacy;
            this.references = references;
            this.kind = kind;
            this.index = index;
        }

        public int arraySize
        {
            get { return kind == "items" ? legacy.Count : references.Count; }
            set
            {
                if (kind != "itemAssets") throw new Exception("Not an asset array.");
                while (references.Count < value) references.Add(null);
                while (references.Count > value) references.RemoveAt(references.Count - 1);
            }
        }

        public SerializedProperty GetArrayElementAtIndex(int at)
        {
            return new SerializedProperty(legacy, references, kind, at);
        }

        public SerializedProperty FindPropertyRelative(string name)
        {
            if (kind == "definition"
                && typeof(ItemDefinition).GetField(name) != null)
                return new SerializedProperty(legacy, references, name, -1);
            return kind == "items" && name == "ItemId"
                ? new SerializedProperty(legacy, references, name, index) : null;
        }

        public string stringValue
        {
            get { return kind == "ItemId" ? legacy[index]?.ItemId : null; }
            set
            {
                if (kind == "ItemId" && legacy[index] != null)
                    legacy[index].ItemId = value;
            }
        }

        public object objectReferenceValue
        {
            get { return kind == "itemAssets" ? references[index] : null; }
            set
            {
                if (kind != "itemAssets") throw new Exception("Not an asset reference.");
                references[index] = (ItemDefinitionAsset)value;
            }
        }
    }
}
