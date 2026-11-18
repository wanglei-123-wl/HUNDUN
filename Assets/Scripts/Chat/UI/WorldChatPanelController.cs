using System;
using System.Collections.Generic;
using Game.Chat.Model;
using Game.Chat.Protocol;
using Game.Chat.Runtime;
using Game.UI.Notice;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEngine.EventSystems;
#endif

namespace Game.Chat.UI
{
    [DisallowMultipleComponent]
    public sealed class WorldChatPanelController : MonoBehaviour
    {
        private static WorldChatPanelController activePanel;

        public static bool IsChatInputFocused =>
            activePanel != null && activePanel.inputField != null
            && activePanel.inputField.isFocused;

        [SerializeField] private TMP_InputField inputField;
        [SerializeField] private Button sendButton;
        [SerializeField] private ScrollRect scrollRect;
        [SerializeField] private RectTransform content;
        [SerializeField] private PublicChatMessageItemView itemTemplate;

        private readonly Dictionary<string, PublicChatMessageItemView> displayed =
            new Dictionary<string, PublicChatMessageItemView>(StringComparer.Ordinal);
        private WorldChatSessionRuntime runtime;
        private long pendingSequence;
        private string pendingDraft;
        private WorldChatHistoryStatus lastHistoryStatus =
            WorldChatHistoryStatus.Disconnected;
#if UNITY_EDITOR
        private bool reportedInitialHit;
        private int clickFrame = -1;
        private int keyFrame = -1;
        private bool lastReportedFocus;
#endif

        private void Awake()
        {
            if (inputField == null || sendButton == null || scrollRect == null
                || content == null || itemTemplate == null
                || itemTemplate.transform.parent != content
                || scrollRect.content != content
                || itemTemplate.gameObject.activeSelf)
            {
                Debug.LogError("[WorldChat] Chat panel references or template are invalid.", this);
                enabled = false;
                return;
            }

        }

        private void OnEnable()
        {
            activePanel = this;
            sendButton.onClick.AddListener(Send);
            inputField.onValueChanged.AddListener(HandleDraftChanged);
            BindRuntime();
            RefreshControls();
        }

        private void OnDisable()
        {
            if (activePanel == this) activePanel = null;
            sendButton?.onClick.RemoveListener(Send);
            inputField?.onValueChanged.RemoveListener(HandleDraftChanged);
            UnbindRuntime();
        }

        private void Update()
        {
            if (runtime != WorldChatSessionRuntime.Instance)
                BindRuntime();
            RefreshControls();
#if UNITY_EDITOR
            if (!reportedInitialHit && EventSystem.current != null && inputField.isActiveAndEnabled)
            {
                reportedInitialHit = true;
                Canvas.ForceUpdateCanvases();
                RectTransform rect = (RectTransform)inputField.transform;
                Canvas canvas = inputField.GetComponentInParent<Canvas>();
                Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                    ? canvas.worldCamera : null;
                ReportInputRaycast("center",
                    RectTransformUtility.WorldToScreenPoint(camera, rect.TransformPoint(rect.rect.center)));
            }

            if (Input.GetMouseButtonDown(0) && RectTransformUtility.RectangleContainsScreenPoint(
                    (RectTransform)inputField.transform, Input.mousePosition))
            {
                ReportInputRaycast("click", Input.mousePosition);
                clickFrame = Time.frameCount;
            }
            else if (clickFrame >= 0 && Time.frameCount > clickFrame)
            {
                ReportInputState("after click");
                clickFrame = -1;
            }

            if (lastReportedFocus != inputField.isFocused)
            {
                lastReportedFocus = inputField.isFocused;
                ReportInputState("focus changed");
            }

            if (inputField.isFocused && (Input.inputString.Length > 0
                    || (Input.anyKeyDown && !Input.GetMouseButtonDown(0)
                        && !Input.GetMouseButtonDown(1) && !Input.GetMouseButtonDown(2))))
            {
                Debug.Log("[ChatInputProbe] key inputChars=" + Input.inputString.Length
                    + " compositionChars=" + Input.compositionString.Length, this);
                keyFrame = Time.frameCount;
            }
            else if (keyFrame >= 0 && Time.frameCount > keyFrame)
            {
                ReportInputState("after key");
                keyFrame = -1;
            }
#endif
        }

#if UNITY_EDITOR
        private void ReportInputState(string phase)
        {
            TMP_Text text = inputField.textComponent;
            Transform caret = inputField.textViewport.Find("Caret");
            CanvasRenderer caretRenderer = caret != null ? caret.GetComponent<CanvasRenderer>() : null;
            Debug.Log("[ChatInputProbe] " + phase
                + " focused=" + inputField.isFocused
                + " selected=" + (EventSystem.current?.currentSelectedGameObject == inputField.gameObject)
                + " draftLength=" + inputField.text.Length
                + " renderedLength=" + text.text.Length
                + " textActive=" + text.isActiveAndEnabled
                + " textAlpha=" + text.color.a
                + " textCulled=" + text.canvasRenderer.cull
                + " caretPresent=" + (caretRenderer != null)
                + " caretAlpha=" + (caretRenderer != null ? caretRenderer.GetAlpha() : -1f), this);
        }

        private void ReportInputRaycast(string phase, Vector2 screenPosition)
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null) return;

