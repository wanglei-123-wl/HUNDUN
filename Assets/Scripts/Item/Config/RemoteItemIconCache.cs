using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Game.Items
{
    public static class RemoteItemIconCache
    {
        public const string LegacyItemId = "dan1_huichundan";
        public const string LegacyAddress = "trial/icon/huichun";
        public const string AddressPrefix = "item/icon/";

        private static readonly Dictionary<string, AsyncOperationHandle<Sprite>> handles =
            new Dictionary<string, AsyncOperationHandle<Sprite>>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Sprite> icons =
            new Dictionary<string, Sprite>(StringComparer.Ordinal);
        private static readonly Dictionary<string, string> sharedIconAliases =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "armor_xingyun_001_item", "armor_001_item" },
                { "ring_xingyun_001_item", "ring_001_item" }
            };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnStartup()
        {
            Clear();
        }

        public static List<string> DiscoverAddresses()
        {
            var addresses = new HashSet<string>(StringComparer.Ordinal);
            foreach (IResourceLocator locator in Addressables.ResourceLocators)
            {
                foreach (object key in locator.Keys)
                {
                    if (!(key is string address)
                        || !address.StartsWith(AddressPrefix, StringComparison.Ordinal)
                        || address.Length == AddressPrefix.Length)
                        continue;

                    if (locator.Locate(address, typeof(Sprite), out var locations)
                        && locations != null && locations.Count > 0)
                        addresses.Add(address);
                }
            }

            var result = new List<string>(addresses);
            result.Sort(StringComparer.Ordinal);
            if (HasSpriteAddress(LegacyAddress))
                result.Insert(0, LegacyAddress);
            return result;
        }

        public static bool TryGetIcon(string itemId, out Sprite icon)
        {
            string key = itemId ?? string.Empty;
            if (icons.TryGetValue(key, out icon) && icon != null)
                return true;
            return sharedIconAliases.TryGetValue(key, out string sharedKey)
                && icons.TryGetValue(sharedKey, out icon)
                && icon != null;
        }

        public static string AddressFor(string itemId)
        {
            if (itemId == LegacyItemId)
                return LegacyAddress;
            string key = itemId ?? string.Empty;
            return AddressPrefix + (sharedIconAliases.TryGetValue(key, out string sharedKey)
                ? sharedKey : key);
        }

        public static IEnumerator Load(IReadOnlyList<string> addresses)
        {
            Clear();
            for (int index = 0; index < addresses.Count; index++)
            {
                string address = addresses[index];
                string itemId = address == LegacyAddress
                    ? LegacyItemId : address.Substring(AddressPrefix.Length);
                var handle = Addressables.LoadAssetAsync<Sprite>(address);
                yield return handle;

                if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
                {
                    Debug.LogWarning("[ResourceTrial] Remote icon unavailable for "
                        + itemId + " (" + address + ").");
                    Addressables.Release(handle);
                    continue;
                }

                if (handles.TryGetValue(itemId, out var previous))
                    Addressables.Release(previous);
                handles[itemId] = handle;
                icons[itemId] = handle.Result;
                Debug.Log("[ResourceTrial] Remote icon loaded for " + itemId);
            }

            Application.quitting -= Clear;
            Application.quitting += Clear;
        }

        public static void Clear()
        {
            foreach (var handle in handles.Values)
            {
                if (handle.IsValid())
                    Addressables.Release(handle);
            }
            handles.Clear();
            icons.Clear();
            Application.quitting -= Clear;
        }

        private static bool HasSpriteAddress(string address)
        {
            foreach (IResourceLocator locator in Addressables.ResourceLocators)
            {
                if (locator.Locate(address, typeof(Sprite), out var locations)
                    && locations != null && locations.Count > 0)
                    return true;
            }
            return false;
        }
    }
}
