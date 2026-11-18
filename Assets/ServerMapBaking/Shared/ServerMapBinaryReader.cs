using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ServerMapBaking
{
    public static class ServerMapBinaryReader
    {
        public static float[] ReadHeightfield(string path, int width, int height)
        {
            int count = checked(width * height);
            float[] values = new float[count];
            using (FileStream stream = File.OpenRead(path))
            using (BinaryReader reader = new BinaryReader(stream, Encoding.UTF8, false))
            {
                if (stream.Length != count * sizeof(float))
                {
                    throw new InvalidDataException("Heightfield byte length does not match manifest dimensions.");
                }

                for (int index = 0; index < count; index++)
                {
                    values[index] = reader.ReadSingle();
                }
            }

            return values;
        }

        public static byte[] ReadWalkable(string path, int width, int height)
        {
            int count = checked(width * height);
            byte[] values = File.ReadAllBytes(path);
            if (values.Length != count)
            {
                throw new InvalidDataException("Walkable byte length does not match manifest dimensions.");
            }

            return values;
        }

        public static NavMeshTriangleData ReadNavMesh(string path)
        {
            NavMeshTriangleData navMesh = new NavMeshTriangleData();
            using (FileStream stream = File.OpenRead(path))
            using (BinaryReader reader = new BinaryReader(stream, Encoding.UTF8, false))
            {
                int vertexCount = ReadCount(reader);
                navMesh.Vertices = new Float3[vertexCount];
                for (int index = 0; index < vertexCount; index++)
                {
                    navMesh.Vertices[index] = ReadFloat3(reader);
                }

                int indexCount = ReadCount(reader);
                navMesh.Indices = new int[indexCount];
                for (int index = 0; index < indexCount; index++)
                {
                    navMesh.Indices[index] = reader.ReadInt32();
                }

                int areaCount = ReadCount(reader);
                navMesh.Areas = new int[areaCount];
                for (int index = 0; index < areaCount; index++)
                {
                    navMesh.Areas[index] = reader.ReadInt32();
                }
            }

            return navMesh;
        }

        public static List<ColliderRecordData> ReadColliders(string path)
        {
            List<ColliderRecordData> colliders = new List<ColliderRecordData>();
            using (FileStream stream = File.OpenRead(path))
            using (BinaryReader reader = new BinaryReader(stream, Encoding.UTF8, false))
            {
                byte[] magic = reader.ReadBytes(4);
                if (magic.Length != 4 ||
                    magic[0] != (byte)'G' || magic[1] != (byte)'C' ||
                    magic[2] != (byte)'O' || magic[3] != (byte)'L')
                {
                    throw new InvalidDataException("Invalid collider file magic.");
                }

                ushort version = reader.ReadUInt16();
                ushort headerSize = reader.ReadUInt16();
                uint colliderCount = reader.ReadUInt32();
                uint recordBytes = reader.ReadUInt32();
                reader.ReadUInt32();

                if (version != ServerMapFormat.ColliderFileVersion ||
                    headerSize != ServerMapFormat.ColliderHeaderSize ||
                    recordBytes != ServerMapFormat.ColliderRecordBytes)
                {
                    throw new InvalidDataException("Unsupported collider file version or record size.");
                }

                for (uint index = 0; index < colliderCount; index++)
                {
                    colliders.Add(new ColliderRecordData
                    {
                        ColliderId = reader.ReadUInt32(),
                        ColliderType = (ServerColliderType)reader.ReadUInt32(),
                        Flags = reader.ReadUInt32(),
                        Center = ReadFloat3(reader),
                        Rotation = ReadFloat3(reader),
                        Size = ReadFloat3(reader)
                    });
                }
            }

            return colliders;
        }

        private static int ReadCount(BinaryReader reader)
        {
            int count = reader.ReadInt32();
            if (count < 0 || count > 100000000)
            {
                throw new InvalidDataException("Invalid collection count in server map file.");
            }

            return count;
        }

        private static Float3 ReadFloat3(BinaryReader reader)
        {
            return new Float3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        }
    }
}
