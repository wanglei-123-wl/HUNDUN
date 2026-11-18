$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$messages = Get-Content (Join-Path $root 'Assets/Scripts/NetWork/NetMessages.cs') -Raw
$session = Get-Content (Join-Path $root 'Assets/Scripts/GameSession.cs') -Raw

$entry = [regex]::Match($messages,
    '(?s)\[Serializable\]\s+public class EnterWorldResult.*?(?=\[Serializable\]\s+public sealed class AttributeSnapshotRequest)')
if (-not $entry.Success) {
    throw 'Enter-world result DTO is missing.'
}
$stubs = @'
using System;
using UnityEngine;
public class NetMessageBase { public string msgId; public string type; }
public class PlayerNetworkState {}
namespace UnityEngine {
    public struct Vector3 { public Vector3(float x, float y, float z) {} }
    public struct Quaternion { public static Quaternion Euler(float x, float y, float z) => new Quaternion(); }
}
'@
Add-Type -TypeDefinition ($stubs + "`n" + $entry.Value) -IgnoreWarnings | Out-Null

$options = [System.Text.Json.JsonSerializerOptions]@{ IncludeFields = $true }
$id = [Guid]::NewGuid().ToString('D')
$accepted = [System.Text.Json.JsonSerializer]::Deserialize[EnterWorldResult](
    '{"success":true,"characterDbId":42,"alchemyProtocolVersion":1,"alchemyOperationSessionId":"' + $id + '"}',
    $options)
if ($accepted.alchemyProtocolVersion -ne 1 -or
    $accepted.alchemyOperationSessionId -ne $id) {
    throw 'Enter-world DTO must retain the dedicated alchemy version and session.'
}
$withoutAlchemy = [System.Text.Json.JsonSerializer]::Deserialize[EnterWorldResult](
    '{"success":true,"characterDbId":42}', $options)
if ($withoutAlchemy.alchemyProtocolVersion -ne 0 -or
    $withoutAlchemy.alchemyOperationSessionId) {
    throw 'Older enter-world replies must remain readable without alchemy fields.'
}
if ([regex]::Matches($session, 'AlchemyOperationSessionId = string\.Empty;').Count -lt 6 -or
    $session -notmatch
        'AlchemyOperationSessionId = result\.alchemyOperationSessionId \?\? string\.Empty;' -or
    $session.IndexOf('AlchemyOperationSessionId = result.alchemyOperationSessionId') -lt
        $session.IndexOf('if (resultCharacterDbId <= 0)')) {
    throw 'Alchemy session must be cleared on exit/recovery and saved only for an accepted character.'
}

Write-Output 'PASS: alchemy enter-world fields and session lifecycle.'
