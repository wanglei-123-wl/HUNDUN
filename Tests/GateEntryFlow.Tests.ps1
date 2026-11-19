$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$files = @(
    'Assets/Scripts/NetWork/NetMessageDispatcher.cs',
    'Assets/Scripts/NetWork/GateEntryHandoff.cs',
    'Assets/Scripts/NetWork/GateEntryCoordinator.cs',
    'Assets/Scripts/WorldMapSceneCatalog.cs',
    'Assets/Scripts/Chat/Protocol/WorldChatMessages.cs',
    'Assets/Scripts/Chat/Transport/WorldChatTransportInbox.cs',
    'Assets/Scripts/Chat/Model/WorldChatTimeline.cs',
    'Assets/Scripts/Chat/Model/IPrivateChatMessageStorage.cs',
    'Assets/Scripts/Chat/Model/PrivateChatConversationStore.cs',
    'Assets/Scripts/Chat/Model/PrivateChatAckQueue.cs',
    'Assets/Scripts/Chat/Storage/PrivateChatFileStorage.cs',
    'Assets/Scripts/Chat/Model/ChatSendSession.cs',
    'Assets/Scripts/Chat/Runtime/WorldChatSessionRuntime.cs'
)
$sources = ($files | ForEach-Object {
    (Get-Content (Join-Path $root $_) -Raw) -replace '(?m)^using [^\r\n]+;\r?\n', ''
}) -join "`n"

$stubs = @'
namespace Game.Client.BattleSync
{
    public interface IBattleSyncMessageRouter
    {
        void Register(string id, System.Action<string> handler);
        void Unregister(string id, System.Action<string> handler);
    }
}
namespace UnityEngine
{
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public sealed class DisallowMultipleComponent : System.Attribute {}
    [System.AttributeUsage(System.AttributeTargets.Field)]
    public sealed class SerializeField : System.Attribute {}
    [System.AttributeUsage(System.AttributeTargets.Field)]
    public sealed class MinAttribute : System.Attribute
    {
        public MinAttribute(float value) {}
    }
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public sealed class DefaultExecutionOrder : System.Attribute
    {
        public DefaultExecutionOrder(int value) {}
    }
    public class MonoBehaviour
    {
        public bool enabled = true;
        public GameObject gameObject;
        protected static void Destroy(object value) {}
        protected static void DontDestroyOnLoad(object value) {}
    }
    public sealed class GameObject
    {
        private readonly System.Collections.Generic.Dictionary<System.Type, MonoBehaviour> components =
            new System.Collections.Generic.Dictionary<System.Type, MonoBehaviour>();
        public T GetComponent<T>() where T : MonoBehaviour
        {
            return components.TryGetValue(typeof(T), out MonoBehaviour component)
                ? (T)component : null;
        }
        public T AddComponent<T>() where T : MonoBehaviour, new()
        {
            var component = new T();
            component.gameObject = this;
            components[typeof(T)] = component;
            return component;
        }
    }
    public static class Time
    {
        public static float realtimeSinceStartup;
        public static double realtimeSinceStartupAsDouble => realtimeSinceStartup;
    }
    public static class Mathf
    {
        public static float Max(float a, float b) => System.Math.Max(a, b);
        public static float Min(float a, float b) => System.Math.Min(a, b);
    }
    public static class Debug
    {
        public static readonly System.Collections.Generic.List<string> Messages =
            new System.Collections.Generic.List<string>();
        public static void Log(string value) => Messages.Add(value);
        public static void LogWarning(string value) => Messages.Add(value);
        public static void LogError(string value) => Messages.Add(value);
    }
    public static class Application { public static string persistentDataPath =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gate-entry-chat-test"); }
    public static class JsonUtility
    {
        public static T FromJson<T>(string json) =>
            System.Text.Json.JsonSerializer.Deserialize<T>(json,
                new System.Text.Json.JsonSerializerOptions { IncludeFields = true });
        public static string ToJson<T>(T value) =>
            System.Text.Json.JsonSerializer.Serialize(value,
                new System.Text.Json.JsonSerializerOptions { IncludeFields = true });
    }
}
public sealed class AuthSession
{
    public static AuthSession Instance { get; set; }
    public bool HasValidGameSession = true;
    public string UserId = "00001";
    public string GameSessionToken = "00001.fake_game_session_token";
    public bool IsLoggingOut;
    public event System.Action<string> GameSessionEnded;
    public void EndSession(string reason) { ClearSession(); GameSessionEnded?.Invoke(reason); }
    public void ClearSession() { GameSessionToken = ""; HasValidGameSession = false; }
    public bool IsCurrentGameEntry(NetworkManager network, long epoch) =>
        HasValidGameSession && !IsLoggingOut && epoch == network.ConnectionEpoch && epoch > 0;
    public bool AcceptGameEntry(NetworkManager network, long epoch, EnterWorldResult result) =>
        IsCurrentGameEntry(network, epoch) && result.success;
    public bool TrySendEnterWorld(NetworkManager network, NetMessageDispatcher dispatcher,
        long id, string character) => network.Send(new EnterWorldRequest {
            characterDbId = id, characterId = character, token = GameSessionToken, sessionLifecycleVersion = 1
        });
    public static bool IsRetryableGameSessionReason(string reason) =>
        reason == "session_still_online" || reason == "session_service_unavailable";
    public static bool IsTerminalGameSessionReason(string reason) => reason == "token_invalid_or_expired";
    public static string GameSessionEndedMessage(string reason) => reason;
}
public sealed class NetworkManager
{
    public static NetworkManager Instance { get; set; }
    public UnityEngine.GameObject gameObject = new UnityEngine.GameObject();
    public T GetComponent<T>() where T : UnityEngine.MonoBehaviour =>
        gameObject.GetComponent<T>();
    public bool IsConnected { get; private set; }
    public long ConnectionEpoch { get; private set; }
    public string ServerHost => "gate-test";
    public int ServerPort => 9000;
    public int SendCount;
    public EnterWorldRequest LastRequest;
    public Game.Chat.Protocol.WorldChatSendRequest LastChatRequest;
    public Game.Chat.Protocol.PrivateChatSendRequest LastPrivateChatRequest;
    public Game.Chat.Protocol.PrivateChatAckRequest LastPrivateChatAck;
    public int PrivateAckSendCount;
    public bool RejectPrivateAcks;
    public event System.Action OnConnected;
    public event System.Action<long> OnConnectedWithEpoch;
    public event System.Action<string> OnDisconnected;
    public event System.Action<long, string> OnDisconnectedWithEpoch;
    public event System.Action<string> OnMessageReceived;
    public void Connect()
    {
        IsConnected = true;
        ConnectionEpoch++;
        OnConnected?.Invoke();
        OnConnectedWithEpoch?.Invoke(ConnectionEpoch);
    }
    public void Disconnect()
    {
        IsConnected = false;
        OnDisconnected?.Invoke("Client disconnected");
        OnDisconnectedWithEpoch?.Invoke(ConnectionEpoch, "Client disconnected");
    }
    public bool Send(EnterWorldRequest request)
    {
        LastRequest = request;
        SendCount++;
        return IsConnected;
    }
    public bool Send(Game.Chat.Protocol.WorldChatSendRequest request)
    {
        LastChatRequest = request;
        SendCount++;
        return IsConnected;
    }
    public bool Send(Game.Chat.Protocol.PrivateChatSendRequest request)
    {
        LastPrivateChatRequest = request;
        SendCount++;
        return IsConnected;
    }
    public bool Send(Game.Chat.Protocol.PrivateChatAckRequest request)
    {
        LastPrivateChatAck = request;
        PrivateAckSendCount++;
        SendCount++;
        return IsConnected && !RejectPrivateAcks;
    }
    public bool Send<T>(T request)
    {
        SendCount++;
        return IsConnected;
    }
    public void Receive(string json) => OnMessageReceived?.Invoke(json);
}
public sealed class EnterWorldRequest
{
    public string msgId = "enter_world";
    public long characterDbId;
    public string characterId;
    public string token;
    public long time;
    public int sessionLifecycleVersion;
}
public class NetMessageBase
{
    public string msgId;
    public string type;
}
public sealed class PlayerNetworkState
{
    public string userId;
    public long characterDbId;
    public string characterId;
    public string sceneId;
}
public static class MovementEventPolicy
{
    public const string IncompatibleServerMessage = "Movement version mismatch.";
    public static bool Accepts(EnterWorldResult result) => result.movementProtocolVersion == 2;
    public static bool IsAdmissionError(ServerErrorMessage error) =>
        error?.requestMsgId == "enter_world" && error.errorCode == "movement_client_update_required";
}
public sealed class EnterWorldResult
{
    public int movementProtocolVersion;
    public bool success;
    public string message;
    public string reason;
    public string sceneSessionId;
    public string equipmentOperationSessionId;
    public string userId;
    public long characterDbId;
    public string characterId;
    public string sceneId;
    public string mapId;
    public long snapshotRevision;
    public long serverTime;
    public PlayerNetworkState self;
}
public sealed class ServerErrorMessage : NetMessageBase
{
    public string requestMsgId;
    public string errorCode;
    public string message;
}
public static class BattleSyncDiagnosticsLogger
{
    public static void LogDispatch(string state, string msgId, string type,
        long reliableMessageId, bool handled, bool deferred, int queueDepth) {}
    public static void LogDispatchException(string msgId, long reliableMessageId,
        string error) {}
}
'@

Add-Type -TypeDefinition ("using System;`nusing System.Collections.Generic;`nusing System.Globalization;`nusing System.IO;`nusing System.Security.Cryptography;`nusing System.Text;`nusing UnityEngine;`nusing Game.Chat.Transport;`nusing Game.Chat.Runtime;`nusing Game.Chat.Model;`nusing Game.Chat.Protocol;`nusing Game.Chat.Storage;`n" + $sources + $stubs) -IgnoreWarnings | Out-Null
function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
}
function New-Context {
    [NetworkManager]::Instance = [NetworkManager]::new()
    [AuthSession]::Instance = [AuthSession]::new()
    $dispatcher = [NetMessageDispatcher]::new()
    $method = [NetMessageDispatcher].GetMethod('OnEnable',
        [System.Reflection.BindingFlags]'Instance,NonPublic')
    $method.Invoke($dispatcher, @()) | Out-Null
    return $dispatcher
}

$temporaryParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$temporaryRoot = [IO.Path]::GetFullPath([IO.Path]::Combine(
    $temporaryParent, "gate-entry-chat-$([Guid]::NewGuid().ToString('N'))"))
Assert-True ($temporaryRoot.StartsWith(
    $temporaryParent.TrimEnd([IO.Path]::DirectorySeparatorChar) +
    [IO.Path]::DirectorySeparatorChar,
    [StringComparison]::OrdinalIgnoreCase)) `
    'The Gate test must keep its private chat data inside the temp directory.'
[UnityEngine.Application]::persistentDataPath = $temporaryRoot
try {
$chatRequest = [Game.Chat.Protocol.WorldChatSendRequest]::new(
    [long]2147483648, '你好')
$chatJsonOptions = [System.Text.Json.JsonSerializerOptions]::new()
$chatJsonOptions.IncludeFields = $true
$chatRequestJson = [System.Text.Json.JsonSerializer]::Serialize(
    $chatRequest, [Game.Chat.Protocol.WorldChatSendRequest], $chatJsonOptions)
Assert-True ($chatRequest.msgId -eq 'chat_send' -and
    $chatRequest.type -eq 'chat_send' -and $chatRequest.channel -eq 'world' -and
    $chatRequestJson.Contains('"requestSequence":2147483648')) `
    'World chat sends must keep their protocol IDs and 64-bit sequence.'
$chatHistoryJson = '{"msgId":"world_chat_history","syncId":"sync-1","snapshotTime":1791345605000,"batchIndex":1,"totalCount":1,"messages":[{"messageId":"chat-1","channel":"world","senderCharacterDbId":2147483648,"serverTime":1791345600000}],"isLast":true,"success":true}'
$chatHistory = [System.Text.Json.JsonSerializer]::Deserialize(
    $chatHistoryJson, [Game.Chat.Protocol.WorldChatHistory], $chatJsonOptions)
Assert-True ($chatHistory.messages.Length -eq 1 -and
    $chatHistory.messages[0].senderCharacterDbId -eq [long]2147483648 -and
    $chatHistory.messages[0].serverTime -eq [long]1791345600000) `
    'World chat history must preserve nested messages and 64-bit IDs and times.'

[UnityEngine.Time]::realtimeSinceStartup = 0
$dispatcher = New-Context
$network = [NetworkManager]::Instance
$script:accepted = 0
$script:failed = ''
$script:worldMessages = 0
$script:inventoryMessages = 0
$script:nearbyMessages = 0
$dispatcher.Register('world_snapshot', [Action[string]]{
    param($json) $script:worldMessages++
})
$coordinator = [GateEntryCoordinator]::new($network, $dispatcher, 1, 'character_001',
    10, [Action]{ $script:accepted++ },
    [Action[string]]{ param($message) $script:failed = $message })
$coordinator.Begin()
Assert-True ($network.SendCount -eq 1 -and $network.LastRequest.token -eq
    [AuthSession]::Instance.GameSessionToken -and
    -not [UnityEngine.JsonUtility]::ToJson($network.LastRequest).Contains('"sceneId"')) `
    'Gate must receive the selected game token once without a client-selected scene.'
