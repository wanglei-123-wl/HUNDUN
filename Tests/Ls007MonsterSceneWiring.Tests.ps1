$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$scene = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scenes/Map001 Gameplay.unity') -Raw
$sourceGuid = 'e98d21bd1c854c45a8719474f78c5126'
$controllerGuid = '9ae6482e5030b3b41aff81bd497c4264'
$canvasGuid = '7eca53df5435e894182b590a2b50788f'
$prefix = '9070070000000000'

$documents = @{}
foreach ($match in [regex]::Matches(
    $scene, '(?ms)^--- !u!(\d+) &(\d+)(?: stripped)?\r?\n(.*?)(?=^--- !u!|\z)')) {
    $id = $match.Groups[2].Value
    if ($documents.ContainsKey($id)) { throw "Duplicate scene fileID: $id" }
    $documents[$id] = $match.Groups[3].Value
}

$instance = $documents['1577461850']
if ($instance -notmatch "m_SourcePrefab: \{fileID: 100100000, guid: $sourceGuid" -or
    $instance -notmatch 'value: "\\u9CC4\\u5996"' -or
    $instance -notmatch "objectReference: \{fileID: 9100000, guid: $controllerGuid" -or
    $instance -notmatch 'propertyPath: m_ApplyRootMotion\s+value: 0') {
    throw 'The scene monster must retain its art source and use Ls007 without root motion.'
}
foreach ($property in @('m_LocalPosition.x', 'm_LocalPosition.y', 'm_LocalPosition.z')) {
    $pattern = "propertyPath: $([regex]::Escape($property))\s+value: -?\d+(?:\.\d+)?\b"
    if ($instance -notmatch $pattern) {
        throw "The scene monster lost its $property override."
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
    @{ Id = $rootGameObject; Type = 'GameObject'; Source = '4701395883959241788' },
    @{ Id = $rootTransform; Type = 'Transform'; Source = '4701395883958751260' },
    @{ Id = $animator; Type = 'Animator'; Source = '4701395883949735844' }
)) {
    if (-not $documents.ContainsKey($reference.Id) -or
        $documents[$reference.Id] -notmatch "$($reference.Type):" -or
        $documents[$reference.Id] -notmatch "m_CorrespondingSourceObject: \{fileID: $($reference.Source), guid: $sourceGuid" -or
        $documents[$reference.Id] -notmatch 'm_PrefabInstance: \{fileID: 1577461850\}') {
        throw "The monster instance is missing stripped reference $($reference.Id)."
    }
}

foreach ($component in @(
    @{ Id = $viewRoot; Script = 'b5bfd1506860cf8439e220de550cfebd' },
    @{ Id = $network; Script = 'e56a5ecba7ebd9b41b86264b6c5caf60' },
    @{ Id = $interpolator; Script = '45e0dcfc1e005a64cb077f74601ec396' },
    @{ Id = $driver; Script = 'b61f48204bbf424a9dc5a0474bf7fa04' },
    @{ Id = $presenter; Script = 'c2eb531d8134f364187379d0663aed02' }
)) {
    if (-not $documents.ContainsKey($component.Id) -or
        $instance -notmatch "addedObject: \{fileID: $($component.Id)\}" -or
        $documents[$component.Id] -notmatch "m_GameObject: \{fileID: $rootGameObject\}" -or
        $documents[$component.Id] -notmatch "m_Script: \{fileID: 11500000, guid: $($component.Script)") {
        throw "Monster component $($component.Id) or its script is missing."
    }
}
if ($documents[$viewRoot] -notmatch "transformInterpolator: \{fileID: $interpolator\}" -or
    $documents[$viewRoot] -notmatch "animatorDriverComponent: \{fileID: $driver\}" -or
    $documents[$viewRoot] -notmatch "modelRoot: \{fileID: $rootTransform\}" -or
    $documents[$network] -notmatch "viewRoot: \{fileID: $viewRoot\}" -or
    $documents[$interpolator] -notmatch "targetTransform: \{fileID: $rootTransform\}" -or
    $documents[$driver] -notmatch "animator: \{fileID: $animator\}" -or
    $documents[$driver] -notmatch "monsterNetworkView: \{fileID: $network\}") {
    throw 'The monster network and animator references are incomplete.'
}
if ($instance -notmatch "addedObject: \{fileID: $collider\}" -or
    $documents[$collider] -notmatch "m_GameObject: \{fileID: $rootGameObject\}" -or
    $documents[$collider] -notmatch 'm_IsTrigger: 0' -or
    $documents[$collider] -notmatch 'm_Enabled: 1') {
    throw 'The monster needs an enabled solid capsule collider.'
}
if ($instance -notmatch "addedObject: \{fileID: $canvasTransform\}" -or
    $documents[$canvas] -notmatch "m_TransformParent: \{fileID: $rootTransform\}" -or
    $documents[$canvas] -notmatch "m_SourcePrefab: \{fileID: 100100000, guid: $canvasGuid" -or
    $documents[$canvas] -notmatch 'value: "\\u9CC4\\u5996"' -or
    $documents[$canvasTransform] -notmatch "m_PrefabInstance: \{fileID: $canvas\}" -or
    $documents[$canvasGameObject] -notmatch "m_PrefabInstance: \{fileID: $canvas\}" -or
    $documents[$fillImage] -notmatch "m_PrefabInstance: \{fileID: $canvas\}" -or
    $documents[$presenter] -notmatch "monsterNetworkView: \{fileID: $network\}" -or
    $documents[$presenter] -notmatch "healthBarRoot: \{fileID: $canvasGameObject\}" -or
    $documents[$presenter] -notmatch "fillImage: \{fileID: $fillImage\}") {
    throw 'The monster overhead health bar is not connected.'
}
Write-Output 'PASS: Ls007 scene-only controller, network, animator, health bar, and collider wiring.'
