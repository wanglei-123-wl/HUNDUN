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
Assert-Equal ($capture.Sample($true, $false, 1, 0, 0)) $false 'Opening must preserve a pre-existing world drag.'
Assert-Equal ($capture.Sample($false, $false, 1, 0, 0)) $false 'Closing must preserve the same world-owned button.'
Assert-Equal ($capture.Sample($false, $false, 0, 0, 1)) $false 'Releasing a world-owned button must remain available.'
Assert-Equal ($capture.Sample($false, $false, 0, 0, 0)) $false 'World input must recover after release.'

$capture.Reset()
Assert-Equal ($capture.Sample($true, $true, 3, 3, 0)) $true 'Both buttons must be captured.'
Assert-Equal ($capture.Sample($true, $false, 2, 0, 1)) $true 'Remaining button must stay captured.'
Assert-Equal ($capture.Sample($true, $false, 0, 0, 2)) $true 'Last release frame must stay blocked.'
Assert-Equal ($capture.Sample($true, $false, 0, 0, 0)) $false 'All buttons released must restore input.'

$capture.Reset()
Assert-Equal ($capture.Sample($false, $false, 1, 1, 0, 10)) $false 'First sample assigns the press to the world.'
Assert-Equal ($capture.Sample($true, $true, 1, 1, 0, 10)) $true 'Newly opened UI blocks camera hover.'
if ($capture.CapturedButtonsThisFrame -ne 0) { throw 'UI must not steal the world press during a repeated frame sample.' }
$capture.Reset()
Assert-Equal ($capture.Sample($true, $true, 1, 1, 0, 20)) $true 'UI owns a press that starts over UI.'
Assert-Equal ($capture.Sample($false, $false, 0, 0, 1, 21)) $true 'UI release remains captured after closing.'
Assert-Equal ($capture.Sample($false, $false, 0, 0, 1, 21)) $true 'Every consumer in the release frame must see capture.'
Assert-Equal ($capture.Sample($false, $false, 0, 0, 0, 22)) $false 'The next frame releases pointer ownership.'

Write-Output 'PASS: character panel hover, drag capture, release and reopen boundaries.'
