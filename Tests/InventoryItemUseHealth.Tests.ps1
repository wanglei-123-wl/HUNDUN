$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Get-Content (Join-Path $root 'Assets/Scripts/NetWork/CombatStateStore.cs') -Raw
$stubs = @'
namespace UnityEngine
{
    public class MonoBehaviour { }
    public static class Mathf
    {
        public static int Max(int a, int b) => System.Math.Max(a, b);
        public static int Clamp(int value, int min, int max) => System.Math.Clamp(value, min, max);
    }
}
public sealed class CombatEntityMessage { public string entityType; public string entityId; }
public sealed class CombatStateMessage
{
    public CombatEntityMessage entity;
    public int hp;
    public int maxHp;
    public string lifeState;
    public long combatRevision;
    public long spawnGeneration;
}
'@
Add-Type -TypeDefinition ($source + "`n" + $stubs) -IgnoreWarnings
function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
}

$store = [CombatStateStore]::new()
$state = [CombatStateMessage]::new()
$state.entity = [CombatEntityMessage]::new()
$state.entity.entityType = 'player'
$state.entity.entityId = '42'
$state.hp = 30
$state.maxHp = 100
$state.lifeState = 'Alive'
$state.combatRevision = 7
Assert-True ($store.TryReplaceSnapshot('world_001', [CombatStateMessage[]]@($state))) 'Combat fixture must load.'
Assert-True ($store.TryApplyItemUseHealth('world_001', '42', 80, 7)) 'Matching item use must apply HP.'
$health = $null
Assert-True ($store.TryGetState('player', '42', [ref]$health) -and
    $health.Hp -eq 80 -and $health.CombatRevision -eq 7) 'HP must change without inventing a combat revision.'
Assert-True (-not $store.TryApplyItemUseHealth('world_001', '42', 90, 6)) 'Stale request cannot overwrite health.'
Assert-True (-not $store.TryApplyItemUseHealth('other_scene', '42', 90, 7)) 'Other scene cannot overwrite health.'
$store.MergeEventHealth('world_001', $state.entity, 20, 100, 'Alive', 8) | Out-Null
Assert-True (-not $store.TryApplyItemUseHealth('world_001', '42', 80, 7)) 'Newer damage cannot be overwritten.'
Assert-True ($store.TryGetState('player', '42', [ref]$health) -and $health.Hp -eq 20) 'Newer combat HP must prevail.'
Write-Output 'PASS: item use HP applies only to matching local combat revision and scene.'
