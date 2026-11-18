$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Player/LocalPlayerLifeController.cs') -Raw
$stubs = @'
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Game.Client.BattleSync;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class HeaderAttribute : Attribute
    {
        public HeaderAttribute(string value) {}
    }
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SerializeField : Attribute {}
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class MinAttribute : Attribute
    {
        public MinAttribute(float value) {}
    }
    public class Coroutine {}
    public class WaitForSecondsRealtime
    {
        public WaitForSecondsRealtime(float value) {}
    }
    public class MonoBehaviour
    {
        protected Coroutine StartCoroutine(IEnumerator routine) => new Coroutine();
        protected void StopCoroutine(Coroutine routine) {}
        protected static T FindObjectOfType<T>() where T : class => null;
    }
    public class GameObject
    {
        public bool activeSelf;
        public void SetActive(bool value) { activeSelf = value; }
    }
    public static class Time
    {
        public static float realtimeSinceStartup = 100f;
        public static double realtimeSinceStartupAsDouble => realtimeSinceStartup;
    }
    public static class Mathf
    {
        public static float Max(float a, float b) => Math.Max(a, b);
    }
    public static class Debug
    {
        public static void LogWarning(string message) {}
    }
    public static class JsonUtility
    {
        public static T FromJson<T>(string json) =>
            System.Text.Json.JsonSerializer.Deserialize<T>(json,
                new System.Text.Json.JsonSerializerOptions { IncludeFields = true });
    }
}
namespace TMPro
{
    public class TMP_Text { public string text; }
}
namespace UnityEngine.UI
{
    public class Button
    {
        public bool interactable;
        public readonly ButtonClickedEvent onClick = new ButtonClickedEvent();
        public class ButtonClickedEvent
        {
            public void AddListener(Action action) {}
            public void RemoveListener(Action action) {}
        }
    }
}
namespace Game.Client.BattleSync
{
    public static class BattleSyncProtocolConstants
    {
        public static class MessageIds
        {
            public const string DeathTimeoutKick = "death_timeout_kick";
        }
    }
}
public sealed class CombatEntityKey
{
    public string EntityType { get; }
    public string EntityId { get; }
    public CombatEntityKey(string type, string id)
    {
        EntityType = type;
        EntityId = id;
    }
}
public sealed class CombatStateData
{
    public int Hp;
    public string LifeState;
    public long CombatRevision;
}
public sealed class CombatStateStore
{
    public event Action<CombatEntityKey, CombatStateData> StateChanged;
    public bool TryGetState(string type, string id, out CombatStateData state)
    {
        state = null;
        return false;
    }
}
public sealed class CombatMessageReceiver
{
    public event Action<CombatEventMessage> CombatEventApplied;
    public void Emit(CombatEventMessage message) => CombatEventApplied?.Invoke(message);
}
public sealed class GameSession
{
    public long LocalCharacterDbId = 1;
    public long CurrentClientTick = 1;
    public bool IsLocalPlayerDead { get; set; }
    public bool AcceptPosition = true;
    public int AppliedPositions;
    public int Resumed;
    public int Prepared;
    public PlayerAuthoritativeState LastPosition;
    public event Action<PlayerAuthoritativeState> LocalAuthoritativeStateReceived;
    public event Action<long, long> LocalWorldEntered;
    public void EnterLocalDeadState() {}
    public void PrepareLocalRespawn() { Prepared++; }
    public bool ApplyLocalRespawnPosition(PlayerAuthoritativeState state)
    {
        AppliedPositions++;
        LastPosition = state;
        return AcceptPosition;
    }
    public void ResumeLocalPlayerControl() { Resumed++; }
    public void RecoverWorldSession(string reason) {}
    public void ReturnToLogin(string reason) {}
    public void Emit(PlayerAuthoritativeState state) =>
        LocalAuthoritativeStateReceived?.Invoke(state);
}
public sealed class NetworkManager
{
    public static NetworkManager Instance;
    public long ConnectionEpoch = 1;
    public bool IsConnected = true;
    public int Sends;
    public RespawnRequest LastRequest;
    public event Action<string> OnDisconnected;
    public bool SendRespawn(RespawnRequest request)
    {
        Sends++;
        LastRequest = request;
        return IsConnected;
    }
    public void Disconnect()
    {
        IsConnected = false;
        OnDisconnected?.Invoke("closed");
    }
}
public sealed class NetMessageDispatcher
{
    public static NetMessageDispatcher Instance;
    private readonly Dictionary<string, Action<string>> handlers =
        new Dictionary<string, Action<string>>();
    public void Register(string id, Action<string> handler)
    {
        if (!handlers.ContainsKey(id)) handlers[id] = null;
        handlers[id] += handler;
    }
    public void Unregister(string id, Action<string> handler)
    {
        if (handlers.ContainsKey(id)) handlers[id] -= handler;
    }
    public void Emit(string id, string json)
    {
        if (handlers.TryGetValue(id, out Action<string> handler)) handler?.Invoke(json);
    }
}
public sealed class RespawnRequest
{
    public long requestSequence;
    public long clientTick;
}
public sealed class ServerErrorMessage
{
    public string requestMsgId;
    public string errorCode;
    public string message;
    public long expectedRequestSequence;
}
public sealed class DeathTimeoutKickMessage
{
    public string errorCode;
}
public sealed class PlayerAuthoritativeState
{
    public long serverTick;
    public float x, y, z, rotY;
}
public sealed class CombatEntityMessage
{
    public string entityType;
    public string entityId;
}
public sealed class CombatEventMessage
{
    public string eventType;
    public CombatEntityMessage source;
    public CombatEntityMessage target;
    public long combatRevision;
    public int hpAfter;
    public long serverTick;
    public long serverTime;
    public long respawnAvailableAt;
    public long deathKickAt;
    public string killerName;
}
'@

