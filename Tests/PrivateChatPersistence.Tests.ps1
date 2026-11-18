$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$paths = @(
    'Assets/Scripts/Chat/Protocol/WorldChatMessages.cs',
    'Assets/Scripts/Chat/Model/IPrivateChatMessageStorage.cs',
    'Assets/Scripts/Chat/Model/PrivateChatConversationStore.cs',
    'Assets/Scripts/Chat/Storage/PrivateChatFileStorage.cs'
)
$sources = ($paths | ForEach-Object {
    (Get-Content -LiteralPath (Join-Path $root $_) -Raw) `
        -replace '(?m)^using [^\r\n]+;\r?\n', ''
}) -join "`n"
$stubs = @'
public class NetMessageBase { public string msgId; public string type; }
namespace UnityEngine
{
    public static class Debug { public static void LogWarning(string message) {} }
    public static class JsonUtility
    {
        private static readonly System.Text.Json.JsonSerializerOptions Options =
            new System.Text.Json.JsonSerializerOptions { IncludeFields = true };
        public static string ToJson<T>(T value) =>
            System.Text.Json.JsonSerializer.Serialize(value, Options);
        public static T FromJson<T>(string json) =>
            System.Text.Json.JsonSerializer.Deserialize<T>(json, Options);
    }
}
public sealed class FailingPrivateChatStorage : Game.Chat.Model.IPrivateChatMessageStorage
{
    public bool TryLoad(out System.Collections.Generic.List<Game.Chat.Protocol.WorldChatMessage> messages)
    {
        messages = new System.Collections.Generic.List<Game.Chat.Protocol.WorldChatMessage>();
        return true;
    }
    public bool TryWrite(long partnerId, Game.Chat.Protocol.WorldChatMessage message) =>
        false;
}
'@
Add-Type -TypeDefinition ("using System;`nusing System.Collections.Generic;`n" +
    "using System.Globalization;`nusing System.IO;`n" +
    "using System.Security.Cryptography;`nusing System.Text;`n" +
    "using Game.Chat.Protocol;`nusing Game.Chat.Model;`n" +
    "using UnityEngine;`n" + $stubs + $sources) `
    -IgnoreWarnings -WarningAction SilentlyContinue | Out-Null

function Check([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}
function New-Message([string]$id, [long]$sender, [long]$target,
    [long]$time) {
    return [Game.Chat.Protocol.WorldChatMessage]@{
        msgId = 'chat_message'
        type = 'chat_message'
        messageId = $id
        channel = 'private'
        senderCharacterDbId = $sender
        senderName = '发送者'
        targetCharacterDbId = $target
        targetName = '接收者'
        content = "消息 $id"
        serverTime = $time
    }
}

$temporaryParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::Combine(
    $temporaryParent, "private-chat-storage-$([Guid]::NewGuid().ToString('N'))"))
Check ($temporaryRoot.StartsWith(
    $temporaryParent.TrimEnd([IO.Path]::DirectorySeparatorChar) +
    [IO.Path]::DirectorySeparatorChar,
    [StringComparison]::OrdinalIgnoreCase)) 'Test storage must stay inside the temp directory.'
try {
    $backend = [Game.Chat.Storage.PrivateChatFileStorage]::new(
        $temporaryRoot, 'test-gate', 9000, 'account-a', [long]1001)
    $store = [Game.Chat.Model.PrivateChatConversationStore]::new()
    $store.Reset(1)
    Check ($store.EnterWorld(1, 1001, $backend) -and $store.IsPersistent) `
        'A scoped conversation store must open before accepting messages.'
    $incomingId = [Guid]::NewGuid().ToString('D')
    $echoId = [Guid]::NewGuid().ToString('D')
    $partner = [long]0
    $incoming = New-Message $incomingId 1002 1001 2000
    $echo = New-Message $echoId 1001 1002 1000
    Check ($store.Receive(1, $incoming, [ref]$partner) -and
        $partner -eq 1002 -and
        $store.Receive(1, $echo, [ref]$partner)) `
        'Incoming private messages and echoes must be written before appearing.'
    Check ($store.GetMessages(1002).Count -eq 2 -and
        $store.GetMessages(1002)[0].messageId -eq $echoId) `
        'The conversation must use the original server timestamp order.'

    $reopened = [Game.Chat.Model.PrivateChatConversationStore]::new()
    $reopened.Reset(2)
    Check ($reopened.EnterWorld(2, 1001,
        [Game.Chat.Storage.PrivateChatFileStorage]::new(
            $temporaryRoot, 'test-gate', 9000, 'account-a', [long]1001)) -and
        $reopened.GetMessages(1002).Count -eq 2 -and
        $reopened.GetMessages(1002)[0].messageId -eq $echoId) `
        'Both message data and partner index must survive a new process session.'
    Check ($reopened.TryReceive(2, $incoming, [ref]$partner) -eq
            [Game.Chat.Model.PrivateChatReceiveResult]::Duplicate -and
        $reopened.GetMessages(1002).Count -eq 2 -and
        -not $reopened.Receive(1,
            (New-Message ([Guid]::NewGuid().ToString('D')) 1002 1001 3000),
            [ref]$partner)) `
        'A redelivery or old epoch must not add another row.'

    foreach ($scope in @(
        @('different-gate', 9000, 'account-a', 1001),
        @('test-gate', 9001, 'account-a', 1001),
        @('test-gate', 9000, 'account-b', 1001),
        @('test-gate', 9000, 'account-a', 1003)
    )) {
        $isolated = [Game.Chat.Model.PrivateChatConversationStore]::new()
        $isolated.Reset(3)
        Check ($isolated.EnterWorld(3, [long]$scope[3],
            [Game.Chat.Storage.PrivateChatFileStorage]::new(
                $temporaryRoot, [string]$scope[0], [int]$scope[1],
                [string]$scope[2], [long]$scope[3])) -and
            $isolated.ConversationIds.Count -eq 0) `
            'Host, port, account and role must independently isolate chat history.'
    }

    $failing = [Game.Chat.Model.PrivateChatConversationStore]::new()
    $failing.Reset(4)
    Check ($failing.EnterWorld(4, 1001, [FailingPrivateChatStorage]::new()) -and
        $failing.TryReceive(4,
            (New-Message ([Guid]::NewGuid().ToString('D')) 1002 1001 4000),
            [ref]$partner) -eq
            [Game.Chat.Model.PrivateChatReceiveResult]::StorageFailed -and
        $failing.ConversationIds.Count -eq 0) `
        'A failed write must never enter the visible conversation or dedup index.'
    $badRoot = Join-Path $temporaryRoot 'occupied'
    [IO.File]::WriteAllText($badRoot, 'not a directory')
    $unavailable = [Game.Chat.Model.PrivateChatConversationStore]::new()
    $unavailable.Reset(5)
    Check (-not $unavailable.EnterWorld(5, 1001,
        [Game.Chat.Storage.PrivateChatFileStorage]::new(
            $badRoot, 'test-gate', 9000, 'account-a', [long]1001)) -and
        -not $unavailable.IsPersistent) `
        'An unwritable storage location must fail closed before messages arrive.'

    $saved = @(Get-ChildItem -LiteralPath (
        Join-Path $temporaryRoot 'PrivateChat') -Filter '*.json' -Recurse)
    Check ($saved.Count -eq 2) 'Only successfully published messages belong on disk.'
    $corruptedId = (Get-Content -LiteralPath $saved[0].FullName -Raw |
        ConvertFrom-Json).message.messageId
    [IO.File]::WriteAllText($saved[0].FullName, '{"broken":true}')
    $redelivery = if ($corruptedId -eq $incomingId) { $incoming } else { $echo }
    Check ($reopened.TryReceive(2, $redelivery, [ref]$partner) -eq
        [Game.Chat.Model.PrivateChatReceiveResult]::StorageFailed) `
        'A duplicate with damaged local storage must not be treated as durable.'
    $corrupted = [Game.Chat.Model.PrivateChatConversationStore]::new()
    $corrupted.Reset(6)
    Check (-not $corrupted.EnterWorld(6, 1001,
        [Game.Chat.Storage.PrivateChatFileStorage]::new(
            $temporaryRoot, 'test-gate', 9000, 'account-a', [long]1001)) -and
        $corrupted.ConversationIds.Count -eq 0) `
        'A corrupt saved message must not silently load partial history.'
    Write-Output 'PASS: durable private messages, order, dedup, scope and failure isolation.'
}
finally {
    if ([IO.Directory]::Exists($temporaryRoot) -and
        $temporaryRoot.StartsWith(
            $temporaryParent.TrimEnd([IO.Path]::DirectorySeparatorChar) +
            [IO.Path]::DirectorySeparatorChar,
            [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
    }
}
