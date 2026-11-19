// Adapted from Game.BattleCore/Maps/ServerMapGroundSurfaceIndex.cs. Source SHA256: 8cc5e3b068351898e7ccf6703a4cf458b302ba0618a2df0f3ce00eec9fef6f11
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

// V3's selected road sheet. The grid is an accelerator, never a replacement
// for the actual footprint or the height at the queried position.
internal sealed class ServerMapGroundSurfaceIndex
{
    private readonly struct Face
    {
        public readonly Vector3 A;
        public readonly Vector3 B;
        public readonly Vector3 C;
        public Face(Vector3 A, Vector3 B, Vector3 C)
        {
            this.A = A;
            this.B = B;
            this.C = C;
        }

        public Vector2 Min => new(MathF.Min(A.X, MathF.Min(B.X, C.X)), MathF.Min(A.Z, MathF.Min(B.Z, C.Z)));
        public Vector2 Max => new(MathF.Max(A.X, MathF.Max(B.X, C.X)), MathF.Max(A.Z, MathF.Max(B.Z, C.Z)));
        public Vector2 Center => new((A.X + B.X + C.X) / 3, (A.Z + B.Z + C.Z) / 3);
    }
    private readonly struct Node
    {
        public readonly Vector2 Min;
        public readonly Vector2 Max;
        public readonly int Start;
        public readonly int Count;
        public readonly int Left;
        public readonly int Right;
        public Node(Vector2 Min, Vector2 Max, int Start, int Count, int Left, int Right)
        {
            this.Min = Min;
            this.Max = Max;
            this.Start = Start;
            this.Count = Count;
            this.Left = Left;
            this.Right = Right;
        }
    }
    private readonly Face[] _faces;
    private readonly int[] _order;
    private readonly Node[] _nodes;
    private int _nodeCount;
    public int TriangleCount => _faces.Length;

    private ServerMapGroundSurfaceIndex(Face[] faces)
    {
        _faces = faces;
        _order = Enumerable.Range(0, faces.Length).ToArray();
        _nodes = new Node[faces.Length / 2 + 1];
        Build(0, faces.Length);
        Array.Resize(ref _nodes, _nodeCount);
    }

    public static ServerMapGroundSurfaceIndex Load(byte[] data, ServerMapManifest manifest)
    {
        using var reader = new BinaryReader(new MemoryStream(data, false));
        if (reader.BaseStream.Length < 16 || Encoding.ASCII.GetString(reader.ReadBytes(4)) != "GGRD" ||
            reader.ReadUInt16() != 1 || reader.ReadUInt16() != 16)
            throw new InvalidDataException("Ground surface header is invalid.");
        uint count = reader.ReadUInt32();
        if (reader.ReadUInt32() != 0 || count == 0 || count > 20_000_000 ||
            reader.BaseStream.Length != 16L + count * 36L)
            throw new InvalidDataException("Ground surface count or length is invalid.");
        var faces = new Face[(int)count];
        for (int i = 0; i < faces.Length; i++)
        {
            Vector3 a = Vertex(), b = Vertex(), c = Vertex();
            double area = ((double)b.X - a.X) * (c.Z - a.Z) - ((double)b.Z - a.Z) * (c.X - a.X);
            if (Math.Abs(area) < 1e-10) throw new InvalidDataException("Ground triangle has no horizontal area.");
            faces[i] = new(a, b, c);
        }
        return new(faces);

        Vector3 Vertex()
        {
            var v = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            if (!PortableMath.IsFinite(v.X) || !PortableMath.IsFinite(v.Y) || !PortableMath.IsFinite(v.Z) ||
                v.X < manifest.BoundsMin.X - .001 || v.X > manifest.BoundsMax.X + .001 ||
                v.Z < manifest.BoundsMin.Z - .001 || v.Z > manifest.BoundsMax.Z + .001 ||
                v.Y < manifest.BoundsMin.Y - .001 || v.Y > manifest.BoundsMax.Y + .001)
                throw new InvalidDataException("Ground surface vertex is invalid or outside map bounds.");
            return v;
        }
    }

