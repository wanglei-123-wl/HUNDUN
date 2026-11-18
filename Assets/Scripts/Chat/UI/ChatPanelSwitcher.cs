using UnityEngine;
using UnityEngine.UI;

namespace Game.Chat.UI
{
    [DisallowMultipleComponent]
    public sealed class ChatPanelSwitcher : MonoBehaviour
    {
        [SerializeField] private Button publicButton;
        [SerializeField] private Button privateButton;
        [SerializeField] private GameObject publicContent;
        [SerializeField] private GameObject publicInput;
        [SerializeField] private GameObject privatePanel;

        private void Awake()
        {
            if (publicButton == null || privateButton == null
                || publicContent == null || publicInput == null || privatePanel == null
                || publicContent == publicInput || publicContent == privatePanel
                || publicInput == privatePanel
                || publicContent.transform.parent != publicInput.transform.parent)
            {
                Debug.LogError("[Chat] Panel switcher references are invalid.", this);
                enabled = false;
                return;
            }
        }

        private void OnEnable()
        {
            if (publicButton == null || privateButton == null) return;
            publicButton.onClick.AddListener(ShowPublic);
            privateButton.onClick.AddListener(ShowPrivate);
        }

        private void OnDisable()
        {
            if (publicButton != null)
                publicButton.onClick.RemoveListener(ShowPublic);
            if (privateButton != null)
                privateButton.onClick.RemoveListener(ShowPrivate);
        }

        private void ShowPublic()
        {
            privatePanel.SetActive(false);
            publicContent.SetActive(true);
            publicInput.SetActive(true);
        }

        private void ShowPrivate()
        {
            publicContent.SetActive(false);
            publicInput.SetActive(false);
            privatePanel.SetActive(true);
        }
    }
}
