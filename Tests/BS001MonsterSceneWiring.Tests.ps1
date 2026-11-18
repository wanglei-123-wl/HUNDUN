$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$scene = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scenes/Map001 Gameplay.unity') -Raw
$sourceGuid = '7f22c0b4e4c94410932f144351dc151d'
$controllerGuid = '68c23a04d70c48b4d988d84354a96cd2'
$canvasGuid = '7eca53df5435e894182b590a2b50788f'
$prefix = '9060010000000000'

$documents = @{}
foreach ($match in [regex]::Matches(
    $scene, '(?ms)^--- !u!(\d+) &(\d+)(?: stripped)?\r?\n(.*?)(?=^--- !u!|\z)')) {
    $id = $match.Groups[2].Value
    if ($documents.ContainsKey($id)) { throw "Duplicate scene fileID: $id" }
    $documents[$id] = $match.Groups[3].Value
}

$instance = $documents['1983369436']
if ($instance -notmatch "m_SourcePrefab: \{fileID: 100100000, guid: $sourceGuid" -or
    $instance -notmatch 'value: "\\u5DE8\\u77F3\\u9886\\u4E3B"' -or
    $instance -notmatch "objectReference: \{fileID: 9100000, guid: $controllerGuid" -or
    $instance -notmatch 'propertyPath: m_ApplyRootMotion\s+value: 0') {
    throw 'The scene boss must retain its art source and use BS001 without root motion.'
}
foreach ($property in @(
    'm_LocalPosition.x', 'm_LocalPosition.y', 'm_LocalPosition.z',
    'm_LocalScale.x', 'm_LocalScale.y', 'm_LocalScale.z'
)) {
    $pattern = "propertyPath: $([regex]::Escape($property))\s+value: -?\d+(?:\.\d+)?\b"
    if ($instance -notmatch $pattern) {
        throw "The scene boss lost its $property override."
    }
}

$rootGameObject = "${prefix}01"
$rootTransform = "${prefix}02"
$animator = "${prefix}03"
$viewRoot = "${prefix}04"
$network = "${prefix}05"
$interpolator = "${prefix}06"
$driver = "${prefix}07"
$presenter = "${prefix}08"
$collider = "${prefix}09"
$canvas = "${prefix}10"
$canvasTransform = "${prefix}11"
$canvasGameObject = "${prefix}12"
$fillImage = "${prefix}13"

foreach ($reference in @(
    @{ Id = $rootGameObject; Type = 'GameObject'; Source = '1706760125350986' },
    @{ Id = $rootTransform; Type = 'Transform'; Source = '4191744185429214' },
    @{ Id = $animator; Type = 'Animator'; Source = '95246818704427922' }
)) {
    if (-not $documents.ContainsKey($reference.Id) -or
        $documents[$reference.Id] -notmatch "$($reference.Type):" -or
        $documents[$reference.Id] -notmatch "m_CorrespondingSourceObject: \{fileID: $($reference.Source), guid: $sourceGuid" -or
        $documents[$reference.Id] -notmatch 'm_PrefabInstance: \{fileID: 1983369436\}') {
        throw "The boss prefab instance is missing stripped reference $($reference.Id)."
    }
}

foreach ($component in @(
    @{ Id = $viewRoot; Script = 'b5bfd1506860cf8439e220de550cfebd' },
    @{ Id = $network; Script = 'e56a5ecba7ebd9b41b86264b6c5caf60' },
    @{ Id = $interpolator; Script = '45e0dcfc1e005a64cb077f74601ec396' },
    @{ Id = $driver; Script = 'fc4d81eae92b4785ab086abb750af71d' },
    @{ Id = $presenter; Script = 'c2eb531d8134f364187379d0663aed02' }
)) {
    if (-not $documents.ContainsKey($component.Id) -or
        $instance -notmatch "addedObject: \{fileID: $($component.Id)\}" -or
        $documents[$component.Id] -notmatch "m_GameObject: \{fileID: $rootGameObject\}" -or
        $documents[$component.Id] -notmatch "m_Script: \{fileID: 11500000, guid: $($component.Script)") {
        throw "Boss component $($component.Id) or its script is missing."
    }
}
if ($documents[$viewRoot] -notmatch "transformInterpolator: \{fileID: $interpolator\}" -or
    $documents[$viewRoot] -notmatch "animatorDriverComponent: \{fileID: $driver\}" -or
    $documents[$viewRoot] -notmatch "modelRoot: \{fileID: $rootTransform\}" -or
    $documents[$network] -notmatch "viewRoot: \{fileID: $viewRoot\}" -or
    $documents[$interpolator] -notmatch "targetTransform: \{fileID: $rootTransform\}" -or
    $documents[$driver] -notmatch "animator: \{fileID: $animator\}" -or
    $documents[$driver] -notmatch "monsterNetworkView: \{fileID: $network\}") {
    throw 'The boss network and animator references are incomplete.'
}
if ($instance -notmatch "addedObject: \{fileID: $collider\}" -or
    $documents[$collider] -notmatch "m_GameObject: \{fileID: $rootGameObject\}" -or
    $documents[$collider] -notmatch 'm_IsTrigger: 0' -or
    $documents[$collider] -notmatch 'm_Enabled: 1') {
    throw 'The boss needs an enabled solid capsule collider.'
}
if ($instance -notmatch "addedObject: \{fileID: $canvasTransform\}" -or
    $documents[$canvas] -notmatch "m_TransformParent: \{fileID: $rootTransform\}" -or
    $documents[$canvas] -notmatch "m_SourcePrefab: \{fileID: 100100000, guid: $canvasGuid" -or
    $documents[$canvas] -notmatch 'value: "\\u5DE8\\u77F3\\u9886\\u4E3B"' -or
    $documents[$canvasTransform] -notmatch "m_PrefabInstance: \{fileID: $canvas\}" -or
    $documents[$canvasGameObject] -notmatch "m_PrefabInstance: \{fileID: $canvas\}" -or
    $documents[$fillImage] -notmatch "m_PrefabInstance: \{fileID: $canvas\}" -or
    $documents[$presenter] -notmatch "monsterNetworkView: \{fileID: $network\}" -or
    $documents[$presenter] -notmatch "healthBarRoot: \{fileID: $canvasGameObject\}" -or
    $documents[$presenter] -notmatch "fillImage: \{fileID: $fillImage\}") {
    throw 'The boss overhead health bar is not connected.'
}
Write-Output 'PASS: BS001 scene-only controller, network, animator, health bar, and collider wiring.'
