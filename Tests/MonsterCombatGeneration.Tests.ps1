$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$store = Get-Content (Join-Path $root 'Assets/Scripts/NetWork/CombatStateStore.cs') -Raw
$store = $store -replace '^using System;\s*using System.Collections.Generic;\s*using UnityEngine;\s*', ''
$stubs = @'
using System;
using System.Collections.Generic;
using UnityEngine;
namespace UnityEngine
{
    public class MonoBehaviour { }
    public static class Mathf
    {
        public static int Max(int a, int b) => Math.Max(a, b);
        public static int Clamp(int value, int min, int max) => Math.Max(min, Math.Min(value, max));
    }
}
public sealed class CombatEntityMessage
{
    public string entityType, entityId;
}
public sealed class CombatStateMessage
{
    public CombatEntityMessage entity;
    public int hp, maxHp;
    public string lifeState;
    public long combatRevision, spawnGeneration;
}
'@
Add-Type -TypeDefinition ($stubs + "`n" + $store) -IgnoreWarnings -ErrorAction Stop

function Assert-True($condition, [string]$description) {
    if (-not $condition) { throw $description }
}

$store = [CombatStateStore]::new()
$store.BeginScene('world_001')
$entity = [CombatEntityMessage]::new()
$entity.entityType = 'monster'
$entity.entityId = 'mon_sp42_000001'
Assert-True ($store.MergeMonsterSpawnState('world_001', $entity.entityId, 0, 100,
    'Dead', 20, 1, 50)) 'First generation must be accepted.'
Assert-True ($store.MergeMonsterSpawnState('world_001', $entity.entityId, 100, 100,
    'Alive', 1, 2, 100)) 'New generation must reset the combat revision.'
Assert-True (-not $store.MergeDynamicMonsterHealth('world_001', $entity.entityId, 0, 21, 90, 1)) 'An old-generation frame must be rejected.'
Assert-True (-not $store.MergeEventHealth('world_001', $entity, 0, 100,
    'Dead', 21, 90)) 'A late old-generation death event must be rejected.'

$old = [CombatStateMessage]::new()
$old.entity = $entity
$old.hp = 0
$old.maxHp = 100
$old.lifeState = 'Dead'
$old.combatRevision = 22
$old.spawnGeneration = 1
Assert-True ($store.TryReplaceSnapshot('world_001', [CombatStateMessage[]]@($old), 90)) 'Snapshot shape must remain valid.'
$state = $null
Assert-True ($store.TryGetState('monster', $entity.entityId, [ref]$state) -and
    $state.SpawnGeneration -eq 2 -and $state.Hp -eq 100 -and $state.CombatRevision -eq 1 -and
    $state.GenerationServerTick -eq 100) 'An old snapshot must not overwrite the new life.'

$new = [CombatStateMessage]::new()
$new.entity = $entity
$new.hp = 80
$new.maxHp = 100
$new.lifeState = 'Alive'
$new.combatRevision = 2
$new.spawnGeneration = 2
Assert-True ($store.TryReplaceSnapshot('world_001', [CombatStateMessage[]]@($new), 120)) 'Fresh snapshot must apply.'
Assert-True ($store.TryGetState('monster', $entity.entityId, [ref]$state) -and
    $state.GenerationServerTick -eq 100 -and $state.Hp -eq 80) 'Same-generation snapshot must preserve its start tick.'
$new.hp = 0
$new.lifeState = 'Dead'
Assert-True ($store.TryReplaceSnapshot('world_001', [CombatStateMessage[]]@($new), 125)) 'Duplicate snapshot must remain valid.'
Assert-True ($store.TryGetState('monster', $entity.entityId, [ref]$state) -and
    $state.Hp -eq 80 -and $state.LifeState -eq 'Alive') 'Equal revision cannot rewrite the current life state.'
Assert-True ($store.MergeDynamicMonsterHealth('world_001', $entity.entityId, 70, 3, 121, 2)) 'Current-generation frame must apply.'
Assert-True ($store.TryGetState('monster', $entity.entityId, [ref]$state) -and
    $state.Hp -eq 70 -and $state.SpawnGeneration -eq 2) 'Valid dynamic health must retain the generation.'

$player = [CombatEntityMessage]::new()
$player.entityType = 'player'
$player.entityId = '1001'
Assert-True ($store.MergeEventHealth('world_001', $player, 30, 50, 'Alive', 1, 10)) 'Player event path must remain available.'
Write-Output 'PASS: monster spawn generations, stale frames/events/snapshots, and player health isolation.'
