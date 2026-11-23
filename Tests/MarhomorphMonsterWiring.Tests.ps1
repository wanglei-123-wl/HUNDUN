$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

function Read-Asset([string] $path) {
    Get-Content -LiteralPath (Join-Path $root $path) -Raw
}

function Get-Documents([string] $yaml) {
    $result = @{}
    foreach ($match in [regex]::Matches($yaml,
        '(?ms)^--- !u!(\d+) &(-?\d+)(?: stripped)?\r?\n(.*?)(?=^--- !u!|\z)')) {
        $id = $match.Groups[2].Value
        if ($result.ContainsKey($id)) { throw "Duplicate Unity fileID: $id" }
        $result[$id] = $match.Groups[3].Value
    }
    return $result
}

function Require([bool] $condition, [string] $message) {
    if (-not $condition) { throw $message }
}

$controller = Read-Asset 'Assets/怪物状态机/恐惧之翼_状态机.controller'
$states = @{
    Idle = '-7982635820565450657'
    Walk = '602273931887715300'
    Attack = '3471517877942017191'
    Death = '4953142598382331594'
}
$clips = @{
    Idle = '2d2730fd655a38e4da30453cad3ba8f7'
    Walk = '6cebab9a54bc3404cbf525a02beb1795'
    Attack = 'b99a0dd11d7c5bc4e86b0c1fdfb638b5'
    Death = '2e3ade417e34cae41931ffe03932f633'
}
$docs = Get-Documents $controller
Require ([regex]::Matches($controller, '(?m)^AnimatorState:\r?$').Count -eq 4) 'Keep the four user-created animation states.'
foreach ($state in $states.Keys) {
    Require ($docs[$states[$state]] -match "m_Motion: \{fileID: 7400000, guid: $($clips[$state])") "The original $state animation was replaced."
}
foreach ($parameter in @(@('IsMoving', 4), @('Attack', 9), @('Dead', 4))) {
    Require ($controller -match "m_Name: $($parameter[0])\s+m_Type: $($parameter[1])\b") "Missing animator parameter $($parameter[0])."
}
$edges = @(
    @{ From = 'Idle'; To = 'Attack'; Parameter = 'Attack'; Mode = 1; Exit = 0 },
    @{ From = 'Idle'; To = 'Walk'; Parameter = 'IsMoving'; Mode = 1; Exit = 0 },
    @{ From = 'Walk'; To = 'Attack'; Parameter = 'Attack'; Mode = 1; Exit = 0 },
    @{ From = 'Walk'; To = 'Idle'; Parameter = 'IsMoving'; Mode = 2; Exit = 0 },
    @{ From = 'Attack'; To = 'Idle'; Parameter = 'IsMoving'; Mode = 2; Exit = 1 },
    @{ From = 'Attack'; To = 'Walk'; Parameter = 'IsMoving'; Mode = 1; Exit = 1 }
)
foreach ($edge in $edges) {
    $source = $docs[$states[$edge.From]]
    $connected = $false
    foreach ($transition in [regex]::Matches($source, '(?m)^  - \{fileID: (-?\d+)\}\r?$')) {
        $body = $docs[$transition.Groups[1].Value]
        if ($body -match "m_DstState: \{fileID: $($states[$edge.To])\}" -and
            $body -match "m_ConditionMode: $($edge.Mode)\s+m_ConditionEvent: $($edge.Parameter)\b" -and
            $body -match "m_HasExitTime: $($edge.Exit)\b") {
            $connected = $true
            if ($edge.Exit -eq 1) {
                Require ($body -match 'm_ExitTime: 0.95') 'Attacks must finish before returning to locomotion.'
            }
        }
    }
    Require $connected "Missing transition $($edge.From) -> $($edge.To)."
}
$deathTransition = $docs['911011000000001006']
Require ($controller -match 'm_AnyStateTransitions:\s+- \{fileID: 911011000000001006\}' -and
    $deathTransition -match "m_DstState: \{fileID: $($states.Death)\}" -and
    $deathTransition -match 'm_ConditionEvent: Dead' -and
    $deathTransition -match 'm_HasExitTime: 0' -and
    $deathTransition -match 'm_CanTransitionToSelf: 0' -and
    $docs[$states.Death] -match 'm_Transitions: \[\]') 'Death must interrupt immediately and remain terminal.'

$folder = 'Assets/Monsters Full Pack Vol 1/Monsters Pack Vol 1/Marhomorph/FBX Files/'
foreach ($file in @('Marhomorph@Bite.FBX.meta', 'Marhomorph@Death.FBX.meta')) {
    Require ((Read-Asset ($folder + $file)) -match 'loopTime: 0') "$file must not loop."
}
foreach ($file in @('Marhomorph@Idle.FBX.meta', 'Marhomorph@Walk.FBX.meta')) {
    Require ((Read-Asset ($folder + $file)) -match 'loopTime: 1') "$file must loop."
}

