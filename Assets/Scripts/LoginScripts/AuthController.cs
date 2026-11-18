using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class AuthController : MonoBehaviour
{
    private static AuthController instance;

    private readonly List<LoginUITMP> boundLoginViews = new List<LoginUITMP>();
    private readonly List<RegisterUITMP> boundRegisterViews = new List<RegisterUITMP>();

    private AuthApiClient authApiClient;
    private AuthSession authSession;
    private int lastResolvedAuthClientId;
    private bool lastResolveHadDestroyedReference;
    private bool loginRequestInFlight;
    private bool registerRequestInFlight;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (instance != null || FindSceneObject<AuthController>() != null)
        {
            return;
        }

        GameObject controllerObject = new GameObject("AuthController");
        controllerObject.AddComponent<AuthController>();
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(this);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
        BindCurrentScene();
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        UnbindUiEvents();
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        BindCurrentScene();
    }

    private void BindCurrentScene()
    {
        UnbindUiEvents();
        ResolveServices();

        foreach (LoginUITMP loginView in FindSceneObjects<LoginUITMP>())
        {
            loginView.OnLoginSubmitted.RemoveListener(HandleLoginSubmitted);
            loginView.OnLoginSubmitted.AddListener(HandleLoginSubmitted);
            boundLoginViews.Add(loginView);
        }

        foreach (RegisterUITMP registerView in FindSceneObjects<RegisterUITMP>())
        {
            registerView.OnRegisterSubmitted.RemoveListener(HandleRegisterSubmitted);
            registerView.OnRegisterSubmitted.AddListener(HandleRegisterSubmitted);
            boundRegisterViews.Add(registerView);
        }

        Debug.Log("[AUTH_LOGIN] stage=bind_scene loginViews=" + boundLoginViews.Count
            + " authClientFound=" + (authApiClient != null)
            + " authClientId=" + lastResolvedAuthClientId
            + " scene=" + SceneManager.GetActiveScene().name);
    }

    private void UnbindUiEvents()
    {
        foreach (LoginUITMP loginView in boundLoginViews)
        {
            if (loginView != null)
            {
                loginView.OnLoginSubmitted.RemoveListener(HandleLoginSubmitted);
            }
        }

        foreach (RegisterUITMP registerView in boundRegisterViews)
        {
            if (registerView != null)
            {
                registerView.OnRegisterSubmitted.RemoveListener(HandleRegisterSubmitted);
            }
        }

        boundLoginViews.Clear();
        boundRegisterViews.Clear();
    }

    private void ResolveServices()
    {
        if (authApiClient == null)
        {
            lastResolveHadDestroyedReference = !ReferenceEquals(authApiClient, null);
            authApiClient = FindSceneObject<AuthApiClient>();
        }
        else
        {
            lastResolveHadDestroyedReference = false;
        }

        if (authApiClient != null)
            lastResolvedAuthClientId = authApiClient.GetInstanceID();

        if (authSession == null)
        {
            authSession = AuthSession.Instance != null ? AuthSession.Instance : FindSceneObject<AuthSession>();
        }
    }

    private void HandleLoginSubmitted(string username, string password)
    {
        Debug.Log("[AUTH_LOGIN] stage=controller_submit inFlight=" + loginRequestInFlight);
        if (loginRequestInFlight)
        {
            return;
        }

        string error = AuthValidator.GetLoginError(username, password);
        if (!string.IsNullOrEmpty(error))
        {
            Debug.LogWarning("[AUTH_LOGIN] stage=controller_validation_failed");
            ShowLoginFailed(error);
            return;
        }

        ResolveServices();
        if (authApiClient == null)
        {
            LogMissingAuthClient("login");
            ShowLoginFailed("认证服务不可用");
            return;
        }

        loginRequestInFlight = true;
        Debug.Log("[AUTH_LOGIN] stage=client_call");
        authApiClient.Login(username, password, result => HandleLoginResult(result, username));
    }

    private void HandleRegisterSubmitted(string username, string password, string confirmPassword)
    {
        if (registerRequestInFlight)
        {
            return;
        }

        string error = AuthValidator.GetRegisterError(username, password, confirmPassword);
        if (!string.IsNullOrEmpty(error))
        {
            ShowRegisterFailed(error);
            return;
        }

        ResolveServices();
        if (authApiClient == null)
        {
            LogMissingAuthClient("register");
            ShowRegisterFailed("认证服务不可用");
            return;
        }

        registerRequestInFlight = true;
        authApiClient.Register(username, password, result => HandleRegisterResult(result, username));
    }

    private void HandleLoginResult(AuthResult result, string submittedUsername)
    {
        loginRequestInFlight = false;
        Debug.Log("[AUTH_LOGIN] stage=controller_result responsePresent=" + (result != null)
            + " success=" + (result != null && result.success));

        if (result == null || !result.success)
        {
            ShowLoginFailed(result == null ? "登录失败" : result.message);
            return;
        }

        SaveSessionIfIdentityExists(result, submittedUsername);
        foreach (LoginUITMP loginView in boundLoginViews)
        {
            if (loginView != null)
            {
                loginView.ShowLoginSuccess();
            }
        }

        CharacterSelectPanelUI characterSelectPanel = FindSceneObject<CharacterSelectPanelUI>();
        if (characterSelectPanel != null)
        {
            characterSelectPanel.InitializeAfterLogin();
        }
    }

    private void HandleRegisterResult(AuthResult result, string submittedUsername)
    {
        registerRequestInFlight = false;

        if (result == null || !result.success)
        {
            ShowRegisterFailed(result == null ? "注册失败" : result.message);
            return;
        }

        SaveSessionIfIdentityExists(result, submittedUsername);
        foreach (RegisterUITMP registerView in boundRegisterViews)
        {
            if (registerView != null)
            {
                registerView.ShowRegisterSuccess();
            }
        }
    }

    private void SaveSessionIfIdentityExists(AuthResult result, string submittedUsername)
    {
        if (result == null)
        {
            return;
        }

        ResolveServices();
        if (authSession == null)
        {
            return;
        }

        string username = string.IsNullOrEmpty(result.username) ? submittedUsername : result.username;
        string userId = string.IsNullOrEmpty(result.userId) ? username : result.userId;
        string token = result.token ?? "";

        if (string.IsNullOrEmpty(userId))
        {
            Debug.LogWarning("Login succeeded, but server response did not include userId or username.");
            return;
        }

        authSession.SaveSession(token, userId, username);
        Debug.Log("Auth session saved: userId=" + userId + ", username=" + username + ", hasToken=" + !string.IsNullOrEmpty(token));
    }

    private void ShowLoginFailed(string message)
    {
        loginRequestInFlight = false;
        foreach (LoginUITMP loginView in boundLoginViews)
        {
            if (loginView != null)
            {
                loginView.ShowLoginFailed(message);
            }
        }
    }

    private void ShowRegisterFailed(string message)
    {
        registerRequestInFlight = false;
        foreach (RegisterUITMP registerView in boundRegisterViews)
        {
            if (registerView != null)
            {
                registerView.ShowRegisterFailed(message);
            }
        }
    }

    private void LogMissingAuthClient(string operation)
    {
        AuthApiClient[] candidates = Resources.FindObjectsOfTypeAll<AuthApiClient>();
        Debug.LogError("[AUTH_LOGIN] stage=auth_client_missing operation=" + operation
            + " scene=" + SceneManager.GetActiveScene().name
            + " lastResolvedClientId=" + lastResolvedAuthClientId
            + " cachedReferenceDestroyed=" + lastResolveHadDestroyedReference
            + " candidateCount=" + candidates.Length);

        foreach (AuthApiClient candidate in candidates)
        {
            if (candidate == null)
                continue;

            GameObject owner = candidate.gameObject;
            Scene scene = owner.scene;
            Debug.LogError("[AUTH_LOGIN] stage=auth_client_candidate operation=" + operation
                + " clientId=" + candidate.GetInstanceID()
                + " objectId=" + owner.GetInstanceID()
                + " objectName=" + owner.name
                + " scene=" + scene.name
                + " sceneValid=" + scene.IsValid()
                + " sceneLoaded=" + scene.isLoaded
                + " componentHideFlags=" + candidate.hideFlags
                + " objectHideFlags=" + owner.hideFlags
                + " activeInHierarchy=" + owner.activeInHierarchy
                + " enabled=" + candidate.enabled
                + " eligible=" + IsSceneObject(candidate));
        }
    }

    private static T FindSceneObject<T>() where T : Component
    {
        foreach (T item in Resources.FindObjectsOfTypeAll<T>())
        {
            if (IsSceneObject(item))
            {
                return item;
            }
        }

        return null;
    }

    private static List<T> FindSceneObjects<T>() where T : Component
    {
        List<T> results = new List<T>();
        foreach (T item in Resources.FindObjectsOfTypeAll<T>())
        {
            if (IsSceneObject(item))
            {
                results.Add(item);
            }
        }

        return results;
    }

    private static bool IsSceneObject(Component item)
    {
        return item != null && item.gameObject.scene.IsValid() && item.hideFlags == HideFlags.None;
    }
}


