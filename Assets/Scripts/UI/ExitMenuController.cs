using UnityEngine;
using UnityEngine.UI;

namespace Game.UI
{
    [DisallowMultipleComponent]
    public sealed class ExitMenuController : MonoBehaviour
    {
        [SerializeField, InspectorName("退出界面")] private GameObject exitWindow;
        [SerializeField, InspectorName("菜单内容")] private GameObject menuContent;
        [SerializeField, InspectorName("继续游戏按钮")] private Button continueButton;

        public bool IsOpen => exitWindow != null && exitWindow.activeInHierarchy
            && menuContent != null && menuContent.activeInHierarchy;

        private void Awake()
        {
            Hide();
        }

        private void OnEnable()
        {
            GameplayUiInputGuard.RegisterModal(menuContent);
            if (continueButton != null)
                continueButton.onClick.AddListener(Hide);
        }

        private void OnDisable()
        {
            if (continueButton != null)
                continueButton.onClick.RemoveListener(Hide);
            GameplayUiInputGuard.UnregisterModal(menuContent);
            Hide();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape) && !GameplayUiInputGuard.BlocksWindowShortcuts)
                Show();
        }

        public void Show()
        {
            if (exitWindow == null || exitWindow == gameObject
                || menuContent == null || menuContent == gameObject)
                return;

            if (!menuContent.activeSelf)
                menuContent.SetActive(true);
            if (!exitWindow.activeSelf)
                exitWindow.SetActive(true);
        }

        public void Hide()
        {
            if (exitWindow != null && exitWindow != gameObject && exitWindow.activeSelf)
                exitWindow.SetActive(false);
        }
    }
}
