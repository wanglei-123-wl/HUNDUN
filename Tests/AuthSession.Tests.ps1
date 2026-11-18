$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Get-Content (Join-Path $root 'Assets/Scripts/LoginScripts/AuthSession.cs') -Raw

# Use an in-memory PlayerPrefs double; never touch Unity's real login data.
$stubs = @'
public sealed class EnterWorldRequest
{
    public long characterDbId, time;
    public string characterId, token;
    public int sessionLifecycleVersion = 1;
}
public sealed class PlayerNetworkState
{
    public long characterDbId;
    public string userId;
}
public sealed class EnterWorldResult
{
    public bool success;
    public long characterDbId, serverTime, gameSessionExpiresAt;
    public int reconnectWindowSeconds;
    public string userId;
    public PlayerNetworkState self;
}
public sealed class HeartbeatAckMessage
{
    public bool success;
    public long serverTime, gameSessionExpiresAt;
    public int reconnectWindowSeconds;
}
public sealed class AccountKickedMessage { public string reason; }
public sealed class GameSessionTransportError { public string requestMsgId, errorCode; }
public sealed class NetworkManager
{
    public static NetworkManager Instance;
    public long ConnectionEpoch = 1;
    public string EndpointIdentity = "Current";
    public bool IsConnected = true;
    public bool ReconnectSuspended;
    public int Disconnects;
    public EnterWorldRequest LastRequest;
    public bool Send(EnterWorldRequest request) { LastRequest = request; return IsConnected; }
    public void SuspendAutoReconnect() { ReconnectSuspended = true; }
    public void Disconnect() { IsConnected = false; Disconnects++; }
}
public sealed class NetMessageDispatcher
{
    private readonly System.Collections.Generic.Dictionary<string, System.Action<string>> handlers =
        new System.Collections.Generic.Dictionary<string, System.Action<string>>();
    public void Register(string id, System.Action<string> action) { handlers[id] = action; }
    public void Unregister(string id, System.Action<string> action) { handlers.Remove(id); }
    public void Receive(string id, string json) { if (handlers.TryGetValue(id, out var action)) action(json); }
}
public sealed class AuthResult { public bool success; }
public sealed class AuthApiClient
{
    public static string SentToken;
    public static System.Action<AuthResult> Complete;
    public void LogoutGameSession(string token, System.Action<AuthResult> handler)
    {
        SentToken = token;
        Complete = handler;
    }
}
public static class AuthSessionTest
{
    public static bool Send(AuthSession auth, NetworkManager network, NetMessageDispatcher dispatcher, long character) =>
        auth.TrySendEnterWorld(network, dispatcher, character, "character_001");
    public static bool Accept(AuthSession auth, NetworkManager network, long epoch, EnterWorldResult reply) =>
        auth.AcceptGameEntry(network, epoch, reply);
    public static void Logout(AuthSession auth, System.Action callback) => auth.LogoutGameSession(callback);
}
public sealed class NetworkEndpointSnapshot
{
    public string Identity;
}
public static class NetworkEndpointRuntime
{
    public static NetworkEndpointSnapshot Current = new NetworkEndpointSnapshot { Identity = "Current" };
    public static NetworkEndpointSnapshot Load() { return Current; }
}
public static class GateEntryHandoff
{
    public static int Clears;
    public static void Clear() { Clears++; }
}
namespace UnityEngine
{
    public class GameObject
    {
        public int GetInstanceID() { return 2; }
        public T AddComponent<T>() where T : new() { return new T(); }
    }
    public class MonoBehaviour
    {
        public GameObject gameObject = new GameObject();
        public int GetInstanceID() { return 1; }
        public T GetComponent<T>() where T : class { return null; }
        protected static void Destroy(object value) {}
        protected static void DontDestroyOnLoad(object value) {}
    }
    public static class Time { public static double realtimeSinceStartupAsDouble; }
    public static class Application
    {
        public static System.Func<bool> wantsToQuit { get; set; }
        public static void Quit() {}
    }
    public static class JsonUtility
    {
        public static T FromJson<T>(string value) => System.Text.Json.JsonSerializer.Deserialize<T>(
            value, new System.Text.Json.JsonSerializerOptions { IncludeFields = true });
    }
    public static class Debug
    {
        public static void Log(object message) {}
        public static void LogWarning(object message) {}
    }
    public static class PlayerPrefs
    {
        private static readonly System.Collections.Generic.Dictionary<string, string> Values =
            new System.Collections.Generic.Dictionary<string, string>();
        public static void SetString(string key, string value) { Values[key] = value; }
        public static string GetString(string key, string fallback)
        {
            return Values.TryGetValue(key, out string value) ? value : fallback;
        }
        public static void DeleteKey(string key) { Values.Remove(key); }
        public static void Save() {}
    }
}
'@
Add-Type -TypeDefinition ($source + "`n" + $stubs)

