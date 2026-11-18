$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Get-Content -LiteralPath (Join-Path $root 'Assets/Scripts/LoginScripts/AuthController.cs') -Raw
$stubs = @'
namespace UnityEngine
{
    public enum RuntimeInitializeLoadType { AfterSceneLoad }
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType type) { }
    }
    public enum HideFlags { None, HideAndDontSave }
    public class Object
    {
        private static int nextId;
        private readonly int id = ++nextId;
        public HideFlags hideFlags;
        public int GetInstanceID() => id;
    }
    public class GameObject : Object
    {
        public string name = "AuthSystem";
        public bool activeInHierarchy = true;
        public SceneManagement.Scene scene =
            new SceneManagement.Scene { name = "SampleScene", isLoaded = true };
        public GameObject() { }
        public GameObject(string value) { name = value; }
        public T AddComponent<T>() where T : Component, new()
        {
            T component = new T();
            component.gameObject = this;
            if (component is AuthController controller)
            {
                Resources.CreatedController = controller;
                Resources.Controllers = new[] { controller };
            }
            return component;
        }
    }
    public class Component : Object
    {
        public GameObject gameObject = new GameObject();
        public bool enabled = true;
    }
    public class MonoBehaviour : Component
    {
        public static object LastDestroyed;
        protected static void Destroy(object value) { LastDestroyed = value; }
        protected static void DontDestroyOnLoad(object value) { }
    }
    public static class Resources
    {
        public static AuthApiClient[] Clients = Array.Empty<AuthApiClient>();
        public static LoginUITMP[] LoginViews = Array.Empty<LoginUITMP>();
        public static RegisterUITMP[] RegisterViews = Array.Empty<RegisterUITMP>();
        public static AuthController[] Controllers = Array.Empty<AuthController>();
        public static AuthController CreatedController;
        public static T[] FindObjectsOfTypeAll<T>() where T : Component
        {
            if (typeof(T) == typeof(AuthApiClient)) return (T[])(object)Clients;
            if (typeof(T) == typeof(LoginUITMP)) return (T[])(object)LoginViews;
            if (typeof(T) == typeof(RegisterUITMP)) return (T[])(object)RegisterViews;
            if (typeof(T) == typeof(AuthController)) return (T[])(object)Controllers;
            return Array.Empty<T>();
        }
    }
    public static class Debug
    {
        public static readonly List<string> Messages = new List<string>();
        public static void Log(object value) => Messages.Add(value.ToString());
        public static void LogError(object value) => Messages.Add(value.ToString());
        public static void LogWarning(object value) => Messages.Add(value.ToString());
    }
}
namespace UnityEngine.SceneManagement
{
    public enum LoadSceneMode { Single }
    public struct Scene
    {
        public string name;
        public bool isLoaded;
        public bool IsValid() => true;
    }
    public static class SceneManager
    {
        public static event Action<Scene, LoadSceneMode> sceneLoaded;
        public static string ActiveSceneName = "SampleScene";
        public static Scene GetActiveScene() =>
            new Scene { name = ActiveSceneName, isLoaded = true };
        public static void LoadScene(string name)
        {
            ActiveSceneName = name;
            sceneLoaded?.Invoke(GetActiveScene(), LoadSceneMode.Single);
        }
    }
}
public sealed class Event2
{
    private event Action<string, string> listeners;
    public void AddListener(Action<string, string> action) { listeners += action; }
    public void RemoveListener(Action<string, string> action) { listeners -= action; }
    public void Invoke(string user, string password) { listeners?.Invoke(user, password); }
}
public sealed class Event3
{
    private event Action<string, string, string> listeners;
    public void AddListener(Action<string, string, string> action) { listeners += action; }
    public void RemoveListener(Action<string, string, string> action) { listeners -= action; }
    public void Invoke(string user, string password, string confirm)
    {
        listeners?.Invoke(user, password, confirm);
    }
}
public sealed class LoginUITMP : UnityEngine.Component
{
    public Event2 OnLoginSubmitted = new Event2();
    public string Failure;
    public void ShowLoginFailed(string message) { Failure = message; }
    public void ShowLoginSuccess() { }
}
public sealed class RegisterUITMP : UnityEngine.Component
{
    public Event3 OnRegisterSubmitted = new Event3();
    public string Failure;
    public void ShowRegisterFailed(string message) { Failure = message; }
    public void ShowRegisterSuccess() { }
}
public sealed class AuthApiClient : UnityEngine.Component
{
    public int LoginCalls;
    public int RegisterCalls;
    public void Login(string user, string password, Action<AuthResult> callback)
    {
        LoginCalls++;
        callback?.Invoke(new AuthResult { message = "simulated failure" });
    }
    public void Register(string user, string password, Action<AuthResult> callback)
    {
        RegisterCalls++;
        callback?.Invoke(new AuthResult { message = "simulated failure" });
    }
}
public sealed class AuthSession : UnityEngine.Component
{
    public static AuthSession Instance;
    public void SaveSession(string token, string userId, string username) { }
}
public sealed class AuthResult
{
    public bool success;
    public string message;
    public string username;
    public string userId;
    public string token;
}
public sealed class CharacterSelectPanelUI : UnityEngine.Component
{
    public void InitializeAfterLogin() { }
}
public static class AuthValidator
{
    public static string GetLoginError(string username, string password) => "";
    public static string GetRegisterError(string username, string password, string confirm) => "";
}
'@
Add-Type -TypeDefinition ("using System;`n" +
    $source + "`n" + $stubs) -IgnoreWarnings -WarningAction SilentlyContinue | Out-Null

