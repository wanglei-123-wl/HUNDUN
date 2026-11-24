$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$coordinator = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/MapTransferCoordinator.cs') -Raw
$catalog = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/WorldMapSceneCatalog.cs') -Raw
$catalog = [regex]::Replace($catalog, '(?m)^using [^;]+;\r?\n', '')
$messages = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/NetWork/NetMessages.cs') -Raw
$transferDtos = [regex]::Match($messages,
    '(?s)\[Serializable\]\s*public sealed class MapTransferOptionsRequest.*?(?=\[Serializable\]\s*public class ServerErrorMessage)')
if (-not $transferDtos.Success) { throw 'Map transfer DTOs were not found.' }
$coordinator = [regex]::Replace($coordinator, '(?m)^using [^;]+;\r?\n', '')
$preflight = (Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/MapTransferResourcePreflight.cs') -Raw) `
    -replace '(?m)^using [^;]+;\r?\n', ''

$stubs = @'
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.Json;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.AddressableAssets.ResourceLocators;
using Game.UI.Notice;

public class NetMessageBase { public string msgId; public string type; }
public class PlayerNetworkState { public string characterId; }
public class MovementReferencedRequest : NetMessageBase { public string sceneSessionId; }
public class MovementEventControl {
    public long epoch;
    public string sceneSessionId;
    public bool IsValidFor(string scene) => epoch > 0 && sceneSessionId == scene;
}
public class ServerErrorMessage { public string requestMsgId; public string errorCode; }
public sealed class GameSession {
    public bool IsWorldReady = true;
    public string SceneSessionId = "source-session";
    public string SceneId = "world_002";
    public string MapId = "map_001";
    public bool IsCurrentMap(string scene, string map) =>
        IsWorldReady && SceneId == scene && MapId == map;
    public int PauseCount;
    public long ResumedAt;
    public string RecoveryReason;
    public bool TryPauseForMapTransfer() {
        if (!NetworkManager.Instance.IsConnected) return false;
        NetworkManager.Instance.SceneOperationsPaused = true;
        PauseCount++;
        return true;
    }
    public void ResumeAfterMapTransferFailure(MovementEventControl movement) {
        ResumedAt = movement?.epoch ?? 0;
        NetworkManager.Instance.SceneOperationsPaused = false;
        PauseCount--;
    }
    public void RecoverWorldSession(string reason) {
        RecoveryReason = reason;
        IsWorldReady = false;
    }
}
public sealed class NetworkManager {
    public static NetworkManager Instance;
    public bool IsConnected = true;
    public long ConnectionEpoch = 1;
    public bool SceneOperationsPaused;
    public event Action<string> OnDisconnected;
    public readonly List<object> Sent = new List<object>();
    public bool Send<T>(T message) { Sent.Add(message); return IsConnected; }
    public void Disconnect() {
        IsConnected = false;
        SceneOperationsPaused = false;
        OnDisconnected?.Invoke("closed");
    }
}
public sealed class NetMessageDispatcher {
    public static NetMessageDispatcher Instance;
    private readonly Dictionary<string, Action<string>> handlers = new Dictionary<string, Action<string>>();
    public void Register(string id, Action<string> handler) {
        if (!handlers.ContainsKey(id)) handlers[id] = null;
        handlers[id] += handler;
    }
    public void Unregister(string id, Action<string> handler) {
        if (handlers.ContainsKey(id)) handlers[id] -= handler;
    }
    public void Emit(string id, string json) {
        if (handlers.TryGetValue(id, out Action<string> handler)) handler?.Invoke(json);
    }
}
public sealed class VillageTeleportPanel {
    public Action<MapTransferRoute> Handler;
    public bool Hidden;
    public void SetTransferHandler(Action<MapTransferRoute> handler) { Handler = handler; }
    public void Hide() { Hidden = true; }
    public void Click(string route) { Click(route, "world_001", "map_demo_1"); }
    public void Click(string route, string scene, string map) {
        Handler?.Invoke(new MapTransferRoute { routeId = route, sceneId = scene, mapId = map });
    }
}
public static class TestWorld {
    public static VillageTeleportPanel Panel;
    public static bool HoldCoroutines;
    public static readonly List<Coroutine> Pending = new List<Coroutine>();
    public static void ResumeAll() {
        foreach (var c in Pending.ToArray()) {
            if (c.Active) while (c.Routine.MoveNext()) {}
            c.Active = false;
        }
        Pending.Clear();
    }
}
public sealed class MapTransferStatusView {
    public static MapTransferStatusView Last;
    public string Message;
    public bool Visible;
    public bool IsVisible => Visible;
    public bool MinimumDisplayElapsed => UnityEngine.Time.realtimeSinceStartup - shownAt >= 3.5f;
    private float shownAt;
    public object gameObject;
    public static MapTransferStatusView Create(object owner) => Last = new MapTransferStatusView();
    public void BeginDestination(string scene, string map) { shownAt = UnityEngine.Time.realtimeSinceStartup; }
    public void Show(string message) { Message = message; Visible = true; }
    public void ShowFailure(string message, bool recovering) { Show(message); }
    public void Hide() { Visible = false; }
    public void Release() { Hide(); }
}
namespace Game.UI.Notice {
    public static class GameNoticeService {
        public static string Last;
        public static bool Show(string value) { Last = value; return true; }
    }
}
namespace UnityEngine {
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class DisallowMultipleComponent : Attribute {}
    public class TextAsset {}
    public class Coroutine { public IEnumerator Routine; public bool Active; }
    public class MonoBehaviour {
        public object transform;
        public static void Destroy(object value) {}
        public T FindObjectOfType<T>(bool includeInactive) where T : class =>
            TestWorld.Panel as T;
        public Coroutine StartCoroutine(IEnumerator routine) {
            var c = new Coroutine { Routine = routine, Active = true };
            if (TestWorld.HoldCoroutines) {
                if (routine.MoveNext()) TestWorld.Pending.Add(c);
                else c.Active = false;
            } else { while (routine.MoveNext()) {} c.Active = false; }
            return c;
        }
        public void StopCoroutine(Coroutine coroutine) {
            coroutine.Active = false;
            (coroutine.Routine as IDisposable)?.Dispose();
        }
    }
    public static class Time { public static float realtimeSinceStartup; }
    public static class Debug {
        public static void LogWarning(object message, object context) {}
        public static void LogError(object message, object context) {}
    }
    public static class JsonUtility {
        public static T FromJson<T>(string json) =>
            JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions { IncludeFields = true });
    }
}
namespace UnityEngine.ResourceManagement.AsyncOperations {
    public enum AsyncOperationStatus { None, Succeeded, Failed }
    public struct AsyncOperationHandle {
        public AsyncOperationStatus Status;
        public Exception OperationException;
        public bool IsValid() => Status != AsyncOperationStatus.None;
    }
    public struct AsyncOperationHandle<T> {
        public AsyncOperationStatus Status;
        public T Result;
        public Exception OperationException;
        public bool IsValid() => Status != AsyncOperationStatus.None;
    }
}
namespace UnityEngine.ResourceManagement.ResourceProviders { public struct SceneInstance {} }
namespace UnityEngine.ResourceManagement.ResourceLocations {
    public interface IResourceLocation {
        string InternalId { get; }
        string ProviderId { get; }
        Type ResourceType { get; }
        IList<IResourceLocation> Dependencies { get; }
    }
    public sealed class TestLocation : IResourceLocation {
        public string InternalId { get; set; }
        public string ProviderId { get; set; } = "test";
        public Type ResourceType { get; set; }
        public IList<IResourceLocation> Dependencies { get; set; }
    }
}
namespace UnityEngine.AddressableAssets.ResourceLocators {
    public interface IResourceLocator {
        bool Locate(object key, Type type, out IList<IResourceLocation> locations);
    }
    public sealed class TestLocator : IResourceLocator {
        public readonly Dictionary<string, IResourceLocation> Entries = new Dictionary<string, IResourceLocation>();
        public bool Locate(object key, Type type, out IList<IResourceLocation> locations) {
            locations = null;
            if (!Entries.TryGetValue((string)key, out var item)) return false;
            locations = new[] { item }; return true;
        }
    }
}
namespace UnityEngine.AddressableAssets {
    public static class Addressables {
        public static AsyncOperationStatus NextStatus = AsyncOperationStatus.Succeeded;
        public static int Downloads;
        public static int Releases;
        public static object LastAddress;
        public static IResourceLocation[] LastLocations;
        public static long MissingBytes;
        public static readonly List<IResourceLocator> ResourceLocators = new List<IResourceLocator>();
        public static AsyncOperationHandle<long> GetDownloadSizeAsync(IEnumerable locations) {
            var copy = new List<IResourceLocation>();
            foreach (IResourceLocation location in locations) copy.Add(location);
            LastLocations = copy.ToArray();
            LastAddress = LastLocations[0].InternalId;
            return new AsyncOperationHandle<long> { Status = NextStatus, Result = MissingBytes };
        }
        public static AsyncOperationHandle DownloadDependenciesAsync(object address) {
            Downloads++;
            LastAddress = address;
            return new AsyncOperationHandle { Status = NextStatus };
        }
        public static void Release(AsyncOperationHandle handle) { Releases++; }
        public static void Release<T>(AsyncOperationHandle<T> handle) { Releases++; }
    }
}
'@
$source = $stubs + "`n" + $transferDtos.Value + "`n" + $catalog + "`n" + $preflight + "`n" + $coordinator
Add-Type -TypeDefinition $source -IgnoreWarnings | Out-Null

