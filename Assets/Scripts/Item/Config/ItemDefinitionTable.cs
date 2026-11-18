using System;
using System.Collections.Generic;
using UnityEngine;

namespace Game.Items
{
    [CreateAssetMenu(
        fileName = "ItemDefinitionTable",
        menuName = "Game/Items/Item Definition Table")]
    public sealed class ItemDefinitionTable : ScriptableObject
    {
        [SerializeField] private int equipmentCatalogVersion = 4;
        [SerializeField] private ItemDefinition defaultConfig =
            new ItemDefinition();
        [SerializeField] private List<ItemDefinition> items =
            new List<ItemDefinition>();
        [SerializeField] private List<ItemDefinitionAsset> itemAssets =
            new List<ItemDefinitionAsset>();

        private readonly Dictionary<string, ItemDefinition> definitionsByItemId =
            new Dictionary<string, ItemDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, ItemDefinition> definitionsByEquipmentId =
            new Dictionary<string, ItemDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, ItemDefinition> skillBooksBySkillId =
            new Dictionary<string, ItemDefinition>(StringComparer.Ordinal);

        private bool lookupBuilt;
        private readonly Dictionary<string, ItemDefinition> remoteIconDefinitions =
            new Dictionary<string, ItemDefinition>(StringComparer.Ordinal);
#if UNITY_EDITOR
        private readonly HashSet<string> reportedMissingRemoteIcons =
            new HashSet<string>(StringComparer.Ordinal);
#endif

        public ItemDefinition DefaultDefinition => defaultConfig;
        public int EquipmentCatalogVersion => equipmentCatalogVersion;
        public IReadOnlyList<ItemDefinition> LegacyDefinitions => items;

        public List<string> GetRequiredRemoteIconAddresses()
        {
            EnsureLookup();
            var addresses = new HashSet<string>(StringComparer.Ordinal);
            foreach (ItemDefinition definition in definitionsByItemId.Values)
            {
                if (!string.IsNullOrEmpty(definition.RemoteIconAddress))
                    addresses.Add(definition.RemoteIconAddress);
            }
            var result = new List<string>(addresses);
            result.Sort(StringComparer.Ordinal);
            return result;
        }

        public bool AreRequiredRemoteIconsLoaded(out string missingItemId)
        {
            EnsureLookup();
            foreach (ItemDefinition definition in definitionsByItemId.Values)
            {
                if (!string.IsNullOrEmpty(definition.RemoteIconAddress)
                    && !RemoteItemIconCache.TryGetIcon(definition.ItemId, out _))
                {
                    missingItemId = definition.ItemId;
                    return false;
                }
            }
            missingItemId = null;
            return true;
        }

        public void RefreshLookup()
        {
            RebuildLookup();
        }

        public bool TryGet(string itemId, out ItemDefinition definition)
        {
            EnsureLookup();

            if (!string.IsNullOrWhiteSpace(itemId)
                && definitionsByItemId.TryGetValue(itemId, out definition))
            {
                definition = ResolveIcon(definition);
                return true;
            }

            definition = defaultConfig;
            return false;
        }

        public ItemDefinition GetOrDefault(string itemId)
        {
            TryGet(itemId, out ItemDefinition definition);
            return definition;
        }

        public bool TryGetSkillBook(string skillId, out ItemDefinition definition)
        {
            EnsureLookup();
            if (!string.IsNullOrWhiteSpace(skillId)
                && skillBooksBySkillId.TryGetValue(skillId, out definition))
            {
                definition = ResolveIcon(definition);
                return true;
            }

            definition = null;
            return false;
        }

        public bool TryGetDetail(string itemId, out ItemDetailData detail)
        {
            detail = null;
            if (!TryGet(itemId, out ItemDefinition definition))
                return false;

            detail = ItemDetailDataResolver.FromItem(definition);
            return detail != null && detail.IsValid;
        }

        public bool TryGetEquipmentDetail(
            string equipmentId,
            out ItemDetailData detail)
        {
            detail = null;
            if (!TryGetByEquipmentId(equipmentId,
                out ItemDefinition definition))
                return false;

            detail = ItemDetailDataResolver.FromItem(definition);
            return detail != null && detail.IsValid;
        }

