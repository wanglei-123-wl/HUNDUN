using System.Collections;
using TMPro;
using UnityEngine;

namespace Game.UI.Notice
{
    [DisallowMultipleComponent]
    public sealed class UnifiedNoticeController : MonoBehaviour
    {
        [SerializeField] private GameObject displayRoot;
        [SerializeField] private TMP_Text noticeText;
        [SerializeField, Min(0.1f)] private float visibleDuration = 2f;

        private Coroutine hideCoroutine;
        private bool initialized;

        private void Awake()
        {
            if (displayRoot == null)
            {
                displayRoot = gameObject;
            }

            if (noticeText == null)
            {
                Transform textTransform = transform.Find("Text (TMP)");
                if (textTransform != null)
                {
                    noticeText = textTransform.GetComponent<TMP_Text>();
                }
            }

            if (noticeText == null)
            {
                noticeText = GetComponentInChildren<TMP_Text>(true);
            }

            if (noticeText == null)
            {
                Debug.LogError(
                    "[UI] Unified notice needs a child TMP_Text reference.", this);
                enabled = false;
                return;
            }

            noticeText.raycastTarget = false;
            noticeText.text = string.Empty;
            initialized = true;
            SetVisible(false);
        }

        public void Show(string message)
        {
            if (!initialized || string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            noticeText.text = message.Trim();
            SetVisible(true);

            if (hideCoroutine != null)
            {
                StopCoroutine(hideCoroutine);
                hideCoroutine = null;
            }

            if (isActiveAndEnabled && visibleDuration > 0f)
            {
                hideCoroutine = StartCoroutine(HideAfterDelay());
            }
        }

        public void Hide()
        {
            if (hideCoroutine != null)
            {
                StopCoroutine(hideCoroutine);
                hideCoroutine = null;
            }

            if (noticeText != null)
            {
                noticeText.text = string.Empty;
            }

            SetVisible(false);
        }

        private IEnumerator HideAfterDelay()
        {
            yield return new WaitForSecondsRealtime(visibleDuration);
            hideCoroutine = null;
            Hide();
        }

        private void SetVisible(bool visible)
        {
            if (displayRoot != null)
            {
                displayRoot.SetActive(visible);
            }
        }
    }
}