$network.Receive('{"msgId":"world_snapshot"}')
$network.Receive('{"msgId":"inventory_snapshot_result","requestSequence":0}')
$network.Receive('{"msgId":"chat_message","channel":"nearby","messageId":"other-channel"}')
$network.Receive('{"msgId":"chat_message","type":"chat_message","channel":"world","messageId":"early-1","serverTime":1234}')
Assert-True ($script:worldMessages -eq 0 -and $script:nearbyMessages -eq 0) `
    'Non-chat world messages and nearby chat must wait for scene handlers.'
Assert-True ([UnityEngine.Debug]::Messages.Exists([Predicate[string]]{
    param($message) $message.Contains('[GateEntry][RX]') -and
        $message.Contains('msgId=world_snapshot')
})) 'Incoming message IDs must be visible while waiting for Gate.'
$network.Receive('{"msgId":"enter_world_result","movementProtocolVersion":2,"success":true,"characterDbId":1,"sceneId":"world_002","mapId":"map_001","sceneSessionId":"session-a","snapshotRevision":4,"serverTime":1234,"equipmentOperationSessionId":"equipment-session-a","self":{"characterDbId":1,"characterId":"character_001","sceneId":"world_002"}}')
$network.Receive('{"msgId":"world_chat_history","type":"world_chat_history","syncId":"sync-1","snapshotTime":1234,"batchIndex":1,"totalCount":0,"messages":[],"isLast":true,"success":true}')
Assert-True ($script:accepted -eq 1 -and $script:failed -eq '' -and
    [GateEntryHandoff]::HasPending) 'Success must prepare a handoff before scene activation.'
$serverScene = ''
$serverMap = ''
Assert-True ([GateEntryHandoff]::TryGetDestination($network.ConnectionEpoch, 1,
    [ref]$serverScene, [ref]$serverMap) -and
    $serverScene -eq 'world_002' -and $serverMap -eq 'map_001') `
    'Gate must hand off the server-selected world and map, not the old request hint.'
$inbox = [Game.Chat.Transport.WorldChatTransportInbox]::Instance
$runtime = [Game.Chat.Runtime.WorldChatSessionRuntime]::Instance
Assert-True ($runtime -ne $null -and $inbox.IsInWorld -and
    $inbox.CharacterDbId -eq 1 -and
    $runtime.Timeline.ConnectionEpoch -eq $network.ConnectionEpoch -and
    $runtime.Timeline.Status -eq [Game.Chat.Model.WorldChatHistoryStatus]::Complete -and
    $runtime.Timeline.MessagesNewestFirst.Count -eq 1 -and
    $runtime.Timeline.MessagesNewestFirst[0].messageId -eq 'early-1') `
    'Persistent chat runtime must combine early realtime with history before scene activation.'
Assert-True ([object]::ReferenceEquals(
    $runtime, [Game.Chat.Runtime.WorldChatSessionRuntime]::Ensure($network, $inbox)) -and
    $runtime.Timeline.MessagesNewestFirst.Count -eq 1) `
    'Scene handoff must reuse the persistent chat runtime without clearing history.'
$raw = ''
Assert-True ([GateEntryHandoff]::TryTake($network.ConnectionEpoch, 1,
    'world_002', 'map_001', [ref]$raw)) `
    'The game scene must take a matching connection and character.'
Assert-True ($raw.Contains('"equipmentOperationSessionId":"equipment-session-a"')) `
    'Handoff must retain the equipment session field for the game scene.'
$dispatcher.Register('inventory_snapshot_result', [Action[string]]{
    param($json) $script:inventoryMessages++
})
$dispatcher.Register('chat_message', [Action[string]]{
    param($json)
    if ($json.Contains('"channel":"nearby"')) { $script:nearbyMessages++ }
})
$dispatcher.ReplayDeferredWorldMessages()
Assert-True ($script:worldMessages -eq 1 -and $script:inventoryMessages -eq 1 -and
    $script:nearbyMessages -eq 1 -and $network.SendCount -eq 1 -and
    $runtime.Timeline.MessagesNewestFirst.Count -eq 1) `
    'Buffered world and inventory messages must replay after handlers are ready without resending enter_world.'
$sendError = ''
Assert-True ($runtime.TrySendWorldMessage('  gate chat  ', [ref]$sendError) -and
    $network.LastChatRequest.content -eq 'gate chat' -and
    $network.LastChatRequest.requestSequence -eq 1 -and
    $runtime.Sender.PendingSequence -eq 1) `
    'An entered persistent chat session must send the first world chat request.'
$network.Receive('{"msgId":"chat_send_result","type":"chat_send_result","requestSequence":1,"success":true}')
Assert-True ($runtime.Sender.PendingSequence -eq 0 -and
    $runtime.Timeline.MessagesNewestFirst.Count -eq 1) `
    'A matched send result must clear pending without adding a duplicate chat message.'
$script:privateMessages = 0
$script:privateTarget = 0
$runtime.add_PrivateMessageReceived(
    [Action[Game.Chat.Protocol.WorldChatMessage]]{
        param($message)
        $script:privateMessages++
        $script:privateTarget = $message.targetCharacterDbId
    })
$privateError = ''
Assert-True ($runtime.TrySendPrivateMessage(1002, '  私聊  ', [ref]$privateError) -and
    $network.LastPrivateChatRequest.channel -eq 'private' -and
    $network.LastPrivateChatRequest.targetCharacterDbId -eq 1002 -and
    $network.LastPrivateChatRequest.content -eq '私聊' -and
    $network.LastPrivateChatRequest.requestSequence -eq 2 -and
    $runtime.Sender.PendingChannel -eq 'private') `
    'The runtime must send private chat with the selected target and shared sequence.'
$network.Receive('{"msgId":"chat_message","type":"chat_message","messageId":"private-1","channel":"private","senderCharacterDbId":1,"senderName":"本人","targetCharacterDbId":1002,"targetName":"好友","content":"私聊","serverTime":1235}')
Assert-True ($script:privateMessages -eq 1 -and $script:privateTarget -eq 1002 -and
    $runtime.PrivateConversations.GetMessages(1002).Count -eq 1 -and
    $network.PrivateAckSendCount -eq 0) `
    'Private chat messages must pass through the runtime without entering world history.'
$network.Receive('{"msgId":"chat_message","type":"chat_message","messageId":"private-1","channel":"private","senderCharacterDbId":1,"senderName":"本人","targetCharacterDbId":1002,"targetName":"好友","content":"私聊","serverTime":1235}')
Assert-True ($script:privateMessages -eq 1 -and
    $runtime.PrivateConversations.GetMessages(1002).Count -eq 1) `
    'A duplicated private message ID must not create a second local message.'
