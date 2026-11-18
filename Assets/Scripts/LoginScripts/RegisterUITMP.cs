using System; // 引入 Serializable，用来让自定义 UnityEvent 能在 Inspector 中显示。
using TMPro; // 引入 TextMeshPro，用来使用 TMP_InputField 和 TMP_Text。
using UnityEngine; // 引入 Unity 基础命名空间，用来使用 MonoBehaviour。
using UnityEngine.Events; // 引入 UnityEvent，用来把注册提交和注册成功事件交给外部处理。
using UnityEngine.UI; // 引入 Unity UI 命名空间，用来使用 Button。

public class RegisterUITMP : MonoBehaviour // 定义注册界面 UI 脚本，建议挂在 RegisterPanel 上。
{
    [Serializable] // 允许这个事件显示在 Inspector 面板中。
    public class RegisterSubmitEvent : UnityEvent<string, string, string> // 定义注册提交事件，参数依次是用户名、密码、确认密码。
    {
    }

    [Header("UI References")] // 在 Inspector 面板中显示 UI 引用分组。
    [SerializeField] private TMP_InputField usernameInput; // 注册用户名输入框，请拖入 UsernameInput。
    [SerializeField] private TMP_InputField passwordInput; // 注册密码输入框，请拖入 PasswordInput。
    [SerializeField] private TMP_InputField confirmPasswordInput; // 确认密码输入框，请拖入 ConfirmPasswordInput。
    [SerializeField] private Button registerButton; // 注册提交按钮，请拖入 RegisterButtonA。
    [SerializeField] private TMP_Text messageText; // 注册提示文字，请拖入 ErrorMessage 或注册面板提示文本。

    [Header("Events")] // 在 Inspector 面板中显示事件分组。
    [SerializeField] private RegisterSubmitEvent onRegisterSubmitted = new RegisterSubmitEvent(); // 注册输入通过本地校验后触发。
    [SerializeField] private UnityEvent onRegisterSucceeded = new UnityEvent(); // 注册成功后触发，用来通知面板返回登录界面。

    private bool isRegistering; // 标记当前是否正在注册，防止重复点击注册按钮。

    public RegisterSubmitEvent OnRegisterSubmitted => onRegisterSubmitted; // 提供给其他脚本访问注册提交事件。
    public UnityEvent OnRegisterSucceeded => onRegisterSucceeded; // 提供给其他脚本访问注册成功事件。

    private void Awake() // 当前脚本初始化时执行。
    {
        ConfigureInputFields(); // 配置注册输入框的输入类型。
        SetMessage(""); // 初始时清空提示文字。
    }

    private void OnEnable() // 当前物体启用时执行。
    {
        if (registerButton != null) // 判断注册按钮是否已经绑定。
        {
            registerButton.onClick.AddListener(OnRegisterButtonClick); // 绑定注册按钮点击事件。
        }
    }

    private void OnDisable() // 当前物体禁用时执行。
    {
        if (registerButton != null) // 判断注册按钮是否已经绑定。
        {
            registerButton.onClick.RemoveListener(OnRegisterButtonClick); // 移除注册按钮点击事件，避免重复绑定。
        }
    }

    private void ConfigureInputFields() // 配置注册输入框格式。
    {
        if (usernameInput != null) // 判断用户名输入框是否存在。
        {
            usernameInput.contentType = TMP_InputField.ContentType.Standard; // 用户名使用普通文本输入。
            usernameInput.lineType = TMP_InputField.LineType.SingleLine; // 用户名只允许单行输入。
        }

        if (passwordInput != null) // 判断密码输入框是否存在。
        {
            passwordInput.contentType = TMP_InputField.ContentType.Password; // 密码输入框使用隐藏显示。
            passwordInput.lineType = TMP_InputField.LineType.SingleLine; // 密码只允许单行输入。
        }

        if (confirmPasswordInput != null) // 判断确认密码输入框是否存在。
        {
            confirmPasswordInput.contentType = TMP_InputField.ContentType.Password; // 确认密码输入框使用隐藏显示。
            confirmPasswordInput.lineType = TMP_InputField.LineType.SingleLine; // 确认密码只允许单行输入。
        }
    }

