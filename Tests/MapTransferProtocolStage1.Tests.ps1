$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$messages = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/NetWork/NetMessages.cs') -Raw
$network = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/NetWork/NetworkManager.cs') -Raw

function Extract([string]$text, [string]$pattern) {
    $match = [regex]::Match($text, $pattern)
    if (-not $match.Success) { throw "Source block not found: $pattern" }
    return $match.Value
}

$inputDtos = Extract $messages '(?s)\[Serializable\]\s*public class AttackModeSetRequest.*?(?=\[Serializable\]\s*public class SkillCastStarted)'
$respawnDto = Extract $messages '(?s)\[Serializable\]\s*public class RespawnRequest.*?(?=\[Serializable\]\s*public sealed class MapTransferOptionsRequest)'
$transferDtos = Extract $messages '(?s)\[Serializable\]\s*public sealed class MapTransferOptionsRequest.*?(?=\[Serializable\]\s*public class ServerErrorMessage)'
$send = Extract $network '(?s)    public bool Send<T>\(T message\)\s*\{.*?\r?\n    \}(?=\r?\n\r?\n    public bool SetSceneSessionId)'
$set = Extract $network '(?s)    public bool SetSceneSessionId\(long epoch, string value\)\s*\{.*?\r?\n    \}(?=\r?\n\r?\n    public bool SetSceneOperationsPaused)'
$pause = Extract $network '(?s)    public bool SetSceneOperationsPaused\(long epoch, bool paused\)\s*\{.*?\r?\n    \}(?=\r?\n\r?\n    internal bool TryFlushMovement)'
$getter = Extract $network '(?s)    public string SceneSessionId => IsConnected.*?sceneSessionId : null;'

if ($messages -notmatch '(?s)class EnterWorldResult : NetMessageBase\s*\{.*?public string sceneSessionId;' -or
    (Get-Content (Join-Path $root 'Assets/Scripts/Loot/Protocol/LootMessageDtos.cs') -Raw) -notmatch
        'class LootPickupMessageDto : MovementReferencedRequest' -or
    (Get-Content (Join-Path $root 'Assets/Scripts/Inventory/Protocol/V2/InventoryV2Messages.cs') -Raw) -notmatch
        '(?s)class InventoryV2ItemUseRequestDto : NetMessageBase\s*\{\s*public string sceneSessionId;' -or
    (Get-Content (Join-Path $root 'Assets/Scripts/NetWork/BattleSync/Protocol/SnapshotRepairMessages.cs') -Raw) -notmatch
        'public string sceneSessionId;') {
    throw 'A scene request DTO is missing sceneSessionId.'
}

$stubs = @'
using System;
using System.Text.Json;
using UnityEngine;
public class NetMessageBase { public string msgId; public string type; }
public class PlayerNetworkState { public string characterId; }
public class MovementEventControl {}
public class MovementReferencedRequest : NetMessageBase {
    public string sceneSessionId;
    public long movementEpoch, movementSequence;
}
public sealed class LootPickupMessageDto : NetMessageBase { public string sceneSessionId; public long requestSequence; }
namespace Game.Inventory.Protocol.V2 {
    public sealed class InventoryV2ItemUseRequestDto : NetMessageBase {
        public string sceneSessionId; public string itemUseOperationSessionId; public long requestSequence;
    }
    public sealed class InventoryV2WorldDropRequestDto : NetMessageBase {
        public string sceneSessionId; public string worldDropOperationSessionId; public long requestSequence;
    }
    public sealed class InventoryV2StackWorldDropRequestDto : MovementReferencedRequest {
        public string worldDropOperationSessionId; public long requestSequence;
    }
}
namespace UnityEngine {
    public static class JsonUtility {
        public static string ToJson(object value) =>
            JsonSerializer.Serialize(value, new JsonSerializerOptions { IncludeFields = true });
    }
}
public sealed class NetworkManager {
    public bool IsConnected = true;
    public long ConnectionEpoch = 7;
    private long sceneSessionEpoch;
    private string sceneSessionId;
    private bool sceneOperationsPaused;
    private bool localPlayerActionsBlocked;
    public string LastJson;
    private long movementEpoch, movementSequence;
    public bool TryFlushMovement() => movementEpoch > 0 && SceneSessionId != null;
    private void ResetMovementSession() { movementEpoch = movementSequence = 0; }
    public void AckMovement(long epoch, long sequence) { movementEpoch = epoch; movementSequence = sequence; }
    public bool SendJson(string json) { LastJson = json; return IsConnected; }
__GETTER__
__SEND__
__SET__
__PAUSE__
}
'@
$source = $stubs.Replace('__GETTER__', $getter).Replace('__SEND__', $send).
    Replace('__SET__', $set).Replace('__PAUSE__', $pause) +
    "`n" + $inputDtos + $respawnDto + $transferDtos
Add-Type -TypeDefinition $source -IgnoreWarnings | Out-Null

function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
}