$network.Receive('{"msgId":"chat_send_result","type":"chat_send_result","requestSequence":2,"success":true}')
Assert-True ($runtime.Sender.PendingSequence -eq 0 -and
    $runtime.Sender.PendingChannel -eq $null) `
    'A private send result must settle the shared sender state.'
$receivedId = [Guid]::NewGuid().ToString('D')
$receivedJson = '{"msgId":"chat_message","type":"chat_message","messageId":"' +
    $receivedId + '","channel":"private","senderCharacterDbId":1002,' +
    '"senderName":"好友","targetCharacterDbId":1,"targetName":"本人",' +
    '"content":"离线补投","serverTime":1230}'
$network.Receive($receivedJson)
Assert-True ($runtime.PrivateConversations.GetMessages(1002).Count -eq 2 -and
    $network.PrivateAckSendCount -eq 0) `
    'An incoming message must be persisted before ACK transmission.'
[UnityEngine.Time]::realtimeSinceStartup = 0.1
$runtime.GetType().GetMethod('Update',
    [System.Reflection.BindingFlags]'Instance,NonPublic').Invoke(
        $runtime, @()) | Out-Null
$ackJson = [System.Text.Json.JsonSerializer]::Serialize(
    $network.LastPrivateChatAck, [Game.Chat.Protocol.PrivateChatAckRequest],
    $chatJsonOptions)
Assert-True ($network.PrivateAckSendCount -eq 1 -and
    $network.LastPrivateChatAck.messageIds.Length -eq 1 -and
    $network.LastPrivateChatAck.messageIds[0] -eq $receivedId -and
    $ackJson.Contains('"msgId":"chat_private_ack"') -and
    $ackJson.Contains('"type":"chat_private_ack"') -and
    -not $ackJson.Contains('requestSequence') -and
    $runtime.Sender.NextSequence -eq 3) `
    'Saved incoming messages need a sequence-free ACK without consuming a chat send sequence.'
$network.Receive($receivedJson)
[UnityEngine.Time]::realtimeSinceStartup = 0.2
$runtime.GetType().GetMethod('Update',
    [System.Reflection.BindingFlags]'Instance,NonPublic').Invoke(
        $runtime, @()) | Out-Null
Assert-True ($network.PrivateAckSendCount -eq 2 -and
    $runtime.PrivateConversations.GetMessages(1002).Count -eq 2 -and
    $script:privateMessages -eq 2) `
    'The same durable incoming message must ACK again without duplicating the row.'
$network.RejectPrivateAcks = $true
$failedId = [Guid]::NewGuid().ToString('D')
$failedJson = $receivedJson.Replace($receivedId, $failedId)
$network.Receive($failedJson)
[UnityEngine.Time]::realtimeSinceStartup = 0.3
$runtime.GetType().GetMethod('Update',
    [System.Reflection.BindingFlags]'Instance,NonPublic').Invoke(
        $runtime, @()) | Out-Null
Assert-True ($network.PrivateAckSendCount -eq 3 -and
    $runtime.PrivateConversations.GetMessages(1002).Count -eq 3) `
    'A transport failure must not remove a durable incoming message.'
$network.RejectPrivateAcks = $false
[UnityEngine.Time]::realtimeSinceStartup = 2.4
$runtime.GetType().GetMethod('Update',
    [System.Reflection.BindingFlags]'Instance,NonPublic').Invoke(
        $runtime, @()) | Out-Null
Assert-True ($network.PrivateAckSendCount -eq 4 -and
    $network.LastPrivateChatAck.messageIds[0] -eq $failedId) `
    'A rejected ACK send must retry after a delay.'
$network.Receive('{"msgId":"error","requestMsgId":"chat_private_ack","errorCode":"private_chat_store_unavailable"}')
[UnityEngine.Time]::realtimeSinceStartup = 4.5
$runtime.GetType().GetMethod('Update',
    [System.Reflection.BindingFlags]'Instance,NonPublic').Invoke(
        $runtime, @()) | Out-Null
Assert-True ($network.PrivateAckSendCount -eq 5 -and
    $network.LastPrivateChatAck.messageIds[0] -eq $receivedId -and
    $network.LastPrivateChatAck.messageIds.Length -eq 2 -and
    $network.LastPrivateChatAck.messageIds[1] -eq $failedId -and
    $runtime.Sender.NextSequence -eq 3) `
    'An ACK storage error must coalesce remembered IDs without changing chat send sequence.'
[UnityEngine.Time]::realtimeSinceStartup = 4.6
$runtime.GetType().GetMethod('Update',
    [System.Reflection.BindingFlags]'Instance,NonPublic').Invoke(
        $runtime, @()) | Out-Null
Assert-True ($network.PrivateAckSendCount -eq 5) `
    'Coalesced ACK retries must not resend the same batch on the next frame.'
$savedIncoming = @(Get-ChildItem -LiteralPath $temporaryRoot -Filter '*.json' -Recurse |
    Where-Object {
        $record = Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json
        $record.message.messageId -eq $receivedId
    })
Assert-True ($savedIncoming.Count -eq 1) `
    'A received message must exist on disk before it can be acknowledged.'
[IO.File]::WriteAllText($savedIncoming[0].FullName, '{"broken":true}')
$network.Receive($receivedJson)
[UnityEngine.Time]::realtimeSinceStartup = 5
$runtime.GetType().GetMethod('Update',
    [System.Reflection.BindingFlags]'Instance,NonPublic').Invoke(
        $runtime, @()) | Out-Null
Assert-True ($network.PrivateAckSendCount -eq 5 -and
    $runtime.PrivateConversations.GetMessages(1002).Count -eq 3) `
    'A corrupt saved duplicate must neither ACK nor append another message.'
$network.Receive('{"msgId":"error","requestMsgId":"chat_private_ack","errorCode":"player_not_in_world"}')
[UnityEngine.Time]::realtimeSinceStartup = 40
$runtime.GetType().GetMethod('Update',
    [System.Reflection.BindingFlags]'Instance,NonPublic').Invoke(
        $runtime, @()) | Out-Null
Assert-True ($network.PrivateAckSendCount -eq 5) `
    'An ACK identity error must stop retries until a confirmed world entry.'
$coordinator.Dispose()
[UnityEngine.Time]::realtimeSinceStartup = 0

[GateEntryHandoff]::Store(5, 1, 'world_001', 'map_demo_1', '{}')
$raw = ''
Assert-True (-not [GateEntryHandoff]::TryGetDestination(5, 2,
    [ref]$serverScene, [ref]$serverMap) -and
    $serverScene -eq $null -and $serverMap -eq $null) `
    'A different character must not inspect the pending destination.'
