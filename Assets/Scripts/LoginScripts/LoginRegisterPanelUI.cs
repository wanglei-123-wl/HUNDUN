using UnityEngine; // 引入 Unity 基础命名空间，用来使用 MonoBehaviour 和 GameObject。
using UnityEngine.UI; // 引入 Unity UI 命名空间，用来使用 Button 按钮组件。

public class LoginRegisterPanelUI : MonoBehaviour // 定义登录注册面板切换脚本，脚本名必须和文件名一致。
{
    [Header("Panels")] // 在 Inspector 面板中显示面板引用分组。
    [SerializeField] private GameObject loginPanel; // 登录面板对象，请拖入场景中的 LoginPanel。
    [SerializeField] private GameObject registerPanel; // 注册面板对象，请拖入场景中的 RegisterPanel。

    [Header("Buttons")] // 在 Inspector 面板中显示按钮引用分组。
    [SerializeField] private Button registerButton; // 登录面板上的注册入口按钮，请拖入场景中的 RegisterButton。
    [SerializeField] private Button backButton; // 注册面板上的返回按钮，请拖入场景中的 BackButton。

    private void Awake() // 当前物体初始化时自动执行。
    {
        AddButtonListeners(); // 给按钮绑定点击事件。
        ShowLoginPanel(); // 进入场景时默认显示登录面板。
    }

    private void OnDestroy() // 当前物体销毁时自动执行。
    {
        RemoveButtonListeners(); // 移除按钮点击事件，避免重复绑定。
    }

    private void AddButtonListeners() // 统一绑定按钮事件。
    {
        if (registerButton != null) // 判断注册入口按钮是否已经在 Inspector 中绑定。
        {
            registerButton.onClick.AddListener(ShowRegisterPanel); // 点击注册入口按钮时显示注册面板。
        }

        if (backButton != null) // 判断返回按钮是否已经在 Inspector 中绑定。
        {
            backButton.onClick.AddListener(ShowLoginPanel); // 点击返回按钮时显示登录面板。
        }
    }

    private void RemoveButtonListeners() // 统一移除按钮事件。
    {
        if (registerButton != null) // 判断注册入口按钮是否存在。
        {
            registerButton.onClick.RemoveListener(ShowRegisterPanel); // 移除注册入口按钮的点击事件。
        }

        if (backButton != null) // 判断返回按钮是否存在。
        {
            backButton.onClick.RemoveListener(ShowLoginPanel); // 移除返回按钮的点击事件。
        }
    }

    public void ShowLoginPanel() // 显示登录面板。
    {
        SetPanelActive(loginPanel, true); // 打开登录面板。
        SetPanelActive(registerPanel, false); // 关闭注册面板。
    }

    public void ShowRegisterPanel() // 显示注册面板。
    {
        SetPanelActive(loginPanel, false); // 关闭登录面板。
        SetPanelActive(registerPanel, true); // 打开注册面板。
    }

    private void SetPanelActive(GameObject panel, bool isActive) // 设置指定面板的显示状态。
    {
        if (panel != null) // 判断面板对象是否已经在 Inspector 中绑定。
        {
            panel.SetActive(isActive); // 按传入状态显示或隐藏面板。
        }
    }
}
