using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Friend.UI
{
    [DisallowMultipleComponent]
    public sealed class FriendRequestItemView : MonoBehaviour
    {
        [SerializeField] private TMP_Text nicknameText;
        [SerializeField] private Button acceptButton;
        [SerializeField] private Button rejectButton;

        private Action<string, bool> handle;
        private string requestId;
        private string nickname;
        private long expireAt;

        public string RequestId => requestId;
        public bool IsExpired(long nowMilliseconds) =>
            expireAt <= 0 || expireAt <= nowMilliseconds;

        private void Awake()
        {
            if (nicknameText != null && acceptButton != null && rejectButton != null)
                return;
            Debug.LogError("[Friend] Received request prefab references are missing.", this);
            enabled = false;
        }

        private void OnEnable()
        {
            if (acceptButton != null) acceptButton.onClick.AddListener(Accept);
            if (rejectButton != null) rejectButton.onClick.AddListener(Reject);
        }

        private void OnDisable()
        {
            if (acceptButton != null) acceptButton.onClick.RemoveListener(Accept);
            if (rejectButton != null) rejectButton.onClick.RemoveListener(Reject);
        }

        public void Bind(string id, string fromNickname, long expiration,
            Action<string, bool> onHandle)
        {
            requestId = id;
            nickname = fromNickname;
            expireAt = expiration;
            handle = onHandle;
            nicknameText.enableWordWrapping = false;
            nicknameText.enableAutoSizing = true;
            nicknameText.fontSizeMin = 12f;
            nicknameText.overflowMode = TextOverflowModes.Ellipsis;
        }

        public void UpdateState(bool busy, bool unavailable, long nowMilliseconds)
        {
            bool expired = IsExpired(nowMilliseconds);
            string name = string.IsNullOrWhiteSpace(nickname)
                ? "未知玩家" : nickname;
            nicknameText.text = name + (expired ? " (已过期)"
                : unavailable ? " (无法处理)" : string.Empty);
            bool canHandle = !busy && !expired && !unavailable
                && !string.IsNullOrWhiteSpace(requestId);
            acceptButton.interactable = canHandle;
            rejectButton.interactable = canHandle;
        }

        private void Accept()
        {
            if (acceptButton.interactable) handle?.Invoke(requestId, true);
        }

        private void Reject()
        {
            if (rejectButton.interactable) handle?.Invoke(requestId, false);
        }
    }
}
