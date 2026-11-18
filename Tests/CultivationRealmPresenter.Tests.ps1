$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$script = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Cultivation/Presentation/CultivationRealmPresenter.cs') -Raw
$meta = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Cultivation/Presentation/CultivationRealmPresenter.cs.meta') -Raw
$scene = Get-Content -LiteralPath (Join-Path $root 'Assets/Scenes/WorldShell.unity') -Raw

$stubs = @'
using Game.CharacterPanel.Protocol;
using TMPro;
using UnityEngine;
namespace Game.CharacterPanel.Protocol {
    public static class JingjieNextRealmFields {
        public static bool IsValid(bool hasNext, string id, int stage, string name) =>
            hasNext ? !string.IsNullOrWhiteSpace(id)
                && !string.IsNullOrWhiteSpace(name) && stage >= 0
                : id == string.Empty && name == string.Empty && stage == 0;
    }
}
namespace TMPro {
    public class TMP_Text {
        public string text;
        public bool enableAutoSizing, enableWordWrapping;
        public float fontSizeMin, fontSizeMax;
    }
}
namespace UnityEngine
{
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public sealed class DisallowMultipleComponent : System.Attribute {}
    [System.AttributeUsage(System.AttributeTargets.Field)]
    public sealed class SerializeField : System.Attribute {}
    public class MonoBehaviour
    {
        protected static T FindObjectOfType<T>() where T : class => null;
    }
}
public sealed class NetworkManager
{
    public static NetworkManager Instance;
    public event System.Action<string> OnDisconnected;
    public void Disconnect() => OnDisconnected?.Invoke("test");
}
public sealed class ProgressionSnapshotResult
{
    public bool success;
    public long characterDbId;
    public string jingjieName;
    public bool hasNextJingjie;
    public string nextJingjieId;
    public int nextJingjieStage;
    public string nextJingjieName;
}
public sealed class GameSession
{
    public long LocalCharacterDbId = 42;
    public int SnapshotRequests;
    public ProgressionSnapshotResult LocalProgressionSnapshot;
    public event System.Action<ProgressionSnapshotResult> LocalProgressionSnapshotChanged;
    public event System.Action<long, long> LocalWorldEntered;
    public void RequestProgressionSnapshot() => SnapshotRequests++;
    public void Emit(ProgressionSnapshotResult result)
    {
        LocalProgressionSnapshot = result;
        LocalProgressionSnapshotChanged?.Invoke(result);
    }
    public void EnterWorld(long id)
    {
        LocalCharacterDbId = id;
        LocalProgressionSnapshot = null;
        LocalWorldEntered?.Invoke(1, id);
    }
}
'@
$script = $script -replace '(?m)^using [^;]+;\r?\n', ''
Add-Type -TypeDefinition ($stubs + "`n" + $script) -IgnoreWarnings | Out-Null

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

$network = [NetworkManager]::new()
[NetworkManager]::Instance = $network
$session = [GameSession]::new()
$presenter = [Game.Cultivation.Presentation.CultivationRealmPresenter]::new()
$current = [TMPro.TMP_Text]::new()
$next = [TMPro.TMP_Text]::new()
Set-Private $presenter 'session' $session
Set-Private $presenter 'currentRealmText' $current
Set-Private $presenter 'nextRealmText' $next
Invoke-Lifecycle $presenter 'OnEnable'
Assert-True ($current.text -eq '--' -and $next.text -eq '--' -and
    $session.SnapshotRequests -eq 1) `
    'Opening without a snapshot must show placeholders and request a snapshot.'
Assert-True ($next.enableAutoSizing -and $next.enableWordWrapping -and
    $next.fontSizeMin -eq 10 -and $next.fontSizeMax -eq 20) `
    'The existing next-realm text must fit names within its scene bounds.'

$realm = [ProgressionSnapshotResult]::new()
$realm.success = $true
$realm.characterDbId = 42
$realm.jingjieName = '炼气一层'
$realm.hasNextJingjie = $true
$realm.nextJingjieId = 'qi_refining'
$realm.nextJingjieStage = 2
$realm.nextJingjieName = '炼气二层'
$session.Emit($realm)
Assert-True ($current.text -eq '炼气一层' -and $next.text -eq '炼气二层') `
    'Current and next realm names must both come from the server snapshot.'
$failed = [ProgressionSnapshotResult]::new()
$failed.characterDbId = 42
$session.Emit($failed)
Assert-True ($current.text -eq '炼气一层' -and $next.text -eq '炼气二层') `
    'A failure without valid realm fields cannot erase the displayed snapshot.'
$noPath = [ProgressionSnapshotResult]::new()
$noPath.success = $true
$noPath.characterDbId = 42
$noPath.jingjieName = '炼气二层'
$noPath.nextJingjieId = ''
$noPath.nextJingjieName = ''
$session.Emit($noPath)
Assert-True ($current.text -eq '炼气二层' -and
    $next.text -eq '无下一突破路径') `
    'A server-declared no-path state must show the agreed prompt.'

$other = [ProgressionSnapshotResult]::new()
$other.success = $true
$other.characterDbId = 99
$other.jingjieName = '其他角色'
$other.nextJingjieId = ''
$other.nextJingjieName = ''
$session.Emit($other)
Assert-True ($current.text -eq '--' -and $next.text -eq '--') `
    'A different character cannot fill either realm.'
$session.Emit($realm)
$session.EnterWorld(99)
Assert-True ($current.text -eq '--' -and $next.text -eq '--') `
    'Entering as another character clears both realms.'
$session.Emit($other)
Assert-True ($current.text -eq '其他角色' -and
    $next.text -eq '无下一突破路径') `
    'A new character snapshot updates current and next realms.'
$network.Disconnect()
Assert-True ($current.text -eq '--' -and $next.text -eq '--') `
    'Disconnect clears both realms.'
Invoke-Lifecycle $presenter 'OnDisable'
$session.Emit($other)
Assert-True ($current.text -eq '--' -and $next.text -eq '--') `
    'Disabled UI must no longer process updates.'
Invoke-Lifecycle $presenter 'OnEnable'
Assert-True ($current.text -eq '其他角色' -and
    $next.text -eq '无下一突破路径' -and $session.SnapshotRequests -eq 1) `
    'Reopening renders cached data without an extra snapshot request.'
Invoke-Lifecycle $presenter 'OnDisable'

$guid = [regex]::Match($meta, '(?m)^guid: ([0-9a-f]{32})$').Groups[1].Value
Assert-True ($guid -ne '') 'Unity script GUID is required.'
Assert-True ((SceneBlock 1 904205820) -match
    'component: \{fileID: 904205824\}') `
    'Cultivation content must retain its existing realm presenter.'
Assert-True ((SceneBlock 114 904205824) -match
    "(?s)guid: $guid.*?session: \{fileID: 2108349142\}.*?" +
    'currentRealmText: \{fileID: 426218465\}.*?' +
    'nextRealmText: \{fileID: 944717356\}') `
    'Presenter must bind the existing session and both realm texts.'
Assert-True ((SceneBlock 114 426218465) -match
    '(?s)m_text: --.*?m_enableAutoSizing: 1.*?m_fontSizeMin: 10') `
    'Current realm should start empty and resize long names inside its text box.'
Assert-True ((SceneBlock 114 944717356) -match
    'm_text: "\\u6682\\u65E0\\u6570\\u636E"') `
    'Next realm cannot show the old example name without a server field.'
Write-Output 'PASS: cultivation realm cache, identity, disconnect and scene wiring.'
