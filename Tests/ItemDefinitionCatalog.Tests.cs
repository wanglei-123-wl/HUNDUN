using System;
using System.Collections.Generic;
using System.Reflection;
using Game.Items;
using Game.Items.Editor;

internal static class ItemDefinitionCatalogTests
{
    private static void Main()
    {
        var legacy = new ItemDefinition
        {
            ItemId = "hp_potion_01",
            DisplayName = "Legacy potion",
            MaxStack = 0
        };
        var newItem = new ItemDefinition
        {
            ItemId = "new_material",
            DisplayName = "New material",
            Description = "Test description"
        };
        var duplicate = new ItemDefinition { ItemId = legacy.ItemId };
        var newAsset = new ItemDefinitionAsset();
        var duplicateAsset = new ItemDefinitionAsset();
        SetField(newAsset, "definition", newItem);
        SetField(duplicateAsset, "definition", duplicate);

        var fallback = new ItemDefinition { ItemId = "fallback" };
        var table = new ItemDefinitionTable();
        SetField(table, "defaultConfig", fallback);
        SetField(table, "items", new List<ItemDefinition> { legacy });
        SetField(table, "itemAssets", new List<ItemDefinitionAsset>
        {
            null, newAsset, duplicateAsset
        });
        Invoke(table, "OnEnable");
        Check(table.EquipmentCatalogVersion == 2,
            "The shared item table must preserve the V2 equipment catalog version.");

        Check(table.TryGet(legacy.ItemId, out ItemDefinition found)
              && ReferenceEquals(found, legacy),
            "Existing entries must retain their lookup priority.");
        Check(legacy.MaxStack == 1, "Legacy stack normalization must remain intact.");
        Check(table.TryGet(newItem.ItemId, out found)
              && ReferenceEquals(found, newItem)
              && found.Description == "Test description",
            "New assets must expose their full definition through the old API.");
        Check(table.Contains(newItem.ItemId), "Contains must include referenced assets.");
        Check(!table.TryGet("missing", out found)
              && ReferenceEquals(found, fallback)
              && ReferenceEquals(table.GetOrDefault("missing"), fallback),
            "Unknown IDs must retain the old fallback behavior.");
        Check(UnityEngine.Debug.Errors.Count == 1
              && UnityEngine.Debug.Errors[0].Contains(legacy.ItemId),
            "Duplicate IDs must be reported without replacing the old entry.");

        var firstEquipment = new ItemDefinition
        {
            ItemId = "weapon_item",
            EquipmentId = "weapon",
            EquipmentCategory = EquipmentCategoryCode.Weapon,
            Category = ItemCategory.Equipment
        };
        var duplicateEquipment = new ItemDefinition
        {
            ItemId = "other_weapon_item",
            EquipmentId = "weapon",
            EquipmentCategory = EquipmentCategoryCode.Weapon,
            Category = ItemCategory.Equipment
        };
        SetField(table, "itemAssets", new List<ItemDefinitionAsset>
        {
            MakeAsset(firstEquipment), MakeAsset(duplicateEquipment)
        });
        UnityEngine.Debug.Errors.Clear();
        table.RefreshLookup();
        Check(table.TryGetByEquipmentId("weapon", out ItemDefinition equipmentFound)
              && ReferenceEquals(equipmentFound, firstEquipment)
              && table.TryGet("weapon_item", out found)
              && ReferenceEquals(found, equipmentFound),
            "Item and equipment IDs must resolve to the same definition.");
        Check(!table.TryGetByEquipmentId("missing", out equipmentFound)
              && equipmentFound == null
              && !table.Contains("other_weapon_item")
              && UnityEngine.Debug.Errors.Count == 1
              && UnityEngine.Debug.Errors[0].Contains("Duplicate equipment definition"),
            "Duplicate equipment IDs must not create partial entries or use the fallback.");

        SetField(newAsset, "definition", new ItemDefinition { ItemId = "updated_material" });
        SetField(table, "itemAssets", new List<ItemDefinitionAsset> { newAsset });
        Invoke(table, "OnValidate");
        Check(!table.Contains(newItem.ItemId) && table.Contains("updated_material"),
            "Editor validation must rebuild the lookup after a definition changes.");

        Check(ItemCatalogValidation.TryValidate(new[]
        {
            new ItemCatalogId("hp_potion_01", "legacy"),
            new ItemCatalogId("new_material", "asset")
        }, out string report), "Distinct IDs must validate.");
        Check(!ItemCatalogValidation.TryValidate(new[]
        {
            new ItemCatalogId("hp_potion_01", "legacy"),
            new ItemCatalogId("hp_potion_01", "duplicate asset"),
            new ItemCatalogId("", "empty asset"),
            new ItemCatalogId(" padded ", "spaced asset")
        }, out report) && report.Contains("duplicate asset")
                       && report.Contains("empty asset")
                       && report.Contains("spaced asset"),
            "Refresh must reject duplicate, empty and padded IDs.");

        var icon = new UnityEngine.Sprite();
        var sword = new ItemDefinition
        {
            ItemId = "sword_item", EquipmentId = "sword",
            Category = ItemCategory.Equipment,
            EquipmentCategory = EquipmentCategoryCode.Weapon, Icon = icon
        };
        var helmet = new ItemDefinition
        {
            ItemId = "helmet_item", EquipmentId = "helmet",
            Category = ItemCategory.Equipment,
            EquipmentCategory = EquipmentCategoryCode.Helmet, Icon = icon
        };
        Check(ItemCatalogValidation.TryValidate(new[]
        {
            new ItemCatalogId(sword, "sword"),
            new ItemCatalogId(helmet, "helmet")
        }, out report), "Distinct complete equipment definitions must validate.");
        var boots = new ItemDefinition
        {
            ItemId = "boots_item", EquipmentId = "boots",
            Category = ItemCategory.Equipment,
            EquipmentCategory = EquipmentCategoryCode.Boots, Icon = icon
        };
        Check(ItemCatalogValidation.TryValidate(new[]
        {
            new ItemCatalogId(boots, "boots")
        }, out report), "Boots must be a valid equipment registration category.");
        foreach (EquipmentCategoryCode category in new[]
            { EquipmentCategoryCode.Shield, EquipmentCategoryCode.Artifact,
                EquipmentCategoryCode.Formation })
        {
            boots.EquipmentCategory = category;
            Check(ItemCatalogValidation.TryValidate(new[]
            {
                new ItemCatalogId(boots, category.ToString())
            }, out report), "New equipment categories must validate.");
        }
        boots.EquipmentCategory = (EquipmentCategoryCode)11;
        Check(!ItemCatalogValidation.TryValidate(new[]
        {
            new ItemCatalogId(boots, "invalid boots")
        }, out report) && report.Contains("category is missing"),
            "Categories outside the registered equipment options must be rejected.");
        var remoteEquipment = new ItemDefinition
        {
            ItemId = "remote_weapon", EquipmentId = "remote",
            Category = ItemCategory.Equipment,
            EquipmentCategory = EquipmentCategoryCode.Weapon,
            RemoteIconAddress = "item/icon/remote_weapon"
        };
        Check(ItemCatalogValidation.TryValidate(new[]
        {
            new ItemCatalogId(remoteEquipment, "remote")
        }, out report), "Remote-only equipment must validate without a local Sprite.");
        remoteEquipment.Icon = icon;
        Check(!ItemCatalogValidation.TryValidate(new[]
        {
            new ItemCatalogId(remoteEquipment, "remote")
        }, out report) && report.Contains("remote icon must not also reference"),
            "The same icon cannot be assigned both locally and remotely.");
        remoteEquipment.Icon = null;
        remoteEquipment.RemoteIconAddress = "item/icon/other";
        Check(!ItemCatalogValidation.TryValidate(new[]
        {
            new ItemCatalogId(remoteEquipment, "remote")
        }, out report) && report.Contains("remote icon address does not match"),
            "Remote icon address must resolve from the exact item ID.");
        helmet.EquipmentId = "sword";
        Check(!ItemCatalogValidation.TryValidate(new[]
        {
            new ItemCatalogId(sword, "sword"),
            new ItemCatalogId(helmet, "helmet")
        }, out report) && report.Contains("duplicate EquipmentId"),
            "Different item IDs must not share an equipment ID.");
        helmet.EquipmentId = " helmet ";
        Check(!ItemCatalogValidation.TryValidate(new[]
        {
            new ItemCatalogId(helmet, "helmet")
        }, out report) && report.Contains("leading or trailing spaces"),
            "Padded equipment IDs must be rejected.");
        helmet.EquipmentId = "";
        Check(!ItemCatalogValidation.TryValidate(new[]
        {
            new ItemCatalogId(helmet, "helmet")
        }, out report) && report.Contains("EquipmentId is empty"),
            "New equipment must have an explicit equipment ID.");
        Check(ItemCatalogValidation.TryValidate(new[]
        {
            new ItemCatalogId(helmet, "previously published",
                allowUnmigratedEquipment: true)
        }, out report), "Previously published equipment can remain until migration.");
        helmet.EquipmentId = "helmet";
        helmet.EquipmentCategory = EquipmentCategoryCode.None;
        helmet.Icon = null;
        Check(!ItemCatalogValidation.TryValidate(new[]
        {
            new ItemCatalogId(helmet, "helmet")
        }, out report) && report.Contains("category is missing")
                       && report.Contains("icon is missing"),
            "New equipment must have a valid category and icon.");
        var invalidOrdinary = new ItemDefinition
        {
            ItemId = "potion", EquipmentId = "weapon",
            EquipmentCategory = EquipmentCategoryCode.Weapon
        };
        Check(!ItemCatalogValidation.TryValidate(new[]
        {
            new ItemCatalogId(invalidOrdinary, "potion")
        }, out report) && report.Contains("only equipment"),
            "Ordinary items must not claim an equipment mapping.");

        var skillBook = new ItemDefinition
        {
            ItemId = "book_fireball_01",
            Category = ItemCategory.SkillBook,
            LinkedSkillId = "fireball_01"
        };
        Check(ItemCatalogValidation.TryValidate(new[]
        {
            new ItemCatalogId(skillBook, "fireball book")
        }, out report), "A skill book with a linked skill must validate.");
        skillBook.LinkedSkillId = "";
        Check(!ItemCatalogValidation.TryValidate(new[]
        {
            new ItemCatalogId(skillBook, "missing linked skill")
        }, out report) && report.Contains("LinkedSkillId is empty"),
            "A skill book must provide a linked skill ID.");
        skillBook.LinkedSkillId = " fireball_01 ";
        Check(!ItemCatalogValidation.TryValidate(new[]
        {
            new ItemCatalogId(skillBook, "spaced linked skill")
        }, out report) && report.Contains("LinkedSkillId has leading or trailing spaces"),
            "A linked skill ID with padding must be rejected.");
        var ordinaryWithSkill = new ItemDefinition
        {
            ItemId = "ordinary_with_skill",
            LinkedSkillId = "fireball_01"
        };
        Check(!ItemCatalogValidation.TryValidate(new[]
        {
            new ItemCatalogId(ordinaryWithSkill, "ordinary item")
        }, out report) && report.Contains("only skill books"),
            "Only skill books may set a linked skill ID.");
        Check(!ItemCatalogValidation.TryValidate(new[]
        {
            new ItemCatalogId(new ItemDefinition
            {
                ItemId = "unknown_skill_book",
                Category = ItemCategory.SkillBook,
                LinkedSkillId = "missing_skill"
            }, "unknown skill book")
        }, id => false, out report)
        && report.Contains("not registered or invalid"),
            "A skill book must not pass when its skill is not registered.");

        var blueprint = new ItemDefinition
        {
            ItemId = "tz001",
            Category = ItemCategory.ForgingBlueprint
        };
        Check(ItemCatalogValidation.TryValidate(new[]
        {
            new ItemCatalogId(blueprint, "forging blueprint")
        }, out report), "A forging blueprint must not require a linked skill.");
        Check(ItemDetailCategoryResolver.Resolve(blueprint)
                == ItemDetailCategory.ForgingBlueprint
            && ItemDetailCategoryResolver.GetLabel(
                ItemDetailCategory.ForgingBlueprint) == "锻造图纸",
            "A forging blueprint must have its own detail label.");

        ItemCatalogEditorTests.Run();
        ItemDefinitionAssetInspectorTests.Run();
        ItemPresentationIntegrationTests.Run();
        Console.WriteLine("PASS: legacy and asset definitions, duplicates, fallback, rebuild, editor validation.");
    }

