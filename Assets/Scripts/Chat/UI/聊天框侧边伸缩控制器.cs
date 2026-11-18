using System.Collections;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace Game.Chat.UI
{
    [RequireComponent(typeof(RectTransform))]
    [DisallowMultipleComponent]
    public sealed class 聊天框侧边伸缩控制器 : MonoBehaviour
    {
        [SerializeField, FormerlySerializedAs("chatContent")]
        private RectTransform 聊天内容节点;

        [SerializeField] private RectTransform 私聊节点;

        [SerializeField, FormerlySerializedAs("toggleButton")]
        private Button 回收展开按钮;

        [SerializeField, Min(0f), FormerlySerializedAs("retractDistance")]
        private float 向左收回距离 = 320f;

        [SerializeField, Min(0f), FormerlySerializedAs("moveDuration")]
        private float 移动时间秒数 = 0.25f;

        private RectTransform buttonRect;
        private Vector2 expandedContentPosition;
        private Vector2 expandedPrivatePosition;
        private Vector2 expandedButtonPosition;
        private bool retracted;
        private Coroutine movement;

        private void Awake()
        {
            if (聊天内容节点 == null)
            {
                Transform contentNode = transform.Find("聊天框/公聊/聊天位置总控");
                if (contentNode == null)
                    contentNode = transform.Find("聊天框/聊天位置总控");
                if (contentNode == null)
                    contentNode = transform.Find("聊天位置总控");
                聊天内容节点 = contentNode as RectTransform;
            }

            if (私聊节点 == null)
            {
                Transform privateNode = transform.Find("聊天框/私聊");
                if (privateNode == null)
                    privateNode = transform.Find("私聊");
                私聊节点 = privateNode as RectTransform;
            }

            if (回收展开按钮 == null)
            {
                Transform buttonNode = transform.Find("聊天框/回收展开");
                if (buttonNode == null)
                    buttonNode = transform.Find("回收展开");
                if (buttonNode != null)
                    回收展开按钮 = buttonNode.GetComponent<Button>();
            }

            if (聊天内容节点 == null || 回收展开按钮 == null)
            {
                Debug.LogError(
                    "[WorldChat] 未找到聊天内容节点或回收展开按钮，请检查聊天框层级和 Button 组件。",
                    this);
                enabled = false;
                return;
            }

            buttonRect = 回收展开按钮.transform as RectTransform;
            if (buttonRect == null)
            {
                Debug.LogError("[WorldChat] 回收展开按钮必须挂在 RectTransform 对象上。", this);
                enabled = false;
                return;
            }

            expandedContentPosition = 聊天内容节点.anchoredPosition;
            if (私聊节点 != null)
                expandedPrivatePosition = 私聊节点.anchoredPosition;
            expandedButtonPosition = buttonRect.anchoredPosition;
        }

        private void OnEnable()
        {
            if (buttonRect != null)
                回收展开按钮.onClick.AddListener(Toggle);
        }

        private void OnDisable()
        {
            if (回收展开按钮 != null)
                回收展开按钮.onClick.RemoveListener(Toggle);
            if (movement != null)
            {
                StopCoroutine(movement);
                movement = null;
            }
        }

        private void Toggle()
        {
            retracted = !retracted;
            Vector2 offset = Vector2.left * (retracted ? 向左收回距离 : 0f);
            Vector2 contentDestination = expandedContentPosition + offset;
            Vector2 privateDestination = expandedPrivatePosition + offset;
            Vector2 buttonDestination = expandedButtonPosition + offset;

            if (movement != null)
                StopCoroutine(movement);

            if (移动时间秒数 <= 0f)
            {
                聊天内容节点.anchoredPosition = contentDestination;
                if (私聊节点 != null)
                    私聊节点.anchoredPosition = privateDestination;
                buttonRect.anchoredPosition = buttonDestination;
                movement = null;
            }
            else
            {
                movement = StartCoroutine(MoveTo(
                    contentDestination, privateDestination, buttonDestination));
            }
        }

        private IEnumerator MoveTo(
            Vector2 contentDestination,
            Vector2 privateDestination,
            Vector2 buttonDestination)
        {
            Vector2 contentStart = 聊天内容节点.anchoredPosition;
            Vector2 privateStart = 私聊节点 != null
                ? 私聊节点.anchoredPosition : expandedPrivatePosition;
            Vector2 buttonStart = buttonRect.anchoredPosition;
            float elapsed = 0f;

            while (elapsed < 移动时间秒数)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.SmoothStep(
                    0f, 1f, Mathf.Clamp01(elapsed / 移动时间秒数));
                聊天内容节点.anchoredPosition = Vector2.Lerp(
                    contentStart, contentDestination, progress);
                if (私聊节点 != null)
                    私聊节点.anchoredPosition = Vector2.Lerp(
                        privateStart, privateDestination, progress);
                buttonRect.anchoredPosition = Vector2.Lerp(
                    buttonStart, buttonDestination, progress);
                yield return null;
            }

            聊天内容节点.anchoredPosition = contentDestination;
            if (私聊节点 != null)
                私聊节点.anchoredPosition = privateDestination;
            buttonRect.anchoredPosition = buttonDestination;
            movement = null;
        }
    }
}
