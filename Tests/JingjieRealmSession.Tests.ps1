$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$sessionSource = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/GameSession.cs') -Raw
$messages = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/NetWork/NetMessages.cs') -Raw
$breakthrough = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/CharacterPanel/Protocol/JingjieBreakthroughMessages.cs') -Raw

$dto = [regex]::Match($messages,
    '(?s)\[Serializable\]\s+public sealed class AttributeSnapshotRequest.*?(?=\[Serializable\]\s+public class PlayerNetworkState)')
$fields = [regex]::Match($sessionSource,
    '(?s)    private long nextAttributeSnapshotSequence;.*?(?=    private long nextAttackModeRequestSequence;)')
$handlers = [regex]::Match($sessionSource,
    '(?s)    private void FlushSnapshotRefreshes\(\).*?(?=    private void HandleBattleSyncReady\()')
if (-not ($dto.Success -and $fields.Success -and $handlers.Success)) {
    throw 'Could not find the production snapshot code.'
}
$breakthrough = $breakthrough -replace '(?m)^using [^\r\n]+;\r?\n', ''
$stubs = @'
using System;
using System.Collections.Generic;
using System.Text.Json;
using Game.CharacterPanel.Protocol;
using Game.Cultivation.Runtime;
using Game.Inventory.Protocol.V2;
using UnityEngine;
public class NetMessageBase { public string msgId; public string type; }
namespace Game.Inventory.Protocol.V2 {
    public class InventoryV2StackChangeDto { public long stackId; public int newCount; }
}
namespace Game.Cultivation.Runtime {
    public class RefiningSessionRuntime {
        public static RefiningSessionRuntime Instance = new();
        public void RequestSnapshot() {}
    }
}
namespace UnityEngine {
    public static class Time { public static float unscaledTime; }
    public static class Debug {
        public static void Log(string text) {}
        public static void LogWarning(string text) {}
    }
    public static class JsonUtility {
        private static readonly JsonSerializerOptions Options = new() { IncludeFields = true };
        public static string ToJson(object value) => JsonSerializer.Serialize(value, value.GetType(), Options);
        public static T FromJson<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);
    }
}
public sealed class NetworkManager {
    public bool IsConnected = true;
    public List<NetMessageBase> Sent { get; } = new();
    public bool Send(NetMessageBase request) { Sent.Add(request); return true; }
}
public partial class GameSession {
    public bool IsWorldReady = true;
    private long localCharacterDbId = 42;
    private bool sessionStarted = true, gameSessionAccepted = true, returningToLogin;
    private NetworkManager networkManager = new();
    public List<NetMessageBase> Sent => networkManager.Sent;
    public long PendingAttribute => pendingAttributeSnapshotSequence;
    public long PendingProgression => pendingProgressionSnapshotSequence;
    public AttributeSnapshotResult LocalAttributeSnapshot { get; private set; }
    public ProgressionSnapshotResult LocalProgressionSnapshot { get; private set; }
    public event Action<AttributeSnapshotResult> LocalAttributeSnapshotChanged;
    public event Action<ProgressionSnapshotResult> LocalProgressionSnapshotChanged;
    public void ReceiveProgression(string json) => HandleProgressionSnapshotResult(json);
    public void ReceiveAttribute(string json) => HandleAttributeSnapshotResult(json);
    public void Flush() => FlushSnapshotRefreshes();
    public void Reset() { ResetAttributeSnapshot(); ResetProgressionSnapshot(); }
}
'@
$source = $stubs + "`n" + $dto.Value + "`n" + $breakthrough + "`npublic partial class GameSession {`n" +
    $fields.Value + $handlers.Value + "`n}"
Add-Type -TypeDefinition $source -IgnoreWarnings | Out-Null

