$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$presenter = Get-Content (Join-Path $root 'Assets/Scripts/CharacterPanel/Presentation/CharacterPanelPresenter.cs') -Raw
$display = Get-Content (Join-Path $root 'Assets/Scripts/Cultivation/Presentation/CultivationProgressDisplay.cs') -Raw
$scene = Get-Content (Join-Path $root 'Assets/Scenes/WorldShell.unity') -Raw
$sessionSource = Get-Content (Join-Path $root 'Assets/Scripts/GameSession.cs') -Raw

$stubs = @'
namespace UnityEngine
{
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public sealed class DisallowMultipleComponent : System.Attribute {}
    [System.AttributeUsage(System.AttributeTargets.Field)]
    public sealed class SerializeField : System.Attribute {}
    public class MonoBehaviour
    {
        public Transform transform = new Transform();
        protected static T FindObjectOfType<T>() where T : class => null;
        protected T[] GetComponentsInChildren<T>(bool includeInactive) => System.Array.Empty<T>();
    }
    public class GameObject { public string name; }
    public class Transform
    {
        public string name;
        public object Component;
        public readonly System.Collections.Generic.Dictionary<string, Transform> Children = new();
        public Transform Find(string path)
        {
            Transform current = this;
            foreach (string part in path.Split('/'))
                if (!current.Children.TryGetValue(part, out current)) return null;
            return current;
        }
        public T GetComponent<T>() where T : class => Component as T;
        public T[] GetComponentsInChildren<T>(bool includeInactive) => System.Array.Empty<T>();
        public T GetComponentInChildren<T>(bool includeInactive) => default;
    }
    public static class Mathf
    {
        public static float Clamp01(float value) => System.Math.Clamp(value, 0f, 1f);
    }
}
namespace UnityEngine.UI
{
    public class Slider { public float value; }
    public class Image
    {
        public enum Type { Simple, Filled }
        public Type type;
        public float fillAmount;
    }
}
namespace TMPro
{
    public class TMP_Text { public string text; public UnityEngine.GameObject gameObject = new UnityEngine.GameObject(); }
}
namespace Game.Cultivation.Protocol
{
    public sealed class CultivationProfessionProgressDto { public int rank; }
    public sealed class CultivationSnapshotDto
    {
        public long characterDbId;
        public long totalComprehension;
        public CultivationProfessionProgressDto alchemyProfession;
        public CultivationProfessionProgressDto forgingProfession;
    }
}
namespace Game.Cultivation.Runtime
{
    public sealed class CultivationSessionRuntime
    {
        public static CultivationSessionRuntime Instance;
        public CultivationSnapshotDto Snapshot;
        public int SnapshotRequests;
        public event Action<CultivationSnapshotDto> SnapshotChanged;
        public bool RequestSnapshot() { SnapshotRequests++; return true; }
        public void Apply(CultivationSnapshotDto snapshot)
        {
            Snapshot = snapshot;
            SnapshotChanged?.Invoke(snapshot);
        }
    }
}
public sealed class NetworkManager
{
    public static NetworkManager Instance;
    public event System.Action<string> OnDisconnected;
    public void Disconnect() => OnDisconnected?.Invoke("test");
}
public sealed class GameSession
{
    public bool IsWorldReady = true;
    public long LocalCharacterDbId = 42;
    public string LocalNickname = "tester";
    public int LocalEnterWorldHp = 75;
    public int LocalEnterWorldMaxHp = 1090;
    public long LocalEnterWorldCombatRevision = 10;
    public AttributeSnapshotResult LocalAttributeSnapshot;
    public ProgressionSnapshotResult LocalProgressionSnapshot;
    public int SnapshotRequestCount;
    public int ProgressionRequestCount;
    public event System.Action<long, long> LocalWorldEntered;
    public event System.Action<AttributeSnapshotResult> LocalAttributeSnapshotChanged;
    public event System.Action<ProgressionSnapshotResult> LocalProgressionSnapshotChanged;
    public void Enter() => LocalWorldEntered?.Invoke(1, LocalCharacterDbId);
    public void RequestAttributeSnapshot() => SnapshotRequestCount++;
    public void RequestProgressionSnapshot() => ProgressionRequestCount++;
    public void Apply(AttributeSnapshotResult result)
    {
        if (result.success && result.characterDbId == LocalCharacterDbId)
        {
            LocalAttributeSnapshot = result;
            LocalAttributeSnapshotChanged?.Invoke(result);
        }
    }
    public void Apply(ProgressionSnapshotResult result)
    {
        if (result.success && result.characterDbId == LocalCharacterDbId)
        {
            LocalProgressionSnapshot = result;
            LocalProgressionSnapshotChanged?.Invoke(result);
        }
    }
}
public sealed class AttributeSnapshotFinal
{
    public int maxHp;
    public int attack;
    public int defense;
    public int critRateBps;
    public int critDamageBps;
    public int magicAttack;
    public int magicDefense;
}
public sealed class AttributeSnapshotResult
{
    public bool success;
    public long characterDbId;
    public AttributeSnapshotFinal final;
}
public sealed class ProgressionSnapshotResult
{
    public bool success;
    public long characterDbId;
    public int currentMp;
    public int maxMp;
    public long jingjieExp;
    public long requiredJingjieExp;
    public long totalJingjieExp;
    public bool hasNextJingjie;
    public string jingjieName;
}
public struct CombatEntityKey
{
    public string EntityType { get; }
    public string EntityId { get; }
    public CombatEntityKey(string type, string id) { EntityType = type; EntityId = id; }
}
public sealed class CombatStateData
{
    public int Hp { get; }
    public int MaxHp { get; }
    public long CombatRevision { get; }
    public CombatStateData(int hp, int maxHp, long revision) { Hp = hp; MaxHp = maxHp; CombatRevision = revision; }
}
public sealed class CombatStateStore
{
    private readonly System.Collections.Generic.Dictionary<string, CombatStateData> states =
        new System.Collections.Generic.Dictionary<string, CombatStateData>();
    public event System.Action<CombatEntityKey, CombatStateData> StateChanged;
    public bool TryGetState(string type, string id, out CombatStateData state) =>
        states.TryGetValue(type + ":" + id, out state);
    public void Set(string type, string id, int hp, long revision, int maxHp = 1090)
    {
        var state = new CombatStateData(hp, maxHp, revision);
        states[type + ":" + id] = state;
        StateChanged?.Invoke(new CombatEntityKey(type, id), state);
    }
}
'@

