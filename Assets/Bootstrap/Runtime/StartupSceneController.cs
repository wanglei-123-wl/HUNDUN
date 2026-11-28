using System.Collections;
using UnityEngine;
using System;
using System.Diagnostics;

public sealed class StartupSceneController : MonoBehaviour
{
    private const float MinimumStartupSeconds = 4.5f;

    private IEnumerator Start()
    {
        string[] args = Environment.GetCommandLineArgs();
        if (Array.IndexOf(args, "-resource-probe-full") >= 0)
        {
            int urlIndex=Array.IndexOf(args,"-resource-probe-url");
            int cacheIndex=Array.IndexOf(args,"-resource-probe-cache");
            bool localUrl=urlIndex>=0 && urlIndex+1<args.Length && Uri.TryCreate(args[urlIndex+1],UriKind.Absolute,out var uri)
                && uri.IsLoopback && uri.Scheme==Uri.UriSchemeHttp;
            bool isolatedCache=cacheIndex>=0 && cacheIndex+1<args.Length
                && System.IO.Path.GetFullPath(args[cacheIndex+1]).StartsWith(@"C:\QiDaoBuild\",StringComparison.OrdinalIgnoreCase);
            if (!Application.isBatchMode || !localUrl || !isolatedCache)
                throw new InvalidOperationException("Resource verification requires batch mode, loopback HTTP and an isolated build cache.");
            gameObject.AddComponent<QiDao.Bootstrap.CodeUpdate.ResourceBindingAcceptanceRunner>();
            yield break;
        }
        int waitIndex = Array.IndexOf(args, "-hotupdate-wait-for-pid");
        if (waitIndex >= 0 && waitIndex + 1 < args.Length && int.TryParse(args[waitIndex + 1], out int previousPid))
        {
            Process previous = null;
            try { previous = Process.GetProcessById(previousPid); } catch { }
            if (previous != null)
            {
                float deadline = Time.realtimeSinceStartup + 30f;
                while (!previous.HasExited && Time.realtimeSinceStartup < deadline) yield return null;
                bool exited = previous.HasExited;
                previous.Dispose();
                if (!exited) { UnityEngine.Debug.LogError("The old game process did not exit; restart was stopped."); yield break; }
            }
        }
        float startedAt = Time.realtimeSinceStartup;
        StartupContentUpdate contentUpdate = GetComponent<StartupContentUpdate>();
        if (contentUpdate == null)
        {
            UnityEngine.Debug.LogError("Startup content update component is missing.");
            yield break;
        }
        StartupResourceServerSettings resourceSettings =
            GetComponent<StartupResourceServerSettings>();
        if (resourceSettings == null)
        {
            UnityEngine.Debug.LogError("Startup resource server settings component is missing.");
            yield break;
        }
        if (!resourceSettings.TryApply(out string addressError))
        {
            UnityEngine.Debug.LogError("Startup resource server configuration failed: " + addressError);
            yield break;
        }
        yield return contentUpdate.Run();
        if (!contentUpdate.IsReady) yield break;

        float remaining = MinimumStartupSeconds - (Time.realtimeSinceStartup - startedAt);
        if (remaining > 0f)
            yield return new WaitForSecondsRealtime(remaining);

        yield return contentUpdate.OpenLoginScene();
    }
}
