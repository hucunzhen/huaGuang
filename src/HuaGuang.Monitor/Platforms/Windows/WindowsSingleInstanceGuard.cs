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

    static bool TryAcquireLockFile() => TryAcquireLockFileAt(LockFilePath);

    static bool TryAcquireLockFileAt(string lockFilePath)
    {
        try
        {
            var directory = Path.GetDirectoryName(lockFilePath)!;
            Directory.CreateDirectory(directory);

            _lockStream?.Dispose();
            _lockStream = new FileStream(
                lockFilePath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);

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

            var firstLine = File.ReadLines(lockFilePath).FirstOrDefault();
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
