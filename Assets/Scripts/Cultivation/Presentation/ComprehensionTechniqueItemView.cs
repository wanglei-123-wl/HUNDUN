using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Cultivation.Presentation
{
    [DisallowMultipleComponent]
    public sealed class ComprehensionTechniqueItemView : MonoBehaviour
    {
        [SerializeField] private Image icon;
        [SerializeField] private TMP_Text techniqueName;
        [SerializeField] private TMP_Text proficiency;
        [SerializeField] private Button selectionButton;
        [SerializeField] private GameObject selectedEffect;

        public string TechniqueId { get; private set; }
        public event Action<string> Selected;

        private void Awake()
        {
            Clear();
            if (selectionButton != null)
                selectionButton.onClick.AddListener(Select);
        }

        private void OnDestroy()
        {
            if (selectionButton != null)
                selectionButton.onClick.RemoveListener(Select);
        }

        private void Select()
        {
            if (!string.IsNullOrWhiteSpace(TechniqueId))
                Selected?.Invoke(TechniqueId);
        }

        public bool Bind(string techniqueId, string displayName,
            Sprite displayIcon, string proficiencyLabel)
        {
            if (string.IsNullOrWhiteSpace(techniqueId))
            {
                Clear();
                return false;
            }

            if (TechniqueId != techniqueId)
                SetSelected(false);
            TechniqueId = techniqueId;
            if (techniqueName != null)
                techniqueName.text = string.IsNullOrWhiteSpace(displayName)
                    ? "--" : displayName;
            if (proficiency != null)
                proficiency.text = string.IsNullOrWhiteSpace(proficiencyLabel)
                    ? "--" : proficiencyLabel;
            if (icon != null)
            {
                icon.sprite = displayIcon;
                icon.enabled = displayIcon != null;
                if (displayIcon != null)
                {
                    icon.color = Color.white;
                    icon.preserveAspect = true;
                }
            }
            return true;
        }

        public void SetSelected(bool selected)
        {
            if (selectedEffect != null && selectedEffect.activeSelf != selected)
                selectedEffect.SetActive(selected);
        }

        public void Clear()
        {
            TechniqueId = string.Empty;
            SetSelected(false);
            if (techniqueName != null)
                techniqueName.text = string.Empty;
            if (proficiency != null)
                proficiency.text = string.Empty;
            if (icon != null)
            {
                icon.sprite = null;
                icon.enabled = false;
            }
        }
    }
}
