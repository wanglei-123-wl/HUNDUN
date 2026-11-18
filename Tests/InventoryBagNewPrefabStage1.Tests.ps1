$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$prefab = Get-Content -LiteralPath (
    Join-Path $root 'Assets/预制体/格子预制体/背包格子预制体.prefab') -Raw
$scene = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scenes/WorldShell.unity') -Raw

function Assert-True([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}
function Block([int]$type, [string]$id) {
    [regex]::Match($prefab, "(?ms)^--- !u!$type &$id`r?`n(.*?)(?=^--- !u!|\z)").Value
}

$ids = [regex]::Matches($prefab, '(?m)^--- !u!\d+ &(\d+)\r?$') |
    ForEach-Object { $_.Groups[1].Value }
Assert-True ($ids.Count -eq ($ids | Select-Object -Unique).Count) `
    'New prefab must not contain duplicate file IDs.'
$rootObject = Block 1 7794243069224851260
$rootRect = Block 224 5764986631924419604
$view = Block 114 860000000000500010
$button = Block 114 860000000000500009
Assert-True ($rootObject -match 'component: \{fileID: 860000000000500009\}' -and
    $rootObject -match 'component: \{fileID: 860000000000500010\}' -and
    $button -match 'guid: 4e29b1a8efbd4b44bb3f3716e73f07ff' -and
    $button -match 'm_TargetGraphic: \{fileID: 5117797185147072052\}' -and
    $view -match 'guid: c3ea7681dfeb4e1792a42a622bf524be') `
    'The new slot root needs a Button and InventorySlotView.'
foreach ($binding in @(
    'button: \{fileID: 860000000000500009\}',
    'background: \{fileID: 5117797185147072052\}',
    'itemIcon: \{fileID: 638944433185019316\}',
    'qualityFrame: \{fileID: 0\}',
    'selectedFrame: \{fileID: 688545590567119317\}',
    'stackCountText: \{fileID: 7047702740175582690\}',
    'newMark: \{fileID: 0\}'
)) {
    Assert-True ($view -match $binding) "Missing slot view binding: $binding"
}
foreach ($id in @(
    '688545590567119317', '1717437807601886101',
    '6217911538028296270'
)) {
    Assert-True ((Block 1 $id) -match 'm_IsActive: 0') `
        "Slot visual $id must start hidden."
}
Assert-True ((Block 114 7047702740175582690) -match '(?m)^  m_text: ?\r?$') `
    'The stack count must not show a sample value.'
Assert-True ((Block 114 5117797185147072052) -match 'm_RaycastTarget: 1' -and
    (Block 114 638944433185019316) -match 'm_RaycastTarget: 0' -and
    (Block 114 8420925706245194796) -match 'm_RaycastTarget: 0') `
    'The slot root receives clicks while icon and selection stay decorative.'
Assert-True ($rootRect -match 'm_SizeDelta: \{x: 75, y: 75\}' -and
    $rootRect -match '(?s)m_Children:\s+- \{fileID: 7133797244692650582\}\s+- \{fileID: 2014292818462792540\}\s+- \{fileID: 5346331171345993304\}\s+m_Father:' -and
    $prefab -notmatch '86000000000050000[1-8]') `
    'The new slot must retain only the original icon, selection and count visuals.'
Assert-True ($scene -match
    'slotPrefab: \{fileID: 860000000000500010, guid: d51e73fbb1064584f9d68da46fa9db74, type: 3\}') `
    'WorldShell must reference the new slot prefab.'

Write-Output 'PASS: UI-only bag prefab bindings, hidden defaults, and WorldShell reference.'
