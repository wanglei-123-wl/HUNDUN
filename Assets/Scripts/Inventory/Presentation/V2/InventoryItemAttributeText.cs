using System.Globalization;
using System.Text;
using Game.Cultivation.Protocol;

namespace Game.Inventory.Presentation.V2
{
    internal static class InventoryItemAttributeText
    {
        internal static string Format(CultivationItemDefinitionDto item, string learningTarget = null)
        {
            var text = new StringBuilder();
            switch (item.kind)
            {
                case "equipment":
                    if (item.attributes != null)
                        foreach (CultivationItemAttributeDto attribute in item.attributes)
                            AppendAttribute(text, attribute);
                    break;
                case "consumable":
                case "pill":
                    if (item.effects != null)
                        foreach (CultivationItemEffectDto effect in item.effects)
                            AppendEffect(text, effect);
                    break;
                default:
                    if (!string.IsNullOrWhiteSpace(learningTarget))
                        AppendLine(text, "学习", learningTarget);
                    if (item.maxStack > 0)
                        AppendLine(text, "最大堆叠", Number(item.maxStack));
                    if (item.bindOnAcquire)
                        AppendLine(text, "绑定", "获取时绑定");
                    break;
            }
            return text.ToString();
        }

        private static void AppendAttribute(StringBuilder text, CultivationItemAttributeDto attribute)
        {
            if (attribute == null)
                return;

            string label = attribute.attributeId switch
            {
                "max_hp" => "生命上限",
                "max_mp" => "法力上限",
                "attack" => "物理攻击",
                "defense" => "物理防御",
                "magic_attack" => "法术攻击",
                "magic_defense" => "法术防御",
                "crit_rate_bps" => "暴击率",
                "crit_damage_bps" => "暴击伤害倍率",
                _ => null
            };
            if (label == null)
                return;

            string value = attribute.attributeId switch
            {
                "crit_rate_bps" => "+" + Percent(attribute.value),
                "crit_damage_bps" => Percent(attribute.value),
                _ => (attribute.value > 0 ? "+" : string.Empty) + Number(attribute.value)
            };
            AppendLine(text, label, value);
        }

        private static void AppendEffect(StringBuilder text, CultivationItemEffectDto effect)
        {
            if (effect?.type == null || effect.amount <= 0)
                return;

            switch (effect.type.ToLowerInvariant())
            {
                case "restore_hp":
                    AppendLine(text, "恢复生命", Number(effect.amount));
                    break;
                case "restore_mp":
                    AppendLine(text, "恢复法力", Number(effect.amount));
                    break;
                case "add_jingjie_exp":
                    AppendLine(text, "增加修为", Number(effect.amount));
                    break;
                case "restore_hp_max_bps":
                    AppendLine(text, "恢复生命", "最大生命的 " + Percent(effect.amount));
                    break;
                case "restore_mp_max_bps":
                    AppendLine(text, "恢复法力", "最大法力的 " + Percent(effect.amount));
                    break;
                case "jingjie_success_bonus_bps":
                    AppendLine(text, "突破成功率", "+" + Percent(effect.amount));
                    break;
            }
        }

        private static void AppendLine(StringBuilder text, string label, string value)
        {
            if (text.Length > 0)
                text.AppendLine();
            text.Append(label).Append('：').Append(value);
        }

        private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

        private static string Percent(long basisPoints) =>
            (basisPoints / 100m).ToString("0.##", CultureInfo.InvariantCulture) + "%";
    }
}
