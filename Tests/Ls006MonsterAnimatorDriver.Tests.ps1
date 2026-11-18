$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$controller = Get-Content -LiteralPath (
    Join-Path $root 'Assets/怪物状态机/Ls006.controller') -Raw

foreach ($parameter in @(
    @{ Name = 'IsMoving'; Type = 4 },
    @{ Name = 'Attack'; Type = 9 },
    @{ Name = 'Dead'; Type = 4 }
)) {
    $pattern = "(?m)^  - m_Name: $($parameter.Name)`r?`n    m_Type: $($parameter.Type)$"
    if ($controller -notmatch $pattern) {
        throw "Ls006 controller is missing $($parameter.Name)."
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
            if ($transitions.ContainsKey($id)) { throw "Duplicate Ls006 transition $id." }
            $transitions[$id] = $body
        }
        '1107' { $machine = $body }
    }
}

$idle = '2420981470031100831'
$walk = '4144000198749084856'
$attack = '6863971194997896658'
$death = '5832739405601069475'
foreach ($state in @(
    @{ Id = $idle; Name = '\u5F85\u673A'; Clip = 'cb447f548c22491ab657b68c29755bab' },
    @{ Id = $walk; Name = '\u884C\u8D70'; Clip = '8b318a57ab9a4ebe9194cb3d0e17ed01' },
    @{ Id = $attack; Name = '\u653B\u51FB'; Clip = '4962416bb03b4712ac483cabb076dfac' },
    @{ Id = $death; Name = '\u6B7B\u4EA1'; Clip = '48e807816ea348fbbcc4bdb9ad4fb099' }
)) {
    if (-not $states.ContainsKey($state.Id) -or
        $states[$state.Id] -notmatch [regex]::Escape("m_Name: `"$($state.Name)`"") -or
        $states[$state.Id] -notmatch "m_Motion: \{fileID: 7400000, guid: $($state.Clip), type: 3\}") {
        throw "Ls006 state $($state.Name) is missing or references the wrong animation."
    }
}

$edges = @(
    @{ Id = '706006000000000001'; From = $idle; To = $attack; Event = 'Attack'; Mode = 1; Exit = 0 },
    @{ Id = '706006000000000002'; From = $idle; To = $walk; Event = 'IsMoving'; Mode = 1; Exit = 0 },
    @{ Id = '706006000000000003'; From = $walk; To = $idle; Event = 'IsMoving'; Mode = 2; Exit = 0 },
    @{ Id = '706006000000000004'; From = $walk; To = $attack; Event = 'Attack'; Mode = 1; Exit = 0 },
    @{ Id = '706006000000000005'; From = $attack; To = $idle; Event = 'IsMoving'; Mode = 2; Exit = 1 },
    @{ Id = '706006000000000006'; From = $attack; To = $walk; Event = 'IsMoving'; Mode = 1; Exit = 1 },
    @{ Id = '706006000000000007'; From = 'Any'; To = $death; Event = 'Dead'; Mode = 1; Exit = 0 }
)
if ($transitions.Count -ne $edges.Count) { throw 'Ls006 has unexpected transitions.' }
foreach ($edge in $edges) {
    $source = if ($edge.From -eq 'Any') { $machine } else { $states[$edge.From] }
    if ($source -notmatch [regex]::Escape("- {fileID: $($edge.Id)}") -or
        -not $transitions.ContainsKey($edge.Id) -or
        $transitions[$edge.Id] -notmatch "m_ConditionMode: $($edge.Mode)\s+m_ConditionEvent: $($edge.Event)\b" -or
        $transitions[$edge.Id] -notmatch "m_DstState: \{fileID: $($edge.To)\}" -or
        $transitions[$edge.Id] -notmatch "m_HasExitTime: $($edge.Exit)" -or
        ($edge.Exit -eq 1 -and $transitions[$edge.Id] -notmatch 'm_ExitTime: 0.95')) {
        throw "Ls006 transition $($edge.Id) is incomplete."
    }
}
if ($states[$death] -notmatch 'm_Transitions: \[\]' -or
    $machine -notmatch "m_DefaultState: \{fileID: $idle\}" -or
    $machine -notmatch 'm_AnyStateTransitions:\s+- \{fileID: 706006000000000007\}') {
    throw 'Ls006 death must be terminal and idle must be the default state.'
}

$existingDriverTest = Get-Content -LiteralPath (
    Join-Path $PSScriptRoot 'Ls003MonsterAnimatorDriver.Tests.ps1') -Raw
$script:Ls006TestRoot = $root
$existingDriverTest = $existingDriverTest.Replace('Ls003', 'Ls006').
    Replace('$root = Split-Path $PSScriptRoot -Parent', '$root = $script:Ls006TestRoot')
. ([scriptblock]::Create($existingDriverTest))
Write-Output 'PASS: Ls006 clips, transitions, terminal death, and animator driver.'