$client = [AuthApiClient]::new()
$loginView = [LoginUITMP]::new()
$registerView = [RegisterUITMP]::new()
[UnityEngine.Resources]::Clients = @($client)
[UnityEngine.Resources]::LoginViews = @($loginView)
[UnityEngine.Resources]::RegisterViews = @($registerView)
$client.hideFlags = [UnityEngine.HideFlags]::HideAndDontSave

$controller = [AuthController]::new()
$flags = [System.Reflection.BindingFlags]'Instance,NonPublic'
$controller.GetType().GetMethod('BindCurrentScene', $flags).Invoke($controller, @()) | Out-Null
$controller.GetType().GetMethod('HandleLoginSubmitted', $flags).
    Invoke($controller, @('user-for-test', 'password-for-test')) | Out-Null
$controller.GetType().GetMethod('HandleRegisterSubmitted', $flags).
    Invoke($controller, @('user-for-test', 'password-for-test', 'password-for-test')) | Out-Null
$messages = [UnityEngine.Debug]::Messages -join "`n"
if ($loginView.Failure -ne '认证服务不可用' -or
    $registerView.Failure -ne '认证服务不可用' -or
    $client.LoginCalls -ne 0 -or $client.RegisterCalls -ne 0 -or
    $messages -notmatch 'stage=auth_client_missing operation=login.*candidateCount=1' -or
    $messages -notmatch 'stage=auth_client_missing operation=register.*candidateCount=1' -or
    $messages -notmatch 'stage=auth_client_candidate operation=login.*eligible=False' -or
    $messages -match 'password-for-test') {
    throw 'Missing authentication service must identify the rejected component without sending or logging credentials.'
}

$client.hideFlags = [UnityEngine.HideFlags]::None
$controller = [AuthController]::new()
$controller.GetType().GetMethod('BindCurrentScene', $flags).Invoke($controller, @()) | Out-Null
$controller.GetType().GetMethod('HandleLoginSubmitted', $flags).
    Invoke($controller, @('user-for-test', 'password-for-test')) | Out-Null
$controller.GetType().GetMethod('HandleRegisterSubmitted', $flags).
    Invoke($controller, @('user-for-test', 'password-for-test', 'password-for-test')) | Out-Null
if ($client.LoginCalls -ne 1 -or $client.RegisterCalls -ne 1) {
    throw 'A visible authentication component must still receive both requests.'
}

$owner = [AuthController]::new()
$owner.GetType().GetMethod('Awake', $flags).Invoke($owner, @()) | Out-Null
$duplicate = [AuthController]::new()
$duplicate.GetType().GetMethod('Awake', $flags).Invoke($duplicate, @()) | Out-Null
if (-not [object]::ReferenceEquals([UnityEngine.MonoBehaviour]::LastDestroyed, $duplicate)) {
    throw 'A duplicate controller must destroy only itself, not its shared authentication object.'
}

$scene = Get-Content -LiteralPath (Join-Path $root 'Assets/Scenes/SampleScene.unity') -Raw
if ($scene -notmatch 'm_Name: AuthSystem' -or
    $scene -notmatch 'guid: 88f6e746a2bc59d4992c200e688df577' -or
    $scene -notmatch 'guid: 72b924beb91c3e24f9262d9fc1fa3b04' -or
    $scene -match 'guid: a0cf6e0e1c26454b88a5ef0d5d05cd32' -or
    $scene -match 'component: \{fileID: 1132108832\}') {
    throw 'Login scene must retain the authentication client and session without a duplicate controller.'
}

