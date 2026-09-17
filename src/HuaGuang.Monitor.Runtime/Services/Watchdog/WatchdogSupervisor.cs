using System.Diagnostics;
using HuaGuang.Monitor.Ipc;
using HuaGuang.Monitor.Models;
using Microsoft.Extensions.Logging;

namespace HuaGuang.Monitor.Services.Watchdog;

public sealed class WatchdogSupervisor
{
    readonly SettingsStore _settings;
    readonly ILogger<WatchdogSupervisor> _logger;
    readonly string _installRoot;
    readonly Func<bool>? _shouldWatchUi;
    DateTimeOffset _lastUiRestartUtc = DateTimeOffset.MinValue;
    DateTimeOffset _lastAcquisitionServiceRestartUtc = DateTimeOffset.MinValue;

    public WatchdogSupervisor(
        SettingsStore settings,
        ILogger<WatchdogSupervisor> logger,
        string installRoot,
        Func<bool>? shouldWatchUi = null)
    {
        _settings = settings;
        _logger = logger;
        _installRoot = installRoot;
        _shouldWatchUi = shouldWatchUi;
    }

    public async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        var options = WatchdogOptions.Load();
        if (!options.Enabled)
        {
            return;
        }

        if (options.ProtectAcquisitionService)
        {
            await EnsureAcquisitionWindowsServiceAsync(options, cancellationToken).ConfigureAwait(false);
        }

        if (options.EnsureAcquisitionRunning)
        {
            await EnsureAcquisitionOrSubscribeRunningAsync(cancellationToken).ConfigureAwait(false);
        }

