using System.Diagnostics;
using System.Text;
using HuaGuang.Monitor.Services;

namespace HuaGuang.Monitor.Platforms.Windows;

/// <summary>防止重复启动；以真实进程为准，避免 Global Mutex 误拦（无界面却在占用）。</summary>
static class WindowsSingleInstanceGuard
{
    const string ProcessBaseName = "HuaGuang.Monitor";
    static readonly string LockFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HuaGuang.Monitor",
        "ui-instance.lock");

    static Mutex? _mutex;
    static FileStream? _lockStream;

    public static bool TryAcquirePrimaryInstance()
    {
        MonitorProcessInstance.Initialize();
        if (MonitorProcessInstance.IsIsolated)
        {
            return TryAcquireNamedInstance(MonitorProcessInstance.Id!);
        }

        if (TryFindPeerUiProcess(out var peerPid))
        {
            var activated = WindowsUiActivation.TryActivateExistingInstance();
            StartupBootstrapLog.Write(
                activated
                    ? $"WinUI.App: peer UI running (PID {peerPid}), brought window to front"
                    : $"WinUI.App: peer UI running (PID {peerPid}), could not activate, exit");
            return false;
        }

        if (!TryAcquireLockFile())
        {
            if (TryFindPeerUiProcess(out peerPid))
            {
                WindowsUiActivation.TryActivateExistingInstance();
                StartupBootstrapLog.Write($"WinUI.App: ui lock in use and peer running (PID {peerPid}), exit");
                return false;
            }

            if (TryReadLockOwnerPid(out var lockPid) && IsProcessAlive(lockPid))
            {
                WindowsUiActivation.TryActivateExistingInstance();
                StartupBootstrapLog.Write($"WinUI.App: ui lock held by live PID {lockPid} (not yet visible as {ProcessBaseName}), exit");
                return false;
            }

            StartupBootstrapLog.Write("WinUI.App: stale ui lock without live owner, reclaiming");
            TryDeleteLockFile();
            if (!TryAcquireLockFile())
            {
                StartupBootstrapLog.Write("WinUI.App: could not acquire ui lock after reclaim, exit");
                return false;
            }
        }

        try
        {
            _mutex = new Mutex(
                initiallyOwned: true,
                name: @"Local\HuaGuang.Monitor.Ui.SingleInstance",
                createdNew: out var createdNew);
            if (!createdNew && TryFindPeerUiProcess(out peerPid))
            {
                ReleaseLockFile();
                _mutex.Dispose();
                _mutex = null;
                StartupBootstrapLog.Write($"WinUI.App: Local mutex held and peer running (PID {peerPid}), exit");
                return false;
            }
        }
        catch (Exception ex)
        {
            StartupBootstrapLog.Write("WinUI.App: Local mutex advisory failed (continue)", ex);
        }

        return true;
    }

    static bool TryFindPeerUiProcess(out int peerPid)
    {
        peerPid = 0;
        var self = Environment.ProcessId;
        var isolatedPids = ListIsolatedInstancePids();
        try
        {
            foreach (var process in Process.GetProcessesByName(ProcessBaseName))
            {
                try
                {
                    if (process.Id == self || process.HasExited || isolatedPids.Contains(process.Id))
                    {
                        continue;
                    }

                    // 锁文件偶发读不到时，仍按命令行 --instance 排除独立窗口，避免主程序误退出。
                    if (IsIsolatedHostProcess(process.Id))
                    {
                        continue;
                    }

                    peerPid = process.Id;
                    return true;
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
        catch
        {
        }

        return false;
    }

    static bool TryAcquireNamedInstance(string instanceId)
    {
        var lockPath = Path.Combine(
            WindowsSharedDataDirectory.Resolve(),
            "instances",
            instanceId,
            "ui-instance.lock");

        if (!TryAcquireLockFileAt(lockPath))
        {
            var activated = WindowsUiActivation.TryActivateExistingInstance();
            StartupBootstrapLog.Write(
                activated
                    ? $"WinUI.App: instance '{instanceId}' already running, brought window to front"
                    : $"WinUI.App: instance '{instanceId}' lock in use, could not activate, exit");
            return false;
        }

        try
        {
            _mutex = new Mutex(
                initiallyOwned: true,
                name: $@"Local\HuaGuang.Monitor.Ui.SingleInstance.{instanceId}",
                createdNew: out var createdNew);
            if (!createdNew)
            {
                ReleaseLockFile();
                _mutex.Dispose();
                _mutex = null;
                WindowsUiActivation.TryActivateExistingInstance();
                StartupBootstrapLog.Write($"WinUI.App: instance '{instanceId}' mutex held, exit");
                return false;
            }
        }
        catch (Exception ex)
        {
            StartupBootstrapLog.Write("WinUI.App: instance mutex advisory failed (continue)", ex);
        }

        StartupBootstrapLog.Write($"WinUI.App: isolated instance '{instanceId}' acquired");
        return true;
    }

    static HashSet<int> ListIsolatedInstancePids()
    {
        var pids = new HashSet<int>();
        try
        {
            var root = Path.Combine(WindowsSharedDataDirectory.Resolve(), "instances");
            if (!Directory.Exists(root))
            {
                return pids;
            }

            foreach (var lockFile in Directory.EnumerateFiles(root, "ui-instance.lock", SearchOption.AllDirectories))
            {
                if (TryReadLockOwnerPidFrom(lockFile, out var pid) && pid > 0 && pid != Environment.ProcessId && IsProcessAlive(pid))
                {
                    pids.Add(pid);
                }
            }
        }
        catch
        {
        }

        return pids;
    }

    /// <summary>
    /// 独立窗口启动参数含 <c>--instance</c>；主程序不应把它当成「已有主 UI」。
    /// </summary>
    static bool IsIsolatedHostProcess(int pid)
    {
        if (!TryGetProcessCommandLine(pid, out var commandLine) || string.IsNullOrWhiteSpace(commandLine))
        {
            return false;
        }

        return CommandLineContainsInstanceSwitch(commandLine);
    }

    internal static bool CommandLineContainsInstanceSwitch(string commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return false;
        }

        // 匹配 --instance / --instance=id，避免误伤路径里偶然出现的 instance 字样。
        for (var i = 0; i < commandLine.Length; i++)
        {
            if (commandLine[i] != '-')
            {
                continue;
            }

            if (i > 0 && !char.IsWhiteSpace(commandLine[i - 1]) && commandLine[i - 1] != '"')
            {
                continue;
            }

            if (commandLine.AsSpan(i).StartsWith("--instance", StringComparison.OrdinalIgnoreCase))
            {
                var after = i + "--instance".Length;
                if (after >= commandLine.Length
                    || char.IsWhiteSpace(commandLine[after])
                    || commandLine[after] == '='
                    || commandLine[after] == '"')
                {
                    return true;
                }
            }
        }

        return false;
    }

    static bool TryGetProcessCommandLine(int pid, out string? commandLine)
    {
        commandLine = null;
        try
        {
            // 用 WMI 脚本 COM，避免额外引用 System.Management。
            var locatorType = Type.GetTypeFromProgID("WbemScripting.SWbemLocator");
            if (locatorType is null)
            {
                return false;
            }

            dynamic locator = Activator.CreateInstance(locatorType)!;
            dynamic services = locator.ConnectServer(".", @"root\cimv2");
            dynamic results = services.ExecQuery(
                $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {pid}");
            foreach (dynamic proc in results)
            {
                commandLine = proc.CommandLine as string;
                return !string.IsNullOrWhiteSpace(commandLine);
            }
        }
        catch
        {
        }

        return false;
    }

    static bool TryAcquireLockFile() => TryAcquireLockFileAt(LockFilePath);

    static bool TryAcquireLockFileAt(string lockFilePath)
    {
        try
        {
            var directory = Path.GetDirectoryName(lockFilePath)!;
            Directory.CreateDirectory(directory);

            _lockStream?.Dispose();
            // FileShare.Read：其它进程仍可读 PID；写权限仍互斥，避免双主实例。
            _lockStream = new FileStream(
                lockFilePath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.Read);

            _lockStream.SetLength(0);
            var payload = Encoding.UTF8.GetBytes($"{Environment.ProcessId}{Environment.NewLine}{DateTimeOffset.Now:O}");
            _lockStream.Write(payload, 0, payload.Length);
            _lockStream.Flush(true);
            return true;
        }
        catch (IOException)
        {
            _lockStream?.Dispose();
            _lockStream = null;
            return false;
        }
        catch (Exception ex)
        {
            StartupBootstrapLog.Write("WinUI.App: ui lock file error (allow start)", ex);
            return true;
        }
    }

    static void ReleaseLockFile()
    {
        try
        {
            _lockStream?.Dispose();
            _lockStream = null;
        }
        catch
        {
        }
    }

    static void TryDeleteLockFile()
    {
        ReleaseLockFile();
        try
        {
            if (File.Exists(LockFilePath))
            {
                File.Delete(LockFilePath);
            }
        }
        catch
        {
        }
    }

    static bool TryReadLockOwnerPid(out int pid) => TryReadLockOwnerPidFrom(LockFilePath, out pid);

    static bool TryReadLockOwnerPidFrom(string lockFilePath, out int pid)
    {
        pid = 0;
        try
        {
            if (!File.Exists(lockFilePath))
            {
                return false;
            }

            using var stream = new FileStream(
                lockFilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var firstLine = reader.ReadLine();
            return int.TryParse(firstLine, out pid);
        }
        catch
        {
            return false;
        }
    }

    static bool IsProcessAlive(int pid)
    {
        if (pid <= 0 || pid == Environment.ProcessId)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }
}