            var pointer = new PointerEventData(eventSystem) { position = screenPosition };
            var hits = new List<RaycastResult>();
            eventSystem.RaycastAll(pointer, hits);
            var names = new List<string>();
            for (int i = 0; i < Mathf.Min(hits.Count, 5); i++)
                names.Add(hits[i].gameObject.name + " (" + hits[i].gameObject.transform.parent?.name + ")");
            Debug.Log("[ChatInputProbe] " + phase + " position=" + screenPosition
                + " hits=" + string.Join(" > ", names)
                + " focused=" + inputField.isFocused, this);
        }
#endif

        private void BindRuntime()
        {
            WorldChatSessionRuntime active = WorldChatSessionRuntime.Instance;
            if (ReferenceEquals(runtime, active)) return;

            UnbindRuntime();
            ClearDisplayed();
            pendingSequence = 0;
            pendingDraft = null;
            lastHistoryStatus = WorldChatHistoryStatus.Disconnected;
            runtime = active;
            if (runtime == null) return;

            runtime.Timeline.Changed += RenderMessages;
            runtime.Sender.Changed += HandleSendStateChanged;
            RenderMessages();
        }

        private void UnbindRuntime()
        {
            if (runtime == null) return;
            runtime.Timeline.Changed -= RenderMessages;
            runtime.Sender.Changed -= HandleSendStateChanged;
            runtime = null;
        }

        private void RenderMessages()
        {
            if (runtime == null) return;
            IReadOnlyList<WorldChatMessage> messages =
                runtime.Timeline.MessagesNewestFirst;
            bool followLatest = displayed.Count == 0
                || scrollRect.verticalNormalizedPosition <= 0.05f;
            var currentIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (WorldChatMessage message in messages)
                currentIds.Add(message.messageId);

            var removedIds = new List<string>();
            foreach (KeyValuePair<string, PublicChatMessageItemView> row in displayed)
            {
                if (currentIds.Contains(row.Key)) continue;
                row.Value.gameObject.SetActive(false);
                Destroy(row.Value.gameObject);
                removedIds.Add(row.Key);
            }
            foreach (string id in removedIds) displayed.Remove(id);

            Canvas.ForceUpdateCanvases();
            for (int index = messages.Count - 1; index >= 0; index--)
            {
                WorldChatMessage message = messages[index];
                if (!displayed.TryGetValue(message.messageId,
                        out PublicChatMessageItemView item))
                {
                    item = Instantiate(itemTemplate, content, false);
                    item.gameObject.SetActive(true);
                    item.Bind(message.senderName, message.content,
                        message.senderCharacterDbId == runtime.Sender.CharacterDbId);
                    displayed.Add(message.messageId, item);
                }

                int desiredSibling = messages.Count - index;
                if (item.transform.GetSiblingIndex() != desiredSibling)
                    item.transform.SetSiblingIndex(desiredSibling);
            }

            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            if (followLatest) scrollRect.verticalNormalizedPosition = 0f;
            RefreshControls();
        }

        private void ClearDisplayed()
        {
            foreach (PublicChatMessageItemView item in displayed.Values)
            {
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
                return;
            }

            string draft = inputField.text;
            if (!runtime.TrySendWorldMessage(draft, out string errorCode))
            {
                GameNoticeService.Show(DescribeError(errorCode));
                RefreshControls();
                return;
            }

            pendingSequence = runtime.Sender.PendingSequence;
            pendingDraft = draft;
            inputField.SetTextWithoutNotify(string.Empty);
            inputField.ActivateInputField();
            RefreshControls();
        }

        private void HandleDraftChanged(string value)
        {
#if UNITY_EDITOR
            ReportInputState("draft changed");
#endif
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
                    && !result.success && string.IsNullOrEmpty(inputField.text))
                    inputField.SetTextWithoutNotify(pendingDraft);
                if (result != null && result.requestSequence == pendingSequence
                    && !result.success)
                    GameNoticeService.Show(DescribeError(result.errorCode));
                pendingSequence = 0;
                pendingDraft = null;
            }
            RefreshControls();
        }

        private void RefreshControls()
        {
            ChatSendSession sender = runtime == null ? null : runtime.Sender;
            double remaining = sender == null ? 0
                : sender.RemainingCooldown(Time.realtimeSinceStartupAsDouble);
            sendButton.interactable = sender != null && sender.IsInWorld
                && !sender.DeliveryUnknown && sender.PendingSequence == 0
                && remaining <= 0;

            WorldChatHistoryStatus historyStatus = runtime == null
                ? WorldChatHistoryStatus.Disconnected
                : runtime.Timeline.Status;
            if (historyStatus != lastHistoryStatus)
            {
                lastHistoryStatus = historyStatus;
                if (historyStatus == WorldChatHistoryStatus.Incomplete)
                    GameNoticeService.Show("聊天记录未完整同步");
            }
        }

        private static string DescribeError(string code)
        {
            switch (code)
            {
                case "player_not_in_world": return "尚未进入世界";
                case "chat_send_outcome_unknown":
                case "chat_send_result_timeout": return "发送状态不明，请重新连接";
                case "chat_send_pending": return "发送中...";
                case "content_empty": return "请输入聊天内容";
                case "content_too_long": return "聊天内容过长";
                case "content_rejected": return "聊天内容未通过审核";
                case "chat_history_busy":
                case "chat_transport_unavailable": return "聊天暂不可用";
                case "rate_limited": return "发送冷却中";
                default:
                    return string.IsNullOrWhiteSpace(code)
                        ? "发送失败，请稍后再试" : "发送失败：" + code;
            }
        }
    }
}
