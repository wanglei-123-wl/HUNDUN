$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$scene = Get-Content -LiteralPath (Join-Path $root 'Assets/Scenes/WorldShell.unity') -Raw
$meta = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Chat/UI/WorldChatPanelController.cs.meta') -Raw
$panel = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Chat/UI/WorldChatPanelController.cs') -Raw
$guard = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Player/GameplayUiInputGuard.cs') -Raw

function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
}
function Get-Record([string]$kind, [string]$id) {
    $match = [regex]::Match($scene,
        "(?ms)^--- !u!$kind &$id\r?\n.*?(?=^--- !u!|\z)")
    if (-not $match.Success) { throw "Missing scene record $kind $id" }
    return $match.Value
}

$controllerObject = Get-Record '1' '849528545'
$controller = Get-Record '114' '2180000001'
$input = Get-Record '114' '2108000004'
$button = Get-Record '114' '282297219'
$scroll = Get-Record '114' '950161648'
$content = Get-Record '224' '19551103'
$templateObject = Get-Record '1' '1951940206'
$template = Get-Record '114' '2097000013'
$templateTransform = Get-Record '224' '1951940207'

Assert-True ($meta -match 'guid: 9d91b53341144e079fc46730f12786a2' -and
    $controllerObject -match 'component: \{fileID: 2180000001\}' -and
    $controller -match 'm_GameObject: \{fileID: 849528545\}' -and
    $controller -match 'guid: 9d91b53341144e079fc46730f12786a2') `
    'The chat panel script must be attached to the existing chat controller.'
foreach ($field in @(
    'inputField: \{fileID: 2108000004\}',
    'sendButton: \{fileID: 282297219\}',
    'scrollRect: \{fileID: 950161648\}',
    'content: \{fileID: 19551103\}',
    'itemTemplate: \{fileID: 2097000013\}'
)) {
    Assert-True ($controller -match $field) "Missing or mismatched chat reference: $field"
}
Assert-True ($scroll -match 'm_Content: \{fileID: 19551103\}' -and
    $scroll -match 'm_Viewport: \{fileID: 887161465\}' -and
    $content -match 'm_Father: \{fileID: 887161465\}' -and
    $templateTransform -match 'm_Father: \{fileID: 19551103\}' -and
    $templateObject -match 'm_IsActive: 0') `
    'The scroll list must use the existing inactive chat template inside its Content.'
Assert-True ($button -match 'm_Calls: \[\]' -and
    $input -match 'm_LineType: 2' -and
    $panel -match 'sendButton\.onClick\.AddListener\(Send\)' -and
    $panel -match 'inputField\.SetTextWithoutNotify\(string\.Empty\)') `
    'Chat sends must use one runtime listener and preserve multiline input.'
Assert-True ($template -match 'nicknameText: \{fileID: 2097000003\}' -and
    $template -match 'messageText: \{fileID: 2097000011\}') `
    'The user-adjusted chat bubble must keep its nickname and message bindings.'
Assert-True ($panel -match 'IsChatInputFocused\s*=>' -and
    $panel -match 'inputField\.isFocused' -and
    $panel -match 'activePanel = this;' -and
    $panel -match 'activePanel == this\) activePanel = null;' -and
    $guard -match 'WorldChatPanelController\.IsChatInputFocused') `
    'Chat input focus must block gameplay through the shared input guard and clear on disable.'

Write-Output 'PASS: world chat panel wiring and focused-input gameplay guard.'
