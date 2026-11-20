$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Cultivation/Presentation/CultivationBreakthroughButton.cs') -Raw
$presentationSource = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Cultivation/Presentation/JingjieBreakthroughPresentation.cs') -Raw
$meta = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Cultivation/Presentation/CultivationBreakthroughButton.cs.meta') -Raw
$scene = Get-Content -LiteralPath (Join-Path $root 'Assets/Scenes/WorldShell.unity') -Raw

$stubs = @'
using System;
using System.Collections.Generic;
using Game.CharacterPanel.Protocol;
using Game.CharacterPanel.Runtime;
using Game.Cultivation.Protocol;
using Game.Cultivation.Runtime;
using Game.Inventory;
using Game.UI.Notice;
using UnityEngine;
using UnityEngine.UI;
namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class DisallowMultipleComponent : Attribute {}
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class RequireComponent : Attribute
    {
        public RequireComponent(Type type) {}
    }
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SerializeField : Attribute {}
    public static class Time { public static float unscaledTime; }
    public static class Debug { public static void LogWarning(string message, object context) {} }
    public class Transform
    {
        public GameObject gameObject;
        public bool TryGetComponent<T>(out T result) where T : class
        { result = gameObject.GetComponent<T>(); return result != null; }
    }
    public class GameObject
    {
        public readonly List<object> Components = new List<object>();
        public readonly Transform transform;
        public bool activeSelf = true;
        public GameObject() { transform = new Transform { gameObject = this }; }
        public T GetComponent<T>() where T : class => Components.Find(value => value is T) as T;
        public T AddComponent<T>() where T : MonoBehaviour, new()
        {
            var result = new T();
            result.gameObject = this;
            Components.Add(result);
            return result;
        }
    }
    public class MonoBehaviour
    {
        public static UnityEngine.UI.Button MockButton;
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
        public bool isActiveAndEnabled => gameObject.activeSelf;
        public MonoBehaviour() { gameObject = new GameObject(); gameObject.Components.Add(this); }
        protected static T FindObjectOfType<T>() where T : class => null;
        public T GetComponent<T>() where T : class => gameObject.GetComponent<T>() ?? MockButton as T;
    }
}
namespace UnityEngine.UI
{
    public sealed class Button
    {
        public bool interactable;
        public readonly ClickEvent onClick = new ClickEvent();
        public void Press() { if (interactable) onClick.Invoke(); }
    }
    public sealed class ClickEvent
    {
        private event Action handlers;
        public void AddListener(Action handler) { handlers += handler; }
        public void RemoveListener(Action handler) { handlers -= handler; }
        public void Invoke() { handlers?.Invoke(); }
    }
}
namespace Game.CharacterPanel.Protocol
{
    public class JingjieBreakthroughResultDto
    {
        public bool success;
        public bool idempotentReplay;
        public string errorCode;
    }
}
namespace Game.CharacterPanel.Runtime
{
    public class JingjieBreakthroughRuntime : MonoBehaviour
    {
        public static JingjieBreakthroughRuntime Instance;
        public event Action RequestTimedOut;
        public event Action<JingjieBreakthroughResultDto> ResultConfirmed;
        public event Action<string> RequestRejected;
        public bool HasUnresolvedOperation;
        public int Requests;
        public bool SendSucceeds = true;
        public bool TryBreakthrough()
        {
            if (HasUnresolvedOperation) return false;
            HasUnresolvedOperation = true;
            Requests++;
            return SendSucceeds;
        }
        public void Confirm(JingjieBreakthroughResultDto result)
        {
            HasUnresolvedOperation = false;
            ResultConfirmed?.Invoke(result);
        }
        public void Reject(string error) { RequestRejected?.Invoke(error); }
        public void Timeout() { RequestTimedOut?.Invoke(); }
    }
}
namespace Game.UI.Notice
{
    public static class GameNoticeService
    {
        public static List<string> Messages = new List<string>();
        public static bool Show(string message)
        {
            Messages.Add(message);
            return true;
        }
    }
}
namespace Game.Inventory.Model.V2 {
    public class InventoryOwnershipSnapshot { public long CharacterDbId = 42; }
}
namespace Game.Inventory {
    public class InventoryModuleRuntime {
        public Game.Inventory.Model.V2.InventoryOwnershipSnapshot CurrentSnapshot =
            new Game.Inventory.Model.V2.InventoryOwnershipSnapshot();
    }
}
namespace Game.Cultivation.Protocol {
    public class RefiningResultDto {}
    public class RefiningBreakthroughDto {
        public string toJingjieId = "qi_refining";
        public int toJingjieStage = 2;
    }
    public static class RefiningBreakthroughConditions {
        public static bool TryFindPath(RefiningResultDto catalog,
            ProgressionSnapshotResult snapshot, long characterDbId,
            out RefiningBreakthroughDto path) {
            path = catalog != null && snapshot != null
                && snapshot.characterDbId == characterDbId
                ? new RefiningBreakthroughDto() : null;
            return path != null;
        }
        public static bool MatchesNextRealm(RefiningBreakthroughDto path,
            ProgressionSnapshotResult snapshot) =>
            path != null && snapshot != null && snapshot.hasNextJingjie
                && path.toJingjieId == snapshot.nextJingjieId
                && path.toJingjieStage == snapshot.nextJingjieStage;
        public static bool CanAttempt(RefiningBreakthroughDto path,
            long cultivation, Game.Inventory.Model.V2.InventoryOwnershipSnapshot inventory,
            long characterDbId) =>
            path != null && cultivation >= 50 && inventory != null
                && inventory.CharacterDbId == characterDbId;
    }
}
namespace Game.Cultivation.Runtime {
    public class RefiningSessionRuntime {
        public static RefiningSessionRuntime Instance;
        public Game.Cultivation.Protocol.RefiningResultDto Catalog;
    }
}
public class ProgressionSnapshotResult {
    public long characterDbId;
    public long jingjieExp;
    public bool hasNextJingjie;
    public string nextJingjieId;
    public int nextJingjieStage;
    public long requiredJingjieExp;
}
public class NetworkManager
{
    public static NetworkManager Instance;
    public bool IsConnected;
    public long ConnectionEpoch = 1;
}
public class GameSession
{
    public bool IsWorldReady;
    public bool IsLocalPlayerDead;
    public string JingjieOperationSessionId;
    public long LocalCharacterDbId = 42;
    public ProgressionSnapshotResult LocalProgressionSnapshot;
}
public class LocalPlayerProvider
{
    public static LocalPlayerProvider Instance;
    public Transform PlayerTransform;
}
public class PlayerEffectPresenter : MonoBehaviour
{
    public readonly HashSet<string> Playing = new HashSet<string>();
    public int PlayCalls, StopCalls;
    public bool TryPlay(string name) { PlayCalls++; return Playing.Add(name); }
    public void Stop(string name) { if (Playing.Remove(name)) StopCalls++; }
}
namespace Game.Cultivation.Presentation
{
    public class CultivationWindowToggle
    {
        public int CloseCalls;
        public void Close() { CloseCalls++; }
    }
}
'@
$source = $source -replace '(?m)^using [^;]+;\r?\n', ''
$presentationSource = $presentationSource -replace '(?m)^using [^;]+;\r?\n', ''
Add-Type -TypeDefinition ($stubs + "`n" + $source + "`n" + $presentationSource) -IgnoreWarnings -WarningAction SilentlyContinue | Out-Null

