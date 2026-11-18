$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Get-Content (Join-Path $root 'Assets/Scripts/CharacterPanel/Presentation/CharacterPanelPointerCapture.cs') -Raw
Add-Type -TypeDefinition $source

function Assert-Equal([bool]$actual, [bool]$expected, [string]$message) {
    if ($actual -ne $expected) { throw $message }
}

$capture = [Game.CharacterPanel.Presentation.CharacterPanelPointerCapture]::new()
Assert-Equal ($capture.Sample($false, $true, 0, 0, 0)) $false 'Closed panel must not block input.'
Assert-Equal ($capture.Sample($true, $false, 0, 0, 0)) $false 'Outside the panel must remain playable.'
Assert-Equal ($capture.Sample($true, $true, 0, 0, 0)) $true 'Hovering over the panel must block input.'
Assert-Equal ($capture.Sample($true, $true, 1, 1, 0)) $true 'Panel press must be captured.'
Assert-Equal ($capture.Sample($true, $false, 1, 0, 0)) $true 'Dragging outside must remain captured.'
Assert-Equal ($capture.Sample($true, $false, 0, 0, 1)) $true 'Release frame must remain blocked.'
Assert-Equal ($capture.Sample($true, $false, 0, 0, 0)) $false 'Input must resume after release.'

$capture.Reset()
Assert-Equal ($capture.Sample($false, $false, 1, 1, 0)) $false 'World press before opening must remain playable.'
Assert-Equal ($capture.Sample($true, $false, 1, 0, 0)) $true 'Opening while held must cancel world drag.'
Assert-Equal ($capture.Sample($false, $false, 1, 0, 0)) $true 'Closing while held must not leak the drag.'
Assert-Equal ($capture.Sample($false, $false, 0, 0, 1)) $true 'Release after closing must remain blocked.'
Assert-Equal ($capture.Sample($false, $false, 0, 0, 0)) $false 'World input must recover after release.'

$capture.Reset()
Assert-Equal ($capture.Sample($true, $true, 3, 3, 0)) $true 'Both buttons must be captured.'
Assert-Equal ($capture.Sample($true, $false, 2, 0, 1)) $true 'Remaining button must stay captured.'
Assert-Equal ($capture.Sample($true, $false, 0, 0, 2)) $true 'Last release frame must stay blocked.'
Assert-Equal ($capture.Sample($true, $false, 0, 0, 0)) $false 'All buttons released must restore input.'

Write-Output 'PASS: character panel hover, drag capture, release and reopen boundaries.'
