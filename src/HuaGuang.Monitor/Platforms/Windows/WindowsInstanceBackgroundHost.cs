using System.Diagnostics;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text;
using HuaGuang.Monitor.Ipc;
using HuaGuang.Monitor.Models;
using HuaGuang.Monitor.Services;
using Microsoft.Win32;

namespace HuaGuang.Monitor.Platforms.Windows;

/// <summary>
/// 独立采集/订阅实例的后台只注册 Windows 服务，不写当前用户开机启动项。
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
        MonitorProcessInstance.RemoveIsolatedLoginStartup();
        var arguments = MonitorProcessInstance.FormatHostArguments(operationMode, lineName);
        if (autoStart)
        {
            DemotePrimaryAutoStart();
        }

        if (!TryApplyWindowsService(autoStart, arguments, out var error))
        {
            throw new InvalidOperationException(error);
        }

        if (autoStart)
        {
            EnsureRunning(arguments);
        }
    }

    public static bool EnsureRunning(TimeSpan? timeout = null) =>
        EnsureRunning(MonitorProcessInstance.FormatHostArguments(), timeout);

    static bool EnsureRunning(string arguments, TimeSpan? timeout = null)
    {
        _ = arguments;
        if (MonitorIpcClient.IsServiceAvailable())
        {
            return true;
        }

        if (!ServiceExists(MonitorProcessInstance.WindowsServiceName))
        {
            return false;
        }

        TryStartWindowsService();
        return MonitorIpcClient.WaitForServiceAvailable(timeout ?? TimeSpan.FromSeconds(8));
    }

    static bool TryApplyWindowsService(bool autoStart, string arguments, out string error)
    {
        error = string.Empty;
        var exe = ResolveServiceExePath();
        if (!File.Exists(exe))
        {
            error =
                $"未找到后台程序：{exe}。请确认安装目录下存在 service\\HuaGuang.Monitor.Service.exe。";
            return false;
        }

        var name = MonitorProcessInstance.WindowsServiceName;
        // DisplayName 避免特殊符号，减少 sc.exe 解析失败。
        var display = $"工业监控-{MonitorProcessInstance.FriendlyName}";
        var startType = autoStart ? "delayed-auto" : "demand";

        if (!ServiceExists(name))
        {
            if (!autoStart)
            {
                // 未勾选开机时不必创建服务，档案已写入 instance-hosts。
                return true;
            }

            // sc 对带空格路径：binPath= "\"C:\path\exe\" --args"
            var createArgs =
                $"create {name} binPath= \"\\\"{exe}\\\" {arguments}\" start= {startType} depend= Tcpip/Afd DisplayName= \"{display}\"";
            if (!TryExecSc(createArgs, allowElevate: true, out var createDetail, out var createCode))
            {
                error = FormatScFailure("创建", name, createCode, createDetail);
                return false;
            }

            TryExecSc(
                $"description {name} \"工业监控独立实例后台（{MonitorProcessInstance.Id}）\"",
                allowElevate: true,
                out _,
                out _);
            TryApplyRestartOnFailure(name);
            return true;
        }

        var configArgs =
            $"config {name} binPath= \"\\\"{exe}\\\" {arguments}\" start= {startType} depend= Tcpip/Afd DisplayName= \"{display}\"";
        if (!TryExecSc(configArgs, allowElevate: true, out var configDetail, out var configCode))
        {
            // 服务已存在但改配置失败时，开机项若本来就是 auto 仍可继续；仅在勾选开机时视为失败。
            if (autoStart)
            {
                error = FormatScFailure("配置", name, configCode, configDetail);
                return false;
            }
        }

        if (autoStart)
        {
            TryApplyRestartOnFailure(name);
        }

        return true;
    }

    static void TryApplyRestartOnFailure(string serviceName)
    {
        TryExecSc(
            $"failure {serviceName} reset= 86400 actions= restart/3000/restart/15000/restart/30000",
            allowElevate: true,
            out _,
            out _);
        TryExecSc($"failureflag {serviceName} 1", allowElevate: true, out _, out _);
    }

    static string FormatScFailure(string action, string serviceName, int exitCode, string detail)
    {
        var builder = new StringBuilder();
        builder.Append($"无法{action}本实例 Windows 服务 {serviceName}");
        if (exitCode != 0)
        {
            builder.Append($"（sc 退出码 {exitCode}）");
        }

        builder.Append('。');
        if (exitCode == 5 || IsAccessDeniedText(detail))
        {
            builder.Append("需要管理员权限：请右键「以管理员身份运行」本程序后再保存「开机自动启动本实例后台」，或在弹出的 UAC 框中点「是」。");
        }
        else if (!string.IsNullOrWhiteSpace(detail))
        {
            builder.Append(' ').Append(detail.Trim());
        }
        else
        {
            builder.Append("独立实例开机只使用服务，不会写入登录启动项。");
        }

        if (!IsProcessElevated())
        {
            builder.Append("（当前窗口未提权）");
        }

        return builder.ToString();
    }

    static bool IsAccessDeniedText(string detail) =>
        detail.Contains("拒绝访问", StringComparison.OrdinalIgnoreCase)
        || detail.Contains("Access is denied", StringComparison.OrdinalIgnoreCase);

    static bool IsProcessElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    static void DemotePrimaryAutoStart()
    {
        try
        {
            TryExecSc($"config {MonitorIpcConstants.DefaultServiceName} start= demand", allowElevate: false, out _, out _);
            TryExecSc($"stop {MonitorIpcConstants.DefaultServiceName}", allowElevate: false, out _, out _);
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
            return TryExecSc(
                $"start {MonitorProcessInstance.WindowsServiceName}",
                allowElevate: true,
                out _,
                out var code) && code == 0;
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

    static string ResolveServiceExePath() => WindowsBackgroundRuntimeLauncher.ResolveServiceExePath();

    static bool TryExecSc(string arguments, bool allowElevate, out string detail, out int exitCode)
    {
        detail = string.Empty;
        exitCode = -1;
        if (TryExecScCore(arguments, elevate: false, out detail, out exitCode) && exitCode == 0)
        {
            return true;
        }

        var shouldElevate = allowElevate
            && !IsProcessElevated()
            && (exitCode is 5 or -1 || IsAccessDeniedText(detail));
        if (shouldElevate)
        {
            if (TryExecScCore(arguments, elevate: true, out var elevatedDetail, out exitCode) && exitCode == 0)
            {
                detail = elevatedDetail;
                return true;
            }

            if (!string.IsNullOrWhiteSpace(elevatedDetail))
            {
                detail = elevatedDetail;
            }
        }

        return exitCode == 0;
    }

    static bool TryExecScCore(string arguments, bool elevate, out string detail, out int exitCode)
    {
        detail = string.Empty;
        exitCode = -1;
        try
        {
            var sc = Path.Combine(Environment.SystemDirectory, "sc.exe");
            if (elevate)
            {
                using var elevated = Process.Start(new ProcessStartInfo
                {
                    FileName = sc,
                    Arguments = arguments,
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                });
                if (elevated is null)
                {
                    detail = "用户取消了管理员授权，或无法弹出 UAC。";
                    return false;
                }

                elevated.WaitForExit(60_000);
                exitCode = elevated.ExitCode;
                detail = exitCode == 0 ? string.Empty : $"提权 sc 退出码 {exitCode}";
                return true;
            }

            using var normal = Process.Start(new ProcessStartInfo
            {
                FileName = sc,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            if (normal is null)
            {
                detail = "无法启动 sc.exe。";
                return false;
            }

            detail = (normal.StandardOutput.ReadToEnd() + normal.StandardError.ReadToEnd()).Trim();
            normal.WaitForExit(20_000);
            exitCode = normal.ExitCode;
            return true;
        }
        catch (Exception ex)
        {
            // 用户在 UAC 点「否」时常见 Win32Exception。
            detail = ex.Message;
            exitCode = -1;
            return false;
        }
    }
}