$source = ($display + "`n" + $presenter) -replace
    '(?m)^using [^;]+;\r?\n', ''
$imports = "using System;`nusing System.Globalization;`n" +
    "using Game.Cultivation.Presentation;`nusing Game.Cultivation.Protocol;`n" +
    "using Game.Cultivation.Runtime;`nusing TMPro;`n" +
    "using UnityEngine;`nusing UnityEngine.UI;`n"
Add-Type -TypeDefinition ($imports + $stubs + "`n" + $source) -IgnoreWarnings | Out-Null

function Invoke-Private($target, [string]$name) {
    $method = $target.GetType().GetMethod($name,
        [System.Reflection.BindingFlags]'Instance,NonPublic')
    $method.Invoke($target, @()) | Out-Null
}
function Set-Private($target, [string]$name, $value) {
    $field = $target.GetType().GetField($name,
        [System.Reflection.BindingFlags]'Instance,NonPublic')
    $field.SetValue($target, $value)
}
function Assert-Text($actual, [string]$expected, [string]$message) {
    if ($actual.text -ne $expected) { throw "$message (actual: $($actual.text))" }
}
function Assert-Fill($actual, [double]$expected, [string]$message) {
    if ([math]::Abs($actual.fillAmount - $expected) -gt 0.0001) {
        throw "$message (actual: $($actual.fillAmount), expected: $expected)"
    }
}

