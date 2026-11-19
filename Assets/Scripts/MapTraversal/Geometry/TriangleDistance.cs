// Adapted from Game.BattleCore/Maps/TriangleDistance.cs. Source SHA256: 928dfcc5bd7e0ee6b8651f580c8ff4e16f3b6e79b3264f6ad25dff3e7017738f
// Geometry predicates retain backend arithmetic; changes are C# 9/runtime I/O compatibility only.
#nullable enable annotations
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace Game.Client.MapTraversal.Geometry
{

// Distance predicates for a continuous vertical capsule sweep. The moving capsule axis
// sweeps a filled parallelogram, not just its two endpoint segments.
internal static class TriangleDistance
{
    public static float SweptAxisSquared(Vector3 low, Vector3 high, Vector3 delta,
        Vector3 a, Vector3 b, Vector3 c)
    {
        float motionSquared = delta.LengthSquared();
        if (motionSquared == 0) return Fast.SweptAxisSquared(low, high, delta, a, b, c);
        Vector3 floatAxis = high - low;
        float edgeSquared = MathF.Max(floatAxis.LengthSquared(), motionSquared);
        // Keep the SIMD float path for well-conditioned sweeps. Thin or
        // near-collinear swept faces need the stable predicates below because
        // float barycentrics lose precision when subtracting edge products.
        if (Vector3.Cross(floatAxis, delta).LengthSquared() > 1e-4f * edgeSquared * edgeSquared)
            return Fast.SweptAxisSquared(low, high, delta, a, b, c);
        // Keep tiny sweeps distinct from their axis before constructing the
        // swept faces. Adding float delta to a world-space endpoint can round
        // away the motion and makes a near-vertex road seam look like a wall.
        var origin = new Point3();
        var axis = Point3.Difference(high, low);
        var move = new Point3(delta.X, delta.Y, delta.Z);
        var pa = Point3.Difference(a, low);
        var pb = Point3.Difference(b, low);
        var pc = Point3.Difference(c, low);
        if (move.LengthSquared == 0) return (float)SegmentTriangleSquared(origin, axis, pa, pb, pc);
        return (float)Math.Min(TrianglesSquared(origin, axis, axis + move, pa, pb, pc),
            TrianglesSquared(origin, axis + move, move, pa, pb, pc));
    }

    private static double TrianglesSquared(Point3 a, Point3 b, Point3 c, Point3 d, Point3 e, Point3 f) =>
        Math.Min(Math.Min(Math.Min(SegmentTriangleSquared(a, b, d, e, f),
            SegmentTriangleSquared(b, c, d, e, f)), SegmentTriangleSquared(c, a, d, e, f)),
            Math.Min(Math.Min(SegmentTriangleSquared(d, e, a, b, c),
                SegmentTriangleSquared(e, f, a, b, c)), SegmentTriangleSquared(f, d, a, b, c)));

    private static double SegmentTriangleSquared(Point3 p, Point3 q, Point3 a, Point3 b, Point3 c)
    {
        Point3 ab = b - a, ac = c - a, direction = q - p;
        Point3 cross = Point3.Cross(direction, ac);
        double determinant = Point3.Dot(ab, cross);
        if (determinant != 0)
        {
            double inverse = 1 / determinant;
            Point3 offset = p - a;
            double u = Point3.Dot(offset, cross) * inverse;
            Point3 r = Point3.Cross(offset, ab);
            double v = Point3.Dot(direction, r) * inverse;
            double t = Point3.Dot(ac, r) * inverse;
            if (u >= 0 && v >= 0 && u + v <= 1 && t >= 0 && t <= 1) return 0;
        }
        return Math.Min(Math.Min(PointTriangleSquared(p, a, b, c), PointTriangleSquared(q, a, b, c)),
            Math.Min(SegmentsSquared(p, q, a, b),
                Math.Min(SegmentsSquared(p, q, b, c), SegmentsSquared(p, q, c, a))));
    }

    private static double PointTriangleSquared(Point3 p, Point3 a, Point3 b, Point3 c)
    {
        Point3 ab = b - a, ac = c - a, ap = p - a;
        Point3 normal = Point3.Cross(ab, ac);
        double areaSquared = normal.LengthSquared;
        // Oriented edge tests avoid subtracting almost equal dot products
        // (va/vb/vc) for a very thin swept triangle. A collapsed face is still
        // its three edges, not a zero-distance hit or an unchecked passage.
        if (areaSquared > 0 &&
            Point3.Dot(Point3.Cross(ab, ap), normal) >= 0 &&
            Point3.Dot(Point3.Cross(c - b, p - b), normal) >= 0 &&
            Point3.Dot(Point3.Cross(a - c, p - c), normal) >= 0)
        {
            double height = Point3.Dot(ap, normal);
            return height * height / areaSquared;
        }
        return Math.Min(SegmentsSquared(p, p, a, b),
            Math.Min(SegmentsSquared(p, p, b, c), SegmentsSquared(p, p, c, a)));
    }

    private static double SegmentsSquared(Point3 p, Point3 q, Point3 a, Point3 b)
    {
        Point3 d1 = q - p, d2 = b - a, r = p - a;
        double aa = d1.LengthSquared, ee = d2.LengthSquared, ff = Point3.Dot(d2, r);
        double s, t;
        if (aa == 0 && ee == 0) return r.LengthSquared;
        if (aa == 0) { s = 0; t = PortableMath.Clamp(ff / ee, 0, 1); }
        else
        {
            double cc = Point3.Dot(d1, r);
            if (ee == 0) { t = 0; s = PortableMath.Clamp(-cc / aa, 0, 1); }
            else
            {
                double bb = Point3.Dot(d1, d2);
                Point3 cross = Point3.Cross(d1, d2);
                double denominator = cross.LengthSquared;
                s = denominator > 0 ? PortableMath.Clamp(Point3.Dot(Point3.Cross(d2, r), cross) / denominator, 0, 1) : 0;
                t = (bb * s + ff) / ee;
                if (t < 0) { t = 0; s = PortableMath.Clamp(-cc / aa, 0, 1); }
                else if (t > 1) { t = 1; s = PortableMath.Clamp((bb - cc) / aa, 0, 1); }
            }
        }
        return ((p + d1 * s) - (a + d2 * t)).LengthSquared;
    }

    private readonly struct Point3 { private readonly double _x, _y, _z; public Point3(double x, double y, double z) { _x = x; _y = y; _z = z; }
        public double LengthSquared => Dot(this, this);
        public static Point3 Difference(Vector3 a, Vector3 b) => new((double)a.X - b.X, (double)a.Y - b.Y, (double)a.Z - b.Z);
        public static Point3 operator +(Point3 a, Point3 b) => new(a._x + b._x, a._y + b._y, a._z + b._z);
        public static Point3 operator -(Point3 a, Point3 b) => new(a._x - b._x, a._y - b._y, a._z - b._z);
        public static Point3 operator *(Point3 a, double t) => new(a._x * t, a._y * t, a._z * t);
        public static double Dot(Point3 a, Point3 b) => a._x * b._x + a._y * b._y + a._z * b._z;
        public static Point3 Cross(Point3 a, Point3 b) => new(a._y * b._z - a._z * b._y,
            a._z * b._x - a._x * b._z, a._x * b._y - a._y * b._x);
    }

    private static class Fast
    {
        public static float SweptAxisSquared(Vector3 low, Vector3 high, Vector3 delta, Vector3 a, Vector3 b, Vector3 c)
        {
            if (delta.LengthSquared() == 0) return SegmentTriangleSquared(low, high, a, b, c);
            return MathF.Min(TrianglesSquared(low, high, high + delta, a, b, c),
                TrianglesSquared(low, high + delta, low + delta, a, b, c));
        }
        private static float TrianglesSquared(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 e, Vector3 f) =>
            MathF.Min(MathF.Min(MathF.Min(SegmentTriangleSquared(a, b, d, e, f),
                SegmentTriangleSquared(b, c, d, e, f)), SegmentTriangleSquared(c, a, d, e, f)),
                MathF.Min(MathF.Min(SegmentTriangleSquared(d, e, a, b, c),
                    SegmentTriangleSquared(e, f, a, b, c)), SegmentTriangleSquared(f, d, a, b, c)));
        private static float SegmentTriangleSquared(Vector3 p, Vector3 q, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, direction = q - p, cross = Vector3.Cross(direction, ac);
            double determinant = Vector3.Dot(ab, cross);
            if (Math.Abs(determinant) > 1e-12)
            {
                double inverse = 1 / determinant;
                Vector3 offset = p - a;
                double u = Vector3.Dot(offset, cross) * inverse;
                Vector3 r = Vector3.Cross(offset, ab);
                double v = Vector3.Dot(direction, r) * inverse, t = Vector3.Dot(ac, r) * inverse;
                if (u >= 0 && v >= 0 && u + v <= 1 && t >= 0 && t <= 1) return 0;
            }
            return MathF.Min(MathF.Min(PointTriangleSquared(p, a, b, c), PointTriangleSquared(q, a, b, c)),
                MathF.Min(SegmentsSquared(p, q, a, b), MathF.Min(SegmentsSquared(p, q, b, c), SegmentsSquared(p, q, c, a))));
        }
        private static float PointTriangleSquared(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            if (Vector3.Cross(ab, ac).LengthSquared() < 1e-20f)
                return MathF.Min(SegmentsSquared(p, p, a, b), MathF.Min(SegmentsSquared(p, p, b, c), SegmentsSquared(p, p, c, a)));
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0 && d2 <= 0) return ap.LengthSquared();
            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0 && d4 <= d3) return bp.LengthSquared();
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0 && d1 >= 0 && d3 <= 0) return Vector3.DistanceSquared(p, a + ab * (d1 / (d1 - d3)));
            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0 && d5 <= d6) return cp.LengthSquared();
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0 && d2 >= 0 && d6 <= 0) return Vector3.DistanceSquared(p, a + ac * (d2 / (d2 - d6)));
            float va = d3 * d6 - d5 * d4;
            if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0)
                return Vector3.DistanceSquared(p, b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6))));
            float inverse = 1 / (va + vb + vc);
            return Vector3.DistanceSquared(p, a + ab * (vb * inverse) + ac * (vc * inverse));
        }
        private static float SegmentsSquared(Vector3 p, Vector3 q, Vector3 a, Vector3 b)
        {
            Vector3 d1 = q - p, d2 = b - a, r = p - a;
            float aa = Vector3.Dot(d1, d1), ee = Vector3.Dot(d2, d2), ff = Vector3.Dot(d2, r), s, t;
            if (aa < 1e-20f && ee < 1e-20f) return r.LengthSquared();
            if (aa < 1e-20f) { s = 0; t = PortableMath.Clamp(ff / ee, 0, 1); }
            else
            {
                float cc = Vector3.Dot(d1, r);
                if (ee < 1e-20f) { t = 0; s = PortableMath.Clamp(-cc / aa, 0, 1); }
                else
                {
                    float bb = Vector3.Dot(d1, d2), denominator = aa * ee - bb * bb;
                    s = denominator > 1e-20f ? PortableMath.Clamp((bb * ff - cc * ee) / denominator, 0, 1) : 0;
                    t = (bb * s + ff) / ee;
                    if (t < 0) { t = 0; s = PortableMath.Clamp(-cc / aa, 0, 1); }
                    else if (t > 1) { t = 1; s = PortableMath.Clamp((bb - cc) / aa, 0, 1); }
                }
            }
            return Vector3.DistanceSquared(p + d1 * s, a + d2 * t);
        }
    }
}

}
