$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$scene = Get-Content -LiteralPath (Join-Path $root 'Assets/Scenes/WorldShell.unity') -Raw
$recipe = Get-Content -LiteralPath (Join-Path $root 'Assets/预制体/格子预制体/丹方预制体.prefab') -Raw
$material = Get-Content -LiteralPath (Join-Path $root 'Assets/预制体/格子预制体/材料区域预制体.prefab') -Raw
$panel = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/Alchemy/Presentation/AlchemyPanelView.cs') -Raw
$slot = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/Alchemy/Presentation/AlchemyMaterialSlotView.cs') -Raw
$row = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/Alchemy/Presentation/AlchemyRecipeRowView.cs') -Raw
$dialog = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/Alchemy/Presentation/AlchemyQuantityConfirmationView.cs') -Raw
$recipeGuid = (Get-Content -LiteralPath (Join-Path $root 'Assets/预制体/格子预制体/丹方预制体.prefab.meta') -Raw |
    Select-String 'guid: ([0-9a-f]{32})').Matches[0].Groups[1].Value
$materialGuid = (Get-Content -LiteralPath (Join-Path $root 'Assets/预制体/格子预制体/材料区域预制体.prefab.meta') -Raw |
    Select-String 'guid: ([0-9a-f]{32})').Matches[0].Groups[1].Value

function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
}
function Section([string]$yaml, [string]$fileId) {
    $pattern = "(?ms)^--- !u!\d+ &$fileId`r?`n(.*?)(?=^--- !u!|\z)"
    $match = [regex]::Match($yaml, $pattern)
    Assert-True $match.Success "Missing Unity fileID $fileId."
    return $match.Groups[1].Value
}

$panelObject = Section $scene '827217722'
$panelComponent = Section $scene '860000000000100003'
Assert-True ($panelObject -match 'component: \{fileID: 860000000000100003\}' -and
    $panelComponent -match 'm_GameObject: \{fileID: 827217722\}' -and
    $panelComponent -match 'guid: f7439ae80fe54e8cb2dbaaf9377e7893') `
    'The alchemy panel component must be attached to the alchemy canvas.'
Assert-True ($panelComponent -match "recipePrefab: \{fileID: 860000000000100001, guid: $recipeGuid" -and
    $panelComponent -match "materialPrefab: \{fileID: 860000000000100002, guid: $materialGuid") `
    'The panel must reference the supplied recipe and material prefabs.'
foreach ($pair in @(
    @('recipeContent', '276652610'),
    @('materialContent', '1259107491'),
    @('recipeIcon', '1960997297'),
    @('recipeName', '1894905148'),
    @('startButton', '1417273727')
)) {
    $name, $id = $pair
    Assert-True ($panelComponent -match "${name}: \{fileID: $id\}" -and
        $scene -match "(?m)^--- !u!\d+ &$id`r?$") `
        "Scene reference $name is missing or points outside this scene."
}
foreach ($pair in @(
    @('primaryMedicineText', '1568357768'),
    @('secondaryMedicineText', '1615024040'),
    @('auxiliaryMedicineText', '1494859512')
)) {
    $name, $id = $pair
    Assert-True ($panelComponent -match "${name}: \{fileID: $id\}" -and
        (Section $scene $id) -match '(?m)^  m_text:[ \t]*(?:"")?\r?$' -and
        $panel -match "private TMP_Text $name;") `
        "The $name field must point to its empty scene text."
}
Assert-True ($panel -match '(?s)private void OnEnable\(\).*?ClearMedicineTexts\(\)' -and
    $panel -match '(?s)private void Render\(\).*?ClearMedicineTexts\(\)' -and
    $panel -match '(?s)private void ClearViews\(\).*?ClearMedicineTexts\(\)' -and
    $panel -match '(?s)private void ClearMedicineTexts\(\).*?primaryMedicineText\.text = string\.Empty;.*?secondaryMedicineText\.text = string\.Empty;.*?auxiliaryMedicineText\.text = string\.Empty;') `
    'All medicine texts must clear on open, render and close before material data is connected.'
Assert-True ((Section $scene '276652610') -match 'm_Father: \{fileID: 935523831\}' -and
    (Section $scene '1259107491') -match 'm_Father: \{fileID: 435165274\}' -and
    (Section $scene '435165276') -match 'm_RaycastTarget: 1') `
    'Material viewport can receive drops even when no material prefab is instantiated.'
