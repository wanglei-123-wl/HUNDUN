$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$paths = @(
    'Assets/Scripts/Cultivation/Protocol/RefiningMessages.cs'
    'Assets/Scripts/Cultivation/Protocol/RefiningPreviewCalculator.cs'
    'Assets/Scripts/Cultivation/Protocol/RefiningTaskClock.cs'
    'Assets/Scripts/Cultivation/Protocol/RefiningBreakthroughConditions.cs'
)
$source = ($paths | ForEach-Object {
    (Get-Content -LiteralPath (Join-Path $root $_) -Raw) -replace
        '(?m)^using [^;]+;\r?\n', ''
}) -join "`n"
$scene = Get-Content -LiteralPath (Join-Path $root 'Assets/Scenes/WorldShell.unity') -Raw

$stubs = @'
using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Inventory.Model.V2;
using Game.Inventory.Protocol.V2;
public class NetMessageBase { public string msgId; public string type; }
public class ProgressionSnapshotResult {
    public bool success;
    public long characterDbId;
    public string jingjieId;
    public int jingjieStage;
    public long jingjieExp;
    public bool hasNextJingjie;
    public string nextJingjieId;
    public int nextJingjieStage;
}
namespace Game.Inventory.Protocol.V2 {
    public class InventoryV2StackChangeDto { public int newCount; }
}
namespace Game.Inventory.Model.V2 {
    public class InventoryOwnershipSnapshot {
        public long CharacterDbId;
        public readonly Dictionary<string,long> Counts = new();
        public bool TryGetItemCount(string id, out long count) =>
            Counts.TryGetValue(id, out count);
    }
}
'@
Add-Type -TypeDefinition ($stubs + "`n" + $source) -IgnoreWarnings | Out-Null

function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
}
function SceneBlock([int]$kind, [long]$id) {
    $pattern = "(?s)--- !u!$kind &$id`r?`n(?<body>.*?)(?=`r?`n--- !u!)"
    $matches = [regex]::Matches($scene, $pattern)
    Assert-True ($matches.Count -eq 1) "Missing or duplicate scene block $kind/$id."
    return $matches[0].Groups['body'].Value
}

$task = [Game.Cultivation.Protocol.RefiningTaskDto]::new()
$task.startedAtUnixMilliseconds = 1790500000000
$task.endsAtUnixMilliseconds = 1790500300000
$task.taskId = 'batch-1'
$clock = $null
Assert-True ([Game.Cultivation.Protocol.RefiningTaskClock]::TryCreate(
    $task, 1790500000000, 100.0, [ref]$clock) -and
    $clock.RemainingSeconds(100.0) -eq 300 -and
    [math]::Abs($clock.Progress(160.0) - 0.2) -lt 0.0001 -and
    $clock.RemainingSeconds(160.0) -eq 240 -and
    $clock.RemainingSeconds(400.0) -eq 0 -and
    $clock.Progress(400.0) -eq 1) `
    'The task clock must use server time and elapsed monotonic time for both displays.'
Assert-True ($clock.RemainingSeconds(100.0) -eq 300) `
    'Wall-time changes cannot affect a clock given unchanged monotonic time.'
$late = $null
Assert-True ([Game.Cultivation.Protocol.RefiningTaskClock]::TryCreate(
    $task, 1790500299500, 50.0, [ref]$late) -and
    $late.RemainingSeconds(50.0) -eq 1 -and
    $late.RemainingSeconds(50.5) -eq 0) `
    'A re-opened task must resume from the server absolute end, not restart.'
$invalid = $null
Assert-True (-not [Game.Cultivation.Protocol.RefiningTaskClock]::TryCreate(
    $task, 0, 100.0, [ref]$invalid)) `
    'Missing server time cannot silently use the local wall clock.'

$catalog = [Game.Cultivation.Protocol.RefiningResultDto]::new()
$catalog.success = $true
$progression = [ProgressionSnapshotResult]::new()
$progression.success = $true
$progression.characterDbId = 42
$progression.jingjieId = 'lianqi'
$progression.jingjieStage = 1
$progression.jingjieExp = 100
$progression.hasNextJingjie = $true
$progression.nextJingjieId = 'lianqi'
$progression.nextJingjieStage = 2
$path = [Game.Cultivation.Protocol.RefiningBreakthroughDto]::new()
$path.fromJingjieId = 'lianqi'
$path.fromJingjieStage = 1
$path.toJingjieId = 'lianqi'
$path.toJingjieStage = 2
$path.requiredCultivation = 100
$path.materials = @()
$catalog.breakthroughs = @($path)
$found = $null
Assert-True ([Game.Cultivation.Protocol.RefiningBreakthroughConditions]::TryFindPath(
    $catalog, $progression, 42, [ref]$found) -and
    [Game.Cultivation.Protocol.RefiningBreakthroughConditions]::MatchesNextRealm(
        $found, $progression) -and
    [Game.Cultivation.Protocol.RefiningBreakthroughConditions]::CanAttempt(
        $found, 100, $null, 42)) `
    'A matching path with sufficient cultivation and no materials must be available.'
$progression.nextJingjieStage = 3
Assert-True (-not [Game.Cultivation.Protocol.RefiningBreakthroughConditions]::MatchesNextRealm(
    $found, $progression)) `
    'A catalog path to another target cannot authorize the published next realm.'
