// Adapted from Game.BattleCore/Maps/IWorldSurfaceQuery.cs. Source SHA256: 8e85d4e2f800f965854fd8ec21f81df99645e66ad3f90d71cb3818d33e08f123
// Geometry predicates retain backend arithmetic; changes are C# 9/runtime I/O compatibility only.
#nullable enable annotations
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace Game.Client.MapTraversal.Geometry
{

/// <summary>
/// Provides the minimum read-only map queries required to place authoritative world entities.
/// </summary>
public interface IWorldSurfaceQuery
{
    bool IsInsideBounds(double x, double z);

    bool IsWalkable(double x, double z);

    bool TryGetGroundHeight(double x, double z, out double y);

    bool HasCollision(
        double x,
        double y,
        double z,
        double radius,
        double height);
}

}
