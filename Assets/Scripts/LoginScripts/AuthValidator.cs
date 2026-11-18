using System.Text.RegularExpressions;

public static class AuthValidator
{
    private const int UsernameMinLength = 3;
    private const int UsernameMaxLength = 32;
    private const int CodeMinLength = 4;
    private const int CodeMaxLength = 6;

    private const int PasswordMinLength = 8;
    private const int PasswordMaxLength = 128;
    private static readonly Regex PhoneRegex = new Regex("^1[3-9]\\d{9}$", RegexOptions.CultureInvariant);
    private static readonly Regex EmailRegex = new Regex("^[^@\\s]+@[^@\\s]+\\.[^@\\s]+$", RegexOptions.CultureInvariant);
    private static readonly Regex CodeRegex = new Regex("^\\d+$", RegexOptions.CultureInvariant);

    public static string GetLoginError(string username, string password)
    {
        string usernameError = GetUsernameError(username);
        if (!string.IsNullOrEmpty(usernameError))
        {
            return usernameError;
        }

        return GetPasswordError(password);
    }

    public static string GetUsernameError(string username)
    {
        username = Normalize(username);

        if (string.IsNullOrEmpty(username))
        {
            return "请输入用户名";
        }

        if (username.Length < UsernameMinLength || username.Length > UsernameMaxLength)
        {
            return "用户名长度须为 3-32 个字符";
        }

        return "";
    }

    public static string GetPasswordError(string password)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            return "请输入密码";
        }

        if (password.Length < PasswordMinLength || password.Length > PasswordMaxLength)
        {
            return "密码长度须为 8-128 个字符";
        }

        return "";
    }

    public static string GetConfirmPasswordError(string password, string confirmPassword)
    {
        if (string.IsNullOrWhiteSpace(confirmPassword))
        {
            return "请确认密码";
        }

        if (password != confirmPassword)
        {
            return "两次输入的密码不一致";
        }

        return "";
    }

    public static string GetRegisterError(string username, string password, string confirmPassword)
    {
        string usernameError = GetUsernameError(username);
        if (!string.IsNullOrEmpty(usernameError))
        {
            return usernameError;
        }

        string passwordError = GetPasswordError(password);
        if (!string.IsNullOrEmpty(passwordError))
        {
            return passwordError;
        }

        return GetConfirmPasswordError(password, confirmPassword);
    }

    public static string GetPhoneError(string phone)
    {
        phone = Normalize(phone);

        if (string.IsNullOrEmpty(phone))
        {
            return "请输入手机号";
        }

        if (!PhoneRegex.IsMatch(phone))
        {
            return "手机号无效";
        }

        return "";
    }

    public static string GetEmailError(string email)
    {
        email = Normalize(email);

        if (string.IsNullOrEmpty(email))
        {
            return "请输入邮箱";
        }

        if (email.Length > 64 || !EmailRegex.IsMatch(email))
        {
            return "邮箱无效";
        }

        return "";
    }

    public static string GetCodeError(string code)
    {
        code = Normalize(code);

        if (string.IsNullOrEmpty(code))
        {
            return "请输入验证码";
        }

        if (code.Length < CodeMinLength || code.Length > CodeMaxLength || !CodeRegex.IsMatch(code))
        {
            return "验证码须为 4-6 位数字";
        }

        return "";
    }

    public static string GetPhoneRegisterError(string phone, string code, string password, string confirmPassword)
    {
        string phoneError = GetPhoneError(phone);
        if (!string.IsNullOrEmpty(phoneError))
        {
            return phoneError;
        }

        string codeError = GetCodeError(code);
        if (!string.IsNullOrEmpty(codeError))
        {
            return codeError;
        }

        return GetRegisterPasswordError(password, confirmPassword);
    }

    public static string GetEmailRegisterError(string email, string code, string password, string confirmPassword)
    {
        string emailError = GetEmailError(email);
        if (!string.IsNullOrEmpty(emailError))
        {
            return emailError;
        }

        string codeError = GetCodeError(code);
        if (!string.IsNullOrEmpty(codeError))
        {
            return codeError;
        }

        return GetRegisterPasswordError(password, confirmPassword);
    }

    public static string Normalize(string value)
    {
        return value == null ? "" : value.Trim();
    }

    private static string GetRegisterPasswordError(string password, string confirmPassword)
    {
        string passwordError = GetPasswordError(password);
        if (!string.IsNullOrEmpty(passwordError))
        {
            return passwordError;
        }

        return GetConfirmPasswordError(password, confirmPassword);
    }
}
