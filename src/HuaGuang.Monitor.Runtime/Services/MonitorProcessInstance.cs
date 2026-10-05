using System.Diagnostics;
using System.Text.RegularExpressions;
using HuaGuang.Monitor.Ipc;
using HuaGuang.Monitor.Models;

namespace HuaGuang.Monitor.Services;

/// <summary>
/// Windows 多开：<c>--instance {id}</c> 时隔离数据目录与单实例锁，并在本进程运行采集/订阅。
/// 不带参数时保持原来的单实例 + 后台服务行为。MQTT ClientId 仍用产线 Excel 中的配置，不加后缀。
/// </summary>
public static class MonitorProcessInstance
{
    public const int MaxIdLength = 32;

    static readonly Lock Gate = new();
    static bool _initialized;
    static string? _id;
    static string? _startupLineName;
    static AppOperationMode? _startupMode;

    public static void Initialize(IEnumerable<string>? arguments = null)
    {
        lock (Gate)
        {
            if (_initialized && arguments is null)
            {
                return;
            }

            var args = arguments?.ToArray()
                ?? Environment.GetCommandLineArgs().Skip(1).ToArray();
            _id = ParseId(args);
            _startupLineName = ParseLine(args);
            _startupMode = ParseMode(args);
            if (_id is null)
            {
                TryParseFromRawCommandLine(Environment.CommandLine, out _id, out _startupLineName, out _startupMode);
            }

            _initialized = true;
            ApplyDefaultSubscribeId();
        }
    }

    /// <summary>
    /// 命令行丢掉 --instance 时，用当前进程 PID 对上 <c>HuaGuangMonitor-{id}</c> 服务名。
    /// </summary>
    public static void RecoverIsolatedIdentityFromWindowsService()
    {
        lock (Gate)
        {
            EnsureInitialized();
            if (!string.IsNullOrEmpty(_id) || !OperatingSystem.IsWindows())
            {
                return;
            }

            try
            {
                var pid = Environment.ProcessId;
                if (!Directory.Exists(InstanceHostProfileStore.DirectoryPath))
                {
                    return;
                }

                foreach (var path in Directory.EnumerateFiles(InstanceHostProfileStore.DirectoryPath, "*.json"))
                {
                    var rawId = Path.GetFileNameWithoutExtension(path);
                    if (!TrySanitize(rawId, out var id, out _))
                    {
                        continue;
                    }

                    if (TryReadWindowsServicePid($"{MonitorIpcConstants.DefaultServiceName}-{id}", out var servicePid)
                        && servicePid == pid)
                    {
                        _id = id;
                        break;
                    }
                }
            }
            catch
            {
            }
        }
    }

    /// <summary>本窗口已保存的产线/模式优先于快捷方式上的 --line/--mode（避免总是先河）。</summary>
    public static void ApplyPersistedHostProfile()
    {
        lock (Gate)
        {
            EnsureInitialized();
            if (IsIsolated)
            {
                if (MonitorInstanceOverlay.TryLoad(out var overlayMode, out _, out var overlayLine))
                {
                    _startupMode = overlayMode;
                    if (!string.IsNullOrWhiteSpace(overlayLine))
                    {
                        _startupLineName = overlayLine;
                    }
                }

                if (InstanceHostProfileStore.TryLoad(Id!, out var profile))
                {
                    if (_startupMode is null
                        && Enum.TryParse(profile.OperationMode, ignoreCase: true, out AppOperationMode mode))
                    {
                        _startupMode = mode;
                    }

                    if (string.IsNullOrWhiteSpace(_startupLineName)
                        && LineCatalog.TryResolveLineName(profile.LineName, out var lineName))
                    {
                        _startupLineName = lineName;
                    }
                }
            }

            ApplyDefaultSubscribeId();
        }

        PersistStartupLineIfSpecified();
    }

    public static void PersistStartupLineIfSpecified()
    {
        var line = StartupLineName;
        if (string.IsNullOrWhiteSpace(line))
        {
            return;
        }

        LineConfigPaths.WriteActiveLineName(line);
    }

    static void ApplyDefaultSubscribeId()
    {
        if (_startupMode is null
            && string.Equals(_id, "sub", StringComparison.OrdinalIgnoreCase))
        {
            _startupMode = AppOperationMode.Subscribe;
        }
    }

    internal static void ResetForTests(string? id)
    {
        lock (Gate)
        {
            _id = string.IsNullOrWhiteSpace(id) ? null : SanitizeOrThrow(id);
            _startupLineName = null;
            _startupMode = null;
            _initialized = true;
        }
    }

