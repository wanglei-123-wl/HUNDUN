using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

public class LoginUITMP : MonoBehaviour
{
    [Serializable]
    public class LoginSubmitEvent : UnityEvent<string, string>
    {
    }

    [Header("UI References")]
    [SerializeField] private TMP_InputField usernameInput;
    [SerializeField] private TMP_InputField passwordInput;
    [SerializeField] private Button loginButton;
    [SerializeField] private TMP_Text messageText;

    [Header("View References")]
    [SerializeField] private GameObject backgroundImage;
    [SerializeField] private GameObject loginView;
    [SerializeField] private GameObject registerView;
    [SerializeField] private GameObject characterSelectView;

    [Header("Events")]
    [SerializeField] private LoginSubmitEvent onLoginSubmitted = new LoginSubmitEvent();

    private bool isLoggingIn;

    public LoginSubmitEvent OnLoginSubmitted => onLoginSubmitted;

    private void Awake()
    {
        ConfigureInputFields();

        if (characterSelectView != null)
        {
            characterSelectView.SetActive(false);
        }

        SetMessage("");
    }

    private void Start()
    {
        string gateFailure = GateEntryHandoff.TakeFailure(out long retryCharacterDbId);
        WorldEntryLoadingView.CloseWorldEntry();
        CharacterSelectPanelUI selection = characterSelectView != null
            ? characterSelectView.GetComponent<CharacterSelectPanelUI>() : null;
        AuthSession auth = AuthSession.Instance;
        if (selection != null && auth != null && auth.TryConsumeCharacterSelectReturn())
        {
            ShowCharacterSelectView();
            selection.InitializeAfterLogin();
            return;
        }

        if (string.IsNullOrEmpty(gateFailure)) return;

        if (retryCharacterDbId > 0 && selection != null && auth != null && auth.HasValidGameSession)
        {
            ShowCharacterSelectView();
            selection.ShowEntryRecovery(gateFailure, retryCharacterDbId);
            return;
        }
        ShowLoginFailed(gateFailure);
    }

    internal void ReturnToLoginView(string message)
    {
        if (characterSelectView != null) characterSelectView.SetActive(false);
        if (registerView != null) registerView.SetActive(false);
        if (backgroundImage != null) backgroundImage.SetActive(true);
        if (loginView != null) loginView.SetActive(true);
        ShowLoginFailed(message);
    }

    private void OnEnable()
    {
        if (loginButton != null)
        {
            loginButton.onClick.AddListener(OnLoginButtonClick);
        }
    }

    private void OnDisable()
    {
        if (loginButton != null)
        {
            loginButton.onClick.RemoveListener(OnLoginButtonClick);
        }
    }

    private void ConfigureInputFields()
    {
        if (usernameInput != null)
        {
            usernameInput.contentType = TMP_InputField.ContentType.Standard;
            usernameInput.lineType = TMP_InputField.LineType.SingleLine;
        }

        if (passwordInput != null)
        {
            passwordInput.contentType = TMP_InputField.ContentType.Password;
            passwordInput.lineType = TMP_InputField.LineType.SingleLine;
        }
    }

    private void OnLoginButtonClick()
    {
        Debug.Log("[AUTH_LOGIN] stage=ui_click inFlight=" + isLoggingIn);
        if (isLoggingIn)
        {
            return;
        }

        string username = GetUsername();
        string password = GetPassword();

        string error = AuthValidator.GetLoginError(username, password);
        if (!string.IsNullOrEmpty(error))
        {
            Debug.LogWarning("[AUTH_LOGIN] stage=ui_validation_failed");
            SetMessage(error);
            return;
        }

        BeginLogin();
        Debug.Log("[AUTH_LOGIN] stage=ui_submit");
        onLoginSubmitted.Invoke(username, password);
    }

    public void BeginLogin()
    {
        isLoggingIn = true;
        SetLoginButtonEnabled(false);
        SetMessage("登录中...");
    }

    public void ShowLoginFailed(string message)
    {
        isLoggingIn = false;
        SetLoginButtonEnabled(true);
        SetMessage(string.IsNullOrEmpty(message) ? "登录失败" : message);
    }

    public void ShowLoginSuccess()
    {
        isLoggingIn = false;
        SetLoginButtonEnabled(true);
        SetMessage("登录成功");
        ShowCharacterSelectView();
    }

    public string GetUsername()
    {
        if (usernameInput == null)
        {
            return "";
        }

        usernameInput.ForceLabelUpdate();
        return usernameInput.text.Trim();
    }

    public string GetPassword()
    {
        if (passwordInput == null)
        {
            return "";
        }

        passwordInput.ForceLabelUpdate();
        return passwordInput.text;
    }

    private void ShowCharacterSelectView()
    {
        if (backgroundImage != null)
        {
            backgroundImage.SetActive(false);
        }

        if (loginView != null)
        {
            loginView.SetActive(false);
        }

        if (registerView != null)
        {
            registerView.SetActive(false);
        }

        if (characterSelectView != null)
        {
            characterSelectView.SetActive(true);
        }
    }

    private void SetLoginButtonEnabled(bool enabled)
    {
        if (loginButton != null)
        {
            loginButton.interactable = enabled;
        }
    }

    private void SetMessage(string message)
    {
        if (messageText != null)
        {
            messageText.text = message;
        }
    }
}
