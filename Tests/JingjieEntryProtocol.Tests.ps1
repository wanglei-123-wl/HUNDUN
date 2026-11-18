$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$messages = Get-Content (Join-Path $root 'Assets/Scripts/NetWork/NetMessages.cs') -Raw
$session = Get-Content (Join-Path $root 'Assets/Scripts/GameSession.cs') -Raw
$item = Get-Content (Join-Path $root 'Assets/掉落物品配置/灵石.asset') -Raw
$table = Get-Content (Join-Path $root 'Assets/Data/Items/ItemDefinitionTable.asset') -Raw
$itemMeta = Get-Content (Join-Path $root 'Assets/掉落物品配置/灵石.asset.meta') -Raw

if ($item -notmatch 'ItemId: ling_shi' -or
    $item -notmatch 'DisplayName: "\\u7075\\u77F3"' -or
    $item -notmatch 'Icon: \{fileID: 21300000, guid: [0-9a-f]+, type: 3\}') {
    throw 'The registered spirit stone must retain its ID and icon and show the name 灵石.'
}
$guid = [regex]::Match($itemMeta, '(?m)^guid: ([0-9a-f]+)$').Groups[1].Value
if (-not $guid -or -not $table.Contains("guid: $guid")) {
    throw 'The spirit stone must remain registered in the current item definition table.'
}

$entry = [regex]::Match($messages,
    '(?s)\[Serializable\]\s+public class EnterWorldResult.*?(?=\[Serializable\]\s+public sealed class AttributeSnapshotRequest)')
$snapshot = [regex]::Match($messages,
    '(?s)\[Serializable\]\s+public sealed class ProgressionSnapshotRequest.*?(?=\[Serializable\]\s+public sealed class SkillSnapshotResult)')
if (-not $entry.Success -or -not $snapshot.Success) {
    throw 'Enter-world or progression snapshot DTOs are missing.'
}
$source = @'
using System;
using UnityEngine;
public class NetMessageBase { public string msgId; public string type; }
public class PlayerNetworkState {}
namespace UnityEngine {
    public struct Vector3 { public Vector3(float x, float y, float z) {} }
    public struct Quaternion { public static Quaternion Euler(float x, float y, float z) => new Quaternion(); }
}
'@ + "`n" + $entry.Value + "`n" + $snapshot.Value
Add-Type -TypeDefinition $source -IgnoreWarnings | Out-Null

$options = [System.Text.Json.JsonSerializerOptions]@{ IncludeFields = $true }
$id = [Guid]::NewGuid().ToString('D')
$accepted = [System.Text.Json.JsonSerializer]::Deserialize[EnterWorldResult](
    '{"success":true,"characterDbId":42,"jingjieOperationSessionId":"' + $id + '"}',
    $options)
if ($accepted.jingjieOperationSessionId -ne $id) {
    throw 'Enter-world result must read the jingjie operation session ID.'
}
$realm = [System.Text.Json.JsonSerializer]::Deserialize[ProgressionSnapshotResult](
    '{"success":true,"characterDbId":42,"currentMp":8,"maxMp":10,"jingjieId":"sample_id","jingjieStage":1,"jingjieName":"sample_name","hasNextJingjie":true,"nextJingjieId":"sample_id","nextJingjieStage":2,"nextJingjieName":"sample_next","jingjieExp":150,"requiredJingjieExp":100,"totalJingjieExp":150}',
    $options)
if ($realm.jingjieId -ne 'sample_id' -or $realm.jingjieStage -ne 1 -or
    $realm.jingjieName -ne 'sample_name' -or $realm.currentMp -ne 8 -or $realm.maxMp -ne 10 -or
    -not $realm.hasNextJingjie -or $realm.nextJingjieId -ne 'sample_id' -or
    $realm.nextJingjieStage -ne 2 -or $realm.nextJingjieName -ne 'sample_next' -or
    $realm.jingjieExp -ne 150 -or $realm.requiredJingjieExp -ne 100 -or
    $realm.totalJingjieExp -ne 150) {
    throw 'A snapshot without level fields must retain realm and mana data.'
}
$noNext = [System.Text.Json.JsonSerializer]::Deserialize[ProgressionSnapshotResult](
    '{"success":true,"characterDbId":42,"jingjieName":"sample_name","hasNextJingjie":false,"nextJingjieId":"","nextJingjieStage":0,"nextJingjieName":""}',
    $options)
if ($noNext.hasNextJingjie -or $noNext.nextJingjieId -ne '' -or
    $noNext.nextJingjieStage -ne 0 -or $noNext.nextJingjieName -ne '') {
    throw 'A snapshot without a next path must retain the server-defined empty fields.'
}
$legacy = [System.Text.Json.JsonSerializer]::Deserialize[ProgressionSnapshotResult](
    '{"success":true,"characterDbId":42,"level":2,"exp":7,"jingjieName":"sample_name"}', $options)
if ($legacy.jingjieName -ne 'sample_name' -or
    [ProgressionSnapshotResult].GetField('level') -or
    [ProgressionSnapshotResult].GetField('exp') -or
    $session -match '"progression_delta"') {
    throw 'The client must keep realm data and retire level fields and the level update message.'
}

$clears = [regex]::Matches($session, 'JingjieOperationSessionId = string\.Empty;').Count
if ($clears -lt 6 -or
    $session -notmatch 'JingjieOperationSessionId = result\.jingjieOperationSessionId \?\? string\.Empty;' -or
    $session.IndexOf('JingjieOperationSessionId = result.jingjieOperationSessionId') -lt
        $session.IndexOf('if (resultCharacterDbId <= 0)')) {
    throw 'Jingjie operation session must be cleared on exit/recovery and saved only for an accepted character.'
}
Write-Output 'PASS: spirit stone config, realm snapshot fields and jingjie operation session lifecycle.'