    public static bool IsIsolated
    {
        get
        {
            EnsureInitialized();
            return !string.IsNullOrEmpty(_id);
        }
    }

    public static string? Id
    {
        get
        {
            EnsureInitialized();
            return _id;
        }
    }

    public static string? StartupLineName
    {
        get
        {
            EnsureInitialized();
            return _startupLineName;
        }
    }

    public static AppOperationMode? StartupMode
    {
        get
        {
            EnsureInitialized();
            return _startupMode;
        }
    }

    public static string WindowTitle
    {
        get
        {
            EnsureInitialized();
            if (!IsIsolated)
            {
                return "工业监控";
            }

            try
            {
                var mode = StartupMode
                    ?? (MonitorInstanceOverlay.TryLoad(out var overlay) ? overlay : (AppOperationMode?)null);
                if (mode == AppOperationMode.Subscribe)
                {
                    return $"工业监控 · 订阅大屏 · {Id}";
                }

                var line = LineConfigPaths.ReadActiveLineName();
                var shortName = LineCatalog.GetShortDisplayName(line);
                return $"工业监控 · {shortName}采集 · {Id}";
            }
            catch
            {
                return $"工业监控 · {Id}";
            }
        }
    }

    public static string WindowsServiceName
    {
        get
        {
            EnsureInitialized();
            return IsIsolated
                ? $"{MonitorIpcConstants.DefaultServiceName}-{Id}"
                : MonitorIpcConstants.DefaultServiceName;
        }
    }

    public static string WindowsServiceDisplayName
    {
        get
        {
            EnsureInitialized();
            if (!IsIsolated)
            {
                return MonitorIpcConstants.ServiceDisplayName;
            }

            var mode = StartupMode ?? AppOperationMode.Acquisition;
            var role = mode == AppOperationMode.Subscribe ? "订阅" : "采集";
            return $"{MonitorIpcConstants.ServiceDisplayName} · {role} {Id}";
        }
    }

    public static string IpcPipeName
    {
        get
        {
            EnsureInitialized();
            return IsIsolated
                ? $"{MonitorIpcConstants.DefaultPipeName}.{Id}"
                : MonitorIpcConstants.DefaultPipeName;
        }
    }

    public static int IpcTcpPort
    {
        get
        {
            EnsureInitialized();
            if (!IsIsolated)
            {
                return MonitorIpcConstants.DefaultTcpPort;
            }

            var hash = 0;
            foreach (var ch in Id!)
            {
                hash = unchecked(hash * 33 + ch);
            }

            return MonitorIpcConstants.DefaultTcpPort + 1 + (Math.Abs(hash) % 800);
        }
    }

    public static string StartupRegistryValueName
    {
        get
        {
            EnsureInitialized();
            return IsIsolated ? $"IndustrialMonitor.{Id}" : "IndustrialMonitor";
        }
    }

    public static string FormatHostArguments(AppOperationMode? operationMode = null, string? lineName = null)
    {
        EnsureInitialized();
        if (!IsIsolated)
        {
            return string.Empty;
        }

        var line = lineName ?? StartupLineName;
        if (string.IsNullOrWhiteSpace(line))
        {
            try
            {
                if (InstanceHostProfileStore.TryLoad(Id!, out var profile)
                    && LineCatalog.TryResolveLineName(profile.LineName, out var profileLine))
                {
                    line = profileLine;
                }
            }
            catch
            {
            }
        }

        var mode = operationMode ?? StartupMode;
        if (mode is null
            && string.Equals(Id, "sub", StringComparison.OrdinalIgnoreCase))
        {
            mode = AppOperationMode.Subscribe;
        }

        var modeArg = mode == AppOperationMode.Subscribe ? "subscribe" : "acquisition";
        if (string.IsNullOrWhiteSpace(line))
        {
            return $"--instance {Id} --mode {modeArg}";
        }

        return $"--instance {Id} --line {LineCatalog.ToCommandAlias(line)} --mode {modeArg}";
    }

    public static string ResolveUserDataDirectory(string sharedDataDirectory) =>
        ResolveUserDataDirectory(sharedDataDirectory, Id);

    internal static string ResolveUserDataDirectory(string sharedDataDirectory, string? instanceId)
    {
        if (string.IsNullOrEmpty(instanceId))
        {
            return sharedDataDirectory;
        }

        return Path.Combine(sharedDataDirectory, "instances", instanceId);
    }

