$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$prefab = Get-Content -LiteralPath (Join-Path $root 'Assets/预制体/NPC预制体/传送员.prefab') -Raw
$hitbox = [regex]::Match($prefab, '(?ms)^--- !u!65 &\d+\r?\nBoxCollider:\r?\n.*?(?=^--- !u!|\z)').Value

if ($hitbox -notmatch '(?m)^  m_IsTrigger: 1\r?$' -or
    $hitbox -notmatch '(?m)^  m_Enabled: 1\r?$' -or
    $hitbox -notmatch 'm_Size: \{x: ([0-9.]+), y: ([0-9.]+), z: ([0-9.]+)\}') {
    throw 'The reusable teleporter needs an enabled click-only BoxCollider.'
}
if ([double]$Matches[1] -lt 2.4 -or [double]$Matches[2] -lt 3.2 -or
    [double]$Matches[3] -lt 2.4) {
    throw 'The teleporter hitbox must provide enough room to click the model.'
}
if ([regex]::Matches($prefab, '(?m)^--- !u!65 ').Count -ne 1) {
    throw 'Each teleporter prefab needs one interaction hitbox.'
}

Write-Output 'PASS: the reusable teleporter has a generous trigger hitbox.'