        if (options.ProtectUi)
        {
            EnsureUiProcess(options);
        }
    }

    /// <summary>仅巡检并尝试拉起 UI（供采集服务在未安装独立守护服务时使用）。</summary>
    public Task RunUiCycleAsync(CancellationToken cancellationToken)
    {
        var options = WatchdogOptions.Load();
        if (options.Enabled && options.ProtectUi)
        {
            EnsureUiProcess(options);
        }

        return Task.CompletedTask;
    }

    async Task EnsureAcquisitionWindowsServiceAsync(WatchdogOptions options, CancellationToken cancellationToken)
    {
        if (IsWindowsServiceRunning(MonitorIpcConstants.ServiceName))
        {
            return;
        }

        if (!IsCooldownElapsed(_lastAcquisitionServiceRestartUtc, options.RestartCooldownSeconds))
        {
            return;
        }

        _logger.LogWarning("采集 Windows 服务未运行，尝试启动 {ServiceName}", MonitorIpcConstants.ServiceName);
        if (TryStartWindowsService(MonitorIpcConstants.ServiceName))
        {
            _lastAcquisitionServiceRestartUtc = DateTimeOffset.UtcNow;
            await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
        }
    }

    async Task EnsureAcquisitionOrSubscribeRunningAsync(CancellationToken cancellationToken)
    {
        if (!MonitorIpcClient.IsServiceAvailable())
        {
            return;
        }

        try
        {
            await _settings.LoadAsyncIfChanged().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "守护服务读取配置失败");
            return;
        }

        if (!_settings.Current.AutoStartAcquisition)
        {
            return;
        }

        var client = new MonitorIpcClient(MonitorIpcClient.DefaultTimeout);
        MonitorIpcResponse statusResponse;
        try
        {
            statusResponse = await client.SendAsync(
                new MonitorIpcRequest { Command = MonitorIpcCommand.GetStatus },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "GetStatus 失败");
            return;
        }

        if (!statusResponse.Success || statusResponse.State?.IsRunning == true)
        {
            return;
        }

        var mode = _settings.Current.OperationMode == AppOperationMode.Subscribe
            ? nameof(AppOperationMode.Subscribe)
            : nameof(AppOperationMode.Acquisition);

        _logger.LogWarning("采集/订阅未运行但已启用自动运行，尝试 IPC Start mode={Mode}", mode);
        try
        {
            var startResponse = await client.SendAsync(
                new MonitorIpcRequest
                {
                    Command = MonitorIpcCommand.Start,
                    OperationMode = mode
                },
                MonitorIpcClient.CommandTimeout,
                cancellationToken).ConfigureAwait(false);
            if (!startResponse.Success)
            {
                _logger.LogWarning("IPC Start 失败: {Error}", startResponse.Error);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "IPC Start 异常");
        }
    }

    void EnsureUiProcess(WatchdogOptions options)
    {
        if (IsProcessRunning(WatchdogConstants.UiProcessName))
        {
            return;
        }

        var registryWatch = _shouldWatchUi?.Invoke() == true;
        if (!ShouldWatchUi())
        {
            var msg = "跳过 UI 重启：未启用监视（无开机自启且 24h 内无 UI 心跳）";
            _logger.LogDebug(msg);
            WatchdogDiagLog.Write(msg);
            return;
        }

        var uiState = WatchdogStateStore.Read(WatchdogConstants.UiRole);
        if (WasRecentGracefulShutdown(uiState, options.GracefulShutdownWindowSeconds))
        {
            var msg = $"跳过 UI 重启：刚正常关闭（{options.GracefulShutdownWindowSeconds}s 内）";
            _logger.LogDebug(msg);
            WatchdogDiagLog.Write(msg);
            return;
        }

        if (!IsCooldownElapsed(_lastUiRestartUtc, options.RestartCooldownSeconds))
        {
            return;
        }

        if (uiState is not null && WasIntentionalUiExit(uiState))
        {
            var msg = "跳过 UI 重启：用户通过「退出程序」正常结束（非强杀）";
            _logger.LogDebug(msg);
            WatchdogDiagLog.Write(msg);
            return;
        }

        if (uiState is null && !registryWatch)
        {
            var msg = "跳过 UI 重启：无 UI 心跳记录（请先正常运行一次界面）";
            _logger.LogDebug(msg);
            WatchdogDiagLog.Write(msg);
            return;
        }

        if (OperatingSystem.IsWindows() && WindowsUiWatchPolicy.ShouldDeferUiLaunchToStartupRegistry(uiState))
        {
            var msg = "跳过 UI 重启：已启用开机自启，等待系统 Run 项在登录时启动（开机 3 分钟内不重复拉起）";
            _logger.LogDebug(msg);
            WatchdogDiagLog.Write(msg);
            return;
        }

        if (registryWatch)
        {
            Thread.Sleep(TimeSpan.FromSeconds(8));
            if (IsProcessRunning(WatchdogConstants.UiProcessName))
            {
                return;
            }
        }

        var uiPath = Path.Combine(_installRoot, WatchdogConstants.UiExeFileName);
        if (!File.Exists(uiPath))
        {
            _logger.LogWarning("未找到界面程序: {Path}", uiPath);
            return;
        }

        var attemptMsg = $"检测到界面异常退出，尝试在用户会话中重启: {uiPath} (dataDir={AppPaths.UserDataDirectory})";
        _logger.LogWarning(attemptMsg);
        WatchdogDiagLog.Write(attemptMsg);
        if (TryStartUi(uiPath, out var startError))
        {
            _lastUiRestartUtc = DateTimeOffset.UtcNow;
            Thread.Sleep(TimeSpan.FromSeconds(5));
            if (!IsProcessRunning(WatchdogConstants.UiProcessName))
            {
                var diedMsg = "拉起命令已成功但 5s 内未发现 HuaGuang.Monitor 进程（可能启动后立即崩溃）";
                _logger.LogWarning(diedMsg);
                WatchdogDiagLog.Write(diedMsg);
            }
            else
            {
                WatchdogDiagLog.Write("UI 进程已出现，拉起成功");
            }
        }
        else
        {
            var failMsg = $"拉起界面失败: {startError}";
            _logger.LogWarning(failMsg);
            WatchdogDiagLog.Write(failMsg);
        }
    }

    bool ShouldWatchUi()
    {
        if (_shouldWatchUi?.Invoke() == true)
        {
            return true;
        }

        return WindowsUiWatchPolicy.ShouldWatchUi();
    }

    /// <summary>用户通过 UiApplicationExit 退出：Graceful 与心跳同时写入，且关闭后没有新的心跳。</summary>
    static bool WasIntentionalUiExit(WatchdogRoleState state)
    {
        if (state.GracefulShutdownUtc is not { } gracefulUtc)
        {
            return false;
        }

        if (state.LastHeartbeatUtc > gracefulUtc.AddSeconds(15))
        {
            return false;
        }

        return state.LastHeartbeatUtc <= gracefulUtc.AddSeconds(5);
    }

    static bool HasRecentCrashLog(int lookbackMinutes)
    {
        try
        {
            var logDir = AppPaths.LogDirectory;
            if (!Directory.Exists(logDir))
            {
                return false;
            }

            var threshold = DateTime.UtcNow.AddMinutes(-lookbackMinutes);
            foreach (var file in Directory.EnumerateFiles(logDir, "crash-*.log"))
            {
                if (File.GetLastWriteTimeUtc(file) >= threshold)
                {
                    return true;
                }
            }
        }
        catch
        {
        }

        return false;
    }

    static bool WasRecentGracefulShutdown(WatchdogRoleState? state, int windowSeconds)
    {
        if (state?.GracefulShutdownUtc is not { } shutdownUtc)
        {
            return false;
        }

        return shutdownUtc > DateTimeOffset.UtcNow.AddSeconds(-windowSeconds);
    }

    static bool IsCooldownElapsed(DateTimeOffset lastRestartUtc, int cooldownSeconds) =>
        lastRestartUtc == DateTimeOffset.MinValue ||
        lastRestartUtc <= DateTimeOffset.UtcNow.AddSeconds(-cooldownSeconds);

    static bool IsProcessRunning(string processName)
    {
        try
        {
            return Process.GetProcessesByName(processName).Length > 0;
        }
        catch
        {
            return false;
        }
    }

    static bool TryStartUi(string exePath, out string? error)
    {
        if (OperatingSystem.IsWindows())
        {
            return WindowsInteractiveProcessLauncher.TryStart(exePath, out error);
        }

        error = null;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory,
                UseShellExecute = true
            });
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    static bool TryStartWindowsService(string serviceName)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "sc.exe"),
                Arguments = $"start \"{serviceName}\"",
                CreateNoWindow = true,
                UseShellExecute = false
            });
            process?.WaitForExit(15_000);
            return process?.ExitCode == 0 || IsWindowsServiceRunning(serviceName);
        }
        catch
        {
            return false;
        }
    }

    static bool IsWindowsServiceRunning(string serviceName)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "sc.exe"),
                Arguments = $"query \"{serviceName}\"",
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                UseShellExecute = false
            });
            if (process is null)
            {
                return false;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(10_000);
            return output.Contains("RUNNING", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static string ResolveInstallRoot()
    {
        var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (baseDir.EndsWith("service", StringComparison.OrdinalIgnoreCase))
        {
            var parent = Directory.GetParent(baseDir);
            if (parent is not null)
            {
                return parent.FullName;
            }
        }

        return baseDir;
    }
}
