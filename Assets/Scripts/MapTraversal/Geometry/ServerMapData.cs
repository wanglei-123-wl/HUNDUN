// Adapted from Game.BattleCore/Maps/ServerMapData.cs. Source SHA256: f12ea191f5bc0ec972b70a858cdcb860dcf6748a4d921af3c7bbb17b69630f1a
// Geometry predicates retain backend arithmetic; changes are C# 9/runtime I/O compatibility only.
#nullable enable annotations
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using Newtonsoft.Json;

namespace Game.Client.MapTraversal.Geometry
{

public sealed class ServerMapData : IWorldSurfaceQuery
{
    private const int ColliderHeaderBytes = 20;
    private const int ColliderRecordBytes = 48;
    private readonly float[] _heightfield;
    private readonly byte[] _walkable;
    private readonly ServerMapCollider[] _colliders;
    private readonly ServerMapCollisionIndex _collisionIndex;
    private readonly ServerMapMeshCollisionIndex? _meshCollisionIndex;
    private readonly ServerMapGroundSurfaceIndex? _groundSurfaceIndex;
    private readonly bool[] _navigationCells;
    private ServerMapData(
        string directoryPath,
        ServerMapManifest manifest,
        float[] heightfield,
        byte[] walkable,
        ServerMapCollider[] colliders,
        ServerMapMeshCollisionIndex? meshCollisionIndex,
        bool[] navigationCells,
        int navigationTriangleCount,
        ServerMapGroundSurfaceIndex? groundSurfaceIndex = null)
    {
        DirectoryPath = directoryPath;
        Manifest = manifest;
        _heightfield = heightfield;
        _walkable = walkable;
        _colliders = colliders;
        _collisionIndex = new ServerMapCollisionIndex(colliders, exactBoxes: manifest.FormatVersion >= 2);
        _meshCollisionIndex = meshCollisionIndex;
        _groundSurfaceIndex = groundSurfaceIndex;
        _navigationCells = navigationCells;
        NavigationTriangleCount = navigationTriangleCount;
    }

    public string DirectoryPath { get; }
    public ServerMapManifest Manifest { get; }
    public IReadOnlyList<ServerMapCollider> Colliders => _colliders;
    public int NavigationTriangleCount { get; }
    public int MeshColliderCount => _meshCollisionIndex?.MeshCount ?? 0;
    public int CollisionTriangleCount => _meshCollisionIndex?.TriangleCount ?? 0;
    public int GroundSurfaceTriangleCount => _groundSurfaceIndex?.TriangleCount ?? 0;

    public static ServerMapData Load(IReadOnlyDictionary<string, byte[]> files)
    {
        if (files == null) throw new ArgumentNullException(nameof(files));
        var manifest = JsonConvert.DeserializeObject<ServerMapManifest>(Encoding.UTF8.GetString(RequiredFile(files, "manifest.json")))
            ?? throw new InvalidDataException("Map manifest is invalid.");
        ValidateManifest(manifest);
        int samples = checked(manifest.Width * manifest.Height);
        float[] heightfield = ReadHeightfield(RequiredFile(files, manifest.HeightFile), samples);
        byte[] walkable = ReadWalkable(RequiredFile(files, manifest.WalkableFile), samples);
        ServerMapCollider[] colliders = ReadColliders(RequiredFile(files, manifest.ColliderFile));
        var meshes = manifest.FormatVersion >= 2
            ? ServerMapMeshCollisionIndex.Load(RequiredFile(files, manifest.MeshColliderFile), colliders.Select(c => c.ColliderId)) : null;
        var navigation = ReadNavigation(RequiredFile(files, manifest.NavMeshFile), manifest);
        var ground = manifest.FormatVersion == 3
            ? ServerMapGroundSurfaceIndex.Load(RequiredFile(files, manifest.GroundSurfaceFile), manifest) : null;
        return new ServerMapData("package:" + manifest.MapId, manifest, heightfield, walkable, colliders, meshes,
            navigation.Cells, navigation.TriangleCount, ground);
    }
    public bool IsInsideBounds(double x, double z) =>
        x >= Manifest.BoundsMin.X && x < Manifest.BoundsMax.X &&
        z >= Manifest.BoundsMin.Z && z < Manifest.BoundsMax.Z;

