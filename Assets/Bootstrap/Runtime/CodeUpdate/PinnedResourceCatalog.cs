using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.Networking;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;

namespace QiDao.Bootstrap.CodeUpdate
{
    [Serializable]
    public sealed class ResourceBundleRecord
    {
        public string relativePath, sha256, cacheHash, bundleName;
        public long bytes;
        public uint crc;
    }

    [Serializable]
    public sealed class PairedResourceManifest
    {
        public int schemaVersion;
        public string resourceReleaseId, target, unityVersion, gameAssemblyName, scriptTypeHash;
        public string catalogSha256, catalogHash, buildRoot;
        public long catalogBytes;
        public ResourceBundleRecord[] bundles;
    }

    // Owns a single immutable catalog snapshot. It never follows latest catalog
    // pointers or clears existing Unity bundle cache versions.
    public sealed class PinnedResourceCatalog
    {
        private readonly CodeRelease release;
        private readonly Uri publication;
        private readonly string directory;
        private readonly Dictionary<string, string> bundleUrls = new Dictionary<string, string>(StringComparer.Ordinal);
        private Func<IResourceLocation, string> previousTransform;
        private PairedResourceManifest manifest;
        public bool Succeeded { get; private set; }
        public bool IntegrityFailure { get; private set; }
        public string Error { get; private set; }
        public IResourceLocator Locator { get; private set; }
        public CodeRelease Release => release;

        public PinnedResourceCatalog(CodeRelease selected, Uri publicationUri, string cacheRoot)
        {
            CodeUpdateProtocol.ValidateResourceBinding(selected);
            release = selected;
            publication = publicationUri;
            directory = Path.Combine(cacheRoot, "resource-catalogs", release.resourceReleaseId);
        }

        public IEnumerator Run()
        {
            Succeeded = false;
            IntegrityFailure = false;
            Error = null;
            yield return CheckRemoteHash();
            if (Error != null) yield break;
            try { Directory.CreateDirectory(directory); }
            catch (Exception error) { Error = "Resource metadata cache is unavailable: " + error.Message; }
            if (Error != null) yield break;
            string manifestPath = Path.Combine(directory, "resource-manifest.json");
            yield return GetControlFile("resource-manifest.json", manifestPath, release.resourceManifestBytes, release.resourceManifestSha256);
            if (Error != null) yield break;
            string catalogPath = Path.Combine(directory, "catalog_0.1.json");
            yield return GetControlFile("catalog_0.1.json", catalogPath, release.resourceCatalogBytes, release.resourceCatalogSha256);
            if (Error != null) yield break;
            try
            {
                manifest = JsonUtility.FromJson<PairedResourceManifest>(File.ReadAllText(manifestPath));
                ValidateManifest();
                WriteControlFile(Path.Combine(directory, "catalog_0.1.hash"), Encoding.UTF8.GetBytes(release.resourceCatalogHash));
            }
            catch (Exception error) { Reject(error); }
            if (Error != null) yield break;
            if (Locator == null)
            {
                var operation = Addressables.LoadContentCatalogAsync(new Uri(catalogPath).AbsoluteUri, false,
                    "pinned_" + release.resourceReleaseId);
                try
                {
                    yield return operation;
                    if (operation.Status != AsyncOperationStatus.Succeeded || operation.Result == null)
                        Error = "Pinned resource catalog could not be loaded: " + operation.OperationException;
                    else Locator = operation.Result;
                }
                finally { if (operation.IsValid()) Addressables.Release(operation); }
                if (Error != null) yield break;
                try { ValidateLocatorAndInstall(); }
                catch (Exception error)
                {
                    Addressables.RemoveResourceLocator(Locator);
                    Locator = null;
                    Reject(error);
                }
            }
            Succeeded = Error == null;
        }

        public IEnumerator ConfirmBeforeLogin()
        {
            if (!Succeeded) yield break;
            Succeeded = false;
            Error = null;
            yield return CheckRemoteHash();
            Succeeded = Error == null;
        }

        private IEnumerator CheckRemoteHash()
        {
            string uri = CodeUpdateProtocol.ResourceUri(publication, release, "catalog_0.1.hash").AbsoluteUri;
            using (var request = StartupCatalogSynchronization.CreateHashRequest(uri))
            {
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success || request.responseCode != 200)
                    Error = "Pinned catalog hash could not be checked online: " + request.error;
                else if (!StartupCatalogSynchronization.TryNormalizeHash(request.downloadHandler.text, out string hash)
                    || hash != release.resourceCatalogHash)
                    Reject(new InvalidDataException("The resource catalog does not match the selected signed code release."));
            }
        }