    public bool TryGetHeight(double x, double z, out double height)
    {
        double selected = double.NegativeInfinity;
        if (PortableMath.IsFinite(x) && PortableMath.IsFinite(z)) Visit(0);
        height = PortableMath.IsFinite(selected) ? selected : 0;
        return PortableMath.IsFinite(selected);

        void Visit(int i)
        {
            var node = _nodes[i];
            if (x < node.Min.X - .0001 || x > node.Max.X + .0001 || z < node.Min.Y - .0001 || z > node.Max.Y + .0001) return;
            if (node.Count == 0) { Visit(node.Left); Visit(node.Right); return; }
            for (int n = node.Start; n < node.Start + node.Count; n++)
            {
                var f = _faces[_order[n]];
                double dx1 = (double)f.B.X - f.A.X, dz1 = (double)f.B.Z - f.A.Z;
                double dx2 = (double)f.C.X - f.A.X, dz2 = (double)f.C.Z - f.A.Z;
                double den = dx1 * dz2 - dz1 * dx2;
                double u = ((x - f.A.X) * dz2 - (z - f.A.Z) * dx2) / den;
                double v = (dx1 * (z - f.A.Z) - dz1 * (x - f.A.X)) / den;
                double y=f.A.Y+u*(f.B.Y-f.A.Y)+v*(f.C.Y-f.A.Y);
                if(u>=-1e-9&&v>=-1e-9&&u+v<=1.000000001)
                {selected=Math.Max(selected,y);continue;}
                // A distance tolerance must not extrapolate a very thin face:
                // its offset half-planes can meet far beyond a sharp vertex.
                // Project onto a real edge and retain the closest support.
                double nearestSquared=double.PositiveInfinity,nearby=double.NegativeInfinity;
                Edge(f.A,f.B);Edge(f.B,f.C);Edge(f.C,f.A);
                if(nearestSquared<=1e-8)selected=Math.Max(selected,nearby);
                void Edge(Vector3 a,Vector3 b)
                {
                    double ex=(double)b.X-a.X,ez=(double)b.Z-a.Z,length=ex*ex+ez*ez;
                    double t=length>0?PortableMath.Clamp(((x-a.X)*ex+(z-a.Z)*ez)/length,0,1):0;
                    double px=a.X+t*ex-x,pz=a.Z+t*ez-z,distance=px*px+pz*pz;
                    if(distance>1e-8||distance>nearestSquared+1e-16)return;
                    double edgeY=a.Y+t*(b.Y-a.Y);
                    nearby=distance<nearestSquared-1e-16?edgeY:Math.Max(nearby,edgeY);
                    nearestSquared=Math.Min(nearestSquared,distance);
                }
            }
        }
    }

    // Partition at every crossed footprint edge. Sampling a fixed number of
    // points can miss a narrow unsupported slit, even with clear endpoints.
    public List<double> SegmentBreaks(double ax, double az, double bx, double bz)
    {
        var breaks = new List<double> { 0, 1 };
        Visit(0);
        breaks.Sort();
        return breaks;
        void Visit(int i)
        {
            var n = _nodes[i];
            if (Math.Min(ax, bx) > n.Max.X || Math.Max(ax, bx) < n.Min.X ||
                Math.Min(az, bz) > n.Max.Y || Math.Max(az, bz) < n.Min.Y) return;
            if (n.Count == 0) { Visit(n.Left); Visit(n.Right); return; }
            for (int k = n.Start; k < n.Start + n.Count; k++)
            {
                var f = _faces[_order[k]];
                double area = ((double)f.B.X - f.A.X) * (f.C.Z - f.A.Z) - ((double)f.B.Z - f.A.Z) * (f.C.X - f.A.X);
                double sign = Math.Sign(area), lo = 0, hi = 1;
                if (Clip(f.A, f.B) && Clip(f.B, f.C) && Clip(f.C, f.A) && hi > lo)
                { breaks.Add(lo); breaks.Add(hi); }
                bool Clip(Vector3 p, Vector3 q)
                {
                    double start = sign * (((double)q.X - p.X) * (az - p.Z) - ((double)q.Z - p.Z) * (ax - p.X));
                    double delta = sign * (((double)q.X - p.X) * (bz - az) - ((double)q.Z - p.Z) * (bx - ax));
                    if (Math.Abs(delta) < 1e-14) return start >= -1e-10;
                    double t = -start / delta;
                    if (delta > 0) lo = Math.Max(lo, t); else hi = Math.Min(hi, t);
                    return hi >= lo;
                }
            }
        }
    }

    private int Build(int start, int count)
    {
        int index = _nodeCount++;
        Vector2 min = new(float.PositiveInfinity), max = new(float.NegativeInfinity);
        for (int n = start; n < start + count; n++)
        { var face = _faces[_order[n]]; min = Vector2.Min(min, face.Min); max = Vector2.Max(max, face.Max); }
        if (count <= 8) _nodes[index] = new(min, max, start, count, -1, -1);
        else
        {
            bool splitX = max.X - min.X >= max.Y - min.Y;
            PartitionMedian(start,count,splitX);
            int left = Build(start, count / 2), right = Build(start + count / 2, count - count / 2);
            _nodes[index] = new(min, max, 0, 0, left, right);
        }
        return index;
    }

    private void PartitionMedian(int start,int count,bool splitX)
    {
        int low=start,high=start+count-1,middle=start+count/2;
        while(low<high)
        {
            float pivot=Coordinate(_order[low+(high-low)/2]);
            int left=low,right=high;
            while(left<=right)
            {
                while(Coordinate(_order[left])<pivot)left++;
                while(Coordinate(_order[right])>pivot)right--;
                if(left<=right){(_order[left],_order[right])=(_order[right],_order[left]);left++;right--;}
            }
            if(middle<=right)high=right;
            else if(middle>=left)low=left;
            else return;
        }
        float Coordinate(int i)=>splitX?_faces[i].Center.X:_faces[i].Center.Y;
    }
}

}