    public bool IsWalkable(double x, double z)
    {
        // The physical support sheet also exists beside/under obstacles. Keep
        // point walkability aligned with the standard baked navigation body;
        // traversal still uses its caller's actual radius and height.
        if (_groundSurfaceIndex is not null) return TryGetStandingHeight(x, z, .5, 2, out _);
        return TryGetCellIndex(x, z, out int index) && _walkable[index] != 0;
    }

    public bool IsNavigationCell(int gridX, int gridZ)
    {
        if (gridX < 0 || gridX >= Manifest.Width || gridZ < 0 || gridZ >= Manifest.Height)
            return false;
        int index = gridZ * Manifest.Width + gridX;
        return _walkable[index] != 0 && _navigationCells[index];
    }

    internal bool IsWalkableCell(int gridX, int gridZ) =>
        gridX >= 0 && gridX < Manifest.Width && gridZ >= 0 && gridZ < Manifest.Height &&
        _walkable[gridZ * Manifest.Width + gridX] != 0;

    public bool TryGetNavigationCell(double x, double z, out int gridX, out int gridZ)
    {
        gridX = (int)Math.Floor((x - Manifest.Origin.X) / Manifest.CellSize);
        gridZ = (int)Math.Floor((z - Manifest.Origin.Z) / Manifest.CellSize);
        return IsInsideBounds(x, z) && (_groundSurfaceIndex is not null ? IsWalkable(x, z) : IsNavigationCell(gridX, gridZ));
    }

    public bool TryGetGroundHeight(double x, double z, out double y)
    {
        y = 0;
        if (_groundSurfaceIndex is not null) return IsInsideBounds(x, z) && _groundSurfaceIndex.TryGetHeight(x, z, out y);
        if (!TryGetCellIndex(x, z, out int index) || _walkable[index] == 0)
            return false;

        float height = _heightfield[index];
        if (!PortableMath.IsFinite(height))
            return false;

        y = height;
        return true;
    }

    public bool HasCollision(
        double x,
        double y,
        double z,
        double playerRadius,
        double playerHeight)
    {
        var feetPosition = new Vector3((float)x, (float)y, (float)z);
        float radius = (float)Math.Max(0, playerRadius);
        float height = (float)Math.Max(radius * 2, playerHeight);
        AdjustCollisionProbe(ref feetPosition, ref radius, ref height);
        return _collisionIndex.Intersects(feetPosition, feetPosition, radius, height, sweep: false) ||
            (_meshCollisionIndex?.Intersects(feetPosition, feetPosition, radius, height) ?? false);
    }

    public bool TryGetStandingHeight(double x,double z,double radius,double height,out double standingY)
    {
        if(!TryGetGroundHeight(x,z,out standingY))return false;
        if(!HasCollision(x,standingY,z,radius,height))return true;
        if(_groundSurfaceIndex is null || HasCollision(x,standingY+.3,z,radius,height))return false;
        double low=standingY,high=standingY+.3;
        for(int i=0;i<14;i++)
        {
            double middle=(low+high)*.5;
            if(HasCollision(x,middle,z,radius,height))low=middle;else high=middle;
        }
        standingY=high;return true;
    }

    public bool HasCollisionAlongSegment(double fromX, double fromY, double fromZ,
        double toX, double toY, double toZ, double radius, double height)
    {
        // A purely vertical V3 sweep is exactly one capsule with a longer
        // axis. Avoid degenerate swept triangles at standing-contact heights.
        if (Manifest.FormatVersion == 3 && fromX == toX && fromZ == toZ)
            return HasCollision(fromX, Math.Min(fromY, toY), fromZ, radius,
                Math.Max(Math.Max(0, radius) * 2, height) + Math.Abs(toY - fromY));
        var from = new Vector3((float)fromX, (float)fromY, (float)fromZ);
        var to = new Vector3((float)toX, (float)toY, (float)toZ);
        float r = (float)Math.Max(0, radius), h = (float)Math.Max(r * 2, height);
        float offset = AdjustCollisionProbe(ref from, ref r, ref h);
        to.Y += offset;
        return _collisionIndex.Intersects(from, to, r, h, sweep: true) ||
            (_meshCollisionIndex?.Intersects(from, to, r, h) ?? false);
    }

