$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$sessionSource = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/GameSession.cs') -Raw
$snapshotSource = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/NetWork/NetMessages.cs') -Raw
$breakthroughSource = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/CharacterPanel/Protocol/JingjieBreakthroughMessages.cs') -Raw

$snapshot = [regex]::Match($snapshotSource,
    '(?s)\[Serializable\]\s+public sealed class ProgressionSnapshotResult.*?(?=\[Serializable\]\s+public sealed class SkillSnapshotResult)')
$reconcile = [regex]::Match($sessionSource,
    '(?s)    public void ReconcileJingjieBreakthrough\(JingjieBreakthroughResultDto result\).*?(?=\r?\n    public bool ApplyItemUseMana\()')
$receive = [regex]::Match($sessionSource,
    '(?s)    private void HandleProgressionSnapshotResult\(string json\).*?(?=\r?\n    private void HandleSkillSnapshotResult\()')
if (-not $snapshot.Success -or -not $reconcile.Success -or -not $receive.Success) {
    throw 'Cannot find the production realm DTO and session handlers.'
}
$breakthroughSource = $breakthroughSource -replace '(?m)^using [^\r\n]+;\r?\n', ''
$stubs = @'
using System;
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
        public int SnapshotRequests;
        public void RequestSnapshot() { SnapshotRequests++; }
    }
}
namespace UnityEngine {
    public static class Debug {
        public static void Log(string text) {}
        public static void LogWarning(string text) {}
    }
    public static class JsonUtility {
        private static readonly JsonSerializerOptions Options =
            new JsonSerializerOptions { IncludeFields = true };
        public static string ToJson(object value) =>
            JsonSerializer.Serialize(value, value.GetType(), Options);
        public static T FromJson<T>(string json) =>
            JsonSerializer.Deserialize<T>(json, Options);
    }
}
public partial class GameSession {
    public bool IsWorldReady = true;
    private long localCharacterDbId = 42;
    public long LocalCharacterDbId => localCharacterDbId;
    public ProgressionSnapshotResult LocalProgressionSnapshot { get; private set; }
    public int SnapshotRequests;
    public event Action<ProgressionSnapshotResult> LocalProgressionSnapshotChanged;
    private bool sessionStarted = true, gameSessionAccepted = true, returningToLogin;
    private long pendingProgressionSnapshotSequence;
    private bool progressionRefreshQueued;
    public void Seed(ProgressionSnapshotResult snapshot) { LocalProgressionSnapshot = snapshot; }
    public void Expect(long sequence) { pendingProgressionSnapshotSequence = sequence; }
    public void Receive(string json) { HandleProgressionSnapshotResult(json); }
    public void RequestProgressionSnapshot() {
        SnapshotRequests++;
        pendingProgressionSnapshotSequence = 100 + SnapshotRequests;
        progressionRefreshQueued = false;
    }
}
'@
$source = $stubs + "`n" + $snapshot.Value + "`n" +
    $breakthroughSource + "`npublic partial class GameSession {`n" +
    $reconcile.Value + "`n" +
    $receive.Value + "`n}"
Add-Type -TypeDefinition $source -IgnoreWarnings | Out-Null

function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
}
function Snapshot([long]$sequence, [long]$character, [string]$name,
    [bool]$hasNext, [string]$nextId, [int]$stage, [string]$nextName) {
    $result = [ProgressionSnapshotResult]::new()
    $result.success = $true
    $result.requestSequence = $sequence
    $result.characterDbId = $character
    $result.jingjieId = 'qi_refining'
    $result.jingjieStage = 1
    $result.jingjieName = $name
    $result.hasNextJingjie = $hasNext
    $result.nextJingjieId = $nextId
    $result.nextJingjieStage = $stage
    $result.nextJingjieName = $nextName
    $result.requiredJingjieExp = 100
    return $result
}

