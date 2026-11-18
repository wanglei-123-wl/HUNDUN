$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$game = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/GameSession.cs') -Raw
$method = [regex]::Match($game,
    '(?s)    public void ApplyItemUseJingjieBalance\(long\? current, long\? total\).*?(?=\r?\n    private void HandleAttributeSnapshotResult\()')
if (-not $method.Success) {
    throw 'Cannot find the production item-use balance handler.'
}
$stubs = @'
using System;
public sealed class ProgressionSnapshotResult
{
    public long jingjieExp;
    public long totalJingjieExp;
}
public partial class GameSession
{
    public bool IsWorldReady = true;
    public ProgressionSnapshotResult LocalProgressionSnapshot;
    public event Action<ProgressionSnapshotResult> LocalProgressionSnapshotChanged;
    private long pendingProgressionSnapshotSequence;
    public int RefreshCount;
    public long PendingSequence => pendingProgressionSnapshotSequence;
    public void Expect(long sequence) { pendingProgressionSnapshotSequence = sequence; }
    public void QueueProgressionSnapshotRefresh() { RefreshCount++; }
}
'@
Add-Type -TypeDefinition ($stubs + "`npublic partial class GameSession {`n" +
    $method.Value + "`n}") -IgnoreWarnings | Out-Null

function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
}

$owner = [GameSession]::new()
$owner.LocalProgressionSnapshot = [ProgressionSnapshotResult]::new()
$owner.LocalProgressionSnapshot.jingjieExp = 10
$owner.LocalProgressionSnapshot.totalJingjieExp = 50
$script:changes = 0
$owner.add_LocalProgressionSnapshotChanged([Action[ProgressionSnapshotResult]]{
    param($snapshot) $script:changes++
})
$owner.Expect(7)
$owner.ApplyItemUseJingjieBalance(35, 75)
Assert-True ($owner.LocalProgressionSnapshot.jingjieExp -eq 35 -and
    $owner.LocalProgressionSnapshot.totalJingjieExp -eq 75 -and
    $owner.PendingSequence -eq 0 -and $owner.RefreshCount -eq 1 -and
    $script:changes -eq 1) `
    'An authoritative balance must update both displays and invalidate older snapshots.'
$owner.ApplyItemUseJingjieBalance($null, $null)
Assert-True ($owner.LocalProgressionSnapshot.jingjieExp -eq 35 -and
    $owner.LocalProgressionSnapshot.totalJingjieExp -eq 75 -and
    $owner.RefreshCount -eq 2 -and $script:changes -eq 1) `
    'Null balances must request reconciliation without clearing the last good state.'
$owner.ApplyItemUseJingjieBalance(30, 70)
Assert-True ($owner.LocalProgressionSnapshot.jingjieExp -eq 35 -and
    $owner.LocalProgressionSnapshot.totalJingjieExp -eq 75 -and
    $owner.RefreshCount -eq 3 -and $script:changes -eq 1) `
    'A result older than the current cumulative balance cannot roll back the display.'
$owner.ApplyItemUseJingjieBalance(40, $null)
Assert-True ($owner.LocalProgressionSnapshot.jingjieExp -eq 40 -and
    $owner.LocalProgressionSnapshot.totalJingjieExp -eq 75 -and
    $script:changes -eq 2) `
    'An omitted cumulative balance must preserve its previous value.'
$owner.IsWorldReady = $false
$owner.ApplyItemUseJingjieBalance(100, 100)
Assert-True ($owner.LocalProgressionSnapshot.jingjieExp -eq 40 -and
    $owner.RefreshCount -eq 4) `
    'An inactive world cannot apply a late item-use result.'
Write-Output 'PASS: item-use jingjie balance, null, stale result and lifecycle.'
