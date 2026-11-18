$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$settingsPath = Join-Path $root 'Assets/Scripts/NetWork/NetworkEndpointSettings.cs'
$managerPath = Join-Path $root 'Assets/Scripts/NetWork/NetworkManager.cs'
$exporterPath = Join-Path $root 'Assets/Editor/NetworkEndpointSettingsExporter.cs'
$scenePath = Join-Path $root 'Assets/Scenes/WorldShell.unity'
$assetPath = Join-Path $root 'Assets/Resources/NetworkEndpointSettings.asset'

$settings = Get-Content -LiteralPath $settingsPath -Raw
$model = [regex]::Match($settings, '(?s)\[Serializable\]\s+public sealed class NetworkEndpointProfile\s*\{.*?(?=\r?\npublic sealed class NetworkEndpointSnapshot)').Value
if ([string]::IsNullOrEmpty($model)) { throw 'Endpoint profile model is missing.' }
$stubs = @'
namespace UnityEngine
{
    [System.AttributeUsage(System.AttributeTargets.Field)]
    public sealed class SerializeField : System.Attribute {}
    [System.AttributeUsage(System.AttributeTargets.Field)]
    public sealed class InspectorNameAttribute : System.Attribute
    {
        public InspectorNameAttribute(string name) {}
    }
    [System.AttributeUsage(System.AttributeTargets.Field)]
    public sealed class TooltipAttribute : System.Attribute
    {
        public TooltipAttribute(string text) {}
    }
}
'@
Add-Type -TypeDefinition ("using System;`nusing UnityEngine;`n" + $stubs + $model) -IgnoreWarnings

function New-Profile([string]$name, [string]$tcpHost, [int]$tcpPort, [string]$httpBaseUrl) {
    $profile = [NetworkEndpointProfile]::new()
    foreach ($value in @(
        @('name', $name), @('tcpHost', $tcpHost),
        @('tcpPort', $tcpPort), @('httpBaseUrl', $httpBaseUrl)
    )) {
        $profile.GetType().GetField($value[0], [System.Reflection.BindingFlags]'NonPublic,Instance').
            SetValue($profile, $value[1])
    }
    return $profile
}

$profile = New-Profile 'Local' '127.0.0.1' 9000 'http://localhost:8080'
$errorMessage = ''
if (-not $profile.TryValidate([ref]$errorMessage) -or $profile.HttpPort -ne 8080) {
    throw "Independent TCP and HTTP hosts must be accepted: $errorMessage"
}

$profile = New-Profile 'Remote' 'gate.example.com' 9000 'https://auth.example.com/api'
if (-not $profile.TryValidate([ref]$errorMessage) -or $profile.HttpPort -ne 443) {
    throw 'An HTTPS base URL and separate TCP host must be accepted.'
}

foreach ($bad in @(
    (New-Profile '' 'localhost' 9000 'http://localhost:8080'),
    (New-Profile 'Bad TCP' 'localhost' 0 'http://localhost:8080'),
    (New-Profile 'Bad HTTP' 'localhost' 9000 'ftp://localhost:8080'),
    (New-Profile 'Credentials' 'localhost' 9000 'http://user:password@localhost:8080'),
    (New-Profile 'Query' 'localhost' 9000 'http://localhost:8080/?secret=1')
)) {
    $errorMessage = ''
    if ($bad.TryValidate([ref]$errorMessage) -or [string]::IsNullOrEmpty($errorMessage)) {
        throw "Invalid endpoint was accepted: $($bad.Name)"
    }
}

