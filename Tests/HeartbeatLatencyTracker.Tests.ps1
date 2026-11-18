$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$tracker = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/NetWork/HeartbeatLatencyTracker.cs') -Raw
$harness = @'
public static class HeartbeatLatencyTrackerTests
{
    private static void Check(bool result, string message)
    {
        if (!result) throw new Exception(message);
    }

    public static void Run()
    {
        long frequency = Stopwatch.Frequency;
        long start = frequency * 100;
        var tracker = new HeartbeatLatencyTracker(2);
        double ms;

        tracker.Reset(1);
        Check(!tracker.TryGet(1, start, out ms), "No sample should be available initially.");
        tracker.Begin(1, "one", start);
        Check(!tracker.Complete(1, "other", start + frequency / 10), "Unmatched reply accepted.");
        Check(tracker.Complete(1, "one", start + frequency / 10), "Matched reply rejected.");
        Check(tracker.TryGet(1, start + frequency / 10, out ms), "Sample unavailable.");
        Check(Math.Abs(ms - 100) < 1, "Round trip duration is incorrect.");
        Check(!tracker.Complete(1, "one", start + frequency / 5), "Duplicate reply accepted.");

        tracker.Begin(1, "old", start + frequency);
        tracker.Begin(1, "new", start + frequency * 2);
        Check(tracker.Complete(1, "new", start + frequency * 2 + frequency / 5),
            "Newer probe was rejected.");
        Check(!tracker.Complete(1, "old", start + frequency * 2 + frequency / 3),
            "Out of order older probe replaced the newest sample.");
        Check(tracker.TryGet(1, start + frequency * 3, out ms) && Math.Abs(ms - 200) < 1,
            "Latest measurement changed after an older reply.");
        Check(!tracker.TryGet(1, start + frequency * 9, out ms), "Stale sample stayed available.");

        tracker.Begin(1, "expired", start + frequency * 10);
        Check(!tracker.Complete(1, "expired", start + frequency * 17),
            "Reply after the timeout was accepted.");
        tracker.Begin(1, "failed-send", start + frequency * 18);
        tracker.Forget(1, "failed-send");
        Check(!tracker.Complete(1, "failed-send", start + frequency * 19),
            "A failed send produced a sample.");

        tracker.Begin(1, "previous-connection", start + frequency * 20);
        tracker.Reset(0);
        Check(!tracker.TryGet(1, start + frequency * 20, out ms),
            "Disconnected connection retained a sample.");
        tracker.Reset(2);
        Check(!tracker.Complete(1, "previous-connection", start + frequency * 21),
            "Reply from an older connection was accepted.");
        tracker.Begin(1, "old-epoch", start + frequency * 21);
        Check(!tracker.Complete(2, "old-epoch", start + frequency * 22),
            "Probe from an older connection was accepted.");
        tracker.Begin(2, "current", start + frequency * 23);
        Check(tracker.Complete(2, "current", start + frequency * 23 + frequency / 4),
            "New connection did not produce a sample.");
        Check(!tracker.TryGet(1, start + frequency * 24, out ms),
            "Old connection could access the new sample.");
    }
}
'@
Add-Type -TypeDefinition ($tracker + "`n" + $harness) -IgnoreWarnings
[HeartbeatLatencyTrackerTests]::Run()

$manager = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/NetWork/NetworkManager.cs') -Raw
$protocol = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/NetWork/NetMessages.cs') -Raw
if ($manager -notmatch 'message\.clientPingId = Guid\.NewGuid\(\)' -or
    $manager -notmatch 'heartbeatLatency\?\.Begin\(epoch, message\.clientPingId, Stopwatch\.GetTimestamp\(\)\)' -or
    $manager -notmatch 'reply\.clientPingId, receivedAtTicks' -or
    $protocol -notmatch '(?s)class HeartbeatAckMessage : NetMessageBase\s*\{[^}]*clientPingId;') {
    throw 'Heartbeat wire ID or receive-time measurement is missing.'
}
Write-Output 'Heartbeat latency tracker tests passed.'
