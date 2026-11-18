$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$receiver = Get-Content (Join-Path $root 'Assets/Scripts/NetWork/CombatMessageReceiver.cs') -Raw
$business = [regex]::Match($receiver,
    '(?s)    public BattleSyncApplyResult ApplyCombatEvent\(CombatEventMessageDto message\).*?(?=    private void RemoveMessageRegistration)')
$deduplication = [regex]::Match($receiver,
    '(?s)    private bool IsEventSeen\(string sceneId, long eventId\).*?(?=    private void ClearSeenEvents)')
if (-not $business.Success -or -not $deduplication.Success) {
    throw 'Combat event business or deduplication method was not found.'
}
$resultType = Get-Content (Join-Path $root 'Assets/Scripts/NetWork/BattleSync/Domain/BattleSyncApplyResult.cs') -Raw
$harness = @'
using System;
using System.Collections.Generic;
using Game.Client.BattleSync;
using Game.Client.BattleSync.Protocol;
using UnityEngine;

namespace UnityEngine
{
    public static class Mathf { public static int Max(int a, int b) => Math.Max(a, b); }
}
namespace Game.Client.BattleSync.Protocol
{
    public sealed class CombatEntityDto { public string entityType; public string entityId; }
    public sealed class CombatEventMessageDto
    {
        public string sceneId;
        public long eventId;
        public string eventType;
        public string actionId;
        public long actionSequence;
        public CombatEntityDto source;
        public CombatEntityDto target;
        public int damage;
        public bool critical;
        public int hpAfter;
        public int maxHp;
        public bool lethal;
        public string killerType;
        public string killerId;
        public string killerName;
        public long respawnAvailableAt;
        public long deathKickAt;
        public long combatRevision;
        public long serverTick;
        public long serverTime;
    }
}
public sealed class CombatEntityMessage { public string entityType; public string entityId; }
public sealed class CombatEventMessage
{
    public string sceneId;
    public long eventId;
    public string eventType;
    public string actionId;
    public long actionSequence;
    public CombatEntityMessage source;
    public CombatEntityMessage target;
    public int damage;
    public bool critical;
    public int hpAfter;
    public int maxHp;
    public bool lethal;
    public string killerType;
    public string killerId;
    public string killerName;
    public long respawnAvailableAt;
    public long deathKickAt;
    public long combatRevision;
    public long serverTick;
    public long serverTime;
}
public sealed class CombatStateData
{
    public int Hp;
    public string LifeState;
    public long CombatRevision;
    public long GenerationServerTick;
}
public sealed class CombatStateStore
{
    public int HealthWrites;
    public CombatStateData Monster = new CombatStateData { Hp = 10, LifeState = "Alive",
        GenerationServerTick = 1 };
    public bool TryGetState(string type, string id, out CombatStateData state)
    {
        state = type == "monster" && id == "m1" ? Monster : null;
        return state != null;
    }
    public bool MergeEventHealth(string scene, CombatEntityMessage target,
        int hp, int maxHp, string lifeState, long revision, long tick)
    {
        HealthWrites++;
        if (target.entityType == "monster")
        {
            Monster.Hp = hp;
            Monster.LifeState = lifeState;
            Monster.CombatRevision = revision;
        }
        return true;
    }
}
public sealed class CombatBusinessHarness
{
    private readonly HashSet<string> seenEventKeys = new HashSet<string>();
    private readonly Queue<string> seenEventOrder = new Queue<string>();
    private readonly CombatStateStore stateStore = new CombatStateStore();
    private readonly string activeSceneId = "world_001";
    private readonly int eventDeduplicationCapacity = 2048;
    public event Action<CombatEventMessage> CombatEventApplied;
    public event Action<string, string> CombatRevisionGapDetected;
    public int Published { get; private set; }
    public CombatEventMessage LastPublished { get; private set; }
    public int HealthWrites => stateStore.HealthWrites;
    public void SetMonsterGenerationTick(long tick) { stateStore.Monster.GenerationServerTick = tick; }
    public CombatBusinessHarness()
    {
        CombatEventApplied += value => { Published++; LastPublished = value; };
    }
    private void ResolveStateStore() { }
__BUSINESS__
__DEDUPLICATION__
}
'@.Replace('__BUSINESS__', $business.Value).
    Replace('__DEDUPLICATION__', $deduplication.Value)
Add-Type -TypeDefinition ($harness + "`n" + $resultType) -IgnoreWarnings

function Assert-True($condition, [string]$description) {
    if (-not $condition) { throw $description }
}

$player = [Game.Client.BattleSync.Protocol.CombatEntityDto]::new()
$player.entityType = 'player'
$player.entityId = '1'
$monster = [Game.Client.BattleSync.Protocol.CombatEntityDto]::new()
$monster.entityType = 'monster'
$monster.entityId = 'm1'
$none = [Game.Client.BattleSync.Protocol.CombatEntityDto]::new()
$none.entityType = 'none'
$none.entityId = ''
$combat = [CombatBusinessHarness]::new()

