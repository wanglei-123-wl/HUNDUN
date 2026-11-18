using UnityEngine;

namespace Game.Forging.Presentation
{
    [DisallowMultipleComponent]
    public sealed class 锻造窗口开关 : MonoBehaviour
    {
        [SerializeField] private GameObject forgingWindow;

        public bool IsOpen => forgingWindow != null && forgingWindow.activeSelf;

        private void Awake()
        {
            if (forgingWindow != null && forgingWindow != gameObject)
                forgingWindow.SetActive(false);
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Z) && !GameplayUiInputGuard.BlocksWindowShortcuts)
                Toggle();
        }

        public void Toggle()
        {
            if (forgingWindow != null && forgingWindow != gameObject)
                forgingWindow.SetActive(!forgingWindow.activeSelf);
        }
    }
}
