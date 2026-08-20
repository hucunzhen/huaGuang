using HuaGuang.Monitor.Services;
using Microsoft.Win32;

namespace HuaGuang.Monitor.Platforms.Windows;

public sealed class WindowsStartupRegistration : IStartupRegistration
{
    const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public bool IsSupported => true;

    public bool IsRegistered
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
            return key?.GetValue(StartupRegistration.RegistryValueName) is string;
        }
    }

    public void Apply(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
            ?? throw new InvalidOperationException("无法写入开机启动注册表。");

        if (!enabled)
        {
            key.DeleteValue(StartupRegistration.RegistryValueName, throwOnMissingValue: false);
            return;
        }

        var exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
        {
            throw new InvalidOperationException("无法确定程序路径，无法设置开机启动。");
        }

        key.SetValue(StartupRegistration.RegistryValueName, Quote(exePath));
    }

    static string Quote(string path) => path.Contains('"') ? path : $"\"{path}\"";
}
