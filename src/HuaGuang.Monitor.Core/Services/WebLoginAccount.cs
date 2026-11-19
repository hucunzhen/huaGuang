using HuaGuang.Monitor.Models;

namespace HuaGuang.Monitor.Services;

/// <summary>订阅网页登录帐号。默认密码固定，界面上直接显示，重置后回到这个密码。</summary>
public static class WebLoginAccount
{
    public const string DefaultUsername = "admin";
    public const string DefaultPassword = "123456";

    public static void Ensure(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.WebLoginUsername))
        {
            settings.WebLoginUsername = DefaultUsername;
        }

        if (string.IsNullOrEmpty(settings.WebLoginPassword))
        {
            settings.WebLoginPassword = DefaultPassword;
        }
    }

    public static void ResetPassword(AppSettings settings)
    {
        Ensure(settings);
        settings.WebLoginPassword = DefaultPassword;
    }

    public static bool Matches(AppSettings settings, string? username, string? password)
    {
        Ensure(settings);
        return string.Equals(settings.WebLoginUsername.Trim(), (username ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase)
            && string.Equals(settings.WebLoginPassword, password ?? string.Empty, StringComparison.Ordinal);
    }
}