$session = [GameSession]::new()
$network = [NetworkManager]::new()
$store = [CombatStateStore]::new()
$presenterInstance = [Game.CharacterPanel.Presentation.CharacterPanelPresenter]::new()
$name = [TMPro.TMP_Text]::new()
$health = [TMPro.TMP_Text]::new()
$mana = [TMPro.TMP_Text]::new()
$attack = [TMPro.TMP_Text]::new()
$defense = [TMPro.TMP_Text]::new()
$magicAttack = [TMPro.TMP_Text]::new()
$magicDefense = [TMPro.TMP_Text]::new()
$critRate = [TMPro.TMP_Text]::new()
$critDamage = [TMPro.TMP_Text]::new()
$realm = [TMPro.TMP_Text]::new()
$alchemyTitle = [TMPro.TMP_Text]::new()
$forgingTitle = [TMPro.TMP_Text]::new()
$comprehension = [TMPro.TMP_Text]::new()
$attributesRoot = [UnityEngine.Transform]::new()
$presenterInstance.transform.Children.Add('属性面板', $attributesRoot)
foreach ($item in @(
    @($presenterInstance.transform, '丹道头衔', $alchemyTitle),
    @($presenterInstance.transform, '器道头衔', $forgingTitle),
    @($attributesRoot, '悟性', $comprehension)
)) {
    $child = [UnityEngine.Transform]::new()
    $child.Component = $item[2]
    $item[0].Children.Add($item[1], $child)
}
$runtime = [Game.Cultivation.Runtime.CultivationSessionRuntime]::new()
[Game.Cultivation.Runtime.CultivationSessionRuntime]::Instance = $runtime
$cultivation = [TMPro.TMP_Text]::new()
$requiredCultivation = [TMPro.TMP_Text]::new()
$healthFill = [UnityEngine.UI.Image]::new()
$manaFill = [UnityEngine.UI.Image]::new()
$cultivationFill = [UnityEngine.UI.Image]::new()
$cultivationSlider = [UnityEngine.UI.Slider]::new()
Set-Private $presenterInstance 'session' $session
Set-Private $presenterInstance 'network' $network
Set-Private $presenterInstance 'stateStore' $store
Set-Private $presenterInstance 'nickname' $name
Set-Private $presenterInstance 'health' $health
Set-Private $presenterInstance 'mana' $mana
Set-Private $presenterInstance 'healthFill' $healthFill
Set-Private $presenterInstance 'manaFill' $manaFill
Set-Private $presenterInstance 'attack' $attack
Set-Private $presenterInstance 'defense' $defense
Set-Private $presenterInstance 'magicAttack' $magicAttack
Set-Private $presenterInstance 'magicDefense' $magicDefense
Set-Private $presenterInstance 'critRate' $critRate
Set-Private $presenterInstance 'critDamage' $critDamage
Set-Private $presenterInstance 'realm' $realm
Set-Private $presenterInstance 'currentExperience' $cultivation
Set-Private $presenterInstance 'requiredExperience' $requiredCultivation
Set-Private $presenterInstance 'experienceFill' $cultivationFill
Set-Private $presenterInstance 'experienceSlider' $cultivationSlider

