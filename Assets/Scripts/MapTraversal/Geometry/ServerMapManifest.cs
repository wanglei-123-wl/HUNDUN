// Adapted from Game.BattleCore/Maps/ServerMapManifest.cs. Source SHA256: b28c14885261610c2f44ec34db93a0c6869063f7790bcc7f67dba49303f00ec1
// Geometry predicates retain backend arithmetic; changes are C# 9/runtime I/O compatibility only.
#nullable enable annotations
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace Game.Client.MapTraversal.Geometry
{

public sealed class ServerMapManifest
{
    public int FormatVersion { get; set; }
    public string SceneId { get; set; } = "";
    public string MapId { get; set; } = "";
    public string CoordinateSystem { get; set; } = "";
    public MapVector3 Origin { get; set; } = new();
    public MapVector3 BoundsMin { get; set; } = new();
    public MapVector3 BoundsMax { get; set; } = new();
    public double CellSize { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string HeightFile { get; set; } = "";
    public string WalkableFile { get; set; } = "";
    public string ColliderFile { get; set; } = "";
    public string? MeshColliderFile { get; set; }
    public string? GroundSurfaceFile { get; set; }
    public double CollisionRootOffsetY { get; set; }
    public double CollisionSkinWidth { get; set; }
    public string NavMeshFile { get; set; } = "";
    public string? RegionsFile { get; set; }
}

public sealed class MapVector3
{
    [JsonProperty("x")]
    public double X { get; set; }

    [JsonProperty("y")]
    public double Y { get; set; }

    [JsonProperty("z")]
    public double Z { get; set; }
}

}