function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
}
function Snapshot([long]$sequence, [int]$stage, [int]$maxMp) {
    $result = [ProgressionSnapshotResult]::new()
    $result.success = $true
    $result.requestSequence = $sequence
    $result.characterDbId = 42
    $result.jingjieId = 'qi'
    $result.jingjieStage = $stage
    $result.jingjieName = "炼气 $stage 层"
    $result.hasNextJingjie = $true
    $result.nextJingjieId = 'qi'
    $result.nextJingjieStage = $stage + 1
    $result.nextJingjieName = "炼气 $($stage + 1) 层"
    $result.currentMp = 160
    $result.maxMp = $maxMp
    $result.jingjieExp = 1000 - $stage * 40
    $result.requiredJingjieExp = 100 + $stage * 20
    $result.totalJingjieExp = 1000
    return $result
}
function Breakthrough([int]$stage, [long]$time) {
    $result = [Game.CharacterPanel.Protocol.JingjieBreakthroughResultDto]::new()
    $snapshot = Snapshot 0 $stage 200
    foreach ($field in @('success','characterDbId','jingjieId','jingjieStage','jingjieName',
        'hasNextJingjie','nextJingjieId','nextJingjieStage','nextJingjieName','jingjieExp',
        'requiredJingjieExp','totalJingjieExp')) {
        $result.$field = $snapshot.$field
    }
    $result.serverTime = $time
    return $result
}
function Attributes([long]$seq, [long]$rev, [long]$combatRev, [long]$time, [int]$attack, [long]$role = 42) {
    $final = [AttributeSnapshotFinal]::new()
    $final.maxHp = 1075 + $attack
    $final.attack = $attack
    $final.defense = $attack + 1
    $final.magicAttack = $attack + 2
    $final.magicDefense = $attack + 3
    $final.critRateBps = 100 + $attack
    $final.critDamageBps = 10000 + $attack
    $payload = [AttributeSnapshotResult]::new()
    $payload.success = $true
    $payload.requestSequence = $seq
    $payload.characterDbId = $role
    $payload.attributeRevision = $rev
    $payload.combatRevision = $combatRev
    $payload.serverTime = $time
    $payload.final = $final
    return [UnityEngine.JsonUtility]::ToJson($payload)
}

$owner = [GameSession]::new()
$owner.IsWorldReady = $false
$owner.ReceiveAttribute((Attributes 0 8 9 100 999))
Assert-True ($null -eq $owner.LocalAttributeSnapshot) 'Pushes outside world entry must be ignored.'
$owner.IsWorldReady = $true
$owner.RequestAttributeSnapshot()
$owner.ReceiveAttribute((Attributes 0 2 3 100 110))
Assert-True ($owner.LocalAttributeSnapshot.final.attack -eq 110 -and $owner.PendingAttribute -eq 1) 'Automatic snapshot cannot consume an explicit query.'
$owner.ReceiveAttribute((Attributes 1 1 2 99 10))
$owner.ReceiveAttribute((Attributes 0 2 2 99 20))
$owner.ReceiveAttribute((Attributes 0 8 9 100 999 99))
Assert-True ($owner.LocalAttributeSnapshot.final.attack -eq 110 -and $owner.PendingAttribute -eq 0) 'An older response or a different role cannot overwrite the local snapshot.'

$owner.RequestProgressionSnapshot()
$owner.ReceiveProgression([UnityEngine.JsonUtility]::ToJson((Snapshot 1 1 200)))
[UnityEngine.Time]::unscaledTime = 1.1
$owner.RequestProgressionSnapshot()
$owner.ReconcileJingjieBreakthrough((Breakthrough 2 1000))
$owner.Flush()
Assert-True ($owner.PendingProgression -eq 2 -and $owner.LocalProgressionSnapshot.jingjieStage -eq 2) 'Keep one request in flight while applying successful cultivation immediately.'
$owner.ReceiveProgression([UnityEngine.JsonUtility]::ToJson((Snapshot 2 1 200)))
$owner.Flush()
Assert-True ($owner.PendingProgression -eq 3 -and $owner.LocalProgressionSnapshot.jingjieStage -eq 2) 'Ignore superseded growth reply and refresh without the 1s interval.'
$owner.ReceiveAttribute((Attributes 0 3 4 1001 120))
$owner.ReceiveProgression([UnityEngine.JsonUtility]::ToJson((Snapshot 3 2 250)))
Assert-True ($owner.LocalProgressionSnapshot.maxMp -eq 250) 'First breakthrough updates the mana cap.'