function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
}
function Emit-Result($dispatcher, $request, [bool]$success, [string]$transferId,
    [long]$nextSequence, [string]$scene = 'world_001', [string]$map = 'map_demo_1',
    [string]$destinationSession = 'destination-session') {
    $payload = @{
        msgId = 'map_transfer_result'
        requestId = $request.requestId
        transferId = $transferId
        success = $success
        sceneSessionId = $(if ($success) { $destinationSession } else { $request.sceneSessionId })
        sceneId = $(if ($success) { $scene } else { '' })
        mapId = $(if ($success) { $map } else { '' })
        self = $(if ($success) { @{ characterId = 'player' } } else { $null })
        movementControl = @{ epoch = $nextSequence; sceneSessionId = $(if ($success) { $destinationSession } else { $request.sceneSessionId }) }
        snapshotRevision = $(if ($success) { 2 } else { 0 })
        errorCode = $(if ($success) { '' } else { 'too_far' })
    } | ConvertTo-Json -Compress -Depth 5
    $dispatcher.Emit('map_transfer_result', $payload)
}
function New-Harness {
    [TestWorld]::HoldCoroutines = $false
    [TestWorld]::Pending.Clear()
    [UnityEngine.AddressableAssets.Addressables]::Downloads = 0
    [UnityEngine.AddressableAssets.Addressables]::Releases = 0
    [UnityEngine.AddressableAssets.Addressables]::MissingBytes = 0
    [UnityEngine.AddressableAssets.Addressables]::ResourceLocators.Clear()
    $locator = [UnityEngine.AddressableAssets.ResourceLocators.TestLocator]::new()
    foreach ($address in [WorldMapSceneCatalog]::GetAllResourceAddresses()) {
        $type = if ($address.StartsWith('game/scene/')) {
            [UnityEngine.ResourceManagement.ResourceProviders.SceneInstance]
        } else { [UnityEngine.TextAsset] }
        $locator.Entries.Add($address, [UnityEngine.ResourceManagement.ResourceLocations.TestLocation]@{
            InternalId = $address; ResourceType = $type })
    }
    [UnityEngine.AddressableAssets.Addressables]::ResourceLocators.Add($locator)
    [NetworkManager]::Instance = [NetworkManager]::new()
    [NetMessageDispatcher]::Instance = [NetMessageDispatcher]::new()
    [TestWorld]::Panel = [VillageTeleportPanel]::new()
    [UnityEngine.Time]::realtimeSinceStartup = 0
    [UnityEngine.AddressableAssets.Addressables]::NextStatus =
        [UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus]::Succeeded
    $owner = [GameSession]::new()
    $service = [MapTransferCoordinator]::new()
    $service.Initialize($owner)
    return @{ Owner = $owner; Service = $service; Network = [NetworkManager]::Instance;
        Dispatcher = [NetMessageDispatcher]::Instance; Panel = [TestWorld]::Panel }
}
function Prepare($harness, $request, [string]$transferId,
    [string]$scene = 'world_001', [string]$map = 'map_demo_1') {
    $payload = @{
        msgId = 'map_transfer_prepare'
        requestId = $request.requestId
        transferId = $transferId
        sceneId = $scene
        mapId = $map
        expiresAt = 1792921103171
    } | ConvertTo-Json -Compress
    $harness.Dispatcher.Emit('map_transfer_prepare', $payload)
}
function Tick($harness, [float]$time) {
    [UnityEngine.Time]::realtimeSinceStartup = $time
    $harness.Service.GetType().GetMethod('Update',
        [System.Reflection.BindingFlags]'Instance,NonPublic').
        Invoke($harness.Service, @()) | Out-Null
}