$manager = Get-Content -LiteralPath $managerPath -Raw
$exporter = Get-Content -LiteralPath $exporterPath -Raw
$scene = Get-Content -LiteralPath $scenePath -Raw
$asset = Get-Content -LiteralPath $assetPath -Raw
$auth = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/LoginScripts/AuthApiClient.cs') -Raw
$gate = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/NetWork/GateTransportBootstrap.cs') -Raw
$session = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/LoginScripts/AuthSession.cs') -Raw
$loginScene = Get-Content -LiteralPath (Join-Path $root 'Assets/Scenes/SampleScene.unity') -Raw
if ($manager -notmatch 'HashSet<string>\(StringComparer\.OrdinalIgnoreCase\)' -or
    $manager -notmatch 'Select a valid server environment' -or
    $exporter -notmatch 'SourceScenePath = "Assets/Scenes/WorldShell\.unity"' -or
    $exporter -match 'Demo 1' -or
    $exporter -notmatch 'source\.TryGetSelectedEndpointProfile' -or
    $exporter -match 'Selected environment differs from the current connection settings' -or
    $exporter -notmatch 'settings\.SetFromScene\(profile\)' -or
    $exporter -notmatch 'sourceScene\.isDirty' -or
    $exporter -notmatch 'EditorSceneManager\.sceneSaved \+= OnSceneSaved' -or
    $exporter -notmatch 'EditorApplication\.playModeStateChanged \+= OnPlayModeStateChanged' -or
    $exporter -notmatch 'EditorApplication\.isPlaying = false' -or
    $exporter -notmatch 'throw new BuildFailedException\(error\)' -or
    $manager -notmatch '(?s)private void Awake\(\).*?Instance = this;\s*if \(!TryConfigureServerEndpoint\(NetworkEndpointRuntime\.Load\(\), out string error\)\)' -or
    $manager -notmatch '(?s)public void Connect\(\)\s*\{\s*if \(!TryConfigureServerEndpoint\(NetworkEndpointRuntime\.Load\(\), out string error\)\)' -or
    $manager -match '(?m)^\s*\[HideInInspector, SerializeField\] private string serverHost;' -or
    $auth -notmatch 'UriBuilder\(settings\.HttpBaseUrl\)' -or
    $auth -notmatch 'NetworkEndpointSnapshot settings = NetworkEndpointRuntime\.Load\(\)' -or
    $auth -notmatch 'BuildUrl\(path, settings\)' -or
    $gate -notmatch 'NetworkEndpointSnapshot settings = NetworkEndpointRuntime\.Load\(\)' -or
    $gate -notmatch 'network\.TryConfigureServerEndpoint\(settings, out error\)' -or
    $settings -notmatch 'if \(!NetworkEndpointSettings\.IsValid\(settings\)\)' -or
    $session -notmatch 'PlayerPrefs\.GetString\(EndpointIdentityKey, ""\) != endpoint\.Identity' -or
    $exporter -notmatch 'EditorApplication\.isPlaying' -or
    $loginScene -match 'serverBaseUrl:') {
    throw 'Profile selection, WorldShell export guards, or runtime routing is missing.'
}

function Convert-SceneScalar([string]$value) {
    $value = $value.Trim()
    if ($value.StartsWith('"')) {
        return ConvertFrom-Json -InputObject $value
    }
    return $value
}

function Get-AssetScalar([string]$name) {
    $match = [regex]::Match($asset, "(?m)^  $([regex]::Escape($name)): (?<value>[^\r\n]*)\r?$")
    if (-not $match.Success) { throw "Exported setting is missing: $name" }
    return Convert-SceneScalar $match.Groups['value'].Value
}

$profileSections = [regex]::Matches($scene,
    '(?m)^  endpointProfiles:\r?\n(?<items>(?:  - name: [^\r\n]*\r?\n    tcpHost: [^\r\n]*\r?\n    tcpPort: [^\r\n]*\r?\n    httpBaseUrl: [^\r\n]*\r?\n)+)  selectedProfileName: (?<selection>[^\r\n]*)\r?$')
if ($profileSections.Count -ne 1) {
    throw 'WorldShell must have exactly one serialized endpoint profile selection.'
}
$selection = Convert-SceneScalar $profileSections[0].Groups['selection'].Value
$profiles = [regex]::Matches($profileSections[0].Groups['items'].Value,
    '(?m)^  - name: (?<name>[^\r\n]*)\r?\n    tcpHost: (?<host>[^\r\n]*)\r?\n    tcpPort: (?<port>[^\r\n]*)\r?\n    httpBaseUrl: (?<url>[^\r\n]*)\r?$')
$selected = @($profiles | Where-Object {
    (Convert-SceneScalar $_.Groups['name'].Value) -eq $selection
})
if ($selected.Count -ne 1) {
    throw 'WorldShell selected environment is missing or duplicated.'
}
$selectedProfile = New-Profile $selection `
    (Convert-SceneScalar $selected[0].Groups['host'].Value) `
    ([int](Convert-SceneScalar $selected[0].Groups['port'].Value)) `
    (Convert-SceneScalar $selected[0].Groups['url'].Value)
$errorMessage = ''
if (-not $selectedProfile.TryValidate([ref]$errorMessage)) {
    throw "WorldShell selected environment is invalid: $errorMessage"
}
if ((Get-AssetScalar 'selectedProfileName') -cne $selectedProfile.Name -or
    (Get-AssetScalar 'serverHost') -cne $selectedProfile.TcpHost -or
    [int](Get-AssetScalar 'tcpPort') -ne $selectedProfile.TcpPort -or
    [int](Get-AssetScalar 'httpPort') -ne $selectedProfile.HttpPort -or
    (Get-AssetScalar 'httpBaseUrl') -cne $selectedProfile.HttpBaseUrl) {
    throw 'Exported endpoint settings do not match the selected WorldShell environment.'
}

Write-Output 'Network endpoint profile tests passed.'