        public bool Contains(string itemId)
        {
            EnsureLookup();
            return !string.IsNullOrWhiteSpace(itemId)
                && definitionsByItemId.ContainsKey(itemId);
        }

        public bool TryGetByEquipmentId(string equipmentId, out ItemDefinition definition)
        {
            EnsureLookup();
            if (!definitionsByEquipmentId.TryGetValue(equipmentId ?? string.Empty,
                out definition))
                return false;

            definition = ResolveIcon(definition);
            return true;
        }

        private ItemDefinition ResolveIcon(ItemDefinition source)
        {
            if (!RemoteItemIconCache.TryGetIcon(source.ItemId, out Sprite icon))
            {
#if UNITY_EDITOR
                if (!string.IsNullOrEmpty(source.RemoteIconAddress)
                    || (UnityEditor.SessionState.GetBool("Game.Items.StrictRemoteIconVerification", false)
                        && source.Icon != null))
                {
                    if (reportedMissingRemoteIcons.Add(source.ItemId))
                        Debug.LogError("[ResourceTrial] Remote icon missing for "
                            + source.ItemId + " (" + (source.RemoteIconAddress
                                ?? RemoteItemIconCache.AddressFor(source.ItemId))
                            + ")", this);
                    if (!remoteIconDefinitions.TryGetValue(source.ItemId, out ItemDefinition missing)
                        || missing.Icon != null)
                    {
                        missing = source.WithIcon(null);
                        remoteIconDefinitions[source.ItemId] = missing;
                    }
                    return missing;
                }
                reportedMissingRemoteIcons.Remove(source.ItemId);
#endif
                remoteIconDefinitions.Remove(source.ItemId);
                return source;
            }

#if UNITY_EDITOR
            reportedMissingRemoteIcons.Remove(source.ItemId);
#endif
            if (!remoteIconDefinitions.TryGetValue(source.ItemId, out ItemDefinition copy)
                || copy.Icon != icon)
            {
                copy = source.WithIcon(icon);
                remoteIconDefinitions[source.ItemId] = copy;
            }
            return copy;
        }

        private void OnEnable()
        {
            RebuildLookup();
        }

        private void OnValidate()
        {
            RebuildLookup();
        }

        private void EnsureLookup()
        {
            if (!lookupBuilt)
            {
                RebuildLookup();
            }
        }

        private void RebuildLookup()
        {
            definitionsByItemId.Clear();
            definitionsByEquipmentId.Clear();
            skillBooksBySkillId.Clear();
            remoteIconDefinitions.Clear();
#if UNITY_EDITOR
            reportedMissingRemoteIcons.Clear();
#endif

            if (items != null)
            {
                for (int index = 0; index < items.Count; index++)
                {
                    RegisterDefinition(items[index]);
                }
            }

            if (itemAssets != null)
            {
                for (int index = 0; index < itemAssets.Count; index++)
                {
                    ItemDefinitionAsset asset = itemAssets[index];
                    if (asset != null)
                    {
                        RegisterDefinition(asset.Definition);
                    }
                }
            }

            lookupBuilt = true;
        }

        private void RegisterDefinition(ItemDefinition definition)
        {
            if (definition == null || !definition.IsValid)
            {
                return;
            }

            if (definition.MaxStack <= 0)
            {
                definition.MaxStack = 1;
            }

            if (definitionsByItemId.ContainsKey(definition.ItemId))
            {
                Debug.LogError(
                    "Duplicate item definition: " + definition.ItemId,
                    this);
                return;
            }

            bool hasEquipmentId = definition.Category == ItemCategory.Equipment
                && !string.IsNullOrWhiteSpace(definition.EquipmentId);
            if (hasEquipmentId && definitionsByEquipmentId.ContainsKey(definition.EquipmentId))
            {
                Debug.LogError(
                    "Duplicate equipment definition: " + definition.EquipmentId,
                    this);
                return;
            }

            definitionsByItemId.Add(definition.ItemId, definition);
            if (hasEquipmentId)
                definitionsByEquipmentId.Add(definition.EquipmentId, definition);
            if (definition.Category == ItemCategory.SkillBook
                && !string.IsNullOrWhiteSpace(definition.LinkedSkillId))
                skillBooksBySkillId.TryAdd(definition.LinkedSkillId.Trim(), definition);
        }
    }
}
