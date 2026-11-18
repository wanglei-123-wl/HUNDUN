using System;
using System.Collections.Generic;

namespace ServerMapBaking
{
    [Serializable]
    public struct JsonFloat3
    {
        public float x;
        public float y;
        public float z;

        public JsonFloat3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }
    }

    [Serializable]
    public sealed class ServerMapManifest
    {
        public int formatVersion = ServerMapFormat.FormatVersion;
        public string sceneId = string.Empty;
        public string mapId = string.Empty;
        public string coordinateSystem = ServerMapFormat.CoordinateSystem;
        public JsonFloat3 origin;
        public JsonFloat3 boundsMin;
        public JsonFloat3 boundsMax;
        public float cellSize;
        public int width;
        public int height;
        public string heightFile = ServerMapFormat.HeightFileName;
        public string walkableFile = ServerMapFormat.WalkableFileName;
        public string colliderFile = ServerMapFormat.ColliderFileName;
        public string navMeshFile = ServerMapFormat.NavMeshFileName;
        public string regionsFile;
    }

    public sealed class ServerMapData
    {
        public ServerMapManifest Manifest = new ServerMapManifest();
        public float[] Heightfield = Array.Empty<float>();
        public byte[] Walkable = Array.Empty<byte>();
        public readonly List<ColliderRecordData> Colliders = new List<ColliderRecordData>();
        public readonly NavMeshTriangleData NavMesh = new NavMeshTriangleData();
        public readonly List<BoxRegionData> Regions = new List<BoxRegionData>();
    }

    public sealed class ColliderRecordData
    {
        public uint ColliderId;
        public ServerColliderType ColliderType;
        public uint Flags;
        public string Name = string.Empty;
        public Float3 Center;
        public Float3 Rotation;
        public Float3 Size;
    }

    public sealed class NavMeshTriangleData
    {
        public Float3[] Vertices = Array.Empty<Float3>();
        public int[] Indices = Array.Empty<int>();
        public int[] Areas = Array.Empty<int>();
    }

    public sealed class BoxRegionData
    {
        public int RegionId;
        public ServerRegionType RegionType;
        public string Name = string.Empty;
        public string Payload = string.Empty;
        public Float3 Center;
        public Float3 Size;
        public QuaternionData Rotation;
    }
}
