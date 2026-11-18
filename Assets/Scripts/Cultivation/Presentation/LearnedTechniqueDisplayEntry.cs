using UnityEngine;

namespace Game.Cultivation.Presentation
{
    public sealed class LearnedTechniqueDisplayEntry
    {
        public string TechniqueId { get; }
        public string DisplayName { get; }
        public Sprite Icon { get; }
        public string ProficiencyLabel { get; }

        public LearnedTechniqueDisplayEntry(string techniqueId, string displayName,
            Sprite icon, string proficiencyLabel)
        {
            TechniqueId = techniqueId;
            DisplayName = displayName;
            Icon = icon;
            ProficiencyLabel = proficiencyLabel;
        }
    }
}