Assert-True (-not [GateEntryHandoff]::TryGetDestination(6, 1,
    [ref]$serverScene, [ref]$serverMap)) `
    'A stale connection must not inspect the pending destination.'
Assert-True (-not [GateEntryHandoff]::TryTake(6, 1,
    'world_001', 'map_demo_1', [ref]$raw) -and
    -not [GateEntryHandoff]::HasPending) 'A stale connection must not consume a Gate result.'
[GateEntryHandoff]::Store(5, 1, 'world_001', 'map_demo_1', '{}')
Assert-True (-not [GateEntryHandoff]::TryTake(5, 1,
    'another_scene', 'map_demo_1', [ref]$raw)) `
    'A different scene must not consume a Gate result.'
[GateEntryHandoff]::Store(5, 1, 'world_001', 'map_demo_1', '{}')
Assert-True (-not [GateEntryHandoff]::TryTake(5, 1,
    'world_001', 'map_001', [ref]$raw)) `
    'A different map must not consume a Gate result.'

$dispatcher = New-Context
$network = [NetworkManager]::Instance
$script:failed = ''
$coordinator = [GateEntryCoordinator]::new($network, $dispatcher, 1, 'character_001',
    10, [Action]{ throw 'Rejected result cannot activate a scene.' },
    [Action[string]]{ param($message) $script:failed = $message })
$coordinator.Begin()
$network.Receive('{"msgId":"enter_world_result","success":false,"message":"Unauthorized: token is invalid or expired"}')
Assert-True ($script:failed.Contains('Unauthorized') -and
    -not [GateEntryHandoff]::HasPending -and -not $network.IsConnected) `
    'Gate rejection must clear the pending scene and disconnect.'

$dispatcher = New-Context
$network = [NetworkManager]::Instance
$script:failed = ''
$coordinator = [GateEntryCoordinator]::new($network, $dispatcher, 1, 'character_001',
    10, [Action]{ throw 'Timeout cannot activate a scene.' },
    [Action[string]]{ param($message) $script:failed = $message })
$coordinator.Begin()
[UnityEngine.Time]::realtimeSinceStartup = 11
$coordinator.CheckTimeout()
Assert-True ($script:failed.Contains('超时') -and
    -not [GateEntryHandoff]::HasPending -and -not $network.IsConnected) `
    'Timeout must fail closed.'
Assert-True ([UnityEngine.Debug]::Messages.Exists([Predicate[string]]{
    param($message) $message.Contains('[GateEntry][TIMEOUT]') -and
        $message.Contains('receivedMessages=0') -and
        $message.Contains('dispatcherSubscribed=True')
})) 'Timeout diagnosis must distinguish silence from dispatch problems.'

$dispatcher = New-Context
$network = [NetworkManager]::Instance
$script:failed = ''
$coordinator = [GateEntryCoordinator]::new($network, $dispatcher, 1, 'character_001',
    10, [Action]{ throw 'Wrong character cannot activate a scene.' },
    [Action[string]]{ param($message) $script:failed = $message })
$coordinator.Begin()
$network.Receive('{"msgId":"enter_world_result","movementProtocolVersion":2,"success":true,"characterDbId":2,"sceneId":"world_001","mapId":"map_demo_1","sceneSessionId":"session-wrong-character","snapshotRevision":4,"serverTime":1234,"self":{"characterDbId":2,"characterId":"character_001"}}')
Assert-True ($script:failed.Contains('不匹配') -and
    -not [GateEntryHandoff]::HasPending) 'Another character cannot pass the Gate handoff.'

$dispatcher = New-Context
$network = [NetworkManager]::Instance
$script:failed = ''
$coordinator = [GateEntryCoordinator]::new($network, $dispatcher, 1, 'character_001',
    10, [Action]{ throw 'Wrong map cannot activate a scene.' },
    [Action[string]]{ param($message) $script:failed = $message })
$coordinator.Begin()
$network.Receive('{"msgId":"enter_world_result","movementProtocolVersion":2,"success":true,"characterDbId":1,"sceneId":"world_002","mapId":"map_demo_1","sceneSessionId":"session-wrong-map","snapshotRevision":4,"serverTime":1234,"self":{"characterDbId":1,"characterId":"character_001"}}')
Assert-True ($script:failed.Contains('地图') -and
    -not [GateEntryHandoff]::HasPending -and -not $network.IsConnected) `
    'An unknown world/map combination must stop entry and close the Gate connection.'

$dispatcher = New-Context
$network = [NetworkManager]::Instance
$script:failed = ''
$coordinator = [GateEntryCoordinator]::new($network, $dispatcher, 1, 'character_001',
    10, [Action]{ throw 'Missing map cannot activate a scene.' },
    [Action[string]]{ param($message) $script:failed = $message })
$coordinator.Begin()
$network.Receive('{"msgId":"enter_world_result","movementProtocolVersion":2,"success":true,"characterDbId":1,"sceneId":"world_001","sceneSessionId":"session-missing-map","snapshotRevision":4,"serverTime":1234,"self":{"characterDbId":1,"characterId":"character_001"}}')
Assert-True ($script:failed.Contains('地图') -and
    -not [GateEntryHandoff]::HasPending -and -not $network.IsConnected) `
    'A success packet without mapId must stop entry rather than assume Demo 1.'

$dispatcher = New-Context
$network = [NetworkManager]::Instance
$script:failed = ''
$coordinator = [GateEntryCoordinator]::new($network, $dispatcher, 1, 'character_001',
    10, [Action]{ throw 'Conflicting self scene cannot activate a scene.' },
    [Action[string]]{ param($message) $script:failed = $message })
$coordinator.Begin()
$network.Receive('{"msgId":"enter_world_result","movementProtocolVersion":2,"success":true,"characterDbId":1,"sceneId":"world_002","mapId":"map_001","sceneSessionId":"session-wrong-self","snapshotRevision":4,"serverTime":1234,"self":{"characterDbId":1,"characterId":"character_001","sceneId":"world_001"}}')
Assert-True ($script:failed.Contains('不匹配') -and
    -not [GateEntryHandoff]::HasPending -and -not $network.IsConnected) `
    'A self scene conflicting with the top-level world cannot pass the handoff.'

$dispatcher = New-Context
$network = [NetworkManager]::Instance
$script:failed = ''
$coordinator = [GateEntryCoordinator]::new($network, $dispatcher, 1, 'character_001',
    10, [Action]{ throw 'Missing scene session cannot activate a scene.' },
    [Action[string]]{ param($message) $script:failed = $message })
