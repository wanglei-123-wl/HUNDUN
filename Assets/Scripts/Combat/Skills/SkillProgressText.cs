using System.Globalization;
using System.Text;

internal static class SkillProgressText
{
    private const int BattleTicksPerSecond = 20;

    internal static string FormatLevel(SkillSnapshotEntry skill)
    {
        if (skill == null || skill.skillLevel <= 0)
            return string.Empty;

        int maxLevel = skill.progress?.maxLevel ?? 0;
        return maxLevel > 0 ? $"等级 {skill.skillLevel}/{maxLevel}" : $"等级 {skill.skillLevel}";
    }

    internal static string FormatDetails(SkillProgressData progress, string description)
    {
        if (progress == null)
            return description ?? string.Empty;

        var text = new StringBuilder();
        if (progress.attributes == null)
            text.Append("技能配置不可用，当前不可释放");
        else
            AppendAttributes(text, progress.attributes);

        if (!string.IsNullOrWhiteSpace(description))
            text.Append("\n\n").Append(description);
        return text.ToString();
    }

    private static void AppendAttributes(StringBuilder text, SkillProgressAttributes attributes)
    {
        string target = attributes.targetType switch
        {
            "enemy_single" => "单个敌人",
            "enemy_area" => "范围内敌人",
            "ally_or_self_single" => "自身或单个友方",
            "self" => "自身",
            _ => null
        };
        string effect = attributes.effectType switch
        {
            "damage" => "伤害",
            "heal" => "治疗",
            "damage_and_buff" => "伤害与状态效果",
            "area_damage_over_time" => "范围持续伤害",
            _ => null
        };
        if (target != null)
            AppendLine(text, "目标", target);
        if (effect != null)
            AppendLine(text, "效果", effect);

        if (attributes.effectType != "heal" && attributes.damageType == "magical")
            AppendLine(text, "伤害类型", "法术");
        else if (attributes.effectType != "heal" && attributes.damageType == "physical")
            AppendLine(text, "伤害类型", "物理");

        AppendLine(text, "法力消耗", attributes.mpCost.ToString());
        AppendLine(text, "冷却", Seconds(attributes.cooldownTicks));
        if (attributes.range > 0)
            AppendLine(text, "施法距离", Number(attributes.range) + " 米");
        if (attributes.hitDelayTicks > 0)
            AppendLine(text, "命中延迟", Seconds(attributes.hitDelayTicks));
        if (attributes.damageCoefficientPermille > 0)
            AppendLine(text, "伤害倍率", Number(attributes.damageCoefficientPermille / 1000d) + " 倍");
        if (attributes.flatDamage > 0)
            AppendLine(text, "固定伤害", attributes.flatDamage.ToString());
        if (attributes.healAmount > 0)
            AppendLine(text, "治疗量", attributes.healAmount.ToString());
        if (attributes.areaRadius > 0)
            AppendLine(text, "作用半径", Number(attributes.areaRadius) + " 米");
        if (attributes.durationTicks > 0)
            AppendLine(text, "持续时间", Seconds(attributes.durationTicks));
        if (attributes.tickIntervalTicks > 0)
            AppendLine(text, "结算间隔", Seconds(attributes.tickIntervalTicks));
        if (attributes.maxTargets > 0)
            AppendLine(text, "最大目标数", attributes.maxTargets.ToString());
        if (!string.IsNullOrWhiteSpace(attributes.buffId))
            AppendLine(text, "附加状态", attributes.buffId);
    }

    private static void AppendLine(StringBuilder text, string label, string value)
    {
        if (text.Length > 0)
            text.Append('\n');
        text.Append(label).Append(' ').Append(value);
    }

    private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Seconds(int ticks) => Number(ticks / (double)BattleTicksPerSecond) + " 秒";
}
