$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$files = @(
    'Assets/Scripts/WorldMapSceneCatalog.cs',
    'Assets/Scripts/NetWork/GateEntryHandoff.cs',
    'Assets/Scripts/WorldMapLayerLoader.cs'
)
$sources = ($files | ForEach-Object {
    (Get-Content -LiteralPath (Join-Path $root $_) -Raw) -replace '(?m)^using [^\r\n]+;\r?\n', ''
}) -join "`n"

$stubs = @'
namespace UnityEngine
{
    [System.AttributeUsage(System.AttributeTargets.Field)]
    public sealed class SerializeField : System.Attribute {}
    public class MonoBehaviour {}
    public static class Debug
    {
        public static void LogError(string message) {}
    }
    public static class PlayerPrefs
    {
        public static string SelectedCharacterDbId = "1";
        public static string GetString(string key, string fallback) => SelectedCharacterDbId;
    }
}
namespace UnityEngine.SceneManagement
{
    public struct Scene
    {
        public bool Valid;
        public bool IsValid() => Valid;
    }
    public static class SceneManager
    {
        public static int ActivationCount;
        public static bool SetActiveScene(Scene scene)
        {
            ActivationCount++;
            return scene.IsValid();
        }
    }
}
namespace UnityEngine.ResourceManagement.ResourceProviders
{
    public sealed class SceneInstance
    {
        public UnityEngine.SceneManagement.Scene Scene;
    }
}
namespace UnityEngine.ResourceManagement.AsyncOperations
{
    public enum AsyncOperationStatus { None, Succeeded, Failed }
    public sealed class AsyncOperationHandle<T>
    {
        public AsyncOperationStatus Status = AsyncOperationStatus.Succeeded;
        public T Result;
        public System.Exception OperationException;
        public bool IsValid() => true;
    }
}
namespace UnityEngine.AddressableAssets
{
    public static class Addressables
    {
        public static int Releases;
        public static void Release<T>(
            UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<T> handle)
        {
            Releases++;
        }
    }
}
public sealed class NetworkManager
{
    public static NetworkManager Instance;
    public bool IsConnected = true;
    public long ConnectionEpoch = 5;
    public void Disconnect() { IsConnected = false; }
}
public sealed class NetMessageDispatcher
{
    public static NetMessageDispatcher Instance;
    public bool HasDeferredWorldOverflow;
    public void ClearDeferredWorldMessages() {}
}
public sealed class GameSession
{
    public int Starts;
    public string FailureReason;
    public void StartSession() { Starts++; }
    public void ReturnToLogin(string reason)
    {
        FailureReason = reason;
        GateEntryHandoff.Clear();
        NetworkManager.Instance.Disconnect();
    }
}
public static class RemoteGameContent
{
    public const string LoginSceneName = "SampleScene";
    public static string LoadedAddress;
    public static int Loads;
    public static bool FailLoad;
    public static UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<
        UnityEngine.ResourceManagement.ResourceProviders.SceneInstance> LoadMapSceneAsync(
            string address)
    {
        LoadedAddress = address;
        Loads++;
        return new UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<
            UnityEngine.ResourceManagement.ResourceProviders.SceneInstance>
        {
            Status = FailLoad
                ? UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Failed
                : UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded,
            Result = new UnityEngine.ResourceManagement.ResourceProviders.SceneInstance
            {
                Scene = new UnityEngine.SceneManagement.Scene { Valid = !FailLoad }
            }
        };
    }
    public static void LoadSceneOrLog(string name) {}
}
'@

Add-Type -TypeDefinition ("using System;`nusing System.Collections;`n" +
    "using UnityEngine;`nusing UnityEngine.AddressableAssets;`n" +
    "using UnityEngine.ResourceManagement.AsyncOperations;`n" +
    "using UnityEngine.ResourceManagement.ResourceProviders;`n" +
    "using UnityEngine.SceneManagement;`n" + $sources + $stubs) -IgnoreWarnings | Out-Null

function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
}
function New-Entry([string]$worldId, [string]$mapId) {
    [NetworkManager]::Instance = [NetworkManager]::new()
    [NetMessageDispatcher]::Instance = [NetMessageDispatcher]::new()
    [RemoteGameContent]::LoadedAddress = $null
    [RemoteGameContent]::Loads = 0
    [RemoteGameContent]::FailLoad = $false
    [UnityEngine.SceneManagement.SceneManager]::ActivationCount = 0
    [UnityEngine.AddressableAssets.Addressables]::Releases = 0
    [GateEntryHandoff]::Store(5, 1, $worldId, $mapId, '{}')
    $loader = [WorldMapLayerLoader]::new()
    $session = [GameSession]::new()
    [WorldMapLayerLoader].GetField('gameSession',
        [System.Reflection.BindingFlags]'Instance,NonPublic').SetValue($loader, $session)
    $start = [WorldMapLayerLoader].GetMethod('Start',
        [System.Reflection.BindingFlags]'Instance,NonPublic')
    return [pscustomobject]@{
        Steps = [System.Collections.IEnumerator]$start.Invoke($loader, @())
        Session = $session
    }
}

