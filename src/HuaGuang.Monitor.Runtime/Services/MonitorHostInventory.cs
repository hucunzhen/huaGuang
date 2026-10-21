using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using HuaGuang.Monitor.Ipc;
using HuaGuang.Monitor.Services.Watchdog;

namespace HuaGuang.Monitor.Services;

/// <summary>诊断用：列出工业监控相关 Windows 服务与进程 PID。</summary>
public static class MonitorHostInventory
{
    public static string FormatReport()
    {
        if (!OperatingSystem.IsWindows())
        {
            return "当前平台不支持查看 Windows 服务/进程清单。";
        }

        var builder = new StringBuilder();
        builder.AppendLine($"本窗口进程 PID {Environment.ProcessId}");
        if (MonitorProcessInstance.IsIsolated)
        {
            builder.AppendLine(
                $"本实例 {MonitorProcessInstance.FriendlyName}（{MonitorProcessInstance.Id}） · IPC 端口 {MonitorProcessInstance.IpcTcpPort}");
        }

        builder.AppendLine();
        builder.AppendLine("Windows 服务：");
        foreach (var serviceName in EnumerateServiceNames())
        {
            AppendServiceLine(builder, serviceName);
        }

        builder.AppendLine();
        builder.AppendLine("相关进程：");
        AppendProcessLines(builder, WatchdogConstants.UiProcessName, "界面");
        AppendProcessLines(builder, WatchdogConstants.AcquisitionProcessName, "采集后台");
        AppendProcessLines(builder, WatchdogConstants.WatchdogProcessName, "守护");

        builder.AppendLine();
        builder.AppendLine("数据实例目录（内部 id，不是 PID）：");
        AppendInstanceDirectories(builder);

        var runKeys = ListIsolatedLoginStartupKeys();
        if (runKeys.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("登录启动项（不应再存在，会与服务抢 PLC）：");
            foreach (var key in runKeys)
            {
                builder.AppendLine($"  · {key}");
            }
        }

        return builder.ToString().TrimEnd();
    }

    static IEnumerable<string> EnumerateServiceNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            MonitorIpcConstants.DefaultServiceName,
            WatchdogConstants.WatchdogWindowsServiceName
        };

        names.Add(MonitorProcessInstance.WindowsServiceNameFor("sub"));
        foreach (var line in LineCatalog.LineNames)
        {
            names.Add(MonitorProcessInstance.WindowsServiceNameFor("acq-" + LineCatalog.ToCommandAlias(line)));
        }

        foreach (var profile in InstanceHostProfileStore.ListAutoStart())
        {
            if (MonitorProcessInstance.TrySanitize(profile.InstanceId, out var id, out _))
            {
                names.Add(MonitorProcessInstance.WindowsServiceNameFor(id));
            }
        }

        return names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase);
    }

    static void AppendServiceLine(StringBuilder builder, string serviceName)
    {
        if (!TryQueryService(serviceName, out var state, out var pid))
        {
            return;
        }

        if (string.Equals(state, "未安装", StringComparison.Ordinal))
        {
            return;
        }

        var prefix = MonitorIpcConstants.DefaultServiceName + "-";
        var instanceId = serviceName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? serviceName[prefix.Length..]
            : null;
        var label = instanceId is null
            ? serviceName
            : $"{MonitorProcessInstance.FormatFriendlyName(instanceId)}  {serviceName}";
        if (pid > 0)
        {
            builder.AppendLine($"  · {label}  {state}  PID {pid}");
        }
        else
        {
            builder.AppendLine($"  · {label}  {state}");
        }
    }

    static void AppendProcessLines(StringBuilder builder, string processName, string label)
    {
        try
        {
            var processes = Process.GetProcessesByName(processName)
                .OrderBy(process => process.Id)
                .ToList();
            if (processes.Count == 0)
            {
                builder.AppendLine($"  · {label}（{processName}）无");
                return;
            }

            foreach (var process in processes)
            {
                try
                {
                    using (process)
                    {
                        builder.AppendLine($"  · {label}  PID {process.Id}  {processName}.exe");
                    }
                }
                catch
                {
                    // 进程可能已退出
                }
            }
        }
        catch
        {
            builder.AppendLine($"  · {label}（{processName}）查询失败");
        }
    }

    static void AppendInstanceDirectories(StringBuilder builder)
    {
        try
        {
            var root = Path.Combine(AppPaths.SharedDataDirectory, "instances");
            if (!Directory.Exists(root))
            {
                builder.AppendLine("  · （无）");
                return;
            }

            foreach (var directory in Directory.GetDirectories(root).OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
            {
                var id = Path.GetFileName(directory);
                builder.AppendLine($"  · {MonitorProcessInstance.FormatFriendlyName(id)}  目录 {id}");
            }
        }
        catch (Exception ex)
        {
            builder.AppendLine($"  · 读取失败：{ex.Message}");
        }
    }

    static bool TryQueryService(string serviceName, out string state, out int pid)
    {
        state = "未安装";
        pid = 0;
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "sc.exe"),
                Arguments = $"queryex \"{serviceName}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process is null)
            {
                return false;
            }

            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit(8_000);
            if (output.Contains("1060", StringComparison.Ordinal)
                || output.Contains("does not exist", StringComparison.OrdinalIgnoreCase)
                || output.Contains("未安装", StringComparison.Ordinal))
            {
                state = "未安装";
                return true;
            }

            var stateMatch = Regex.Match(output, @"STATE\s*:\s*\d+\s+(\w+)", RegexOptions.IgnoreCase);
            state = stateMatch.Success ? stateMatch.Groups[1].Value.ToUpperInvariant() : "未知";
            var pidMatch = Regex.Match(output, @"PID\s*:\s*(\d+)", RegexOptions.IgnoreCase);
            if (pidMatch.Success)
            {
                int.TryParse(pidMatch.Groups[1].Value, out pid);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    static List<string> ListIsolatedLoginStartupKeys()
    {
        var result = new List<string>();
        if (!OperatingSystem.IsWindows())
        {
            return result;
        }

        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Run",
                writable: false);
            if (key is null)
            {
                return result;
            }

            foreach (var name in key.GetValueNames())
            {
                if (name.StartsWith("IndustrialMonitor.", StringComparison.OrdinalIgnoreCase))
                {
                    var value = key.GetValue(name)?.ToString() ?? string.Empty;
                    result.Add(string.IsNullOrWhiteSpace(value) ? name : $"{name} = {value}");
                }
            }
        }
        catch
        {
        }

        return result;
    }
}
