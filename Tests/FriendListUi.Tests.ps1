$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'FriendSearchUi.Tests.ps1')

$root = Split-Path $PSScriptRoot -Parent
$scene = Get-Content -LiteralPath (Join-Path $root 'Assets/Scenes/WorldShell.unity') -Raw
$prefabPath = 'Assets/预制体/聊天预制体/好友框预制体.prefab'
$prefab = Get-Content -LiteralPath (Join-Path $root $prefabPath) -Raw
function Block([string]$yaml, [string]$id) {
    $match = [regex]::Match($yaml, "(?ms)^--- !u!\d+ &$id\r?\n.*?(?=^--- !u!|\z)")
    if (-not $match.Success) { throw "Missing YAML object $id" }
    return $match.Value
}
function Check([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}
function Guid-Of([string]$path) {
    $meta = Get-Content -LiteralPath (Join-Path $root "$path.meta") -Raw
    return [regex]::Match($meta, '(?m)^guid: ([a-f0-9]{32})$').Groups[1].Value
}

$controllerGuid = Guid-Of 'Assets/Scripts/Friend/UI/FriendListPanelController.cs'
$rowGuid = Guid-Of 'Assets/Scripts/Friend/UI/FriendListItemView.cs'
$prefabGuid = Guid-Of $prefabPath
$panel = Block $scene '3001000030'
$row = Block $prefab '900000000000000003'
Check ((Block $scene '1907362641').Contains('component: {fileID: 3001000030}') -and
    $panel.Contains("guid: $controllerGuid") -and
    $panel.Contains('friendsScroll: {fileID: 658026737}') -and
    $panel.Contains('friendsContent: {fileID: 387736487}') -and
    $panel.Contains("guid: $prefabGuid") -and
    (Block $scene '658026737').Contains('m_Content: {fileID: 387736487}') -and
    (Block $scene '658026737').Contains('m_Horizontal: 0')) `
    'The always-on presenter must bind the existing friend list and its prefab.'
Check ((Block $scene '387736486').Contains('component: {fileID: 3001000031}') -and
    (Block $scene '387736486').Contains('component: {fileID: 3001000032}') -and
    (Block $scene '3001000032').Contains('m_VerticalFit: 2') -and
    $row.Contains("guid: $rowGuid") -and
    $row.Contains('nicknameText: {fileID: 1668164784260500846}') -and
    $row.Contains('onlineImage: {fileID: 6801348361995948217}')) `
    'Friend list layout and the nickname/online references must be complete.'

$flags = [System.Reflection.BindingFlags]'Instance,NonPublic'
$view = [Game.Friend.UI.FriendListItemView]::new()
$viewType = $view.GetType()
$nickname = [TMPro.TextMeshProUGUI]::new()
$online = [UnityEngine.UI.Image]::new()
$viewType.GetField('nicknameText', $flags).SetValue($view, $nickname)
$viewType.GetField('onlineImage', $flags).SetValue($view, $online)
$viewType.GetField('selectButton', $flags).SetValue($view,
    [UnityEngine.UI.Button]::new())
$friend = [Game.Friend.Protocol.FriendDto]@{
    characterDbId = 1002
    nickname = '小雨'
    online = $true
    level = 1
}
$view.Bind($friend, $null)
$onlineColor = $online.color
Check ($view.CharacterDbId -eq 1002 -and $nickname.text -eq '小雨' -and
    $onlineColor.g -gt $onlineColor.r -and -not $online.raycastTarget) `
    'The row must show the nickname and an unclickable online projection.'
$friend.nickname = ''
$friend.online = $false
$view.Bind($friend, $null)
Check ($nickname.text -eq '角色 #1002' -and
    $online.color.g -lt $onlineColor.g) `
    'Missing nicknames must retain the ID, and the row cannot show a made-up level.'

$runtime = [Game.Friend.Runtime.FriendSessionRuntime]::new()
[Game.Friend.Runtime.FriendSessionRuntime]::Instance = $runtime
$runtime.State.Reset(71)
$runtime.State.EnterWorld(71, 1001) | Out-Null
$controller = [Game.Friend.UI.FriendListPanelController]::new()
$panelType = $controller.GetType()
$content = [UnityEngine.RectTransform]::new()
$scroll = [UnityEngine.UI.ScrollRect]::new()
$scroll.content = $content
$content.SetParent($scroll.transform, $false)
$scroll.transform.SetParent(([UnityEngine.GameObject]::new()).transform, $false)
$prefabView = [Game.Friend.UI.FriendListItemView]::new()
$viewType.GetField('nicknameText', $flags).SetValue($prefabView,
    [TMPro.TextMeshProUGUI]::new())
$viewType.GetField('onlineImage', $flags).SetValue($prefabView,
    [UnityEngine.UI.Image]::new())
$viewType.GetField('selectButton', $flags).SetValue($prefabView,
    [UnityEngine.UI.Button]::new())
foreach ($entry in @(
    @('friendsScroll', $scroll),
    @('friendsContent', $content),
    @('friendPrefab', $prefabView)
)) {
    $panelType.GetField($entry[0], $flags).SetValue($controller, $entry[1])
}
$panelType.GetMethod('Awake', $flags).Invoke($controller, @()) | Out-Null
Check ($controller.enabled -and
    $panelType.GetField('emptyText', $flags).GetValue($controller) -ne $null) `
    'Valid list references must create a visible empty-state presenter.'
$panelType.GetMethod('OnEnable', $flags).Invoke($controller, @()) | Out-Null
$rows = $panelType.GetField('rows', $flags).GetValue($controller)
$hint = $panelType.GetField('emptyText', $flags).GetValue($controller)
Check ($rows.Count -eq 0 -and $hint.text -match '正在获取') `
    'Before the first snapshot the friend list must not show stale rows.'

$runtime.State.BeginSnapshot(71, 1)
$snapshot = [Game.Friend.Protocol.FriendListResult]::new()
$snapshot.msgId = $snapshot.type = 'friend_list_result'
$snapshot.requestSequence = 1
$snapshot.friends = [Game.Friend.Protocol.FriendDto[]]@(
    [Game.Friend.Protocol.FriendDto]@{
        characterDbId = 1002; nickname = '小雨'; online = $true
    },
    [Game.Friend.Protocol.FriendDto]@{
        characterDbId = 1002; nickname = '新昵称'; online = $false
    },
    [Game.Friend.Protocol.FriendDto]@{
        characterDbId = 1003; nickname = '小明'; online = $true
    }
)
$snapshot.blocks = [Game.Friend.Protocol.BlockDto[]]@()
$snapshot.recentContacts = [Game.Friend.Protocol.RecentContactDto[]]@()
$snapshot.pendingReceivedRequests = [Game.Friend.Protocol.FriendRequestDto[]]@()
$snapshot.pendingSentRequests = [Game.Friend.Protocol.FriendRequestDto[]]@()
Check ($runtime.State.ApplySnapshot(71, $snapshot)) 'The snapshot must apply.'
Check ($rows.Count -eq 2 -and $rows.ContainsKey([long]1002) -and
    $rows[[long]1002].CharacterDbId -eq 1002 -and
    -not $hint.gameObject.activeSelf) `
    'Snapshot duplicates must render once by numeric character ID.'
$original = $rows[[long]1002]
$delta = [Game.Friend.Protocol.FriendListDelta]::new()
$delta.msgId = $delta.type = 'friend_list_delta'
$delta.changeType = 'add'
$delta.friend = [Game.Friend.Protocol.FriendDto]@{
    characterDbId = 1002; nickname = '覆盖昵称'; online = $true
}
$runtime.State.ApplyDelta(71, $delta)
Check ($rows.Count -eq 2 -and
    [object]::ReferenceEquals($original, $rows[[long]1002])) `
    'An add for an existing ID must reuse the row, not duplicate it.'
$delta.friend = [Game.Friend.Protocol.FriendDto]@{
    characterDbId = 1004; nickname = '新好友'; online = $false
}
$runtime.State.ApplyDelta(71, $delta)
Check ($rows.Count -eq 3 -and $rows.ContainsKey([long]1004)) `
    'A new friend delta must append one row.'
$delta.changeType = 'remove'
$delta.targetCharacterDbId = 1003
$runtime.State.ApplyDelta(71, $delta)
$runtime.State.ApplyDelta(71, $delta)
Check ($rows.Count -eq 2 -and -not $rows.ContainsKey([long]1003)) `
    'Remove must be idempotent and remove its old row.'
$runtime.State.Reset(72)
Check ($rows.Count -eq 0 -and $hint.text -match '正在获取' -and
    $panelType.GetField('boundEpoch', $flags).GetValue($controller) -eq 72) `
    'Connection reset must clear old friends before the next snapshot.'
$runtime.State.EnterWorld(72, 1001) | Out-Null
$runtime.State.BeginSnapshot(72, 1)
$snapshot.friends = [Game.Friend.Protocol.FriendDto[]]@()
Check ($runtime.State.ApplySnapshot(72, $snapshot) -and
    $rows.Count -eq 0 -and $hint.text -eq '暂无好友') `
    'An empty next-connection snapshot must remain empty.'
$panelType.GetMethod('OnDisable', $flags).Invoke($controller, @()) | Out-Null

Write-Output 'PASS: friend list scene/prefab bindings, snapshots, deltas, empty state and epoch isolation.'
