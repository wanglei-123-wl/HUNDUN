using System;
using UnityEngine;

namespace Game.Minimap
{
    public sealed class MinimapMapCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class Map
        {
            public string sceneId;
            public string mapId;
            public string displayName;
            public Texture2D texture;
            public Vector2 minXZ;
            public Vector2 maxXZ;
        }

        [SerializeField] private Map[] maps = Array.Empty<Map>();

        public Map Find(string sceneId, string mapId)
        {
            foreach (Map map in maps)
            {
                if (map != null && map.sceneId == sceneId && map.mapId == mapId
                    && map.texture != null && map.maxXZ.x > map.minXZ.x
                    && map.maxXZ.y > map.minXZ.y)
                    return map;
            }

            return null;
        }
    }
}
