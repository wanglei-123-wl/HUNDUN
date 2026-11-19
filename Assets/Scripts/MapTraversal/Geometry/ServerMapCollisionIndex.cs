// Adapted from Game.BattleCore/Maps/ServerMapCollisionIndex.cs. Source SHA256: b0d719255b85665d92831ff8eab7a1c61ee007911a7902a68d74ad325276919f
// Geometry predicates retain backend arithmetic; changes are C# 9/runtime I/O compatibility only.
#nullable enable annotations
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace Game.Client.MapTraversal.Geometry
{

// Immutable BVH: a collider occurs in exactly one leaf, with no per-query set or shared scratch state.
internal sealed class ServerMapCollisionIndex
{
    private readonly struct Node
    {
        public readonly Vector3 Min;
        public readonly Vector3 Max;
        public readonly int Left;
        public readonly int Right;
        public readonly int Collider;
        public Node(Vector3 Min, Vector3 Max, int Left, int Right, int Collider)
        {
            this.Min = Min;
            this.Max = Max;
            this.Left = Left;
            this.Right = Right;
            this.Collider = Collider;
        }
    }
    private readonly ServerMapCollider[] _colliders;
    private readonly Node[] _nodes;
    private readonly bool _rotated;
    private readonly bool _exactBoxes;
    private int _count;

    public ServerMapCollisionIndex(ServerMapCollider[] colliders, bool exactBoxes = false)
    {
        _colliders = colliders;
        _exactBoxes = exactBoxes;
        _nodes = new Node[Math.Max(1, colliders.Length * 2)];
        _rotated = colliders.Any(c => c.ColliderType == ServerMapColliderType.Box && c.Rotation != Vector3.Zero);
        if (colliders.Length > 0)
        {
            int[] indices = Enumerable.Range(0, colliders.Length).ToArray();
            Build(indices, 0, indices.Length);
        }
    }

    private int Build(int[] indices, int start, int count)
    {
        int nodeIndex = _count++;
        Vector3 min = new(float.PositiveInfinity), max = new(float.NegativeInfinity);
        for (int i = start; i < start + count; i++)
        {
            var bounds = _colliders[indices[i]].Bounds();
            min = Vector3.Min(min, bounds.Min); max = Vector3.Max(max, bounds.Max);
        }
        if (count == 1) _nodes[nodeIndex] = new(min, max, -1, -1, indices[start]);
        else
        {
            Vector3 size = max - min;
            int axis = size.X >= size.Y && size.X >= size.Z ? 0 : size.Y >= size.Z ? 1 : 2;
            Array.Sort(indices, start, count, Comparer<int>.Create((a, b) =>
                Coordinate(_colliders[a].Center).CompareTo(Coordinate(_colliders[b].Center))));
            int left = Build(indices, start, count / 2);
            int right = Build(indices, start + count / 2, count - count / 2);
            _nodes[nodeIndex] = new(min, max, left, right, -1);
            float Coordinate(Vector3 v) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;
        }
        return nodeIndex;
    }

    public bool Intersects(Vector3 from, Vector3 to, float radius, float height, bool sweep)
    {
        if (_colliders.Length == 0) return false;
        Vector3 offset = new(0, height * 0.5f, 0);
        // The existing rotated-box predicate expands in collider-local coordinates.
        // Its broad phase must also enclose that rotated expansion, not just a world capsule.
        float reach = MathF.Sqrt(radius * radius * 2 + height * height * 0.25f);
        Vector3 extent = _rotated ? new Vector3(reach) : new Vector3(radius, height * 0.5f, radius);
        Vector3 min = Vector3.Min(from, to) + offset - extent - new Vector3(0.0001f);
        Vector3 max = Vector3.Max(from, to) + offset + extent + new Vector3(0.0001f);
        return Visit(0);

        bool Visit(int index)
        {
            Node node = _nodes[index];
            if (min.X > node.Max.X || max.X < node.Min.X || min.Y > node.Max.Y ||
                max.Y < node.Min.Y || min.Z > node.Max.Z || max.Z < node.Min.Z) return false;
            if (node.Collider >= 0)
            {
                if (_exactBoxes) return _colliders[node.Collider].IntersectsPlayerExactSweep(from, to, radius, height);
                return sweep ? _colliders[node.Collider].IntersectsPlayerSweep(from, to, radius, height)
                    : _colliders[node.Collider].IntersectsPlayer(from, radius, height);
            }
            return Visit(node.Left) || Visit(node.Right);
        }
    }
}

}
