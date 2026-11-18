$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Get-Content (Join-Path $root 'Assets/Scripts/NetWork/BattleSync/Runtime/BattleSyncRuntime.cs') -Raw
$method = [regex]::Match(
    $source,
    '(?s)        private void HandleNetworkDisconnected\(\s*long connectionEpoch,\s*string reason\)\s*\{.*?\n        \}(?=\s*private bool EnsureConnectionObserved)'
)
if (-not $method.Success) { throw 'BattleSync disconnect handler was not found.' }

$harness = @'
public sealed class SessionState
{
    public bool HasActiveConnection;
}
public sealed class Coordinator
{
    public readonly SessionState SessionState = new SessionState();
    public int Calls;
    public long LastEpoch;
    public object HandleNetworkDisconnected(long epoch, string reason)
    {
        Calls++;
        LastEpoch = epoch;
        return null;
    }
}
public sealed class RuntimeHarness
{
    private bool initialized = true;
    private Coordinator coordinator = new Coordinator();
    public Coordinator Current { get { return coordinator; } }
    private void LogResult(object result) {}
__HANDLER__
    public void Disconnect(long epoch)
    {
        HandleNetworkDisconnected(epoch, "test");
    }
}
'@.Replace('__HANDLER__', $method.Value)
Add-Type -TypeDefinition $harness

$runtime = [RuntimeHarness]::new()
$runtime.Disconnect(0)
if ($runtime.Current.Calls -ne 0) { throw 'Epoch zero without an active connection must be ignored.' }

$runtime.Current.SessionState.HasActiveConnection = $true
$runtime.Disconnect(0)
if ($runtime.Current.Calls -ne 1 -or $runtime.Current.LastEpoch -ne 0) {
    throw 'Epoch zero with an active connection must remain diagnosable.'
}

$runtime.Disconnect(7)
if ($runtime.Current.Calls -ne 2 -or $runtime.Current.LastEpoch -ne 7) {
    throw 'Real disconnection must reach the coordinator.'
}

$runtime.Current.SessionState.HasActiveConnection = $false
$runtime.Disconnect(7)
if ($runtime.Current.Calls -ne 3) { throw 'Repeated valid-epoch disconnection must reach the coordinator.' }

$runtime.Current.SessionState.HasActiveConnection = $true
$runtime.Disconnect(6)
if ($runtime.Current.Calls -ne 4 -or $runtime.Current.LastEpoch -ne 6) {
    throw 'Stale positive-epoch disconnection must remain diagnosable.'
}

Write-Output 'PASS: idle epoch zero ignored; active, repeated and stale disconnects forwarded.'
