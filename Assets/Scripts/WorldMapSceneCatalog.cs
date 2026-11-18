using System;

public static class WorldMapSceneCatalog
{
    public const string DemoWorldId = "world_001";
    public const string DemoMapId = "map_demo_1";
    public const string DemoMapAddress = "game/scene/demo1-map";
    public const string VillageWorldId = "world_002";
    public const string VillageMapId = "map_001";
    public const string VillageMapAddress = "game/scene/map001";

    private static readonly (string SceneId, string MapId, string Address)[] Maps =
    {
        (DemoWorldId, DemoMapId, DemoMapAddress),
        (VillageWorldId, VillageMapId, VillageMapAddress),
        ("world005", "map_005", "game/scene/world005"),
        ("world006", "map_006", "game/scene/world006"),
        ("world007", "map_007", "game/scene/world007"),
        ("world008", "map_008", "game/scene/world008"),
        ("world009", "map_009", "game/scene/world009"),
        ("world010", "map_010", "game/scene/world010")
    };

    public static bool TryGetAddress(string sceneId, string mapId, out string address)
    {
        foreach (var map in Maps)
        {
            if (!string.Equals(sceneId, map.SceneId, StringComparison.Ordinal)
                || !string.Equals(mapId, map.MapId, StringComparison.Ordinal))
                continue;
            address = map.Address;
            return true;
        }

        address = null;
        return false;
    }

    public static bool TryGetMapByAddress(string address, out string sceneId, out string mapId)
    {
        foreach (var map in Maps)
        {
            if (!string.Equals(address, map.Address, StringComparison.Ordinal))
                continue;
            sceneId = map.SceneId;
            mapId = map.MapId;
            return true;
        }

        sceneId = null;
        mapId = null;
        return false;
    }
}
