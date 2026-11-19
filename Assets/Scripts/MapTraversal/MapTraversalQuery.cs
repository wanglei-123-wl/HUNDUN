using System;
using Game.Client.MapTraversal.Geometry;

namespace Game.Client.MapTraversal
{
    public sealed class MapTraversalQuery
    {
        private readonly ServerMapData data;
        internal MapTraversalQuery(ServerMapData data, string fingerprint)
        {
            this.data = data;
            PackageFingerprint = fingerprint;
        }

        public string SceneId => data.Manifest.SceneId;
        public string MapId => data.Manifest.MapId;
        public int FormatVersion => data.Manifest.FormatVersion;
        public string PackageFingerprint { get; }
        public string QuerySourceFingerprint => MapTraversalBuildInfo.QuerySourceFingerprint;
        public double CollisionSkinWidth => data.Manifest.CollisionSkinWidth;
        public double CollisionRootOffsetY => data.Manifest.CollisionRootOffsetY;
        public bool IsInsideBounds(double x, double z) => PortableMath.IsFinite(x) && PortableMath.IsFinite(z)
            && data.IsInsideBounds(x, z);
        public bool TryGetGroundHeight(double x, double z, out double y) => data.TryGetGroundHeight(x, z, out y);

        public bool TryGetStandingHeight(double x, double z, double radius, double height, out double y)
        {
            y = 0;
            return ValidBody(radius, height) && IsInsideBounds(x, z)
                && data.TryGetStandingHeight(x, z, radius, height, out y);
        }

        public bool CanTraverseGroundSegment(double ax, double ay, double az, double bx, double bz,
            double radius, double height, out double endY)
        {
            endY = ay;
            if (!ValidBody(radius, height) || !PortableMath.IsFinite(ay)
                || !IsInsideBounds(ax, az) || !IsInsideBounds(bx, bz)) return false;
            if (FormatVersion == 3)
                return data.CanTraverseGroundSegment(ax, ay, az, bx, bz, radius, height, out endY);
            return data.TryGetGroundHeight(bx, bz, out endY)
                && Geometry.MapTraversal.IsClear(data, ax, ay, az, bx, endY, bz, radius, height,
                    flying: false, requireNavigation: false);
        }

        public bool HasCollisionAlongSegment(double ax, double ay, double az, double bx, double by, double bz,
            double radius, double height) => !ValidBody(radius, height)
            || !PortableMath.IsFinite(ay) || !PortableMath.IsFinite(by)
            || !IsInsideBounds(ax, az) || !IsInsideBounds(bx, bz)
            || data.HasCollisionAlongSegment(ax, ay, az, bx, by, bz, radius, height);

        private static bool ValidBody(double radius, double height) => PortableMath.IsFinite(radius)
            && PortableMath.IsFinite(height) && radius > 0 && height >= radius * 2;
    }
}
