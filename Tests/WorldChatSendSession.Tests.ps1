$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$files = @(
    'Assets/Scripts/Chat/Protocol/WorldChatMessages.cs',
    'Assets/Scripts/Chat/Model/ChatSendSession.cs'
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
Add-Type -TypeDefinition ("using System;`nusing System.Globalization;`nusing Game.Chat.Protocol;`n" + $sources + $stubs) -IgnoreWarnings | Out-Null

function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
}
function New-Result([long]$sequence, [bool]$success,
    [string]$errorCode, [string]$message) {
    return [Game.Chat.Protocol.WorldChatSendResult]@{
        msgId = 'chat_send_result'
        type = 'chat_send_result'
        requestSequence = $sequence
        success = $success
        errorCode = $errorCode
        message = $message
    }
}

$send = [Game.Chat.Model.ChatSendSession]::new()
$script:sent = [System.Collections.Generic.List[Game.Chat.Protocol.WorldChatSendRequest]]::new()
$transmit = [Func[Game.Chat.Protocol.WorldChatSendRequest,bool]]{
    param($request)
    $script:sent.Add($request)
    return $true
}
$errorCode = ''
$send.Reset(101)
Assert-True (-not $send.TrySendWorld(101, 'hello', 0, $transmit,
    [ref]$errorCode) -and $errorCode -eq 'player_not_in_world') `
    'World chat must be blocked before successful entry.'
$send.EnterWorld(101, 4)
Assert-True (-not $send.TrySendWorld(101, '    ', 0, $transmit,
    [ref]$errorCode) -and $errorCode -eq 'content_empty') `
    'Blank messages must be rejected locally.'
Assert-True (-not $send.TrySendWorld(101, ('😀' * 51), 0, $transmit,
    [ref]$errorCode) -and $errorCode -eq 'content_too_long' -and
    $send.NextSequence -eq 1) `
    'Message length must use UTF16 units without consuming a sequence.'
Assert-True ($send.TrySendWorld(101, '  你好  ', 0, $transmit,
    [ref]$errorCode) -and $script:sent.Count -eq 1 -and
    $script:sent[0].content -eq '你好' -and
    $script:sent[0].channel -eq 'world' -and
    $script:sent[0].requestSequence -eq 1 -and
    $send.PendingSequence -eq 1 -and $send.NextSequence -eq 2) `
    'A valid send must trim content and allocate the first shared chat sequence.'
Assert-True (-not $send.TrySendWorld(101, 'again', 6, $transmit,
    [ref]$errorCode) -and $errorCode -eq 'chat_send_pending') `
    'One in-flight chat request must block repeated submissions.'
Assert-True (-not $send.ReceiveResult(100, (New-Result 1 $true '' ''), 2) -and
    -not $send.ReceiveResult(101, (New-Result 2 $true '' ''), 2) -and
    $send.PendingSequence -eq 1) `
    'Old epochs and unmatched results must not settle an in-flight request.'
Assert-True ($send.ReceiveResult(101, (New-Result 1 $true '' ''), 2) -and
    $send.PendingSequence -eq 0 -and
    -not $send.TrySendWorld(101, 'again', 4.9, $transmit,
        [ref]$errorCode) -and $errorCode -eq 'rate_limited') `
    'A matched result clears pending but does not cancel local cooldown.'
Assert-True ($send.TrySendWorld(101, 'again', 5, $transmit,
    [ref]$errorCode) -and $script:sent[1].requestSequence -eq 2) `
    'A fresh operation after cooldown must consume the next chat sequence.'
$limited = New-Result 2 $false 'rate_limited' 'Chat is too frequent. retryAfterMs=3200'
$send.ReceiveResult(101, $limited, 5.1) | Out-Null
Assert-True ([Math]::Abs($send.RemainingCooldown(5.1) - 3.2) -lt 0.001 -and
    -not $send.TrySendWorld(101, 'later', 8.2, $transmit,
        [ref]$errorCode) -and $errorCode -eq 'rate_limited') `
    'Rate limit retryAfterMs must replace the local cooldown.'
Assert-True ($send.TrySendWorld(101, 'later', 8.3, $transmit,
    [ref]$errorCode) -and $script:sent[2].requestSequence -eq 3) `
    'Rejected business requests must still consume the chat sequence.'
$send.ReceiveResult(101, (New-Result 3 $false 'content_rejected' ''), 8.4) | Out-Null
Assert-True ($send.RemainingCooldown(8.4) -gt 4.8) `
    'Content rejection after server cooldown reservation must retain cooldown.'

$send.Reset(102)
$send.EnterWorld(102, 4)
$send.TrySendWorld(102, 'auth', 0, $transmit, [ref]$errorCode) | Out-Null
Assert-True ($send.ReceiveGateError(102, 'not_authenticated', 'login required') -and
    $send.NextSequence -eq 1 -and -not $send.IsInWorld -and
    -not $send.TrySendWorld(102, 'auth', 1, $transmit, [ref]$errorCode)) `
    'Gate authentication rejection must not consume a chat sequence.'
$send.EnterWorld(102, 4)
Assert-True ($send.TrySendWorld(102, 'auth', 1, $transmit,
    [ref]$errorCode) -and $script:sent[4].requestSequence -eq 1) `
    'Same-connection reentry must reuse the unconsumed sequence.'
$invalidRetry = New-Result 1 $false 'rate_limited' 'retryAfterMs=not-a-number'
$send.ReceiveResult(102, $invalidRetry, 1) | Out-Null
Assert-True ($send.RemainingCooldown(1) -eq 5) `
    'Invalid retryAfterMs must conservatively wait the local cooldown.'

$send.Reset(103)
$send.EnterWorld(103, 4)
$send.TrySendWorld(103, 'wait', 1, $transmit, [ref]$errorCode) | Out-Null
$send.CheckTimeout(103, 15.9)
Assert-True ($send.PendingSequence -eq 1) `
    'Result waiting must last until its deadline.'
$send.CheckTimeout(103, 16)
Assert-True ($send.DeliveryUnknown -and $send.PendingSequence -eq 0 -and
    -not $send.TrySendWorld(103, 'retry', 20, $transmit,
        [ref]$errorCode) -and $errorCode -eq 'chat_send_outcome_unknown') `
    'Result timeout must not resend or assume the server did not process the request.'
$send.Reset(104)
$send.EnterWorld(104, 4)
$failedTransport = [Func[Game.Chat.Protocol.WorldChatSendRequest,bool]]{
    param($request) return $false
}
Assert-True (-not $send.TrySendWorld(104, 'maybe sent', 0,
    $failedTransport, [ref]$errorCode) -and $send.DeliveryUnknown -and
    $send.NextSequence -eq 2) `
    'Failed transport return cannot prove a request was not delivered.'
$send.Reset(105)
$send.EnterWorld(105, 4)
Assert-True ($send.TrySendWorld(105, 'fresh', 0, $transmit,
    [ref]$errorCode) -and $script:sent[6].requestSequence -eq 1) `
    'A new connection must begin a new chat sequence.'

Write-Output 'PASS: world chat send validation, shared sequence, cooldown, result matching and uncertainty.'