    private void OnRegisterButtonClick() // 玩家点击注册按钮时执行。
    {
        if (isRegistering) // 判断当前是否已经处于注册中状态。
        {
            return; // 正在注册时直接返回，避免重复提交。
        }

        string username = GetUsername(); // 获取当前用户名输入。
        string password = GetPassword(); // 获取当前密码输入。
        string confirmPassword = GetConfirmPassword(); // 获取当前确认密码输入。
        string error = AuthValidator.GetRegisterError(username, password, confirmPassword); // 使用统一校验工具获取第一个注册输入错误。

        if (!string.IsNullOrEmpty(error)) // 判断本地校验是否发现错误。
        {
            SetMessage("错误"); // 把错误文字显示到注册界面提示文字。
            return; // 停止注册流程，等待玩家修改输入。
        }

        BeginRegister(); // 输入合法时进入注册中状态。
        onRegisterSubmitted.Invoke(username, password, confirmPassword); // 把注册输入交给外部脚本处理。
    }

    public void BeginRegister() // 外部脚本也可以调用，表示开始注册。
    {
        isRegistering = true; // 标记当前正在注册。
        SetRegisterButtonEnabled(false); // 禁用注册按钮，防止重复点击。
        SetMessage("注册中..."); // 显示注册中提示。
    }

    public void ShowRegisterFailed(string message) // 外部脚本调用，表示注册失败。
    {
        isRegistering = false; // 结束注册中状态。
        SetRegisterButtonEnabled(true); // 恢复注册按钮可点击。
        SetMessage(string.IsNullOrEmpty(message) ? "注册失败" : message); // 显示失败原因。
    }

    public void ShowRegisterSuccess() // 外部脚本调用，表示注册成功。
    {
        isRegistering = false; // 结束注册中状态。
        SetRegisterButtonEnabled(true); // 恢复注册按钮可点击。
        ClearInputs(); // 清空注册输入框。
        SetMessage("注册成功"); // 显示注册成功提示。
        onRegisterSucceeded.Invoke(); // 通知外部脚本处理注册成功后的界面跳转。
    }

    public string GetUsername() // 获取注册用户名。
    {
        if (usernameInput == null) // 判断用户名输入框是否未绑定。
        {
            return ""; // 未绑定时返回空字符串。
        }

        usernameInput.ForceLabelUpdate(); // 强制刷新输入框文本。
        return usernameInput.text.Trim(); // 返回去掉前后空格的用户名。
    }

    public string GetPassword() // 获取注册密码。
    {
        if (passwordInput == null) // 判断密码输入框是否未绑定。
        {
            return ""; // 未绑定时返回空字符串。
        }

        passwordInput.ForceLabelUpdate(); // 强制刷新输入框文本。
        return passwordInput.text; // 返回密码原文。
    }

    public string GetConfirmPassword() // 获取确认密码。
    {
        if (confirmPasswordInput == null) // 判断确认密码输入框是否未绑定。
        {
            return ""; // 未绑定时返回空字符串。
        }

        confirmPasswordInput.ForceLabelUpdate(); // 强制刷新输入框文本。
        return confirmPasswordInput.text; // 返回确认密码原文。
    }

    private void ClearInputs() // 清空注册输入框。
    {
        if (usernameInput != null) // 判断用户名输入框是否存在。
        {
            usernameInput.text = ""; // 清空用户名输入框。
        }

        if (passwordInput != null) // 判断密码输入框是否存在。
        {
            passwordInput.text = ""; // 清空密码输入框。
        }

        if (confirmPasswordInput != null) // 判断确认密码输入框是否存在。
        {
            confirmPasswordInput.text = ""; // 清空确认密码输入框。
        }
    }

    private void SetRegisterButtonEnabled(bool enabled) // 设置注册按钮是否可点击。
    {
        if (registerButton != null) // 判断注册按钮是否存在。
        {
            registerButton.interactable = enabled; // 设置按钮交互状态。
        }
    }

    private void SetMessage(string message) // 设置注册界面提示文字。
    {
        if (messageText != null) // 判断提示文字是否存在。
        {
            messageText.text = message; // 显示提示内容。
        }
    }
}
