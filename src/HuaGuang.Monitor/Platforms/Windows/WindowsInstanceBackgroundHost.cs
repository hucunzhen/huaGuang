using System.Diagnostics;
using System.ServiceProcess;
using HuaGuang.Monitor.Ipc;
using HuaGuang.Monitor.Models;
using HuaGuang.Monitor.Services;
using Microsoft.Win32;

namespace HuaGuang.Monitor.Platforms.Windows;

/// <summary>
/// 独立采集/订阅实例的后台：优先注册 Windows 服务（开机自动），失败则写入当前用户开机启动项。
/// </summary>
public static class WindowsInstanceBackgroundHost
{
    public static void Apply(bool autoStart, AppOperationMode operationMode, string lineName)
    {
        if (!MonitorProcessInstance.IsIsolated)
        {
            return;
        }

        InstanceHostProfileStore.Save(MonitorProcessInstance.Id!, lineName, operationMode, autoStart);
        var arguments = MonitorProcessInstance.FormatHostArguments(operationMode, lineName);
        if (autoStart)
        {
            DemotePrimaryAutoStart();
        }

        if (TryApplyWindowsService(autoStart, arguments))
        {
            RemoveRunKey();
            if (autoStart)
            {
                EnsureRunning(arguments);
            }

            return;
        }

        ApplyRunKey(autoStart, arguments);
        if (autoStart)
        {
            EnsureRunning(arguments);
        }
    }

    public static bool EnsureRunning(TimeSpan? timeout = null) =>
        EnsureRunning(MonitorProcessInstance.FormatHostArguments(), timeout);

    static bool EnsureRunning(string arguments, TimeSpan? timeout = null)
    {
        if (MonitorIpcClient.IsServiceAvailable())
        {
            return true;
        }

        TryStartWindowsService();
        if (MonitorIpcClient.WaitForServiceAvailable(TimeSpan.FromSeconds(20)))
        {
            return true;
        }

        if (!TryLaunchProcess(arguments))
        {
            return MonitorIpcClient.IsServiceAvailable();
        }

        return MonitorIpcClient.WaitForServiceAvailable(timeout ?? TimeSpan.FromSeconds(45));
    }

    static bool TryApplyWindowsService(bool autoStart, string arguments)
    {
        var exe = ResolveServiceExePath();
        if (!File.Exists(exe))
        {
            return false;
        }

        var name = MonitorProcessInstance.WindowsServiceName;
        var display = MonitorProcessInstance.WindowsServiceDisplayName;
        var binPath = $"\\\"{exe}\\\" {arguments}";
        var startType = autoStart ? "auto" : "demand";
        if (!ServiceExists(name))
        {
            var create = ExecSc($"create {name} binPath= \"{binPath}\" start= {startType} DisplayName= \"{display}\"");
            if (create != 0)
            {
                return false;
            }

            ExecSc($"description {name} \"工业监控独立实例后台（{MonitorProcessInstance.Id}）\"");
        }
        else
        {
            ExecSc($"config {name} binPath= \"{binPath}\" start= {startType} DisplayName= \"{display}\"");
        }

        return true;
    }

    static void DemotePrimaryAutoStart()
    {
        try
        {
            ExecSc($"config {MonitorIpcConstants.DefaultServiceName} start= demand");
            ExecSc($"stop {MonitorIpcConstants.DefaultServiceName}");
        }
        catch
        {
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            key?.DeleteValue(StartupRegistration.RegistryValueName, throwOnMissingValue: false);
        }
        catch
        {
        }

        try
        {
            PrimaryUiAutoStartStore.Set(false);
        }
        catch
        {
        }
    }

    static bool TryStartWindowsService()
    {
        try
        {
            using var service = new ServiceController(MonitorProcessInstance.WindowsServiceName);
            if (service.Status == ServiceControllerStatus.Running)
            {
                return true;
            }

            service.Start();
            service.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(40));
            return true;
        }
        catch
        {
            return ExecSc($"start {MonitorProcessInstance.WindowsServiceName}") == 0;
        }
    }

    static bool ServiceExists(string name)
    {
        try
        {
            using var service = new ServiceController(name);
            _ = service.Status;
            return true;
        }
        catch
        {
            return false;
        }
    }

    static void ApplyRunKey(bool enabled, string arguments)
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true)
            ?? Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true)
            ?? throw new InvalidOperationException("无法写入开机启动注册表。");

        var valueName = MonitorProcessInstance.StartupRegistryValueName;
        if (!enabled)
        {
            key.DeleteValue(valueName, throwOnMissingValue: false);
            return;
        }

        var exe = ResolveServiceExePath();
        if (!File.Exists(exe))
        {
            throw new InvalidOperationException("未找到后台服务程序，无法设置本实例开机启动。");
        }

        key.SetValue(valueName, $"\"{exe}\" {arguments}");
    }

    static void RemoveRunKey()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            key?.DeleteValue(MonitorProcessInstance.StartupRegistryValueName, throwOnMissingValue: false);
        }
        catch
        {
        }
    }

    static bool TryLaunchProcess(string arguments)
    {
        var exe = ResolveServiceExePath();
        if (!File.Exists(exe))
        {
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = arguments,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            });
            return true;
        }
        catch
        {
            return false;
        }
    }

    static string ResolveServiceExePath() => WindowsBackgroundRuntimeLauncher.ResolveServiceExePath();

    static int ExecSc(string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "sc.exe"),
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            if (process is null)
            {
                return -1;
            }

            process.WaitForExit(20_000);
            return process.ExitCode;
        }
        catch
        {
            return -1;
        }
    }
}
