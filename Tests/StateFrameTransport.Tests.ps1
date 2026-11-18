$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/NetWork/NetworkManager.cs') -Raw
$reader = [regex]::Match($source,
    '(?s)    private static string ReadNewlineDelimitedMessage\(NetworkStream networkStream\).*?(?=    private bool ShouldDispatchReceivedMessage)')
if (-not $reader.Success) { throw 'NetworkManager LF reader was not found.' }

$harness = @'
using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
public static class StateFrameTransportHarness
{
    private const int MaxFrameBytes = 1048576;
__READER__
    public static string Read(NetworkStream stream) => ReadNewlineDelimitedMessage(stream);
    public static Task SendRest(NetworkStream stream, byte[] remaining) =>
        Task.Run(() =>
        {
            Thread.Sleep(20);
            stream.Write(remaining, 0, remaining.Length);
            stream.Flush();
        });
}
'@.Replace('__READER__', $reader.Value)
Add-Type -TypeDefinition $harness -ErrorAction Stop

$frame = '{"msgId":"state_frame","type":"state_frame","sceneId":"world_001","serverTick":101,"serverTime":1784713000100,"frameSequence":1,"players":[[101,19820,3000,9510,16565,10,0,0,1,0,15,729,5]],"monsters":[]}'
$enter = '{"msgId":"player_enter","player":{"netId":101}}'
$self = '{"msgId":"player_authoritative_state","netId":101}'
$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
$listener.Start()
$client = $null
$server = $null
$writer = $null
try {
    $pending = $listener.AcceptTcpClientAsync()
    $client = [System.Net.Sockets.TcpClient]::new()
    $client.Connect([System.Net.IPAddress]::Loopback,
        ([System.Net.IPEndPoint]$listener.LocalEndpoint).Port)
    $server = $pending.GetAwaiter().GetResult()
    $client.ReceiveTimeout = 3000
    $readerStream = $client.GetStream()
    $writer = $server.GetStream()

    $splitAt = $frame.IndexOf('19820')
    $first = [System.Text.Encoding]::UTF8.GetBytes($frame.Substring(0, $splitAt))
    $remaining = [System.Text.Encoding]::UTF8.GetBytes(
        $frame.Substring($splitAt) + "`n" + $enter + "`n" + $self + "`n")
    $writer.Write($first, 0, $first.Length)
    $send = [StateFrameTransportHarness]::SendRest($writer, $remaining)

    $actualFrame = [StateFrameTransportHarness]::Read($readerStream)
    $actualEnter = [StateFrameTransportHarness]::Read($readerStream)
    $actualSelf = [StateFrameTransportHarness]::Read($readerStream)
    $send.GetAwaiter().GetResult()
    if ($actualFrame -cne $frame -or $actualEnter -cne $enter -or
        $actualSelf -cne $self) {
        throw 'LF framing lost a split state frame or combined an adjacent message.'
    }
    Write-Output 'PASS: segmented state frame and coalesced enter/authoritative messages retain LF boundaries.'
}
finally {
    if ($writer) { $writer.Dispose() }
    if ($server) { $server.Dispose() }
    if ($client) { $client.Dispose() }
    $listener.Stop()
}
