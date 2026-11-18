using System;
using Game.Friend.Protocol;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Friend.UI
{
    [DisallowMultipleComponent]
    public sealed class FriendSearchResultItemView : MonoBehaviour
    {
        [SerializeField] private TMP_Text nicknameText;
        [SerializeField] private Button addButton;

        private FriendSearchPlayer player;
        private Action<FriendSearchPlayer> onAdd;

        public long CharacterDbId => player == null ? 0 : player.characterDbId;

        private void Awake()
        {
            if (nicknameText != null && addButton != null) return;
            Debug.LogError("[Friend] Search result prefab has missing references.", this);
            enabled = false;
        }

        private void OnEnable()
        {
            if (addButton != null) addButton.onClick.AddListener(Add);
        }

        private void OnDisable()
        {
            if (addButton != null) addButton.onClick.RemoveListener(Add);
        }

        public void Bind(FriendSearchPlayer result, Action<FriendSearchPlayer> add)
        {
            player = result;
            onAdd = add;
            nicknameText.enableWordWrapping = false;
            nicknameText.enableAutoSizing = true;
            nicknameText.fontSizeMin = 12;
            nicknameText.overflowMode = TextOverflowModes.Ellipsis;
            UpdateState(false, false, false, false);
        }

        public void UpdateState(bool isFriend, bool isRequested,
            bool unavailable, bool busy)
        {
            if (player == null || nicknameText == null || addButton == null) return;
            string name = string.IsNullOrWhiteSpace(player.nickname)
                ? "角色 #" + player.characterDbId : player.nickname;
            nicknameText.text = name + (isFriend ? " (好友)"
                : isRequested ? " (已申请)"
                : unavailable ? " (不可申请)"
                : !player.online ? " (离线)" : string.Empty);
            addButton.interactable = player.characterDbId > 0
                && !isFriend && !isRequested && !unavailable && !busy;
        }

        private void Add()
        {
            if (addButton.interactable && player != null)
                onAdd?.Invoke(player);
        }
    }
}
