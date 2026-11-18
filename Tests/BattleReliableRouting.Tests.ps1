$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$coordinator = Get-Content (Join-Path $root 'Assets/Scripts/NetWork/BattleSync/Core/BattleSyncCoordinator.cs') -Raw
$batch = [regex]::Match($coordinator,
    '(?s)        private void HandleCombatEventBatchJson\(string json\).*?(?=        private void HandleStateFrameJson)')
$single = [regex]::Match($coordinator,
    '(?s)        private void HandleCombatEventJson\(string json\).*?(?=        private void HandleCombatEventBatchJson)')
$validation = [regex]::Match($coordinator,
    '(?s)        private static bool ValidateCombatEvent\(.*?(?=        private void ProcessSnapshot)')
$monster = [regex]::Match($coordinator,
    '(?s)        private void HandleMonsterUpsertJson\(string json\).*?(?=        private void HandleMonsterRemoveJson)')
if (-not $batch.Success -or -not $single.Success -or -not $validation.Success -or -not $monster.Success) {
    throw 'Reliable routing handlers were not found.'
}

$paths = @(
    'Assets/Scripts/NetWork/BattleSync/Protocol/BattleSyncProtocolConstants.cs'
    'Assets/Scripts/NetWork/BattleSync/Protocol/StructureMessages.cs'
    'Assets/Scripts/NetWork/BattleSync/Domain/BattleSyncApplyResult.cs'
    'Assets/Scripts/NetWork/BattleSync/Services/ReliableAckService.cs'
    'Assets/Scripts/NetWork/BattleSync/Processing/ReliableMessageProcessor.cs'
)
$sources = ($paths | ForEach-Object {
    (Get-Content (Join-Path $root $_) -Raw) -replace '(?m)^using [^\r\n]+;\r?\n', ''
}) -join "`n"
$harness = @'
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using Game.Client.BattleSync.Protocol;

