$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$files = @(
    'Assets/Scripts/Friend/Protocol/FriendMessages.cs',
    'Assets/Scripts/Friend/Model/FriendRequestQueue.cs',
    'Assets/Scripts/Friend/Model/FriendState.cs'
)
$sources = ($files | ForEach-Object {
    (Get-Content (Join-Path $root $_) -Raw) -replace '(?m)^using [^\r\n]+;\r?\n', ''
}) -join "`n"
$stub = @'
public class NetMessageBase
{
    public string msgId;
    public string type;
}
'@
Add-Type -TypeDefinition ("using System;`nusing System.Collections.Generic;`n" +
    "using System.Text;`nusing Game.Friend.Protocol;`n" + $stub + $sources) `
    -IgnoreWarnings | Out-Null

function Assert-True([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}

$queue = [Game.Friend.Model.FriendRequestQueue]::new()
$queue.Reset(71)
Assert-True (-not $queue.RequestList()) 'Friend requests require a confirmed world entry.'
Assert-True ($queue.EnterWorld(71, 9001) -and $queue.RequestList() -and
    $queue.Search('小') -and $queue.Send(9002, '') -and
    $queue.Handle('request-1', $true)) 'All friend operations must use one queue.'
Assert-True (-not $queue.Search('') -and -not $queue.Search(('你' * 6)) -and
    -not $queue.Send(9001, '') -and -not $queue.Handle(' ', $false)) `
    'Invalid requests must not consume a sequence.'
$script:sent = [System.Collections.Generic.List[Game.Friend.Protocol.FriendRequestMessage]]::new()
$transmit = [Func[Game.Friend.Protocol.FriendRequestMessage,bool]]{
    param($request)
    $script:sent.Add($request)
    return $true
}
Assert-True ($queue.TrySendNext(71, 0, $transmit) -and
    $queue.Pending.requestSequence -eq 1 -and
    $queue.Pending.msgId -eq 'friend_list' -and
    -not $queue.TrySendNext(71, 1, $transmit)) `
    'A second operation must wait for the list response.'
Assert-True (-not $queue.Complete(70, 'friend_list', 1, $true, '') -and
    -not $queue.Complete(71, 'friend_list', 2, $true, '') -and
    $queue.Complete(71, 'friend_list', 1, $true, '')) `
    'Replies must match both epoch and pending request sequence.'
$queue.TrySendNext(71, 2, $transmit) | Out-Null
Assert-True ($script:sent[1].requestSequence -eq 2 -and
    $script:sent[1].keyword -eq '小') 'Search must take the next friend sequence.'
$queue.Complete(71, 'friend_search', 2, $false, 'friend_search_rate_limited') | Out-Null
$queue.TrySendNext(71, 3, $transmit) | Out-Null
Assert-True ($script:sent[2].requestSequence -eq 3 -and
    $script:sent[2].targetCharacterDbId -eq 9002) `
    'Business failures consume a sequence; send uses the numeric character ID.'
$queue.Complete(71, 'friend_request_send', 3, $true, '') | Out-Null
$queue.TrySendNext(71, 4, $transmit) | Out-Null
Assert-True ($script:sent[3].requestSequence -eq 4 -and
    $script:sent[3].requestId -eq 'request-1') `
    'Handling a received application uses the server request ID.'
$queue.Complete(71, 'friend_request_accept', 4, $false, 'request_expired') | Out-Null
Assert-True ($queue.NextSequence -eq 5) 'Expired requests consume their validated sequence.'
$queue.RequestList() | Out-Null
$queue.TrySendNext(71, 5, $transmit) | Out-Null
Assert-True ($queue.Complete(71, 'friend_list', 5, $false, 'sequence_gap') -and
    $queue.NextSequence -eq 5 -and $queue.NeedsRecovery -and
    -not $queue.RequestList()) `
    'Sequence errors must not be blindly advanced or retried.'
$queue.Reset(72)
$queue.EnterWorld(72, 9001) | Out-Null
$queue.RequestList() | Out-Null
$queue.TrySendNext(72, 0, $transmit) | Out-Null
$queue.CheckTimeout(72, 7.9)
Assert-True (-not $queue.NeedsRecovery) 'A pending request must keep its timeout window.'
$queue.CheckTimeout(72, 8)
Assert-True ($queue.NeedsRecovery -and $queue.Pending.requestSequence -eq 1 -and
    -not $queue.RequestList()) 'Unknown outcomes must freeze later friend requests.'
$queue.Reset(73)
$queue.EnterWorld(73, 9001) | Out-Null
$queue.RequestList() | Out-Null
$reject = [Func[Game.Friend.Protocol.FriendRequestMessage,bool]]{
    param($request) return $false
}
Assert-True (-not $queue.TrySendNext(73, 0, $reject) -and
    $queue.NeedsRecovery -and $queue.NextSequence -eq 2) `
    'A failed transport call cannot prove the request was not delivered.'
$queue.Reset(74)
$queue.EnterWorld(74, 9001) | Out-Null
Assert-True ($queue.RequestList() -and $queue.RequestList() -and
    $queue.WaitingCount -eq 1) 'Repeated refresh requests must be coalesced.'
$queue.TrySendNext(74, 0, $transmit) | Out-Null
Assert-True ($queue.RequestList() -and $queue.RequestList() -and
    $queue.WaitingCount -eq 0) 'An in-flight snapshot already covers a repeat refresh.'
