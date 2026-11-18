using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CreateCharacterPanelUI : MonoBehaviour
{
    [Header("Panel")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private CharacterSelectPanelUI owner;
    [SerializeField] private Button submitButton;
    [SerializeField] private Button closeButton;
    [SerializeField] private TMP_Text messageText;
    [SerializeField] private TMP_InputField nicknameInput;

    [Header("Choices")]
    [SerializeField] private CharacterRoleSwitcher roleSwitcher;

    public event Action<CharacterSpriteMapping, string> CreateRequested;

    private readonly List<CharacterSpriteMapping> mappings = new List<CharacterSpriteMapping>();
    private CharacterSpriteMapping selectedMapping;
    private bool isSubmitting;

    private void Awake()
    {
        ResolveReferences();
    }

    private void OnEnable()
    {
        ResolveReferences();

        if (submitButton != null)
        {
            submitButton.onClick.AddListener(Submit);
        }

        if (closeButton != null)
        {
            closeButton.onClick.AddListener(Hide);
        }

        SubscribeRoleSwitcher();
    }

    private void OnDisable()
    {
        if (submitButton != null)
        {
            submitButton.onClick.RemoveListener(Submit);
        }

        if (closeButton != null)
        {
            closeButton.onClick.RemoveListener(Hide);
        }

        UnsubscribeRoleSwitcher();
    }

    public void Show(IReadOnlyList<CharacterSpriteMapping> availableMappings)
    {
        ResolveReferences();

        mappings.Clear();
        if (availableMappings != null)
        {
            for (int index = 0; index < availableMappings.Count; index++)
            {
                CharacterSpriteMapping mapping = availableMappings[index];
                if (mapping != null &&
                    !string.IsNullOrWhiteSpace(mapping.characterId))
                {
                    mappings.Add(mapping);
                }
            }
        }

        if (panelRoot != null)
        {
            panelRoot.SetActive(true);
            panelRoot.transform.SetAsLastSibling();
        }

        SubscribeRoleSwitcher();
        if (roleSwitcher != null)
        {
            roleSwitcher.gameObject.SetActive(true);
            roleSwitcher.SelectIndex(0, false);
        }
        else
        {
            SelectMapping(mappings.Count > 0 ? mappings[0] : null);
        }

        if (nicknameInput != null)
        {
            nicknameInput.text = "";
        }

        SetSubmitting(false);
        BringActionButtonsToFront();
        ShowMessage(mappings.Count == 0 ? "Character templates are missing." : "");
    }

    public void Hide()
    {
        SetSubmitting(false);
        SelectMapping(null);

        if (panelRoot != null)
        {
            panelRoot.SetActive(false);
        }
    }

    public void SetSubmitting(bool submitting)
    {
        isSubmitting = submitting;

        if (submitButton != null)
        {
            submitButton.interactable = !isSubmitting && selectedMapping != null;
        }

        if (closeButton != null)
        {
            closeButton.interactable = !isSubmitting;
        }

        if (roleSwitcher != null)
        {
            roleSwitcher.enabled = !isSubmitting;
        }

        if (nicknameInput != null)
        {
            nicknameInput.interactable = !isSubmitting;
        }

    }

    public void ShowMessage(string message)
    {
        if (messageText != null)
        {
            messageText.text = message ?? "";
        }
    }

    public void SetOwner(CharacterSelectPanelUI panelOwner)
    {
        owner = panelOwner;
    }

    private void Submit()
    {
        Debug.Log("Create character submit clicked.");

        if (isSubmitting)
        {
            Debug.LogWarning("Create character submit ignored: request is already in flight.", this);
            return;
        }

        if (selectedMapping == null || string.IsNullOrWhiteSpace(selectedMapping.characterId))
        {
            ShowMessage("Please select a character.");
            Debug.LogWarning("Create character submit ignored: no character is selected.", this);
            return;
        }

        ResolveReferences();
        if (nicknameInput == null)
        {
            ShowMessage("Nickname input is missing.");
            Debug.LogError("Create character submit failed: Nicknanetxt TMP_InputField was not found.", this);
            return;
        }

        string nickname = nicknameInput.text.Trim();
        if (string.IsNullOrWhiteSpace(nickname))
        {
            ShowMessage("Please enter a character nickname.");
            nicknameInput.ActivateInputField();
            return;
        }

        SetSubmitting(true);
        ShowMessage("Creating character...");

        ResolveOwner();
        if (owner != null)
        {
            owner.CreateCharacterFromPanel(selectedMapping, nickname);
            return;
        }

        if (CreateRequested != null)
        {
            CreateRequested.Invoke(selectedMapping, nickname);
            return;
        }

        SetSubmitting(false);
        ShowMessage("Create controller is missing.");
        Debug.LogError("Create character submit failed: CharacterSelectPanelUI owner is missing and no listener is registered.", this);
    }

    private void SelectMapping(CharacterSpriteMapping mapping)
    {
        selectedMapping = mapping;

        if (submitButton != null)
        {
            submitButton.interactable = !isSubmitting && selectedMapping != null;
        }
    }

    private void HandleRoleIndexChanged(int index)
    {
        CharacterSpriteMapping mapping = index >= 0 && index < mappings.Count
            ? mappings[index]
            : null;
        SelectMapping(mapping);
    }

    private void SubscribeRoleSwitcher()
    {
        if (roleSwitcher == null)
        {
            return;
        }

        roleSwitcher.OnIndexChanged -= HandleRoleIndexChanged;
        roleSwitcher.OnIndexChanged += HandleRoleIndexChanged;
    }

    private void UnsubscribeRoleSwitcher()
    {
        if (roleSwitcher != null)
        {
            roleSwitcher.OnIndexChanged -= HandleRoleIndexChanged;
        }
    }

    private void BringActionButtonsToFront()
    {
        if (submitButton != null)
        {
            submitButton.transform.SetAsLastSibling();
        }

        if (closeButton != null)
        {
            closeButton.transform.SetAsLastSibling();
        }
    }

    private void ResolveReferences()
    {
        if (panelRoot == null)
        {
            panelRoot = gameObject;
        }

        if (owner == null)
        {
            ResolveOwner();
        }

        if (submitButton == null)
        {
            submitButton = FindButtonByName("submitButton", "SubmitButton");
        }

        if (closeButton == null)
        {
            closeButton = FindButtonByName("ButtonC", "CloseButton", "BackButton");
        }

        if (messageText == null)
        {
            messageText = FindTextByName("MessageText", "ErrorMessage", "ErrorMessageCreate");
        }

        if (nicknameInput == null)
        {
            nicknameInput = FindInputFieldByName("Nicknanetxt", "NicknameTxt", "NicknameInput");
        }

        if (nicknameInput != null)
        {
            nicknameInput.contentType = TMP_InputField.ContentType.Standard;
            nicknameInput.lineType = TMP_InputField.LineType.SingleLine;
        }

        if (roleSwitcher == null)
        {
            roleSwitcher = GetComponentInChildren<CharacterRoleSwitcher>(true);
        }
    }

    private Button FindButtonByName(params string[] names)
    {
        Button[] buttons = GetComponentsInChildren<Button>(true);
        for (int buttonIndex = 0; buttonIndex < buttons.Length; buttonIndex++)
        {
            Button button = buttons[buttonIndex];
            for (int nameIndex = 0; nameIndex < names.Length; nameIndex++)
            {
                if (string.Equals(button.name.Trim(), names[nameIndex], StringComparison.OrdinalIgnoreCase))
                {
                    return button;
                }
            }
        }

        return null;
    }

    private TMP_InputField FindInputFieldByName(params string[] names)
    {
        TMP_InputField[] inputFields = GetComponentsInChildren<TMP_InputField>(true);
        for (int inputIndex = 0; inputIndex < inputFields.Length; inputIndex++)
        {
            TMP_InputField inputField = inputFields[inputIndex];
            for (int nameIndex = 0; nameIndex < names.Length; nameIndex++)
            {
                if (string.Equals(inputField.name.Trim(), names[nameIndex], StringComparison.OrdinalIgnoreCase))
                {
                    return inputField;
                }
            }
        }

        return inputFields.Length == 1 ? inputFields[0] : null;
    }

    private void ResolveOwner()
    {
        if (owner != null)
        {
            return;
        }

        owner = GetComponentInParent<CharacterSelectPanelUI>(true);
        if (owner != null)
        {
            return;
        }

        CharacterSelectPanelUI[] panels = Resources.FindObjectsOfTypeAll<CharacterSelectPanelUI>();
        for (int index = 0; index < panels.Length; index++)
        {
            CharacterSelectPanelUI panel = panels[index];
            if (panel != null && panel.gameObject.scene.IsValid() && panel.hideFlags == HideFlags.None)
            {
                owner = panel;
                return;
            }
        }
    }

    private TMP_Text FindTextByName(params string[] names)
    {
        TMP_Text[] texts = GetComponentsInChildren<TMP_Text>(true);
        for (int textIndex = 0; textIndex < texts.Length; textIndex++)
        {
            TMP_Text text = texts[textIndex];
            for (int nameIndex = 0; nameIndex < names.Length; nameIndex++)
            {
                if (string.Equals(text.name.Trim(), names[nameIndex], StringComparison.OrdinalIgnoreCase))
                {
                    return text;
                }
            }
        }

        return null;
    }
}