$h = New-Harness
Assert-True ($null -eq $h.Panel.Handler) `
    'The menu must stay disabled until destination commit handling exists.'
$script:commits = 0
$h.Service.SetCommitHandler([Action[MapTransferResult]]{
    param($result) $script:commits++
})
$h.Panel.Click('gm-route')
$request = [MapTransferRequest]$h.Network.Sent[0]
Assert-True ($h.Owner.PauseCount -eq 1 -and $h.Panel.Hidden -and
    $h.Network.SceneOperationsPaused -and $null -eq $h.Panel.Handler -and
    $request.routeId -eq 'gm-route' -and $request.sceneSessionId -eq 'source-session' -and
    [Guid]::TryParseExact($request.requestId, 'D', [ref]([Guid]::Empty))) `
    'Selecting a route must pause input and send a source-bound UUID request.'
Tick $h 6
Assert-True ($h.Network.Sent.Count -eq 2 -and
    [object]::ReferenceEquals($h.Network.Sent[0], $h.Network.Sent[1])) `
    'A timed-out request must retry the exact same identity.'
$id = [Guid]::NewGuid().ToString('D')
Prepare $h $request $id
$ready = [MapTransferReadyRequest]$h.Network.Sent[2]
Assert-True ($ready.loaded -and $ready.transferId -eq $id -and
    $ready.sceneSessionId -eq 'source-session') `
    'Prepared resources must report ready using the source session.'