$manager = [NetworkManager]::new()
$move = [CombatActionRequest]::new()
$move.actionSequence = 19
Assert-True (-not $manager.Send($move)) 'World input must not send without a server scene session.'
Assert-True (-not $manager.SetSceneSessionId(6, 'old')) 'An older connection cannot set the scene session.'
Assert-True ($manager.SetSceneSessionId(7, 'server-session-a')) 'The current Gate session must be stored.'
Assert-True (-not $manager.Send($move)) 'Position operations require movement control confirmation.'
$manager.AckMovement(4, 12)
Assert-True ($manager.Send($move)) 'World operation should send after movement control confirmation.'
$json = $manager.LastJson | ConvertFrom-Json -AsHashtable
Assert-True ($json.sceneSessionId -eq 'server-session-a' -and $json.actionSequence -eq 19 -and $json.movementEpoch -eq 4 -and $json.movementSequence -eq 12) `
    'Input must carry the current scene session without changing its sequence.'

$item = [Game.Inventory.Protocol.V2.InventoryV2ItemUseRequestDto]::new()
$item.itemUseOperationSessionId = 'business-session'
$item.requestSequence = 23
Assert-True ($manager.Send($item)) 'Item use should send with the scene session.'
$json = $manager.LastJson | ConvertFrom-Json -AsHashtable
Assert-True ($json.sceneSessionId -eq 'server-session-a' -and
    $json.itemUseOperationSessionId -eq 'business-session' -and $json.requestSequence -eq 23) `
    'Scene stamping must not reset business sessions or their sequence.'

$stackDrop = [Game.Inventory.Protocol.V2.InventoryV2StackWorldDropRequestDto]::new()
$stackDrop.worldDropOperationSessionId = 'world-drop-session'
$stackDrop.requestSequence = 24
Assert-True ($manager.Send($stackDrop)) 'Stack world drop requires the active scene.'
$json = $manager.LastJson | ConvertFrom-Json -AsHashtable
Assert-True ($json.worldDropOperationSessionId -eq 'world-drop-session' -and
    $json.requestSequence -eq 24 -and $json.sceneSessionId -eq 'server-session-a' -and $json.movementSequence -eq 12) `
    'Stack world drop must reference the current movement state.'

Assert-True ($manager.SetSceneOperationsPaused(7, $true) -and
    -not $manager.Send($move) -and -not $manager.Send($item) -and
    -not $manager.Send($stackDrop)) `
    'Scene input, item use and world drop must stop during the map transfer.'
Assert-True ($manager.SetSceneOperationsPaused(7, $false) -and $manager.Send($move)) `
    'Failed transfer must resume scene requests with the same sequence.'

$options = [MapTransferOptionsRequest]::new()
Assert-True ($manager.Send($options)) 'Menu route query should use the current scene session.'
$json = $manager.LastJson | ConvertFrom-Json -AsHashtable
Assert-True ($json.msgId -eq 'map_transfer_options' -and
    $json.sceneSessionId -eq 'server-session-a' -and
    -not $json.ContainsKey('queryId') -and
    -not $json.ContainsKey('sourcePointId')) 'Route query must use the legacy payload.'

$request = [MapTransferRequest]::new()
$request.requestId = [Guid]::NewGuid().ToString('D')
$request.routeId = 'village_to_demo1'
Assert-True ($manager.Send($request)) 'The first transfer request uses the current movement state.'
$manager.AckMovement(8, 22)
Assert-True ($manager.Send($request)) 'An existing transfer retry preserves the original movement reference.'
$json = $manager.LastJson | ConvertFrom-Json -AsHashtable
Assert-True ($json.sceneSessionId -eq 'server-session-a' -and
    $json.routeId -eq 'village_to_demo1' -and $json.movementEpoch -eq 4 -and $json.movementSequence -eq 12 -and
    -not $json.ContainsKey('sourcePointId')) `
    'A retry must retain its original source session and legacy payload.'

$ready = [MapTransferReadyRequest]::new()
$ready.transferId = [Guid]::NewGuid().ToString('D')
Assert-True (-not $manager.Send($ready)) 'Ready requests need their explicit source session.'
$ready.sceneSessionId = 'server-session-a'
$ready.loaded = $true
Assert-True ($manager.Send($ready)) 'Ready requests should send with the source session.'
$json = $manager.LastJson | ConvertFrom-Json -AsHashtable
Assert-True ($json.msgId -eq 'map_transfer_ready' -and
    $json.sceneSessionId -eq 'server-session-a' -and $json.loaded) `
    'Ready payload must keep its source generation and load result.'

$manager.ConnectionEpoch = 8
Assert-True ($null -eq $manager.SceneSessionId -and
    -not $manager.Send($move) -and -not $manager.Send($stackDrop)) `
    'A reconnected Gate must not reuse the old scene session.'
$manager.IsConnected = $false
Assert-True (-not $manager.SetSceneSessionId(8, 'disconnected')) `
    'A disconnected Gate must not accept a scene session.'

Write-Output 'PASS: scene session stamping, business sequence, transfer DTOs and connection isolation.'
