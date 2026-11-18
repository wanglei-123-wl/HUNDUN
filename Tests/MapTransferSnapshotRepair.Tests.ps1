$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$sources = @(
    'Assets/Scripts/NetWork/BattleSync/Protocol/BattleSyncProtocolConstants.cs'
    'Assets/Scripts/NetWork/BattleSync/Protocol/SnapshotRepairMessages.cs'
    'Assets/Scripts/NetWork/BattleSync/Services/SnapshotRepairService.cs'
) | ForEach-Object { Get-Content -LiteralPath (Join-Path $root $_) -Raw }
$sources = $sources | ForEach-Object {
    [regex]::Replace($_, '(?m)^using [^;]+;\r?\n', '')
}

$stubs = @'
namespace Game.Client.BattleSync {
    public enum BattleEntityKind { Player, Monster, Loot }
    public interface IBattleSyncTransport {
        bool IsConnected { get; }
        long ConnectionEpoch { get; }
        bool SendJson(string json);
    }
    public interface IBattleSyncJsonCodec {
        bool TrySerialize<T>(T value, out string json, out string error) where T : class;
    }
    public interface IBattleSyncClock { long NowMilliseconds { get; } }
    public sealed class TestClock : IBattleSyncClock {
        public long NowMilliseconds { get; set; }
    }
    public sealed class TestTransport : IBattleSyncTransport {
        public bool IsConnected => true;
        public long ConnectionEpoch => 1;
        public readonly List<string> Sent = new List<string>();
        public bool SendJson(string json) { Sent.Add(json); return true; }
    }
    public sealed class TestCodec : IBattleSyncJsonCodec {
        public bool TrySerialize<T>(T value, out string json, out string error) where T : class {
            json = JsonSerializer.Serialize(value, new JsonSerializerOptions { IncludeFields = true });
            error = null;
            return true;
        }
    }
}
'@
Add-Type -TypeDefinition ("using System;`nusing System.Collections.Generic;`nusing System.Text.Json;`nusing Game.Client.BattleSync.Protocol;`n" +
    ($sources -join "`n") + $stubs) -IgnoreWarnings | Out-Null

function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
}

$transport = [Game.Client.BattleSync.TestTransport]::new()
$clock = [Game.Client.BattleSync.TestClock]::new()
$session = 'source-scene-session'
$provider = [Func[string]]{ $session }
$repair = [Game.Client.BattleSync.SnapshotRepairService]::new(
    $transport, [Game.Client.BattleSync.TestCodec]::new(), $clock, $provider)
$repair.BeginSession(1, 'world_002', 1)
$repair.RequestRepair([Game.Client.BattleSync.SnapshotRepairReason]::ManualRequest,
    [Game.Client.BattleSync.BattleEntityKind]::Player, '', 1)
$repair.Update()
Assert-True ($transport.Sent.Count -eq 1) 'Snapshot repair was not sent.'
$first = $transport.Sent[0] | ConvertFrom-Json -AsHashtable
Assert-True ($first.sceneSessionId -eq 'source-scene-session' -and
    $first.requestSequence -eq 1 -and $first.msgId -eq 'snapshot_repair_request') `
    'Snapshot repair must carry the current scene session and preserve its sequence.'

$repair.NotifySnapshotBatchCompleted(2)
$session = ''
$clock.NowMilliseconds = 2000
$repair.RequestRepair([Game.Client.BattleSync.SnapshotRepairReason]::ManualRequest,
    [Game.Client.BattleSync.BattleEntityKind]::Player, '', 2)
$repair.Update()
Assert-True ($transport.Sent.Count -eq 1) 'Repair must wait while no scene session exists.'
$session = 'destination-scene-session'
$repair.Update()
$second = $transport.Sent[1] | ConvertFrom-Json -AsHashtable
Assert-True ($second.sceneSessionId -eq 'destination-scene-session' -and
    $second.requestSequence -eq 2) `
    'Repair must resume with the new scene session without resetting its request sequence.'

Write-Output 'PASS: snapshot repair scene session and continuous request sequence.'