Assert-True ([UnityEngine.AddressableAssets.Addressables]::Releases -eq 2 -and
    [UnityEngine.AddressableAssets.Addressables]::Downloads -eq 0 -and
    [UnityEngine.AddressableAssets.Addressables]::LastLocations[1].InternalId -eq 'game/map-traversal/map_demo_1') `
    'Both cache checks must release their handles and cover traversal without downloading.'
Prepare $h $request $id
Assert-True ($h.Network.Sent.Count -eq 3) 'Repeated prepare must not download or send twice.'
Emit-Result $h.Dispatcher $request $true $id 19
Emit-Result $h.Dispatcher $request $true $id 19
Assert-True ($script:commits -eq 1 -and $h.Owner.PauseCount -eq 1) `
    'Success must be delivered once to the commit handler without resuming source input.'
Assert-True ([MapTransferStatusView]::Last.Visible -and [MapTransferStatusView]::Last.Message.Contains('进入地图')) `
    'The status must remain visible until destination loading and snapshot application finish.'
$h.Network.SceneOperationsPaused = $false
$h.Service.CompleteCommittedTransfer()
Assert-True ($null -ne $h.Panel.Handler -and $h.Owner.PauseCount -eq 1) `
    'Only the completed destination barrier may unlock the next transfer.'
Assert-True (-not [MapTransferStatusView]::Last.Visible) 'Completed destination loading must hide its own status.'
$h.Service.Shutdown()

$h = New-Harness
$h.Service.SetCommitHandler([Action[MapTransferResult]]{ param($result) })
$h.Network.SceneOperationsPaused = $true
Tick $h 1
Assert-True ($null -eq $h.Panel.Handler) `
    'A paused world must disable an already bound destination button.'
$h.Network.SceneOperationsPaused = $false
Tick $h 2
Assert-True ($null -ne $h.Panel.Handler) `
    'An idle, resumed world must restore the transfer handler.'
$h.Owner.IsWorldReady = $false
Tick $h 3
Assert-True ($null -eq $h.Panel.Handler) `
    'A world session that becomes unready must disable its destination.'
$h.Service.Shutdown()
Assert-True ($h.Panel.Hidden) 'Shutting down a world session must hide its menu.'

$h = New-Harness
$h.Service.SetCommitHandler([Action[MapTransferResult]]{ param($result) })
$h.Panel.Click('gm-route')
$request = [MapTransferRequest]$h.Network.Sent[0]
Emit-Result $h.Dispatcher $request $false '' 23
Assert-True ($h.Owner.PauseCount -eq 0 -and $h.Owner.ResumedAt -eq 23 -and
    -not $h.Network.SceneOperationsPaused -and $null -eq $h.Panel.Handler -and
    [MapTransferStatusView]::Last.Visible) `
    'A rejected request must resume with the server-provided next input sequence.'
