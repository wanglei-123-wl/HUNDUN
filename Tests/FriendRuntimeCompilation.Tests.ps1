$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$files = @(
    'Assets/Scripts/Friend/Protocol/FriendMessages.cs',
    'Assets/Scripts/Friend/Model/FriendRequestQueue.cs',
    'Assets/Scripts/Friend/Model/FriendState.cs',
    'Assets/Scripts/Friend/Transport/FriendTransportInbox.cs',
    'Assets/Scripts/Friend/Runtime/FriendSessionRuntime.cs'
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
public class NetworkManager
{
    public long ConnectionEpoch { get; set; }
    public bool IsConnected { get; set; }
    public object LastRequest;
    public UnityEngine.GameObject gameObject = new UnityEngine.GameObject();
    public event Action<long> OnConnectedWithEpoch;
    public event Action<long, string> OnDisconnectedWithEpoch;
    public T GetComponent<T>() where T : class { return null; }
    public bool Send<T>(T request) { LastRequest = request; return true; }
}
public class NetMessageDispatcher
{
    public void Register(string id, Action<string> callback) { }
    public void Unregister(string id, Action<string> callback) { }
}
public class GameSession
{
    public event Action<long, long> LocalWorldEntered;
    public void EnterWorld(long epoch, long characterDbId)
    {
        LocalWorldEntered?.Invoke(epoch, characterDbId);
    }
}
namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class DisallowMultipleComponent : Attribute { }
    public class MonoBehaviour { }
    public class GameObject
    {
        public T AddComponent<T>() where T : new() { return new T(); }
    }
    public static class Time
    {
        public static double realtimeSinceStartupAsDouble;
    }
    public static class JsonUtility
    {
        public static T FromJson<T>(string text) =>
            System.Text.Json.JsonSerializer.Deserialize<T>(text,
                new System.Text.Json.JsonSerializerOptions { IncludeFields = true });
    }
    public static class Debug
    {
        public static void LogWarning(string text) { }
    }
}
'@
Add-Type -TypeDefinition ("using System;`nusing System.Collections.Generic;`n" +
    "using System.Text;`nusing Game.Friend.Protocol;`n" +
    "using Game.Friend.Model;`nusing Game.Friend.Transport;`n" +
    "using UnityEngine;`n" + $stubs + $sources) -IgnoreWarnings | Out-Null
function Check([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}
$network = [NetworkManager]::new()
$network.ConnectionEpoch = 8
$network.IsConnected = $true
$dispatcher = [NetMessageDispatcher]::new()
$session = [GameSession]::new()
$runtime = [Game.Friend.Runtime.FriendSessionRuntime]::Ensure(
    $network, $dispatcher, $session)
$session.EnterWorld(8, 1001)
Check ($network.LastRequest.msgId -eq 'friend_list' -and
    $network.LastRequest.requestSequence -eq 1) `
    'A world entry must fetch the first friend list in this connection.'
$flags = [System.Reflection.BindingFlags]'Instance,NonPublic'
$receive = $runtime.GetType().GetMethod('HandlePacket', $flags)
$emptyList = '{"msgId":"friend_list_result","type":"friend_list_result","requestSequence":1,"friends":[],"blocks":[],"recentContacts":[],"pendingReceivedRequests":[],"pendingSentRequests":[]}'
$receive.Invoke($runtime, @([long]8, 'friend_list_result', $emptyList)) | Out-Null
Check ($runtime.State.HasSnapshot -and $runtime.Search('offline') -and
    $network.LastRequest.requestSequence -eq 2) `
    'Search must follow the list using the shared friend request sequence.'
$receive.Invoke($runtime, @([long]8, 'friend_search_result',
    '{"msgId":"friend_search_result","type":"friend_search_result","requestSequence":2,"success":true,"players":[{"characterDbId":1002,"online":false}]}')) | Out-Null
Check ($runtime.SendRequest(1002) -and
    $network.LastRequest.requestSequence -eq 3 -and
    $network.LastRequest.targetCharacterDbId -eq 1002) `
    'An offline target must be sent by numeric character ID on the next sequence.'
$requestId = [Guid]::NewGuid().ToString('D')
$result = '{"msgId":"friend_request_send_result","type":"friend_request_send_result","requestSequence":3,"success":true,"requestId":"' +
    $requestId + '"}'
$receive.Invoke($runtime, @([long]8, 'friend_request_send_result', $result)) | Out-Null
Check ($runtime.State.SentRequestIds[[long]1002] -eq $requestId -and
    $runtime.State.PendingSent.Count -eq 0 -and
    $runtime.Requests.NextSequence -eq 4) `
    'A matched success must retain the server ID without inventing list-only fields.'
Check ($runtime.RequestList() -and $network.LastRequest.requestSequence -eq 4) `
    'Refreshing sent requests must use the next friend sequence.'
$refreshed = '{"msgId":"friend_list_result","type":"friend_list_result","requestSequence":4,"friends":[],"blocks":[],"recentContacts":[],"pendingReceivedRequests":[],"pendingSentRequests":[{"requestId":"' +
    $requestId + '","toCharacterDbId":1002,"status":"pending"}]}'
$receive.Invoke($runtime, @([long]8, 'friend_list_result', $refreshed)) | Out-Null
Check ($runtime.State.SentRequestIds.Count -eq 0 -and
    $runtime.State.PendingSent.ContainsKey($requestId)) `
    'A later list must replace provisional IDs with server-owned request data.'
Write-Output 'PASS: friend runtime compiles and retains offline request IDs until list reconciliation.'
