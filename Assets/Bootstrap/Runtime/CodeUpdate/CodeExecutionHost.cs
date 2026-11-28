using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace QiDao.Bootstrap.CodeUpdate
{
    // Unity must see the selected assembly during native engine initialization
    // to deserialize nested [Serializable] data. The installed bootstrap stays
    // immutable; each verified code hash gets its own writable execution copy.
    public sealed class CodeExecutionHost
    {
        public const string ReleaseArgument = "-hotupdate-execution-release";
        private const string OriginalArgument = "-hotupdate-execution-original";
        private const string RecoveryArgument = "-hotupdate-execution-recovery";
        private FileStream usageLease;
        private static FileStream executionLease;
        public static bool IsExecution => Argument(ReleaseArgument) != null;
        public static string PinnedReleaseId => Argument(ReleaseArgument);
        public static bool IsRecovery => CodeUpdateRuntime.HasArgument(RecoveryArgument);
        public Process Child { get; private set; }
        public string Error { get; private set; }

        public static string OriginalPlayer
        {
            get
            {
                string original = Argument(OriginalArgument);
                if (original != null) return Path.GetFullPath(original);
                using (var process = Process.GetCurrentProcess()) return process.MainModule.FileName;
            }
        }

        public static string HostDirectory(string cacheRoot, string buildGuid, CodeRelease release)
        {
            if (!CodeUpdateProtocol.IsHex(buildGuid, 32)) throw new InvalidDataException("Base player build identity is missing.");
            string identity = Path.GetFullPath(cacheRoot).ToUpperInvariant() + "|" + buildGuid + "|" + release.codeSha256;
            string key = CodeUpdateProtocol.Hash(Encoding.UTF8.GetBytes(identity)).Substring(0, 24);
            // Keep native Mono/Win32 DLL paths below MAX_PATH, including long
            // engine module names. The full code hash is still verified below.
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "QiDao", "CodeExecution", key);
        }

        public static string ManagedGamePath(string executable)
        {
            return Path.Combine(Path.GetDirectoryName(executable), Path.GetFileNameWithoutExtension(executable)
                + "_Data", "Managed", CodeUpdateProtocol.AssemblyName + ".dll");
        }

        public IEnumerator Launch(CodeRelease release, byte[] code, CodeUpdateCache cache, bool recovery)
        {
            string original = OriginalPlayer;
            string directory = HostDirectory(cache.Root, Application.buildGUID, release);
            string identity = Path.GetFullPath(cache.Root).ToUpperInvariant() + "|" + Application.buildGUID + "|" + release.codeSha256;
            var preparation = Task.Run(() =>
            {
                usageLease = AcquireUsageLease(directory);
                return Prepare(original, directory, identity, release, code);
            });
            while (!preparation.IsCompleted) yield return null;
            if (preparation.IsFaulted) { ReleaseUsageLease(); Error = preparation.Exception.ToString(); yield break; }
            try
            {
                string executable = preparation.Result;
                var start = new ProcessStartInfo(executable, ForwardArguments(release.releaseId, original, recovery))
                { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable) };
                if (CodeUpdateRuntime.HasArgument("-batchmode")) start.WindowStyle = ProcessWindowStyle.Hidden;
                Child = Process.Start(start);
                if (Child == null) throw new IOException("The verified game process could not be started.");
                cache.BeginStart(release, JsonUtility.ToJson(new CodeStartJournal
                {
                    releaseId = release.releaseId, processId = Child.Id,
                    processStartTicks = Child.StartTime.ToUniversalTime().Ticks
                }));
            }
            catch (Exception error) { ReleaseUsageLease(); Error = error.ToString(); }
            if (Error == null && !CodeUpdateRuntime.HasArgument("-batchmode"))
            {
                float deadline = Time.realtimeSinceStartup + 20f;
                while (!Child.HasExited && Time.realtimeSinceStartup < deadline)
                {
                    Child.Refresh();
                    if (Child.MainWindowHandle != IntPtr.Zero) break;
                    yield return null;
                }
                if (Child.HasExited) Error = "The verified game process exited before its startup window was ready.";
            }
        }

        private static FileStream AcquireUsageLease(string directory)
        {
            Exception failure = null;
            // A cleaner may be removing an inactive old copy. Retry opening its
            // shared read lease, then recreate/verify the copy as necessary.
            for (int attempt = 0; attempt < 50; attempt++)
            {
                try
                {
                    Directory.CreateDirectory(directory);
                    string path = Path.Combine(directory, ".usage-v1");
                    if (!File.Exists(path))
                    {
                        try { using (var created = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read)) created.Flush(true); }
                        catch (IOException) { if (!File.Exists(path)) throw; }
                    }
                    return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                }
                catch (IOException error) { failure = error; Thread.Sleep(100); }
            }
            throw new IOException("The execution copy is busy; retry startup.", failure);
        }

        public static void HoldCurrentExecutionLease(string directory)
        {
            if (executionLease != null) return;
            executionLease = AcquireUsageLease(directory);
            Application.quitting += ReleaseCurrentExecutionLease;
        }

        private static void ReleaseCurrentExecutionLease()
        {
            executionLease?.Dispose(); executionLease = null;
        }

        public void ReleaseUsageLease()
        {
            usageLease?.Dispose(); usageLease = null;
        }

        private static string Prepare(string original, string directory, string identity, CodeRelease release, byte[] code)
        {
            string source = Path.GetFullPath(Path.GetDirectoryName(original)).TrimEnd('\\', '/');
            string target = Path.GetFullPath(directory).TrimEnd('\\', '/');
            if (target.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || File.Exists(ManagedGamePath(original)))
                throw new InvalidDataException("The execution source must be the fixed bootstrap outside its execution cache.");
            CodeUpdateProtocol.VerifyCodeBytes(release, code);
            string name = "Local\\QiDao.ExecutionHost." + CodeUpdateProtocol.Hash(Encoding.UTF8.GetBytes(target.ToUpperInvariant()));
            using (var mutex = new Mutex(false, name))
            {
                bool acquired;
                try { acquired = mutex.WaitOne(120000); }
                catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) throw new IOException("Another client is preparing the execution directory; retry startup.");
                try
                {
                    Directory.CreateDirectory(target);
                    string marker = Path.Combine(target, ".host-identity");
                    if (File.Exists(marker) && File.ReadAllText(marker) != identity)
                        throw new InvalidDataException("The execution directory belongs to another complete build/code identity.");
                    foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
                    {
                        string relative = file.Substring(source.Length + 1);
                        string copy = Path.Combine(target, relative);
                        string expected = HashFile(file);
                        if (File.Exists(copy) && new FileInfo(copy).Length == new FileInfo(file).Length && HashFile(copy) == expected) continue;
                        Directory.CreateDirectory(Path.GetDirectoryName(copy));
                        bool valid = false;
                        for (int attempt = 0; attempt < 3 && !valid; attempt++)
                        {
                            string temporary = copy + ".p-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                            try
                            {
                                using (var input = File.OpenRead(file))
                                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                                { input.CopyTo(output); output.Flush(true); }
                                valid = HashFile(temporary) == expected && HashFile(file) == expected;
                                if (valid)
                                {
                                    if (File.Exists(copy)) File.Replace(temporary, copy, null);
                                    else File.Move(temporary, copy);
                                }
                            }
                            finally { if (File.Exists(temporary)) File.Delete(temporary); }
                        }
                        if (!valid) throw new IOException("Fixed player copy could not be verified: " + relative);
                    }
                    string executable = Path.Combine(target, Path.GetFileName(original));
                    string game = ManagedGamePath(executable);
                    if (!File.Exists(game) || CodeUpdateProtocol.Hash(File.ReadAllBytes(game)) != release.codeSha256)
                        CodeUpdateCache.AtomicWrite(game, code);
                    CodeUpdateProtocol.VerifyCodeBytes(release, File.ReadAllBytes(game));
                    CodeUpdateCache.AtomicWrite(marker, Encoding.UTF8.GetBytes(identity));
                    return executable;
                }
                finally { mutex.ReleaseMutex(); }
            }
        }

        private static string HashFile(string path)
        {
            using (var hash = System.Security.Cryptography.SHA256.Create())
            using (var stream = File.OpenRead(path))
                return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        private static string ForwardArguments(string release, string original, bool recovery)
        {
            string[] args = Environment.GetCommandLineArgs();
            var result = new System.Collections.Generic.List<string>();
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i] == ReleaseArgument || args[i] == OriginalArgument || args[i] == "-hotupdate-wait-for-pid") { i++; continue; }
                if (args[i] == RecoveryArgument || args[i] == "-hotupdate-recovery") continue;
                string value = args[i];
                result.Add(Quote(value));
                if (value == "-logFile" && i + 1 < args.Length) result.Add(Quote(args[++i] + ".execution.log"));
            }
            result.Add(Quote(ReleaseArgument)); result.Add(Quote(release));
            result.Add(Quote(OriginalArgument)); result.Add(Quote(original));
            if (recovery) result.Add(Quote(RecoveryArgument));
            return string.Join(" ", result);
        }

        internal static string Quote(string value)
        {
            var result = new StringBuilder("\""); int slashes = 0;
            foreach (char c in value)
            {
                if (c == '\\') { slashes++; continue; }
                result.Append('\\', c == '"' ? slashes * 2 + 1 : slashes); result.Append(c); slashes = 0;
            }
            return result.Append('\\', slashes * 2).Append('"').ToString();
        }

        private static string Argument(string name)
        {
            string[] args = Environment.GetCommandLineArgs(); int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
