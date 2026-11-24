$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$game = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/GameSession.cs') -Raw
$method = [regex]::Match($game,
    '(?s)    private IEnumerator CompleteMapTransferPresentation\(.*?(?=    private void HandleMapTransferCommitted)').Value
if (!$method) { throw 'Destination presentation coroutine was not found.' }

$stubs = @'
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
public sealed class MapTransferResult { public string sceneId="destination", mapId="map", sceneSessionId="new-session"; }
public struct BattleSyncReadyContext { public long ConnectionEpoch; }
public sealed class ThirdPersonCameraFollow {}
public sealed class InputController { public bool Enabled; public void SetInputEnabled(bool value) { Enabled=value; } }
public sealed class NetworkManager {
    public bool IsConnected=true, Paused=true;
    public long ConnectionEpoch=1;
    public string SceneSessionId="new-session";
    public bool SetSceneOperationsPaused(long epoch, bool value) { Paused=value; return true; }
}
public sealed class SyncState { public bool IsReady=true; }
public sealed class SyncRuntime { public SyncState SessionState = new SyncState(); }
public sealed class MapTransferCoordinator {
    public bool CanCompletePresentation => Time.realtimeSinceStartup >= 3.5f;
    public bool Completed;
    public string Message;
    public void ReportPreparationStatus(string text) { Message=text; }
    public void CompleteCommittedTransfer() { Completed=true; }
}
namespace Game.Minimap {
    public sealed class MinimapPresenter {
        public bool Ready=true;
        public bool IsPreparedForMap(string scene, string map) => Ready && scene=="destination" && map=="map";
    }
}
namespace UnityEngine {
    public static class Time { public static float realtimeSinceStartup; }
    public sealed class Coroutine {}
    public sealed class AsyncOperation {}
}
namespace UnityEngine.SceneManagement {
    public sealed class SceneData { public bool Loaded=true; }
    public struct Scene {
        public SceneData Data;
        public bool IsValid() => Data != null;
        public bool isLoaded => Data != null && Data.Loaded;
        public static bool operator ==(Scene a, Scene b) => object.ReferenceEquals(a.Data,b.Data);
        public static bool operator !=(Scene a, Scene b) => !(a==b);
        public override bool Equals(object o) => o is Scene && this==(Scene)o;
        public override int GetHashCode() => Data?.GetHashCode() ?? 0;
    }
    public static class SceneManager {
        public static bool FailUnload;
        public static Scene Unloading;
        public static Scene Active = new Scene { Data=new SceneData() };
        public static Scene GetActiveScene() => Active;
        public static AsyncOperation UnloadSceneAsync(Scene scene) {
            Unloading=scene;
            return FailUnload ? null : new AsyncOperation();
        }
    }
}
public sealed class GameSession {
    public MapTransferResult pendingMapTransfer;
    public bool sessionStarted=true, worldRecoveryPending, returningToLogin;
    public NetworkManager networkManager=new NetworkManager();
    public bool TraversalReady=true, PlayerReady=true;
    public SyncRuntime battleSyncRuntime=new SyncRuntime();
    public MapTransferCoordinator mapTransferCoordinator=new MapTransferCoordinator();
    public InputController localPlayerInputController=new InputController();
    public Game.Minimap.MinimapPresenter Minimap=new Game.Minimap.MinimapPresenter();
    public Coroutine mapTransferLoadCoroutine, mapTransferCompletionCoroutine;
    public object mapTraversalWaitingTransferReady;
    public float mapTransferSnapshotDeadline=20;
    public Scene mapTransferSourceScene;
    public bool enterWorldConfirmed, mapTransferPaused=true, localPlayerDead, inputWasEnabledForTransfer=true;
    public string Recovery;
    private T FindObjectOfType<T>(bool includeInactive=false) where T:class =>
        typeof(T)==typeof(Game.Minimap.MinimapPresenter) ? Minimap as T : new ThirdPersonCameraFollow() as T;
    private bool IsMapTraversalReady(string scene, string map) => TraversalReady;
    private bool HasPreparedLocalCharacter(ThirdPersonCameraFollow camera) => PlayerReady;
    private void RecoverWorldSession(string reason) { Recovery=reason; worldRecoveryPending=true; }
    public IEnumerator Begin() {
        Time.realtimeSinceStartup=0;
        SceneManager.FailUnload=false;
        pendingMapTransfer=new MapTransferResult();
        return CompleteMapTransferPresentation(new BattleSyncReadyContext { ConnectionEpoch=1 },pendingMapTransfer);
    }
'@
Add-Type -TypeDefinition ($stubs + $method + '}') -IgnoreWarnings | Out-Null
function Assert-True($condition, [string]$message) { if (!$condition) { throw $message } }

# Fast resources must still wait until the full visible-time minimum.
$session = [GameSession]::new()
$steps = $session.Begin()
Assert-True ($steps.MoveNext() -and $session.networkManager.Paused) 'Fast load must stay paused initially.'
[UnityEngine.Time]::realtimeSinceStartup = 3.49
Assert-True ($steps.MoveNext() -and !$session.mapTransferCoordinator.Completed) 'Cannot reveal the destination before 3.5 seconds.'
[UnityEngine.Time]::realtimeSinceStartup = 3.5
Assert-True (!$steps.MoveNext() -and $session.mapTransferCoordinator.Completed -and
    $session.localPlayerInputController.Enabled -and !$session.networkManager.Paused) 'Ready destination must finish at the minimum.'

# Each independent preparation condition must keep the overlay and input pause.
foreach ($missing in @('scene', 'traversal', 'snapshots', 'player', 'minimap')) {
    $session = [GameSession]::new()
    switch ($missing) {
        'scene' { $session.mapTransferLoadCoroutine = [UnityEngine.Coroutine]::new() }
        'traversal' { $session.TraversalReady = $false }
        'snapshots' { $session.battleSyncRuntime.SessionState.IsReady = $false }
        'player' { $session.PlayerReady = $false }
        'minimap' { $session.Minimap.Ready = $false }
    }
    $steps = $session.Begin()
    [UnityEngine.Time]::realtimeSinceStartup = 10
    Assert-True ($steps.MoveNext() -and !$session.mapTransferCoordinator.Completed -and
        $session.networkManager.Paused -and !$session.localPlayerInputController.Enabled) "$missing must block even after the time minimum."
    $session.mapTransferLoadCoroutine = $null
    $session.TraversalReady = $true
    $session.battleSyncRuntime.SessionState.IsReady = $true
    $session.PlayerReady = $true
    $session.Minimap.Ready = $true
    Assert-True (!$steps.MoveNext() -and $session.mapTransferCoordinator.Completed) "$missing readiness must release the completed destination."
}

# Source unloading is asynchronous; recheck readiness and identity afterwards.
$session = [GameSession]::new()
$oldMap = [UnityEngine.SceneManagement.SceneData]::new()
$session.mapTransferSourceScene = [UnityEngine.SceneManagement.Scene]@{ Data = $oldMap }
$steps = $session.Begin()
[UnityEngine.Time]::realtimeSinceStartup = 4
Assert-True ($steps.MoveNext() -and $steps.Current -is [UnityEngine.AsyncOperation] -and
    !$session.mapTransferCoordinator.Completed -and $session.networkManager.Paused) 'Source unload must finish before revealing the destination.'
$oldMap.Loaded = $false
$session.battleSyncRuntime.SessionState.IsReady = $false
Assert-True ($steps.MoveNext() -and !$session.mapTransferCoordinator.Completed) 'A repair during unload must keep the screen covered.'
$session.battleSyncRuntime.SessionState.IsReady = $true
Assert-True (!$steps.MoveNext() -and $session.mapTransferCoordinator.Completed) 'Unload plus restored snapshots can finish.'

foreach ($interrupt in @('disconnect', 'epoch', 'session', 'cancel')) {
    $session = [GameSession]::new()
    $steps = $session.Begin()
    $steps.MoveNext() | Out-Null
    switch ($interrupt) {
        'disconnect' { $session.networkManager.IsConnected = $false }
        'epoch' { $session.networkManager.ConnectionEpoch = 2 }
        'session' { $session.networkManager.SceneSessionId = 'stale-session' }
        'cancel' { $session.pendingMapTransfer = $null }
    }
    [UnityEngine.Time]::realtimeSinceStartup = 5
    Assert-True (!$steps.MoveNext() -and !$session.mapTransferCoordinator.Completed -and
        !$session.localPlayerInputController.Enabled) "$interrupt must prevent stale completion."
}

$scene = Get-Content -LiteralPath (Join-Path $root 'Assets/Scenes/WorldShell.unity') -Raw
$canvas = [regex]::Match($scene, '(?ms)^--- !u!1 &668023335\r?\n.*?(?=^--- !u!)').Value
$status = [regex]::Match($scene, '(?ms)^--- !u!114 &668023340\r?\n.*?(?=^--- !u!)').Value
$rect = [regex]::Match($scene, '(?ms)^--- !u!224 &1213515974\r?\n.*?(?=^--- !u!)').Value
$slideshow = [regex]::Match($scene, '(?ms)^--- !u!114 &668023345\r?\n.*?(?=^--- !u!)').Value
Assert-True ($canvas -match 'm_IsActive: 0' -and $canvas -match 'fileID: 668023340' -and
    $status -match 'label: \{fileID: 1133269812\}' -and $status -match 'minimumVisibleSeconds: 3.5' -and
    $rect -match 'm_Children:\r?\n  - \{fileID: 668023342\}\r?\n  - \{fileID: 1133269811\}' -and
    [regex]::Matches($slideshow, 'fileID: 21300000').Count -eq 8) 'Hidden transfer canvas must bind the actual destination label above the slideshow.'
$ids = [regex]::Matches($scene, '(?m)^--- !u!\d+ &(\d+)\r?$') | ForEach-Object { $_.Groups[1].Value }
Assert-True (@($ids | Group-Object | Where-Object Count -gt 1).Count -eq 0) 'Scene IDs must remain unique.'

Write-Output 'PASS: minimum display duration, independent readiness conditions, asynchronous unload, stale-session cancellation and hidden transfer scene wiring.'
