$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$files = @(
    'Assets/Scripts/Chat/Protocol/WorldChatMessages.cs',
    'Assets/Scripts/Chat/Model/IPrivateChatMessageStorage.cs',
    'Assets/Scripts/Chat/Model/PrivateChatConversationStore.cs'
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
Add-Type -TypeDefinition ("using System;`nusing System.Collections.Generic;`n" +
    "using Game.Chat.Protocol;`n" + $sources + $stubs) `
    -IgnoreWarnings | Out-Null

function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
}
function New-Message([string]$id, [long]$sender, [long]$target,
    [string]$channel = 'private') {
    return [Game.Chat.Protocol.WorldChatMessage]@{
        msgId = 'chat_message'
        type = 'chat_message'
        messageId = $id
        channel = $channel
        senderCharacterDbId = $sender
        targetCharacterDbId = $target
        senderName = '发送者'
        targetName = '接收者'
        content = $id
        serverTime = 1791345600000
    }
}

$store = [Game.Chat.Model.PrivateChatConversationStore]::new()
$store.Reset(301)
Assert-True ($store.EnterWorld(301, 1001)) `
    'The local character must enter the current private conversation epoch.'
$script:changed = 0
$script:lastPartner = 0
$store.add_ConversationChanged([Action[long]]{
    param($partner)
    $script:changed++
    $script:lastPartner = $partner
})

$partner = 0
Assert-True ($store.Receive(301, (New-Message 'm1' 1001 1002),
    [ref]$partner) -and $partner -eq 1002 -and
    $store.GetMessages(1002).Count -eq 1) `
    'An own echo must be stored under the target character ID.'
Assert-True (-not $store.Receive(301, (New-Message 'm1' 1001 1002),
    [ref]$partner) -and $store.GetMessages(1002).Count -eq 1 -and
    $script:changed -eq 1) `
    'The same message ID must be ignored on a duplicate server delivery.'
Assert-True ($store.Receive(301, (New-Message 'm2' 1002 1001),
    [ref]$partner) -and $partner -eq 1002 -and
    $store.GetMessages(1002).Count -eq 2 -and
    $script:lastPartner -eq 1002) `
    'An incoming message must join the same two-person conversation.'
Assert-True (-not $store.Receive(301, (New-Message 'foreign' 1003 1004),
    [ref]$partner) -and
    -not $store.Receive(301, (New-Message 'world' 1002 1001 'world'),
        [ref]$partner)) `
    'Foreign participants and non-private messages must be rejected.'
Assert-True ($store.ConversationIds.Count -eq 1 -and
    $store.GetMessages(1003).Count -eq 0) `
    'Conversation keys must be limited to accepted partner IDs.'
Assert-True (-not $store.Receive(300, (New-Message 'old' 1002 1001),
    [ref]$partner)) `
    'A stale connection epoch must not alter current conversations.'

$store.Reset(302)
Assert-True ($store.ConnectionEpoch -eq 302 -and
    $store.CharacterDbId -eq 0 -and
    $store.ConversationIds.Count -eq 0 -and
    $store.GetMessages(1002).Count -eq 0) `
    'Reset must clear all private conversations for the old connection.'
Assert-True (-not $store.EnterWorld(301, 1001) -and
    $store.EnterWorld(302, 1001)) `
    'The store must reject an old epoch and accept the current one.'

Write-Output 'PASS: private conversation routing, message deduplication, foreign filtering and epoch reset.'
