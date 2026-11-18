$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$messages = Get-Content (Join-Path $root 'Assets/Scripts/NetWork/NetMessages.cs') -Raw
$entity = Get-Content (Join-Path $root 'Assets/Scripts/NetWork/EntityManager.cs') -Raw
$inbox = Get-Content (Join-Path $root 'Assets/Scripts/Combat/RemoteSkillCastEventInbox.cs') -Raw
$dto = [regex]::Match($messages,
    '(?s)\[Serializable\]\s*public class SkillCastEvent : NetMessageBase\s*\{.*?\n\}')
if (-not $dto.Success) { throw 'skill_cast_event DTO is missing.' }

$fields = @('castId', 'sceneId', 'casterId', 'skillId',
    'casterX', 'casterY', 'casterZ', 'casterRotY',
    'targetType', 'targetId', 'targetX', 'targetY', 'targetZ',
    'serverTick', 'serverTime', 'hitTick')
foreach ($field in $fields) {
    if ($dto.Value -notmatch "public \w+ $field;") {
        throw "Missing event field: $field"
    }
}
if (($entity -notmatch 'Register\("skill_cast_event", HandleSkillCastEvent\)') -or
    ($entity -notmatch 'Unregister\("skill_cast_event", HandleSkillCastEvent\)') -or
    ($entity -notmatch 'remoteSkillCastInbox\.BeginSession\(context\.ConnectionEpoch') -or
    ($entity -notmatch 'remoteSkillCastInbox\.EndSession\(connectionEpoch\)') -or
    ($entity -notmatch 'OnDisconnectedWithEpoch \+= HandleNetworkDisconnected') -or
    ($entity -notmatch 'OnDisconnectedWithEpoch -= HandleNetworkDisconnected') -or
    ($entity -notmatch 'remoteSkillCastInbox\.TryAccept\(message, epoch, connected,')) {
    throw 'Remote event routing or session lifecycle is not connected.'
}
if ($entity -match 'Register\("skill_cast_event", HandleSkillCastEvent\)(?s:.*?)Register\("skill_cast_started"') {
    throw 'Remote events must not enter the local skill pipeline.'
}

$source = @'
using System;
using System.Collections.Generic;
using System.Globalization;
public class NetMessageBase
{
    public string msgId;
    public string type;
}
'@ + "`n" + $dto.Value + "`n" +
    ($inbox -replace '(?m)^using [^\r\n]+;\r?\n', '')
Add-Type -TypeDefinition $source -IgnoreWarnings | Out-Null

function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
}

$json = @'
{"msgId":"skill_cast_event","type":"skill_cast_event","castId":"31e45513-9c3a-463b-9f27-39d36c7f6d55","sceneId":"world_001","casterId":"101","skillId":"fireball_01","casterX":10.5,"casterY":0,"casterZ":20.5,"casterRotY":0,"targetType":"monster","targetId":"monster_001","targetX":14.5,"targetY":0,"targetZ":20.5,"serverTick":100,"serverTime":1791360000000,"hitTick":103}
'@
$jsonOptions = [System.Text.Json.JsonSerializerOptions]::new()
$jsonOptions.IncludeFields = $true
$message = [System.Text.Json.JsonSerializer]::Deserialize[SkillCastEvent](
    $json, $jsonOptions)
Assert-True ($message.castId -eq '31e45513-9c3a-463b-9f27-39d36c7f6d55' -and
    $message.casterY -eq 0 -and $message.casterRotY -eq 0 -and
    $message.serverTime -eq 1791360000000) `
    'Wire fields, long time and legitimate zero coordinates must deserialize.'

$inboxState = [RemoteSkillCastEventInbox]::new()
$reason = ''
Assert-True (-not $inboxState.TryAccept($message, 7, $true, 1,
    [ref]$reason) -and $reason -eq 'inactive_session') `
    'Events received before entering the world must be ignored.'
$inboxState.BeginSession(7, 'world_001', 42)
Assert-True ($inboxState.TryAccept($message, 7, $true, 1,
    [ref]$reason) -and $reason -eq 'accepted') `
    'A current, valid observer event must be accepted.'
Assert-True (-not $inboxState.TryAccept($message, 7, $true, 2,
    [ref]$reason) -and $reason -eq 'duplicate_cast') `
    'The same castId must not play twice.'
$message.castId = 'second'
$message.sceneId = 'another_scene'
Assert-True (-not $inboxState.TryAccept($message, 7, $true, 3,
    [ref]$reason) -and $reason -eq 'invalid_event') `
    'Events from another scene must be rejected.'
$message.sceneId = 'world_001'
Assert-True (-not $inboxState.TryAccept($message, 8, $true, 3,
    [ref]$reason) -and $reason -eq 'inactive_session') `
    'Events from an old connection must be rejected.'
Assert-True (-not $inboxState.TryAccept($message, 7, $false, 3,
    [ref]$reason)) 'Disconnected clients must not accept events.'
$message.casterId = '42'
Assert-True (-not $inboxState.TryAccept($message, 7, $true, 3,
    [ref]$reason) -and $reason -eq 'local_caster') `
    'The local caster must not play its own observer event.'
$message.casterId = '101'
$message.targetX = [double]::NaN
Assert-True (-not $inboxState.TryAccept($message, 7, $true, 3,
    [ref]$reason) -and $reason -eq 'invalid_event') `
    'Non-finite world coordinates must be rejected.'
$message.targetX = 14.5
$inboxState.EndSession(7)
Assert-True (-not $inboxState.TryAccept($message, 7, $true, 3,
    [ref]$reason)) 'Disconnected sessions must be cleared.'
$inboxState.BeginSession(8, 'world_001', 42)
$message.castId = '31e45513-9c3a-463b-9f27-39d36c7f6d55'
Assert-True ($inboxState.TryAccept($message, 8, $true, 4,
    [ref]$reason)) 'A new session must not inherit previous deduplication state.'
$message.castId = 'late'
$message.type = 'skill_cast_started'
Assert-True (-not $inboxState.TryAccept($message, 8, $true, 5,
    [ref]$reason) -and $reason -eq 'invalid_event') `
    'The observer handler must not accept local cast-start messages.'
$message.type = 'skill_cast_event'
$message.castId = '31e45513-9c3a-463b-9f27-39d36c7f6d55'
Assert-True ($inboxState.TryAccept($message, 8, $true, 125,
    [ref]$reason)) 'Expired cast IDs must not be retained forever.'

Write-Output 'PASS: remote skill event protocol, session guard and deduplication.'
