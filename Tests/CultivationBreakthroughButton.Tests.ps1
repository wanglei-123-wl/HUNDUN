$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Cultivation/Presentation/CultivationBreakthroughButton.cs') -Raw
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
    public class MonoBehaviour
    {
        public static UnityEngine.UI.Button MockButton;
        protected static T FindObjectOfType<T>() where T : class => null;
        protected T GetComponent<T>() where T : class => MockButton as T;
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
    public class JingjieBreakthroughRuntime
    {
        public static JingjieBreakthroughRuntime Instance;
        public event Action RequestTimedOut;
        public event Action<JingjieBreakthroughResultDto> ResultConfirmed;
        public event Action<string> RequestRejected;
        public bool HasUnresolvedOperation;
        public int Requests;
        public bool TryBreakthrough()
        {
            if (HasUnresolvedOperation) return false;
            HasUnresolvedOperation = true;
            Requests++;
            return true;
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
}
public class GameSession
{
    public bool IsWorldReady;
    public string JingjieOperationSessionId;
    public long LocalCharacterDbId = 42;
    public ProgressionSnapshotResult LocalProgressionSnapshot;
}
'@
$source = $source -replace '(?m)^using [^;]+;\r?\n', ''
Add-Type -TypeDefinition ($stubs + "`n" + $source) -IgnoreWarnings | Out-Null

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
$presenter = [Game.Cultivation.Presentation.CultivationBreakthroughButton]::new()
Set-Private $presenter 'session' $session
Set-Private $presenter 'inventory' $inventory
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
$button.Press()
Assert-True ($runtime.Requests -eq 1) 'Unconfirmed operation cannot be sent twice.'
$result = [Game.CharacterPanel.Protocol.JingjieBreakthroughResultDto]::new()
$result.success = $true
$runtime.Confirm($result)
Assert-True ($button.interactable -and
    [Game.UI.Notice.GameNoticeService]::Messages.Count -eq 1 -and
    [Game.UI.Notice.GameNoticeService]::Messages[0] -eq '突破成功') `
    'Only the authoritative first success enables another attempt and shows success.'
$result.idempotentReplay = $true
$runtime.Confirm($result)
Assert-True ([Game.UI.Notice.GameNoticeService]::Messages.Count -eq 1) `
    'A replay cannot show a second success.'
$button.Press()
$result.success = $false
$result.idempotentReplay = $false
$result.errorCode = 'required_items_insufficient'
$runtime.Confirm($result)
Assert-True ($runtime.Requests -eq 2 -and $button.interactable -and
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
$network.IsConnected = $false
Invoke-Lifecycle $presenter 'Update'
Assert-True (-not $button.interactable) 'Disconnect must disable sending.'
Invoke-Lifecycle $presenter 'OnDisable'
$runtime.Confirm($result)
Assert-True ([Game.UI.Notice.GameNoticeService]::Messages.Count -eq 4) `
    'Closed UI must no longer show result notices.'
Invoke-Lifecycle $presenter 'OnEnable'
Invoke-Lifecycle $presenter 'Update'
Assert-True (-not $button.interactable) 'Reopening while offline must stay disabled.'
Invoke-Lifecycle $presenter 'OnDisable'

$guid = [regex]::Match($meta, '(?m)^guid: ([0-9a-f]{32})$').Groups[1].Value
Assert-True ($guid -ne '') 'Unity script GUID is required.'
Assert-True ((SceneBlock 1 1194018312) -match
    'component: \{fileID: 2200000005\}') `
    'The adaptation component must be attached to the existing breakthrough button.'
Assert-True ((SceneBlock 114 2200000005) -match
    "(?s)guid: $guid.*?session: \{fileID: 2108349142\}.*?inventory: \{fileID: 390961493\}") `
    'The button must bind the current world session and current inventory.'
Assert-True ((SceneBlock 114 1194018314) -match 'm_Interactable: 1') `
    'The editor button stays editable; runtime gates interaction.'
Write-Output 'PASS: breakthrough button binding, pending guard, notices and scene wiring.'