function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
}

$future = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds() + 3600
$session = [AuthSession]::new()
$session.SaveSession('login-token-for-test', 'account-test', 'user-test')
Assert-True ($session.GetAuthorizationHeader() -eq 'Bearer login-token-for-test') 'HTTP must use login token.'
Assert-True (-not $session.HasValidGameSession) 'Login alone must not authorize Gate.'

Assert-True ($session.SaveGameSession('00001.fake_game_session_token', $future)) 'Valid game token must be accepted.'
Assert-True (-not $session.IsLoggedIn) 'Selection must revoke local HTTP login state.'
Assert-True ($session.LoginToken -eq '') 'Revoked login token must be removed.'
Assert-True ($session.GetAuthorizationHeader() -eq '') 'Game token must never become an HTTP credential.'
Assert-True ($session.UserId -eq 'account-test') 'Selection must preserve account identity.'

$loaded = [AuthSession]::new()
$loaded.LoadSession()
Assert-True ($loaded.GameSessionToken -eq $session.GameSessionToken) 'Game token must survive local storage round trip.'
Assert-True ($loaded.GameSessionExpiresAt -eq $future) 'Expiry must survive local storage round trip.'
Assert-True (-not $loaded.IsLoggedIn) 'Revoked login token must not reappear after loading.'
Assert-True ([AuthSession]::MaskToken($loaded.GameSessionToken) -eq '00001..._token') 'Mask must show only prefix and suffix.'
Assert-True ([AuthSession]::MaskToken('12345678901') -eq '<redacted-short-token>') 'Short tokens must not be exposed.'
Assert-True ([AuthSession]::MaskToken('') -eq '<empty>') 'Missing tokens must be identifiable.'

$loaded.SaveSession('fresh-login-token', 'account-test', 'user-test')
Assert-True ($loaded.GameSessionToken -eq '') 'New login must clear previous game token.'
$session.LoadSession()
Assert-True ($session.GameSessionToken -eq '') 'New login must clear persisted game token.'
Assert-True (-not $session.SaveGameSession('expired-token', 1)) 'Expired game token must be rejected.'
Assert-True ($session.UserId -eq '') 'Invalid game session must clear identity.'
Assert-True (-not $session.SaveGameSession('', $future)) 'Missing game token must be rejected.'
$loaded.LoadSession()
Assert-True ($loaded.GameSessionToken -eq '' -and $loaded.LoginToken -eq '') 'Invalid selection must clear stored credentials.'

$session.SaveSession('login', 'account', 'user')
$null = $session.SaveGameSession('valid-game-token', $future)
$session.ClearSession()
$loaded.LoadSession()
Assert-True ($loaded.GameSessionToken -eq '' -and $loaded.UserId -eq '') 'Logout must clear persisted game state.'