function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
}
function Invoke-Lifecycle($target, [string]$name) {
    $target.GetType().GetMethod(
        $name, [System.Reflection.BindingFlags]'Instance,NonPublic'
    ).Invoke($target, @()) | Out-Null
}
function Set-Private($target, [string]$name, $value) {
    $target.GetType().GetField(
        $name, [System.Reflection.BindingFlags]'Instance,NonPublic'
    ).SetValue($target, $value)
}
function SceneBlock([int]$kind, [long]$id) {
    $pattern = "(?s)--- !u!$kind &$id`r?`n(?<body>.*?)(?=`r?`n--- !u!)"
    $matches = [regex]::Matches($scene, $pattern)
    Assert-True ($matches.Count -eq 1) "Scene block $kind/$id must exist once."
    return $matches[0].Groups['body'].Value
}

$button = [UnityEngine.UI.Button]::new()
[UnityEngine.MonoBehaviour]::MockButton = $button
$session = [GameSession]::new()
$network = [NetworkManager]::new()
[NetworkManager]::Instance = $network
$runtime = [Game.CharacterPanel.Runtime.JingjieBreakthroughRuntime]::new()
$refining = [Game.Cultivation.Runtime.RefiningSessionRuntime]::new()
$refining.Catalog = [Game.Cultivation.Protocol.RefiningResultDto]::new()
[Game.Cultivation.Runtime.RefiningSessionRuntime]::Instance = $refining
$inventory = [Game.Inventory.InventoryModuleRuntime]::new()
$window = [Game.Cultivation.Presentation.CultivationWindowToggle]::new()
$effects = [PlayerEffectPresenter]::new()
$provider = [LocalPlayerProvider]::new()
$provider.PlayerTransform = $effects.transform
[LocalPlayerProvider]::Instance = $provider
$presenter = [Game.Cultivation.Presentation.CultivationBreakthroughButton]::new()
Set-Private $presenter 'session' $session
Set-Private $presenter 'inventory' $inventory
Set-Private $presenter 'window' $window
Invoke-Lifecycle $presenter 'OnEnable'
Assert-True (-not $button.interactable) 'Cannot break through before entering the world.'
$session.IsWorldReady = $true
$session.JingjieOperationSessionId = 'session'
$network.IsConnected = $true
Invoke-Lifecycle $presenter 'Update'
Assert-True (-not $button.interactable) 'Runtime must be available.'
[Game.CharacterPanel.Runtime.JingjieBreakthroughRuntime]::Instance = $runtime
Invoke-Lifecycle $presenter 'Update'
Assert-True (-not $button.interactable) 'Without a matching progression snapshot the button stays disabled.'
$progression = [ProgressionSnapshotResult]::new()
$progression.characterDbId = 42
$progression.jingjieExp = 50
$progression.requiredJingjieExp = 50
$progression.hasNextJingjie = $true
$progression.nextJingjieId = 'qi_refining'
$progression.nextJingjieStage = 2
$session.LocalProgressionSnapshot = $progression
Invoke-Lifecycle $presenter 'Update'
Assert-True ($button.interactable) 'A ready operation enables the button.'
$progression.hasNextJingjie = $false
Invoke-Lifecycle $presenter 'Update'
Assert-True (-not $button.interactable) `
    'The server declaring no next path must disable the breakthrough button.'
$progression.hasNextJingjie = $true
$progression.nextJingjieStage = 3
Invoke-Lifecycle $presenter 'Update'
Assert-True (-not $button.interactable) `
    'A stale catalog targeting another realm cannot authorize the button.'
