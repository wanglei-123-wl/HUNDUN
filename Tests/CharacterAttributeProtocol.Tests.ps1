$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$messages = Get-Content (Join-Path $root 'Assets/Scripts/NetWork/NetMessages.cs') -Raw
$session = Get-Content (Join-Path $root 'Assets/Scripts/GameSession.cs') -Raw
$scene = Get-Content (Join-Path $root 'Assets/Scenes/WorldShell.unity') -Raw

$dto = [regex]::Match($messages,
    '(?s)\[Serializable\]\s+public sealed class AttributeSnapshotRequest.*?(?=\[Serializable\]\s+public class PlayerNetworkState)')
if (-not $dto.Success) { throw 'Attribute snapshot protocol DTOs are missing.' }
$source = @'
using System;
public class NetMessageBase { public string msgId; public string type; }
'@ + "`n" + $dto.Value
Add-Type -TypeDefinition $source -IgnoreWarnings | Out-Null

$request = [AttributeSnapshotRequest]::new(1)
$json = [System.Text.Json.JsonSerializer]::Serialize($request,
    [System.Text.Json.JsonSerializerOptions]@{ IncludeFields = $true })
$parsed = $json | ConvertFrom-Json
if ($parsed.msgId -ne 'attribute_snapshot' -or
    $parsed.type -ne 'attribute_snapshot' -or
    $parsed.requestSequence -ne 1 -or
    @($parsed.PSObject.Properties).Count -ne 3) {
    throw "Attribute snapshot request JSON must match the Gate protocol: $json"
}
$response = '{"requestSequence":1,"success":true,"characterDbId":42,"attributeRevision":10,"combatRevision":25,"final":{"maxHp":1090,"attack":110,"defense":18,"critRateBps":500,"critDamageBps":15000,"magicAttack":75,"magicDefense":32}}'
$result = [System.Text.Json.JsonSerializer]::Deserialize[AttributeSnapshotResult](
    $response, [System.Text.Json.JsonSerializerOptions]@{ IncludeFields = $true })
if ($result.final.maxHp -ne 1090 -or $result.final.attack -ne 110 -or
    $result.final.defense -ne 18 -or $result.final.critRateBps -ne 500 -or
    $result.final.critDamageBps -ne 15000 -or
    $result.final.magicAttack -ne 75 -or $result.final.magicDefense -ne 32) {
    throw 'Attribute snapshot final fields must deserialize from the response.'
}
$inventoryModule = Get-Content (Join-Path $root 'Assets/Scripts/Inventory/Integration/InventoryModuleRuntime.cs') -Raw
if ($inventoryModule -notmatch 'QueueAttributeSnapshotRefresh\(\)' -or
    $inventoryModule -notmatch 'QueueProgressionSnapshotRefresh\(\)') {
    throw 'Successful equipment writes must refresh final combat stats and progression MP.'
}
if ($session -notmatch 'Register\("attribute_snapshot_result", HandleAttributeSnapshotResult\)' -or
    $session -notmatch 'result.requestSequence != pendingAttributeSnapshotSequence' -or
    $session -notmatch 'result.characterDbId != localCharacterDbId' -or
    $session -notmatch '!result.success \|\| result.final == null' -or
    $session -notmatch 'RequestAttributeSnapshot\(\);\s+RequestProgressionSnapshot\(\);\s+if \(enterWorldConfirmed\)') {
    throw 'The world session must request, validate and cache the successful result.'
}
if ($scene -notmatch '(?s)guid: c58c60ffbd7741429aeec66f2da79d4d, type: 3\}(?:(?!--- !u!).)*attack: \{fileID: 1388168625\}\s+defense: \{fileID: 324513067\}(?:(?!--- !u!).)*critRate: \{fileID: 435721324\}\s+critDamage: \{fileID: 313718223\}') {
    throw 'Attribute text references are not bound to the correct objects.'
}
Write-Output 'PASS: attribute snapshot request JSON, final fields, session guards and scene bindings.'
