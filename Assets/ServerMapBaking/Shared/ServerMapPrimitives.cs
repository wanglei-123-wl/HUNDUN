using System;

namespace ServerMapBaking
{
    [Serializable]
    public struct Float3
    {
        public float X;
        public float Y;
        public float Z;

        public Float3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }
    }

    [Serializable]
    public struct QuaternionData
    {
        public float X;
        public float Y;
        public float Z;
        public float W;

        public QuaternionData(float x, float y, float z, float w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }
    }

    [Serializable]
    public struct BoundsData
    {
        public Float3 Center;
        public Float3 Size;

        public BoundsData(Float3 center, Float3 size)
        {
            Center = center;
            Size = size;
        }
    }
}