$coordinator.Begin()
$network.Receive('{"msgId":"enter_world_result","movementProtocolVersion":2,"success":true,"characterDbId":1,"sceneId":"world_001","mapId":"map_demo_1","snapshotRevision":4,"serverTime":1234,"self":{"characterDbId":1,"characterId":"character_001"}}')
Assert-True ($script:failed.Contains('不匹配') -and
    -not [GateEntryHandoff]::HasPending -and -not $network.IsConnected) `
    'A success packet without sceneSessionId must not activate the world.'

$dispatcher = New-Context
$network = [NetworkManager]::Instance
$coordinator = [GateEntryCoordinator]::new($network, $dispatcher, 1, 'character_001',
    10, [Action]{}, [Action[string]]{ param($message) })
$coordinator.Begin()
$network.Receive('{"msgId":"enter_world_result","movementProtocolVersion":2,"success":true,"characterDbId":1,"sceneId":"world_001","mapId":"map_demo_1","sceneSessionId":"session-b","snapshotRevision":4,"serverTime":1234,"self":{"characterDbId":1,"characterId":"character_001"}}')
$network.Disconnect()
$inbox = [Game.Chat.Transport.WorldChatTransportInbox]::Instance
$network.Receive('{"msgId":"chat_message","channel":"world","messageId":"stale-1"}')
Assert-True (-not $inbox.IsInWorld -and $inbox.CharacterDbId -eq 0 -and
    [Game.Chat.Runtime.WorldChatSessionRuntime]::Instance.Timeline.ConnectionEpoch -eq 0 -and
    [Game.Chat.Runtime.WorldChatSessionRuntime]::Instance.Timeline.MessagesNewestFirst.Count -eq 0) `
    'A disconnected Gate connection must drop its pending chat and reject late chat callbacks.'
Assert-True ([GateEntryHandoff]::RequiresHandoff -and
    -not [GateEntryHandoff]::HasPending) `
    'A connection lost after success must forbid a second automatic enter_world.'
$coordinator.Dispose()
[GateEntryHandoff]::Clear()

$dispatcher = New-Context
$network = [NetworkManager]::Instance
$coordinator = [GateEntryCoordinator]::new($network, $dispatcher, 1, 'character_001',
    10, [Action]{}, [Action[string]]{ param($message) })
$coordinator.Begin()
$network.Receive('{"msgId":"enter_world_result","movementProtocolVersion":2,"success":true,"characterDbId":1,"sceneId":"world_001","mapId":"map_demo_1","sceneSessionId":"session-c","snapshotRevision":4,"serverTime":1234,"self":{"characterDbId":1,"characterId":"character_001"}}')
for ($i = 0; $i -le 512; $i++) { $network.Receive('{"msgId":"world_snapshot"}') }
Assert-True (-not [GateEntryHandoff]::HasPending -and
    [GateEntryHandoff]::RequiresHandoff -and -not $network.IsConnected) `
    'Buffer overflow must invalidate the handoff and close the Gate connection.'
$coordinator.Dispose()
[GateEntryHandoff]::Clear()

$selection = Get-Content (Join-Path $root 'Assets/Scripts/LoginScripts/CharacterSelectPanelUI.cs') -Raw
$dispatcher = New-Context
$network = [NetworkManager]::Instance
$network.Connect()
$script:worldReplayed = 0
$script:businessDelivered = 0
$dispatcher.Register('world_snapshot', [Action[string]]{
    param($json) $script:worldReplayed++
})
$dispatcher.Register('inventory_snapshot_result', [Action[string]]{
    param($json) $script:businessDelivered++
})
$dispatcher.BeginDeferredMapTransferMessages()
$network.Receive('{"msgId":"world_snapshot","sceneId":"world_001"}')
$network.Receive('{"msgId":"inventory_snapshot_result"}')
Assert-True ($script:worldReplayed -eq 0 -and $script:businessDelivered -eq 1) `
    'During map load, scene packets wait while inventory messages continue.'
$dispatcher.ReplayDeferredWorldMessages()
Assert-True ($script:worldReplayed -eq 1) `
    'The destination scene packet must replay after initialization.'

$game = Get-Content (Join-Path $root 'Assets/Scripts/GameSession.cs') -Raw
$messages = Get-Content (Join-Path $root 'Assets/Scripts/NetWork/NetMessages.cs') -Raw
$scene = Get-Content (Join-Path $root 'Assets/Scenes/SampleScene.unity') -Raw
Assert-True ($selection -match 'StartCoroutine\(EnterWorldBeforeLoading\(selectedCharacter\)\)' -and
    $game -match 'GateEntryHandoff.RequiresHandoff' -and
    $game -match 'ReplayDeferredWorldMessages\(\)' -and
    $game.IndexOf('if (GateEntryHandoff.RequiresHandoff)') -lt
        $game.IndexOf('if (networkManager.IsConnected)') -and
    $scene -notmatch 'gateSceneId:' -and
    $scene -match 'gateEnterWorldTimeout: 10') `
    'Selection must wait for Gate, and the game scene must consume and replay its handoff.'
$failureHandler = [regex]::Match($selection,
    '(?s)private void HandleGateEntryFailed\(string message\)(.*?)private IEnumerator LoadSceneWithProgress').Groups[1].Value
Assert-True ($failureHandler.Contains('ShowMessage(message)') -and
    $failureHandler.Contains('RefreshButtonState()') -and
    -not $failureHandler.Contains('ClearSession()') -and
    -not $failureHandler.Contains('LoadSceneOrLog(') -and
    $selection.Contains('GateEntryHandoff.TryGetDestination(network.ConnectionEpoch,') -and
    -not $selection.Contains('World map loading is not configured')) `
    'Gate failure must remain on selection, show the server message and validate the destination.'
Assert-True ($messages.Contains('public string equipmentOperationSessionId;') -and
    $game.Contains('EquipmentOperationSessionId = result.equipmentOperationSessionId') -and
    $game.Contains('EquipmentOperationSessionId = string.Empty;') -and
    $game.IndexOf('HandleEnterWorldResult(acceptedResult)') -lt
        $game.IndexOf('ReplayDeferredWorldMessages()')) `
    'The accepted equipment operation session must be saved and old session data cleared before snapshot replay.'

[UnityEngine.Time]::realtimeSinceStartup = 0
$dispatcher = New-Context
$network = [NetworkManager]::Instance
$coordinator = [GateEntryCoordinator]::new($network, $dispatcher, 1, 'character_001',
    10, [Action]{}, [Action[string]]{ param($message) })
[AuthSession]::Instance.UserId = '00002'
$coordinator.Begin()
$earlyId = [Guid]::NewGuid().ToString('D')
$earlyJson = '{"msgId":"chat_message","type":"chat_message","messageId":"' +
    $earlyId + '","channel":"private","senderCharacterDbId":1002,' +
    '"targetCharacterDbId":1,"content":"early","serverTime":10}'
$network.Receive($earlyJson)
$earlyRuntime = [Game.Chat.Runtime.WorldChatSessionRuntime]::Instance
Assert-True ($earlyRuntime.PrivateConversations.CharacterDbId -eq 0 -and
    $network.PrivateAckSendCount -eq 0) `
    'Messages preceding enter_world_result must not be saved or acknowledged.'
