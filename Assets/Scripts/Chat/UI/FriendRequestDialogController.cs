using UnityEngine;
using UnityEngine.UI;

namespace Game.Chat.UI
{
    [RequireComponent(typeof(Button))]
    [DisallowMultipleComponent]
    public sealed class FriendRequestDialogController : MonoBehaviour
    {
        [SerializeField] private Button closeButton;
        [SerializeField] private GameObject requestDialog;

        private Button openButton;

        private void Awake()
        {
            openButton = GetComponent<Button>();
            if (openButton == null || closeButton == null || requestDialog == null
                || !closeButton.transform.IsChildOf(requestDialog.transform))
            {
                Debug.LogError("[FriendChat] Request dialog or its exit button is not bound correctly.", this);
                enabled = false;
                return;
            }
        }

        private void OnEnable()
        {
            if (openButton == null || closeButton == null || requestDialog == null) return;
            openButton.onClick.AddListener(Open);
            closeButton.onClick.AddListener(Close);
            GameplayUiInputGuard.RegisterModal(requestDialog);
        }

        private void OnDisable()
        {
            if (openButton != null)
                openButton.onClick.RemoveListener(Open);
            if (closeButton != null)
                closeButton.onClick.RemoveListener(Close);
            GameplayUiInputGuard.UnregisterModal(requestDialog);
        }

        private void Open()
        {
            requestDialog.SetActive(true);
        }

        private void Close()
        {
            requestDialog.SetActive(false);
        }
    }
}
