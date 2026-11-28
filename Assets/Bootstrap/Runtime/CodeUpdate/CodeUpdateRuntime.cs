using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using QiDao.Bootstrap.Contracts;
using UnityEngine;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

namespace QiDao.Bootstrap.CodeUpdate
{
    public sealed class CodeUpdateRuntime
    {
        private static Assembly loadedAssembly;
        private static CodeRelease loadedRelease;
        private readonly CodeUpdateConfiguration config;
        private readonly CodeUpdateCache cache;
        private string journal;
        private bool attemptedLoad;
        private CodeRelease pendingRelease;
        private byte[] pendingCode;
        private CodeExecutionHost executionHost;
        public bool Succeeded { get; private set; }
        public bool RequiresRestart { get; private set; }
        public bool UsedRecovery { get; private set; }
        public string Error { get; private set; }
        public CodeRelease SelectedRelease { get; private set; }
        public Assembly GameAssembly => loadedAssembly;
        public bool CodeStartupConfirmed => Succeeded && journal == null;
        public Uri PublicationUri => CodeUpdateProtocol.ManifestUri(config);
        public string CacheRoot => cache.Root;
        public bool ExecutionHandedOff => executionHost?.Child != null && executionHost.Error == null;

        public CodeUpdateRuntime(CodeUpdateConfiguration configuration, string cacheDirectory)
        {
            config = configuration ?? throw new ArgumentNullException(nameof(configuration));
            CodeUpdateProtocol.ManifestUri(config);
            if (config.unityVersion != Application.unityVersion)
                throw new InvalidDataException("Code bootstrap Unity version does not match this player.");
            cache = new CodeUpdateCache(cacheDirectory, JsonUtility.FromJson<CodeCacheState>, JsonUtility.ToJson);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            loadedAssembly = null;
            loadedRelease = null;
        }

        public static CodeUpdateRuntime CreateDefault()
        {
            TextAsset asset = Resources.Load<TextAsset>("Startup/HotUpdateBootstrap");
            if (asset == null) throw new InvalidDataException("Trusted code bootstrap configuration is missing.");
            var config = JsonUtility.FromJson<CodeUpdateConfiguration>(asset.text);
            config.manifestUrl = StartupResourceEndpoint.ResolveCodeManifestAddress(config.manifestUrl);
            return new CodeUpdateRuntime(config, Path.Combine(Application.persistentDataPath, "CodeUpdates", "windows-mono-v1"));
        }

        public IEnumerator Run(Action<string> status, Action<float> progress)
        {
            Succeeded = false;
            Error = null;
            RequiresRestart = false;
            UsedRecovery = false;
#if UNITY_EDITOR
            // Editor Play Mode deliberately uses freshly compiled scripts. Player
            // acceptance below exercises the download path without this branch.
            if (Application.isEditor)
            {
                try
                {
                    loadedAssembly = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == CodeUpdateProtocol.AssemblyName);
                    InvokeEntry(loadedAssembly);
                    Succeeded = true;
                }
                catch (Exception error) { Error = error.ToString(); }
                yield break;
            }
#endif
            status?.Invoke(StartupUiText.CheckingGameUpdate);
            progress?.Invoke(0f);
            try { cache.RecoverAbandonedStarts(JsonUtility.FromJson<CodeStartJournal>); }
            catch (Exception error) { Error = error.ToString(); }
            if (Error != null) yield break;
            if (CodeExecutionHost.IsExecution)
            {
                try
                {
                    string id = CodeExecutionHost.PinnedReleaseId;
                    var pinned = ReadRelease(File.ReadAllText(cache.EnvelopePath(id)));
                    if (pinned.releaseId != id || cache.IsFailed(id)) throw new InvalidDataException("The execution release is invalid or quarantined.");
                    UsedRecovery = CodeExecutionHost.IsRecovery;
                    if (!UsedRecovery && pinned.sequence < cache.ReadState().highestSequence)
                        throw new InvalidDataException("The execution release is older than the verified publication floor.");
                    Load(pinned, File.ReadAllBytes(cache.CodePath(pinned)));
                }
                catch (Exception error) { Error = error.ToString(); RequiresRestart = true; }
                yield break;
            }
            if (HasArgument("-hotupdate-recovery"))
            {
                TryRecovery();
                yield return CompletePendingExecution();
                if (Succeeded || RequiresRestart || ExecutionHandedOff) yield break;
                // A first-install failure has no healthy cache to restore. A
                // restarted process must still be able to fetch a fixed release.
                Error = null;
            }

