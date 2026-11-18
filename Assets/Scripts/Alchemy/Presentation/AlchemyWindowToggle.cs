using UnityEngine;

namespace Game.Alchemy.Presentation
{
    [DisallowMultipleComponent]
    public sealed class AlchemyWindowToggle : MonoBehaviour
    {
        [SerializeField] private GameObject alchemyWindow;

        public bool IsOpen => alchemyWindow != null && alchemyWindow.activeSelf;

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.X) && !GameplayUiInputGuard.BlocksWindowShortcuts)
                Toggle();
        }

        public void Toggle()
        {
            if (alchemyWindow != null && alchemyWindow != gameObject)
                alchemyWindow.SetActive(!alchemyWindow.activeSelf);
        }
    }
}
