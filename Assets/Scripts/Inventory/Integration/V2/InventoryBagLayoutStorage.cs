using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Game.Inventory.Integration.V2
{
    public interface IInventoryBagLayoutStorage
    {
        IReadOnlyList<string> Load(long characterDbId);
        void Save(long characterDbId, IReadOnlyList<string> slots);
    }

    public sealed class InventoryBagPlayerPrefsStorage : IInventoryBagLayoutStorage
    {
        private const string KeyPrefix = "inventory_v2_bag_layout_";

        [Serializable]
        private sealed class SavedLayout
        {
            public string[] slots;
        }

        public IReadOnlyList<string> Load(long characterDbId)
        {
            if (characterDbId <= 0) throw new ArgumentOutOfRangeException(nameof(characterDbId));
            string json = PlayerPrefs.GetString(Key(characterDbId), string.Empty);
            if (string.IsNullOrEmpty(json)) return Array.Empty<string>();
            try
            {
                SavedLayout layout = JsonUtility.FromJson<SavedLayout>(json);
                return layout != null && layout.slots != null
                    ? layout.slots
                    : Array.Empty<string>();
            }
            catch (Exception)
            {
                return Array.Empty<string>();
            }
        }

        public void Save(long characterDbId, IReadOnlyList<string> slots)
        {
            if (characterDbId <= 0) throw new ArgumentOutOfRangeException(nameof(characterDbId));
            if (slots == null) throw new ArgumentNullException(nameof(slots));
            var copy = new string[slots.Count];
            for (int index = 0; index < copy.Length; index++) copy[index] = slots[index];
            PlayerPrefs.SetString(Key(characterDbId),
                JsonUtility.ToJson(new SavedLayout { slots = copy }));
            PlayerPrefs.Save();
        }

        private static string Key(long characterDbId)
        {
            return KeyPrefix + characterDbId.ToString(CultureInfo.InvariantCulture);
        }
    }
}
