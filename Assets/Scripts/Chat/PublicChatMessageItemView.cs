using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Chat
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(LayoutElement))]
    public sealed class PublicChatMessageItemView : MonoBehaviour
    {
        [Header("消息组件")]
        [SerializeField] private TMP_Text nicknameText;
        [SerializeField] private TMP_Text messageText;
        [SerializeField] private RectTransform bubbleRect;
        [SerializeField] private Image bubbleImage;
        [SerializeField] private LayoutElement rowLayout;

        [Header("气泡外观")]
        [SerializeField] private float minBubbleWidth = 52f;
        [SerializeField] private float maxBubbleWidth = 216f;
        [SerializeField] private Color otherColor = new Color(0.1f, 0.31f, 0.34f, 0.95f);
        [SerializeField] private Color ownColor = new Color(0.2f, 0.42f, 0.31f, 0.95f);

        private const float HorizontalInset = 6f;
        private const float BubbleBottom = 7f;
        private const float TextHorizontalPadding = 20f;
        private const float TextVerticalPadding = 12f;
        private const float MinBubbleHeight = 32f;

        public void Bind(string nickname, string message, bool isOwnMessage)
        {
            if (nicknameText == null || messageText == null || bubbleRect == null
                || bubbleImage == null || rowLayout == null)
                throw new InvalidOperationException("Public chat message item references are not configured.");

            RectTransform content = transform.parent as RectTransform;
            if (content == null)
                throw new InvalidOperationException("Public chat message item must be a child of Content.");

            nicknameText.text = nickname ?? string.Empty;
            messageText.text = message ?? string.Empty;

            float availableWidth = content.rect.width - HorizontalInset * 2f;
            if (availableWidth <= TextHorizontalPadding)
                throw new InvalidOperationException("Public chat Content is too narrow for a message bubble.");

            float widthLimit = Mathf.Min(maxBubbleWidth, availableWidth);
            float minimumWidth = Mathf.Min(minBubbleWidth, widthLimit);
            float unwrappedWidth = messageText.GetPreferredValues(messageText.text).x;
            float bubbleWidth = Mathf.Clamp(
                Mathf.Ceil(unwrappedWidth + TextHorizontalPadding),
                minimumWidth,
                widthLimit);
            float textWidth = bubbleWidth - TextHorizontalPadding;
            float textHeight = messageText.GetPreferredValues(
                messageText.text, textWidth, Mathf.Infinity).y;
            float bubbleHeight = Mathf.Max(
                MinBubbleHeight,
                Mathf.Ceil(textHeight + TextVerticalPadding));
            float bubbleTop = -bubbleRect.anchoredPosition.y;

            bubbleRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, bubbleWidth);
            bubbleRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, bubbleHeight);
            rowLayout.preferredHeight = bubbleTop + bubbleHeight + BubbleBottom;

            float edge = isOwnMessage ? 1f : 0f;
            float offset = isOwnMessage ? -HorizontalInset : HorizontalInset;
            RectTransform nicknameRect = nicknameText.rectTransform;
            nicknameRect.anchorMin = new Vector2(edge, 1f);
            nicknameRect.anchorMax = new Vector2(edge, 1f);
            nicknameRect.pivot = new Vector2(edge, 1f);
            nicknameRect.anchoredPosition = new Vector2(offset, -3f);
            nicknameText.alignment = isOwnMessage
                ? TextAlignmentOptions.Right
                : TextAlignmentOptions.Left;

            bubbleRect.anchorMin = new Vector2(edge, 1f);
            bubbleRect.anchorMax = new Vector2(edge, 1f);
            bubbleRect.pivot = new Vector2(edge, 1f);
            bubbleRect.anchoredPosition = new Vector2(offset, -bubbleTop);
            bubbleImage.color = isOwnMessage ? ownColor : otherColor;

            LayoutRebuilder.MarkLayoutForRebuild(content);
        }
    }
}