Assert-True ((Section $scene '1960997295') -match 'm_IsActive: 0' -and
    (Section $scene '1894905148') -match '(?m)^  m_text:[ \t]*(?:"")?\r?$') `
    'An empty recipe detail must not display a placeholder icon or name.'

$recipeRoot = Section $recipe '334709263143785662'
$recipeIcon = Section $recipe '3710291447805812359'
$recipeView = Section $recipe '860000000000100001'
Assert-True ($recipeRoot -match 'm_IsActive: 1' -and
    $recipeIcon -match 'm_IsActive: 0' -and
    $recipeView -match 'icon: \{fileID: 3467359151563893415\}' -and
    $recipeView -match 'recipeName: \{fileID: 3608704083760925628\}') `
    'Recipe row must show its background with no placeholder icon.'
Assert-True ((Section $scene '179676599') -match '(?m)^  m_text:[ \t]*(?:"")?\r?$' -and
    (Section $scene '1894905148') -match '(?m)^  m_text:[ \t]*(?:"")?\r?$' -and
    $panel -match 'row\.Bind\(recipe\.recipeId, recipe\.name, FindRecipeIcon\(recipe\),' -and
    $panel -match 'recipeName\.text = current != null \? current\.name : string\.Empty;' -and
    $row -match 'recipeName\.text = learned \? displayName') `
    'Prefab and selected recipe names must use catalog names; stale grade sample text must not appear.'
Assert-True ($row -match 'recipeName\.color = learned\s*\? new Color\(\.16f, \.26f, \.21f, 1f\) : Color\.gray;' -and
    $row -match 'background\.color = selected\s*\? new Color\(\.76f, \.91f, \.78f, 1f\) : Color\.white;') `
    'Learned recipe names must remain readable on both selected and unselected row backgrounds.'
$materialRoot = Section $material '4261888921512990684'
$materialIcon = Section $material '1676944045584843234'
$materialView = Section $material '860000000000100002'
Assert-True ($materialRoot -match 'm_IsActive: 1' -and
    $materialIcon -match 'm_IsActive: 0' -and
    $materialView -match 'selectedIcon: \{fileID: 2191761708158052052\}' -and
    $materialView -match 'quantityText: \{fileID: 3354442903527065257\}' -and
    (Section $material '2783087684775908638') -match 'm_RaycastTarget: 1') `
    'Material slot must keep its drop surface active and its display icon hidden.'
Assert-True ($panel -match 'startButton\.onClick\.AddListener\(StartAlchemy\)' -and
    $panel -match 'runtime\.TryStart\(selection,' -and
    $panel -match 'runtime\.CanStart\(selection,' -and
    $panel -notmatch 'network\.Send\(|PrepareStart\(' -and
    $slot -match 'IDropHandler') `
    'The start button must delegate authority to the alchemy runtime.'