$demo = New-Entry 'world_001' 'map_demo_1'
Assert-True ($demo.Steps.MoveNext() -and
    [RemoteGameContent]::LoadedAddress -eq 'game/scene/demo1-map' -and
    $demo.Session.Starts -eq 0) 'Demo 1 must load its own map before starting the session.'
Assert-True (-not $demo.Steps.MoveNext() -and $demo.Session.Starts -eq 1 -and
    [UnityEngine.SceneManagement.SceneManager]::ActivationCount -eq 1) `
    'Demo 1 must activate its map before starting the world session.'

$village = New-Entry 'world_002' 'map_001'
Assert-True ($village.Steps.MoveNext() -and
    [RemoteGameContent]::LoadedAddress -eq 'game/scene/map001') `
    'The first-spawn world must select the village map.'
Assert-True (-not $village.Steps.MoveNext() -and $village.Session.Starts -eq 1) `
    'The village session must start after its map activates.'

$unknown = New-Entry 'world_002' 'map_demo_1'
Assert-True (-not $unknown.Steps.MoveNext() -and
    [RemoteGameContent]::Loads -eq 0 -and $unknown.Session.Starts -eq 0 -and
    -not [NetworkManager]::Instance.IsConnected -and
    [GateEntryHandoff]::TakeFailure().Contains('destination')) `
    'Unknown world/map pairs must not load a fallback map or spawn a player.'

$lost = New-Entry 'world_002' 'map_001'
Assert-True ($lost.Steps.MoveNext()) 'The map load must start before the disconnect case.'
[NetworkManager]::Instance.Disconnect()
Assert-True (-not $lost.Steps.MoveNext() -and $lost.Session.Starts -eq 0 -and
    [UnityEngine.SceneManagement.SceneManager]::ActivationCount -eq 0) `
    'Disconnect during map load must prevent activation and player spawn.'

$failed = New-Entry 'world_001' 'map_demo_1'
[RemoteGameContent]::FailLoad = $true
Assert-True ($failed.Steps.MoveNext()) 'The failed map load must begin.'
Assert-True (-not $failed.Steps.MoveNext() -and $failed.Session.Starts -eq 0 -and
    [UnityEngine.AddressableAssets.Addressables]::Releases -eq 1 -and
    [GateEntryHandoff]::TakeFailure().Contains('resource')) `
    'A missing map resource must close entry and show an update error.'

$sessionSource = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/GameSession.cs') -Raw
$selectionSource = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/LoginScripts/CharacterSelectPanelUI.cs') -Raw
$messagesSource = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/NetWork/NetMessages.cs') -Raw
Assert-True ($sessionSource.Contains('sceneId = serverSceneId;') -and
    $sessionSource.Contains('activeMapId = serverMapId;') -and
    $sessionSource.Contains('NetworkManager activeNetwork = NetworkManager.Instance != null') -and
    $sessionSource.Contains('NetMessageDispatcher activeDispatcher = NetMessageDispatcher.Instance != null') -and
    $sessionSource.Contains('selectedCharacterDbId, sceneId, activeMapId, out string acceptedResult') -and
    $sessionSource.Contains('!string.Equals(activeMapId, result.mapId, System.StringComparison.Ordinal)') -and
    $sessionSource.IndexOf('localPlayerReconciler?.ResetAtSpawn(spawnPosition, spawnRotation)') -lt
        $sessionSource.IndexOf('if (enterWorldConfirmed) StartLocalPlayerInput();') -and
    -not $sessionSource.Contains('request.sceneId =') -and
    [regex]::Match($messagesSource,
        '(?s)public class EnterWorldRequest.*?public EnterWorldRequest\(\)').Value -notmatch
        'public string sceneId;' -and
    $selectionSource.Contains('StartCoroutine(LoadSceneWithProgress(character.characterDbId))')) `
    'The selected map and authoritative spawn must be applied before local input.'

Write-Output 'PASS: server-selected map load, handoff identity, fail-closed entry and spawn order.'
