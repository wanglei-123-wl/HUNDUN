using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace QiDao.Bootstrap.CodeUpdate
{
    [Serializable]
    public sealed class CodeCacheState
    {
        public string activeRelease;
        public string previousRelease;
        public long highestSequence;
        public string readyRelease;
        public string previousReadyRelease;
        public long readySequence;
    }

    [Serializable]
    public sealed class CodeStartJournal
    {
        public string releaseId;
        public int processId;
        public long processStartTicks;
    }

    // Contains code artifacts and startup state only. Never reads or clears the
    // Addressables/Unity resource cache, and supports concurrent game clients.
    public sealed class CodeUpdateCache
    {
        private readonly string root;
        private readonly Func<string, CodeCacheState> parseState;
        private readonly Func<CodeCacheState, string> serializeState;
        private readonly string mutexName;
        public string Root => root;

        public CodeUpdateCache(string directory, Func<string, CodeCacheState> parse,
            Func<CodeCacheState, string> serialize)
        {
            root = Path.GetFullPath(directory);
            parseState = parse;
            serializeState = serialize;
            Directory.CreateDirectory(root);
            mutexName = "Local\\QiDao.CodeUpdate." + CodeUpdateProtocol.Hash(Encoding.UTF8.GetBytes(root));
        }

        public string ReleaseDirectory(string releaseId)
        {
            if (!CodeUpdateProtocol.IsHex(releaseId, 32)) throw new InvalidDataException("Invalid cache release ID.");
            return Path.Combine(root, "releases", releaseId);
        }

        public string CodePath(string releaseId) => Path.Combine(ReleaseDirectory(releaseId), "QiDao.GameLogic.dll.bytes");
        public string CodePath(CodeRelease release)
        {
            if (!CodeUpdateProtocol.IsHex(release.codeSha256, 64)) throw new InvalidDataException("Invalid code artifact hash.");
            return Path.Combine(root, "artifacts", release.codeSha256, "QiDao.GameLogic.dll.bytes");
        }
        public string EnvelopePath(string releaseId) => Path.Combine(ReleaseDirectory(releaseId), "manifest.json");
        public bool IsFailed(string releaseId) => File.Exists(Path.Combine(ReleaseDirectory(releaseId), "failed.txt"));

        public void MarkFailed(string releaseId, string reason)
        {
            AtomicWrite(Path.Combine(ReleaseDirectory(releaseId), "failed.txt"), Encoding.UTF8.GetBytes(reason ?? "Startup failed."));
        }

        public void Store(CodeRelease release, string envelopeJson, byte[] bytes)
        {
            CodeUpdateProtocol.VerifyCodeBytes(release, bytes);
            WithWriteLock(() =>
            {
                string path = CodePath(release);
                if (!File.Exists(path) || new FileInfo(path).Length != release.codeBytes
                    || CodeUpdateProtocol.Hash(File.ReadAllBytes(path)) != release.codeSha256)
                    AtomicWrite(path, bytes);
                // A concurrent client may have completed this same immutable DLL.
                StoreManifest(release, envelopeJson);
            });
        }

        public void StoreManifest(CodeRelease release, string envelopeJson)
        {
            string path = EnvelopePath(release.releaseId);
            if (!File.Exists(path) || File.ReadAllText(path) != envelopeJson)
                AtomicWrite(path, Encoding.UTF8.GetBytes(envelopeJson));
        }

        public CodeCacheState ReadState()
        {
            foreach (string path in new[] { Path.Combine(root, "state.json"), Path.Combine(root, "state.json.backup") })
            {
                if (!File.Exists(path)) continue;
                try
                {
                    var state = parseState(File.ReadAllText(path));
                    if (state != null && state.highestSequence >= 0
                        && (string.IsNullOrEmpty(state.activeRelease) || CodeUpdateProtocol.IsHex(state.activeRelease, 32))
                        && (string.IsNullOrEmpty(state.previousRelease) || CodeUpdateProtocol.IsHex(state.previousRelease, 32))
                        && (string.IsNullOrEmpty(state.readyRelease) || CodeUpdateProtocol.IsHex(state.readyRelease, 32))
                        && (string.IsNullOrEmpty(state.previousReadyRelease) || CodeUpdateProtocol.IsHex(state.previousReadyRelease, 32)))
                        return state;
                }
                catch { /* A valid backup may survive an interrupted disk write. */ }
            }
            return new CodeCacheState();
        }

        public void MarkResourcesReady(CodeRelease release)
        {
            WithWriteLock(() =>
            {
                var state = ReadState();
                if (release.sequence < state.readySequence) return;
                if (state.readyRelease != release.releaseId)
                {
                    state.previousReadyRelease = state.readyRelease;
                    state.readyRelease = release.releaseId;
                }
                state.readySequence = release.sequence;
                AtomicWrite(Path.Combine(root, "state.json"), Encoding.UTF8.GetBytes(serializeState(state)));
            });
        }

        public void MarkHealthy(CodeRelease release, bool allowRecovery = false)
        {
            WithWriteLock(() =>
            {
                CodeCacheState state = ReadState();
                if (release.sequence < state.highestSequence && !allowRecovery) return;
                if (state.activeRelease != release.releaseId)
                {
                    state.previousRelease = state.activeRelease;
                    state.activeRelease = release.releaseId;
                }
                state.highestSequence = Math.Max(state.highestSequence, release.sequence);
                AtomicWrite(Path.Combine(root, "state.json"), Encoding.UTF8.GetBytes(serializeState(state)));
            });
        }

        private void WithWriteLock(Action action)
        {
            using (var mutex = new Mutex(false, mutexName))
            {
                bool acquired;
                try { acquired = mutex.WaitOne(2000); }
                catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) throw new IOException("Another client is committing its code version; retry startup.");
                try
                {
                    action();
                }
                finally { mutex.ReleaseMutex(); }
            }
        }

        public string BeginStart(CodeRelease release, string journalJson)
        {
            string path = Path.Combine(root, "sessions", Process.GetCurrentProcess().Id + "-" + Guid.NewGuid().ToString("N") + ".json");
            AtomicWrite(path, Encoding.UTF8.GetBytes(journalJson));
            return path;
        }

        public void EndStart(string journalPath)
        {
            string directory = Path.Combine(root, "sessions") + Path.DirectorySeparatorChar;
            if (!string.IsNullOrEmpty(journalPath)
                && Path.GetFullPath(journalPath).StartsWith(directory, StringComparison.OrdinalIgnoreCase))
                File.Delete(journalPath);
        }

        public void RecoverAbandonedStarts(Func<string, CodeStartJournal> parse)
        {
            string directory = Path.Combine(root, "sessions");
            if (!Directory.Exists(directory)) return;
            foreach (string path in Directory.GetFiles(directory, "*.json"))
            {
                CodeStartJournal journal;
                try { journal = parse(File.ReadAllText(path)); }
                catch { continue; }
                if (journal == null || !CodeUpdateProtocol.IsHex(journal.releaseId, 32)) continue;
                bool alive = false;
                try
                {
                    using (var process = Process.GetProcessById(journal.processId))
                        alive = !process.HasExited && process.StartTime.ToUniversalTime().Ticks == journal.processStartTicks;
                }
                catch { /* The process no longer exists. */ }
                if (alive) continue;
                if (ReadState().activeRelease != journal.releaseId)
                    MarkFailed(journal.releaseId, "The previous process exited before code startup was confirmed healthy.");
                EndStart(path);
            }
        }

        public static void AtomicWrite(string path, byte[] bytes)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary = path + ".partial-" + Guid.NewGuid().ToString("N");
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (File.Exists(path)) File.Replace(temporary, path, path + ".backup");
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