$owner = [GameSession]::new()
$changes = 0
$owner.add_LocalProgressionSnapshotChanged([Action[ProgressionSnapshotResult]]{
    param($value) $script:changes++
})
$first = Snapshot 1 42 '炼气一层' $true 'qi_refining' 2 '炼气二层'
$owner.Expect(1)
$owner.Receive([UnityEngine.JsonUtility]::ToJson($first))
Assert-True ($owner.LocalProgressionSnapshot.nextJingjieName -eq '炼气二层' -and
    $owner.LocalProgressionSnapshot.hasNextJingjie -and $changes -eq 1) `
    'A successful snapshot must publish current and next realm together.'

$other = Snapshot 2 99 '其他角色' $true 'other' 3 '其他三层'
$owner.Expect(2)
$owner.Receive([UnityEngine.JsonUtility]::ToJson($other))
Assert-True ($owner.LocalProgressionSnapshot.nextJingjieName -eq '炼气二层' -and
    $changes -eq 1) 'Another character cannot replace the current snapshot.'
$owner.Expect(3)
$invalid = Snapshot 3 42 '无效数据' $false '' 3 ''
$owner.Receive([UnityEngine.JsonUtility]::ToJson($invalid))
Assert-True ($owner.LocalProgressionSnapshot.nextJingjieName -eq '炼气二层' -and
    $changes -eq 1) 'Malformed no-path fields cannot overwrite the cached realm.'
$owner.Expect(4)
$failure = Snapshot 4 42 '' $false '' 0 ''
$failure.success = $false
$owner.Receive([UnityEngine.JsonUtility]::ToJson($failure))
Assert-True ($owner.LocalProgressionSnapshot.nextJingjieName -eq '炼气二层' -and
    $changes -eq 1) 'A failed snapshot without valid realm fields preserves the display.'

$after = [Game.CharacterPanel.Protocol.JingjieBreakthroughResultDto]::new()
$after.success = $true
$after.characterDbId = 42
$after.jingjieId = 'qi_refining'
$after.jingjieStage = 2
$after.jingjieName = '炼气二层'
$after.hasNextJingjie = $true
$after.nextJingjieId = 'qi_refining'
$after.nextJingjieStage = 3
$after.nextJingjieName = '炼气三层'
$after.requiredJingjieExp = 200
$owner.Expect(5)
$owner.ReconcileJingjieBreakthrough($after)
Assert-True ($owner.LocalProgressionSnapshot.jingjieName -eq '炼气二层' -and
    $owner.LocalProgressionSnapshot.nextJingjieName -eq '炼气三层' -and
    $owner.LocalProgressionSnapshot.requiredJingjieExp -eq 200 -and
    $changes -eq 2 -and $owner.SnapshotRequests -eq 1) `
    'A fresh breakthrough must publish both realms and request a new snapshot.'
$stale = Snapshot 5 42 '炼气一层' $true 'qi_refining' 2 '炼气二层'
$owner.Receive([UnityEngine.JsonUtility]::ToJson($stale))
Assert-True ($owner.LocalProgressionSnapshot.nextJingjieName -eq '炼气三层' -and
    $changes -eq 2) 'An older in-flight snapshot cannot undo breakthrough state.'

$replay = [Game.CharacterPanel.Protocol.JingjieBreakthroughResultDto]::new()
$replay.success = $true
$replay.idempotentReplay = $true
$replay.characterDbId = 42
$replay.jingjieName = '历史境界'
$replay.nextJingjieName = '历史目标'
$owner.ReconcileJingjieBreakthrough($replay)
Assert-True ($owner.LocalProgressionSnapshot.nextJingjieName -eq '炼气三层' -and
    $owner.SnapshotRequests -eq 2 -and $changes -eq 2) `
    'A historical replay only requests fresh state, never publishes stale realm fields.'

$noPath = Snapshot 102 42 '炼气三层' $false '' 0 ''
$owner.Receive([UnityEngine.JsonUtility]::ToJson($noPath))
Assert-True (-not $owner.LocalProgressionSnapshot.hasNextJingjie -and
    $owner.LocalProgressionSnapshot.nextJingjieId -eq '' -and
    $owner.LocalProgressionSnapshot.nextJingjieStage -eq 0 -and
    $owner.LocalProgressionSnapshot.nextJingjieName -eq '' -and $changes -eq 3) `
    'The latest snapshot must carry the explicit no-next-path state.'

$failedBreakthrough = [Game.CharacterPanel.Protocol.JingjieBreakthroughResultDto]::new()
$failedBreakthrough.characterDbId = 42
$owner.ReconcileJingjieBreakthrough($failedBreakthrough)
Assert-True ($owner.LocalProgressionSnapshot.jingjieName -eq '炼气三层' -and
    $owner.LocalProgressionSnapshot.nextJingjieName -eq '' -and
    $owner.SnapshotRequests -eq 3 -and $changes -eq 3) `
    'A failed breakthrough with no realm fields must preserve the last good snapshot.'
Write-Output 'PASS: realm snapshot, breakthrough, replay and failure session boundaries.'