$throwingObserver = [Action]{ throw 'Private UI not available' }
$earlyRuntime.PrivateConversations.add_Changed($throwingObserver)
$network.Receive('{"msgId":"enter_world_result","movementProtocolVersion":2,"success":true,"characterDbId":1,"sceneId":"world_001","mapId":"map_demo_1","sceneSessionId":"session-d","snapshotRevision":4,"serverTime":20,"self":{"characterDbId":1,"characterId":"character_001"}}')
$dispatcher.ReplayDeferredWorldMessages()
Assert-True ($earlyRuntime.PrivateConversations.GetMessages(1002).Count -eq 1 -and
    $network.PrivateAckSendCount -eq 0) `
    'Deferred private messages must be saved only after identity is confirmed.'
[UnityEngine.Time]::realtimeSinceStartup = 0.1
$earlyRuntime.GetType().GetMethod('Update',
    [System.Reflection.BindingFlags]'Instance,NonPublic').Invoke(
        $earlyRuntime, @()) | Out-Null
Assert-True ($network.PrivateAckSendCount -eq 1 -and
    $network.LastPrivateChatAck.messageIds[0] -eq $earlyId) `
    'A saved early delivery must ACK after enter_world_result despite a broken UI observer.'
$earlyRuntime.PrivateConversations.remove_Changed($throwingObserver)
$network.Receive('{"msgId":"enter_world_result","movementProtocolVersion":2,"success":true,"characterDbId":2,"self":{"characterDbId":2}}')
$network.Receive($earlyJson)
$network.Receive($earlyJson.Replace('"targetCharacterDbId":1',
    '"targetCharacterDbId":2'))
[UnityEngine.Time]::realtimeSinceStartup = 0.2
$earlyRuntime.GetType().GetMethod('Update',
    [System.Reflection.BindingFlags]'Instance,NonPublic').Invoke(
        $earlyRuntime, @()) | Out-Null
Assert-True (-not [Game.Chat.Transport.WorldChatTransportInbox]::Instance.IsInWorld -and
    $earlyRuntime.PrivateConversations.CharacterDbId -eq 0 -and
    $earlyRuntime.Sender.CharacterDbId -eq 0 -and
    $network.PrivateAckSendCount -eq 1) `
    'A different role on the same connection must revoke the old store and ACK identity.'
$coordinator.Dispose()
[GateEntryHandoff]::Clear()

foreach ($destination in @(
    @{ Scene = 'world_001'; Map = 'map_demo_1' },
    @{ Scene = 'world_002'; Map = 'map_001' }
)) {
    $dispatcher = New-Context
    $network = [NetworkManager]::Instance
    $script:accepted = 0
    $script:replayed = 0
    $dispatcher.Register('world_snapshot', [Action[string]]{
        param($json) $script:replayed++
    })
    $initial = [GateEntryCoordinator]::new($network, $dispatcher, 1,
        'character_001', 10, [Action]{ $script:accepted++ },
        [Action[string]]{ param($message) throw $message })
    $initial.Begin()
    $network.Receive('{"msgId":"enter_world_result","movementProtocolVersion":2,"success":true,"characterDbId":1,"sceneId":"world_001","mapId":"map_demo_1","sceneSessionId":"session-e","snapshotRevision":4,"serverTime":1234,"self":{"characterDbId":1,"characterId":"character_001"}}')
    $initial.Dispose()
    $initialJson = ''
    Assert-True ([GateEntryHandoff]::TryTake($network.ConnectionEpoch, 1,
        'world_001', 'map_demo_1', [ref]$initialJson)) `
        'The initial connection must hand off before recovering.'
    $dispatcher.ReplayDeferredWorldMessages()
    $previousEpoch = $network.ConnectionEpoch
    $network.Disconnect()
    $dispatcher.ClearDeferredWorldMessages()
    $network.Connect()
    $recovery = [GateEntryCoordinator]::new($network, $dispatcher, 1,
        'character_001', 10, [Action]{ $script:accepted++ },
        [Action[string]]{ param($message) throw $message })
    $recovery.Begin()
    $network.Receive('{"msgId":"world_snapshot","sceneId":"' +
        $destination.Scene + '"}')
    $network.Receive('{"msgId":"enter_world_result","movementProtocolVersion":2,"success":true,"characterDbId":1,"sceneId":"' +
        $destination.Scene + '","mapId":"' + $destination.Map +
        '","sceneSessionId":"session-recovered","snapshotRevision":5,"serverTime":5678,"self":{"characterDbId":1,"characterId":"character_001","sceneId":"' +
        $destination.Scene + '"}}')
    $returnedScene = ''
    $returnedMap = ''
    Assert-True ($network.ConnectionEpoch -gt $previousEpoch -and
        $network.SendCount -eq 2 -and $script:accepted -eq 2 -and
        $script:replayed -eq 0 -and
        [GateEntryHandoff]::TryGetDestination($network.ConnectionEpoch, 1,
            [ref]$returnedScene, [ref]$returnedMap) -and
        $returnedScene -eq $destination.Scene -and
        $returnedMap -eq $destination.Map) `
        'Recovery must send a new enter_world and preserve its server-selected destination.'
    $recovery.Dispose()
    $recoveryJson = ''
    Assert-True ([GateEntryHandoff]::TryTake($network.ConnectionEpoch, 1,
        $destination.Scene, $destination.Map, [ref]$recoveryJson) -and
        $recoveryJson.Contains('"snapshotRevision":5')) `
        'Only the new connection may consume its matching recovery snapshot.'
    $dispatcher.ReplayDeferredWorldMessages()
    Assert-True ($script:replayed -eq 1) `
        'Recovery must replay current-connection world messages after accepting the new spawn.'
}

$game = Get-Content (Join-Path $root 'Assets/Scripts/GameSession.cs') -Raw
$recoveryHandler = [regex]::Match($game,
    '(?s)private void HandleRecoveryAccepted\(\)(.*?)private IEnumerator LoadRecoveredWorldShell').Groups[1].Value
Assert-True ($game.Contains('BeginRecoveryGate();') -and
    $game.Contains('if (returningToLogin || worldRecoveryPending) return;') -and
    $game.Contains('localPlayerInputController?.BeginMovement(result.movementControl, result.movementPolicy);') -and
    $game.Contains('if (!worldHandoffInProgress) SendLeaveWorld();') -and
    $game.Contains('if (worldHandoffInProgress)') -and
    $game.Contains('HandleRecoveryFailed("Gate disconnected while loading the world.') -and
    $game.Contains('if (operation.Status != AsyncOperationStatus.Succeeded)') -and
    $game.Contains('GateEntryHandoff.SetFailure(message);') -and
    $recoveryHandler.Contains('GateEntryHandoff.TryGetDestination(epoch, characterDbId,') -and
    $recoveryHandler.Contains('GateEntryHandoff.TryTake(epoch, characterDbId, sceneId, activeMapId,') -and
    $recoveryHandler.Contains('HandleEnterWorldResult(acceptedResult);') -and
    $recoveryHandler.Contains('messageDispatcher.ReplayDeferredWorldMessages();') -and
    $recoveryHandler.Contains('StartCoroutine(LoadRecoveredWorldShell());')) `
    'The game session must consume a new result or fail closed during cross-map recovery.'