$session.SaveSession('old-server-token', 'old-account', 'old-user')
[UnityEngine.PlayerPrefs]::SetString('selected_character_id', 'old-character')
[UnityEngine.PlayerPrefs]::SetString('selected_character_db_id', '42')
[NetworkEndpointRuntime]::Current = [NetworkEndpointSnapshot]@{ Identity = 'Remote' }
$loaded.LoadSession()
Assert-True ($loaded.LoginToken -eq '' -and $loaded.UserId -eq '') 'Switching environments must clear the old login.'
Assert-True ([UnityEngine.PlayerPrefs]::GetString('selected_character_id', '') -eq '' -and
    [UnityEngine.PlayerPrefs]::GetString('selected_character_db_id', '') -eq '') `
    'Switching environments must clear the old character selection.'
Assert-True ([GateEntryHandoff]::Clears -gt 0) 'Switching environments must clear Gate handoff.'
$loaded.SaveSession('new-server-token', 'new-account', 'new-user')
$session.LoadSession()
Assert-True ($session.LoginToken -eq 'new-server-token') 'Same environment must retain its own login.'

[NetworkEndpointRuntime]::Current = $null
$session.LoadSession()
Assert-True ($session.LoginToken -eq '') 'Missing environment must not restore a saved login.'
Write-Output 'PASS: AuthSession persistence, token separation, expiry, cleanup and masking.'

# Keep the transport and HTTP callbacks in memory while exercising the real session object.
[NetworkEndpointRuntime]::Current = [NetworkEndpointSnapshot]@{ Identity = 'Current' }
$session.SaveSession('login-before-selection', 'account-test', 'user-test')
$null = $session.SaveGameSession('same-game-ticket', $future)
$network = [NetworkManager]::new()
[NetworkManager]::Instance = $network
$dispatcher = [NetMessageDispatcher]::new()
Assert-True ([AuthSessionTest]::Send($session, $network, $dispatcher, 42)) 'The selected game token must be sent through the bound Gate.'
Assert-True ($network.LastRequest.sessionLifecycleVersion -eq 1 -and $network.LastRequest.token -eq 'same-game-ticket') 'Gate entry must opt into lifecycle V1 with the original token.'
Assert-True (-not [AuthSessionTest]::Accept($session, $network, 1, [EnterWorldResult]@{ success = $true; characterDbId = 43 })) 'Another character cannot confirm the ticket.'
$serverSeconds = 2000000000L
$serverMs = $serverSeconds * 1000
[UnityEngine.Time]::realtimeSinceStartupAsDouble = 100
Assert-True ([AuthSessionTest]::Accept($session, $network, 1, [EnterWorldResult]@{
    success = $true; characterDbId = 42; serverTime = $serverMs; gameSessionExpiresAt = ($serverSeconds + 4); reconnectWindowSeconds = 9
})) 'Entry metadata must accept Unix seconds and milliseconds in their documented fields.'
[UnityEngine.Time]::realtimeSinceStartupAsDouble = 104
Assert-True (-not $session.HasValidGameSession) 'Ticket expiry must follow the server clock estimate and monotonic elapsed time.'
$dispatcher.Receive('heartbeat_result', ('{"success":true,"serverTime":' + ($serverMs + 4000) +
    ',"gameSessionExpiresAt":' + ($serverSeconds + 40) + '}'))
Assert-True ($session.HasValidGameSession -and $session.GameSessionToken -eq 'same-game-ticket' -and
    $session.GameSessionExpiresAt -eq ($serverSeconds + 40) -and $session.ReconnectWindowSeconds -eq 9) 'Heartbeat must renew the same ticket and preserve omitted fields.'
$dispatcher.Receive('heartbeat_result', ('{"success":true,"serverTime":' + ($serverMs + 5000) + '}'))
Assert-True ($session.GameSessionExpiresAt -eq ($serverSeconds + 40)) 'An omitted expiry must never erase the confirmed deadline.'
$network.ConnectionEpoch = 2
$dispatcher.Receive('heartbeat_result', ('{"success":true,"serverTime":' + ($serverMs + 6000) +
    ',"gameSessionExpiresAt":' + ($serverSeconds + 60) + '}'))
$dispatcher.Receive('account_kicked', '{"reason":"kicked_by_new_login"}')
Assert-True ($session.GameSessionToken -eq 'same-game-ticket' -and $session.GameSessionExpiresAt -eq ($serverSeconds + 40)) 'A stale connection epoch must neither renew nor revoke the ticket.'
Assert-True ([AuthSessionTest]::Send($session, $network, $dispatcher, 42)) 'Reconnection must send the same selected ticket.'
$network.IsConnected = $false
$dispatcher.Receive('account_kicked', '{"reason":"kicked_by_new_login"}')
Assert-True ($network.ReconnectSuspended -and $session.GameSessionToken -eq '') 'Final notices read before EOF must still terminate the active epoch.'

$session.SaveSession('fresh-login', 'account-test', 'user-test')
$null = $session.SaveGameSession('logout-game-ticket', $future)
$network = [NetworkManager]::new()
[NetworkManager]::Instance = $network
$script:logoutFinished = $false
[AuthSessionTest]::Logout($session, [Action]{ $script:logoutFinished = $true })
Assert-True ([AuthApiClient]::SentToken -eq 'logout-game-ticket' -and $network.ReconnectSuspended -and
    $network.IsConnected -and $network.Disconnects -eq 0 -and -not $script:logoutFinished) 'Logout must wait on HTTP with the game ticket and retries suspended.'
[AuthApiClient]::Complete.Invoke([AuthResult]@{ success = $true })
Assert-True ($script:logoutFinished -and $network.Disconnects -eq 1 -and $session.GameSessionToken -eq '') 'Logout completion must clear the ticket and close the original TCP connection.'
Write-Output 'PASS: lifecycle V1, monotonic expiry, stale epoch, final kick, and HTTP-before-disconnect.'
