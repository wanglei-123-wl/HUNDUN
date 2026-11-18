using UnityEngine;
using UnityEngine.UI;

namespace Game.CharacterPanel.Presentation
{
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public sealed class CharacterPanelToggle : MonoBehaviour
    {
        [SerializeField] private GameObject panelCanvas;
        [SerializeField] private Button closeButton;

        public bool IsOpen => panelCanvas != null && panelCanvas.activeSelf;

        private void OnEnable()
        {
            if (closeButton != null) closeButton.onClick.AddListener(Close);
        }

        private void OnDisable()
        {
            if (closeButton != null) closeButton.onClick.RemoveListener(Close);
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Tab) && !GameplayUiInputGuard.BlocksWindowShortcuts)
                Toggle();
        }

        private void Close()
        {
            SetOpen(false);
        }

        public void Toggle()
        {
            SetOpen(!IsOpen);
        }

        public void SetOpen(bool open)
        {
            if (panelCanvas == null || panelCanvas == gameObject
                || !panelCanvas.transform.IsChildOf(transform))
            {
                Debug.LogWarning("Character panel canvas must be a child of the toggle object.", this);
                return;
            }

            if (panelCanvas.activeSelf != open) panelCanvas.SetActive(open);
        }
    }
}
