$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Get-Content (Join-Path $root 'Assets/Scripts/NetWork/NetworkManager.cs') -Raw
$method = [regex]::Match($source,
    '(?s)    private bool ShouldDispatchReceivedMessage\(long receivedEpoch\)\s*\{.*?\n    \}')
if (-not $method.Success) {
    throw 'Network epoch dispatch guard was not found.'
}
$failureMethod = [regex]::Match($source,
    '(?s)    private bool ShouldHandleConnectionFailure\(long failedEpoch\)\s*\{.*?\n    \}')
if (-not $failureMethod.Success) {
    throw 'Network failure callback guard was not found.'
}

$messageCallback = [regex]::Match($source,
    '(?s)EnqueueMainThread\(\(\) =>\s*\{\s*if \(!ShouldDispatchReceivedMessage\(connectedEpoch\)\)\s*return;\s*BattleSyncDiagnosticsLogger.MarkMessageDequeued\(.*?OnMessageReceived\?\.Invoke\(message\);')
if (-not $messageCallback.Success) {
    throw 'Received messages must check their captured epoch before dispatch.'
}

$connectedCallback = [regex]::Match($source,
    '(?s)EnqueueMainThread\(\(\) =>\s*\{\s*if \(!ShouldDispatchReceivedMessage\(connectedEpoch\)\)\s*return;\s*StartHeartbeat\(\);')
if (-not $connectedCallback.Success) {
    throw 'A stale connected callback must not start a heartbeat.'
}
$failureCallback = [regex]::Match($source,
    '(?s)EnqueueMainThread\(\(\) =>\s*\{\s*if \(!ShouldHandleConnectionFailure\(failedEpoch\)\)\s*return;\s*HandleDisconnected\("Connection lost:')
if (-not $failureCallback.Success) {
    throw 'A stale read failure must not disconnect the current connection.'
}
$sendFailureCallback = [regex]::Match($source,
    '(?s)EnqueueMainThread\(\(\) =>\s*\{\s*if \(!ShouldHandleConnectionFailure\(sendEpoch\)\)\s*return;\s*HandleDisconnected\("Send failed:')
if (-not $sendFailureCallback.Success) {
    throw 'A stale send failure must not disconnect the current connection.'
}

$harness = @'
using System.Threading;
public sealed class NetworkEpochDispatchHarness
{
    private long connectionEpoch;
    private volatile bool shouldRun;
    private volatile bool isConnecting;
    public long ConnectionEpoch => Interlocked.Read(ref connectionEpoch);
    public void SetState(long epoch, bool running, bool connecting)
    {
        Interlocked.Exchange(ref connectionEpoch, epoch);
        shouldRun = running;
        isConnecting = connecting;
    }
__METHOD__
__FAILURE_METHOD__
    public bool CanDispatch(long epoch) => ShouldDispatchReceivedMessage(epoch);
    public bool CanHandleFailure(long epoch) => ShouldHandleConnectionFailure(epoch);
}
'@.Replace('__METHOD__', $method.Value).
    Replace('__FAILURE_METHOD__', $failureMethod.Value)
Add-Type -TypeDefinition $harness

$network = [NetworkEpochDispatchHarness]::new()
$network.SetState(1, $true, $false)
if (-not $network.CanDispatch(1) -or $network.CanDispatch(0)) {
    throw 'The active epoch alone must dispatch.'
}
if (-not $network.CanHandleFailure(1) -or $network.CanHandleFailure(2)) {
    throw 'Only the current connection failure may be handled.'
}

$network.SetState(1, $true, $true)
if ($network.CanDispatch(1) -or $network.CanHandleFailure(1)) {
    throw 'Queued frames must wait until the connection attempt finishes.'
}

$network.SetState(2, $true, $false)
if ($network.CanDispatch(1) -or -not $network.CanDispatch(2)) {
    throw 'Frames from the old epoch must not reach the new connection.'
}
if ($network.CanHandleFailure(1) -or -not $network.CanHandleFailure(2)) {
    throw 'An old failure callback must not close the new connection.'
}

$network.SetState(2, $false, $false)
if ($network.CanDispatch(2) -or $network.CanHandleFailure(2)) {
    throw 'Frames queued before a manual disconnect must be dropped.'
}

$network.SetState(0, $true, $false)
if (-not $network.CanHandleFailure(0)) {
    throw 'Initial connection failures must still trigger recovery.'
}

Write-Output 'PASS: stale messages and failures are rejected; initial failures recover.'