namespace Game.Client.BattleSync.Protocol
{
    public sealed class MonsterStateDto { public string monsterInstanceId; }
    public sealed class LootStateDto {}
    public sealed class CombatEntityDto { public string entityType; public string entityId; }
    public sealed class BattleOutboxAckDto
    {
        public string msgId = BattleSyncProtocolConstants.MessageIds.BattleOutboxAck;
        public string type = BattleSyncProtocolConstants.MessageIds.BattleOutboxAck;
        public long reliableMessageId;
    }
    public sealed class PlayerStateDto {}
}
namespace Game.Client.BattleSync
{
    public static class BattleSyncDiagnosticsLogger
    {
        public static void LogAckAttempt(PendingReliableAck pending, bool succeeded,
            string stage, string detail) { }
        public static void LogAckRequest(ReliableAckRequest request,
            ReliableAckRequestResult result) { }
        public static void LogValidationFailure(string messageId, long reliableMessageId,
            string sceneId, string detail) { }
    }
    public interface IBattleSyncTransport
    {
        bool IsConnected { get; }
        long ConnectionEpoch { get; }
        bool SendJson(string json);
    }
    public interface IBattleSyncJsonCodec
    {
        bool TrySerialize<T>(T value, out string json, out string error);
        bool TryDeserialize<T>(string json, out T value, out string error) where T : class;
    }
    public sealed class TestCodec : IBattleSyncJsonCodec
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions { IncludeFields = true };
        public bool TrySerialize<T>(T value, out string json, out string error)
        {
            json = JsonSerializer.Serialize(value, Options);
            error = null;
            return true;
        }
        public bool TryDeserialize<T>(string json, out T value, out string error) where T : class
        {
            try { value = JsonSerializer.Deserialize<T>(json, Options); error = null; return true; }
            catch (Exception ex) { value = null; error = ex.Message; return false; }
        }
    }
    public sealed class TestTransport : IBattleSyncTransport
    {
        public bool IsConnected => true;
        public long ConnectionEpoch => 1;
        public bool SendFails;
        public readonly List<string> Sent = new List<string>();
        public bool SendJson(string json)
        {
            if (SendFails) return false;
            Sent.Add(json);
            return true;
        }
    }
    public sealed class TestSessionState
    {
        public long ConnectionEpoch = 1;
        public string SceneId = "world_001";
    }
    public sealed class TestClock { public long NowMilliseconds => 100; }
    public sealed class TestMonster
    {
        public int Calls;
        public BattleSyncApplyResult ApplyMonsterUpsert(MonsterUpsertMessageDto message)
        {
            Calls++;
            return BattleSyncApplyResult.Applied;
        }
    }
    public sealed class TestCombat
    {
        public int Calls;
        public int FailOnCall;
        public BattleSyncApplyResult ApplyCombatEvent(CombatEventMessageDto message)
        {
            Calls++;
            return Calls == FailOnCall ? BattleSyncApplyResult.RetryableFailure : BattleSyncApplyResult.Applied;
        }
    }
    public sealed class TestBindings
    {
        public readonly TestMonster Monster = new TestMonster();
        public readonly TestCombat Combat = new TestCombat();
    }
    public sealed class RoutingHarness
    {
        private readonly TestTransport transport = new TestTransport();
        private readonly TestCodec jsonCodec = new TestCodec();
        private readonly TestSessionState sessionState = new TestSessionState();
        private readonly TestClock clock = new TestClock();
        private readonly TestBindings moduleBindings = new TestBindings();
        private readonly ReliableAckService ackService;
        private readonly ReliableMessageProcessor reliableMessageProcessor;
        public RoutingHarness()
        {
            ackService = new ReliableAckService(transport, jsonCodec);
            reliableMessageProcessor = new ReliableMessageProcessor(ackService);
            ackService.BeginSession(1, "world_001");
            reliableMessageProcessor.BeginSession(1, "world_001");
        }
        public int AckCount => transport.Sent.Count;
        public int CombatCalls => moduleBindings.Combat.Calls;
        public int MonsterCalls => moduleBindings.Monster.Calls;
        public int FailCombatOnCall { set { moduleBindings.Combat.FailOnCall = value; } }
        public bool FailAck { set { transport.SendFails = value; } }
        public string Ack(int index) => transport.Sent[index];
        public void Single(string json) => HandleCombatEventJson(json);
        public void Batch(string json) => HandleCombatEventBatchJson(json);
        public void MonsterUpsert(string json) => HandleMonsterUpsertJson(json);
        public void RetryAcks() => ackService.Tick(10000);
        private bool TryReadReliable<T>(string json, out T message) where T : ReliableStructureMessageDtoBase
        {
            return jsonCodec.TryDeserialize(json, out message, out _)
                && message != null && message.sceneId == sessionState.SceneId;
        }
        private static bool ValidateBase(ReliableStructureMessageDtoBase message, string id) =>
            message != null && message.msgId == id && message.reliableMessageId > 0
                && !string.IsNullOrWhiteSpace(message.sceneId) && message.serverTime >= 0;
__VALIDATION__
        private void ProcessReliable(ReliableStructureMessageDtoBase message, string identity,
            long serverTick, Func<BattleSyncApplyResult> apply)
        {
            var context = new ReliableMessageContext(1, message.sceneId, message.msgId,
                message.reliableMessageId, identity, clock.NowMilliseconds, message.serverTime, serverTick);
            reliableMessageProcessor.Process(context, apply);
        }
__MONSTER__
__SINGLE__
__BATCH__
    }
}
'@.Replace('__MONSTER__', $monster.Value).Replace('__SINGLE__', $single.Value).
    Replace('__BATCH__', $batch.Value).Replace('__VALIDATION__', $validation.Value)
Add-Type -TypeDefinition ($harness + "`n" + $sources) -IgnoreWarnings

function Assert-True($condition, $description) {
    if (-not $condition) { throw $description }
}

$event = '{"eventId":11,"eventType":"damage","source":{"entityType":"player","entityId":"1"},"target":{"entityType":"monster","entityId":"m1"},"hpAfter":9,"maxHp":10,"combatRevision":1}'
$batchJson = '{"msgId":"combat_event_batch","type":"combat_event_batch","reliableMessageId":20,"sceneId":"world_001","serverTime":100,"serverTick":5,"events":[' + $event + ',' + $event.Replace('"eventId":11','"eventId":12').Replace('"combatRevision":1','"combatRevision":2') + ']}'
$router = [Game.Client.BattleSync.RoutingHarness]::new()
$router.Batch($batchJson)
Assert-True ($router.CombatCalls -eq 2 -and $router.AckCount -eq 1) 'Batch should apply its events and ACK once.'
$firstAck = $router.Ack(0) | ConvertFrom-Json
Assert-True ($firstAck.msgId -eq 'battle_outbox_ack' -and
    $firstAck.type -eq 'battle_outbox_ack' -and
    $firstAck.reliableMessageId -eq 20) 'ACK must use the exact server ID and both protocol fields.'