$h.Service.Shutdown()

$h = New-Harness
$h.Service.SetCommitHandler([Action[MapTransferResult]]{ param($result) })
$h.Panel.Click('gm-route')
$request = [MapTransferRequest]$h.Network.Sent[0]
[UnityEngine.AddressableAssets.Addressables]::NextStatus =
    [UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus]::Failed
$id = [Guid]::NewGuid().ToString('D')
Prepare $h $request $id
$ready = [MapTransferReadyRequest]$h.Network.Sent[1]
Assert-True (-not $ready.loaded -and $h.Owner.PauseCount -eq 1) `
    'Resource failure must send loaded=false and wait for the final Gate result.'
Emit-Result $h.Dispatcher $request $false $id 0
Assert-True ($h.Owner.PauseCount -eq 0) `
    'A final failure after resource load failure must restore source input.'
$h.Service.Shutdown()

$h = New-Harness
$h.Service.SetCommitHandler([Action[MapTransferResult]]{ param($result) })
$h.Panel.Click('gm-route')
$request = [MapTransferRequest]$h.Network.Sent[0]
Prepare $h $request ([Guid]::NewGuid().ToString('D'))
$ready = $h.Network.Sent[1]
Tick $h 11
Assert-True ($h.Network.Sent.Count -eq 3 -and
    [object]::ReferenceEquals($ready, $h.Network.Sent[2])) `
    'An unanswered ready must retry with the same identity and load result.'
Tick $h 22
Assert-True ($h.Owner.RecoveryReason -eq 'map_transfer_uncertain' -and
    $null -eq $h.Panel.Handler -and $h.Owner.PauseCount -eq 1) `
    'Uncertain timeout must recover the world without resuming stale source input.'
$h.Service.Shutdown()

$h = New-Harness
$h.Service.SetCommitHandler([Action[MapTransferResult]]{ param($result) })
$h.Network.Disconnect()
Assert-True ($null -eq $h.Panel.Handler -and $h.Panel.Hidden -and
    -not $h.Network.SceneOperationsPaused) `
    'A disconnected Gate must hide the menu and disable its old action.'
$h.Owner.IsWorldReady = $false
$h.Network.IsConnected = $true
$h.Network.ConnectionEpoch = 2
Tick $h 1
Assert-True ($null -eq $h.Panel.Handler) `
    'A new TCP connection alone must not enable the old world menu.'
$h.Owner.IsWorldReady = $true
Tick $h 2
Assert-True ($null -ne $h.Panel.Handler) `
    'After the world is confirmed again, the transfer handler should rebind.'
$h.Service.Shutdown()

$h = New-Harness
$h.Owner.SceneId = 'world_001'
$h.Owner.MapId = 'map_demo_1'
$script:destinations = [Collections.Generic.List[string]]::new()
$h.Service.SetCommitHandler([Action[MapTransferResult]]{
    param($result) $script:destinations.Add($result.sceneId)
})
$legs = @(
    @{ Route='luxian002'; Scene='world_002'; Map='map_001'; Address='game/scene/map001' },
    @{ Route='luxian001'; Scene='world_001'; Map='map_demo_1'; Address='game/scene/demo1-map' },
    @{ Route='luxian002'; Scene='world_002'; Map='map_001'; Address='game/scene/map001' },
    @{ Route='luxian003'; Scene='world005'; Map='map_005'; Address='game/scene/world005' },
    @{ Route='luxian004'; Scene='world_002'; Map='map_001'; Address='game/scene/map001' },
    @{ Route='luxian006'; Scene='world008'; Map='map_008'; Address='game/scene/world008' },
    @{ Route='luxian009'; Scene='world_002'; Map='map_001'; Address='game/scene/map001' },
    @{ Route='luxian007'; Scene='world009'; Map='map_009'; Address='game/scene/world009' },
    @{ Route='luxian008'; Scene='world_002'; Map='map_001'; Address='game/scene/map001' },
    @{ Route='luxian005'; Scene='world007'; Map='map_007'; Address='game/scene/world007' },
    @{ Route='luxian011'; Scene='world006'; Map='map_006'; Address='game/scene/world006' }
)
$previousRequest = $null
foreach ($leg in $legs) {
    $before = $h.Network.Sent.Count
    $h.Panel.Click($leg.Route, $leg.Scene, $leg.Map)
    $request = [MapTransferRequest]$h.Network.Sent[$before]
    Assert-True ($request.routeId -eq $leg.Route -and
        $request.sceneSessionId -eq $h.Owner.SceneSessionId -and
        $request.requestId -ne $previousRequest) 'Each leg must use its own route, UUID and current source session.'
    $previousRequest = $request.requestId
    $id = [Guid]::NewGuid().ToString('D')
    Prepare $h $request $id $leg.Scene $leg.Map
    Assert-True ($h.Network.Sent[$before + 1].loaded -and
        [UnityEngine.AddressableAssets.Addressables]::LastAddress -eq $leg.Address) `
        'Each destination must prepare its actual mapped resource.'
    $destinationSession = [Guid]::NewGuid().ToString('D')
    Emit-Result $h.Dispatcher $request $true $id 19 $leg.Scene $leg.Map $destinationSession
    $h.Owner.SceneId = $leg.Scene
    $h.Owner.MapId = $leg.Map
    $h.Owner.SceneSessionId = $destinationSession
    $h.Network.SceneOperationsPaused = $false
    [UnityEngine.Time]::realtimeSinceStartup += 3.5
    $h.Service.CompleteCommittedTransfer()
}
Assert-True (($script:destinations -join ',') -eq ($legs.Scene -join ',') -and
    $null -eq $h.Owner.RecoveryReason) 'Repeated round trips must commit the selected destinations without stale state.'
