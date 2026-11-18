$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$scene = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scenes/Map001 Gameplay.unity') -Raw
$sourceGuid = '676b18dce2fb46c6bd91fe173e587304'
$controllerGuid = 'b400bca43a39a3046aa5073b3dc92ce7'
$canvasGuid = '7eca53df5435e894182b590a2b50788f'
$prefix = '9060060000000000'

$documents = @{}
foreach ($match in [regex]::Matches(
    $scene, '(?ms)^--- !u!(\d+) &(\d+)(?: stripped)?\r?\n(.*?)(?=^--- !u!|\z)')) {
    $id = $match.Groups[2].Value
    if ($documents.ContainsKey($id)) { throw "Duplicate scene fileID: $id" }
    $documents[$id] = $match.Groups[3].Value
}

$instance = $documents['382546989']
if ($instance -notmatch "m_SourcePrefab: \{fileID: 100100000, guid: $sourceGuid" -or
    $instance -notmatch 'value: "\\u6BD2\\u86DB"' -or
    $instance -notmatch "objectReference: \{fileID: 9100000, guid: $controllerGuid" -or
    $instance -notmatch 'propertyPath: m_ApplyRootMotion\s+value: 0') {
    throw 'The scene spider must retain its art source and use Ls006 without root motion.'
}
foreach ($property in @(
    'm_LocalPosition.x', 'm_LocalPosition.y', 'm_LocalPosition.z',
    'm_LocalScale.x', 'm_LocalScale.y', 'm_LocalScale.z'
)) {
    $pattern = "propertyPath: $([regex]::Escape($property))\s+value: -?\d+(?:\.\d+)?\b"
    if ($instance -notmatch $pattern) {
        throw "The scene spider lost its $property override."
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
    @{ Id = $rootGameObject; Type = 'GameObject'; Source = '1557585983417840' },
    @{ Id = $rootTransform; Type = 'Transform'; Source = '4843028258353612' },
    @{ Id = $animator; Type = 'Animator'; Source = '95690966028241422' }
)) {
    if (-not $documents.ContainsKey($reference.Id) -or
        $documents[$reference.Id] -notmatch "$($reference.Type):" -or
        $documents[$reference.Id] -notmatch "m_CorrespondingSourceObject: \{fileID: $($reference.Source), guid: $sourceGuid" -or
        $documents[$reference.Id] -notmatch 'm_PrefabInstance: \{fileID: 382546989\}') {
        throw "The spider prefab instance is missing stripped reference $($reference.Id)."
    }
}

foreach ($component in @(
    @{ Id = $viewRoot; Script = 'b5bfd1506860cf8439e220de550cfebd' },
    @{ Id = $network; Script = 'e56a5ecba7ebd9b41b86264b6c5caf60' },
    @{ Id = $interpolator; Script = '45e0dcfc1e005a64cb077f74601ec396' },
    @{ Id = $driver; Script = 'f1b11ce53a064bf194e94d7b10be30c9' },
    @{ Id = $presenter; Script = 'c2eb531d8134f364187379d0663aed02' }
)) {
    if (-not $documents.ContainsKey($component.Id) -or
        $instance -notmatch "addedObject: \{fileID: $($component.Id)\}" -or
        $documents[$component.Id] -notmatch "m_GameObject: \{fileID: $rootGameObject\}" -or
        $documents[$component.Id] -notmatch "m_Script: \{fileID: 11500000, guid: $($component.Script)") {
        throw "Spider component $($component.Id) or its script is missing."
    }
}
if ($documents[$viewRoot] -notmatch "transformInterpolator: \{fileID: $interpolator\}" -or
    $documents[$viewRoot] -notmatch "animatorDriverComponent: \{fileID: $driver\}" -or
    $documents[$viewRoot] -notmatch "modelRoot: \{fileID: $rootTransform\}" -or
    $documents[$network] -notmatch "viewRoot: \{fileID: $viewRoot\}" -or
    $documents[$interpolator] -notmatch "targetTransform: \{fileID: $rootTransform\}" -or
    $documents[$driver] -notmatch "animator: \{fileID: $animator\}" -or
    $documents[$driver] -notmatch "monsterNetworkView: \{fileID: $network\}") {
    throw 'The spider network and animator references are incomplete.'
}
if ($instance -notmatch "addedObject: \{fileID: $collider\}" -or
    $documents[$collider] -notmatch "m_GameObject: \{fileID: $rootGameObject\}" -or
    $documents[$collider] -notmatch 'm_IsTrigger: 0' -or
    $documents[$collider] -notmatch 'm_Enabled: 1') {
    throw 'The spider needs an enabled solid capsule collider.'
}
if ($instance -notmatch "addedObject: \{fileID: $canvasTransform\}" -or
    $documents[$canvas] -notmatch "m_TransformParent: \{fileID: $rootTransform\}" -or
    $documents[$canvas] -notmatch "m_SourcePrefab: \{fileID: 100100000, guid: $canvasGuid" -or
    $documents[$canvas] -notmatch 'value: "\\u6BD2\\u86DB"' -or
    $documents[$canvasTransform] -notmatch "m_PrefabInstance: \{fileID: $canvas\}" -or
    $documents[$canvasGameObject] -notmatch "m_PrefabInstance: \{fileID: $canvas\}" -or
    $documents[$fillImage] -notmatch "m_PrefabInstance: \{fileID: $canvas\}" -or
    $documents[$presenter] -notmatch "monsterNetworkView: \{fileID: $network\}" -or
    $documents[$presenter] -notmatch "healthBarRoot: \{fileID: $canvasGameObject\}" -or
    $documents[$presenter] -notmatch "fillImage: \{fileID: $fillImage\}") {
    throw 'The spider overhead health bar is not connected.'
}
Write-Output 'PASS: Ls006 scene-only controller, network, animator, health bar, and collider wiring.'