$router.Batch($batchJson)
Assert-True ($router.CombatCalls -eq 2 -and $router.AckCount -eq 2) 'Duplicate batch should only resend ACK.'

$bad = $batchJson.Replace('"eventId":12','"eventId":0').Replace('"reliableMessageId":20','"reliableMessageId":21')
$router.Batch($bad)
Assert-True ($router.CombatCalls -eq 2 -and $router.AckCount -eq 2) 'Invalid batch must neither partially apply nor ACK.'
$router.FailCombatOnCall = 3
$retry = $batchJson.Replace('"reliableMessageId":20','"reliableMessageId":22')
$router.Batch($retry)
Assert-True ($router.AckCount -eq 2) 'Failed batch must not ACK.'
$router.FailCombatOnCall = 0
$router.Batch($retry)
Assert-True ($router.AckCount -eq 3) 'Retried batch must ACK after applying.'

$upsert = '{"msgId":"monster_upsert","type":"monster_upsert","reliableMessageId":30,"sceneId":"world_001","serverTime":100,"monster":{"monsterInstanceId":"m1"}}'
$router.MonsterUpsert($upsert)
$router.MonsterUpsert($upsert)
Assert-True ($router.MonsterCalls -eq 1 -and $router.AckCount -eq 5) 'Reliable monster upsert must deduplicate and re-ACK.'
$router.MonsterUpsert($upsert.Replace('"reliableMessageId":30','"reliableMessageId":0'))
Assert-True ($router.MonsterCalls -eq 1 -and $router.AckCount -eq 5) 'Unreliable legacy upsert must not ACK.'

$attacks = [Game.Client.BattleSync.RoutingHarness]::new()
$playerMiss = '{"msgId":"combat_event","type":"combat_event","reliableMessageId":40,"sceneId":"world_001","serverTime":100,"serverTick":5,"eventId":14,"eventType":"attack","source":{"entityType":"player","entityId":"1"},"target":null,"damage":0,"combatRevision":0}'
$attacks.Single($playerMiss)
$attacks.Single($playerMiss)
Assert-True ($attacks.CombatCalls -eq 1 -and $attacks.AckCount -eq 2 -and
    $attacks.Ack(0) -match '"reliableMessageId":40') 'Targetless player attack must reach reliable processing and duplicate must re-ACK.'
$monsterMiss = $playerMiss.Replace('"reliableMessageId":40', '"reliableMessageId":41').
    Replace('"eventId":14', '"eventId":15').
    Replace('"entityType":"player"', '"entityType":"monster"').
    Replace('"target":null', '"target":{"entityType":"none","entityId":""}')
$attacks.Single($monsterMiss)
Assert-True ($attacks.CombatCalls -eq 2 -and $attacks.AckCount -eq 3) 'Targetless monster attack with an explicit none target must pass validation.'
$targetedAttack = $playerMiss.Replace('"reliableMessageId":40', '"reliableMessageId":42').
    Replace('"eventId":14', '"eventId":16').
    Replace('"target":null', '"target":{"entityType":"monster","entityId":"m1"}')
$attacks.Single($targetedAttack)
Assert-True ($attacks.CombatCalls -eq 3 -and $attacks.AckCount -eq 4) 'Targeted zero-damage attack with no health revision must now reach reliable processing.'
$attacks.Single($targetedAttack)
Assert-True ($attacks.CombatCalls -eq 3 -and $attacks.AckCount -eq 5) 'Duplicate targeted attack must re-ACK without replay.'
$attacks.Single($playerMiss.Replace('"reliableMessageId":40', '"reliableMessageId":43').
    Replace('"eventType":"attack"', '"eventType":"damage"'))
$attacks.Single($monsterMiss.Replace('"reliableMessageId":41', '"reliableMessageId":45').
    Replace('"eventType":"attack"', '"eventType":"death"'))
$attacks.Single($playerMiss.Replace('"reliableMessageId":40', '"reliableMessageId":46').
    Replace('"target":null', '"target":{"entityType":"player","entityId":""}'))