    /// <summary>V3 ground movement follows the selected sheet, including every crossed
    /// footprint edge. This does not implement movement between overlapping floors.</summary>
    public bool CanTraverseGroundSegment(double ax, double ay, double az, double bx, double bz,
        double radius, double height, out double endGround)
    {
        endGround = 0;
        if (_groundSurfaceIndex is null || !PortableMath.IsFinite(ax + ay + az + bx + bz) ||
            !TryGetGroundHeight(ax, az, out double startGround) || Math.Abs(startGround - ay) > .301 ||
            !TryGetGroundHeight(bx, bz, out endGround)) return false;
        double dx = bx - ax, dz = bz - az;
        var breaks = _groundSurfaceIndex.SegmentBreaks(ax, az, bx, bz);
        double previousT = 0, previousY = ay;
        bool bodyClear = true;
        for (int i = 1; i < breaks.Count; i++)
        {
            double lo = breaks[i - 1], hi = breaks[i];
            if (hi - lo < 1e-9) continue;
            double inset = Math.Min(1e-7, (hi - lo) * .01);
            if (!TryGetGroundHeight(ax + dx * ((lo + hi) * .5), az + dz * ((lo + hi) * .5), out _) ||
                !TryGetGroundHeight(ax + dx * (lo + inset), az + dz * (lo + inset), out double aY) ||
                !TryGetGroundHeight(ax + dx * (hi - inset), az + dz * (hi - inset), out double bY) ||
                Math.Abs(aY - previousY) > .301) return false;
            // The initial root may already be raised by the rounded foot's
            // contact with the next tread. Do not sweep it down into that tread
            // before starting the horizontal movement.
            double bodyAY=lo<=1e-9?Math.Max(aY,ay):aY;
            if (HasCollisionAlongSegment(ax + dx * previousT, previousY, az + dz * previousT,
                    ax + dx * lo, bodyAY, az + dz * lo, radius, height) ||
                HasCollisionAlongSegment(ax + dx * lo, bodyAY, az + dz * lo,
                    ax + dx * hi, bY, az + dz * hi, radius, height)) bodyClear = false;
            previousT = hi; previousY = bY;
        }
        if (Math.Abs(previousY - endGround) > .301) return false;
        if (bodyClear && !HasCollision(bx, endGround, bz, radius, height)) return true;

        // Follow the actual standing root through each supported patch. A
        // rounded foot can already be raised before the center reaches a tread;
        // resetting it to the floor at a coplanar triangle edge creates a wall.
        // Step height applies locally, not to the accumulated rise of a flight.
        // Standing-root paths are not monotonic under subdivision. Preserve
        // the original swept path; refine only when contact needs a tighter
        // approximation of the rounded foot's changing support height.
        if (FollowStandingRoot(.1, out double rootY) || FollowStandingRoot(.025, out rootY))
        {
            endGround = rootY;
            return true;
        }
        return false;

        bool FollowStandingRoot(double maxStep, out double root)
        {
            root = ay;
            double length = Math.Sqrt(dx * dx + dz * dz);
            double currentT = 0, currentY = ay, currentGround = startGround;
            for (int i = 1; i < breaks.Count; i++)
            {
                double lo = breaks[i - 1], hi = breaks[i];
                if (hi - lo < 1e-9) continue;
                int steps = Math.Max(1, (int)Math.Ceiling(length * (hi - lo) / maxStep));
                for (int step = 1; step <= steps; step++)
                {
                    double nextT = lo + (hi - lo) * step / steps;
                    double fromX = ax + dx * currentT, fromZ = az + dz * currentT;
                    double toX = ax + dx * nextT, toZ = az + dz * nextT;
                    if (!TryGetGroundHeight(toX, toZ, out double nextGround) ||
                        !TryGetStandingHeight(toX, toZ, radius, height, out double nextY)) return false;
                    if (HasCollisionAlongSegment(fromX, currentY, fromZ, toX, nextY, toZ, radius, height))
                    {
                        // First try only the required rise, preserving passages
                        // whose ceiling does not allow the full step-height lift.
                        double lifted = Math.Max(currentY, nextY);
                        if (!CanStepAt(lifted) && !CanStepAt(Math.Max(lifted, currentGround + .3))) return false;
                        bool CanStepAt(double lift) => lift - currentY <= .301 &&
                            !HasCollisionAlongSegment(fromX, currentY, fromZ, fromX, lift, fromZ, radius, height) &&
                            !HasCollisionAlongSegment(fromX, lift, fromZ, toX, lift, toZ, radius, height) &&
                            !HasCollisionAlongSegment(toX, lift, toZ, toX, nextY, toZ, radius, height);
                    }
                    currentT = nextT; currentY = nextY; currentGround = nextGround;
                }
            }
            root = currentY;
            return true;
        }
    }

