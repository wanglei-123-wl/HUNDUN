using System;
using Game.Inventory.Model.V2;

namespace Game.Inventory.Presentation.V2
{
    public static class InventoryBagDisplayOrder
    {
        public static int[] Filter(
            InventoryBagLayout layout, Func<string, bool> matches)
        {
            if (layout == null)
                throw new ArgumentNullException(nameof(layout));
            if (matches == null)
                throw new ArgumentNullException(nameof(matches));

            int[] order = new int[layout.Capacity];
            int displayIndex = 0;
            for (int layoutIndex = 0; layoutIndex < layout.Capacity; layoutIndex++)
            {
                string identity = layout.GetIdentity(layoutIndex);
                if (identity != null && matches(identity))
                    order[displayIndex++] = layoutIndex;
            }

            for (int layoutIndex = 0; layoutIndex < layout.Capacity; layoutIndex++)
            {
                if (layout.GetIdentity(layoutIndex) == null)
                    order[displayIndex++] = layoutIndex;
            }

            for (; displayIndex < order.Length; displayIndex++)
                order[displayIndex] = layout.Capacity;
            return order;
        }
    }
}