$attacks.Single($playerMiss.Replace('"reliableMessageId":40', '"reliableMessageId":0'))
$attacks.Single($playerMiss.Replace('"reliableMessageId":40', '"reliableMessageId":-1'))
$attacks.Single($playerMiss.Replace('"reliableMessageId":40,', ''))
Assert-True ($attacks.CombatCalls -eq 3 -and $attacks.AckCount -eq 5) 'Damage without a target and non-positive reliable ID must not pass validation.'
$batchAttack = $playerMiss.Replace('"msgId":"combat_event"', '"msgId":"combat_event_batch"').
    Replace('"reliableMessageId":40', '"reliableMessageId":44').
    Replace('"eventId":14,"eventType":"attack"', '"events":[{"eventId":14,"eventType":"attack"').
    Replace('"combatRevision":0}', '"combatRevision":0}]}')
$attacks.Batch($batchAttack)
Assert-True ($attacks.CombatCalls -eq 4 -and $attacks.AckCount -eq 6 -and
    $attacks.Ack(5) -match '"reliableMessageId":44') 'Targetless attack in a batch must use the outer reliable ID.'
$attacks.Batch($batchAttack)
Assert-True ($attacks.CombatCalls -eq 4 -and $attacks.AckCount -eq 7) 'Duplicate attack batch must re-ACK without replaying the event.'
$attacks.Batch($batchAttack.Replace('"reliableMessageId":44', '"reliableMessageId":0'))
$attacks.Batch($batchAttack.Replace('"reliableMessageId":44', '"reliableMessageId":-1'))
$attacks.Batch($batchAttack.Replace('"reliableMessageId":44,', ''))
Assert-True ($attacks.CombatCalls -eq 4 -and $attacks.AckCount -eq 7) 'Batch with zero, negative or missing reliable ID must not ACK.'
$monsterTargeted = $monsterMiss.Replace('"reliableMessageId":41', '"reliableMessageId":47').
    Replace('"eventId":15', '"eventId":17').
    Replace('"target":{"entityType":"none","entityId":""}',
        '"target":{"entityType":"player","entityId":"1"}')
$attacks.Single($monsterTargeted)
Assert-True ($attacks.CombatCalls -eq 5 -and $attacks.AckCount -eq 8) 'Zero-damage monster attack against a player must ACK.'

$attackRetry = [Game.Client.BattleSync.RoutingHarness]::new()
$attackRetry.FailAck = $true
$attackRetry.Single($playerMiss)
Assert-True ($attackRetry.CombatCalls -eq 1 -and $attackRetry.AckCount -eq 0) 'Failed attack ACK send must not repeat the business event.'
$attackRetry.FailAck = $false
$attackRetry.Single($playerMiss)
Assert-True ($attackRetry.CombatCalls -eq 1 -and $attackRetry.AckCount -eq 1 -and
    ($attackRetry.Ack(0) | ConvertFrom-Json).reliableMessageId -eq 40) 'Repeated attack must retry the original ACK.'

$lifeEvents = [Game.Client.BattleSync.RoutingHarness]::new()
foreach ($kind in @('damage', 'death', 'respawn')) {
    $lifeEvent = @{
        msgId = 'combat_event'; type = 'combat_event'
        reliableMessageId = 50 + $lifeEvents.CombatCalls
        sceneId = 'world_001'; serverTime = 100; serverTick = 5
        eventId = 60 + $lifeEvents.CombatCalls; eventType = $kind
        source = @{ entityType = 'player'; entityId = '1' }
        target = @{ entityType = 'monster'; entityId = 'm1' }
        damage = 1; hpAfter = 9; maxHp = 10; combatRevision = 1
    } | ConvertTo-Json -Depth 5 -Compress
    $lifeEvents.Single($lifeEvent)
}
Assert-True ($lifeEvents.CombatCalls -eq 3 -and $lifeEvents.AckCount -eq 3) 'Damage, death and respawn must retain reliable ACK routing.'

$queued = [Game.Client.BattleSync.RoutingHarness]::new()
$queued.FailAck = $true
$queued.MonsterUpsert($upsert)
Assert-True ($queued.MonsterCalls -eq 1 -and $queued.AckCount -eq 0) 'Failed ACK send must not reapply the monster.'
$queued.FailAck = $false
$queued.RetryAcks()
Assert-True ($queued.MonsterCalls -eq 1 -and $queued.AckCount -eq 1) 'Queued ACK must be retried without reapplying.'

Write-Output 'PASS: batch ACK, duplicate delivery, failed/invalid batches, reliable monster upsert, ACK retry.'
