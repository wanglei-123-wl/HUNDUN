$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Get-Content (Join-Path $root 'Assets/Scripts/CharacterPanel/Presentation/CharacterPanelToggle.cs') -Raw
$scene = Get-Content (Join-Path $root 'Assets/Scenes/WorldShell.unity') -Raw

function Assert-True([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}

$toggle = [regex]::Match($scene, '(?ms)^--- !u!114 &2008553970\r?\n(.*?)(?=^--- !u!|\z)')
$button = [regex]::Match($scene, '(?ms)^--- !u!114 &900979444\r?\n(.*?)(?=^--- !u!|\z)')
$canvas = [regex]::Match($scene, '(?ms)^--- !u!224 &118479040\r?\n(.*?)(?=^--- !u!|\z)')
$panel = [regex]::Match($scene, '(?ms)^--- !u!1 &2008553968\r?\n(.*?)(?=^--- !u!|\z)')
Assert-True $toggle.Success 'Character panel toggle is missing from WorldShell.'
Assert-True $button.Success 'Character panel close button is missing from WorldShell.'
Assert-True ($canvas.Value -match 'm_LocalScale: \{x: ([01]), y: \1, z: \1\}' -and
    $panel.Value -match 'm_IsActive: 0') `
    'Screen-space canvas scale may be editor-driven; the panel must start hidden and Play Mode verifies its visible raycast geometry.'
Assert-True ($toggle.Value -match 'panelCanvas: \{fileID: 2008553968\}' -and
    $toggle.Value -match 'closeButton: \{fileID: 900979444\}') `
    'Character panel toggle must reference its panel and close button.'
Assert-True ($button.Value -match 'm_GameObject: \{fileID: 900979442\}') `
    'Close button reference must resolve to the character panel button.'
Assert-True ($source -match 'closeButton\.onClick\.AddListener\(Close\)' -and
    $source -match 'closeButton\.onClick\.RemoveListener\(Close\)' -and
    $source -match '(?s)private void Close\(\)\s*\{\s*SetOpen\(false\);') `
    'Close listener must be registered, unregistered, and close the panel.'
Assert-True ($source -match 'Input\.GetKeyDown\(KeyCode\.Tab\)' -and
    $source -match 'SetOpen\(!IsOpen\)') `
    'Existing Tab toggle behavior must remain intact.'

Write-Output 'PASS: character panel close button wiring and Tab toggle.'