$progression.nextJingjieStage = 2
Assert-True (-not [Game.Cultivation.Protocol.RefiningBreakthroughConditions]::CanAttempt(
    $found, 99, $null, 42)) 'Total cultivation cannot substitute for available cultivation.'
$progression.jingjieStage = 2
Assert-True (-not [Game.Cultivation.Protocol.RefiningBreakthroughConditions]::TryFindPath(
    $catalog, $progression, 42, [ref]$found)) `
    'A different realm stage must not reuse an unrelated breakthrough path.'
$progression.jingjieStage = 1
$material = [Game.Cultivation.Protocol.RefiningMaterialDto]::new()
$material.itemId = 'tupo_dan'
$material.count = 2
$path.materials = @($material)
$bag = [Game.Inventory.Model.V2.InventoryOwnershipSnapshot]::new()
$bag.CharacterDbId = 42
$bag.Counts['tupo_dan'] = 1
Assert-True (-not [Game.Cultivation.Protocol.RefiningBreakthroughConditions]::CanAttempt(
    $path, 100, $bag, 42)) 'Insufficient material must block the local attempt.'
$bag.Counts['tupo_dan'] = 2
Assert-True ([Game.Cultivation.Protocol.RefiningBreakthroughConditions]::CanAttempt(
    $path, 100, $bag, 42) -and
    -not [Game.Cultivation.Protocol.RefiningBreakthroughConditions]::CanAttempt(
        $path, 100, $bag, 99)) `
    'Exact material count is sufficient only for the current character.'
$path.requiredCultivation = 0
$path.materials = @()
Assert-True ([Game.Cultivation.Protocol.RefiningBreakthroughConditions]::CanAttempt(
    $path, 0, $null, 42)) 'Zero cultivation does not by itself mean maximum realm.'
$catalog.breakthroughs = @()
Assert-True (-not [Game.Cultivation.Protocol.RefiningBreakthroughConditions]::TryFindPath(
    $catalog, $progression, 42, [ref]$found)) `
    'No published next path must keep the button disabled.'

$leftMeta = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Cultivation/Presentation/RefiningTaskPresenter.cs.meta') -Raw
$rightMeta = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Cultivation/Presentation/CultivationProgressPresenter.cs.meta') -Raw
$leftGuid = [regex]::Match($leftMeta, '(?m)^guid: ([0-9a-f]{32})$').Groups[1].Value
$rightGuid = [regex]::Match($rightMeta, '(?m)^guid: ([0-9a-f]{32})$').Groups[1].Value
Assert-True ($leftGuid -ne '' -and $rightGuid -ne '' -and
    (SceneBlock 1 1395841564) -match 'component: \{fileID: 2200000020\}' -and
    (SceneBlock 1 625551245) -match 'component: \{fileID: 2200000021\}' -and
    (SceneBlock 114 2200000020) -match
        "(?s)guid: $leftGuid.*?materialDropTarget: \{fileID: 1887203104\}.*?inventory: \{fileID: 390961493\}.*?countdownText: \{fileID: 1798083088\}.*?cultivationText: \{fileID: 267538611\}.*?taskFill: \{fileID: 1808865920\}.*?materialIcon: \{fileID: 1322811157\}" -and
    (SceneBlock 114 2200000021) -match
        "(?s)guid: $rightGuid.*?inventory: \{fileID: 390961493\}.*?progressText: \{fileID: 1003707233\}.*?cultivationFill: \{fileID: 1974593791\}.*?nextRealmText: \{fileID: 944717356\}" -and
    $scene -notmatch '--- !u!114 &(1395841566|625551247)\r?\n') `
    'Task and cultivation progress must bind separate UI and have no health-driver conflict.'
Assert-True ((SceneBlock 224 1798083087) -match
    'm_SizeDelta: \{x: 125, y: 10\}' -and
    (SceneBlock 224 267538610) -match
    'm_SizeDelta: \{x: 145, y: 16\}') `
    'Time and reward text bounds must accommodate long task values.'
Write-Output 'PASS: server-calibrated task clock, breakthrough conditions and exact scene references.'
