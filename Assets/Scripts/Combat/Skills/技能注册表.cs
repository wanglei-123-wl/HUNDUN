using System;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class 技能注册表 : MonoBehaviour
{
    [Header("技能注册")]
    [SerializeField] private List<技能释放配置> 技能列表 =
        new List<技能释放配置>();

    private readonly Dictionary<string, 技能释放配置> 配置索引 =
        new Dictionary<string, 技能释放配置>(StringComparer.Ordinal);

    private void Awake()
    {
        确保默认技能();
        重建索引();
    }

    private void OnValidate()
    {
        确保默认技能();
        重建索引();
    }

    public bool TryGet(string skillId, out 技能释放配置 配置)
    {
        配置 = null;
        if (string.IsNullOrWhiteSpace(skillId))
        {
            return false;
        }

        return 配置索引.TryGetValue(skillId.Trim(), out 配置)
            && 配置 != null
            && 配置.IsValid;
    }

    public bool IsRegisteredAndValid(string skillId)
    {
        return TryGet(skillId, out _);
    }

    public string[] 获取有效技能ID()
    {
        var skillIds = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (技能列表 == null)
        {
            return skillIds.ToArray();
        }

        for (int index = 0; index < 技能列表.Count; index++)
        {
            技能释放配置 config = 技能列表[index];
            if (config == null || !config.IsValid
                || string.IsNullOrWhiteSpace(config.技能ID))
            {
                continue;
            }

            string skillId = config.技能ID.Trim();
            if (seen.Add(skillId))
                skillIds.Add(skillId);
        }

        return skillIds.ToArray();
    }

    private void 确保默认技能()
    {
        if (技能列表 == null)
        {
            技能列表 = new List<技能释放配置>();
        }

        添加默认技能("slash_01", "斩灵术",
            技能释放方式.玩家释放节点);
        添加默认技能("fireball_01", "爆破术",
            技能释放方式.怪物目标位置);
    }

    private void 添加默认技能(
        string skillId, string skillName, 技能释放方式 releaseType)
    {
        for (int index = 0; index < 技能列表.Count; index++)
        {
            技能释放配置 existing = 技能列表[index];
            if (existing != null
                && string.Equals(existing.技能ID?.Trim(), skillId,
                    StringComparison.Ordinal))
            {
                return;
            }
        }

        技能列表.Add(new 技能释放配置
        {
            技能ID = skillId,
            技能名称 = skillName,
            释放方式 = releaseType,
            目标类型 = "monster",
            需要目标 = true
        });
    }

    private void 重建索引()
    {
        配置索引.Clear();
        if (技能列表 == null)
        {
            return;
        }

        for (int index = 0; index < 技能列表.Count; index++)
        {
            技能释放配置 配置 = 技能列表[index];
            if (配置 == null || string.IsNullOrWhiteSpace(配置.技能ID))
            {
                continue;
            }

            string skillId = 配置.技能ID.Trim();
            if (配置索引.ContainsKey(skillId))
            {
                Debug.LogWarning(
                    "[SkillRegistry] 重复技能ID，后面的配置未注册: " + skillId,
                    this);
                continue;
            }

            配置索引.Add(skillId, 配置);
        }
    }
}
