$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$scene = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scenes/WorldShell.unity') -Raw
$hudSource = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/CharacterPanel/Presentation/ProgressionHudPresenter.cs') -Raw
$cultivationSource = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Cultivation/Presentation/CultivationProgressPresenter.cs') -Raw
$realmSource = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Cultivation/Presentation/CultivationRealmPresenter.cs') -Raw
$characterSource = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/CharacterPanel/Presentation/CharacterPanelPresenter.cs') -Raw

function Get-Record([string]$kind, [string]$id) {
    $match = [regex]::Match($scene,
        "(?ms)^--- !u!$kind &$id\r?\n.*?(?=^--- !u!|\z)")
    if (-not $match.Success) { throw "Missing scene record $kind $id" }
    return $match.Value
}

$bottom = Get-Record '1' '1966549451'
$hud = Get-Record '114' '3100000002'
$cultivation = Get-Record '114' '2200000021'
$realm = Get-Record '114' '904205824'
$character = [regex]::Match($scene,
    '(?ms)^--- !u!114 &\d+\r?\n(?:(?!^--- !u!).)*?guid: c58c60ffbd7741429aeec66f2da79d4d.*?(?=^--- !u!|\z)').Value
if ($bottom -notmatch 'component: \{fileID: 3100000002\}' -or
    $hud -notmatch 'guid: 3e5e4d4f6dd44e58b8f9a1ef7cf723a1' -or
    $hud -notmatch 'session: \{fileID: 2108349142\}' -or
    $hud -notmatch 'nicknameText: \{fileID: 591938116\}' -or
    $hud -notmatch 'realmText: \{fileID: 1705721523\}' -or
    $hud -notmatch 'experienceText: \{fileID: 1395329221\}' -or
    $hud -notmatch 'experienceFill: \{fileID: 1875507875\}' -or
    (Get-Record '114' '1875507875') -notmatch 'm_Type: 3') {
    throw 'Bottom HUD nickname, realm or cultivation widgets are not wired.'
}

if ($cultivation -notmatch 'session: \{fileID: 2108349142\}' -or
    $realm -notmatch 'session: \{fileID: 2108349142\}' -or
    $character -notmatch 'session: \{fileID: 2108349142\}' -or
    $character -notmatch 'nickname: \{fileID: 1120892252\}' -or
    $hudSource -notmatch 'CultivationProgressDisplay.FromSnapshot' -or
    $cultivationSource -notmatch 'CultivationProgressDisplay.FromSnapshot' -or
    $hudSource -notmatch 'session.LocalNickname' -or
    $characterSource -notmatch 'session.LocalNickname' -or
    $hudSource -notmatch 'result.jingjieName' -or
    $realmSource -notmatch 'result.jingjieName') {
    throw 'HUD and panels must read the same progression and nickname source.'
}

Write-Output 'PASS: WorldShell HUD shares nickname, realm and cultivation sources.'
