$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$scene = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scenes/WorldShell.unity') -Raw
$panelMeta = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Chat/UI/PrivateChatPanelController.cs.meta') -Raw
$panel = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Chat/UI/PrivateChatPanelController.cs') -Raw
$friendItem = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Friend/UI/FriendListItemView.cs') -Raw
$friendPanel = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Friend/UI/FriendListPanelController.cs') -Raw
$friendPrefab = Get-Content -LiteralPath (
    Join-Path $root 'Assets/预制体/聊天预制体/好友框预制体.prefab') -Raw

function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
}
function Get-Record([string]$kind, [string]$id) {
    $match = [regex]::Match($scene,
        "(?ms)^--- !u!$kind &$id\r?\n.*?(?=^--- !u!|\z)")
    if (-not $match.Success) { throw "Missing scene record $kind $id" }
    return $match.Value
}

$chatObject = Get-Record '1' '1907362641'
$panelRecord = Get-Record '114' '3001000035'
$contentObject = Get-Record '1' '123806974'
$contentLayout = Get-Record '114' '3001000033'
$contentFitter = Get-Record '114' '3001000034'
$privateScroll = Get-Record '114' '1545799734'

Assert-True ($panelMeta -match
    'guid: 7d4b2f4e3f4a4cc2b8b0d5e7f1a6c903' -and
    $chatObject -match 'component: \{fileID: 3001000035\}' -and
    $panelRecord -match
    'guid: 7d4b2f4e3f4a4cc2b8b0d5e7f1a6c903') `
    'The private chat controller must be attached to the existing chat interface.'

foreach ($field in @(
    'friendList: \{fileID: 3001000030\}',
    'inputField: \{fileID: 1339065476\}',
    'sendButton: \{fileID: 748232911\}',
    'scrollRect: \{fileID: 1545799734\}',
    'content: \{fileID: 123806975\}',
    'itemTemplate: \{fileID: 217644179295693684, guid: 5a7e4bcd9d9eb4f44ab41d2b6fc58b88, type: 3\}'
)) {
    Assert-True ($panelRecord -match $field) "Missing private chat reference: $field"
}

Assert-True ($contentObject -match 'component: \{fileID: 3001000033\}' -and
    $contentObject -match 'component: \{fileID: 3001000034\}' -and
    $contentLayout -match
    'guid: 59f8146938fff824cb5fd77236b75775' -and
    $contentFitter -match
    'guid: 3245ec927659c4140ac4f8d17403cc18' -and
    $privateScroll -match 'm_Horizontal: 0' -and
    $privateScroll -match 'm_Content: \{fileID: 123806975\}') `
    'Private chat Content must have vertical layout, size fitting and vertical scrolling.'

Assert-True ($friendPrefab -match
    'component: \{fileID: 900000000000000004\}' -and
    $friendPrefab -match
    'm_Script: \{fileID: 11500000, guid: 4e29b1a8efbd4b44bb3f3716e73f07ff, type: 3\}' -and
    $friendPrefab -match
    'selectButton: \{fileID: 900000000000000004\}') `
    'The friend row prefab must expose a clickable Button.'

Assert-True ($friendItem -match 'Action<FriendDto>' -and
    $friendItem -match 'selectButton\.onClick\.AddListener\(Select\)' -and
    $friendPanel -match 'event Action<FriendDto> FriendSelected' -and
    $friendPanel -match 'row\.Bind\(friend, HandleFriendSelected\)') `
    'Friend rows must publish selection without owning private chat behavior.'

Assert-True ($panel -match
    'runtime\.TrySendPrivateMessage\(selectedPartnerId, draft' -and
    $panel -match
    'runtime\.PrivateConversations\.GetMessages\(selectedPartnerId\)' -and
    $panel -match 'RemainingCooldown\("private"' -and
    $panel -match 'item\.Bind\(message\.senderName, message\.content' -and
    $panel -match 'case "target_not_found"' -and
    $panel -match 'case "private_chat_store_unavailable"' -and
    $panel -notmatch '消息已保存至服务器' -and
    $panel -notmatch 'GameNoticeService\.Show\("正在发送\.\.\."\)' -and
    $panel -match
    '!string\.Equals\(result\.errorCode, "target_offline"' -and
    $panel -match 'IsChatInputFocused\s*=>' -and
    $panel -match 'activePanel = this;' -and
    $panel -match 'activePanel == this\) activePanel = null') `
    'Private chat must render local conversations and send through the private runtime API.'

Write-Output 'PASS: private chat UI references, friend selection wiring and local message rendering.'