$progression.nextJingjieStage = 2
$progression.requiredJingjieExp = 51
Invoke-Lifecycle $presenter 'Update'
Assert-True (-not $button.interactable) `
    'The server next-breakthrough requirement must gate the button.'
$progression.requiredJingjieExp = 50
Invoke-Lifecycle $presenter 'Update'
Assert-True ($button.interactable) 'Matching current data permits breakthrough.'
$button.Press()
Assert-True ($runtime.Requests -eq 1 -and -not $button.interactable) `
    'Click must send once and disable the button while unresolved.'
Assert-True ($window.CloseCalls -eq 1 -and $effects.Playing.Contains('MeshFX_Wind')) `
    'An accepted click must close the cultivation window and play Wind on the player.'
$button.Press()
Assert-True ($runtime.Requests -eq 1) 'Unconfirmed operation cannot be sent twice.'
$flow = [Game.Cultivation.Presentation.JingjieBreakthroughPresentation]::Ensure($runtime)
Invoke-Lifecycle $presenter 'OnDisable'
$result = [Game.CharacterPanel.Protocol.JingjieBreakthroughResultDto]::new()
$result.success = $true
$runtime.Confirm($result)
Assert-True ($flow.IsBusy -and [Game.UI.Notice.GameNoticeService]::Messages.Count -eq 0) `
    'An early authoritative success must wait for the full effect even while the UI is closed.'
