using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Game.Client.MapTraversal
{
    public sealed class MapTraversalSession : IDisposable
    {
        private sealed class Preparation
        {
            public string SceneId, MapId;
            public readonly CancellationTokenSource Cancellation = new CancellationTokenSource();
            public AsyncOperationHandle<TextAsset> Handle;
            public bool AssetReleaseScheduled;
            public Task<MapTraversalQuery> Job;
            public MapTraversalQuery Query;
        }

        private Preparation pending;
        public MapTraversalQuery Current { get; private set; }
        public string LastError { get; private set; }
        public bool IsReady(string sceneId, string mapId) => Current != null
            && Current.SceneId == sceneId && Current.MapId == mapId;
        public bool IsPrepared(string sceneId, string mapId) => pending?.Query != null
            && pending.SceneId == sceneId && pending.MapId == mapId;

        public IEnumerator Prepare(string sceneId, string mapId, Func<bool> isValid, float timeoutSeconds)
        {
            CancelPending();
            LastError = null;
            var preparation = new Preparation { SceneId = sceneId, MapId = mapId };
            pending = preparation;
            if (!WorldMapSceneCatalog.TryGetTraversalAddress(sceneId, mapId, out string address))
            {
                Fail(preparation, "Unknown map traversal address.");
                yield break;
            }
            if (IsReady(sceneId, mapId))
            {
                if (isValid()) preparation.Query = Current;
                else Fail(preparation, "Map session changed during preparation.");
                yield break;
            }

            double deadline = Time.realtimeSinceStartupAsDouble + Math.Max(1f, timeoutSeconds);
            if (!Continue(preparation, isValid, deadline)) yield break;
            try { preparation.Handle = Addressables.LoadAssetAsync<TextAsset>(address); }
            catch (Exception error) { Fail(preparation, error.Message); yield break; }
            while (true)
            {
                if (!Continue(preparation, isValid, deadline)) yield break;
                if (preparation.Handle.IsDone) break;
                yield return null;
            }
            if (!Continue(preparation, isValid, deadline)) yield break;
            if (preparation.Handle.Status != AsyncOperationStatus.Succeeded || preparation.Handle.Result == null)
            {
                Fail(preparation, preparation.Handle.OperationException?.Message ?? "Map traversal resource is unavailable.");
                yield break;
            }

            try
            {
                byte[] archive = preparation.Handle.Result.bytes;
                ReleaseAsset(preparation);
                CancellationToken token = preparation.Cancellation.Token;
                preparation.Job = Task.Run(() => MapTraversalPackage.Load(archive, sceneId, mapId, token), token);
            }
            catch (Exception error) { Fail(preparation, error.Message); yield break; }
            // Canceled coroutines still observe failures from background jobs.
            preparation.Job.ContinueWith(job => { _ = job.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
            while (!preparation.Job.IsCompleted)
            {
                if (!Continue(preparation, isValid, deadline)) yield break;
                yield return null;
            }
            if (!Continue(preparation, isValid, deadline)) yield break;
            if (preparation.Job.IsFaulted || preparation.Job.IsCanceled)
            {
                Fail(preparation, preparation.Job.Exception?.GetBaseException().Message ?? "Map data loading was canceled.");
                yield break;
            }
            preparation.Query = preparation.Job.Result;
            preparation.Job = null;
        }

        public bool Commit(string sceneId, string mapId)
        {
            if (!IsPrepared(sceneId, mapId)) return false;
            Preparation prepared = pending;
            Current = prepared.Query;
            pending = null;
            prepared.Cancellation.Dispose();
            Debug.Log("[MapTraversal] ready sceneId=" + Current.SceneId + " mapId=" + Current.MapId
                + " format=" + Current.FormatVersion + " packageSha256=" + Current.PackageFingerprint
                + " querySourceSha256=" + Current.QuerySourceFingerprint + " runningServerHashVerified=False");
            return true;
        }

        private bool Continue(Preparation preparation, Func<bool> isValid, double deadline)
        {
            if (pending != preparation) return false;
            if (!isValid()) { Fail(preparation, "Map session changed during preparation."); return false; }
            if (Time.realtimeSinceStartupAsDouble > deadline)
            { Fail(preparation, "Map traversal loading timed out."); return false; }
            return true;
        }

        private void Fail(Preparation preparation, string error)
        {
            if (pending != preparation) return;
            LastError = error;
            Debug.LogError("[MapTraversal] load failed sceneId=" + preparation.SceneId + " mapId=" + preparation.MapId + " " + error);
            CancelPending();
        }

        public void CancelPending()
        {
            Preparation preparation = pending;
            pending = null;
            if (preparation == null) return;
            preparation.Cancellation.Cancel();
            ReleaseAsset(preparation);
            preparation.Query = null;
            preparation.Cancellation.Dispose();
        }

        private static void ReleaseAsset(Preparation preparation)
        {
            if (preparation.AssetReleaseScheduled || !preparation.Handle.IsValid()) return;
            preparation.AssetReleaseScheduled = true;
            if (preparation.Handle.IsDone) Addressables.Release(preparation.Handle);
            else preparation.Handle.Completed += handle => { if (handle.IsValid()) Addressables.Release(handle); };
        }

        public void Dispose()
        {
            CancelPending();
            Current = null;
        }
    }
}
