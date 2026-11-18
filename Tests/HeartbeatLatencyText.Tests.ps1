$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$presenter = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/NetWork/HeartbeatLatencyTextPresenter.cs') -Raw
$scene = Get-Content -LiteralPath (Join-Path $root 'Assets/Scenes/WorldShell.unity') -Raw

$stubs = @'
namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class DisallowMultipleComponent : Attribute {}
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SerializeField : Attribute {}
    public class MonoBehaviour
    {
        protected T GetComponent<T>() where T : class { return null; }
    }
    public static class Time { public static float unscaledTime; }
}
namespace TMPro { public class TMP_Text { public string text; } }
public sealed class NetworkManager
{
    public static NetworkManager Instance;
    public bool hasSample;
    public double sample;
    public bool TryGetHeartbeatRoundTripMs(out double ms)
    {
        ms = sample;
        return hasSample;
    }
}
public static class HeartbeatLatencyTextTests
{
    private static void Check(bool valid, string error)
    {
        if (!valid) throw new Exception(error);
    }
    private static void Call(HeartbeatLatencyTextPresenter presenter, string method)
    {
        typeof(HeartbeatLatencyTextPresenter).GetMethod(method,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .Invoke(presenter, null);
    }
    public static void Run()
    {
        var presenter = new HeartbeatLatencyTextPresenter();
        var text = new TMP_Text();
        typeof(HeartbeatLatencyTextPresenter).GetField("latencyText",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .SetValue(presenter, text);
        Time.unscaledTime = 0;
        Call(presenter, "OnEnable");
        Check(text.text == "\u5EF6\u8FDF: -- ms", "Unknown latency should not show a number.");
        NetworkManager.Instance = new NetworkManager { hasSample = true, sample = 43.7 };
        Time.unscaledTime = 0.1f;
        Call(presenter, "Update");
        Check(text.text == "\u5EF6\u8FDF: -- ms", "Refresh interval was ignored.");
        Time.unscaledTime = 0.3f;
        Call(presenter, "Update");
        Check(text.text == "\u5EF6\u8FDF: 44 ms", "Measured round trip was not shown.");
        NetworkManager.Instance.hasSample = false;
        Time.unscaledTime = 0.6f;
        Call(presenter, "Update");
        Check(text.text == "\u5EF6\u8FDF: -- ms", "Stale or disconnected reading stayed visible.");
        NetworkManager.Instance = null;
        Time.unscaledTime = 0.9f;
        Call(presenter, "OnEnable");
        Check(text.text == "\u5EF6\u8FDF: -- ms", "Missing network showed a number.");
    }
}
'@
Add-Type -TypeDefinition ($presenter + "`n" + $stubs) -IgnoreWarnings
[HeartbeatLatencyTextTests]::Run()

if ($scene -notmatch '(?s)--- !u!1 &49236846\r?\n.*?component: \{fileID: 49236850\}' -or
    $scene -notmatch '(?s)--- !u!114 &49236850\r?\n.*?guid: c858aa3f5a6e44d6b3b282925318c808.*?latencyText: \{fileID: 49236848\}' -or
    $scene -notmatch '(?s)--- !u!224 &1519366376\r?\n.*?m_LocalScale: \{x: 1, y: 1, z: 1\}' -or
    $scene -notmatch '(?s)--- !u!114 &49236848\r?\n.*?m_RaycastTarget: 0') {
    throw 'The existing latency text, canvas visibility, or scene binding is incorrect.'
}
Write-Output 'Heartbeat latency text and scene binding tests passed.'