    private static void SetField(object target, string name, object value)
    {
        FieldInfo field = target.GetType().GetField(name,
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null) throw new Exception("Missing test field: " + name);
        field.SetValue(target, value);
    }

    private static ItemDefinitionAsset MakeAsset(ItemDefinition definition)
    {
        var asset = new ItemDefinitionAsset();
        SetField(asset, "definition", definition);
        return asset;
    }

    private static void Invoke(object target, string name)
    {
        typeof(ItemDefinitionTable).GetMethod(name,
            BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(target, null);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}

namespace UnityEngine
{
    public class Object
    {
        public static T[] FindObjectsOfType<T>(bool includeInactive)
        {
            return Array.Empty<T>();
        }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class CreateAssetMenuAttribute : Attribute
    {
        public string fileName;
        public string menuName;
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SerializeField : Attribute { }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class TextAreaAttribute : Attribute
    {
        public TextAreaAttribute() { }
        public TextAreaAttribute(int minLines, int maxLines) { }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class MinAttribute : Attribute
    {
        public MinAttribute(float value) { }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class InspectorNameAttribute : Attribute
    {
        public string DisplayName { get; }
        public InspectorNameAttribute(string displayName) { DisplayName = displayName; }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class DisallowMultipleComponent : Attribute { }

    public class ScriptableObject : Object { }
    public class MonoBehaviour : Object
    {
        protected T GetComponent<T>() where T : class { return null; }
        protected static T Instantiate<T>(T original, object parent, bool worldPositionStays)
        {
            return original;
        }
    }
    public class RectTransform { }
    public class Sprite { }
    public sealed class GUIContent
    {
        public string text;
        public string tooltip;
        public GUIContent(string text, string tooltip = null)
        {
            this.text = text;
            this.tooltip = tooltip;
        }
    }
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Method)]
    public sealed class HeaderAttribute : Attribute
    {
        public HeaderAttribute(string header) { }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Method)]
    public sealed class TooltipAttribute : Attribute
    {
        public TooltipAttribute(string tooltip) { }
    }
    public class GameObject
    {
        public void SetActive(bool value) { }
    }
    public class AudioClip { }
    public static class GUILayout
    {
        public static bool ClickNextButton;
        public static bool Button(string label)
        {
            bool clicked = ClickNextButton;
            ClickNextButton = false;
            return clicked;
        }
    }

    public struct Color
    {
        public static Color white => new Color();
    }

    public static class Debug
    {
        public static readonly List<string> Errors = new List<string>();
        public static readonly List<string> Messages = new List<string>();
        public static void LogError(string message) { Errors.Add(message); }
        public static void LogError(string message, object context)
        {
            Errors.Add(message);
        }
        public static void Log(string message, object context)
        {
            Messages.Add(message);
        }
        public static void LogWarning(string message)
        {
            Messages.Add(message);
        }
        public static void LogWarning(string message, object context)
        {
            Messages.Add(message);
        }
    }
}

namespace Game.Items
{
    internal static class RemoteItemIconCache
    {
        public static string AddressFor(string itemId)
        {
            return "item/icon/" + itemId;
        }

        public static bool TryGetIcon(string itemId, out UnityEngine.Sprite icon)
        {
            icon = null;
            return false;
        }
    }
}
