using UnityEngine;
using UnityEngine.UI;

namespace Game.Cultivation.Presentation
{
    [DisallowMultipleComponent]
    public sealed class ComprehensionCategoryTabs : MonoBehaviour
    {
        [SerializeField] private Button techniqueButton;
        [SerializeField] private GameObject techniqueSelected;
        [SerializeField] private Button alchemyButton;
        [SerializeField] private GameObject alchemySelected;
        [SerializeField] private Button forgingButton;
        [SerializeField] private GameObject forgingSelected;

        private void OnEnable()
        {
            if (techniqueButton != null)
                techniqueButton.onClick.AddListener(ShowTechnique);
            if (alchemyButton != null)
                alchemyButton.onClick.AddListener(ShowAlchemy);
            if (forgingButton != null)
                forgingButton.onClick.AddListener(ShowForging);
            ShowTechnique();
        }

        private void OnDisable()
        {
            if (techniqueButton != null)
                techniqueButton.onClick.RemoveListener(ShowTechnique);
            if (alchemyButton != null)
                alchemyButton.onClick.RemoveListener(ShowAlchemy);
            if (forgingButton != null)
                forgingButton.onClick.RemoveListener(ShowForging);
        }

        private void ShowTechnique() => Select(techniqueSelected);

        private void ShowAlchemy() => Select(alchemySelected);

        private void ShowForging() => Select(forgingSelected);

        private void Select(GameObject selected)
        {
            if (selected == null)
                return;

            HideOther(techniqueSelected, selected);
            HideOther(alchemySelected, selected);
            HideOther(forgingSelected, selected);
            if (!selected.activeSelf)
                selected.SetActive(true);
        }

        private static void HideOther(GameObject effect, GameObject selected)
        {
            if (effect != null && effect != selected && effect.activeSelf)
                effect.SetActive(false);
        }
    }
}
