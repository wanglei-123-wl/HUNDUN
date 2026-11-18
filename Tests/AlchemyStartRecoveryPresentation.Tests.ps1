$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$panel = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Alchemy/Presentation/AlchemyPanelView.cs') -Raw

function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
}
function Method([string]$name) {
    $pattern = '(?ms)^        private void ' + [regex]::Escape($name) +
        '\([^)]*\)\r?\n        \{.*?^        \}'
    $match = [regex]::Match($panel, $pattern)
    Assert-True $match.Success "Missing panel method $name."
    return $match.Value
}

$disable = Method 'OnDisable'
$destroy = Method 'OnDestroy'
$world = Method 'HandleWorldEntered'
$snapshot = Method 'HandleSnapshotChanged'
$result = Method 'HandleStartResult'
$resolved = Method 'HandleStartSelectionResolved'
$character = Method 'EnsureSelectionCharacter'
$render = Method 'Render'
$bind = Method 'BindRuntimes'
Assert-True ($disable -notmatch 'selection\.Clear\(|UnbindRuntimes\(' -and
    $disable -notmatch 'LocalWorldEntered -=' -and
    $disable -notmatch 'selectedRecipeId = null' -and
    $destroy -match 'LocalWorldEntered -=' -and
    $destroy -match 'UnbindRuntimes\(' -and
    $world -match 'EnsureSelectionCharacter\(characterDbId\)' -and
    $world -match 'BindRuntimes\(\)' -and
    $character -match 'selection = new AlchemyMaterialSelection\(\)' -and
    $bind -notmatch 'selection\.Clear\(' -and
    $render -notmatch 'selection\.Clear\(' -and
    $render -notmatch 'selectedRecipeId = null') `
    'Closing or temporarily losing sync keeps the selection; switching character isolates it.'
Assert-True ($snapshot -notmatch 'ResolveOperation\(' -and
    $result -notmatch 'selection\.ResolveOperation\(' -and
    $bind -match 'AdoptStartSelection\(\)' -and
    $resolved -match 'RefreshSlots\(\)' -and
    $resolved -match 'RefreshStartButton\(\)' -and
    $panel -match 'runtime\.StartSelectionResolved \+= HandleStartSelectionResolved' -and
    $panel -match 'runtime\.StartSelectionResolved -= HandleStartSelectionResolved' -and
    $panel -match 'selection\.SyncRestoredStackItems\(inventory\?\.CurrentSnapshot\)') `
    'Runtime owns submitted choices; recreated panels adopt them and update on replay completion.'
Write-Output 'PASS: alchemy panel start result and reconnect selection boundaries.'
