using Game.Cultivation.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Cultivation.Presentation
{
    [DisallowMultipleComponent]
    public sealed class CultivationPanelToggle : MonoBehaviour
    {
        [SerializeField] private Button comprehensionButton;
        [SerializeField] private GameObject comprehensionContent;
        [SerializeField] private GameObject comprehensionSelected;
        [SerializeField] private Button cultivateButton;
        [SerializeField] private GameObject cultivationContent;
        [SerializeField] private GameObject cultivationSelected;
        [SerializeField] private Button skillButton;
        [SerializeField] private GameObject skillContent;
        [SerializeField] private GameObject skillSelected;

        private bool openOnSkills;

        private void OnEnable()
        {
            if (comprehensionButton != null)
                comprehensionButton.onClick.AddListener(ShowComprehension);
            if (cultivateButton != null)
                cultivateButton.onClick.AddListener(ShowCultivation);
            if (skillButton != null)
                skillButton.onClick.AddListener(ShowSkills);
            if (!openOnSkills)
                ShowComprehension();
            openOnSkills = false;
        }

        private void OnDisable()
        {
            if (comprehensionButton != null)
                comprehensionButton.onClick.RemoveListener(ShowComprehension);
            if (cultivateButton != null)
                cultivateButton.onClick.RemoveListener(ShowCultivation);
            if (skillButton != null)
                skillButton.onClick.RemoveListener(ShowSkills);
        }

        private void ShowComprehension()
        {
            Select(comprehensionContent, comprehensionSelected);
        }

        private void ShowCultivation()
        {
            if (cultivationContent == null || cultivationContent == gameObject)
                return;

            bool opening = !cultivationContent.activeSelf;
            Select(cultivationContent, cultivationSelected);
            if (opening)
                RefiningSessionRuntime.Instance?.Refresh();
        }

        private void ShowSkills()
        {
            Select(skillContent, skillSelected);
        }

        internal void OpenSkills()
        {
            openOnSkills = !isActiveAndEnabled;
            ShowSkills();
        }

        private void Select(GameObject content, GameObject selectedEffect)
        {
            if (content == null || content == gameObject)
                return;

            // Let the previous page release its listeners before enabling the next page.
            HideOther(comprehensionContent, content);
            HideOther(cultivationContent, content);
            HideOther(skillContent, content);
            HideOther(comprehensionSelected, selectedEffect);
            HideOther(cultivationSelected, selectedEffect);
            HideOther(skillSelected, selectedEffect);
            SetActive(selectedEffect, true);
            SetActive(content, true);
        }

        private void HideOther(GameObject target, GameObject selected)
        {
            if (target != selected)
                SetActive(target, false);
        }

        private void SetActive(GameObject target, bool active)
        {
            if (target != null && target != gameObject && target.activeSelf != active)
                target.SetActive(active);
        }
    }
}
