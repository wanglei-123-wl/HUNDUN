$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$snapshotSource = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/NetWork/BattleSync/Protocol/SnapshotMessages.cs') -Raw
$managerSource = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/Monster/Network/MonsterSnapshotManager.cs') -Raw
$coordinatorSource = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/NetWork/BattleSync/Core/BattleSyncCoordinator.cs') -Raw
$constants = @'
namespace Game.Client.BattleSync.Protocol
{
    public static class BattleSyncProtocolConstants
    {
        public static class MessageIds
        {
            public const string WorldSnapshot = "world_snapshot";
            public const string MonsterSnapshot = "monster_snapshot";
            public const string LootSnapshot = "loot_snapshot";
            public const string CombatSnapshot = "combat_snapshot";
            public const string InventorySnapshotResult = "inventory_snapshot_result";
        }
    }
}
'@
Add-Type -TypeDefinition ($snapshotSource + "`n" + $constants) -ErrorAction Stop

$message = [Game.Client.BattleSync.Protocol.MonsterSnapshotMessageDto]::new()
$message.msgId = 'monster_snapshot'
$message.sceneId = 'world_001'
$message.serverTick = 1
$message.serverTime = 1
$message.snapshotRevision = 1
$message.reason = 'repair'
$monster = [Game.Client.BattleSync.Protocol.MonsterStateDto]::new()
$monster.netId = 501
$monster.monsterInstanceId = ' mon_sp42_000001 '
$monster.sceneId = 'world_001'
$monster.monsterId = 'Ls003'
$monster.prefabId = 'AnotherMonster'
$message.monsters = @($monster)
$errorText = ''
if (-not [Game.Client.BattleSync.Protocol.BattleSyncSnapshotValidator]::TryValidate($message, [ref]$errorText)) {
    throw "monsterId must validate even when prefabId points elsewhere: $errorText"
}
if ($monster.monsterInstanceId -ne ' mon_sp42_000001 ') {
    throw 'The instance ID must not be trimmed or parsed.'
}
$monster.prefabId = 'Ls003'
$monster.monsterId = ''
if ([Game.Client.BattleSync.Protocol.BattleSyncSnapshotValidator]::TryValidate($message, [ref]$errorText)) {
    throw 'A prefabId-only snapshot must not validate.'
}
if (-not $coordinatorSource.Contains('!string.IsNullOrWhiteSpace(m.monster.monsterId)') -or
    $coordinatorSource.Contains('!string.IsNullOrWhiteSpace(m.monster.prefabId)') -or
    -not $managerSource.Contains('FindPrefabById(item.monsterId)') -or
    $managerSource.Contains('FindPrefabById(item.prefabId)') -or
    -not $managerSource.Contains('CreateMissingMonster(position, rotation)') -or
    -not $managerSource.Contains('root.AddComponent<MonsterNetworkView>()') -or
    -not $managerSource.Contains('collider.enabled = false')) {
    throw 'Monster enter validation or missing-resource presentation is not wired to monsterId.'
}
Write-Output 'PASS: monsterId authority, raw instance ID, and missing-resource presentation wiring.'
