// Adapted from Game.BattleCore/Maps/ServerMapCollider.cs. Source SHA256: 6ff1bed915038d50e337a2f079a78bb0f6b9913747ced65e3ad15e81b2e68e90
// Geometry predicates retain backend arithmetic; changes are C# 9/runtime I/O compatibility only.
#nullable enable annotations
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace Game.Client.MapTraversal.Geometry
{

public sealed class ServerMapCollider
{
    public readonly uint ColliderId;
    public readonly ServerMapColliderType ColliderType;
    public readonly uint Flags;
    public readonly Vector3 Center;
    public readonly Vector3 Rotation;
    public readonly Vector3 Size;
    public ServerMapCollider(uint ColliderId, ServerMapColliderType ColliderType, uint Flags, Vector3 Center, Vector3 Rotation, Vector3 Size)
    {
        this.ColliderId = ColliderId;
        this.ColliderType = ColliderType;
        this.Flags = Flags;
        this.Center = Center;
        this.Rotation = Rotation;
        this.Size = Size;
    }

    public bool IntersectsPlayer(Vector3 feetPosition, float radius, float height)
    {
        return ColliderType switch
        {
            ServerMapColliderType.Box => IntersectsBox(feetPosition, radius, height),
            ServerMapColliderType.Sphere => IntersectsSphere(feetPosition, radius, height),
            ServerMapColliderType.Capsule => IntersectsCapsule(feetPosition, radius, height),
            _ => false
        };
    }

    private bool IntersectsBox(Vector3 feetPosition, float radius, float height)
    {
        Vector3 playerCenter = feetPosition + new Vector3(0, height * 0.5f, 0);
        Vector3 local = Rotation == Vector3.Zero ? playerCenter - Center : InverseUnityEuler(playerCenter - Center, Rotation);
        Vector3 half = Size * 0.5f;

        return MathF.Abs(local.X) < half.X + radius &&
               MathF.Abs(local.Y) < half.Y + height * 0.5f - 0.001f &&
               MathF.Abs(local.Z) < half.Z + radius;
    }

    internal (Vector3 Min, Vector3 Max) Bounds()
    {
        Vector3 half = Size * 0.5f;
        if (ColliderType == ServerMapColliderType.Box && Rotation != Vector3.Zero)
        {
            Vector3 x = Forward(Vector3.UnitX), y = Forward(Vector3.UnitY), z = Forward(Vector3.UnitZ);
            half = Vector3.Abs(x) * half.X + Vector3.Abs(y) * half.Y + Vector3.Abs(z) * half.Z;
        }
        else if (ColliderType == ServerMapColliderType.Sphere)
            half = new Vector3(Size.X * 0.5f);
        else if (ColliderType == ServerMapColliderType.Capsule)
            half = new Vector3(Size.X * 0.5f, MathF.Max(Size.X, Size.Y) * 0.5f, Size.X * 0.5f);
        return (Center - half, Center + half);

        Vector3 Forward(Vector3 v) => RotateY(RotateX(RotateZ(v, DegreesToRadians(Rotation.Z)),
            DegreesToRadians(Rotation.X)), DegreesToRadians(Rotation.Y));
    }

    internal bool IntersectsPlayerSweep(Vector3 from, Vector3 to, float radius, float height)
    {
        if (ColliderType == ServerMapColliderType.Box)
        {
            Vector3 a = from + new Vector3(0, height * 0.5f, 0) - Center;
            Vector3 b = to + new Vector3(0, height * 0.5f, 0) - Center;
            if (Rotation != Vector3.Zero)
            {
                a = InverseUnityEuler(a, Rotation);
                b = InverseUnityEuler(b, Rotation);
            }
            Vector3 half = Size * 0.5f + new Vector3(radius, height * 0.5f - 0.001f, radius);
            double enter = 0, exit = 1;
            return Clip(a.X, b.X, half.X, ref enter, ref exit) &&
                Clip(a.Y, b.Y, half.Y, ref enter, ref exit) &&
                Clip(a.Z, b.Z, half.Z, ref enter, ref exit) && enter < exit;
        }

        // Both capsule axes are vertical. Their squared distance during a sweep is
        // a piecewise quadratic; test its minimum in each vertical-overlap interval.
        double colliderRadius = Size.X * 0.5;
        double segmentHalf = ColliderType == ServerMapColliderType.Capsule
            ? Math.Max(0, Size.Y - Size.X) * 0.5 : 0;
        double low = Center.Y - segmentHalf, high = Center.Y + segmentHalf;
        double dx = to.X - from.X, dy = to.Y - from.Y, dz = to.Z - from.Z;
        double px = from.X - Center.X, pz = from.Z - Center.Z;
        Span<double> times = stackalloc double[4];
        int count = 2;
        times[0] = 0; times[1] = 1;
        if (Math.Abs(dy) > 1e-12)
        {
            double t = (high - from.Y - radius) / dy;
            if (t > 0 && t < 1) times[count++] = t;
            t = (low - from.Y - Math.Max(radius, height - radius)) / dy;
            if (t > 0 && t < 1) times[count++] = t;
        }
        PortableMath.Sort(times.Slice(0, count));
        double combined = colliderRadius + radius;
        for (int i = 1; i < count; i++)
        {
            double midY = from.Y + dy * ((times[i - 1] + times[i]) * 0.5);
            double gap = 0, slope = 0;
            if (midY + radius > high) { gap = from.Y + radius - high; slope = dy; }
            else if (midY + Math.Max(radius, height - radius) < low)
            { gap = low - from.Y - Math.Max(radius, height - radius); slope = -dy; }
            double denominator = dx * dx + dz * dz + slope * slope;
            double t = denominator > 0
                ? PortableMath.Clamp(-(px * dx + pz * dz + gap * slope) / denominator, times[i - 1], times[i])
                : times[i - 1];
            double x = px + dx * t, z = pz + dz * t, y = gap + slope * t;
            if (x * x + y * y + z * z <= combined * combined) return true;
        }
        return false;
    }

    internal bool IntersectsPlayerExactSweep(Vector3 from, Vector3 to, float radius, float height)
    {
        if (ColliderType != ServerMapColliderType.Box)
            return IntersectsPlayerSweep(from, to, radius, height);
        Vector3 half = Size * .5f;
        Vector3 low = InverseUnityEuler(from + new Vector3(0, radius, 0) - Center, Rotation);
        Vector3 high = InverseUnityEuler(from + new Vector3(0, MathF.Max(radius, height - radius), 0) - Center, Rotation);
        Vector3 delta = InverseUnityEuler(to - from, Rotation);
        if (Inside(low) || Inside(high) || Inside(low + delta) || Inside(high + delta)) return true;
        Span<Vector3> corners = stackalloc Vector3[8];
        for (int i = 0; i < 8; i++)
            corners[i] = new Vector3((i & 1) == 0 ? -half.X : half.X,
                (i & 2) == 0 ? -half.Y : half.Y, (i & 4) == 0 ? -half.Z : half.Z);
        float threshold = MathF.Max(0, radius - .0001f);
        threshold *= threshold;
        for (int i = 0; i < BoxTriangleIndices.Length; i += 3)
            if (TriangleDistance.SweptAxisSquared(low, high, delta, corners[BoxTriangleIndices[i]],
                corners[BoxTriangleIndices[i + 1]], corners[BoxTriangleIndices[i + 2]]) < threshold) return true;
        return false;

        bool Inside(Vector3 p) => MathF.Abs(p.X) <= half.X && MathF.Abs(p.Y) <= half.Y && MathF.Abs(p.Z) <= half.Z;
    }

    private static readonly int[] BoxTriangleIndices = new int[] {0, 2, 1, 1, 2, 3, 4, 5, 6, 5, 7, 6,
        0, 4, 2, 2, 4, 6, 1, 3, 5, 3, 7, 5, 0, 1, 4, 1, 5, 4, 2, 6, 3, 3, 6, 7};

    private static bool Clip(double a, double b, double half, ref double enter, ref double exit)
    {
        double delta = b - a;
        if (Math.Abs(delta) < 1e-12) return Math.Abs(a) < half;
        double first = (-half - a) / delta, second = (half - a) / delta;
        enter = Math.Max(enter, Math.Min(first, second));
        exit = Math.Min(exit, Math.Max(first, second));
        return enter < exit;
    }

    private bool IntersectsSphere(Vector3 feetPosition, float radius, float height)
    {
        float colliderRadius = Size.X * 0.5f;
        Vector3 closest = ClosestPointOnVerticalSegment(
            Center,
            feetPosition + new Vector3(0, radius, 0),
            feetPosition + new Vector3(0, MathF.Max(radius, height - radius), 0));
        float combinedRadius = colliderRadius + radius;
        return Vector3.DistanceSquared(Center, closest) <= combinedRadius * combinedRadius;
    }

    private bool IntersectsCapsule(Vector3 feetPosition, float radius, float height)
    {
        float colliderRadius = Size.X * 0.5f;
        float colliderSegmentHalf = MathF.Max(0, Size.Y - colliderRadius * 2) * 0.5f;
        Vector3 colliderStart = Center - new Vector3(0, colliderSegmentHalf, 0);
        Vector3 colliderEnd = Center + new Vector3(0, colliderSegmentHalf, 0);
        Vector3 playerStart = feetPosition + new Vector3(0, radius, 0);
        Vector3 playerEnd = feetPosition + new Vector3(0, MathF.Max(radius, height - radius), 0);
        float combinedRadius = colliderRadius + radius;
        return SegmentDistanceSquared(colliderStart, colliderEnd, playerStart, playerEnd) <=
               combinedRadius * combinedRadius;
    }

    private static Vector3 ClosestPointOnVerticalSegment(Vector3 point, Vector3 start, Vector3 end)
    {
        float y = PortableMath.Clamp(point.Y, MathF.Min(start.Y, end.Y), MathF.Max(start.Y, end.Y));
        return new Vector3(start.X, y, start.Z);
    }

    private static float SegmentDistanceSquared(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2)
    {
        const float epsilon = 1e-6f;
        Vector3 d1 = q1 - p1;
        Vector3 d2 = q2 - p2;
        Vector3 r = p1 - p2;
        float a = Vector3.Dot(d1, d1);
        float e = Vector3.Dot(d2, d2);
        float f = Vector3.Dot(d2, r);
        float s;
        float t;

        if (a <= epsilon && e <= epsilon)
            return Vector3.DistanceSquared(p1, p2);

        if (a <= epsilon)
        {
            s = 0;
            t = PortableMath.Clamp(f / e, 0, 1);
        }
        else
        {
            float c = Vector3.Dot(d1, r);
            if (e <= epsilon)
            {
                t = 0;
                s = PortableMath.Clamp(-c / a, 0, 1);
            }
            else
            {
                float b = Vector3.Dot(d1, d2);
                float denominator = a * e - b * b;
                s = denominator == 0 ? 0 : PortableMath.Clamp((b * f - c * e) / denominator, 0, 1);
                t = (b * s + f) / e;

                if (t < 0)
                {
                    t = 0;
                    s = PortableMath.Clamp(-c / a, 0, 1);
                }
                else if (t > 1)
                {
                    t = 1;
                    s = PortableMath.Clamp((b - c) / a, 0, 1);
                }
            }
        }

        Vector3 c1 = p1 + d1 * s;
        Vector3 c2 = p2 + d2 * t;
        return Vector3.DistanceSquared(c1, c2);
    }

    private static float DegreesToRadians(float degrees) => degrees * (MathF.PI / 180f);

    private static Vector3 InverseUnityEuler(Vector3 value, Vector3 eulerDegrees)
    {
        value = RotateY(value, -DegreesToRadians(eulerDegrees.Y));
        value = RotateX(value, -DegreesToRadians(eulerDegrees.X));
        return RotateZ(value, -DegreesToRadians(eulerDegrees.Z));
    }

    private static Vector3 RotateX(Vector3 value, float radians)
    {
        float sin = MathF.Sin(radians);
        float cos = MathF.Cos(radians);
        return new Vector3(value.X, value.Y * cos - value.Z * sin, value.Y * sin + value.Z * cos);
    }

    private static Vector3 RotateY(Vector3 value, float radians)
    {
        float sin = MathF.Sin(radians);
        float cos = MathF.Cos(radians);
        return new Vector3(value.X * cos + value.Z * sin, value.Y, -value.X * sin + value.Z * cos);
    }

    private static Vector3 RotateZ(Vector3 value, float radians)
    {
        float sin = MathF.Sin(radians);
        float cos = MathF.Cos(radians);
        return new Vector3(value.X * cos - value.Y * sin, value.X * sin + value.Y * cos, value.Z);
    }
}

public enum ServerMapColliderType : uint
{
    Box = 1,
    Sphere = 2,
    Capsule = 3,
    TriangleMesh = 4
}

}
