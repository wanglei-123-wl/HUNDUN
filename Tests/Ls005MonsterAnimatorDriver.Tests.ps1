$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$controller = Get-Content -LiteralPath (
    Join-Path $root 'Assets/怪物状态机/Ls004.controller') -Raw

foreach ($parameter in @(
    @{ Name = 'IsMoving'; Type = 4 },
    @{ Name = 'Attack'; Type = 9 },
    @{ Name = 'Dead'; Type = 4 }
)) {
    $pattern = "(?m)^  - m_Name: $($parameter.Name)`r?`n    m_Type: $($parameter.Type)$"
    if ($controller -notmatch $pattern) {
        throw "Ls004 controller is missing $($parameter.Name)."
    }
}

$states = @{}
foreach ($match in [regex]::Matches(
    $controller, '(?ms)^--- !u!1102 &(-?\d+)\r?\nAnimatorState:\r?\n(.*?)(?=^--- !u!|\z)')) {
    $states[$match.Groups[1].Value] = $match.Groups[2].Value
}
$idle = '-3422833013298414224'
$walk = '5227052162294690709'
$attack = '2965600342717920257'
$death = '1736087640802975741'

foreach ($state in @(
    @{ Id = $idle; Name = '\u5F85\u673A'; Clip = '9c1839e7dbc24d0881f0b8d884049da5' },
    @{ Id = $walk; Name = '\u884C\u8D70'; Clip = '312fba8992294220a1e81badf20f6f69' },
    @{ Id = $attack; Name = '\u653B\u51FB'; Clip = 'd4c1ffb7fc4649e08398fd8464e589ca' },
    @{ Id = $death; Name = '\u6B7B\u4EA1'; Clip = '9098b1ab46dd44e88417cf725a9cdcd1' }
)) {
    if (-not $states.ContainsKey($state.Id) -or
        $states[$state.Id] -notmatch [regex]::Escape("m_Name: `"$($state.Name)`"") -or
        $states[$state.Id] -notmatch "m_Motion: \{fileID: 7400000, guid: $($state.Clip), type: 3\}") {
        throw "Ls004 state $($state.Name) is missing or references the wrong animation."
    }
}

$transitions = @{}
foreach ($match in [regex]::Matches(
    $controller, '(?ms)^--- !u!1101 &(\d+)\r?\nAnimatorStateTransition:\r?\n(.*?)(?=^--- !u!|\z)')) {
    $transitions[$match.Groups[1].Value] = $match.Groups[2].Value
}

$edges = @(
    @{ Id = '705005000000000001'; From = $idle; To = $attack; Event = 'Attack'; Mode = 1; Exit = 0 },
    @{ Id = '705005000000000002'; From = $idle; To = $walk; Event = 'IsMoving'; Mode = 1; Exit = 0 },
    @{ Id = '705005000000000003'; From = $walk; To = $idle; Event = 'IsMoving'; Mode = 2; Exit = 0 },
    @{ Id = '705005000000000004'; From = $walk; To = $attack; Event = 'Attack'; Mode = 1; Exit = 0 },
    @{ Id = '705005000000000005'; From = $attack; To = $idle; Event = 'IsMoving'; Mode = 2; Exit = 1 },
    @{ Id = '705005000000000006'; From = $attack; To = $walk; Event = 'IsMoving'; Mode = 1; Exit = 1 },
    @{ Id = '705005000000000007'; From = 'Any'; To = $death; Event = 'Dead'; Mode = 1; Exit = 0 }
)
foreach ($edge in $edges) {
    $source = if ($edge.From -eq 'Any') { $controller } else { $states[$edge.From] }
    if ($source -notmatch [regex]::Escape("- {fileID: $($edge.Id)}") -or
        -not $transitions.ContainsKey($edge.Id) -or
        $transitions[$edge.Id] -notmatch "m_ConditionMode: $($edge.Mode)\s+m_ConditionEvent: $($edge.Event)\b" -or
        $transitions[$edge.Id] -notmatch "m_DstState: \{fileID: $($edge.To)\}" -or
        $transitions[$edge.Id] -notmatch "m_HasExitTime: $($edge.Exit)") {
        throw "Ls004 transition $($edge.Id) is incomplete."
    }
}
if ($states[$death] -notmatch 'm_Transitions: \[\]' -or
    $controller -notmatch "m_DefaultState: \{fileID: $idle\}") {
    throw 'Ls004 death must be terminal and idle must be the default state.'
}

$existingDriverTest = Get-Content -LiteralPath (
    Join-Path $PSScriptRoot 'Ls003MonsterAnimatorDriver.Tests.ps1') -Raw
$script:Ls005TestRoot = $root
$existingDriverTest = $existingDriverTest.Replace('Ls003', 'Ls005').
    Replace('Assets/怪物状态机/Ls005.controller', 'Assets/怪物状态机/Ls004.controller').
    Replace('$root = Split-Path $PSScriptRoot -Parent', '$root = $script:Ls005TestRoot')
. ([scriptblock]::Create($existingDriverTest))
Write-Output 'PASS: Ls004 controller states, clips, transitions, and terminal death.'
