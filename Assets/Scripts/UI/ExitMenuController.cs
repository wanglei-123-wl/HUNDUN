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
        [SerializeField, InspectorName("登出按钮")] private Button logoutButton;
        [SerializeField, InspectorName("退出游戏按钮")] private Button quitButton;
        [SerializeField, InspectorName("游戏会话")] private GameSession gameSession;

        private bool exitRequested;

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
                continueButton.onClick.AddListener(ContinueGame);
            if (logoutButton != null)
                logoutButton.onClick.AddListener(ReturnToCharacterSelect);
            if (quitButton != null)
                quitButton.onClick.AddListener(QuitGame);
        }

        private void OnDisable()
        {
            if (continueButton != null)
                continueButton.onClick.RemoveListener(ContinueGame);
            if (logoutButton != null)
                logoutButton.onClick.RemoveListener(ReturnToCharacterSelect);
            if (quitButton != null)
                quitButton.onClick.RemoveListener(QuitGame);
            GameplayUiInputGuard.UnregisterModal(menuContent);
            Hide();
        }

        private void ContinueGame()
        {
            if (!exitRequested) Hide();
        }

        private void ReturnToCharacterSelect()
        {
            if (!exitRequested && gameSession != null && gameSession.ReturnToCharacterSelect()) LockMenu();
        }

        private void QuitGame()
        {
            if (!exitRequested && gameSession != null && gameSession.QuitGame()) LockMenu();
        }

        private void LockMenu()
        {
            exitRequested = true;
            if (continueButton != null) continueButton.interactable = false;
            if (logoutButton != null) logoutButton.interactable = false;
            if (quitButton != null) quitButton.interactable = false;
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
