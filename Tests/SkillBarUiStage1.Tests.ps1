$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$scene = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scenes/WorldShell.unity') -Raw
$slotSource = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Combat/Skills/技能栏槽位视图.cs') -Raw
$barSource = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Combat/Skills/技能栏控制器.cs') -Raw
$skillPanelSource = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Combat/Skills/LearnedSkillListView.cs') -Raw
$inputSource = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Player/AuthorityMovement/LocalPlayerInputController.cs') -Raw

function Get-Record([string]$kind, [string]$id) {
    $match = [regex]::Match($scene,
        "(?ms)^--- !u!$kind &$id\r?\n.*?(?=^--- !u!|\z)")
    if (-not $match.Success) { throw "Missing scene record $kind $id" }
    return $match.Value
}

$left = Get-Record '224' '1115503880'
$slotObjects = @('1102412866', '473856987', '337203347', '285743009')
$slotTransforms = @('1102412867', '473856988', '337203348', '285743010')
$slotComponents = @('1102412870', '473856989', '337203349', '285743011')
$slotIconComponents = @('553414104', '1174272177', '759495424', '1878630537')
$buttons = @('1546530919', '511349614', '1358437328', '647628043')
$panel = Get-Record '114' '190542701'
$wiredButtons = [regex]::Matches($panel, '(?m)^  - \{fileID: (\d+)\}$')
$children = [regex]::Matches($left, '(?m)^  - \{fileID: (\d+)\}$')
if ($children.Count -ne $slotTransforms.Count) {
    throw 'Left skill bar does not have the expected number of slots.'
}

for ($index = 0; $index -lt $slotObjects.Count; $index++) {
    $number = $index + 1
    $transform = Get-Record '224' $slotTransforms[$index]
    $object = Get-Record '1' $slotObjects[$index]
    $component = Get-Record '114' $slotComponents[$index]
    $icon = Get-Record '114' $slotIconComponents[$index]
    if ($children[$index].Groups[1].Value -ne $slotTransforms[$index] -or
        $transform -notmatch 'm_Father: \{fileID: 1115503880\}' -or
        $object -notmatch "component: \{fileID: $($slotComponents[$index])\}" -or
        $component -notmatch 'guid: 7a6cd0f7b95d4c6d9d597ba1f1b06c5f' -or
        $component -notmatch "\\u69FD\\u4F4D\\u7F16\\u53F7`": $number" -or
        $component -notmatch '\\u6280\\u80FD\\u680F": \{fileID: 1966549456\}') {
        throw "Left skill slot $number is not wired to its own binding."
    }
    if ($icon -notmatch 'm_Color: \{r: 1, g: 1, b: 1, a: 0\}' -or
        $component -notmatch "\\u6280\\u80FD\\u56FE\\u6807`": \{fileID: $($slotIconComponents[$index])\}") {
        throw "Left skill slot $number icon setup changed."
    }
    if ($inputSource -notmatch "TrySendSkillSlotCast\($number, CurrentClientTick\)") {
        throw "Skill key $number is not wired to slot $number."
    }
    if ($wiredButtons.Count -ne $buttons.Count -or
        $wiredButtons[$index].Groups[1].Value -ne $buttons[$index] -or
        (Get-Record '114' $buttons[$index]) -notmatch 'guid: 4e29b1a8efbd4b44bb3f3716e73f07ff') {
        throw "Skill panel button $number is not wired to its matching slot."
    }
}

if ($barSource -notmatch 'TryBindLearnedSkill' -or
    $skillPanelSource -notmatch 'TryBindLearnedSkill\(slotNumber, selectedSkillId\)' -or
    $slotSource -match 'IDropHandler|IBeginDragHandler|IEndDragHandler|TryBindSkillBook' -or
    $slotSource -notmatch '图标颜色\.a = 显示图标 \? 1f : 0f' -or
    $slotSource -notmatch '技能图标\.color = 图标颜色') {
    throw 'Learned skill buttons or display-only skill slots are incomplete.'
}

Write-Output 'PASS: WorldShell learned skill buttons, left slot display and cast keys are wired.'
