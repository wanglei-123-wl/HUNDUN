$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$panel = Get-Content -LiteralPath (
    Join-Path $root 'Assets/Scripts/Alchemy/Presentation/AlchemyPanelView.cs') -Raw
$method = [regex]::Match($panel,
    '(?s)private void RenderRequiredMaterials\(AlchemyRecipeDto recipe\).*?(?=\s*private Sprite FindRecipeIcon)').Value

function Assert-True($condition, [string]$message) {
    if (-not $condition) { throw $message }
}

Assert-True (-not [string]::IsNullOrWhiteSpace($method) -and
    $panel -match '(?s)private void Render\(\).*?ClearMedicineTexts\(\).*?if \(!ready .*?return;.*?RenderRequiredMaterials\(current\);\s*RebuildSlots\(\);' -and
    $method -notmatch 'secondaryMedicineText|auxiliaryMedicineText') `
    'Only a current recipe can populate the primary text; other medicine texts stay empty.'

$stub = @'
using System;
using System.Collections.Generic;
using System.Globalization;
using Game.Alchemy.Protocol;
using Game.Inventory;
using Game.Items;
namespace TMPro {
    public class TMP_Text { public string text; }
}
namespace Game.Alchemy.Protocol {
    public class AlchemyRecipeMaterialDto {
        public string itemId;
        public int count;
    }
    public class AlchemyRecipeDto {
        public AlchemyRecipeMaterialDto[] materials;
    }
}
namespace Game.Items {
    public class ItemDefinition { public string DisplayName; }
    public class ItemDefinitionTable {
        public readonly Dictionary<string, ItemDefinition> Entries = new();
        public bool TryGet(string id, out ItemDefinition item) =>
            Entries.TryGetValue(id, out item);
    }
}
namespace Game.Inventory {
    public class InventoryModuleRuntime {
        public ItemDefinitionTable ItemDefinitions = new();
    }
}
namespace Game.Alchemy.Presentation {
    public class MedicineTextHarness {
        public TMPro.TMP_Text primaryMedicineText = new();
        public InventoryModuleRuntime inventory = new();
        __METHOD__
    }
}
'@
Add-Type -TypeDefinition $stub.Replace('__METHOD__', $method) -IgnoreWarnings |
    Out-Null
$view = [Game.Alchemy.Presentation.MedicineTextHarness]::new()
$view.inventory.ItemDefinitions.Entries.Add('hui_chun_cao',
    [Game.Items.ItemDefinition]@{ DisplayName = '回春草' })
$recipe = [Game.Alchemy.Protocol.AlchemyRecipeDto]::new()
$recipe.materials = @(
    [Game.Alchemy.Protocol.AlchemyRecipeMaterialDto]@{
        itemId = 'hui_chun_cao'; count = 4
    },
    [Game.Alchemy.Protocol.AlchemyRecipeMaterialDto]@{
        itemId = 'ling_shi'; count = 7
    }
)
$render = $view.GetType().GetMethod('RenderRequiredMaterials',
    [Reflection.BindingFlags]'Instance, NonPublic')
$render.Invoke($view, @($recipe)) | Out-Null
Assert-True ($view.primaryMedicineText.text -eq '回春草 x4、ling_shi x7') `
    'All catalog materials must appear in primary text with required counts and stable ID fallback.'

$view.inventory.ItemDefinitions.Entries.Add('ling_shi',
    [Game.Items.ItemDefinition]@{ DisplayName = '灵石' })
$render.Invoke($view, @($recipe)) | Out-Null
Assert-True ($view.primaryMedicineText.text -eq '回春草 x4、灵石 x7') `
    'Available local item names must replace IDs without changing catalog quantities.'
$recipe.materials = @()
$render.Invoke($view, @($recipe)) | Out-Null
Assert-True ($view.primaryMedicineText.text -eq '') `
    'An empty catalog material list must not leave stale medicine text.'
Write-Output 'PASS: primary medicine materials and counts, name fallback, and empty-state clearing.'
