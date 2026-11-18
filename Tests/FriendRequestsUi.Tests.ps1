$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'FriendSearchUi.Tests.ps1')

$root = Split-Path $PSScriptRoot -Parent
$scene = Get-Content -LiteralPath (Join-Path $root 'Assets/Scenes/WorldShell.unity') -Raw
$prefabPath = 'Assets/预制体/聊天预制体/同意好友预制体.prefab'
$prefab = Get-Content -LiteralPath (Join-Path $root $prefabPath) -Raw
function Record([string]$yaml, [string]$id) {
    $match = [regex]::Match($yaml, "(?ms)^--- !u!\d+ &$id\r?\n.*?(?=^--- !u!|\z)")
    if (-not $match.Success) { throw "Missing YAML object $id" }
    return $match.Value
}
function Check([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}
$controllerGuid = (Get-Content -LiteralPath (Join-Path $root `
    'Assets/Scripts/Friend/UI/FriendRequestsPanelController.cs.meta') -Raw |
    Select-String 'guid: ([a-f0-9]{32})').Matches.Groups[1].Value
$rowGuid = (Get-Content -LiteralPath (Join-Path $root `
    'Assets/Scripts/Friend/UI/FriendRequestItemView.cs.meta') -Raw |
    Select-String 'guid: ([a-f0-9]{32})').Matches.Groups[1].Value
$prefabGuid = (Get-Content -LiteralPath (Join-Path $root "$prefabPath.meta") -Raw |
    Select-String 'guid: ([a-f0-9]{32})').Matches.Groups[1].Value
$panel = Record $scene '3001000020'
$template = Record $prefab '900000000000000002'
Check ((Record $scene '36594508').Contains('m_IsActive: 0') -and
    (Record $scene '36594511').Contains('m_GameObject: {fileID: 36594508}') -and
    (Record $scene '36594511').Contains('m_Enabled: 1') -and
    (Record $scene '36594511').Contains('m_RenderMode: 0') -and
    (Record $scene '1907362641').Contains('component: {fileID: 3001000020}')) `
    'The manually opened request dialog must have a visible scale and a persistent presenter.'
Check ($panel.Contains("guid: $controllerGuid") -and
    $panel.Contains('dialogRoot: {fileID: 36594508}') -and
    $panel.Contains('requestScroll: {fileID: 1919542671}') -and
    $panel.Contains('requestContent: {fileID: 1325124319}') -and
    $panel.Contains("guid: $prefabGuid") -and
    (Record $scene '1919542671').Contains('m_Content: {fileID: 1325124319}')) `
    'Request list and prefab references must belong to the request dialog.'
Check ((Record $scene '1325124318').Contains('component: {fileID: 3001000021}') -and
    (Record $scene '1325124318').Contains('component: {fileID: 3001000022}') -and
    (Record $scene '3001000022').Contains('m_VerticalFit: 2') -and
    $template.Contains("guid: $rowGuid") -and
    $template.Contains('nicknameText: {fileID: 5920539955831466487}') -and
    $template.Contains('acceptButton: {fileID: 4553460262921438649}') -and
    $template.Contains('rejectButton: {fileID: 8978097153466097754}')) `
    'The row must bind its own nickname and two independent action buttons.'

$flags = [System.Reflection.BindingFlags]'Instance,NonPublic'
$rowView = [Game.Friend.UI.FriendRequestItemView]::new()
$rowType = $rowView.GetType()
$name = [TMPro.TextMeshProUGUI]::new()
$acceptButton = [UnityEngine.UI.Button]::new()
$rejectButton = [UnityEngine.UI.Button]::new()
$rowType.GetField('nicknameText', $flags).SetValue($rowView, $name)
$rowType.GetField('acceptButton', $flags).SetValue($rowView, $acceptButton)
$rowType.GetField('rejectButton', $flags).SetValue($rowView, $rejectButton)
$now = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
$script:clickedId = ''
$script:accepted = $false
$callback = [Action[string,bool]] {
    param($id, $decision)
    $script:clickedId = $id
    $script:accepted = $decision
}
$rowView.Bind('received-1', '小明', ($now + 60000), $callback)
$rowView.UpdateState($false, $false, $now)
Check ($acceptButton.interactable -and $rejectButton.interactable -and
    $rowView.RequestId -eq 'received-1') 'A valid row must accept either decision by its request ID.'
$rowType.GetMethod('Accept', $flags).Invoke($rowView, @()) | Out-Null
Check ($script:clickedId -eq 'received-1' -and $script:accepted) `
    'Accept must only pass this row request ID.'
$rowType.GetMethod('Reject', $flags).Invoke($rowView, @()) | Out-Null
Check ($script:clickedId -eq 'received-1' -and -not $script:accepted) `
    'Reject must only pass this row request ID.'
$rowView.Bind('expired', '小雨', ($now - 1), $callback)
$rowView.UpdateState($false, $false, $now)
Check (-not $acceptButton.interactable -and -not $rejectButton.interactable -and
    $name.text.Contains('已过期')) 'Expired requests must be visible but cannot be handled.'
$rowType.GetMethod('Accept', $flags).Invoke($rowView, @()) | Out-Null
Check ($script:clickedId -eq 'received-1') 'An expired row must not invoke the callback.'

$runtime = [Game.Friend.Runtime.FriendSessionRuntime]::new()
[Game.Friend.Runtime.FriendSessionRuntime]::Instance = $runtime
$runtime.Requests.Reset(71)
$runtime.Requests.EnterWorld(71, 1001) | Out-Null
$runtime.State.Reset(71)
$runtime.State.EnterWorld(71, 1001) | Out-Null
$runtime.State.BeginSnapshot(71, 1)
$snapshot = [Game.Friend.Protocol.FriendListResult]::new()
$snapshot.msgId = $snapshot.type = 'friend_list_result'
$snapshot.requestSequence = 1
$snapshot.friends = [Game.Friend.Protocol.FriendDto[]]@()
$snapshot.blocks = [Game.Friend.Protocol.BlockDto[]]@()
$snapshot.recentContacts = [Game.Friend.Protocol.RecentContactDto[]]@()
$snapshot.pendingSentRequests = [Game.Friend.Protocol.FriendRequestDto[]]@()
$snapshot.pendingReceivedRequests = [Game.Friend.Protocol.FriendRequestDto[]]@(
    [Game.Friend.Protocol.FriendRequestDto]@{
        requestId = 'received-1'
        fromNickname = '小明'
        status = 'pending'
        expireAt = $now + 60000
    },
    [Game.Friend.Protocol.FriendRequestDto]@{
        requestId = 'expired'
        fromNickname = '小雨'
        status = 'pending'
        expireAt = $now - 1
    }
)
Check ($runtime.State.ApplySnapshot(71, $snapshot)) 'The initial list snapshot must apply.'
$presenter = [Game.Friend.UI.FriendRequestsPanelController]::new()
$presenterType = $presenter.GetType()
$content = [UnityEngine.RectTransform]::new()
$scroll = [UnityEngine.UI.ScrollRect]::new()
$scroll.content = $content
$prefabRow = [Game.Friend.UI.FriendRequestItemView]::new()
$rowType.GetField('nicknameText', $flags).SetValue($prefabRow,
    [TMPro.TextMeshProUGUI]::new())
$rowType.GetField('acceptButton', $flags).SetValue($prefabRow,
    [UnityEngine.UI.Button]::new())
$rowType.GetField('rejectButton', $flags).SetValue($prefabRow,
    [UnityEngine.UI.Button]::new())
foreach ($entry in @(
    @('dialogRoot', [UnityEngine.GameObject]::new()),
    @('requestScroll', $scroll),
    @('requestContent', $content),
    @('requestPrefab', $prefabRow),
    @('emptyStateText', [TMPro.TextMeshProUGUI]::new()),
    @('runtime', $runtime),
    @('boundEpoch', [long]71)
)) {
    $presenterType.GetField($entry[0], $flags).SetValue($presenter, $entry[1])
}
$refresh = $presenterType.GetMethod('RefreshRows', $flags)
$refresh.Invoke($presenter, @()) | Out-Null
$rows = $presenterType.GetField('rows', $flags).GetValue($presenter)
Check ($rows.Count -eq 2 -and
    -not $rows['expired'].IsExpired($now - 2) -and
    $rows['expired'].IsExpired($now)) 'Snapshot pending requests must render once with expiry preserved.'
$notice = [Game.Friend.Protocol.FriendReceived]::new()
$notice.msgId = $notice.type = 'friend_request_received'
$notice.requestId = 'received-1'
$notice.fromNickname = '重复通知'
$notice.expireAt = $now + 60000
$runtime.State.Receive(71, $notice)
$refresh.Invoke($presenter, @()) | Out-Null
Check ($rows.Count -eq 2 -and
    $rows['received-1'].RequestId -eq 'received-1') `
    'A duplicate notification may not create another row.'
$notice.requestId = 'received-2'
$notice.fromNickname = '小龙'
$runtime.State.Receive(71, $notice)
$refresh.Invoke($presenter, @()) | Out-Null
Check ($rows.Count -eq 3) 'A fresh received push must display locally.'
$presenterType.GetMethod('HandleRequest', $flags).Invoke($presenter,
    @('missing-id', $true)) | Out-Null
Check ($runtime.Requests.Pending -eq $null) 'An unknown row cannot send an operation.'
$presenterType.GetMethod('HandleRequest', $flags).Invoke($presenter,
    @('received-2', $false)) | Out-Null
Check ($runtime.Requests.Pending.msgId -eq 'friend_request_reject' -and
    $runtime.Requests.Pending.requestId -eq 'received-2') `
    'Reject must send the selected server request ID through the shared queue.'
$result = [Game.Friend.Protocol.FriendOperationResult]::new()
$result.success = $true
$result.requestMsgId = 'friend_request_accept'
$result.requestSequence = $runtime.Requests.Pending.requestSequence
$presenterType.GetMethod('HandleOperationResult', $flags).Invoke($presenter,
    @($result)) | Out-Null
Check ($presenterType.GetField('pendingSequence', $flags).GetValue($presenter) -eq 1) `
    'An accept result cannot complete a pending reject.'
$result.requestMsgId = 'friend_request_reject'
$result.requestSequence = 2
$presenterType.GetMethod('HandleOperationResult', $flags).Invoke($presenter,
    @($result)) | Out-Null
Check ($presenterType.GetField('pendingSequence', $flags).GetValue($presenter) -eq 1) `
    'The wrong sequence cannot complete the pending request.'
$result.requestSequence = 1
$runtime.Requests.Complete(71, 'friend_request_reject', 1, $true, '') | Out-Null
$presenterType.GetMethod('HandleOperationResult', $flags).Invoke($presenter,
    @($result)) | Out-Null
Check ($rows.Count -eq 2 -and -not $rows.ContainsKey('received-2') -and
    $runtime.Requests.WaitingCount -eq 1 -and
    -not $rowType.GetField('acceptButton', $flags).GetValue($rows['received-1']).interactable) `
    'A matching success must hide its row and disable others during the list refresh.'
$runtime.Requests.Reset(72)
$runtime.State.Reset(72)
$presenterType.GetMethod('Update', $flags).Invoke($presenter, @()) | Out-Null
Check ($rows.Count -eq 0 -and
    $presenterType.GetField('pendingSequence', $flags).GetValue($presenter) -eq 0) `
    'A new connection must clear rows and pending actions from the prior epoch.'
$runtime.Requests.EnterWorld(72, 1001) | Out-Null
$runtime.State.EnterWorld(72, 1001) | Out-Null
$runtime.State.BeginSnapshot(72, 1)
Check ($runtime.State.ApplySnapshot(72, $snapshot)) `
    'The next connection can load its own received requests.'
$refresh.Invoke($presenter, @()) | Out-Null
$presenterType.GetMethod('HandleRequest', $flags).Invoke($presenter,
    @('received-1', $true)) | Out-Null
Check ($runtime.Requests.Pending.msgId -eq 'friend_request_accept' -and
    $runtime.Requests.Pending.requestId -eq 'received-1') `
    'Accept must use the same queue and the selected server request ID.'
$result.success = $false
$result.requestSequence = $runtime.Requests.Pending.requestSequence
$result.requestMsgId = 'friend_request_accept'
$result.errorCode = 'request_expired'
$runtime.Requests.Complete(72, 'friend_request_accept',
    $result.requestSequence, $false, 'request_expired') | Out-Null
$presenterType.GetMethod('HandleOperationResult', $flags).Invoke($presenter,
    @($result)) | Out-Null
Check ($rows.ContainsKey('received-1') -and
    -not $rowType.GetField('acceptButton', $flags).GetValue($rows['received-1']).interactable -and
    $runtime.Requests.WaitingCount -eq 1) `
    'A server-side expiry must disable the row and refresh without marking success.'

Write-Output 'PASS: received request bindings, duplicate merging, expiry, action IDs and epoch isolation.'
