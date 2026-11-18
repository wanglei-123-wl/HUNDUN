$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'FriendSearchUi.Tests.ps1') -CompileOnly

function Check([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}

$flags = [System.Reflection.BindingFlags]'Instance,NonPublic'
$runtime = [Game.Friend.Runtime.FriendSessionRuntime]::new()
$runtime.Requests.Reset(73)
$runtime.Requests.EnterWorld(73, 1001) | Out-Null
$runtime.State.Reset(73)
$runtime.State.EnterWorld(73, 1001) | Out-Null

$panel = [Game.Friend.UI.FriendRequestsPanelController]::new()
$panelType = $panel.GetType()
$hint = [TMPro.TextMeshProUGUI]::new()
$content = [UnityEngine.RectTransform]::new()
$scroll = [UnityEngine.UI.ScrollRect]::new()
$scroll.content = $content
$prefab = [Game.Friend.UI.FriendRequestItemView]::new()
$rowFlags = [System.Reflection.BindingFlags]'Instance,NonPublic'
$rowType = $prefab.GetType()
$rowType.GetField('nicknameText', $rowFlags).SetValue(
    $prefab, [TMPro.TextMeshProUGUI]::new())
$rowType.GetField('acceptButton', $rowFlags).SetValue(
    $prefab, [UnityEngine.UI.Button]::new())
$rowType.GetField('rejectButton', $rowFlags).SetValue(
    $prefab, [UnityEngine.UI.Button]::new())
foreach ($entry in @(
    @('requestScroll', $scroll),
    @('requestContent', $content),
    @('requestPrefab', $prefab),
    @('runtime', $runtime),
    @('boundEpoch', [long]73),
    @('emptyStateText', $hint)
)) {
    $panelType.GetField($entry[0], $flags).SetValue($panel, $entry[1])
}

$refresh = $panelType.GetMethod('RefreshRows', $flags)
$refresh.Invoke($panel, @()) | Out-Null
Check ($hint.text -eq '正在获取好友申请...' -and $hint.gameObject.activeSelf) `
    'Loading requests must still show the loading hint.'

$runtime.State.BeginSnapshot(73, 1)
$snapshot = [Game.Friend.Protocol.FriendListResult]::new()
$snapshot.msgId = $snapshot.type = 'friend_list_result'
$snapshot.requestSequence = 1
$snapshot.friends = [Game.Friend.Protocol.FriendDto[]]@()
$snapshot.blocks = [Game.Friend.Protocol.BlockDto[]]@()
$snapshot.recentContacts = [Game.Friend.Protocol.RecentContactDto[]]@()
$snapshot.pendingSentRequests = [Game.Friend.Protocol.FriendRequestDto[]]@()
$snapshot.pendingReceivedRequests = [Game.Friend.Protocol.FriendRequestDto[]]@()
Check ($runtime.State.ApplySnapshot(73, $snapshot)) `
    'The empty request snapshot must apply.'
$refresh.Invoke($panel, @()) | Out-Null
$rows = $panelType.GetField('rows', $flags).GetValue($panel)
Check ($rows.Count -eq 0 -and $hint.text -eq '' -and
    -not $hint.gameObject.activeSelf) `
    'No received requests must leave the panel empty without a hint.'

$notice = [Game.Friend.Protocol.FriendReceived]::new()
$notice.msgId = $notice.type = 'friend_request_received'
$notice.requestId = 'received-1'
$notice.fromNickname = 'Player'
$notice.expireAt = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds() + 60000
$runtime.State.Receive(73, $notice)
$refresh.Invoke($panel, @()) | Out-Null
Check ($rows.Count -eq 1 -and $hint.text -eq '' -and
    -not $hint.gameObject.activeSelf) `
    'An incoming request must still render its row without a hint.'

$handled = [Game.Friend.Protocol.FriendHandled]::new()
$handled.msgId = $handled.type = 'friend_request_handled'
$handled.requestId = 'received-1'
$handled.status = 'rejected'
$runtime.State.Handle(73, $handled)
$refresh.Invoke($panel, @()) | Out-Null
Check ($rows.Count -eq 0 -and $hint.text -eq '' -and
    -not $hint.gameObject.activeSelf) `
    'Handling the last request must not restore an empty-state hint.'

$runtime.Requests.MarkUncertain('friend_result_timeout')
$panelType.GetMethod('RefreshControls', $flags).Invoke($panel, @()) | Out-Null
Check ($hint.text -eq '好友状态不明，请重新连接' -and $hint.gameObject.activeSelf) `
    'Recovery must still show the reconnect hint.'

Write-Output 'PASS: request empty hint removed; loading, rows and recovery preserved.'
