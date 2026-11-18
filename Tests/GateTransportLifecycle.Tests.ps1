$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

function Read-Source([string]$relativePath) {
    Get-Content (Join-Path $root $relativePath) -Raw
}

function Assert-Source([bool]$condition, [string]$message) {
    if (-not $condition) { throw $message }
}

$manager = Read-Source 'Assets/Scripts/NetWork/NetworkManager.cs'
$dispatcher = Read-Source 'Assets/Scripts/NetWork/NetMessageDispatcher.cs'
$bootstrap = Read-Source 'Assets/Scripts/NetWork/GateTransportBootstrap.cs'
$managerAwake = [regex]::Match($manager, '(?s)private void Awake\(\).*?(?=private void Start\(\))').Value
$dispatcherAwake = [regex]::Match($dispatcher, '(?s)private void Awake\(\).*?(?=private void OnEnable\(\))').Value

Assert-Source ($managerAwake -match 'enabled = false;\s*Destroy\(this\);' -and
    $managerAwake -notmatch 'Destroy\(gameObject\)') 'A duplicate transport must keep its scene object.'
Assert-Source ($manager -match '(?s)private void OnDestroy\(\).*?if \(Instance != this\).*?return;.*?Disconnect\(\);') `
    'Destroying a duplicate transport must not disconnect the active transport.'
Assert-Source ($dispatcherAwake -match 'enabled = false;\s*Destroy\(this\);' -and
    $dispatcherAwake -notmatch 'Destroy\(gameObject\)') 'A duplicate dispatcher must keep its scene object.'
Assert-Source ($bootstrap -match 'NetworkManager network = NetworkManager.Instance;' -and
    $bootstrap -match 'if \(network == null\)' -and
    $bootstrap -match 'NetMessageDispatcher.Instance == null' -and
    $bootstrap -notmatch '\.Connect\(|\.Send\(|RuntimeInitializeOnLoadMethod') `
    'The bootstrap must reuse the transport without starting a connection.'

$scene = Read-Source 'Assets/Scenes/WorldShell.unity'
Assert-Source ($scene -match '(?s)--- !u!1 &1973135049\s+GameObject:.*?component: \{fileID: 1973135053\}.*?component: \{fileID: 1973135054\}.*?component: \{fileID: 1973135055\}.*?m_Name: NetworkSystem') `
    'Existing scene modules must remain attached to NetworkSystem.'

foreach ($path in @(
    'Assets/Scripts/GameSession.cs',
    'Assets/Scripts/Inventory/Integration/InventoryModuleRuntime.cs',
    'Assets/Scripts/Loot/Integration/LootModuleRuntime.cs',
    'Assets/Scripts/Player/LocalPlayerLifeController.cs'
)) {
    $source = Read-Source $path
    Assert-Source ($source -match 'if \(NetworkManager.Instance != null\)\s*(\{|networkManager = NetworkManager.Instance)' -and
        $source -match 'if \(NetMessageDispatcher.Instance != null\)\s*(\{|messageDispatcher = NetMessageDispatcher.Instance)') `
        "$path must prefer the active transport and dispatcher over scene references."
}

Assert-Source ((Read-Source 'Assets/Scripts/CharacterPanel/Presentation/CharacterPanelPresenter.cs') -match
    'network = NetworkManager.Instance') 'The character panel must subscribe to the active connection.'
Assert-Source ((Read-Source 'Assets/Scripts/LoginScripts/CharacterSelectPanelUI.cs') -match
    'StartCoroutine\(LoadSceneWithProgress\(character\.characterDbId\)\)') `
    'Scene entry must retain the selected character identity during loading.'

Write-Output 'PASS: duplicate components retain scene modules; active references and dormant bootstrap are ready.'