[UnityEngine.Time]::unscaledTime = 4.999
Invoke-Lifecycle $flow 'Update'
Assert-True ($effects.Playing.Contains('MeshFX_Wind') -and
    [Game.UI.Notice.GameNoticeService]::Messages.Count -eq 0) 'Wind must remain visible before five seconds.'
Invoke-Lifecycle $presenter 'OnEnable'
Assert-True (-not $button.interactable) 'Reopening the UI must not allow a second request during the effect.'
[UnityEngine.Time]::unscaledTime = 5
Invoke-Lifecycle $flow 'Update'
Invoke-Lifecycle $presenter 'Update'
Assert-True ($button.interactable -and -not $flow.IsBusy -and
    -not $effects.Playing.Contains('MeshFX_Wind') -and
    [Game.UI.Notice.GameNoticeService]::Messages.Count -eq 1 -and
    [Game.UI.Notice.GameNoticeService]::Messages[0] -eq '突破成功') `
    'After five seconds, Wind must disappear before the confirmed success is displayed.'
$result.idempotentReplay = $true
$runtime.Confirm($result)
Assert-True ([Game.UI.Notice.GameNoticeService]::Messages.Count -eq 1) `
    'A replay cannot show a second success.'
$button.Press()
$result.success = $false
$result.idempotentReplay = $false
$result.errorCode = 'required_items_insufficient'
$runtime.Confirm($result)
Invoke-Lifecycle $presenter 'Update'
Assert-True ($runtime.Requests -eq 2 -and $button.interactable -and
    -not $effects.Playing.Contains('MeshFX_Wind') -and
    [Game.UI.Notice.GameNoticeService]::Messages[1] -eq '突破材料不足') `
    'Definitive failure allows a new click with a business error notice.'
$button.Press()
$runtime.Timeout()
Assert-True (-not $button.interactable -and
    [Game.UI.Notice.GameNoticeService]::Messages[2] -match '尚未确认') `
    'Timeout cannot unlock an unresolved operation.'
$runtime.Reject('jingjie_operation_busy')
Assert-True (-not $button.interactable -and
    [Game.UI.Notice.GameNoticeService]::Messages[3] -eq '突破处理中，请稍后') `
    'A busy response cannot unlock an unresolved operation.'
[UnityEngine.Time]::unscaledTime = 10
Invoke-Lifecycle $flow 'Update'
Assert-True (-not $effects.Playing.Contains('MeshFX_Wind') -and $flow.IsBusy -and
    [Game.UI.Notice.GameNoticeService]::Messages.Count -eq 4) `
    'Wind must stop after five seconds without inventing success when the server is late.'
$result.success = $true
$result.errorCode = $null
$runtime.Confirm($result)
Invoke-Lifecycle $presenter 'Update'
Assert-True (-not $flow.IsBusy -and $button.interactable -and
    [Game.UI.Notice.GameNoticeService]::Messages.Count -eq 5 -and
    [Game.UI.Notice.GameNoticeService]::Messages[4] -eq '突破成功') `
    'A late authoritative success may show after the effect has already ended.'
$runtime.Confirm($result)
Assert-True ([Game.UI.Notice.GameNoticeService]::Messages.Count -eq 5) 'Repeated results cannot replay success.'

$button.Press()
$network.IsConnected = $false
Invoke-Lifecycle $flow 'Update'
Invoke-Lifecycle $presenter 'Update'
Assert-True (-not $button.interactable -and -not $flow.IsBusy -and
    -not $effects.Playing.Contains('MeshFX_Wind')) 'Disconnect must disable sending and remove the effect.'
Invoke-Lifecycle $presenter 'OnDisable'
$runtime.Confirm($result)
Assert-True ([Game.UI.Notice.GameNoticeService]::Messages.Count -eq 5) `
    'An old result after disconnect must not show success.'
