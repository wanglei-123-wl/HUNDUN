$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$sources = @(
    'Assets/Scripts/Chat/Protocol/WorldChatMessages.cs',
    'Assets/Scripts/Chat/Model/PrivateChatAckQueue.cs'
) | ForEach-Object {
    (Get-Content -LiteralPath (Join-Path $root $_) -Raw) `
        -replace '(?m)^using [^\r\n]+;\r?\n', ''
}
$stub = 'public class NetMessageBase { public string msgId; public string type; }'
Add-Type -TypeDefinition ("using System;`nusing System.Collections.Generic;`n" +
    "using Game.Chat.Protocol;`n" + $stub + ($sources -join "`n")) `
    -IgnoreWarnings -WarningAction SilentlyContinue | Out-Null
function Check([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}
function New-Message([string]$id, [long]$sender, [long]$target,
    [string]$channel = 'private') {
    return [Game.Chat.Protocol.WorldChatMessage]@{
        msgId = 'chat_message'
        type = 'chat_message'
        channel = $channel
        messageId = $id
        senderCharacterDbId = $sender
        targetCharacterDbId = $target
    }
}

$queue = [Game.Chat.Model.PrivateChatAckQueue]::new()
$queue.Reset(71)
Check ($queue.EnterWorld(71, 1001)) 'ACK requires a confirmed local character.'
$id = [Guid]::NewGuid().ToString('D')
Check (-not $queue.QueueReceived(71, (New-Message $id 1001 1002)) -and
    -not $queue.QueueReceived(71, (New-Message $id 1002 1003)) -and
    -not $queue.QueueReceived(71, (New-Message $id 1002 1001 'world')) -and
    -not $queue.QueueReceived(71, (New-Message 'not-a-uuid' 1002 1001)) -and
    -not $queue.QueueReceived(70, (New-Message $id 1002 1001)) -and
    $queue.PendingCount -eq 0) `
    'Own echo, foreign target, non-private, invalid UUID and old epoch never ACK.'
Check ($queue.QueueReceived(71, (New-Message $id 1002 1001)) -and
    $queue.QueueReceived(71, (New-Message $id 1002 1001)) -and
    $queue.PendingCount -eq 1) 'Repeated IDs must merge while pending.'

foreach ($index in 1..204) {
    $nextId = [Guid]::NewGuid().ToString('D')
    Check ($queue.QueueReceived(71, (New-Message $nextId 1002 1001))) `
        'The ACK queue must accept a multi-batch delivery.'
}
$sizes = [System.Collections.Generic.List[int]]::new()
$batch = $null
while ($queue.TryGetBatch(71, [ref]$batch)) {
    $sizes.Add($batch.Length)
    Check ($queue.MarkSent(71, $batch)) 'Sent ACKs must retire only their batch.'
}
Check ($sizes.Count -eq 3 -and $sizes[0] -eq 100 -and
    $sizes[1] -eq 100 -and $sizes[2] -eq 5 -and
    $queue.PendingCount -eq 0) `
    'Backlog must be sent in bounded batches without losing later IDs.'
Check ($queue.RetryRecent(71) -and $queue.PendingCount -eq 205) `
    'A server ACK error must restore recently attempted batches without duplicates.'
Check ($queue.TryGetBatch(71, [ref]$batch) -and
    $queue.MarkSent(71, $batch) -and
    $queue.QueueReceived(71, (New-Message $batch[0] 1002 1001)) -and
    $queue.PendingCount -eq 106) `
    'Redelivery after ACK transmission must allow a fresh ACK.'
$queue.Reset(72)
Check (-not $queue.TryGetBatch(71, [ref]$batch) -and
    -not $queue.QueueReceived(71, (New-Message $id 1002 1001)) -and
    $queue.PendingCount -eq 0 -and
    $queue.EnterWorld(72, 1003) -and
    -not $queue.QueueReceived(72, (New-Message $id 1002 1001))) `
    'Reconnect and role switch must not send acknowledgements from an old identity.'
Write-Output 'PASS: private ACK identity, UUID validation, batches, retries and epoch isolation.'