Write-Output 'PASS: Gate entry, same/cross-map recovery handoffs, replay and failure boundaries.'
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

[UnityEngine.Time]::realtimeSinceStartup = 0
$dispatcher = New-Context
$network = [NetworkManager]::Instance
$script:accepted = 0
$script:failed = ''
$coordinator = [GateEntryCoordinator]::new($network, $dispatcher, 1, 'character_001',
    90, [Action]{ $script:accepted++ },
    [Action[string]]{ param($message) $script:failed = $message }, $true, 10)
$coordinator.Begin()
$network.Receive('{"msgId":"enter_world_result","success":false,"reason":"session_still_online"}')
$coordinator.CheckTimeout()
Assert-True ($network.SendCount -eq 1 -and -not $coordinator.IsCompleted) 'The old connection reason must wait before retrying.'
[UnityEngine.Time]::realtimeSinceStartup = 1
$coordinator.CheckTimeout()
Assert-True ($network.SendCount -eq 2 -and $network.LastRequest.sessionLifecycleVersion -eq 1) 'Retry must resend V1 without a new login.'
$network.Receive('{"msgId":"enter_world_result","success":false,"reason":"session_service_unavailable"}')
[UnityEngine.Time]::realtimeSinceStartup = 3
$coordinator.CheckTimeout()
Assert-True ($network.SendCount -eq 3 -and $network.LastRequest.token -eq [AuthSession]::Instance.GameSessionToken) 'Service failures must back off using the existing ticket.'
$network.Receive('{"msgId":"enter_world_result","success":false,"reason":"token_invalid_or_expired"}')
[UnityEngine.Time]::realtimeSinceStartup = 30
$coordinator.CheckTimeout()
Assert-True ($network.SendCount -eq 3 -and $script:accepted -eq 0 -and -not $network.IsConnected -and
    -not [AuthSession]::Instance.HasValidGameSession -and $script:failed -eq 'token_invalid_or_expired') 'Token rejection must end retries regardless of localized message text.'

[UnityEngine.Time]::realtimeSinceStartup = 0
$dispatcher = New-Context
$network = [NetworkManager]::Instance
$coordinator = [GateEntryCoordinator]::new($network, $dispatcher, 1, 'character_001',
    90, [Action]{ throw 'Unexpected accept.' }, [Action[string]]{ param($message) throw $message }, $true, 10)
$coordinator.Begin()
$epoch = $network.ConnectionEpoch
[UnityEngine.Time]::realtimeSinceStartup = 10
$coordinator.CheckTimeout()
Assert-True (-not $network.IsConnected -and $network.SendCount -eq 1) 'A hung response must close only the failed attempt.'
[UnityEngine.Time]::realtimeSinceStartup = 11
$coordinator.CheckTimeout()
Assert-True ($network.IsConnected -and $network.ConnectionEpoch -gt $epoch -and $network.SendCount -eq 2) 'A hung response must be retried on a fresh TCP connection.'
$coordinator.Dispose()
Write-Output 'PASS: busy/service-unavailable backoff, terminal invalidation, and a hung attempt on a new connection.'

$dispatcher = New-Context
$network = [NetworkManager]::Instance
$script:accepted = 0
$script:failed = ''
$coordinator = [GateEntryCoordinator]::new($network, $dispatcher, 1, 'character_001', 10,
    [Action]{ $script:accepted++ }, [Action[string]]{ param($message) $script:failed = $message })
$coordinator.Begin()
$network.Receive('{"msgId":"error","requestMsgId":"cast_skill","errorCode":"movement_client_update_required","message":"Unrelated error"}')
Assert-True (-not $coordinator.IsCompleted) 'An unrelated error must not fail world entry.'
$network.Receive('{"msgId":"error","requestMsgId":"enter_world","errorCode":"movement_client_update_required","message":"Upgrade client"}')
Assert-True ($coordinator.IsCompleted -and $script:accepted -eq 0 -and
    $script:failed -eq 'Upgrade client' -and -not $network.IsConnected) `
    'The Gate version rejection must bypass deferred world messages and fail entry immediately.'
Write-Output 'PASS: the movement broadcast version error fails entry immediately.'

$dispatcher = New-Context
$network = [NetworkManager]::Instance
$script:sessionMessages = 0
$script:bufferedMessages = 0
foreach ($id in @('heartbeat_result','account_kicked')) {
    $dispatcher.Register($id, [Action[string]]{ param($json) $script:sessionMessages++ })
}
$dispatcher.Register('error', [Action[string]]{
    param($json)
    if ($json.Contains('"heartbeat"')) { $script:sessionMessages++ }
    else { $script:bufferedMessages++ }
})
$dispatcher.Register('world_snapshot', [Action[string]]{ param($json) $script:bufferedMessages++ })
$network.Connect()
$dispatcher.BeginDeferredWorldMessages()
$network.Receive('{"msgId":"world_snapshot"}')
$network.Receive('{"msgId":"error","requestMsgId":"skill_bar_save"}')
$network.Receive('{"msgId":"heartbeat_result","success":true}')
$network.Receive('{"msgId":"error","requestMsgId":"heartbeat"}')
$network.Receive('{"msgId":"account_kicked","reason":"logout"}')
Assert-True ($script:sessionMessages -eq 3 -and $script:bufferedMessages -eq 0) 'Connection control must run while world packets are buffered.'
$dispatcher.ReplayDeferredWorldMessages()
Assert-True ($script:sessionMessages -eq 3 -and $script:bufferedMessages -eq 2) 'Session control must never replay with world packets.'
Write-Output 'PASS: heartbeat, terminal notices and heartbeat errors bypass world snapshot buffering.'
