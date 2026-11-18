using System;

namespace ServerMapBaking.Editor
{
    [Serializable]
    internal sealed class ServerMapBakeSettings
    {
        public string OutputFolder = ServerMapFormat.DefaultOutputFolder;
        public bool DrawTerrain = true;
        public bool DrawColliders = true;
        public bool DrawNavMesh = true;
        public bool DrawRegions = true;
        public int TerrainPreviewSamples = 64;
    }
}
