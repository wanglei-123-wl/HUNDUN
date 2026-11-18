using System;
using System.Collections.Generic;
using Game.Chat.Model;
using Game.Chat.Protocol;
using Game.Chat.Runtime;
using Game.Friend.Protocol;
using Game.Friend.UI;
using Game.UI.Notice;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Chat.UI
{
    [DisallowMultipleComponent]
    public sealed class PrivateChatPanelController : MonoBehaviour
    {
        private static PrivateChatPanelController activePanel;

        public static bool IsChatInputFocused =>
            activePanel != null && activePanel.inputField != null
            && activePanel.inputField.isFocused;

        [SerializeField] private FriendListPanelController friendList;
        [SerializeField] private TMP_InputField inputField;
        [SerializeField] private Button sendButton;
        [SerializeField] private ScrollRect scrollRect;
        [SerializeField] private RectTransform content;
        [SerializeField] private PublicChatMessageItemView itemTemplate;

        private readonly Dictionary<string, PublicChatMessageItemView> displayed =
            new Dictionary<string, PublicChatMessageItemView>(
                StringComparer.Ordinal);
        private WorldChatSessionRuntime runtime;
        private long selectedPartnerId;
        private string selectedPartnerName;
        private long pendingSequence;
        private long pendingTargetId;
        private string pendingDraft;

        private void Awake()
        {
            if (friendList == null || inputField == null || sendButton == null
                || scrollRect == null || content == null || itemTemplate == null
                || scrollRect.content != content)
            {
                Debug.LogError(
                    "[Chat] Private chat panel references are invalid.", this);
                enabled = false;
                return;
            }

        }

        private void OnEnable()
        {
            activePanel = this;
            sendButton.onClick.AddListener(Send);
            inputField.onValueChanged.AddListener(HandleDraftChanged);
            friendList.FriendSelected += HandleFriendSelected;
            BindRuntime();
            RefreshControls();
        }

        private void OnDisable()
        {
            if (activePanel == this) activePanel = null;
            sendButton?.onClick.RemoveListener(Send);
            inputField?.onValueChanged.RemoveListener(HandleDraftChanged);
            if (friendList != null)
                friendList.FriendSelected -= HandleFriendSelected;
            UnbindRuntime();
        }

        private void Update()
        {
            if (runtime != WorldChatSessionRuntime.Instance)
                BindRuntime();
            RefreshControls();
        }

        private void BindRuntime()
        {
            WorldChatSessionRuntime active = WorldChatSessionRuntime.Instance;
            if (ReferenceEquals(runtime, active)) return;

            UnbindRuntime();
            ClearDisplayed();
            selectedPartnerId = 0;
            selectedPartnerName = null;
            pendingSequence = 0;
            pendingTargetId = 0;
            pendingDraft = null;
            runtime = active;
            if (runtime == null)
            {
                RefreshControls();
                return;
            }

            runtime.PrivateConversations.Changed += RenderMessages;
            runtime.Sender.Changed += HandleSendStateChanged;
            RenderMessages();
        }

        private void UnbindRuntime()
        {
            if (runtime == null) return;
            runtime.PrivateConversations.Changed -= RenderMessages;
            runtime.Sender.Changed -= HandleSendStateChanged;
            runtime = null;
        }

        private void HandleFriendSelected(FriendDto friend)
        {
            if (friend == null || friend.characterDbId <= 0) return;
            if (selectedPartnerId == friend.characterDbId
                && selectedPartnerName == friend.nickname)
                return;

            selectedPartnerId = friend.characterDbId;
            selectedPartnerName = string.IsNullOrWhiteSpace(friend.nickname)
                ? "角色 #" + selectedPartnerId : friend.nickname;
            ClearDisplayed();
            RenderMessages();
            inputField.ActivateInputField();
            RefreshControls();
        }

        private void RenderMessages()
        {
            if (runtime == null) return;

            IReadOnlyList<WorldChatMessage> messages =
                selectedPartnerId > 0
                    ? runtime.PrivateConversations.GetMessages(selectedPartnerId)
                    : Array.Empty<WorldChatMessage>();
            bool followLatest = displayed.Count == 0
                || scrollRect.verticalNormalizedPosition <= 0.05f;
            var currentIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (WorldChatMessage message in messages)
                if (message != null && !string.IsNullOrEmpty(message.messageId))
                    currentIds.Add(message.messageId);

            var removedIds = new List<string>();
            foreach (KeyValuePair<string, PublicChatMessageItemView> row in displayed)
            {
                if (currentIds.Contains(row.Key)) continue;
                if (row.Value != null)
                {
                    row.Value.gameObject.SetActive(false);
                    Destroy(row.Value.gameObject);
                }
                removedIds.Add(row.Key);
            }
            foreach (string id in removedIds) displayed.Remove(id);

            Canvas.ForceUpdateCanvases();
            for (int index = 0; index < messages.Count; index++)
            {
                WorldChatMessage message = messages[index];
                if (message == null || string.IsNullOrEmpty(message.messageId))
                    continue;

                if (!displayed.TryGetValue(message.messageId,
                        out PublicChatMessageItemView item))
                {
                    item = Instantiate(itemTemplate, content, false);
                    item.gameObject.SetActive(true);
                    item.Bind(message.senderName, message.content,
                        message.senderCharacterDbId == runtime.Sender.CharacterDbId);
                    displayed.Add(message.messageId, item);
                }

                if (item.transform.GetSiblingIndex() != index)
                    item.transform.SetSiblingIndex(index);
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            if (followLatest) scrollRect.verticalNormalizedPosition = 0f;
            RefreshControls();
        }

        private void ClearDisplayed()
        {
            foreach (PublicChatMessageItemView item in displayed.Values)
            {
                if (item == null) continue;
                item.gameObject.SetActive(false);
                Destroy(item.gameObject);
            }
            displayed.Clear();
        }

        private void Send()
        {
            if (runtime == null)
            {
                GameNoticeService.Show("聊天暂不可用");
                RefreshControls();
                return;
            }
            if (selectedPartnerId <= 0)
            {
                GameNoticeService.Show("请先选择好友");
                RefreshControls();
                return;
            }

            string draft = inputField.text;
            if (!runtime.TrySendPrivateMessage(selectedPartnerId, draft,
                    out string errorCode))
            {
                if (!string.Equals(errorCode, "target_offline",
                        StringComparison.Ordinal))
                    GameNoticeService.Show(DescribeError(errorCode));
                RefreshControls();
                return;
            }

            pendingSequence = runtime.Sender.PendingSequence;
            pendingTargetId = selectedPartnerId;
            pendingDraft = draft;
            inputField.SetTextWithoutNotify(string.Empty);
            inputField.ActivateInputField();
            RefreshControls();
        }

        private void HandleDraftChanged(string value)
        {
            RefreshControls();
        }

        private void HandleSendStateChanged()
        {
            if (runtime == null) return;
            ChatSendSession sender = runtime.Sender;
            if (pendingSequence > 0 && sender.PendingSequence == 0)
            {
                WorldChatSendResult result = sender.LastResult;
                if (result != null && result.requestSequence == pendingSequence
                    && !result.success && pendingTargetId == selectedPartnerId
                    && string.IsNullOrEmpty(inputField.text))
                    inputField.SetTextWithoutNotify(pendingDraft);
                if (result != null && result.requestSequence == pendingSequence
                    && !result.success
                    && !string.Equals(result.errorCode, "target_offline",
                        StringComparison.Ordinal))
                    GameNoticeService.Show(DescribeError(result.errorCode));
                pendingSequence = 0;
                pendingTargetId = 0;
                pendingDraft = null;
            }
            RefreshControls();
        }

        private void RefreshControls()
        {
            ChatSendSession sender = runtime == null ? null : runtime.Sender;
            double remaining = sender == null ? 0
                : sender.RemainingCooldown("private",
                    Time.realtimeSinceStartupAsDouble);

            sendButton.interactable = sender != null && sender.IsInWorld
                && !sender.DeliveryUnknown
                && sender.PendingSequence == 0 && remaining <= 0
                && remaining <= 0;
        }

        private static string DescribeError(string code)
        {
            switch (code)
            {
                case "player_not_in_world": return "尚未进入世界";
                case "chat_send_outcome_unknown":
                case "chat_send_result_timeout": return "发送状态不明，请重新连接";
                case "chat_send_pending": return "发送中...";
                case "target_required": return "请先选择好友";
                case "cannot_chat_self": return "不能给自己发送私聊";
                case "rate_limited": return "私聊发送冷却中";
                case "content_empty": return "请输入聊天内容";
                case "content_too_long": return "聊天内容过长";
                case "content_rejected": return "聊天内容未通过审核";
                case "target_not_found": return "目标角色不存在或已删除";
                case "target_offline": return "目标当前不在线";
                case "chat_blocked": return "当前无法向对方发送私聊";
                case "private_chat_store_unavailable":
                    return "服务器暂无法保存私聊";
                case "private_chat_local_store_unavailable":
                    return "本地私聊记录不可用";
                case "chat_history_busy":
                case "chat_transport_unavailable":
                case "invalid_channel":
                    return "聊天暂不可用";
                default:
                    return string.IsNullOrWhiteSpace(code)
                        ? "发送失败，请稍后再试" : "发送失败：" + code;
            }
        }
    }
}
