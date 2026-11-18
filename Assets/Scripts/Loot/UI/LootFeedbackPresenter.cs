using System;
using Game.UI.Notice;
using Game.Items;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class LootFeedbackPresenter : MonoBehaviour
{
    [SerializeField] private GameObject feedbackRoot;
    [SerializeField] private TMP_Text feedbackText;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private ItemDefinitionTable itemConfigTable;

    public event Action<string> FeedbackShown;

    private void Awake()
    {
        SetVisible(false);
    }

    public void ShowPickupSuccess(LootPickupResultMessageDto message)
    {
        if (message == null
            || !string.Equals(
                message.status,
                LootProtocolConstants.PickupStatuses.Succeeded,
                StringComparison.Ordinal))
        {
            return;
        }

        ItemDefinition config = itemConfigTable == null
            ? null
            : itemConfigTable.GetOrDefault(message.itemId);

        string displayName = config != null
            && !string.IsNullOrWhiteSpace(config.DisplayName)
                ? config.DisplayName
                : message.itemId;

        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = "未知物品";
        }

        int count = Mathf.Max(1, message.grantedCount);
        ShowMessage("获得 " + displayName + " ×" + count);

        if (audioSource != null && config != null && config.PickupSound != null)
        {
            audioSource.PlayOneShot(config.PickupSound);
        }
    }

    public void ShowError(string message)
    {
        ShowMessage(message);
    }

    public void ShowMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        GameNoticeService.Show(message);
        FeedbackShown?.Invoke(message);
    }

    public void Hide()
    {
        GameNoticeService.Hide();
        SetVisible(false);
    }

    private void SetVisible(bool visible)
    {
        if (feedbackRoot != null)
        {
            feedbackRoot.SetActive(visible);
        }
    }
}