Add-Type -TypeDefinition ($stubs + "`n" +
    ($source -replace '(?m)^using [^\r\n]+;\r?\n', '')) -IgnoreWarnings | Out-Null
function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
}
function Set-Field($target, [string]$name, $value) {
    $target.GetType().GetField($name,
        [Reflection.BindingFlags]'Instance,NonPublic').SetValue($target, $value)
}
function Invoke-Private($target, [string]$name) {
    $target.GetType().GetMethod($name,
        [Reflection.BindingFlags]'Instance,NonPublic').Invoke($target, @()) | Out-Null
}

[NetworkManager]::Instance = [NetworkManager]::new()
[NetMessageDispatcher]::Instance = [NetMessageDispatcher]::new()
$receiver = [CombatMessageReceiver]::new()
$session = [GameSession]::new()
$button = [UnityEngine.UI.Button]::new()
$label = [TMPro.TMP_Text]::new()
$message = [TMPro.TMP_Text]::new()
$life = [LocalPlayerLifeController]::new()
Set-Field $life 'gameSession' $session
Set-Field $life 'combatReceiver' $receiver
Set-Field $life 'respawnButton' $button
Set-Field $life 'respawnButtonText' $label
Set-Field $life 'deathMessageText' $message
Invoke-Private $life 'Awake'
Invoke-Private $life 'OnEnable'

$death = [CombatEventMessage]::new()
$death.eventType = 'death'
$death.target = [CombatEntityMessage]::new()
$death.target.entityType = 'player'
$death.target.entityId = '1'
$death.combatRevision = 1
$death.serverTick = 2
$death.serverTime = 100000
$death.respawnAvailableAt = 100000
$death.deathKickAt = 200000
$receiver.Emit($death)
Assert-True ($button.interactable -and $label.text -eq '复活') `
    'Death event must permit respawn only after the server deadline.'
$life.RequestRespawn()
Assert-True ([NetworkManager]::Instance.Sends -eq 1 -and
    [NetworkManager]::Instance.LastRequest.requestSequence -eq 1 -and
    [NetworkManager]::Instance.LastRequest.clientTick -eq 1 -and
    -not [NetworkManager]::Instance.LastRequest.PSObject.Properties['x']) `
    'Respawn request must not choose a client coordinate.'
[NetMessageDispatcher]::Instance.Emit('error',
    '{"requestMsgId":"respawn","errorCode":"respawn_location_unavailable","message":"No safe location"}')
Assert-True (-not $button.interactable -and
    $message.text -eq 'No safe location' -and
    $label.text -eq '暂无安全落点') `
    'Unavailable location must show the server error and block immediate retry.'
$life.RequestRespawn()
Assert-True ([NetworkManager]::Instance.Sends -eq 1) `
    'Repeated clicks during a missing-safe-location cooldown must not send requests.'
[NetMessageDispatcher]::Instance.Emit('error',
    '{"requestMsgId":"respawn","errorCode":"respawn_sequence_gap","expectedRequestSequence":99}')
Assert-True ($message.text -eq 'No safe location') `
    'A late error without a pending request must not overwrite the location failure.'
[UnityEngine.Time]::realtimeSinceStartup = 111
Invoke-Private $life 'RefreshRespawnButton'
Assert-True ($button.interactable) 'The retry may become available after the cooldown.'
$life.RequestRespawn()
Assert-True ([NetworkManager]::Instance.Sends -eq 2) `
    'A later explicit retry must still use the existing respawn protocol.'

$respawn = [CombatEventMessage]::new()
$respawn.eventType = 'respawn'
$respawn.target = $death.target
$respawn.combatRevision = 2
$respawn.hpAfter = 10
$respawn.serverTick = 7
$receiver.Emit($respawn)
$stale = [PlayerAuthoritativeState]::new()
$stale.serverTick = 6
$session.Emit($stale)
Assert-True ($session.AppliedPositions -eq 0 -and $session.Resumed -eq 0) `
    'An authority frame predating the respawn event cannot place the player.'
$valid = [PlayerAuthoritativeState]::new()
$valid.serverTick = 7
$valid.x = 3
$valid.y = 4
$valid.z = 5
$valid.rotY = 90
$session.AcceptPosition = $false
$session.Emit($valid)
Assert-True ($session.Resumed -eq 0) `
    'Control must remain disabled until the authoritative position is applied.'
$session.AcceptPosition = $true
$session.Emit($valid)
Assert-True ($session.AppliedPositions -eq 2 -and
    $session.Resumed -eq 1 -and
    $session.LastPosition.x -eq 3 -and
    $session.LastPosition.y -eq 4 -and
    $session.LastPosition.z -eq 5) `
    'Respawn must use the server position and only then resume input.'

$gameSource = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/GameSession.cs') -Raw
$authority = [regex]::Match($gameSource,
    '(?s)public bool ApplyLocalRespawnPosition\(PlayerAuthoritativeState state\)(.*?)public void ResumeLocalPlayerControl').Groups[1].Value
Assert-True ($authority.Contains('state.GetPosition()') -and
    $authority.Contains('state.GetRotation()') -and
    $authority.Contains('localPlayerReconciler.ResetAtSpawn(position, rotation)') -and
    $authority.Contains('localPlayerVisualSmoother?.ResetVisual()')) `
    'The game session must reset prediction and visuals at the server-selected position.'
Invoke-Private $life 'OnDisable'
Write-Output 'PASS: respawn location error, retry throttle, event ordering and authoritative reset.'
