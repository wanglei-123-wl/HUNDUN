using System;
using System.Globalization;
using Game.Cultivation.Presentation;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.CharacterPanel.Presentation
{
    [DisallowMultipleComponent]
    public sealed class ProgressionHudPresenter : MonoBehaviour
    {
        [SerializeField] private GameSession session;
        [SerializeField] private TMP_Text realmText;
        [SerializeField] private TMP_Text experienceText;
        [SerializeField] private Image experienceFill;
        [SerializeField] private TMP_Text mpText;
        [SerializeField] private Image mpFill;
        [SerializeField] private TMP_Text nicknameText;

        private bool subscribed;

        private void OnEnable()
        {
            ResolveReferences();
            Subscribe();
            Refresh();
            if (session != null && session.LocalProgressionSnapshot == null)
                session.RequestProgressionSnapshot();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        private void Subscribe()
        {
            if (subscribed || session == null) return;
            session.LocalProgressionSnapshotChanged += HandleProgressionSnapshotChanged;
            session.LocalManaChanged += RenderMana;
            session.LocalWorldEntered += HandleWorldEntered;
            if (NetworkManager.Instance != null)
                NetworkManager.Instance.OnDisconnected += HandleDisconnected;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed) return;
            if (session != null)
            {
                session.LocalProgressionSnapshotChanged -= HandleProgressionSnapshotChanged;
                session.LocalManaChanged -= RenderMana;
                session.LocalWorldEntered -= HandleWorldEntered;
            }
            if (NetworkManager.Instance != null)
                NetworkManager.Instance.OnDisconnected -= HandleDisconnected;
            subscribed = false;
        }

        private void HandleProgressionSnapshotChanged(ProgressionSnapshotResult result)
        {
            Render(result);
        }

        private void HandleWorldEntered(long epoch, long characterDbId)
        {
            Refresh();
        }

        private void HandleDisconnected(string reason)
        {
            Clear();
        }

        private void Refresh()
        {
            RenderMana();
            Render(session != null ? session.LocalProgressionSnapshot : null);
        }

        private void Render(ProgressionSnapshotResult result)
        {
            bool valid = result != null && result.success
                && session != null
                && result.characterDbId == session.LocalCharacterDbId;
            CultivationProgressDisplay cultivation =
                CultivationProgressDisplay.FromSnapshot(result,
                    session != null ? session.LocalCharacterDbId : 0,
                    session != null && session.IsWorldReady);

            if (nicknameText != null)
                nicknameText.text = session != null && session.LocalCharacterDbId > 0
                    && !string.IsNullOrWhiteSpace(session.LocalNickname)
                    ? session.LocalNickname : "--";

            if (realmText != null)
                realmText.text = valid && !string.IsNullOrWhiteSpace(result.jingjieName)
                    ? result.jingjieName : "--";

            if (experienceText != null)
                experienceText.text = cultivation.Progress;

            if (experienceFill != null)
                experienceFill.fillAmount = cultivation.FillAmount;

            if (experienceText != null)
            {
                RectTransform rect = experienceText.rectTransform;
                Color color = experienceText.color;
                Debug.Log("[ProgressionHUD][Render]"
                    + " valid=" + valid
                    + " characterDbId=" + (result == null ? 0 : result.characterDbId)
                    + " localCharacterDbId="
                    + (session == null ? 0 : session.LocalCharacterDbId)
                    + " jingjieExp=" + (result == null ? -1 : result.jingjieExp)
                    + " requiredJingjieExp="
                    + (result == null ? -1 : result.requiredJingjieExp)
                    + " currentMp=" + (result == null ? -1 : result.currentMp)
                    + " maxMp=" + (result == null ? -1 : result.maxMp)
                    + " assignedText=\"" + experienceText.text + "\""
                    + " assignedMpText=\""
                    + (mpText == null ? "<null>" : mpText.text) + "\""
                    + " activeSelf=" + experienceText.gameObject.activeSelf
                    + " activeInHierarchy=" + experienceText.gameObject.activeInHierarchy
                    + " enabled=" + experienceText.enabled
                    + " alpha=" + color.a
                    + " rectWidth=" + rect.rect.width
                    + " rectHeight=" + rect.rect.height
                    + " scale=" + rect.localScale);
            }
            else
            {
                Debug.LogWarning("[ProgressionHUD][Render] experienceText reference is null."
                    + " valid=" + valid
                    + " characterDbId=" + (result == null ? 0 : result.characterDbId)
                    + " localCharacterDbId="
                    + (session == null ? 0 : session.LocalCharacterDbId));
            }
        }

        private void RenderMana()
        {
            int currentMp = 0, maxMp = 0;
            bool available = session != null && session.TryGetLocalMana(out currentMp, out maxMp);
            if (mpText != null)
                mpText.text = available ? currentMp.ToString(CultureInfo.InvariantCulture) + "/"
                    + maxMp.ToString(CultureInfo.InvariantCulture) : "--";
            if (mpFill != null)
                mpFill.fillAmount = available && maxMp > 0 ? Mathf.Clamp01(currentMp / (float)maxMp) : 0f;
        }

        private void Clear()
        {
            if (nicknameText != null) nicknameText.text = "--";
            if (realmText != null) realmText.text = "--";
            if (experienceText != null) experienceText.text = "--/--";
            if (experienceFill != null) experienceFill.fillAmount = 0f;
            if (mpText != null) mpText.text = "--";
            if (mpFill != null) mpFill.fillAmount = 0f;
        }

        private void ResolveReferences()
        {
            if (session == null)
                session = FindObjectOfType<GameSession>();
            if (realmText == null || experienceText == null)
            {
                foreach (TMP_Text text in GetComponentsInChildren<TMP_Text>(true))
                {
                    if (text == null) continue;
                    if (realmText == null && text.gameObject.name == "境界显示框")
                        realmText = text;
                    if (experienceText == null
                        && (text.gameObject.name == "修为数值"
                            || text.gameObject.name == "经验数值"))
                        experienceText = text;
                }
            }
            if (experienceFill == null)
            {
                Transform root = FindChildTransform("修为条")
                    ?? FindChildTransform("经验条");
                if (root != null)
                {
                    foreach (Image image in root.GetComponentsInChildren<Image>(true))
                    {
                        if (image != null && image.type == Image.Type.Filled)
                        {
                            experienceFill = image;
                            break;
                        }
                    }
                }
            }
            if (mpText == null || mpFill == null)
            {
                Transform root = FindChildTransform("玩家蓝条");
                if (root != null)
                {
                    TMP_Text[] texts = root.GetComponentsInChildren<TMP_Text>(true);
                    if (mpText == null && texts.Length > 0)
                        mpText = texts[texts.Length - 1];

                    Image[] images = root.GetComponentsInChildren<Image>(true);
                    if (mpFill == null)
                    {
                        foreach (Image image in images)
                        {
                            if (image != null && image.type == Image.Type.Filled)
                            {
                                mpFill = image;
                                break;
                            }
                        }
                    }
                }
            }
        }

        private Transform FindChildTransform(string objectName)
        {
            foreach (Transform child in GetComponentsInChildren<Transform>(true))
            {
                if (child != null && string.Equals(child.name, objectName,
                    StringComparison.Ordinal))
                    return child;
            }
            return null;
        }
    }
}
