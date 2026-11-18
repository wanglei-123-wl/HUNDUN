$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$files = @(
    'Assets/Scripts/Chat/Protocol/WorldChatMessages.cs',
    'Assets/Scripts/Chat/Model/WorldChatTimeline.cs'
)
$sources = ($files | ForEach-Object {
    (Get-Content (Join-Path $root $_) -Raw) -replace '(?m)^using [^\r\n]+;\r?\n', ''
}) -join "`n"
$stubs = @'
public class NetMessageBase
{
    public string msgId;
    public string type;
}
'@
Add-Type -TypeDefinition ("using System;`nusing System.Collections.Generic;`nusing Game.Chat.Protocol;`n" + $sources + $stubs) -IgnoreWarnings | Out-Null

function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
}
function New-Message([string]$id, [long]$time) {
    return [Game.Chat.Protocol.WorldChatMessage]@{
        msgId = 'chat_message'
        type = 'chat_message'
        channel = 'world'
        messageId = $id
        serverTime = $time
    }
}
function New-Batch([int]$index, [int]$total, [bool]$last,
    [Game.Chat.Protocol.WorldChatMessage[]]$items) {
    return [Game.Chat.Protocol.WorldChatHistory]@{
        msgId = 'world_chat_history'
        type = 'world_chat_history'
        syncId = 'sync-a'
        snapshotTime = [long]5000
        batchIndex = $index
        totalCount = $total
        messages = $items
        isLast = $last
        success = $true
    }
}

$timeline = [Game.Chat.Model.WorldChatTimeline]::new()
$timeline.Reset(11)
$timeline.ReceiveRealtime(11, (New-Message 'early' 4000))
$timeline.EnterWorld(11, 0, 5)
$timeline.ReceiveHistory(11, (New-Batch 1 0 $true @()))
Assert-True ($timeline.Status -eq [Game.Chat.Model.WorldChatHistoryStatus]::Complete -and
    $timeline.ReceivedHistoryCount -eq 0 -and
    $timeline.MessagesNewestFirst.Count -eq 1 -and
    $timeline.MessagesNewestFirst[0].messageId -eq 'early') `
    'Empty history must complete and release realtime buffered before world entry.'

$timeline.Reset(12)
$timeline.EnterWorld(12, 0, 5)
$timeline.ReceiveRealtime(12, (New-Message 'overlap' 100))
$timeline.ReceiveRealtime(12, (New-Message 'newer' 101))
$timeline.ReceiveHistory(12, (New-Batch 1 3 $false @(
    (New-Message 'same-time-a' 100), (New-Message 'same-time-b' 100))))
$timeline.ReceiveHistory(12, (New-Batch 1 3 $false @(
    (New-Message 'same-time-a' 100), (New-Message 'same-time-b' 100))))
Assert-True ($timeline.ReceivedHistoryCount -eq 2 -and
    $timeline.Status -eq [Game.Chat.Model.WorldChatHistoryStatus]::Receiving -and
    $timeline.MessagesNewestFirst.Count -eq 0) `
    'Repeated batches must not count twice or release realtime early.'
$timeline.ReceiveHistory(12, (New-Batch 2 3 $true @(
    (New-Message 'overlap' 99))))
$ids = @($timeline.MessagesNewestFirst | ForEach-Object { $_.messageId })
Assert-True ($timeline.Status -eq [Game.Chat.Model.WorldChatHistoryStatus]::Complete -and
    $timeline.ReceivedHistoryCount -eq 3 -and
    (($ids -join ',') -eq 'newer,same-time-a,same-time-b,overlap')) `
    'History order and same-millisecond order must survive realtime overlap.'
$timeline.ReceiveRealtime(12, (New-Message 'overlap' 200))
$timeline.ReceiveHistory(12, (New-Batch 2 3 $true @()))
Assert-True ($timeline.MessagesNewestFirst.Count -eq 4 -and
    $timeline.MessagesNewestFirst[3].serverTime -eq 99) `
    'Late duplicate history or realtime must not change the displayed messages.'

$timeline.Reset(13)
$timeline.EnterWorld(13, 0, 5)
$timeline.ReceiveHistory(13, (New-Batch 2 1 $true @((New-Message 'gap' 1))))
Assert-True ($timeline.Status -eq [Game.Chat.Model.WorldChatHistoryStatus]::Incomplete -and
    $timeline.ErrorCode -eq 'world_chat_history_batch_gap') `
    'First batch must start at one.'

$timeline.Reset(14)
$timeline.EnterWorld(14, 0, 5)
$timeline.ReceiveHistory(14, (New-Batch 1 2 $false @((New-Message 'part' 10))))
$conflict = New-Batch 2 2 $true @((New-Message 'part-2' 9))
$conflict.syncId = 'other-sync'
$timeline.ReceiveRealtime(14, (New-Message 'live' 11))
$timeline.ReceiveHistory(14, $conflict)
Assert-True ($timeline.Status -eq [Game.Chat.Model.WorldChatHistoryStatus]::Incomplete -and
    $timeline.ErrorCode -eq 'world_chat_history_mismatch' -and
    $timeline.ReceivedHistoryCount -eq 1 -and
    (($timeline.MessagesNewestFirst | ForEach-Object { $_.messageId }) -join ',') -eq 'live,part') `
    'Conflicting metadata must mark history incomplete and release partial history plus realtime.'

$timeline.Reset(15)
$timeline.EnterWorld(15, 0, 5)
$timeline.ReceiveHistory(15, (New-Batch 1 2 $true @(
    (New-Message 'duplicate' 1), (New-Message 'duplicate' 1))))
Assert-True ($timeline.Status -eq [Game.Chat.Model.WorldChatHistoryStatus]::Incomplete -and
    $timeline.ReceivedHistoryCount -eq 0) `
    'Duplicate IDs inside history must not count toward the snapshot.'

$timeline.Reset(16)
$timeline.EnterWorld(16, 10, 5)
$timeline.ReceiveRealtime(16, (New-Message 'timeout-live' 15))
$timeline.CheckTimeout(16, 14.9)
Assert-True ($timeline.Status -eq [Game.Chat.Model.WorldChatHistoryStatus]::Receiving) `
    'Receiving before the deadline must keep buffering.'
$timeline.CheckTimeout(16, 15)
Assert-True ($timeline.Status -eq [Game.Chat.Model.WorldChatHistoryStatus]::Incomplete -and
    $timeline.ErrorCode -eq 'world_chat_history_timeout' -and
    $timeline.MessagesNewestFirst.Count -eq 1) `
    'History timeout must not block realtime delivery.'
$timeline.Reset(17)
$timeline.ReceiveRealtime(16, (New-Message 'stale' 99))
$timeline.ReceiveHistory(16, (New-Batch 1 0 $true @()))
$timeline.EnterWorld(16, 0, 5)
$timeline.EnterWorld(17, 0, 5)
$timeline.ReceiveHistory(17, (New-Batch 1 0 $true @()))
Assert-True ($timeline.ConnectionEpoch -eq 17 -and
    $timeline.Status -eq [Game.Chat.Model.WorldChatHistoryStatus]::Complete -and
    $timeline.MessagesNewestFirst.Count -eq 0) `
    'Old connection callbacks must not leak into a new session.'

Write-Output 'PASS: world chat history, realtime merge, deduplication, failures and connection isolation.'
