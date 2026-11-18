using Game.Items;

public static class 技能详情适配器
{
    public static ItemDetailData 转换(技能释放配置 configuration)
    {
        if (configuration == null || !configuration.IsValid)
            return null;

        var data = new ItemDetailData
        {
            Identity = configuration.技能ID ?? string.Empty,
            DisplayName = configuration.技能名称 ?? string.Empty,
            Icon = configuration.图标,
            Category = ItemDetailCategory.Cultivation,
            CategoryLabel = ItemDetailCategoryResolver.GetLabel(
                ItemDetailCategory.Cultivation),
            Description = configuration.介绍 ?? string.Empty,
            DetailText = configuration.详情 ?? string.Empty
        };

        if (configuration.详情条目 != null)
        {
            for (int index = 0; index < configuration.详情条目.Count; index++)
            {
                技能详情条目 entry = configuration.详情条目[index];
                if (entry == null || !entry.IsValid)
                    continue;

                data.Entries.Add(new ItemDetailEntry
                {
                    Label = entry.名称 ?? string.Empty,
                    Value = entry.数值 ?? string.Empty
                });
            }
        }

        return data;
    }
}