for ($stage = 3; $stage -le 6; $stage++) {
    [UnityEngine.Time]::unscaledTime += 0.02
    $owner.ReconcileJingjieBreakthrough((Breakthrough $stage (1000 + $stage)))
    $owner.ReceiveAttribute((Attributes 0 ($stage + 1) ($stage + 2) (1000 + $stage) (100 + $stage * 10)))
    $owner.Flush()
}
$owner.ReceiveProgression([UnityEngine.JsonUtility]::ToJson((Snapshot 4 3 300)))
$owner.Flush()
Assert-True ($owner.PendingProgression -eq 5) 'Rapid breakthroughs coalesce into the latest growth query.'
$owner.ReceiveProgression([UnityEngine.JsonUtility]::ToJson((Snapshot 5 6 450)))
$growth = $owner.LocalProgressionSnapshot
$final = $owner.LocalAttributeSnapshot.final
Assert-True ($growth.jingjieStage -eq 6 -and $growth.nextJingjieStage -eq 7 -and
    $growth.jingjieExp -eq 760 -and $growth.requiredJingjieExp -eq 220 -and $growth.totalJingjieExp -eq 1000 -and
    $growth.currentMp -eq 160 -and $growth.maxMp -eq 450 -and
    $final.maxHp -eq 1235 -and $final.attack -eq 160 -and $final.defense -eq 161 -and
    $final.magicAttack -eq 162 -and $final.magicDefense -eq 163 -and
    $final.critRateBps -eq 260 -and $final.critDamageBps -eq 10160) 'Five consecutive breakthroughs must show the latest full character data.'

$replay = Breakthrough 3 1003
$replay.idempotentReplay = $true
$owner.ReconcileJingjieBreakthrough($replay)
$owner.Flush()
Assert-True ($owner.LocalProgressionSnapshot.jingjieStage -eq 6) 'A duplicate never reapplies an old cultivation result.'
$null = $owner.ApplyItemUseMana(220, 450)
$owner.ReceiveProgression([UnityEngine.JsonUtility]::ToJson((Snapshot 6 6 450)))
Assert-True ($owner.LocalProgressionSnapshot.currentMp -eq 220) 'A growth reply cannot restore stale mana after a potion, skill, or meditation update.'

$owner.ReconcileJingjieBreakthrough((Breakthrough 7 2000))
$owner.Flush()
$owner.ReceiveProgression([UnityEngine.JsonUtility]::ToJson((Snapshot 7 7 450)))
$owner.ReceiveAttribute((Attributes 0 8 10 1999 170))
[UnityEngine.Time]::unscaledTime += 0.51
$owner.Flush()
Assert-True ($owner.Sent[$owner.Sent.Count - 2].msgId -eq 'attribute_snapshot' -and
    $owner.Sent[$owner.Sent.Count - 1].msgId -eq 'progression_snapshot') 'A missing breakthrough push triggers one repair query before a growth refresh.'
$repaired = Snapshot 8 7 500
$repaired.hasNextJingjie = $false
$repaired.nextJingjieId = ''
$repaired.nextJingjieStage = 0
$repaired.nextJingjieName = ''
$owner.ReceiveAttribute((Attributes 2 9 11 2100 180))
$owner.ReceiveProgression([UnityEngine.JsonUtility]::ToJson($repaired))
Assert-True ($owner.LocalAttributeSnapshot.final.attack -eq 180 -and
    $owner.LocalProgressionSnapshot.maxMp -eq 500 -and
    -not $owner.LocalProgressionSnapshot.hasNextJingjie) 'Explicit repair updates both snapshots and preserves the no-next-realm state.'
$failed = Breakthrough 10 2200
$failed.success = $false
$owner.ReconcileJingjieBreakthrough($failed)
Assert-True ($owner.LocalProgressionSnapshot.jingjieStage -eq 7) 'A failed request cannot advance realm or overwrite existing cultivation.'
$owner.Reset()
Assert-True ($null -eq $owner.LocalAttributeSnapshot -and $null -eq $owner.LocalProgressionSnapshot -and
    $owner.PendingAttribute -eq 0 -and $owner.PendingProgression -eq 0) 'A new session clears caches and pending requests.'
Write-Output 'PASS: breakthrough attributes, consecutive realm growth, mana ordering and repair.'
