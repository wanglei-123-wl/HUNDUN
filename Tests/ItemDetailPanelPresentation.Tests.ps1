$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/UI/ItemDetail/ItemDetailPanelController.cs') -Raw
$prefab = Get-Content -LiteralPath (
    Join-Path $root 'Assets/预制体/查看详情.prefab') -Raw
$potion = Get-Content -LiteralPath (
    Join-Path $root 'Assets/掉落物品配置/生命药水.asset') -Raw

function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
}

function Get-Rect([string]$id) {
    $match = [regex]::Match($prefab,
        "(?ms)^--- !u!224 &$id\r?\n.*?(?=^--- !u!|\z)")
    if (-not $match.Success) { throw "Missing tooltip RectTransform $id" }
    return $match.Value
}

Assert-True ($potion -match '(?m)^    Description: "\\u56DE\\u590D') `
    'The potion must retain its configured description.'
Assert-True ($source -match '(?s)CurrentDetail = detail;\s*SetVisible\(false\);\s*ApplyDetail\(detail\);\s*Canvas\.ForceUpdateCanvases\(\);\s*FitScrollContent\(descriptionScroll, descriptionText\);\s*FitScrollContent\(detailScroll, detailText\);\s*ResetScroll\(descriptionScroll\);\s*ResetScroll\(detailScroll\);\s*SetScreenPosition\(screenPosition\);\s*SetVisible\(true\);') `
    'The tooltip must measure and reset scrolling before becoming visible.'
Assert-True ($source -match 'visualCanvasGroup\.alpha = visible \? 1f : 0f' -and
    $source -notmatch 'visualRoot\.SetActive\(false\)|visualTransform\.localScale = Vector3\.zero') `
    'Hiding the tooltip must preserve active layout and a nonzero scale.'
Assert-True ($source -match 'Mathf\.Max\(viewport\.rect\.height, height \+ 2f \* topInset\)' -and
    $source -match '(?s)scroll\.StopMovement\(\);\s*scroll\.horizontalNormalizedPosition = 0f;\s*scroll\.verticalNormalizedPosition = 1f;') `
    'Short content must fit the viewport, and old scroll inertia must be cleared.'
Assert-True ($source -match '(?s)textRect\.anchorMin = new Vector2\(textRect\.anchorMin\.x, 1f\);\s*textRect\.anchorMax = new Vector2\(textRect\.anchorMax\.x, 1f\);\s*textRect\.pivot = new Vector2\(textRect\.pivot\.x, 1f\);\s*textRect\.anchoredPosition = new Vector2\(\s*textRect\.anchoredPosition\.x, -5f\);') `
    'Runtime layout must correct text anchors in already-built remote scenes.'

foreach ($id in @('6783291081329240416', '727102737951995478')) {
    $rect = Get-Rect $id
    Assert-True ($rect -match 'm_AnchorMin: \{x: 0\.5, y: 1\}' -and
        $rect -match 'm_AnchorMax: \{x: 0\.5, y: 1\}' -and
        $rect -match 'm_AnchoredPosition: \{x: 0, y: -5\}' -and
        $rect -match 'm_Pivot: \{x: 0\.5, y: 1\}') `
        "Tooltip text $id must stay anchored to the top of its scroll content."
}

foreach ($id in @('5239972952397159497', '6273551780315726529')) {
    $scroll = [regex]::Match($prefab,
        "(?ms)^--- !u!114 &$id\r?\n.*?(?=^--- !u!|\z)")
    Assert-True ($scroll.Success -and
        $scroll.Value -match 'm_Horizontal: 0\s+m_Vertical: 1') `
        "Tooltip scroll $id must allow vertical movement only."
}

Write-Output 'Item detail panel presentation checks passed.'