            string envelopeJson = null;
            CodeRelease release = null;
            var manifestUri = new UriBuilder(CodeUpdateProtocol.ManifestUri(config));
            manifestUri.Query = (manifestUri.Query.Length > 1 ? manifestUri.Query.Substring(1) + "&" : "")
                + "code_check=" + Guid.NewGuid().ToString("N");
            using (var sink = new BoundedDownload(65536))
            using (var request = new UnityWebRequest(manifestUri.Uri, UnityWebRequest.kHttpVerbGET, sink, null))
            {
                request.timeout = config.requestTimeoutSeconds;
                request.SetRequestHeader("Cache-Control", "no-cache");
                yield return request.SendWebRequest();
                if (request.result != UnityWebRequest.Result.Success) Error = "Code manifest request failed: " + request.error;
                else
                {
                    try
                    {
                        envelopeJson = Encoding.UTF8.GetString(sink.Bytes);
                        release = ReadRelease(envelopeJson);
                        if (release.sequence < cache.ReadState().highestSequence)
                            throw new InvalidDataException("An older code manifest was replayed; use a signed newer publication to roll back.");
                        if (cache.IsFailed(release.releaseId))
                            throw new InvalidDataException("This code release failed a previous startup.");
                    }
                    catch (Exception error) { Error = error.ToString(); }
                }
            }
            if (Error != null)
            {
                TryRecovery();
                yield return CompletePendingExecution();
                yield break;
            }

            byte[] code = null;
            try
            {
                if (File.Exists(cache.CodePath(release)))
                {
                    byte[] cached = File.ReadAllBytes(cache.CodePath(release));
                    CodeUpdateProtocol.VerifyCodeBytes(release, cached);
                    code = cached;
                }
            }
            catch (Exception error) { Debug.LogWarning("[CodeUpdate] Cached code needs repair: " + error.Message); }
            if (code == null)
            {
                status?.Invoke(StartupUiText.DownloadingGameUpdate);
                for (int attempt = 0; attempt < 2 && code == null; attempt++)
                {
                    var codeUri = new Uri(CodeUpdateProtocol.ManifestUri(config), release.codePath);
                    using (var sink = new BoundedDownload(release.codeBytes))
                    using (var request = new UnityWebRequest(codeUri, UnityWebRequest.kHttpVerbGET, sink, null))
                    {
                        request.timeout = config.requestTimeoutSeconds;
                        var operation = request.SendWebRequest();
                        while (!operation.isDone)
                        {
                            progress?.Invoke(Mathf.Clamp01((float)((double)sink.Count / release.codeBytes)));
                            yield return null;
                        }
                        if (request.result != UnityWebRequest.Result.Success) Error = "Game code download failed: " + request.error;
                        else
                        {
                            try
                            {
                                byte[] bytes = sink.Bytes;
                                CodeUpdateProtocol.VerifyCodeBytes(release, bytes);
                                cache.Store(release, envelopeJson, bytes);
                                code = bytes;
                                Error = null;
                            }
                            catch (Exception error) { Error = error.ToString(); }
                        }
                    }
                }
            }
            if (code == null) TryRecovery();
            else
            {
                // A cached DLL is authenticated against the newly checked signed
                // manifest, not merely trusted because its file already exists.
                try { cache.StoreManifest(release, envelopeJson); }
                catch (Exception error) { Error = error.ToString(); }
                if (Error != null) TryRecovery();
                else Load(release, code);
            }
            yield return CompletePendingExecution();
            if (Succeeded) progress?.Invoke(1f);
        }