    private float AdjustCollisionProbe(ref Vector3 position, ref float radius, ref float height)
    {
        if (Manifest.FormatVersion < 2) return 0;
        // Preserve the nominal body dimensions; shrink only the internal query by skin,
        // leaving its capsule-axis endpoints at the same positions used by Unity's bake.
        float skin = MathF.Min((float)Manifest.CollisionSkinWidth, radius * .5f);
        float offset = (float)Manifest.CollisionRootOffsetY + skin;
        position.Y += offset;
        radius -= skin;
        height -= skin * 2;
        return offset;
    }

    private bool TryGetCellIndex(double x, double z, out int index)
    {
        index = -1;
        if (!IsInsideBounds(x, z))
            return false;

        int gridX = (int)Math.Floor((x - Manifest.Origin.X) / Manifest.CellSize);
        int gridZ = (int)Math.Floor((z - Manifest.Origin.Z) / Manifest.CellSize);
        if (gridX < 0 || gridX >= Manifest.Width || gridZ < 0 || gridZ >= Manifest.Height)
            return false;

        index = checked(gridZ * Manifest.Width + gridX);
        return true;
    }

    private static void ValidateManifest(ServerMapManifest manifest)
    {
        if (manifest.FormatVersion is < 1 or > 3)
            throw new InvalidDataException($"Unsupported map format version: {manifest.FormatVersion}");
        if (manifest.FormatVersion >= 2 && string.IsNullOrWhiteSpace(manifest.MeshColliderFile))
            throw new InvalidDataException("V2 map requires meshColliderFile.");
        if (manifest.FormatVersion == 1 && !string.IsNullOrWhiteSpace(manifest.MeshColliderFile))
            throw new InvalidDataException("Mesh collision data must declare map format V2.");
        if ((manifest.FormatVersion == 3) != !string.IsNullOrWhiteSpace(manifest.GroundSurfaceFile))
            throw new InvalidDataException("Precise ground surface data must declare map format V3.");
        if (!PortableMath.IsFinite(manifest.CollisionRootOffsetY) || manifest.CollisionRootOffsetY < 0 ||
            manifest.CollisionRootOffsetY > 1 || !PortableMath.IsFinite(manifest.CollisionSkinWidth) ||
            manifest.CollisionSkinWidth < 0 || manifest.CollisionSkinWidth > .5)
            throw new InvalidDataException("Invalid collision probe offset or skin width.");
        if (string.IsNullOrWhiteSpace(manifest.SceneId))
            throw new InvalidDataException("Map sceneId is required.");
        if (string.IsNullOrWhiteSpace(manifest.MapId))
            throw new InvalidDataException("Map mapId is required.");
        if (!string.Equals(manifest.CoordinateSystem, "UnityLeftHanded_YUp", StringComparison.Ordinal))
            throw new InvalidDataException($"Unsupported coordinate system: {manifest.CoordinateSystem}");
        if (manifest.Width <= 0 || manifest.Height <= 0 || manifest.CellSize <= 0)
            throw new InvalidDataException("Map grid size is invalid.");
        if (manifest.BoundsMax.X <= manifest.BoundsMin.X || manifest.BoundsMax.Z <= manifest.BoundsMin.Z)
            throw new InvalidDataException("Map bounds are invalid.");
    }

