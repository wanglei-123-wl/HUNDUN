// Adapted from Game.BattleCore/Maps/ServerMapMeshCollisionIndex.cs. Source SHA256: 991c30db455727294cbc5c0a9136ba94fb20d43ec9a2e953feca2921bfc5f417
// Geometry predicates retain backend arithmetic; changes are C# 9/runtime I/O compatibility only.
#nullable enable annotations
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;

namespace Game.Client.MapTraversal.Geometry
{

internal sealed class ServerMapMeshCollisionIndex
{
    private readonly struct Triangle
    {
        public readonly Vector3 A;
        public readonly Vector3 B;
        public readonly Vector3 C;
        public Triangle(Vector3 A, Vector3 B, Vector3 C)
        {
            this.A = A;
            this.B = B;
            this.C = C;
        }

        public Vector3 Min => Vector3.Min(A, Vector3.Min(B, C));
        public Vector3 Max => Vector3.Max(A, Vector3.Max(B, C));
        public Vector3 Center => (A + B + C) / 3;
    }
    private readonly struct Node
    {
        public readonly Vector3 Min;
        public readonly Vector3 Max;
        public readonly int Start;
        public readonly int Count;
        public readonly int Left;
        public readonly int Right;
        public Node(Vector3 Min, Vector3 Max, int Start, int Count, int Left, int Right)
        {
            this.Min = Min;
            this.Max = Max;
            this.Start = Start;
            this.Count = Count;
            this.Left = Left;
            this.Right = Right;
        }
    }
    private readonly Triangle[] _triangles;
    private readonly int[] _order;
    private readonly Node[] _nodes;
    private int _nodeCount;
    public int MeshCount { get; }
    public int TriangleCount => _triangles.Length;

    private ServerMapMeshCollisionIndex(Triangle[] triangles, int meshCount)
    {
        MeshCount = meshCount;
        _triangles = triangles;
        _order = Enumerable.Range(0, triangles.Length).ToArray();
        // Median splitting with up to eight triangles per leaf needs fewer than N/2 nodes.
        _nodes = new Node[Math.Max(1, triangles.Length / 2 + 1)];
        if (triangles.Length > 0) Build(0, triangles.Length);
        Array.Resize(ref _nodes, _nodeCount);
    }

    public static ServerMapMeshCollisionIndex Load(byte[] data, IEnumerable<uint> primitiveIds)
    {
        using var reader = new BinaryReader(new MemoryStream(data, false));
        var stream = reader.BaseStream;
        if (stream.Length < 16 || Encoding.ASCII.GetString(reader.ReadBytes(4)) != "GMES" ||
            reader.ReadUInt16() != 1 || reader.ReadUInt16() != 16)
            throw new InvalidDataException("Mesh collider header is invalid.");
        uint count = reader.ReadUInt32();
        if (reader.ReadUInt32() != 0 || count > (stream.Length - 16) / 64)
            throw new InvalidDataException("Mesh collider count or reserved field is invalid.");
        var ids = new HashSet<uint>(primitiveIds);
        var triangles = new List<Triangle>();
        for (uint m = 0; m < count; m++)
        {
            if (stream.Length - stream.Position < 16) throw new InvalidDataException("Truncated mesh collider record.");
            uint id = reader.ReadUInt32(), flags = reader.ReadUInt32();
            uint vc = reader.ReadUInt32(), ic = reader.ReadUInt32();
            if (id == 0 || !ids.Add(id) || flags != 0 || vc < 3 || vc > 20_000_000 ||
                ic == 0 || ic % 3 != 0 || ic > 60_000_000 ||
                (long)vc * 12 + (long)ic * 4 > stream.Length - stream.Position)
                throw new InvalidDataException("Invalid mesh collider counts, ID, flags or record length.");
            var vertices = new Vector3[(int)vc];
            for (int i = 0; i < vertices.Length; i++)
            {
                var v = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                if (!PortableMath.IsFinite(v.X) || !PortableMath.IsFinite(v.Y) || !PortableMath.IsFinite(v.Z))
                    throw new InvalidDataException("Non-finite collision mesh vertex.");
                vertices[i] = v;
            }
            for (uint i = 0; i < ic; i += 3)
            {
                uint a = reader.ReadUInt32(), b = reader.ReadUInt32(), c = reader.ReadUInt32();
                if (a >= vc || b >= vc || c >= vc) throw new InvalidDataException("Collision mesh index is out of bounds.");
                triangles.Add(new(vertices[a], vertices[b], vertices[c]));
            }
        }
        if (stream.Position != stream.Length) throw new InvalidDataException("Unexpected trailing mesh collider data.");
        return new(triangles.ToArray(), checked((int)count));
    }

    private int Build(int start, int count)
    {
        int node = _nodeCount++;
        Vector3 min = new(float.PositiveInfinity), max = new(float.NegativeInfinity);
        for (int i = start; i < start + count; i++)
        {
            var triangle = _triangles[_order[i]];
            min = Vector3.Min(min, triangle.Min); max = Vector3.Max(max, triangle.Max);
        }
        if (count <= 8) _nodes[node] = new(min, max, start, count, -1, -1);
        else
        {
            Vector3 size = max - min;
            int axis = size.X >= size.Y && size.X >= size.Z ? 0 : size.Y >= size.Z ? 1 : 2;
            Array.Sort(_order, start, count, Comparer<int>.Create((a, b) =>
                Coordinate(_triangles[a].Center).CompareTo(Coordinate(_triangles[b].Center))));
            int left = Build(start, count / 2), right = Build(start + count / 2, count - count / 2);
            _nodes[node] = new(min, max, 0, 0, left, right);
            float Coordinate(Vector3 value) => axis == 0 ? value.X : axis == 1 ? value.Y : value.Z;
        }
        return node;
    }

    public bool Intersects(Vector3 from, Vector3 to, float radius, float height)
    {
        if (_triangles.Length == 0) return false;
        Vector3 min = Vector3.Min(from, to) - new Vector3(radius, 0, radius);
        Vector3 max = Vector3.Max(from, to) + new Vector3(radius, height, radius);
        Vector3 low = from + new Vector3(0, radius, 0);
        Vector3 high = from + new Vector3(0, MathF.Max(radius, height - radius), 0);
        Vector3 delta = to - from;
        // Tangency to a supporting surface is not penetration. Tolerance is numerical only.
        float threshold = MathF.Max(0, radius - 0.0001f);
        threshold *= threshold;
        return Visit(0);

        bool Visit(int i)
        {
            var node = _nodes[i];
            if (min.X > node.Max.X || max.X < node.Min.X || min.Y > node.Max.Y ||
                max.Y < node.Min.Y || min.Z > node.Max.Z || max.Z < node.Min.Z) return false;
            if (node.Count == 0) return Visit(node.Left) || Visit(node.Right);
            for (int t = node.Start; t < node.Start + node.Count; t++)
            {
                var triangle = _triangles[_order[t]];
                if (Vector3.Cross(triangle.B - triangle.A, triangle.C - triangle.A).LengthSquared() < 1e-20f) continue;
                if (TriangleDistance.SweptAxisSquared(low, high, delta, triangle.A, triangle.B, triangle.C) < threshold)
                    return true;
            }
            return false;
        }
    }
}

}