Invoke-Private $presenterInstance 'OnEnable'
Assert-Text $name 'tester' 'Nickname should remain bound.'
Assert-Text $health '75/1090' 'Opening must use both enter_world health values.'
Assert-Text $mana '--' 'Mana must wait for a progression snapshot.'
Assert-Fill $healthFill (75.0 / 1090) 'HP fill must use the complete entry pair.'
Assert-Fill $manaFill 0 'MP fill must wait for a progression snapshot.'
Assert-Text $attack '--' 'Attributes must wait for a successful server snapshot.'
Assert-Text $realm '--' 'Realm must wait for a successful progression snapshot.'
Assert-Text $alchemyTitle '--' 'Alchemy title must wait for a cultivation snapshot.'
Assert-Text $forgingTitle '--' 'Forging title must wait for a cultivation snapshot.'
Assert-Text $comprehension '--' 'Comprehension must wait for a cultivation snapshot.'
if ($runtime.SnapshotRequests -ne 1) { throw 'The panel must request missing cultivation data.' }
$professionSnapshot = [Game.Cultivation.Protocol.CultivationSnapshotDto]::new()
$professionSnapshot.characterDbId = 42
$professionSnapshot.alchemyProfession = [Game.Cultivation.Protocol.CultivationProfessionProgressDto]::new()
$professionSnapshot.alchemyProfession.rank = -1
$professionSnapshot.forgingProfession = [Game.Cultivation.Protocol.CultivationProfessionProgressDto]::new()
$runtime.Apply($professionSnapshot)
Assert-Text $alchemyTitle '未入门' 'Rank -1 must display as uninitiated.'
Assert-Text $forgingTitle '炼器学徒' 'Rank zero must display as an apprentice.'
Assert-Text $comprehension '0' 'An authoritative zero must display as a number.'
$professionSnapshot.alchemyProfession.rank = 1
$professionSnapshot.totalComprehension = 27
$runtime.Apply($professionSnapshot)
Assert-Text $alchemyTitle '一品炼丹师' 'The alchemy rank must drive the alchemy title.'
Assert-Text $forgingTitle '炼器学徒' 'Alchemy promotion must not change the forging title.'
Assert-Text $comprehension '27' 'The total comprehension must display as a plain integer.'
if ($session.SnapshotRequestCount -ne 1) { throw 'Missing attributes should request a snapshot on open.' }
if ($session.ProgressionRequestCount -ne 1) { throw 'Missing progression should request a snapshot on open.' }
$progression = [ProgressionSnapshotResult]::new()
$progression.success = $true
$progression.characterDbId = 99
$progression.currentMp = 35
$progression.maxMp = 50
$progression.jingjieExp = 40
$progression.requiredJingjieExp = 100
$progression.totalJingjieExp = 60
$progression.hasNextJingjie = $true
$progression.jingjieName = '武夫一重'
$session.Apply($progression)
Assert-Text $realm '--' 'Another character cannot update the local realm.'
Assert-Fill $manaFill 0 'Another character cannot update local MP fill.'
$progression.characterDbId = 42
$progression.success = $false
$session.Apply($progression)
Assert-Text $realm '--' 'A failed progression result cannot display a realm.'
Assert-Fill $manaFill 0 'A failed progression result cannot update MP fill.'
$progression.success = $true
$session.Apply($progression)
Assert-Text $realm '武夫一重' 'Realm must use the authoritative jingjieName.'
Assert-Text $mana '35/50' 'Current and maximum mana must come from progression.'
Assert-Fill $manaFill 0.7 'MP fill must match current and maximum mana.'
Assert-Text $cultivation '40' 'Character panel must show usable cultivation.'
Assert-Text $requiredCultivation '100' 'Character panel requirement must be next-breakthrough cultivation.'
if ([math]::Abs($cultivationFill.fillAmount - 0.4) -gt 0.0001 -or
    [math]::Abs($cultivationSlider.value - 0.4) -gt 0.0001) {
    throw 'Character panel cultivation fill and slider must match the cultivation panel.'
}
$progression.currentMp = 10
$session.Apply($progression)
Assert-Fill $manaFill 0.2 'MP fill must shrink with current mana.'
$progression.currentMp = 50
$session.Apply($progression)
Assert-Fill $manaFill 1 'MP fill must grow with current mana.'
$progression.currentMp = 0
$progression.maxMp = 0
$session.Apply($progression)
Assert-Fill $manaFill 0 'Zero maximum mana must clear MP fill.'
$progression.currentMp = 35
$progression.maxMp = 50
$session.Apply($progression)
Assert-Text $cultivation '40' 'Mana updates must preserve character cultivation.'
if ([math]::Abs($cultivationFill.fillAmount - 0.4) -gt 0.0001) {
    throw 'Mana updates must preserve the cultivation fill.'
}
$progression.hasNextJingjie = $false
$session.Apply($progression)
Assert-Text $requiredCultivation '--' 'No path must hide character panel cultivation requirement.'
if ($cultivationFill.fillAmount -ne 0) {
    throw 'No path must clear character panel cultivation fill.'
}
$progression.hasNextJingjie = $true
$progression.jingjieExp = 20
$session.Apply($progression)
Assert-Fill $cultivationFill 0.2 'Cultivation fill must respond to the new progress.'
$progression.jingjieExp = 40
$progression.hasNextJingjie = $false
$session.Apply($progression)
Set-Private $presenterInstance 'requiredExperience' $null
Invoke-Private $presenterInstance 'Refresh'
Assert-Text $cultivation '40/--' 'The single text widget must match cultivation panel no-path formatting.'
Set-Private $presenterInstance 'requiredExperience' $cultivation
Invoke-Private $presenterInstance 'Refresh'
Assert-Text $cultivation '40/--' 'A shared text reference must still display the full cultivation progress.'
Set-Private $presenterInstance 'requiredExperience' $requiredCultivation
$progression.hasNextJingjie = $true
$session.Apply($progression)
$progression.jingjieName = ''
$session.Apply($progression)
Assert-Text $realm '--' 'A missing realm name cannot be synthesized locally.'
$progression.jingjieName = '武夫一重'
$session.Apply($progression)
$values = [AttributeSnapshotFinal]::new()
$values.maxHp = 1090; $values.attack = 110; $values.defense = 18
$values.critRateBps = 500; $values.critDamageBps = 15000
$values.magicAttack = 75; $values.magicDefense = 32
$result = [AttributeSnapshotResult]::new()
$result.success = $true; $result.characterDbId = 99; $result.final = $values
$session.Apply($result)
Assert-Text $attack '--' 'Another character must not update local attributes.'
Assert-Fill $healthFill (75.0 / 1090) 'Another character cannot update local HP fill.'
$result.characterDbId = 42; $result.success = $false
$session.Apply($result)
Assert-Text $attack '--' 'Failed responses must not update attributes.'
Assert-Fill $healthFill (75.0 / 1090) 'A failed attribute result cannot update HP fill.'
$result.success = $true
$session.Apply($result)
Assert-Text $attack '110' 'Attack must come from final.'
Assert-Text $defense '18' 'Defense must come from final.'
Assert-Text $magicAttack '75' 'Magic attack must come from final.'
Assert-Text $magicDefense '32' 'Magic defense must come from final.'
Assert-Text $critRate '5%' 'Critical rate must convert basis points.'
Assert-Text $critDamage '150%' 'Critical damage must convert basis points.'
Assert-Text $health '75/1090' 'An attribute snapshot must not replace the entry health pair.'
Assert-Fill $healthFill (75.0 / 1090) 'HP fill must use the complete entry health pair.'
$store.Set('player', '99', 5, 11)
$store.Set('monster', '42', 5, 11)
$store.Set('player', '42', 5, 9)
Assert-Text $health '75/1090' 'Other actors and stale revisions must be ignored.'
Assert-Fill $healthFill (75.0 / 1090) 'Other actors and stale revisions must not change HP fill.'
$store.Set('player', '42', 60, 11)
Assert-Text $health '60/1090' 'Current local combat state must update health.'
Assert-Fill $healthFill (60.0 / 1090) 'Damage must shrink HP fill.'
$store.Set('player', '42', 90, 12)
Assert-Fill $healthFill (90.0 / 1090) 'Healing must grow HP fill.'
$values.maxHp = 327
$session.Apply($result)
$store.Set('player', '42', 1023, 13, 1075)
Assert-Text $health '1023/1075' 'A new combat pair cannot use the old attribute cap of 327.'
Assert-Fill $healthFill (1023.0 / 1075) 'HP fill must track the same pair as the text.'
$values.maxHp = 1375
$session.Apply($result)
Assert-Text $health '1023/1075' 'An attribute push cannot overwrite either member of the HP pair.'
$store.Set('player', '42', 1023, 14, 1375)
Assert-Text $health '1023/1375' 'Increasing the health cap does not heal the player.'
$store.Set('player', '42', 0, 15, 1375)
Assert-Text $health '0/1375' 'Death must retain the current maximum HP.'
$store.Set('player', '42', 1375, 16, 1375)
Assert-Text $health '1375/1375' 'Respawn must apply health as one pair.'
Invoke-Private $presenterInstance 'OnDisable'
$professionSnapshot.forgingProfession.rank = 9
$professionSnapshot.totalComprehension = 12345
$runtime.Apply($professionSnapshot)
Assert-Text $forgingTitle '炼器学徒' 'The closed panel must unsubscribe from cultivation updates.'
$store.Set('player', '42', 50, 17)
Assert-Text $health '1375/1375' 'Closed panel must unsubscribe from live updates.'
Assert-Fill $healthFill 1 'Closed panel must retain its last HP fill.'
Invoke-Private $presenterInstance 'OnEnable'
Assert-Text $alchemyTitle '一品炼丹师' 'Reopening must use the cached alchemy title.'
Assert-Text $forgingTitle '九品炼器师' 'Reopening must use the cached forging title.'
Assert-Text $comprehension '12345' 'Reopening must use the cached comprehension.'
if ($runtime.SnapshotRequests -ne 1) { throw 'Cached cultivation should not request again on open.' }
Assert-Text $health '50/1090' 'Reopening must read the latest cached state.'
Assert-Fill $healthFill (50.0 / 1090) 'Reopening must refresh HP fill.'
Assert-Fill $manaFill 0.7 'Reopening must refresh MP fill.'
Assert-Fill $cultivationFill 0.4 'Reopening must refresh cultivation fill.'
Assert-Text $attack '110' 'Reopening must use cached final attributes.'
Assert-Text $mana '35/50' 'Reopening must use cached progression mana.'
Assert-Text $realm '武夫一重' 'Reopening must use the cached local realm.'
if ($session.SnapshotRequestCount -ne 1) { throw 'Cached attributes should not request again on open.' }
if ($session.ProgressionRequestCount -ne 1) { throw 'Cached progression should not request again on open.' }
$network.Disconnect()
Assert-Text $alchemyTitle '--' 'Disconnect must clear the alchemy title.'
Assert-Text $forgingTitle '--' 'Disconnect must clear the forging title.'
Assert-Text $comprehension '--' 'Disconnect must clear comprehension.'
Assert-Text $health '--' 'Disconnect must clear displayed health.'
Assert-Text $mana '--' 'Disconnect must clear displayed mana.'
Assert-Text $magicAttack '--' 'Disconnect must clear displayed magic attack.'
Assert-Text $magicDefense '--' 'Disconnect must clear displayed magic defense.'
Assert-Text $attack '--' 'Disconnect must clear displayed attributes.'
Assert-Text $realm '--' 'Disconnect must clear displayed realm.'
Assert-Text $cultivation '--' 'Disconnect must clear character cultivation.'
Assert-Fill $healthFill 0 'Disconnect must clear HP fill.'
Assert-Fill $manaFill 0 'Disconnect must clear MP fill.'
if ($cultivationFill.fillAmount -ne 0 -or $cultivationSlider.value -ne 0) {
    throw 'Disconnect must clear character cultivation progress.'
}
$session.LocalEnterWorldHp = -1
$session.LocalAttributeSnapshot = $null
$session.LocalProgressionSnapshot = $null
$session.LocalCharacterDbId = 52
Invoke-Private $presenterInstance 'OnDisable'
Invoke-Private $presenterInstance 'OnEnable'
Assert-Text $health '--' 'Reopening while disconnected must not show old state.'
Assert-Text $realm '--' 'Reopening while disconnected must not show old realm.'
Assert-Fill $healthFill 0 'Reopening while disconnected must keep HP fill empty.'
Assert-Fill $manaFill 0 'Reopening while disconnected must keep MP fill empty.'
Assert-Text $alchemyTitle '--' 'The previous character title must not appear for the new character.'
Assert-Text $forgingTitle '--' 'The previous forging title must not appear for the new character.'
Assert-Text $comprehension '--' 'The previous comprehension must not appear for the new character.'
$newSnapshot = [Game.Cultivation.Protocol.CultivationSnapshotDto]::new()
$newSnapshot.characterDbId = 52
$newSnapshot.totalComprehension = 2
$runtime.Apply($newSnapshot)
Assert-Text $alchemyTitle '--' 'An absent profession must stay unknown.'
Assert-Text $comprehension '2' 'The new character must show its own comprehension.'
$runtime.Apply($null)
Assert-Text $comprehension '--' 'Clearing the cultivation session must clear the panel immediately.'
Invoke-Private $presenterInstance 'OnDisable'