        private IEnumerator CompletePendingExecution()
        {
            if (pendingRelease == null) yield break;
            executionHost = new CodeExecutionHost();
            yield return executionHost.Launch(pendingRelease, pendingCode, cache, UsedRecovery);
            pendingRelease = null; pendingCode = null;
            if (executionHost.Error != null) Error = executionHost.Error;
            else Error = null;
        }

        // Acceptance parents wait for their owned child so the test observes the
        // final exit code. The production bootstrap exits after the handoff.
        public IEnumerator WaitForExecutionChild()
        {
            if (!ExecutionHandedOff) yield break;
            while (!executionHost.Child.HasExited) yield return null;
            int exit = executionHost.Child.ExitCode;
            executionHost.ReleaseUsageLease();
            executionHost.Child.Dispose();
            Application.Quit(exit);
        }

        private CodeRelease ReadRelease(string json)
        {
            var envelope = JsonUtility.FromJson<SignedCodeEnvelope>(json);
            byte[] payload = CodeUpdateProtocol.VerifyEnvelope(envelope, config.publicKeyXml);
            var release = JsonUtility.FromJson<CodeRelease>(Encoding.UTF8.GetString(payload));
            CodeUpdateProtocol.Validate(release, config);
#if !UNITY_EDITOR && !QIDAO_HOTUPDATE_PROBE
            CodeUpdateProtocol.ValidateResourceBinding(release);
#endif
            return release;
        }

        private void TryRecovery()
        {
            string failure = Error;
            var state = cache.ReadState();
            foreach (string id in new[] { state.readyRelease, state.activeRelease, state.previousReadyRelease, state.previousRelease }.Distinct())
            {
                if (string.IsNullOrEmpty(id) || cache.IsFailed(id)) continue;
                try
                {
                    CodeRelease release = ReadRelease(File.ReadAllText(cache.EnvelopePath(id)));
                    if (release.releaseId != id) throw new InvalidDataException("Cached manifest belongs to another release.");
                    byte[] code = File.ReadAllBytes(cache.CodePath(release));
                    CodeUpdateProtocol.VerifyCodeBytes(release, code);
                    if (attemptedLoad) { RequiresRestart = true; return; }
                    UsedRecovery = true;
                    Debug.LogWarning("[CodeUpdate] Recovering verified release=" + id + " reason=" + failure);
                    Load(release, code);
                    return;
                }
                catch (Exception error) { Debug.LogWarning("[CodeUpdate] Cached recovery is unavailable: " + error.Message); }
            }
            if (string.IsNullOrEmpty(Error)) Error = "No verified code release is available for recovery.";
        }