$attack = [Game.Client.BattleSync.Protocol.CombatEventMessageDto]::new()
$attack.sceneId = 'world_001'
$attack.eventId = 101
$attack.eventType = 'attack'
$attack.actionId = 'basic_attack'
$attack.actionSequence = 7
$attack.source = $player
$attack.target = $null
$attack.serverTick = 5
Assert-True (($combat.ApplyCombatEvent($attack) -eq [Game.Client.BattleSync.BattleSyncApplyResult]::Applied) -and
    $combat.Published -eq 1 -and $combat.HealthWrites -eq 0 -and
    $combat.LastPublished.source.entityId -eq '1' -and $null -eq $combat.LastPublished.target -and
    $combat.LastPublished.actionId -eq 'basic_attack' -and
    $combat.LastPublished.actionSequence -eq 7) 'Targetless player attack must publish its action identity without writing health.'
Assert-True (($combat.ApplyCombatEvent($attack) -eq [Game.Client.BattleSync.BattleSyncApplyResult]::AlreadyApplied) -and
    $combat.Published -eq 1 -and $combat.HealthWrites -eq 0) 'Repeated attack event must not publish again.'

$attack.eventId = 102
$attack.source = $monster
$attack.target = $none
Assert-True (($combat.ApplyCombatEvent($attack) -eq [Game.Client.BattleSync.BattleSyncApplyResult]::Applied) -and
    $combat.Published -eq 2 -and $combat.HealthWrites -eq 0) 'Targetless monster attack must not write health.'
$attack.eventId = 103
$attack.target = $monster
Assert-True (($combat.ApplyCombatEvent($attack) -eq [Game.Client.BattleSync.BattleSyncApplyResult]::Applied) -and
    $combat.Published -eq 3 -and $combat.HealthWrites -eq 0 -and
    $combat.LastPublished.target.entityId -eq 'm1') 'Targeted attack with omitted health fields must not write health.'

$damage = [Game.Client.BattleSync.Protocol.CombatEventMessageDto]::new()
$damage.sceneId = 'world_001'
$damage.eventId = 104
$damage.eventType = 'damage'
$damage.source = $player
$damage.target = $monster
$damage.hpAfter = 9
$damage.maxHp = 10
$damage.combatRevision = 1
$damage.serverTick = 6
Assert-True (($combat.ApplyCombatEvent($damage) -eq [Game.Client.BattleSync.BattleSyncApplyResult]::Applied) -and
    $combat.HealthWrites -eq 1 -and $combat.Published -eq 4) 'Damage must still update target health.'

$death = [Game.Client.BattleSync.Protocol.CombatEventMessageDto]::new()
$death.sceneId = 'world_001'
$death.eventId = 105
$death.eventType = 'death'
$death.source = $player
$death.target = $monster
$death.maxHp = 10
$death.lethal = $true
$death.combatRevision = 2
$death.serverTick = 7
Assert-True (($combat.ApplyCombatEvent($death) -eq [Game.Client.BattleSync.BattleSyncApplyResult]::Applied) -and
    $combat.HealthWrites -eq 2 -and $combat.Published -eq 5) 'Death must still write target health.'

$respawn = [Game.Client.BattleSync.Protocol.CombatEventMessageDto]::new()
$respawn.sceneId = 'world_001'
$respawn.eventId = 106
$respawn.eventType = 'respawn'
$respawn.source = $player
$respawn.target = $monster
$respawn.hpAfter = 10
$respawn.maxHp = 10
$respawn.combatRevision = 3
$respawn.serverTick = 8
Assert-True (($combat.ApplyCombatEvent($respawn) -eq [Game.Client.BattleSync.BattleSyncApplyResult]::Applied) -and
    $combat.HealthWrites -eq 3 -and $combat.Published -eq 6) 'Respawn must still write target health.'

$attack.eventId = 107
$attack.sceneId = 'other_scene'
Assert-True (($combat.ApplyCombatEvent($attack) -eq [Game.Client.BattleSync.BattleSyncApplyResult]::IgnoredWrongScene) -and
    $combat.Published -eq 6 -and $combat.HealthWrites -eq 3) 'Wrong-scene attack must not publish or write health.'

$combat.SetMonsterGenerationTick(20)
$attack.sceneId = 'world_001'
$attack.eventId = 108
$attack.serverTick = 10
Assert-True (($combat.ApplyCombatEvent($attack) -eq [Game.Client.BattleSync.BattleSyncApplyResult]::IgnoredStale) -and
    $combat.Published -eq 6) 'Old-life monster attack must not play on a newer life.'
$damage.eventId = 109
$damage.serverTick = 10
$damage.combatRevision = 4
Assert-True (($combat.ApplyCombatEvent($damage) -eq [Game.Client.BattleSync.BattleSyncApplyResult]::IgnoredStale) -and
    $combat.HealthWrites -eq 3 -and $combat.Published -eq 6) 'Old-life health must not cross the spawn tick.'

Write-Output 'PASS: attack notification, no health mutation, deduplication and lifecycle isolation.'
