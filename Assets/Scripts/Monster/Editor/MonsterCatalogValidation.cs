using System;
using System.Collections.Generic;
using Game.Monsters.Config;
using UnityEditor;
using UnityEngine;

namespace Game.Monsters.Editor
{
    internal static class MonsterCatalogValidation
    {
        internal static bool TryValidate(
            IReadOnlyList<MonsterDefinitionAsset> assets, out string report)
        {
            var errors = new List<string>();
            var ids = new Dictionary<string, string>(StringComparer.Ordinal);
            var seen = new HashSet<MonsterDefinitionAsset>();

            if (assets == null)
            {
                report = "怪物总表没有配置列表。";
                return false;
            }

            for (int index = 0; index < assets.Count; index++)
            {
                MonsterDefinitionAsset asset = assets[index];
                if (asset == null)
                {
                    errors.Add("怪物配置[" + index + "]为空。");
                    continue;
                }

                string path = AssetDatabase.GetAssetPath(asset);
                if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal))
                    errors.Add(asset.name + "：请先将怪物配置保存到项目 Assets 中。");
                if (!seen.Add(asset))
                    errors.Add(asset.name + "：同一个怪物配置重复发布。");

                string id = asset.Id;
                if (string.IsNullOrWhiteSpace(id))
                    errors.Add(asset.name + "：怪物ID不能为空。");
                else if (!string.Equals(id, id.Trim(), StringComparison.Ordinal))
                    errors.Add(asset.name + "：怪物ID首尾不能有空格。");
                else if (ids.TryGetValue(id, out string firstPath))
                    errors.Add(asset.name + "：怪物ID '" + id + "' 重复，已存在于 " + firstPath + "。");
                else
                    ids.Add(id, path);

                if (string.IsNullOrWhiteSpace(asset.DisplayName))
                    errors.Add(asset.name + "：显示名称不能为空。");
                if (asset.Prefab == null)
                {
                    errors.Add(asset.name + "：怪物预制体不能为空。");
                    continue;
                }

                if (!PrefabUtility.IsPartOfPrefabAsset(asset.Prefab))
                {
                    errors.Add(asset.name + "：怪物预制体必须是项目内的预制体资源。");
                    continue;
                }

                GameObject prefab = asset.Prefab;
                if (prefab.GetComponent<MonsterNetworkView>() == null)
                    errors.Add(asset.name + "：预制体根节点缺少 MonsterNetworkView。");
                if (prefab.GetComponent<MonsterViewRoot>() == null)
                    errors.Add(asset.name + "：预制体根节点缺少 MonsterViewRoot。");
                if (prefab.GetComponent<MonsterTransformInterpolator>() == null)
                    errors.Add(asset.name + "：预制体根节点缺少 MonsterTransformInterpolator。");

                bool hasDriver = false;
                foreach (MonoBehaviour component in prefab.GetComponentsInChildren<MonoBehaviour>(true))
                {
                    if (component is IMonsterAnimatorDriver)
                    {
                        hasDriver = true;
                        break;
                    }
                }
                if (!hasDriver)
                    errors.Add(asset.name + "：预制体缺少怪物动画驱动组件。");
            }

            report = string.Join("\n", errors);
            return errors.Count == 0;
        }
    }
}