$h.Service.Shutdown()

foreach ($stage in @('prepare', 'result', 'duplicate_prepare')) {
    $h = New-Harness
    $h.Owner.SceneId = 'world_001'
    $h.Owner.MapId = 'map_demo_1'
    $script:wrongCommits = 0
    $h.Service.SetCommitHandler([Action[MapTransferResult]]{ param($result) $script:wrongCommits++ })
    $h.Panel.Click('luxian002', 'world_002', 'map_001')
    $request = [MapTransferRequest]$h.Network.Sent[0]
    $id = [Guid]::NewGuid().ToString('D')
    if ($stage -ne 'prepare') { Prepare $h $request $id 'world_002' 'map_001' }
    if ($stage -eq 'result') { Emit-Result $h.Dispatcher $request $true $id 19 }
    else { Prepare $h $request $id }
    Assert-True ($h.Owner.RecoveryReason -eq 'map_transfer_uncertain' -and
        $script:wrongCommits -eq 0 -and $h.Owner.PauseCount -eq 1) `
        "A mismatched target at $stage must never commit or resume stale input."
    $h.Service.Shutdown()
}

$h = New-Harness
$h.Service.SetCommitHandler([Action[MapTransferResult]]{ param($result) })
$h.Panel.Click('same-map', 'world_002', 'map_001')
$h.Panel.Click('unknown', 'world_999', 'unknown-map')
Assert-True ($h.Network.Sent.Count -eq 0 -and $h.Owner.PauseCount -eq 0) `
    'Same-map and unknown-map requests must not freeze the player or reach Gate.'
$h.Service.Shutdown()

$h = New-Harness
$h.Owner.SceneId = 'world009'
$h.Owner.MapId = 'map_009'
$script:fireCavernCommit = $null
$h.Service.SetCommitHandler([Action[MapTransferResult]]{ param($result) $script:fireCavernCommit = $result })
$h.Panel.Click('luxian012', 'world010', 'map_010')
$request = [MapTransferRequest]$h.Network.Sent[0]
$id = [Guid]::NewGuid().ToString('D')
Prepare $h $request $id 'world010' 'map_010'
Assert-True ($request.routeId -eq 'luxian012' -and $h.Network.Sent[1].loaded -and
    [UnityEngine.AddressableAssets.Addressables]::LastAddress -eq 'game/scene/world010') `
    'The Gate route to the fire cavern must load the world010 scene.'
Emit-Result $h.Dispatcher $request $true $id 19 'world010' 'map_010'
Assert-True ($script:fireCavernCommit.sceneId -eq 'world010') 'The fire cavern result must reach the commit handler.'
$h.Service.Shutdown()

# New preflight regression cases run the actual asynchronous coordinator.
foreach ($missing in @('game/scene/world005', 'game/map-traversal/map_005')) {
    $h = New-Harness
    $h.Service.SetCommitHandler([Action[MapTransferResult]]{ param($result) })
    [UnityEngine.AddressableAssets.Addressables]::ResourceLocators[0].Entries.Remove($missing) | Out-Null
    $h.Panel.Click('luxian003', 'world005', 'map_005')
    Assert-True ($h.Owner.PauseCount -eq 0 -and $h.Network.Sent.Count -eq 0 -and
        -not $h.Network.SceneOperationsPaused -and $null -eq $h.Panel.Handler -and
        [MapTransferStatusView]::Last.Visible -and
        [Game.UI.Notice.GameNoticeService]::Last.Contains('重启游戏')) `
        "Missing resource $missing must block before pausing input or sending a request."
    $h.Service.Shutdown()
}

