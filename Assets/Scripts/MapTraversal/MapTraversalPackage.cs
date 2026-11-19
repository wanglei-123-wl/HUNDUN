using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Game.Client.MapTraversal.Geometry;
using Newtonsoft.Json;

namespace Game.Client.MapTraversal
{
    [Serializable]
    internal sealed class MapTraversalPackageDescriptor
    {
        public int packageVersion;
        public string sceneId, mapId, querySourceFingerprint, packageFingerprint;
        public MapTraversalPackageFile[] files;
    }

    [Serializable]
    internal sealed class MapTraversalPackageFile
    {
        public string name, sha256;
        public long bytes;
    }

    internal static class MapTraversalPackage
    {
        private const long MaximumExpandedBytes = 512L * 1024 * 1024;
        private static readonly SemaphoreSlim ParseGate = new SemaphoreSlim(1, 1);

        public static MapTraversalQuery Load(byte[] archive, string sceneId, string mapId, CancellationToken cancellation)
        {
            // Only one map builds its spatial indexes at a time, including canceled loads.
            ParseGate.Wait(cancellation);
            try
            {
                cancellation.ThrowIfCancellationRequested();
                using var stream = new MemoryStream(archive, false);
                using var zip = new ZipArchive(stream, ZipArchiveMode.Read, false);
                var descriptorEntry = zip.GetEntry("package.json");
                if (descriptorEntry == null || descriptorEntry.Length > 65536)
                    throw new InvalidDataException("Map package descriptor is missing or invalid.");
                MapTraversalPackageDescriptor descriptor;
                using (var reader = new StreamReader(descriptorEntry.Open(), Encoding.UTF8))
                    descriptor = JsonConvert.DeserializeObject<MapTraversalPackageDescriptor>(reader.ReadToEnd());
                if (descriptor == null || descriptor.packageVersion != 1 || descriptor.sceneId != sceneId
                    || descriptor.mapId != mapId || descriptor.querySourceFingerprint != MapTraversalBuildInfo.QuerySourceFingerprint
                    || descriptor.files == null || descriptor.files.Length == 0 || descriptor.files.Length > 16)
                    throw new InvalidDataException("Map package identity, format or query version does not match.");

                var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
                long total = 0;
                foreach (MapTraversalPackageFile file in descriptor.files)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if (file == null || string.IsNullOrWhiteSpace(file.name) || file.name.IndexOf('/') >= 0
                        || file.name.IndexOf('\\') >= 0 || file.name == "package.json" || files.ContainsKey(file.name)
                        || file.bytes < 0 || file.bytes > MaximumExpandedBytes || file.sha256?.Length != 64)
                        throw new InvalidDataException("Map package file table is invalid.");
                    total = checked(total + file.bytes);
                    if (total > MaximumExpandedBytes) throw new InvalidDataException("Map package exceeds the memory limit.");
                    ZipArchiveEntry entry = zip.GetEntry(file.name);
                    if (entry == null || entry.Length != file.bytes)
                        throw new InvalidDataException("Map package file is missing or has an unexpected length: " + file.name);
                    byte[] bytes = new byte[checked((int)file.bytes)];
                    using (Stream input = entry.Open())
                    {
                        int offset = 0;
                        while (offset < bytes.Length)
                        {
                            cancellation.ThrowIfCancellationRequested();
                            int read = input.Read(bytes, offset, Math.Min(65536, bytes.Length - offset));
                            if (read <= 0) throw new InvalidDataException("Truncated map package file: " + file.name);
                            offset += read;
                        }
                    }
                    if (Hash(bytes) != file.sha256)
                        throw new InvalidDataException("Map package checksum mismatch: " + file.name);
                    files.Add(file.name, bytes);
                }
                if (zip.Entries.Count != files.Count + 1)
                    throw new InvalidDataException("Unexpected or duplicate entries in map package.");
                string fingerprint = Hash(Encoding.UTF8.GetBytes(string.Join("\n", descriptor.files
                    .OrderBy(file => file.name, StringComparer.Ordinal).Select(file => file.name + ":" + file.sha256))));
                if (fingerprint != descriptor.packageFingerprint)
                    throw new InvalidDataException("Map package fingerprint mismatch.");
                cancellation.ThrowIfCancellationRequested();
                ServerMapData data = ServerMapData.Load(files);
                cancellation.ThrowIfCancellationRequested();
                if (data.Manifest.SceneId != sceneId || data.Manifest.MapId != mapId)
                    throw new InvalidDataException("Map manifest does not match the requested map.");
                return new MapTraversalQuery(data, fingerprint);
            }
            finally { ParseGate.Release(); }
        }

        private static string Hash(byte[] bytes)
        {
            using var algorithm = SHA256.Create();
            return BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
    }
}
