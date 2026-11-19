// Adapted from Game.BattleCore/Maps/MapTraversal.cs. Source SHA256: e65c024c77c659fb13db63f220740ed6f6353f587ad9eb0ed2675f111a503631
// Geometry predicates retain backend arithmetic; changes are C# 9/runtime I/O compatibility only.
#nullable enable annotations
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace Game.Client.MapTraversal.Geometry
{

internal static class MapTraversal
{
    // Each yielded item is one bounded unit of work, shared by direct checks, A*,
    // smoothing and actual movement. Ground sweeps visit every crossed height cell.
    internal static IEnumerable<bool> Steps(ServerMapData map, double ax, double ay, double az,
        double bx, double by, double bz, double radius, double height, bool flying, bool requireNavigation = true)
    {
        if (!PortableMath.IsFinite(ax + ay + az + bx + by + bz) ||
            !map.IsInsideBounds(ax, az) || !map.IsInsideBounds(bx, bz))
        { yield return false; yield break; }
        if (flying)
        {
            yield return !map.HasCollisionAlongSegment(ax, ay, az, bx, by, bz, radius, height);
            yield break;
        }
        if (map.Manifest.FormatVersion == 3)
        {
            double dx3=bx-ax,dz3=bz-az;
            int pieces=Math.Max(1,(int)Math.Ceiling(Math.Sqrt(dx3*dx3+dz3*dz3)/Math.Min(.5,map.Manifest.CellSize)));
            double previousX=ax,previousZ=az,previousY3=ay;
            for(int piece=1;piece<=pieces;piece++)
            {
                double t3=(double)piece/pieces,x3=ax+dx3*t3,z3=az+dz3*t3;
                bool clear=map.CanTraverseGroundSegment(previousX,previousY3,previousZ,x3,z3,radius,height,out double nextY);
                yield return clear;
                if(!clear)yield break;
                previousX=x3;previousZ=z3;previousY3=nextY;
            }
            yield break;
        }
        bool IsTraversableCell(int gridX, int gridZ) => requireNavigation
            ? map.IsNavigationCell(gridX, gridZ)
            : map.IsWalkableCell(gridX, gridZ);

        bool startNavigation = map.TryGetNavigationCell(ax, az, out int x, out int z);
        if ((requireNavigation && (!startNavigation || !map.TryGetNavigationCell(bx, bz, out _, out _))) ||
            !map.TryGetGroundHeight(ax, az, out _) ||
            !map.TryGetGroundHeight(bx, bz, out double lastGround) ||
            map.HasCollision(ax, ay, az, radius, height) ||
            map.HasCollision(bx, lastGround, bz, radius, height))
        { yield return false; yield break; }
        yield return true;

        double size = map.Manifest.CellSize, dx = bx - ax, dz = bz - az;
        int sx = Math.Sign(dx), sz = Math.Sign(dz);
        double tx = sx == 0 ? double.PositiveInfinity :
            (map.Manifest.Origin.X + (x + (sx > 0 ? 1 : 0)) * size - ax) / dx;
        double tz = sz == 0 ? double.PositiveInfinity :
            (map.Manifest.Origin.Z + (z + (sz > 0 ? 1 : 0)) * size - az) / dz;
        double stepX = sx == 0 ? double.PositiveInfinity : size / Math.Abs(dx);
        double stepZ = sz == 0 ? double.PositiveInfinity : size / Math.Abs(dz);
        double t = 0, previousGround = ay;
        double tolerance = requireNavigation ? Math.Max(0.6, size * 1.2) : .301;
        while (t < 1)
        {
            double next = Math.Min(1, Math.Min(tx, tz));
            if (next > t + 1e-12)
            {
                double mid = (t + next) * 0.5;
                if (!IsTraversableCell(x, z) ||
                    !map.TryGetGroundHeight(ax + dx * mid, az + dz * mid, out double ground) ||
                    (requireNavigation && (Math.Abs(ground - (ay + (by - ay) * t)) > tolerance ||
                        Math.Abs(ground - (ay + (by - ay) * next)) > tolerance)) ||
                    Math.Abs(ground - previousGround) > tolerance ||
                    map.HasCollisionAlongSegment(ax + dx * t, previousGround, az + dz * t,
                        ax + dx * t, ground, az + dz * t, radius, height) ||
                    map.HasCollisionAlongSegment(ax + dx * t, ground, az + dz * t,
                        ax + dx * next, ground, az + dz * next, radius, height))
                { yield return false; yield break; }
                previousGround = ground;
                yield return true;
            }
            if (next >= 1) break;
            bool crossX = tx <= next + 1e-12, crossZ = tz <= next + 1e-12;
            if (crossX && crossZ &&
                (!IsTraversableCell(x + sx, z) || !IsTraversableCell(x, z + sz)))
            { yield return false; yield break; }
            if (crossX) { x += sx; tx += stepX; }
            if (crossZ) { z += sz; tz += stepZ; }
            t = next;
        }
        yield return Math.Abs(lastGround - previousGround) <= tolerance &&
            !map.HasCollisionAlongSegment(bx, previousGround, bz, bx, lastGround, bz, radius, height);
    }

    internal static bool IsClear(ServerMapData map, double ax, double ay, double az,
        double bx, double by, double bz, double radius, double height, bool flying, bool requireNavigation = true)
    {
        foreach (bool clear in Steps(map, ax, ay, az, bx, by, bz, radius, height, flying, requireNavigation))
            if (!clear) return false;
        return true;
    }
}

}
