using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class DeleteCharacterPanelUI : MonoBehaviour
{
    [Header("Controller")]
    [SerializeField] private CharacterSelectPanelUI owner;

    [Header("Panel")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TMP_Text tipText;

    [Header("Buttons")]
    [SerializeField] private Button openButton;
    [SerializeField] private Button confirmButton;
    [SerializeField] private Button backButton;

    [Header("Settings")]
    [SerializeField] private float timeoutSeconds = 3f;

    private CharacterData pendingCharacter;
    private Coroutine timeoutCoroutine;
    private bool waitingForServer;
    private int requestVersion;

    private void Awake()
    {
        if (panelRoot != null)
        {
            panelRoot.SetActive(false);
        }
    }

    private void OnEnable()
    {
        if (openButton != null)
        {
            openButton.onClick.AddListener(ShowForCurrentCharacter);
        }

        if (confirmButton != null)
        {
            confirmButton.onClick.AddListener(ConfirmDelete);
        }

        if (backButton != null)
        {
            backButton.onClick.AddListener(Hide);
        }
    }

    private void OnDisable()
    {
        if (openButton != null)
        {
            openButton.onClick.RemoveListener(ShowForCurrentCharacter);
        }

        if (confirmButton != null)
        {
            confirmButton.onClick.RemoveListener(ConfirmDelete);
        }

        if (backButton != null)
        {
            backButton.onClick.RemoveListener(Hide);
        }

        CancelPendingRequest();
    }

    public void ShowForCurrentCharacter()
    {
        if (owner == null)
        {
            return;
        }

        CharacterData character = owner.CurrentCharacter;
        if (character == null || string.IsNullOrWhiteSpace(character.characterId))
        {
            ShowPanel();
            SetTip("删除失败");
            SetConfirmEnabled(false);
            return;
        }

        CancelPendingRequest();

        pendingCharacter = character;

        ShowPanel();
        SetConfirmEnabled(true);
        SetTip("sure " + GetCharacterName(character) + "?");
    }

    public void ConfirmDelete()
    {
        if (waitingForServer || pendingCharacter == null || owner == null)
        {
            return;
        }

        waitingForServer = true;
        SetConfirmEnabled(false);
        SetTip("waiting");

        int currentVersion = ++requestVersion;

        timeoutCoroutine = StartCoroutine(
            WaitForDeleteTimeout(currentVersion));

        owner.DeleteCharacter(
            pendingCharacter,
            result => HandleDeleteResult(currentVersion, result));
    }

    public void Hide()
    {
        CancelPendingRequest();
        pendingCharacter = null;

        if (panelRoot != null)
        {
            panelRoot.SetActive(false);
        }
    }

    private void HandleDeleteResult(int version, AuthResult result)
    {
        if (version != requestVersion || !waitingForServer)
        {
            return;
        }

        StopTimeout();

        waitingForServer = false;

        bool success = result != null && result.success;
        string serverMessage = result == null ? "" : result.message;

        if (!string.IsNullOrWhiteSpace(serverMessage))
        {
            SetTip(serverMessage);
        }
        else if (success)
        {
            SetTip("成功删除 " + GetCharacterName(pendingCharacter));
        }
        else
        {
            SetTip("删除失败");
        }

        SetConfirmEnabled(!success);
    }

    private IEnumerator WaitForDeleteTimeout(int version)
    {
        yield return new WaitForSecondsRealtime(
            Mathf.Max(0.1f, timeoutSeconds));

        timeoutCoroutine = null;

        if (version != requestVersion || !waitingForServer)
        {
            yield break;
        }

        waitingForServer = false;
        SetTip("删除失败");
        SetConfirmEnabled(true);
    }

    private void CancelPendingRequest()
    {
        requestVersion++;
        waitingForServer = false;
        StopTimeout();
    }

    private void StopTimeout()
    {
        if (timeoutCoroutine == null)
        {
            return;
        }

        StopCoroutine(timeoutCoroutine);
        timeoutCoroutine = null;
    }

    private void ShowPanel()
    {
        if (panelRoot != null)
        {
            panelRoot.SetActive(true);
        }
    }

    private void SetTip(string message)
    {
        if (tipText != null)
        {
            tipText.text = message;
        }
    }

    private void SetConfirmEnabled(bool enabled)
    {
        if (confirmButton != null)
        {
            confirmButton.interactable = enabled;
        }
    }

    private static string GetCharacterName(CharacterData character)
    {
        if (character == null)
        {
            return "";
        }

        return string.IsNullOrWhiteSpace(character.characterName)
            ? character.characterId
            : character.characterName;
    }
}