        private IEnumerator GetControlFile(string name, string path, long size, string sha256)
        {
            bool cached = false;
            try
            {
                cached = File.Exists(path) && new FileInfo(path).Length == size
                    && CodeUpdateProtocol.Hash(File.ReadAllBytes(path)) == sha256;
            }
            catch { /* Repair only this control file; leave bundle caches alone. */ }
            if (cached) yield break;
            using (var sink = new CodeUpdateRuntime.BoundedDownload(size))
            using (var request = new UnityWebRequest(CodeUpdateProtocol.ResourceUri(publication, release, name),
                UnityWebRequest.kHttpVerbGET, sink, null))
            {
                request.timeout = 30;
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success) Error = "Resource metadata download failed: " + request.error;
                else
                {
                    try
                    {
                        byte[] bytes = sink.Bytes;
                        if (bytes.LongLength != size || CodeUpdateProtocol.Hash(bytes) != sha256)
                            throw new InvalidDataException("Resource metadata size/SHA256 differs from its signed code binding: " + name);
                        WriteControlFile(path, bytes);
                    }
                    catch (Exception error) { Reject(error); }
                }
            }
        }

        private static void WriteControlFile(string path, byte[] bytes)
        {
            try { CodeUpdateCache.AtomicWrite(path, bytes); }
            catch (IOException)
            {
                // Another client may have atomically committed the same signed
                // metadata after our existence check. Accept only identical bytes.
                if (!File.Exists(path) || new FileInfo(path).Length != bytes.LongLength
                    || CodeUpdateProtocol.Hash(File.ReadAllBytes(path)) != CodeUpdateProtocol.Hash(bytes)) throw;
            }
        }

        private void ValidateManifest()
        {
            if (manifest == null || manifest.schemaVersion != 1 || manifest.resourceReleaseId != release.resourceReleaseId
                || manifest.target != CodeUpdateProtocol.Target || manifest.unityVersion != Application.unityVersion
                || manifest.gameAssemblyName != CodeUpdateProtocol.AssemblyName || manifest.scriptTypeHash != release.scriptTypeHash
                || manifest.catalogSha256 != release.resourceCatalogSha256 || manifest.catalogHash != release.resourceCatalogHash
                || manifest.catalogBytes != release.resourceCatalogBytes || manifest.buildRoot != release.resourceBuildRoot
                || manifest.bundles == null || manifest.bundles.Length == 0)
                throw new InvalidDataException("Resource manifest is incompatible with the selected game code/schema.");
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var bundle in manifest.bundles)
                if (bundle == null || bundle.relativePath == null || !bundle.relativePath.StartsWith("bundles/", StringComparison.Ordinal)
                    || bundle.relativePath.Split('/').Any(part => part == ".." || part == "." || part.Contains("\\"))
                    || !names.Add(bundle.relativePath) || bundle.bytes <= 0 || bundle.crc == 0
                    || !CodeUpdateProtocol.IsHex(bundle.sha256, 64) || !CodeUpdateProtocol.IsHex(bundle.cacheHash, 32)
                    || string.IsNullOrWhiteSpace(bundle.bundleName))
                    throw new InvalidDataException("Resource bundle manifest contains invalid or conflicting metadata.");
        }

        private void ValidateLocatorAndInstall()
        {
            var expected = manifest.bundles.ToDictionary(b => b.relativePath, StringComparer.Ordinal);
            var discovered = new HashSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<IResourceLocation>();
            var pending = new Stack<IResourceLocation>();
            foreach (object key in Locator.Keys)
                if (Locator.Locate(key, null, out var locations)) foreach (var location in locations) pending.Push(location);
            string prefix = release.resourceBuildRoot + "/";
            string actualRoot = new Uri(CodeUpdateProtocol.ResourceUri(publication, release, "catalog_0.1.json"), ".").AbsoluteUri;
            while (pending.Count > 0)
            {
                IResourceLocation location = pending.Pop();
                if (!visited.Add(location)) continue;
                if (location.HasDependencies) foreach (var dependency in location.Dependencies) pending.Push(dependency);
                if (!(location.Data is AssetBundleRequestOptions options)) continue;
                if (!location.InternalId.StartsWith(prefix, StringComparison.Ordinal))
                    throw new InvalidDataException("A resource bundle escapes the immutable resource channel: " + location.InternalId);
                string relative = location.InternalId.Substring(prefix.Length);
                if (!expected.TryGetValue(relative, out var bundle) || bundle.bytes != options.BundleSize
                    || bundle.crc != options.Crc || bundle.cacheHash != options.Hash || bundle.bundleName != options.BundleName)
                    throw new InvalidDataException("Catalog bundle differs from the authenticated resource manifest: " + relative);
                discovered.Add(relative);
                bundleUrls[location.InternalId] = actualRoot + relative;
            }
            if (!discovered.SetEquals(expected.Keys)) throw new InvalidDataException("The pinned catalog omitted required bundle records.");
            previousTransform = Addressables.InternalIdTransformFunc;
            Addressables.InternalIdTransformFunc = Transform;
            foreach (var locator in Addressables.ResourceLocators.ToArray())
                if (!ReferenceEquals(locator, Locator)) Addressables.RemoveResourceLocator(locator);
        }

        private string Transform(IResourceLocation location)
        {
            return bundleUrls.TryGetValue(location.InternalId, out string mapped) ? mapped
                : previousTransform == null ? location.InternalId : previousTransform(location);
        }

        private void Reject(Exception error)
        {
            IntegrityFailure = true;
            Error = error.ToString();
        }
    }
}