[AuthController].GetField('instance', [System.Reflection.BindingFlags]'Static,NonPublic').
    SetValue($null, $null)
[UnityEngine.Resources]::Controllers = @()
[UnityEngine.Resources]::Clients = @()
[UnityEngine.Resources]::LoginViews = @()
[UnityEngine.Resources]::RegisterViews = @()
[UnityEngine.SceneManagement.SceneManager]::ActiveSceneName = 'Startup'
[UnityEngine.Debug]::Messages.Clear()
$bootstrap = [AuthController].GetMethod('Bootstrap', [System.Reflection.BindingFlags]'Static,NonPublic')
$bootstrap.Invoke($null, @()) | Out-Null
$persistent = [UnityEngine.Resources]::CreatedController
if ($null -eq $persistent) { throw 'Startup must create the persistent authentication controller.' }
$persistent.GetType().GetMethod('Awake', $flags).Invoke($persistent, @()) | Out-Null
$persistent.GetType().GetMethod('OnEnable', $flags).Invoke($persistent, @()) | Out-Null
if (([UnityEngine.Debug]::Messages -join "`n") -notmatch
    'stage=bind_scene loginViews=0 authClientFound=False.*scene=Startup') {
    throw 'Startup scene must not require the login UI or authentication client.'
}

$client = [AuthApiClient]::new()
$loginView = [LoginUITMP]::new()
$registerView = [RegisterUITMP]::new()
[UnityEngine.Resources]::Clients = @($client)
[UnityEngine.Resources]::LoginViews = @($loginView)
[UnityEngine.Resources]::RegisterViews = @($registerView)
[UnityEngine.SceneManagement.SceneManager]::LoadScene('SampleScene')
$loginView.OnLoginSubmitted.Invoke('test-user', 'test-password')
$registerView.OnRegisterSubmitted.Invoke('test-user', 'test-password', 'test-password')
if ($client.LoginCalls -ne 1 -or $client.RegisterCalls -ne 1 -or
    ([UnityEngine.Debug]::Messages -join "`n") -notmatch
        'stage=bind_scene loginViews=1 authClientFound=True.*scene=SampleScene') {
    throw 'Startup-to-login transition must bind both authentication requests.'
}

[UnityEngine.Resources]::LoginViews = @()
[UnityEngine.Resources]::RegisterViews = @()
[UnityEngine.SceneManagement.SceneManager]::LoadScene('WorldShell')
$returnLogin = [LoginUITMP]::new()
$returnRegister = [RegisterUITMP]::new()
[UnityEngine.Resources]::LoginViews = @($returnLogin)
[UnityEngine.Resources]::RegisterViews = @($returnRegister)
[UnityEngine.SceneManagement.SceneManager]::LoadScene('SampleScene')
$returnLogin.OnLoginSubmitted.Invoke('test-user', 'test-password')
$returnRegister.OnRegisterSubmitted.Invoke('test-user', 'test-password', 'test-password')
if ($client.LoginCalls -ne 2 -or $client.RegisterCalls -ne 2 -or
    ([UnityEngine.Debug]::Messages -join "`n") -match 'stage=auth_client_missing') {
    throw 'Returning to login must rebind the persistent controller without losing its client.'
}

$persistent.GetType().GetMethod('OnDisable', $flags).Invoke($persistent, @()) | Out-Null
[AuthController].GetField('instance', [System.Reflection.BindingFlags]'Static,NonPublic').
    SetValue($null, $null)
[UnityEngine.Resources]::Controllers = @()
[UnityEngine.Resources]::CreatedController = $null
[UnityEngine.SceneManagement.SceneManager]::ActiveSceneName = 'SampleScene'
$bootstrap.Invoke($null, @()) | Out-Null
$direct = [UnityEngine.Resources]::CreatedController
if ($null -eq $direct) { throw 'Direct login scene play must create a controller.' }
$direct.GetType().GetMethod('Awake', $flags).Invoke($direct, @()) | Out-Null
$direct.GetType().GetMethod('OnEnable', $flags).Invoke($direct, @()) | Out-Null
$returnLogin.OnLoginSubmitted.Invoke('test-user', 'test-password')
if ($client.LoginCalls -ne 3) {
    throw 'Direct login scene play must bind its existing login UI.'
}

Write-Output 'Auth controller lifecycle diagnostics tests passed.'