Invoke-Lifecycle $presenter 'OnEnable'
Invoke-Lifecycle $presenter 'Update'
Assert-True (-not $button.interactable) 'Reopening while offline must stay disabled.'
Invoke-Lifecycle $presenter 'OnDisable'

$network.IsConnected = $true
Invoke-Lifecycle $presenter 'OnEnable'
$button.Press()
$session.IsLocalPlayerDead = $true
Invoke-Lifecycle $flow 'Update'
$runtime.Confirm($result)
Assert-True (-not $flow.IsBusy -and -not $effects.Playing.Contains('MeshFX_Wind') -and
    [Game.UI.Notice.GameNoticeService]::Messages.Count -eq 5) 'Death must clean up without a delayed success notice.'
$session.IsLocalPlayerDead = $false
Invoke-Lifecycle $presenter 'Update'
$button.Press()
$session.JingjieOperationSessionId = 'new-session'
Invoke-Lifecycle $flow 'Update'
$runtime.Confirm($result)
Assert-True (-not $flow.IsBusy -and [Game.UI.Notice.GameNoticeService]::Messages.Count -eq 5) `
    'Changing operation sessions must discard old presentation callbacks.'
Invoke-Lifecycle $presenter 'Update'
$runtime.SendSucceeds = $false
$closeCalls = $window.CloseCalls
$button.Press()
Assert-True ($window.CloseCalls -eq $closeCalls -and -not $flow.IsBusy -and
    -not $effects.Playing.Contains('MeshFX_Wind')) 'An unconfirmed send must not close the UI or start an effect.'
Invoke-Lifecycle $presenter 'OnDisable'

$guid = [regex]::Match($meta, '(?m)^guid: ([0-9a-f]{32})\r?$').Groups[1].Value
Assert-True ($guid -ne '') 'Unity script GUID is required.'
Assert-True ((SceneBlock 1 1194018312) -match
    'component: \{fileID: 2200000005\}') `
    'The adaptation component must be attached to the existing breakthrough button.'
Assert-True ((SceneBlock 114 2200000005) -match
    "(?s)guid: $guid.*?session: \{fileID: 2108349142\}.*?inventory: \{fileID: 390961493\}") `
    'The button must bind the current world session and current inventory.'
Assert-True ((SceneBlock 114 1194018314) -match 'm_Interactable: 1') `
    'The editor button stays editable; runtime gates interaction.'
Assert-True ((SceneBlock 114 2200000005) -match 'window: \{fileID: 860000000000500005\}') `
    'The breakthrough button must close the existing cultivation window controller.'
$effectSettings = Get-Content -LiteralPath (Join-Path $root 'Assets/Settings/玩家公共表现配置.asset') -Raw
$windEntry = [regex]::Match($effectSettings,
    '(?ms)^  - 特效名称: MeshFX_Wind\r?\n(?<body>.*?)(?=^  - 特效名称:|\z)').Groups['body'].Value
$windMeta = Get-Content -LiteralPath (Join-Path $root (
    'Assets/Piloto Studio/Super Realistic FX Bundle 02/Realistic Mesh Effects/MeshFX_Wind.prefab.meta')) -Raw
$windGuid = [regex]::Match($windMeta, '(?m)^guid: ([0-9a-f]{32})\r?$').Groups[1].Value
Assert-True ($windGuid -ne '' -and $windEntry.Contains(
    'fileID: 2867752102684665680, guid: ' + $windGuid + ', type: 3')) `
    'The registered effect must reference the actual Wind prefab root.'
Assert-True ($windEntry -match '(?m)^    触发方式: 1\r?$' -and
    $windEntry -match '(?m)^    播放方式: 1\r?$' -and
    $windEntry -match '(?m)^    显示条件: 1\r?$') `
    'Wind must play on demand on a living player, looping until explicitly removed.'
Write-Output 'PASS: breakthrough close, five-second Wind playback, authoritative notices, pending guards, cleanup and scene wiring.'