    public static bool TrySanitize(string? raw, out string id, out string error)
    {
        id = string.Empty;
        error = string.Empty;
        var trimmed = raw?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            error = "实例 id 不能为空。";
            return false;
        }

        if (trimmed.Length > MaxIdLength)
        {
            error = $"实例 id 最长 {MaxIdLength} 个字符。";
            return false;
        }

        if (!Regex.IsMatch(trimmed, @"^[A-Za-z0-9][A-Za-z0-9_-]*$"))
        {
            error = "实例 id 只能含字母、数字、下划线和连字符，且须以字母或数字开头。";
            return false;
        }

        id = trimmed;
        return true;
    }

    static string SanitizeOrThrow(string raw)
    {
        if (!TrySanitize(raw, out var id, out var error))
        {
            throw new ArgumentException(error, nameof(raw));
        }

        return id;
    }

    static bool TryReadWindowsServicePid(string serviceName, out int pid)
    {
        pid = 0;
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = Path.Combine(Environment.SystemDirectory, "sc.exe"),
                Arguments = $"queryex \"{serviceName}\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (process is null)
            {
                return false;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(8_000);
            var match = Regex.Match(output, @"PID\s*:\s*(\d+)", RegexOptions.IgnoreCase);
            return match.Success && int.TryParse(match.Groups[1].Value, out pid) && pid > 0;
        }
        catch
        {
            return false;
        }
    }

    static void TryParseFromRawCommandLine(
        string commandLine,
        out string? id,
        out string? lineName,
        out AppOperationMode? mode)
    {
        id = null;
        lineName = null;
        mode = null;
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return;
        }

        var tokens = Regex.Matches(commandLine, @"(""[^""]+""|\S+)")
            .Select(match => match.Value.Trim('"'))
            .SkipWhile(token => !token.Contains("HuaGuang.Monitor", StringComparison.OrdinalIgnoreCase))
            .Skip(1)
            .ToArray();
        if (tokens.Length == 0)
        {
            tokens = Regex.Matches(commandLine, @"(""[^""]+""|\S+)")
                .Select(match => match.Value.Trim('"'))
                .ToArray();
        }

        id = ParseId(tokens);
        try
        {
            lineName = ParseLine(tokens);
        }
        catch
        {
            lineName = null;
        }

        try
        {
            mode = ParseMode(tokens);
        }
        catch
        {
            mode = null;
        }
    }

    internal static string? ParseId(IReadOnlyList<string> args)
    {
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg.StartsWith("--instance=", StringComparison.OrdinalIgnoreCase))
            {
                return RequireId(arg["--instance=".Length..]);
            }

            if (arg.Equals("--instance", StringComparison.OrdinalIgnoreCase)
                && i + 1 < args.Count)
            {
                return RequireId(args[i + 1]);
            }
        }

        return null;
    }

    static string RequireId(string raw)
    {
        if (!TrySanitize(raw, out var id, out var error))
        {
            throw new InvalidOperationException($"启动参数 --instance 无效：{error}");
        }

        return id;
    }

    internal static string? ParseLine(IReadOnlyList<string> args)
    {
        var raw = ReadOption(args, "--line");
        if (raw is null)
        {
            return null;
        }

        if (!LineCatalog.TryResolveLineName(raw, out var lineName))
        {
            throw new InvalidOperationException($"启动参数 --line 无法识别：{raw}");
        }

        return lineName;
    }

    internal static AppOperationMode? ParseMode(IReadOnlyList<string> args)
    {
        var raw = ReadOption(args, "--mode");
        if (raw is null)
        {
            return null;
        }

        var key = raw.Trim().ToLowerInvariant();
        return key switch
        {
            "acquisition" or "acquire" or "acq" or "采集" or "采集模式" => AppOperationMode.Acquisition,
            "subscribe" or "sub" or "订阅" or "订阅模式" => AppOperationMode.Subscribe,
            _ => throw new InvalidOperationException($"启动参数 --mode 无法识别：{raw}（请用 acquisition 或 subscribe）")
        };
    }

    static string? ReadOption(IReadOnlyList<string> args, string name)
    {
        var prefix = name + "=";
        for (var i = 0; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return arg[prefix.Length..];
            }

            if (arg.Equals(name, StringComparison.OrdinalIgnoreCase) && i + 1 < args.Count)
            {
                return args[i + 1];
            }
        }

        return null;
    }

    static void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        Initialize();
    }
}