        private void Load(CodeRelease release, byte[] code)
        {
            SelectedRelease = release;
            try
            {
                if (loadedAssembly != null)
                {
                    if (loadedRelease?.codeSha256 != release.codeSha256)
                        throw new InvalidOperationException("Game code is already loaded; another release requires a new process.");
                    Succeeded = true;
                    return;
                }
                var preloaded = AppDomain.CurrentDomain.GetAssemblies().SingleOrDefault(a => a.GetName().Name == CodeUpdateProtocol.AssemblyName);
                if (!CodeExecutionHost.IsExecution)
                {
                    if (preloaded != null) throw new InvalidOperationException("The fixed bootstrap must not contain game code.");
                    CodeUpdateProtocol.VerifyCodeBytes(release, code);
                    pendingRelease = release; pendingCode = code;
                    return;
                }
                string expected = CodeExecutionHost.ManagedGamePath(Path.Combine(CodeExecutionHost.HostDirectory(cache.Root,
                    Application.buildGUID, release), Path.GetFileName(CodeExecutionHost.OriginalPlayer)));
                if (preloaded == null || !string.Equals(Path.GetFullPath(preloaded.Location), Path.GetFullPath(expected), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Game code was not preloaded from its verified execution directory.");
                CodeUpdateProtocol.VerifyCodeBytes(release, File.ReadAllBytes(expected));
                CodeExecutionHost.HoldCurrentExecutionLease(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(expected))));
                using (var process = Process.GetCurrentProcess())
                    journal = cache.BeginStart(release, JsonUtility.ToJson(new CodeStartJournal
                    {
                        releaseId = release.releaseId, processId = process.Id,
                        processStartTicks = process.StartTime.ToUniversalTime().Ticks
                    }));
                attemptedLoad = true;
                CodeUpdateProtocol.VerifyCodeBytes(release, code);
                loadedAssembly = preloaded;
                if (loadedAssembly.GetName().Name != CodeUpdateProtocol.AssemblyName)
                    throw new InvalidDataException("The signed DLL has the wrong assembly identity.");
                loadedRelease = release;
                InvokeEntry(loadedAssembly);
                using (GameStartupServices.Create()) { }
                Error = null;
                Succeeded = true;
                // A player closing during a long resource download is not a
                // crashed code initializer. Resource activation is confirmed
                // separately by the startup/resource integration layer.
                MarkHealthy();
                Debug.Log("[CodeUpdate] Verified preloaded game code release=" + release.releaseId + " sha256=" + release.codeSha256);
            }
            catch (Exception error)
            {
                Error = error.ToString();
                RequiresRestart = attemptedLoad || loadedAssembly != null;
                Succeeded = false;
                try
                {
                    cache.MarkFailed(release.releaseId, Error);
                    cache.EndStart(journal);
                }
                catch (Exception storageError)
                {
                    Error += "\nStartup failure could not be persisted: " + storageError.Message;
                }
                Debug.LogError("[CodeUpdate] " + Error);
            }
        }

        private static void InvokeEntry(Assembly assembly)
        {
            Type entry = assembly.GetType(CodeUpdateProtocol.EntryType, true);
            MethodInfo initialize = entry.GetMethod("Initialize", BindingFlags.Static | BindingFlags.Public);
            if (initialize == null || initialize.GetParameters().Length != 0)
                throw new MissingMethodException(CodeUpdateProtocol.EntryType, "Initialize");
            initialize.Invoke(null, null);
        }

        public void MarkHealthy()
        {
#if UNITY_EDITOR
            if (Application.isEditor) return;
#endif
            if (!Succeeded || SelectedRelease == null) throw new InvalidOperationException("No loaded code release to confirm.");
            cache.MarkHealthy(SelectedRelease, UsedRecovery);
            cache.EndStart(journal);
            journal = null;
        }

        public void MarkResourcesReady()
        {
#if UNITY_EDITOR
            if (Application.isEditor) return;
#endif
            if (!Succeeded || SelectedRelease == null) throw new InvalidOperationException("No code/resource release to activate.");
            cache.MarkResourcesReady(SelectedRelease);
        }

        public void RequireResourceRecovery(string error)
        {
            Error = error;
            RequiresRestart = true;
            cache.MarkFailed(SelectedRelease.releaseId, error);
        }

        public bool RestartForRecovery()
        {
            if (!RequiresRestart) return false;
            string executable = CodeExecutionHost.OriginalPlayer;
            Process.Start(new ProcessStartInfo(executable, "-hotupdate-recovery -hotupdate-wait-for-pid " + Process.GetCurrentProcess().Id)
            { UseShellExecute = false });
            Application.Quit();
            return true;
        }

        public static bool HasArgument(string name) => Array.IndexOf(Environment.GetCommandLineArgs(), name) >= 0;

        internal sealed class BoundedDownload : DownloadHandlerScript
        {
            private readonly MemoryStream bytes = new MemoryStream();
            private readonly long limit;
            public BoundedDownload(long maximum) : base(new byte[65536]) { limit = maximum; }
            public long Count => bytes.Length;
            public byte[] Bytes => bytes.ToArray();
            protected override bool ReceiveData(byte[] data, int length)
            {
                if (data == null || length < 0 || bytes.Length + length > limit) return false;
                bytes.Write(data, 0, length);
                return true;
            }
        }
    }
}
