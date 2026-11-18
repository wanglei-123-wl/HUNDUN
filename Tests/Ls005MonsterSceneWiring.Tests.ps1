$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$scene = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scenes/Map001 Gameplay.unity') -Raw
$sourceGuid = '443afc2412d74f44be8d5241437e576a'
$canvasGuid = '7eca53df5435e894182b590a2b50788f'

$documents = @{}
foreach ($match in [regex]::Matches(
    $scene, '(?ms)^--- !u!(\d+) &(\d+)(?: stripped)?\r?\n(.*?)(?=^--- !u!|\z)')) {
    $id = $match.Groups[2].Value
    if ($documents.ContainsKey($id)) {
        throw "Duplicate scene fileID: $id"
    }
    $documents[$id] = $match.Groups[3].Value
}

$instance = $documents['815450815']
if ($instance -notmatch "m_SourcePrefab: \{fileID: 100100000, guid: $sourceGuid" -or
    $instance -notmatch 'value: "\\u9AB7\\u9AC5\\u602A"' -or
    $instance -notmatch 'objectReference: \{fileID: 9100000, guid: 5d421e47fa8537e438f57f6e9375e356' -or
    $instance -notmatch 'propertyPath: m_ApplyRootMotion\s+value: 0') {
    throw 'The scene skeleton must keep its source prefab and use the Ls004 controller without root motion.'
}

$rootGameObject = '905500000000000007'
$rootTransform = '905500000000000008'
$animator = '905500000000000009'
$networkView = '905500000000000002'
$components = @(
    @{ Id = '905500000000000001'; Script = 'b5bfd1506860cf8439e220de550cfebd' },
    @{ Id = $networkView; Script = 'e56a5ecba7ebd9b41b86264b6c5caf60' },
    @{ Id = '905500000000000003'; Script = '45e0dcfc1e005a64cb077f74601ec396' },
    @{ Id = '905500000000000004'; Script = '181e89161af447ecb6cd9ebed1b7dff6' },
    @{ Id = '905500000000000005'; Script = 'c2eb531d8134f364187379d0663aed02' }
)
foreach ($component in $components) {
    if (-not $documents.ContainsKey($component.Id) -or
        $instance -notmatch "addedObject: \{fileID: $($component.Id)\}" -or
        $documents[$component.Id] -notmatch "m_GameObject: \{fileID: $rootGameObject\}" -or
        $documents[$component.Id] -notmatch "m_Script: \{fileID: 11500000, guid: $($component.Script)") {
        throw "The scene skeleton is missing component $($component.Id) or its script."
    }
}

if ($documents[$rootGameObject] -notmatch "m_PrefabInstance: \{fileID: 815450815\}" -or
    $documents[$rootTransform] -notmatch "fileID: 7695038878700202725, guid: $sourceGuid" -or
    $documents[$animator] -notmatch "fileID: 7695038878690313497, guid: $sourceGuid" -or
    $documents['905500000000000001'] -notmatch 'transformInterpolator: \{fileID: 905500000000000003\}' -or
    $documents['905500000000000001'] -notmatch 'animatorDriverComponent: \{fileID: 905500000000000004\}' -or
    $documents['905500000000000002'] -notmatch 'viewRoot: \{fileID: 905500000000000001\}' -or
    $documents['905500000000000003'] -notmatch "targetTransform: \{fileID: $rootTransform\}" -or
    $documents['905500000000000004'] -notmatch "animator: \{fileID: $animator\}" -or
    $documents['905500000000000004'] -notmatch "monsterNetworkView: \{fileID: $networkView\}") {
    throw 'The scene skeleton has an incomplete network or animation reference.'
}

$collider = $documents['905500000000000006']
if ($instance -notmatch 'addedObject: \{fileID: 905500000000000006\}' -or
    $collider -notmatch "m_GameObject: \{fileID: $rootGameObject\}" -or
    $collider -notmatch 'm_IsTrigger: 0') {
    throw 'The scene skeleton needs a solid capsule collider on its root.'
}

$canvas = $documents['905500000000000013']
if ($instance -notmatch 'addedObject: \{fileID: 905500000000000012\}' -or
    $canvas -notmatch "m_TransformParent: \{fileID: $rootTransform\}" -or
    $canvas -notmatch "m_SourcePrefab: \{fileID: 100100000, guid: $canvasGuid" -or
    $canvas -notmatch 'value: "\\u9AB7\\u9AC5\\u602A"' -or
    $documents['905500000000000005'] -notmatch 'healthBarRoot: \{fileID: 905500000000000010\}' -or
    $documents['905500000000000005'] -notmatch 'fillImage: \{fileID: 905500000000000011\}' -or
    $documents['905500000000000005'] -notmatch "monsterNetworkView: \{fileID: $networkView\}") {
    throw 'The skeleton health bar prefab or presenter references are incomplete.'
}
Write-Output 'PASS: Ls005 scene-only network, animator, health bar, and collider wiring.'
