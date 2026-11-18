$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$controller = Get-Content -LiteralPath (
    Join-Path $root 'Assets/怪物状态机/BOOS状态机/BS001.controller') -Raw

foreach ($parameter in @(
    @{ Name = 'IsMoving'; Type = 4 },
    @{ Name = 'Attack'; Type = 9 },
    @{ Name = 'Dead'; Type = 4 }
)) {
    $pattern = "(?m)^  - m_Name: $($parameter.Name)`r?`n    m_Type: $($parameter.Type)$"
    if ($controller -notmatch $pattern) {
        throw "BS001 controller is missing $($parameter.Name)."
    }
}

$states = @{}
$transitions = @{}
$machine = $null
foreach ($document in [regex]::Matches(
    $controller, '(?ms)^--- !u!(\d+) &(-?\d+)\r?\n(.*?)(?=^--- !u!|\z)')) {
    $type = $document.Groups[1].Value
    $id = $document.Groups[2].Value
    $body = $document.Groups[3].Value
    switch ($type) {
        '1102' { $states[$id] = $body }
        '1101' {
            if ($transitions.ContainsKey($id)) { throw "Duplicate BS001 transition $id." }
            $transitions[$id] = $body
        }
        '1107' { $machine = $body }
    }
}

$idle = '-8781032620717825405'
$walk = '5259520204746425663'
$attack = '-7883097775163634453'
$death = '5328312146323853316'
foreach ($state in @(
    @{ Id = $idle; Name = '\u5F85\u673A'; Clip = '2bacbfec62074148a6060ac93a8f2dc5' },
    @{ Id = $walk; Name = '\u884C\u8D70'; Clip = 'c050995830b04386b1709618aaa95c94' },
    @{ Id = $attack; Name = '\u653B\u51FB'; Clip = 'e8945b6feaf8433d86df634556682a62' },
    @{ Id = $death; Name = '\u6B7B\u4EA1'; Clip = 'a0a3e5db1b194a2eba9676c86561a8b3' }
)) {
    if (-not $states.ContainsKey($state.Id) -or
        $states[$state.Id] -notmatch [regex]::Escape("m_Name: `"$($state.Name)`"") -or
        $states[$state.Id] -notmatch "m_Motion: \{fileID: 7400000, guid: $($state.Clip), type: 3\}") {
        throw "BS001 state $($state.Name) is missing or references the wrong animation."
    }
}

$edges = @(
    @{ Id = '706001000000000001'; From = $idle; To = $attack; Event = 'Attack'; Mode = 1; Exit = 0 },
    @{ Id = '706001000000000002'; From = $idle; To = $walk; Event = 'IsMoving'; Mode = 1; Exit = 0 },
    @{ Id = '706001000000000003'; From = $walk; To = $idle; Event = 'IsMoving'; Mode = 2; Exit = 0 },
    @{ Id = '706001000000000004'; From = $walk; To = $attack; Event = 'Attack'; Mode = 1; Exit = 0 },
    @{ Id = '706001000000000005'; From = $attack; To = $idle; Event = 'IsMoving'; Mode = 2; Exit = 1 },
    @{ Id = '706001000000000006'; From = $attack; To = $walk; Event = 'IsMoving'; Mode = 1; Exit = 1 },
    @{ Id = '706001000000000007'; From = 'Any'; To = $death; Event = 'Dead'; Mode = 1; Exit = 0 }
)
if ($transitions.Count -ne $edges.Count) { throw 'BS001 has unexpected transitions.' }
foreach ($edge in $edges) {
    $source = if ($edge.From -eq 'Any') { $machine } else { $states[$edge.From] }
    if ($source -notmatch [regex]::Escape("- {fileID: $($edge.Id)}") -or
        -not $transitions.ContainsKey($edge.Id) -or
        $transitions[$edge.Id] -notmatch "m_ConditionMode: $($edge.Mode)\s+m_ConditionEvent: $($edge.Event)\b" -or
        $transitions[$edge.Id] -notmatch "m_DstState: \{fileID: $($edge.To)\}" -or
        $transitions[$edge.Id] -notmatch "m_HasExitTime: $($edge.Exit)") {
        throw "BS001 transition $($edge.Id) is incomplete."
    }
}
if ($states[$death] -notmatch 'm_Transitions: \[\]' -or
    $machine -notmatch "m_DefaultState: \{fileID: $idle\}" -or
    $machine -notmatch 'm_AnyStateTransitions:\s+- \{fileID: 706001000000000007\}') {
    throw 'BS001 death must be terminal and idle must be the default state.'
}

$existingDriverTest = Get-Content -LiteralPath (
    Join-Path $PSScriptRoot 'Ls003MonsterAnimatorDriver.Tests.ps1') -Raw
$script:BS001TestRoot = $root
$existingDriverTest = $existingDriverTest.Replace('Ls003', 'BS001').
    Replace('Assets/怪物状态机/BS001.controller', 'Assets/怪物状态机/BOOS状态机/BS001.controller').
    Replace('$root = Split-Path $PSScriptRoot -Parent', '$root = $script:BS001TestRoot')
. ([scriptblock]::Create($existingDriverTest))
Write-Output 'PASS: BS001 clips, transitions, terminal death, and animator driver.'
