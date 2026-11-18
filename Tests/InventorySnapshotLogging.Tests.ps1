$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Get-Content (Join-Path $root 'Assets/Scripts/NetWork/NetMessageDispatcher.cs') -Raw
$stubs = @'
public sealed class NetworkManager
{
    public static NetworkManager Instance { get; set; }
    public bool IsConnected { get; set; }
    public event System.Action<string> OnMessageReceived;
}

namespace Game.Client.BattleSync
{
    public interface IBattleSyncMessageRouter
    {
        void Register(string msgId, System.Action<string> handler);
        void Unregister(string msgId, System.Action<string> handler);
    }

    public static class BattleSyncDiagnosticsLogger
    {
        public static void LogDispatch(string phase, string msgId, string type,
            long reliableMessageId, bool accepted, bool deferred, int count) {}
        public static void LogDispatchException(string msgId,
            long reliableMessageId, string message) {}
    }
}

namespace UnityEngine
{
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public sealed class DefaultExecutionOrder : System.Attribute
    {
        public DefaultExecutionOrder(int order) { }
    }

    public class MonoBehaviour
    {
        public object gameObject;
        public bool enabled = true;
        protected static void Destroy(object value) { }
        protected static void DontDestroyOnLoad(object value) { }
    }

    public static class JsonUtility
    {
        public static T FromJson<T>(string json) =>
            System.Text.Json.JsonSerializer.Deserialize<T>(json,
                new System.Text.Json.JsonSerializerOptions { IncludeFields = true });
    }

    public static class Debug
    {
        public static readonly System.Collections.Generic.List<string> Messages =
            new System.Collections.Generic.List<string>();
        public static readonly System.Collections.Generic.List<string> Warnings =
            new System.Collections.Generic.List<string>();
        public static void Log(string message) => Messages.Add(message);
        public static void LogWarning(string message) => Warnings.Add(message);
    }
}
'@

Add-Type -TypeDefinition ($source + "`n" + $stubs) -IgnoreWarnings
$dispatcher = [NetMessageDispatcher]::new()
$secret = 'private-instance-id'
$v2 = '{"msgId":"inventory_snapshot_result","type":"inventory_snapshot_result","equipmentProtocolVersion":2,"equipmentCatalogVersion":2,"requestSequence":7,"success":true,"capacity":60,"errorCode":"","equipmentItems":[{"itemInstanceId":"' + $secret + '"}]}'
$dispatcher.Dispatch($v2)
$messages = [UnityEngine.Debug]::Messages
if ($messages.Count -ne 1 -or
    -not $messages[0].Contains('protocolVersion=2') -or
    -not $messages[0].Contains('catalogVersion=2') -or
    -not $messages[0].Contains('requestSequence=7') -or
    -not $messages[0].Contains('success=True') -or
    -not $messages[0].Contains('capacity=60') -or
    $messages[0].Contains($secret) -or
    $messages[0].Contains('equipmentItems')) {
    throw 'V2 snapshot must log only its safe header fields before handler lookup.'
}

$dispatcher.Dispatch('{"msgId":"inventory_snapshot_result","type":"inventory_snapshot_result","capacity":40,"errorCode":"invalid_request"}')
if ($messages.Count -ne 2 -or
    -not $messages[1].Contains('protocolVersion=0') -or
    -not $messages[1].Contains('requestSequence=0') -or
    -not $messages[1].Contains('capacity=40') -or
    -not $messages[1].Contains('errorCode=invalid_request')) {
    throw 'Legacy or invalid snapshot fields must remain visible for diagnosis.'
}

$dispatcher.Dispatch('{"msgId":"heartbeat","token":"do-not-log"}')
if ($messages.Count -ne 2) {
    throw 'Unrelated messages must not be logged by snapshot diagnostics.'
}

Write-Output 'PASS: inventory snapshot headers logged without raw payload.'
