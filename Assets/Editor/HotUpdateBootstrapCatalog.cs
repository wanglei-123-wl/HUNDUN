using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.AddressableAssets.ResourceLocators;

public static class HotUpdateBootstrapCatalog
{
    // Leave providers/SDK initialization in the base player, but no baked game
    // catalog, game type references, or mutable legacy remote hash endpoints.
    public static void MakeEmpty(string aaDirectory)
    {
        string settingsPath = Path.Combine(aaDirectory, "settings.json");
        string catalogPath = Path.Combine(aaDirectory, "catalog.json");
        if (!File.Exists(settingsPath)) return;
        if (!File.Exists(catalogPath)) throw new BuildFailedException("The hot-update base requires a local JSON bootstrap catalog.");
        var catalog = JsonUtility.FromJson<ContentCatalogData>(File.ReadAllText(catalogPath));
        catalog.SetData(new List<ContentCatalogDataEntry>());
        File.WriteAllText(catalogPath, JsonUtility.ToJson(catalog));
        var settings = JObject.Parse(File.ReadAllText(settingsPath));
        var main = ((JArray)settings["m_CatalogLocations"]).Cast<JObject>()
            .Single(l => ((JArray)l["m_Keys"]).Any(k => (string)k == "AddressablesMainContentCatalog"));
        main["m_InternalId"] = "{UnityEngine.AddressableAssets.Addressables.RuntimePath}/catalog.json";
        main["m_Dependencies"] = new JArray();
        settings["m_CatalogLocations"] = new JArray(main);
        settings["m_DisableCatalogUpdateOnStart"] = true;
        settings["m_IsLocalCatalogInBundle"] = false;
        File.WriteAllText(settingsPath, settings.ToString(Newtonsoft.Json.Formatting.None));
    }
}
