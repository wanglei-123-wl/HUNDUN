$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = (Get-Content (Join-Path $root 'Assets/Scripts/Alchemy/Protocol/AlchemyTaskClock.cs') -Raw) `
    -replace '(?m)^using System;\r?\n', ''
$stub = @'
namespace Game.Alchemy.Protocol
{
    public sealed class AlchemyTaskDto
    {
        public long startedAtUnixMilliseconds;
        public long endsAtUnixMilliseconds;
    }
}
'@
Add-Type -TypeDefinition ("using System;`n" + $stub + "`n" + $source) `
    -IgnoreWarnings | Out-Null

function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
}

$task = [Game.Alchemy.Protocol.AlchemyTaskDto]::new()
$task.startedAtUnixMilliseconds = 1800000000000L
$task.endsAtUnixMilliseconds = 1800000060000L
$clock = $null
Assert-True ([Game.Alchemy.Protocol.AlchemyTaskClock]::TryCreate(
    $task, 1800000005000L, 100.0, [ref]$clock)) `
    'A task must sync against the response serverTime in milliseconds.'
Assert-True ($clock.RemainingSeconds(100.0) -eq 55 -and
    [Math]::Abs($clock.Progress(100.0) - 5.0 / 60.0) -lt 0.00001) `
    'The server timestamp must determine both remaining time and fill.'
Assert-True ($clock.RemainingSeconds(110.5) -eq 45 -and
    [Math]::Abs($clock.Progress(110.5) - 15.5 / 60.0) -lt 0.00001) `
    'The clock must advance from monotonic elapsed time without polling.'
Assert-True ($clock.RemainingSeconds(200.0) -eq 0 -and
    $clock.Progress(200.0) -eq 1.0) `
    'Finishing locally only clamps the display; it does not grant output.'
Assert-True ($clock.RemainingSeconds(90.0) -eq 55) `
    'A backwards monotonic input must not move the clock backwards.'

$future = $null
Assert-True ([Game.Alchemy.Protocol.AlchemyTaskClock]::TryCreate(
    $task, 1799999995000L, 100.0, [ref]$future) -and
    $future.RemainingSeconds(100.0) -eq 65 -and
    $future.Progress(100.0) -eq 0.0) `
    'A server time before the task starts must not show negative progress.'
Assert-True (-not [Game.Alchemy.Protocol.AlchemyTaskClock]::TryCreate(
    $task, 0L, 100.0, [ref]$clock)) `
    'A missing server timestamp cannot start a local clock.'
$task.endsAtUnixMilliseconds = $task.startedAtUnixMilliseconds
Assert-True (-not [Game.Alchemy.Protocol.AlchemyTaskClock]::TryCreate(
    $task, 1800000005000L, 100.0, [ref]$clock)) `
    'A zero-duration task must not divide by zero.'

$panel = Get-Content (Join-Path $root 'Assets/Scripts/Alchemy/Presentation/AlchemyPanelView.cs') -Raw
$tickBody = [regex]::Match($panel,
    '(?s)private void TickTask\(\).*?(?=private static string TaskName)').Value
$runtime = Get-Content (Join-Path $root 'Assets/Scripts/Alchemy/Runtime/AlchemySessionRuntime.cs') -Raw
$scene = Get-Content (Join-Path $root 'Assets/Scenes/WorldShell.unity') -Raw
$panelBlock = [regex]::Match($scene,
    '(?s)--- !u!114 &860000000000100003\s+MonoBehaviour:.*?(?=--- !u!)').Value
$barBlock = [regex]::Match($scene,
    '(?s)--- !u!1 &1391164144545938121\s+GameObject:.*?(?=--- !u!)').Value
$fillBlock = [regex]::Match($scene,
    '(?s)--- !u!114 &2018479976647568642\s+MonoBehaviour:.*?(?=--- !u!)').Value
$outputBlock = [regex]::Match($scene,
    '(?s)--- !u!1 &1531636761\s+GameObject:.*?(?=--- !u!)').Value
$rootBlock = [regex]::Match($scene,
    '(?s)--- !u!224 &1784731223\s+RectTransform:.*?(?=--- !u!)').Value
Assert-True ($panelBlock.Contains('taskFill: {fileID: 2018479976647568642}') -and
    $panelBlock.Contains('taskStatus: {fileID: 1949619011}') -and
    $panelBlock.Contains('resultIcon: {fileID: 1531636763}') -and
    $panelBlock.Contains('startButton: {fileID: 1417273727}')) `
    'The alchemy panel must reference the existing bar, label, output icon and start button.'
Assert-True ($barBlock.Contains('component: {fileID: 2018479976647568642}') -and
    -not $scene.Contains('--- !u!114 &1348802012') -and
    $fillBlock.Contains('m_GameObject: {fileID: 1391164144545938121}') -and
    $fillBlock.Contains('m_Type: 3') -and
    $panel -match '(?s)private void ClearTaskDisplay\(\).*?taskFill\.fillAmount = 0f;' -and
    $outputBlock.Contains('m_IsActive: 0') -and
    $rootBlock.Contains('{fileID: 1949619010}') -and
    $panel.Contains('!runtime.IsSessionActive') -and
    $runtime.Contains('public bool IsSessionActive => CanUseSession();') -and
    $panel.Contains('task.ParsedSucceeded == true') -and
    $tickBody.Contains('completionQuerySent = runtime.RequestSnapshot()') -and
    -not $tickBody.Contains('TryClaim(')) `
    'Only the alchemy bar may lose its health driver; finishing must query without claiming.'
Write-Output 'PASS: alchemy task clock and scene display bindings.'
