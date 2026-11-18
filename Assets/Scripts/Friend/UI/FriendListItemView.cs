using System;
using Game.Friend.Protocol;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Friend.UI
{
    [DisallowMultipleComponent]
    public sealed class FriendListItemView : MonoBehaviour
    {
        [SerializeField] private TMP_Text nicknameText;
        [SerializeField] private Image onlineImage;
        [SerializeField] private Button selectButton;

        private FriendDto friend;
        private Action<FriendDto> onSelected;
        public long CharacterDbId { get; private set; }

        private void Awake()
        {
            if (nicknameText != null && onlineImage != null
                && selectButton != null) return;
            Debug.LogError("[Friend] Friend list row references are missing.", this);
            enabled = false;
        }

        private void OnEnable()
        {
            if (selectButton != null) selectButton.onClick.AddListener(Select);
        }

        private void OnDisable()
        {
            if (selectButton != null) selectButton.onClick.RemoveListener(Select);
        }

        public void Bind(FriendDto value, Action<FriendDto> selected)
        {
            friend = value;
            onSelected = selected;
            if (friend == null || nicknameText == null || onlineImage == null
                || selectButton == null) return;
            CharacterDbId = friend.characterDbId;
            nicknameText.text = string.IsNullOrWhiteSpace(friend.nickname)
                ? "角色 #" + CharacterDbId : friend.nickname;
            nicknameText.enableWordWrapping = false;
            nicknameText.enableAutoSizing = true;
            nicknameText.fontSizeMin = 9f;
            nicknameText.fontSizeMax = 12f;
            nicknameText.overflowMode = TextOverflowModes.Ellipsis;
            nicknameText.raycastTarget = false;
            onlineImage.color = friend.online
                ? new Color(0.14f, 0.68f, 0.35f)
                : new Color(0.48f, 0.52f, 0.55f);
            onlineImage.raycastTarget = false;
            selectButton.interactable = CharacterDbId > 0;
        }

        private void Select()
        {
            if (selectButton != null && selectButton.interactable && friend != null)
                onSelected?.Invoke(friend);
        }
    }
}
