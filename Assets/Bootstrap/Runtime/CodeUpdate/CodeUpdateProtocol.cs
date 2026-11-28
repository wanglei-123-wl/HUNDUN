using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace QiDao.Bootstrap.CodeUpdate
{
    [Serializable]
    public sealed class CodeUpdateConfiguration
    {
        public string manifestUrl;
        public string publicKeyXml;
        public int bootstrapApiVersion;
        public string unityVersion;
        public int maxCodeBytes;
        public int requestTimeoutSeconds;
    }

    [Serializable]
    public sealed class SignedCodeEnvelope
    {
        public string payload;
        public string signature;
    }

    [Serializable]
    public sealed class CodeRelease
    {
        public int schemaVersion;
        public string releaseId;
        public long sequence;
        public string target;
        public string unityVersion;
        public int bootstrapApiVersion;
        public string assemblyName;
        public string entryType;
        public string codePath;
        public long codeBytes;
        public string codeSha256;
        // The resource integration stage will pin these to an immutable catalog.
        public string resourceReleaseId;
        public string resourceCatalogHash;
        public string resourceCatalogSha256;
        public long resourceCatalogBytes;
        public string resourceManifestSha256;
        public long resourceManifestBytes;
        public string resourceBuildRoot;
        public string scriptTypeHash;
    }

    public static class CodeUpdateProtocol
    {
        public const string AssemblyName = "QiDao.GameLogic";
        public const string EntryType = "QiDao.GameLogic.GameEntry";
        public const string Target = "windows-x64-mono";
        public const string ChannelPath = "/client-hotupdate/windows-mono-v1";

        public static byte[] VerifyEnvelope(SignedCodeEnvelope envelope, string publicKeyXml)
        {
            if (envelope == null || string.IsNullOrEmpty(envelope.payload) || envelope.payload.Length > 65536
                || string.IsNullOrEmpty(envelope.signature) || envelope.signature.Length > 2048)
                throw new InvalidDataException("Invalid signed code manifest envelope.");
            byte[] payload = Convert.FromBase64String(envelope.payload);
            byte[] signature = Convert.FromBase64String(envelope.signature);
            using (var rsa = RSA.Create())
            {
                rsa.FromXmlString(publicKeyXml);
                if (!rsa.VerifyData(payload, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                    throw new InvalidDataException("Code manifest signature does not match the trusted publisher.");
            }
            return payload;
        }

        public static void Validate(CodeRelease release, CodeUpdateConfiguration config)
        {
            if (config == null || config.bootstrapApiVersion <= 0 || config.maxCodeBytes <= 0
                || config.requestTimeoutSeconds <= 0 || string.IsNullOrEmpty(config.publicKeyXml))
                throw new InvalidDataException("Invalid trusted code bootstrap configuration.");
            if (release == null || release.schemaVersion != 1 || !IsHex(release.releaseId, 32)
                || release.sequence <= 0 || release.target != Target || release.unityVersion != config.unityVersion
                || release.bootstrapApiVersion != config.bootstrapApiVersion
                || release.assemblyName != AssemblyName || release.entryType != EntryType
                || release.codeBytes <= 0 || release.codeBytes > config.maxCodeBytes
                || !IsHex(release.codeSha256, 64)
                || release.codePath != "releases/" + release.releaseId + "/" + AssemblyName + ".dll.bytes")
                throw new InvalidDataException("Code release is invalid or incompatible with this base client.");
            if (HasResourceBinding(release)) ValidateResourceBinding(release);
        }

        public static bool HasResourceBinding(CodeRelease release) => !string.IsNullOrEmpty(release?.resourceReleaseId);

        public static void ValidateResourceBinding(CodeRelease release)
        {
            if (release == null || !IsHex(release.resourceReleaseId, 32) || !IsHex(release.resourceCatalogHash, 32)
                || !IsHex(release.resourceCatalogSha256, 64) || !IsHex(release.resourceManifestSha256, 64)
                || !IsHex(release.scriptTypeHash, 32) || release.resourceCatalogBytes <= 0
                || release.resourceCatalogBytes > 4194304 || release.resourceManifestBytes <= 0
                || release.resourceManifestBytes > 1048576
                || !Uri.TryCreate(release.resourceBuildRoot, UriKind.Absolute, out var root)
                || (root.Scheme != Uri.UriSchemeHttp && root.Scheme != Uri.UriSchemeHttps)
                || !string.IsNullOrEmpty(root.UserInfo) || !string.IsNullOrEmpty(root.Query) || !string.IsNullOrEmpty(root.Fragment)
                || root.AbsolutePath != ChannelPath + "/resources/" + release.resourceReleaseId)
                throw new InvalidDataException("Code release has no complete compatible resource binding.");
        }

        public static Uri ResourceUri(Uri publication, CodeRelease release, string file)
        {
            ValidateResourceBinding(release);
            if (file != "catalog_0.1.json" && file != "catalog_0.1.hash" && file != "resource-manifest.json")
                throw new InvalidDataException("Unexpected resource control file.");
            return new Uri(publication, "resources/" + release.resourceReleaseId + "/" + file);
        }

        public static bool IsHex(string value, int length)
        {
            if (value == null || value.Length != length) return false;
            foreach (char c in value)
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'))) return false;
            return true;
        }

        public static string Hash(byte[] bytes)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        public static void VerifyCodeBytes(CodeRelease release, byte[] bytes)
        {
            if (bytes == null || bytes.LongLength != release.codeBytes || Hash(bytes) != release.codeSha256)
                throw new InvalidDataException("Downloaded game code length or SHA256 does not match its signed manifest.");
        }

        public static Uri ManifestUri(CodeUpdateConfiguration config)
        {
            if (!Uri.TryCreate(config.manifestUrl, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
                throw new InvalidDataException("Code manifest must use a valid HTTP(S) endpoint.");
            return uri;
        }
    }
}