foreach ($failure in @('bytes', 'operation')) {
    $h = New-Harness
    $h.Service.SetCommitHandler([Action[MapTransferResult]]{ param($result) })
    if ($failure -eq 'bytes') { [UnityEngine.AddressableAssets.Addressables]::MissingBytes = 8192 }
    else { [UnityEngine.AddressableAssets.Addressables]::NextStatus = [UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus]::Failed }
    $h.Panel.Click('luxian003', 'world005', 'map_005')
    Assert-True ($h.Owner.PauseCount -eq 0 -and $h.Network.Sent.Count -eq 0 -and
        [UnityEngine.AddressableAssets.Addressables]::Downloads -eq 0 -and
        [UnityEngine.AddressableAssets.Addressables]::Releases -eq 1 -and
        [MapTransferStatusView]::Last.Visible) `
        'Missing cache or failed size probe must preserve control and never start a download.'
    $h.Service.Shutdown()
}

foreach ($cancel in @('timeout', 'disconnect', 'epoch', 'session', 'handler', 'shutdown', 'duplicate')) {
    $h = New-Harness
    $h.Service.SetCommitHandler([Action[MapTransferResult]]{ param($result) })
    [TestWorld]::HoldCoroutines = $true
    $h.Panel.Click('luxian003', 'world005', 'map_005')
    Assert-True ($h.Owner.PauseCount -eq 0 -and $h.Network.Sent.Count -eq 0 -and
        -not $h.Network.SceneOperationsPaused -and $null -eq $h.Panel.Handler -and
        [MapTransferStatusView]::Last.Message.Contains('检查地图')) `
        'While checking, movement stays enabled and repeated menu actions are disabled.'
    # Unsolicited handshake messages must be ignored before a request is sent.
    $h.Dispatcher.Emit('map_transfer_prepare', '{}')
    $h.Dispatcher.Emit('map_transfer_result', '{}')
    switch ($cancel) {
        'timeout' { Tick $h 11 }
        'disconnect' { $h.Network.Disconnect() }
        'epoch' { $h.Network.ConnectionEpoch++; Tick $h 1 }
        'session' { $h.Owner.SceneSessionId = 'changed-session' }
        'handler' { $h.Service.SetCommitHandler($null) }
        'shutdown' { $h.Service.Shutdown() }
        'duplicate' { $h.Panel.Click('other-route', 'world007', 'map_007') }
    }
    [TestWorld]::ResumeAll()
    if ($cancel -eq 'duplicate') {
        Assert-True ($h.Network.Sent.Count -eq 1 -and $h.Network.Sent[0].routeId -eq 'luxian003' -and
            $h.Owner.PauseCount -eq 1) 'A second click cannot replace the in-flight destination.'
    } else {
        Assert-True ($h.Owner.PauseCount -eq 0 -and $h.Network.Sent.Count -eq 0 -and
            [UnityEngine.AddressableAssets.Addressables]::Releases -eq 1 -and
            $null -eq $h.Owner.RecoveryReason -and
            ([MapTransferStatusView]::Last.Visible -eq ($cancel -ne 'shutdown'))) `
            "Preflight cancellation at $cancel must release handles and never send late requests or reset the world."
    }
    $h.Service.Shutdown()
}

$h = New-Harness
$h.Service.SetCommitHandler([Action[MapTransferResult]]{ param($result) })
$h.Panel.Click('luxian003', 'world005', 'map_005')
$request = [MapTransferRequest]$h.Network.Sent[0]
[UnityEngine.AddressableAssets.Addressables]::MissingBytes = 1024
$id = [Guid]::NewGuid().ToString('D')
Prepare $h $request $id 'world005' 'map_005'
Assert-True (-not $h.Network.Sent[1].loaded -and
    [UnityEngine.AddressableAssets.Addressables]::Downloads -eq 0 -and
    [MapTransferStatusView]::Last.Message.Contains('取消')) `
    'Cache loss after the initial probe must cancel the reservation without downloading.'
$payload = @{ requestId=$request.requestId; transferId=$id; success=$false;
    sceneSessionId=$request.sceneSessionId; errorCode='client_load_failed' } | ConvertTo-Json -Compress
$h.Dispatcher.Emit('map_transfer_result', $payload)
Assert-True ($h.Owner.PauseCount -eq 0 -and [Game.UI.Notice.GameNoticeService]::Last.Contains('重启游戏') -and
    [MapTransferStatusView]::Last.Visible) 'A rejected cache-loss transfer must retain the actionable error in the destination label.'
$h.Service.Shutdown()

$h = New-Harness
$h.Service.SetCommitHandler([Action[MapTransferResult]]{ param($result) })
$h.Panel.Click('luxian003', 'world005', 'map_005')
$h.Service.NotifyWorldRecovery()
Assert-True ([MapTransferStatusView]::Last.Visible -and
    [Game.UI.Notice.GameNoticeService]::Last.Contains('重新连接')) `
    'Destination load or snapshot recovery must retain status and explain recovery.'
$h.Service.Shutdown()

$h = New-Harness
$h.Service.SetCommitHandler([Action[MapTransferResult]]{ param($result) })
$h.Panel.Click('luxian003', 'world005', 'map_005')
$request = [MapTransferRequest]$h.Network.Sent[0]
[TestWorld]::HoldCoroutines = $true
$id = [Guid]::NewGuid().ToString('D')
Prepare $h $request $id 'world005' 'map_005'
Tick $h 26
[TestWorld]::ResumeAll()
Assert-True ($h.Network.Sent.Count -eq 2 -and -not $h.Network.Sent[1].loaded -and
    [UnityEngine.AddressableAssets.Addressables]::Releases -eq 2 -and
    [UnityEngine.AddressableAssets.Addressables]::Downloads -eq 0) `
    'A stalled preparation recheck must release its operation and send loaded=false exactly once.'
Emit-Result $h.Dispatcher $request $false $id 27 'world005' 'map_005'
Assert-True ($h.Owner.PauseCount -eq 0 -and [MapTransferStatusView]::Last.Visible) `
    'A server rejection after preparation timeout must retain its error message.'
$h.Service.Shutdown()

$h = New-Harness
$entries = [UnityEngine.AddressableAssets.Addressables]::ResourceLocators[0].Entries
$scene = $entries['game/scene/world005']
$traversal = $entries['game/map-traversal/map_005']
$bundle = [UnityEngine.ResourceManagement.ResourceLocations.TestLocation]@{ InternalId='scene-bundle'; ResourceType=[object] }
$shared = [UnityEngine.ResourceManagement.ResourceLocations.TestLocation]@{ InternalId='shared-bundle'; ResourceType=[object] }
$scene.Dependencies = [UnityEngine.ResourceManagement.ResourceLocations.IResourceLocation[]]@($bundle)
$bundle.Dependencies = [UnityEngine.ResourceManagement.ResourceLocations.IResourceLocation[]]@($shared)
$shared.Dependencies = [UnityEngine.ResourceManagement.ResourceLocations.IResourceLocation[]]@($bundle)
$traversal.Dependencies = [UnityEngine.ResourceManagement.ResourceLocations.IResourceLocation[]]@($shared)
$locations = [MapTransferResourcePreflight]::Resolve('world005', 'map_005', [UnityEngine.AddressableAssets.Addressables]::ResourceLocators)
Assert-True ($locations.Count -eq 4 -and $locations[0] -eq $scene -and $locations[1] -eq $traversal) `
    'The cache size probe must include shared nested bundles exactly once even in a dependency cycle.'
$traversal.ResourceType = [object]
$failed = $false
try { [MapTransferResourcePreflight]::Resolve('world005', 'map_005', [UnityEngine.AddressableAssets.Addressables]::ResourceLocators) | Out-Null }
catch { $failed = $true }
Assert-True $failed 'An address with the wrong resource type must fail instead of treating it as ready.'
$h.Service.Shutdown()

Write-Output 'PASS: map and traversal cache preflight, non-blocking cancellation, status, Gate handshake and repeated transfers.'
