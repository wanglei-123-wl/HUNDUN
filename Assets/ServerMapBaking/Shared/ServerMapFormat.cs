namespace ServerMapBaking
{
    public static class ServerMapFormat
    {
        public const int FormatVersion = 1;
        public const string CoordinateSystem = "UnityLeftHanded_YUp";
        public const string DefaultOutputFolder = "Assets/ServerMapData";
        public const string ManifestFileName = "manifest.json";
        public const string HeightFileName = "heightfield.bin";
        public const string WalkableFileName = "walkable.bin";
        public const string ColliderFileName = "colliders.bin";
        public const string NavMeshFileName = "navmesh.bin";
        public const ushort ColliderFileVersion = 1;
        public const ushort ColliderHeaderSize = 20;
        public const uint ColliderRecordBytes = 48;
        public const int MaxIdLength = 64;
    }

    public enum ServerColliderType : uint
    {
        Box = 1,
        Sphere = 2,
        Capsule = 3,
        TriangleMesh = 4
    }

    public enum ServerRegionType
    {
        Custom = 0,
        SafeZone = 1,
        BattleZone = 2,
        Teleport = 3,
        MapBoundary = 4,
        DeathZone = 5,
        Water = 6,
        NoMove = 7
    }
}