$prefabPath = 'Assets/预制体/怪物预制体/恐惧之翼.prefab'
$prefab = Read-Asset $prefabPath
$prefabGuid = [regex]::Match((Read-Asset ($prefabPath + '.meta')), '(?m)^guid: (\w+)\r?$').Groups[1].Value
$driverGuid = [regex]::Match((Read-Asset 'Assets/Scripts/Monster/Animation/MarhomorphMonsterAnimatorDriver.cs.meta'), '(?m)^guid: (\w+)\r?$').Groups[1].Value
Require ($prefabGuid.Length -eq 32 -and $driverGuid.Length -eq 32) 'Missing asset GUIDs.'
$docs = Get-Documents $prefab
foreach ($block in $docs.Values) {
    foreach ($reference in [regex]::Matches($block, '\{fileID: (-?\d+)\}')) {
        $id = $reference.Groups[1].Value
        Require ($id -eq '0' -or $docs.ContainsKey($id)) "Broken prefab reference: $id"
    }
}
$go = '7091305905381461676'
$transform = '7091305905381430092'
$prefix = '9110110000000000'
foreach ($component in @(
    @{ Suffix = '02'; Guid = 'b5bfd1506860cf8439e220de550cfebd' },
    @{ Suffix = '03'; Guid = 'e56a5ecba7ebd9b41b86264b6c5caf60' },
    @{ Suffix = '04'; Guid = '45e0dcfc1e005a64cb077f74601ec396' },
    @{ Suffix = '05'; Guid = $driverGuid },
    @{ Suffix = '06'; Guid = 'c2eb531d8134f364187379d0663aed02' }
)) {
    $id = $prefix + $component.Suffix
    Require ($docs[$go] -match "component: \{fileID: $id\}" -and
        $docs[$id] -match "m_GameObject: \{fileID: $go\}" -and
        $docs[$id] -match "m_Script: \{fileID: 11500000, guid: $($component.Guid)") "Missing monster component $id."
}
Require ($docs['7091305905374182608'] -match 'm_Controller: \{fileID: 9100000, guid: 449733927d30b164a977bea47f2580fc' -and
    $docs['7091305905374182608'] -match 'm_Avatar: \{fileID: 9000000, guid: 96931e08e0851514dbd0d14e1f302686' -and
    $docs['7091305905374182608'] -match 'm_ApplyRootMotion: 0') 'Use the user controller and matching Marhomorph avatar without root motion.'
foreach ($connection in @(
    @('02', 'transformInterpolator', "${prefix}04"),
    @('02', 'animatorDriverComponent', "${prefix}05"),
    @('02', 'modelRoot', $transform),
    @('03', 'viewRoot', "${prefix}02"),
    @('04', 'targetTransform', $transform),
    @('05', 'animator', '7091305905374182608'),
    @('05', 'monsterNetworkView', "${prefix}03"),
    @('06', 'monsterNetworkView', "${prefix}03"),
    @('06', 'healthBarRoot', "${prefix}09"),
    @('06', 'fillImage', "${prefix}10"),
    @('06', 'healthText', "${prefix}12")
)) {
    Require ($docs[$prefix + $connection[0]] -match "$($connection[1]): \{fileID: $($connection[2])\}") "Broken component connection: $connection"
}
Require ($docs["${prefix}07"] -match 'm_IsTrigger: 0' -and $docs["${prefix}07"] -match 'm_Enabled: 1') 'Missing solid selection collider.'
Require ($docs["${prefix}08"] -match "m_TransformParent: \{fileID: $transform\}" -and
    $docs["${prefix}08"] -match 'm_SourcePrefab: \{fileID: 100100000, guid: 7eca53df5435e894182b590a2b50788f' -and
    $docs["${prefix}08"] -match 'value: "\\u6050\\u60E7\\u4E4B\\u7FFC"') 'The shared monster health bar must be parented and named correctly.'
Require ($docs[$transform] -match 'm_LocalPosition: \{x: 0, y: 0, z: 0\}' -and
    $docs[$transform] -match 'm_LocalScale: \{x: 0.6, y: 0.6, z: 0.6\}') 'Preserve the model scale and give the prefab a neutral origin.'

$scene = Read-Asset 'Assets/Scenes/Map001 Gameplay.unity'
$sceneDocs = Get-Documents $scene
$instance = $sceneDocs["${prefix}20"]
Require ($instance -match "m_SourcePrefab: \{fileID: 100100000, guid: $prefabGuid" -and
    $sceneDocs[$transform] -match "m_CorrespondingSourceObject: \{fileID: $transform, guid: $prefabGuid") 'Scene monster is not connected to the new prefab.'
foreach ($position in @(@('x', '-65.4564'), @('y', '21.763649'), @('z', '20.460712'))) {
    Require ($instance -match "propertyPath: m_LocalPosition.$($position[0])\s+value: $([regex]::Escape($position[1]))\b") 'The scene monster moved.'
}
$definition = Read-Asset 'Assets/Data/Monsters/Ls011.asset'
$definitionGuid = [regex]::Match((Read-Asset 'Assets/Data/Monsters/Ls011.asset.meta'), '(?m)^guid: (\w+)\r?$').Groups[1].Value
$catalog = Read-Asset 'Assets/Data/Monsters/怪物总表.asset'
Require ($definition -match '(?m)^  id: Ls011\r?$' -and
    $definition -match 'displayName: "\\u6050\\u60E7\\u4E4B\\u7FFC"' -and
    $definition -match "prefab: \{fileID: $go, guid: $prefabGuid, type: 3\}") 'Ls011 must reference the finished monster prefab.'
Require ([regex]::Matches($catalog, "guid: $definitionGuid, type: 2").Count -eq 1) 'Ls011 must be registered exactly once.'
$ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($asset in Get-ChildItem -LiteralPath (Join-Path $root 'Assets/Data/Monsters') -Filter '*.asset') {
    $entry = [regex]::Match((Get-Content -LiteralPath $asset.FullName -Raw), '(?m)^  id: (\S+)\r?$')
    if ($entry.Success) { Require ($ids.Add($entry.Groups[1].Value)) 'Duplicate monster ID.' }
}
Require ($controller -match "m_DefaultState: \{fileID: $($states.Idle)\}") 'The default state must be Idle, not Death.'
Write-Output 'PASS: own controller, corrected walk and entry state, prefab wiring, original placement and unique Ls011 registration.'