    private static float[] ReadHeightfield(byte[] bytes, int sampleCount)
    {

        int expectedBytes = checked(sampleCount * sizeof(float));
        if (bytes.Length != expectedBytes)
            throw new InvalidDataException($"Heightfield size mismatch. Expected {expectedBytes}, got {bytes.Length}.");

        var result = new float[sampleCount];
        for (int i = 0; i < sampleCount; i++)
        {
            int bits = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(i * sizeof(float), sizeof(float)));
            result[i] = BitConverter.Int32BitsToSingle(bits);
        }
        return result;
    }

    private static byte[] ReadWalkable(byte[] bytes, int sampleCount)
    {

        if (bytes.Length != sampleCount)
            throw new InvalidDataException($"Walkable size mismatch. Expected {sampleCount}, got {bytes.Length}.");
        return bytes;
    }

    private static ServerMapCollider[] ReadColliders(byte[] bytes)
    {

        if (bytes.Length < ColliderHeaderBytes || Encoding.ASCII.GetString(bytes, 0, 4) != "GCOL")
            throw new InvalidDataException("Collider file magic is invalid.");

        ushort version = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4, 2));
        ushort headerSize = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(6, 2));
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8, 4));
        uint recordBytes = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12, 4));
        if (version != 1 || headerSize != ColliderHeaderBytes || recordBytes != ColliderRecordBytes)
            throw new InvalidDataException("Collider file version or layout is unsupported.");

        long expectedLength = headerSize + (long)count * recordBytes;
        if (bytes.LongLength != expectedLength)
            throw new InvalidDataException($"Collider file size mismatch. Expected {expectedLength}, got {bytes.LongLength}.");

        var result = new ServerMapCollider[checked((int)count)];
        for (int i = 0; i < result.Length; i++)
        {
            int offset = checked((int)headerSize + i * (int)recordBytes);
            ReadOnlySpan<byte> record = bytes.AsSpan(offset, (int)recordBytes);
            ServerMapColliderType colliderType = (ServerMapColliderType)BinaryPrimitives.ReadUInt32LittleEndian(record[4..8]);
            if (colliderType is not (ServerMapColliderType.Box or ServerMapColliderType.Sphere or ServerMapColliderType.Capsule))
                throw new InvalidDataException($"Unsupported collider type: {colliderType}.");
            result[i] = new ServerMapCollider(
                BinaryPrimitives.ReadUInt32LittleEndian(record[0..4]),
                colliderType,
                BinaryPrimitives.ReadUInt32LittleEndian(record[8..12]),
                ReadVector3(record[12..24]),
                ReadVector3(record[24..36]),
                ReadVector3(record[36..48]));
        }
        return result;
    }

    private static (bool[] Cells, int TriangleCount) ReadNavigation(byte[] bytes, ServerMapManifest manifest)
    {

        if (bytes.Length < 12)
            throw new InvalidDataException("Navigation mesh is truncated.");
        int vertexCount = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        if (vertexCount < 0 || 4L + vertexCount * 12L + 8 > bytes.Length)
            throw new InvalidDataException("Navigation vertex count is invalid.");
        var vertices = new Vector3[vertexCount];
        int offset = 4;
        for (int vertex = 0; vertex < vertexCount; vertex++, offset += 12)
        {
            vertices[vertex] = ReadVector3(bytes.AsSpan(offset, 12));
            if (!PortableMath.IsFinite(vertices[vertex].X) || !PortableMath.IsFinite(vertices[vertex].Y) ||
                !PortableMath.IsFinite(vertices[vertex].Z))
                throw new InvalidDataException("Navigation vertex is not finite.");
        }
        int indexCount = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, 4));
        offset += 4;
        if (indexCount < 0 || indexCount % 3 != 0 || offset + (long)indexCount * 4 + 4 > bytes.Length)
            throw new InvalidDataException("Navigation index count is invalid.");
        var indices = new int[indexCount];
        for (int index = 0; index < indexCount; index++, offset += 4)
        {
            indices[index] = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, 4));
            if ((uint)indices[index] >= (uint)vertexCount)
                throw new InvalidDataException("Navigation index is outside the vertex table.");
        }
        int regionCount = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, 4));
        offset += 4;
        int triangleCount = indexCount / 3;
        if (triangleCount == 0 || regionCount != triangleCount || offset + (long)regionCount * 4 != bytes.Length)
            throw new InvalidDataException("Navigation region table size is invalid.");

        var cells = new bool[checked(manifest.Width * manifest.Height)];
        for (int triangle = 0; triangle < triangleCount; triangle++)
        {
            Vector3 a = vertices[indices[triangle * 3]];
            Vector3 b = vertices[indices[triangle * 3 + 1]];
            Vector3 c = vertices[indices[triangle * 3 + 2]];
            double area = (b.X - a.X) * (c.Z - a.Z) - (b.Z - a.Z) * (c.X - a.X);
            if (Math.Abs(area) < 1e-8) continue;
            double minX = Math.Min(a.X, Math.Min(b.X, c.X));
            double maxX = Math.Max(a.X, Math.Max(b.X, c.X));
            double minZ = Math.Min(a.Z, Math.Min(b.Z, c.Z));
            double maxZ = Math.Max(a.Z, Math.Max(b.Z, c.Z));
            int startX = Math.Max(0, (int)Math.Floor((minX - manifest.Origin.X) / manifest.CellSize) - 1);
            int endX = Math.Min(manifest.Width - 1, (int)Math.Ceiling((maxX - manifest.Origin.X) / manifest.CellSize));
            int startZ = Math.Max(0, (int)Math.Floor((minZ - manifest.Origin.Z) / manifest.CellSize) - 1);
            int endZ = Math.Min(manifest.Height - 1, (int)Math.Ceiling((maxZ - manifest.Origin.Z) / manifest.CellSize));
            for (int z = startZ; z <= endZ; z++)
            for (int x = startX; x <= endX; x++)
            {
                double px = manifest.Origin.X + (x + 0.5) * manifest.CellSize;
                double pz = manifest.Origin.Z + (z + 0.5) * manifest.CellSize;
                double ab = (b.X - a.X) * (pz - a.Z) - (b.Z - a.Z) * (px - a.X);
                double bc = (c.X - b.X) * (pz - b.Z) - (c.Z - b.Z) * (px - b.X);
                double ca = (a.X - c.X) * (pz - c.Z) - (a.Z - c.Z) * (px - c.X);
                if ((ab >= -1e-4 && bc >= -1e-4 && ca >= -1e-4) ||
                    (ab <= 1e-4 && bc <= 1e-4 && ca <= 1e-4))
                    cells[z * manifest.Width + x] = true;
            }
        }
        if (!cells.Any(cell => cell))
            throw new InvalidDataException("Navigation mesh covers no map cells.");
        return (cells, triangleCount);
    }

    private static Vector3 ReadVector3(ReadOnlySpan<byte> bytes) => new(
        ReadFloat(bytes[0..4]),
        ReadFloat(bytes[4..8]),
        ReadFloat(bytes[8..12]));

    private static float ReadFloat(ReadOnlySpan<byte> bytes) =>
        BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(bytes));

    private static byte[] RequiredFile(IReadOnlyDictionary<string, byte[]> files, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName.IndexOf('/') >= 0 || fileName.IndexOf((char)92) >= 0
            || !files.TryGetValue(fileName, out byte[] bytes))
            throw new InvalidDataException("Missing or invalid map data file: " + fileName);
        return bytes;
    }
}
}