$bindingsPresent = $scene -match '(?s)m_Script: \{fileID: 11500000, guid: c58c60ffbd7741429aeec66f2da79d4d, type: 3\}.*?stateStore: \{fileID: 1973135054\}.*?health: \{fileID: 476124679\}'
$entryHpPresent = $sessionSource -match 'LocalEnterWorldMaxHp = selfState\?\.maxHp' -and
    $sessionSource -match 'Mathf\.Clamp\(selfState\.hp, 0, LocalEnterWorldMaxHp\)'
$entryRevisionPresent = $sessionSource -match 'LocalEnterWorldCombatRevision = selfState'
if (-not ($bindingsPresent -and $entryHpPresent -and $entryRevisionPresent)) {
    throw 'Scene and enter_world self bindings must remain connected.'
}

$worldScene = (Get-Content (Join-Path $root 'Assets/Scenes/WorldShell.unity') -Raw) -replace "`r`n", "`n"
$worldPresenter = [regex]::Match($worldScene,
    '(?ms)^--- !u!114 &\d+\r?\n(?:(?!^--- !u!).)*?m_Script: \{fileID: 11500000, guid: c58c60ffbd7741429aeec66f2da79d4d, type: 3\}(?:(?!^--- !u!).)*').Value
if (-not $worldPresenter) { throw 'WorldShell character presenter is missing.' }
if ($worldPresenter -match '(?m)^  (totalExperience|attributePoints):' -or
    $worldScene -match '(?:&|fileID: )200034200[0-7](?!\d)') {
    throw 'WorldShell must not retain the removed total cultivation and attribute point rows.'
}
foreach ($binding in @(
    @('healthFill', '356852590'),
    @('manaFill', '1404490644'),
    @('experienceFill', '722376669')
)) {
    $field = $binding[0]
    $imageId = $binding[1]
    if ($worldPresenter -notmatch "(?m)^  ${field}: \{fileID: ${imageId}\}$") {
        throw "WorldShell character presenter has no $field binding."
    }
    $image = [regex]::Match($worldScene,
        "(?ms)^--- !u!114 &${imageId}\r?`n(?:(?!^--- !u!).)*").Value
    if ($image -notmatch '(?m)^  m_Type: 3$' -or
        $image -notmatch '(?m)^  m_FillMethod: 0$' -or
        $image -notmatch '(?m)^  m_FillOrigin: 0$') {
        throw "WorldShell $field must fill horizontally from left to right."
    }
}

Write-Output 'PASS: character panel HP, MP, cultivation, profession titles, scene bindings and lifecycle.'
