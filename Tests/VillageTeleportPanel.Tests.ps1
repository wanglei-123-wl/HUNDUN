$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$map = Get-Content -LiteralPath (Join-Path $root 'Assets/Scenes/Map001 Gameplay.unity') -Raw
$shell = Get-Content -LiteralPath (Join-Path $root 'Assets/Scenes/WorldShell.unity') -Raw
$panel = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/VillageTeleportPanel.cs') -Raw
$interaction = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/MapTeleporterInteraction.cs') -Raw
$session = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/GameSession.cs') -Raw
$panelGuid = (Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/VillageTeleportPanel.cs.meta') -Raw) -replace '(?s).*guid: ([0-9a-f]{32}).*', '$1'
$interactionGuid = (Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/MapTeleporterInteraction.cs.meta') -Raw) -replace '(?s).*guid: ([0-9a-f]{32}).*', '$1'
$prefabGuid = (Get-Content -LiteralPath (Join-Path $root 'Assets/预制体/NPC预制体/传送员.prefab.meta') -Raw) -replace '(?s).*guid: ([0-9a-f]{32}).*', '$1'

if ($shell -notmatch "(?s)--- !u!1 &999270564`r?`n.*?--- !u!114 &999270565`r?`n.*?guid: $panelGuid.*?panelRoot: \{fileID: 497090377\}.*?closeButton: \{fileID: 176687212\}.*?destinations:" -or
    $shell -notmatch '(?s)--- !u!1 &1058282300\r?\n.*?m_Name: "\\u7A7A\\u7075\\u6751\\u6309\\u94AE".*?--- !u!114 &1058282301\r?\n.*?guid: 4e29b1a8efbd4b44bb3f3716e73f07ff' -or
    $shell -notmatch '(?s)--- !u!114 &176687212\r?\n.*?guid: 4e29b1a8efbd4b44bb3f3716e73f07ff' -or
    $map -match "guid: $interactionGuid" -or
    $map -notmatch "(?ms)^--- !u!1001 &\d+\r?\nPrefabInstance:(?:(?!^--- !u!).)*?m_SourcePrefab: \{fileID: 100100000, guid: $prefabGuid, type: 3\}" -or
    $map -notmatch '(?s)--- !u!1 &890567748\r?\n.*?m_Name: EventSystem.*?m_IsActive: 0' -or
    $shell -notmatch '(?s)m_Name: EventSystem.*?m_IsActive: 1' -or
    $map -match "guid: $panelGuid" -or
    $panel -notmatch 'public void Show\(\)' -or
    $panel -notmatch '&& transferHandler != null\)\s*Open\(\)' -or
    $panel -notmatch 'querySessionId != null && !IsQueryCurrent\(\)' -or
    $panel -notmatch '(?s)private void Close\(\).*?querySessionId = null;.*?Unsubscribe\(\);' -or
    $panel -notmatch 'closeButton.onClick.AddListener\(Close\)' -or
    $panel -notmatch 'destination.button.onClick.AddListener\(destination.click\)' -or
    $panel -notmatch 'network.Send\(new MapTransferOptionsRequest\(\)\)' -or
    $panel -notmatch 'dispatcher.Register\("map_transfer_options_result", HandleOptionsResult\)' -or
    $panel -notmatch 'string.Equals\(result.sceneSessionId,' -or
    $panel -notmatch 'panelRoot.SetActive\(false\)' -or
    $panel -notmatch 'GameplayUiInputGuard.RegisterModal\(panelRoot\)' -or
    $panel -notmatch 'GameplayUiInputGuard.UnregisterModal\(panelRoot\)' -or
    $panel -match 'FindTeleporterTarget' -or
    $session -notmatch 'AddComponent<MapTeleporterInteraction>\(\)' -or
    $interaction -match 'teleporterName|GetRootGameObjects' -or
    $interaction -notmatch 'Input.GetMouseButtonDown\(1\)' -or
    $interaction -notmatch 'Input.GetMouseButtonUp\(1\)' -or
    $interaction -notmatch 'hit.collider.GetComponentInParent<VillageTeleporter>\(\)' -or
    $interaction -notmatch 'FindTeleporterUnderPointer\(camera, Input.mousePosition\) != target' -or
    $interaction -notmatch 'panel.Show\(\)') {
    throw 'Map teleporter interaction or WorldShell panel wiring is incomplete.'
}

Write-Output 'PASS: Map001 pointer interaction opens the WorldShell teleport panel.'