$queue.Complete(74, 'friend_list', 1, $true, '') | Out-Null
foreach ($i in 1..32) {
    Assert-True ($queue.Search('小')) 'The bounded queue must admit requests up to its limit.'
}
Assert-True (-not $queue.Search('小') -and $queue.WaitingCount -eq 32) `
    'Additional UI actions must not grow the pending queue without bound.'

function New-List([long]$sequence, [Game.Friend.Protocol.FriendDto[]]$friends,
    [Game.Friend.Protocol.FriendRequestDto[]]$received) {
    return [Game.Friend.Protocol.FriendListResult]@{
        msgId = 'friend_list_result'
        type = 'friend_list_result'
        requestSequence = $sequence
        friends = $friends
        blocks = @()
        recentContacts = @()
        pendingReceivedRequests = $received
        pendingSentRequests = @()
    }
}
function New-Notice([string]$id) {
    return [Game.Friend.Protocol.FriendReceived]@{
        msgId = 'friend_request_received'
        type = 'friend_request_received'
        requestId = $id
        fromCharacterDbId = [long]9002
        fromNickname = '小雨'
        expireAt = [long]10000
    }
}
$state = [Game.Friend.Model.FriendState]::new()
$state.Reset(73)
$state.EnterWorld(73, 9001) | Out-Null
$state.Receive(73, (New-Notice 'early'))
$state.BeginSnapshot(73, 1)
$first = New-List 1 @([Game.Friend.Protocol.FriendDto]@{
    characterDbId = [long]9002
    nickname = '小雨'
}) @([Game.Friend.Protocol.FriendRequestDto]@{
    requestId = 'early'
    fromCharacterDbId = [long]9002
    status = 'pending'
})
Assert-True ($state.ApplySnapshot(73, $first) -and
    $state.Friends.Count -eq 1 -and $state.PendingReceived.Count -eq 1 -and
    $state.ReceivedNotices.Count -eq 0) `
    'An early notification must merge by ID without duplicating a full snapshot row.'
$sentId = [Guid]::NewGuid().ToString('D')
$state.RecordSentRequest(72, 9003, $sentId)
Assert-True ($state.SentRequestIds.Count -eq 0) `
    'A sent request from another connection must not enter the current state.'
$state.RecordSentRequest(73, 9003, $sentId)
Assert-True ($state.SentRequestIds[[long]9003] -eq $sentId -and
    $state.PendingSent.Count -eq 0) `
    'A successful request must retain its server ID without fabricating a full list row.'
$state.Handle(73, [Game.Friend.Protocol.FriendHandled]@{
    msgId = 'friend_request_handled'
    type = 'friend_request_handled'
    requestId = $sentId
    status = 'accepted'
})
Assert-True ($state.SentRequestIds.Count -eq 0) `
    'A handled request must release its locally remembered ID.'
$state.RecordSentRequest(73, 9003, $sentId)
$state.BeginSnapshot(73, 2)
$state.Receive(73, (New-Notice 'later'))
$state.ApplyDelta(73, [Game.Friend.Protocol.FriendListDelta]@{
    msgId = 'friend_list_delta'
    type = 'friend_list_delta'
    changeType = 'remove'
    targetCharacterDbId = [long]9002
})
Assert-True ($state.Friends.Count -eq 1 -and
    $state.ReceivedNotices.Count -eq 0) `
    'Pushes arriving during a query must wait for its matching snapshot.'
Assert-True (-not $state.ApplySnapshot(73, (New-List 3 @() @())) -and
    $state.ApplySnapshot(73, $first) -eq $false) `
    'An outdated snapshot must not replace the current collections.'
Assert-True ($state.ApplySnapshot(73, (New-List 2 @(
    [Game.Friend.Protocol.FriendDto]@{ characterDbId = [long]9002 }
) @())) -and $state.Friends.Count -eq 0 -and
    $state.SentRequestIds.Count -eq 0 -and
    $state.ReceivedNotices.Count -eq 1) `
    'The matching snapshot must reconcile remembered IDs, then replay buffered pushes.'
$state.Handle(73, [Game.Friend.Protocol.FriendHandled]@{
    msgId = 'friend_request_handled'
    type = 'friend_request_handled'
    requestId = 'later'
    status = 'accepted'
})
Assert-True ($state.ReceivedNotices.Count -eq 0) `
    'Handled applications must be removed by server request ID.'
$state.Reset(74)
$state.Receive(73, (New-Notice 'old'))
Assert-True (-not $state.HasSnapshot -and $state.ReceivedNotices.Count -eq 0 -and
    $state.Friends.Count -eq 0) 'Old connection pushes must not affect new state.'

$runtime = Get-Content (Join-Path $root `
    'Assets/Scripts/Friend/Runtime/FriendSessionRuntime.cs') -Raw
$session = Get-Content (Join-Path $root 'Assets/Scripts/GameSession.cs') -Raw
Assert-True ($runtime.Contains('session.LocalWorldEntered += HandleWorldEntered;') -and
    $runtime.Contains('RequestList();') -and
    $runtime.Contains('state.RecordSentRequest(epoch,') -and
    $session.Contains('.Ensure(networkManager, messageDispatcher, this);')) `
    'The runtime must query on entry and retain a matched successful send ID.'

Write-Output 'PASS: friend request queue, snapshot merging, push ordering, reconnection and session wiring.'
