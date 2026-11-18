$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$scene = Get-Content -LiteralPath (Join-Path $root 'Assets/Scenes/WorldShell.unity') -Raw
$toggle = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Cultivation/Presentation/CultivationPanelToggle.cs') -Raw
$guard = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Player/GameplayUiInputGuard.cs') -Raw
$camera = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Player/ThirdPersonCameraFollow.cs') -Raw
$movement = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Player/AuthorityMovement/LocalPlayerInputController.cs') -Raw
$captureSource = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/CharacterPanel/Presentation/CharacterPanelPointerCapture.cs') -Raw

function Get-SceneBlock([string]$kind, [string]$id) {
    $pattern = "(?s)--- !u!$kind &$id`r?`n(?<body>.*?)(?=`r?`n--- !u!)"
    $matches = [regex]::Matches($scene, $pattern)
    if ($matches.Count -ne 1) { throw "Scene block $kind/$id missing or duplicated." }
    return $matches[0].Groups['body'].Value
}
function Assert-Match([string]$source, [string]$pattern, [string]$message) {
    if ($source -notmatch $pattern) { throw $message }
}

$rootObject = Get-SceneBlock '1' '112115721'
$rootRaycaster = Get-SceneBlock '114' '112115722'
$rootCanvas = Get-SceneBlock '223' '112115724'
$content = Get-SceneBlock '1' '904205820'
$contentRect = Get-SceneBlock '224' '904205821'
$contentImage = Get-SceneBlock '114' '904205822'
$nav = Get-SceneBlock '1' '1548782250'
$navRect = Get-SceneBlock '224' '1548782251'

Assert-Match $rootCanvas '(?s)m_GameObject: \{fileID: 112115721\}.*?m_Enabled: 1.*?m_RenderMode: 0' (
    'The cultivation window must have an enabled screen-space canvas.')
Assert-Match $rootObject 'component: \{fileID: 112115722\}' 'The canvas needs its GraphicRaycaster.'
if ($rootObject -match 'component: \{fileID: 904205822\}' -or
    $nav -match 'component: \{fileID: 904205822\}') {
    throw 'The full-screen host or navigation must not capture panel-area clicks.'
}
Assert-Match $rootRaycaster 'm_Enabled: 1' 'The canvas raycaster must be enabled.'
Assert-Match $content 'm_IsActive: 0' 'Cultivation content must start manually hidden.'
Assert-Match $contentRect 'm_Father: \{fileID: 112115725\}' (
    'The hit area must be a child of the always-active canvas.')
Assert-Match $contentImage 'm_RaycastTarget: 1' (
    'The visible cultivation background must block clicks behind it.')
Assert-Match $nav 'm_IsActive: 1' 'The navigation must remain enabled within the window.'
Assert-Match $navRect 'm_Father: \{fileID: 112115725\}' (
    'The navigation must not be hidden with the content.')
foreach ($id in @('1910929966', '1802858646', '209222905')) {
    Assert-Match (Get-SceneBlock '114' $id) 'm_RaycastTarget: 1' (
        "Navigation button $id must receive clicks.")
}
foreach ($id in @('1832157818', '794189227', '779501689')) {
    Assert-Match (Get-SceneBlock '114' $id) 'm_RaycastTarget: 0' (
        "Selection effect $id must not change the clickable region.")
}
if ($toggle -match 'RegisterModal|UnregisterModal' -or
    $rootObject -match 'CanvasGroup') {
    throw 'Cultivation must not use a full-screen modal capture.'
}
Assert-Match $guard 'pointerEventSystem\.RaycastAll\(pointerEventData, pointerHits\)' (
    'The existing input guard must detect UI under the pointer.')
Assert-Match $guard 'hit\.module is GraphicRaycaster' (
    'Only actual UI graphics must block gameplay.')
Assert-Match $guard 'uiCapture\.Sample\(overUi, overUi, held, pressed, released\)' (
    'UI drags must stay captured after leaving the panel.')
Assert-Match $camera 'uiBlocked = GameplayUiInputGuard\.BlocksGameplayInput' (
    'Camera rotation must respect UI blocking.')
Assert-Match $movement 'uiBlocked = GameplayUiInputGuard\.BlocksGameplayInput' (
    'Player movement and attacks must respect UI blocking.')

Add-Type -TypeDefinition $captureSource -IgnoreWarnings | Out-Null
$capture = [Game.CharacterPanel.Presentation.CharacterPanelPointerCapture]::new()
if ($capture.Sample($true, $false, 0, 0, 0)) {
    throw 'An open panel must not block outside UI.'
}
if (-not $capture.Sample($true, $true, 1, 1, 0)) {
    throw 'Pressing on the panel must block gameplay.'
}
if (-not $capture.Sample($true, $false, 1, 0, 0)) {
    throw 'Dragging outside the panel must remain blocked.'
}
if (-not $capture.Sample($true, $false, 0, 0, 1)) {
    throw 'The release frame must remain blocked.'
}
if ($capture.Sample($true, $false, 0, 0, 0)) {
    throw 'After release, other panels and world input must be available.'
}

Write-Output 'PASS: cultivation UI raycast area and pointer capture remain local.'
