using System;
using System.Collections.Generic;
using Game.Client.Vfx;
using UnityEngine;

namespace Game.Monsters.Config
{
    public sealed class MonsterCatalog : ScriptableObject
    {
        [SerializeField] private PlayerGroundRingEffect selectionRingPrefab;
        [SerializeField] private List<MonsterDefinitionAsset> monsterAssets =
            new List<MonsterDefinitionAsset>();

        private readonly Dictionary<string, MonsterDefinitionAsset> definitions =
            new Dictionary<string, MonsterDefinitionAsset>(StringComparer.Ordinal);
        private bool lookupBuilt;

        public PlayerGroundRingEffect SelectionRingPrefab => selectionRingPrefab;
        public IReadOnlyList<MonsterDefinitionAsset> MonsterAssets => monsterAssets;

        public bool TryGet(string id, out MonsterDefinitionAsset definition)
        {
            if (!lookupBuilt)
                RefreshLookup();

            return definitions.TryGetValue(id ?? string.Empty, out definition);
        }

        public void RefreshLookup()
        {
            definitions.Clear();
            if (monsterAssets != null)
            {
                foreach (MonsterDefinitionAsset asset in monsterAssets)
                {
                    if (asset == null || string.IsNullOrWhiteSpace(asset.Id) || asset.Prefab == null)
                        continue;

                    if (definitions.ContainsKey(asset.Id))
                    {
                        Debug.LogError("怪物总表存在重复怪物ID：" + asset.Id, this);
                        continue;
                    }

                    definitions.Add(asset.Id, asset);
                }
            }

            lookupBuilt = true;
        }

        private void OnEnable()
        {
            RefreshLookup();
        }

        private void OnValidate()
        {
            RefreshLookup();
        }
    }
}
