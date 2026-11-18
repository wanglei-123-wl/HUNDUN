$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$scene = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scenes/WorldShell.unity') -Raw
$source = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Combat/Skills/快捷栏图标显示控制器.cs') -Raw
$protocol = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Inventory/Protocol/V2/QuickbarMessages.cs') -Raw

function Get-Record([string]$kind, [string]$id) {
    $match = [regex]::Match($scene,
        "(?ms)^--- !u!$kind &$id\r?\n.*?(?=^--- !u!|\z)")
    if (-not $match.Success) { throw "Missing scene record $kind $id" }
    return $match.Value
}

$right = Get-Record '1' '480379233'
$rightTransform = Get-Record '224' '480379234'
$controller = Get-Record '114' '3100000001'
if ($right -notmatch 'component: \{fileID: 3100000001\}' -or
    $controller -notmatch 'guid: b6e5b6f8f8ed44b1a5e2d2bc4e6f8d10' -or
    $controller -notmatch 'displayedSlotCount: 4' -or
    $protocol -notmatch 'const int SlotCount = 8') {
    throw 'Four visible quickbar slots must use the existing eight-slot protocol.'
}

$slotObjects = @('1573231148', '1146142419', '666912628', '1373901148')
$slotTransforms = @('1573231149', '1146142420', '666912629', '1373901149')
$iconObjects = @('1636739786', '1457511457', '960906616', '815440442')
$children = [regex]::Matches($rightTransform, '(?m)^  - \{fileID: (\d+)\}$')
if ($children.Count -ne $slotTransforms.Count) {
    throw 'Right quickbar must have four direct slot children.'
}
for ($index = 0; $index -lt $slotObjects.Count; $index++) {
    $slot = Get-Record '1' $slotObjects[$index]
    $icon = Get-Record '1' $iconObjects[$index]
    if ($children[$index].Groups[1].Value -ne $slotTransforms[$index] -or
        $slot -match 'guid: 7a6cd0f7b95d4c6d9d597ba1f1b06c5f' -or
        $slot -match 'component: \{fileID: (1573231150|1146142421|666912630|1373901150)\}' -or
        $icon -notmatch 'm_IsActive: 0' -or
        $icon -notmatch 'HUD\\u683C\\u5B50\\u6309\\u94AE') {
        throw "Right quickbar slot $($index + 1) has incorrect wiring."
    }
}

foreach ($id in @('1573231150', '1146142421', '666912630', '1373901150')) {
    if ($scene -match "(?m)^--- !u!114 &$id\r?$") {
        throw "Old skill drop component $id remains on the quickbar."
    }
}

if ($source -notmatch 'child.Find\("HUD格子按钮"\)' -or
    $source -notmatch '图标.raycastTarget = false' -or
    $source -notmatch '图标.gameObject.SetActive\(visible\)' -or
    $source -notmatch 'TryUseQuickbarSlot\(图标.槽位\)' -or
    $source -notmatch '快捷栏会话.TryAssign\(目标图标.槽位, stack.StackId\)' -or
    $source -notmatch '快捷栏会话.TryMove\(拖拽源.槽位, 目标图标.槽位\)' -or
    $source -notmatch 'hit.transform.IsChildOf\(panel.transform\)' -or
    $source -notmatch '快捷栏会话\?\.TryClear\(拖拽源.槽位\)') {
    throw 'Quickbar use, move, visual or bag-only clear logic is incomplete.'
}

Write-Output 'PASS: WorldShell right quickbar and bag-only unbind are wired.'