Assert-True ($row -match 'icon\.color = Color\.white' -and
    $slot -match 'selected > 0 \? icon : null' -and
    $slot -match 'quantityText\.text = selected > 0' -and
    $slot -notmatch 'selected\.ToString\(CultureInfo\.InvariantCulture\)\s*\+\s*"/"' -and
    $slot -match 'new Vector2\(44f, 15f\)' -and
    $dialog -match 'GameplayUiInputGuard\.RegisterModal\(gameObject\)') `
    'Icons must stay hidden without content, preserve original color and keep the dialog modal.'
Assert-True ($panel -match 'class AlchemyPanelView : MonoBehaviour, IDropHandler' -and
    $panel -match 'RectangleContainsScreenPoint\(' -and
    $panel -match 'foreach \(long stackId in selection\.SelectedStackIds\)' -and
    $panel -match 'foreach \(string identity in selection\.UnsupportedIdentities\)' -and
    $panel -match 'slot\.Bind\(identity, count, icon\)' -and
    $panel -notmatch 'for \(int i = 0; i < recipe\.materials\.Length; i\+\+\)' -and
    $panel -notmatch 'requiredItemId != source\.ItemId' -and
    $panel -notmatch 'selection\.RemoveInvalidStacks\(' -and
    $panel -match 'selection\.SetUnsupportedItem\(' -and
    $panel -match 'selection\.RemoveStack\(stackId\)' -and
    $panel -match 'selection\.RemoveUnsupportedItem\(identity\)') `
    'No recipe slots are created until placement; real stack/equipment identities drive display and removal.'
Assert-True ($panel -match 'selection\.SelectedCountForStack\(stack\.StackId\)' -and
    $panel -notmatch 'Math\.Min\(stack\.Count, Math\.Max\(1L, remaining\)\)' -and
    $panel -match 'error == AlchemySelectionError\.UnsupportedItem' -and
    $panel -match 'error == AlchemySelectionError\.EmptySelection') `
    'The quantity dialog shows actual preselection and invalid local submits have actionable messages.'
Assert-True ((Section $scene '595191402') -match 'm_SizeDelta: \{x: 268, y: 109\}' -and
    (Section $scene '595191402') -match 'm_AnchoredPosition: \{x: -130.6, y: 138.1\}' -and
    (Section $scene '595191403') -match 'm_Content: \{fileID: 1259107491\}\s+m_Horizontal: 0\s+m_Vertical: 1' -and
    (Section $scene '1223003667') -match 'm_Content: \{fileID: 276652610\}\s+m_Horizontal: 0\s+m_Vertical: 1') `
    'Two rows must fit the 268 by 109 area with 3-unit margins; both scroll views must be vertical only.'
Assert-True ($panel -match 'private const int MaterialColumns = 5;' -and
    $panel -match 'private const float MaterialSlotSize = 50f;' -and
    $panel -match 'private const float MaterialSpacing = 3f;' -and
    $panel -match 'index % MaterialColumns' -and
    $panel -match 'index / MaterialColumns' -and
    $panel -match 'SetSizeWithCurrentAnchors\(RectTransform.Axis.Vertical,' -and
    $panel -match 'slot.ReturnToBagRequested \+= ReturnMaterialToBag;' -and
    $panel -match 'slot.ReturnToBagRequested -= ReturnMaterialToBag;') `
    'Material slots must wrap at five columns and grow content vertically with matching spacing.'
Assert-True ($slot -match 'IBeginDragHandler, IDragHandler,' -and
    $slot -match 'IEndDragHandler' -and
    $slot -match 'InventoryDragPreview.TryCreate\(' -and
    $slot -match 'private void OnDisable\(\)\s*\{\s*ClearDrag\(\);' -and
    $slot -match 'RemoveRequested\?\.Invoke\(Identity\)' -and
    $slot -match 'ReturnToBagRequested\?\.Invoke\(identity, eventData\)' -and
    $panel -match 'if \(IsOverBagSlot\(eventData\)\)\s*RemoveMaterial\(identity\);' -and
    $panel -match 'bag.GridView.GetSlot\(slot.DisplayIndex\) == slot;' -and
    $panel -match 'private void RemoveMaterial\(string identity\)\s*\{\s*if \(!HasCurrentData\(\) \|\| selection.IsLocked') `
    'Right click and a drop over a real bag slot must use the same guarded local-only removal.'
Write-Output 'PASS: alchemy panel wiring and material/recipe prefab structure.'
