$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$files = @(
    'Assets/Scripts/Chat/Protocol/WorldChatMessages.cs',
    'Assets/Scripts/Chat/Model/ChatSendSession.cs'
)
$sources = ($files | ForEach-Object {
    (Get-Content (Join-Path $root $_) -Raw) `
        -replace '(?m)^using [^\r\n]+;\r?\n', ''
}) -join "`n"
$stubs = @'
public class NetMessageBase
{
    public string msgId;
    public string type;
}
'@
Add-Type -TypeDefinition ("using System;`nusing System.Globalization;`n" +
    "using Game.Chat.Protocol;`n" + $sources + $stubs) `
    -IgnoreWarnings | Out-Null

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
$script:privateRequests =
    [System.Collections.Generic.List[Game.Chat.Protocol.PrivateChatSendRequest]]::new()
$privateTransmit = [Func[Game.Chat.Protocol.PrivateChatSendRequest,bool]]{
    param($request)
    $script:privateRequests.Add($request)
    return $true
}
$script:worldRequests =
    [System.Collections.Generic.List[Game.Chat.Protocol.WorldChatSendRequest]]::new()
$worldTransmit = [Func[Game.Chat.Protocol.WorldChatSendRequest,bool]]{
    param($request)
    $script:worldRequests.Add($request)
    return $true
}
$errorCode = ''
$send.Reset(201)
$send.EnterWorld(201, 1001)

Assert-True (-not $send.TrySendPrivate(201, 0, 'hello', 0,
    $privateTransmit, [ref]$errorCode) -and
    $errorCode -eq 'target_required' -and $send.NextSequence -eq 1) `
    'Private chat must reject a missing target without consuming a sequence.'
Assert-True (-not $send.TrySendPrivate(201, 1001, 'hello', 0,
    $privateTransmit, [ref]$errorCode) -and
    $errorCode -eq 'cannot_chat_self' -and $send.NextSequence -eq 1) `
    'Private chat must reject the local character as its own target.'
Assert-True ($send.TrySendPrivate(201, 1002, '  你好  ', 0,
    $privateTransmit, [ref]$errorCode) -and
    $privateRequests.Count -eq 1 -and
    $privateRequests[0].channel -eq 'private' -and
    $privateRequests[0].targetCharacterDbId -eq 1002 -and
    $privateRequests[0].content -eq '你好' -and
    $privateRequests[0].requestSequence -eq 1 -and
    $send.PendingChannel -eq 'private') `
    'Private chat must send the target ID and use the first shared chat sequence.'
Assert-True (-not $send.TrySendPrivate(201, 1002, 'again', 0.7,
    $privateTransmit, [ref]$errorCode) -and
    $errorCode -eq 'chat_send_pending') `
    'An in-flight private message must block a second send.'
Assert-True ($send.ReceiveResult(201, (New-Result 1 $true '' ''), 0.1) -and
    $send.PendingSequence -eq 0 -and
    $send.RemainingCooldown('private', 0.7) -gt 0) `
    'A private result must keep the private cooldown after clearing pending.'
Assert-True (-not $send.TrySendPrivate(201, 1002, 'again', 0.7,
    $privateTransmit, [ref]$errorCode) -and
    $errorCode -eq 'rate_limited') `
    'Private chat must enforce the 800 millisecond default cooldown.'
Assert-True ($send.TrySendWorld(201, 'world', 0.7, $worldTransmit,
    [ref]$errorCode) -and $worldRequests[0].requestSequence -eq 2) `
    'World and private chat must use one sequence while retaining separate cooldowns.'
$send.ReceiveResult(201, (New-Result 2 $true '' ''), 0.8) | Out-Null
Assert-True ($send.TrySendPrivate(201, 1002, 'offline', 0.9,
    $privateTransmit, [ref]$errorCode) -and
    $privateRequests[1].requestSequence -eq 3) `
    'Private chat can continue after a world result and use the next shared sequence.'
$offline = New-Result 3 $false 'target_offline' 'Target player is offline.'
Assert-True ($send.ReceiveResult(201, $offline, 1.0) -and
    $send.LastErrorCode -eq 'target_offline' -and
    $send.PendingSequence -eq 0) `
    'An explicit failure from an older server must still settle the request.'
$send.Reset(202)
$send.EnterWorld(202, 1001)
Assert-True ($send.TrySendPrivate(202, 1002, 'offline recipient', 0,
    $privateTransmit, [ref]$errorCode) -and
    $send.ReceiveResult(202, (New-Result 1 $true '' ''), 0.1) -and
    $send.LastErrorCode -eq $null -and $send.NextSequence -eq 2) `
    'A successful send to an offline role is accepted, not mistaken for a delivery failure.'

$send.Reset(203)
$send.EnterWorld(203, 1001)
Assert-True ($send.TrySendPrivate(203, 1002, 'later', 0,
    $privateTransmit, [ref]$errorCode) -and
    $send.NextSequence -eq 2) `
    'A new connection must restart the shared chat sequence.'

Write-Output 'PASS: private chat request fields, target validation, shared sequence, 800ms cooldown and errors.'
