using Microsoft.Extensions.Logging;

namespace HuaGuang.Monitor.Services.Logging;

/// <summary>
/// 捕获未处理异常与进程退出，同步写入 crash/exit 日志（不依赖 Logger 管道）。
/// </summary>
public static class CrashExitLogger
{
    static readonly Lock Gate = new();
    static int _earlyRegistered;
    static ILogger? _logger;
    static string? _role;
    static string? _version;
    static string? _crashLogPath;
    static string? _exitLogPath;

    public static void RegisterEarly(string role = "app")
    {
        if (Interlocked.Exchange(ref _earlyRegistered, 1) == 1)
        {
            return;
        }

        _role = role;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandled;
        TaskScheduler.UnobservedTaskException += OnUnobservedTask;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;
    }

    public static void Register(ILoggerFactory loggerFactory, string? version = null)
    {
        _logger = loggerFactory.CreateLogger("CrashExit");
        if (!string.IsNullOrWhiteSpace(version))
        {
            _version = version.Trim();
        }
    }

    public static void SetContext(string? version, string? role = null)
    {
        if (!string.IsNullOrWhiteSpace(version))
        {
            _version = version.Trim();
        }

        if (!string.IsNullOrWhiteSpace(role))
        {
            _role = role.Trim();
        }
    }

    public static void Record(string source, Exception? exception, bool fatal = true, string? detail = null)
    {
        var message = BuildMessage(source, exception, fatal, detail);
        WriteCrashFile(message);
        if (exception is not null)
        {
            _logger?.Log(fatal ? LogLevel.Critical : LogLevel.Error, exception, "{Source} fatal={Fatal}", source, fatal);
        }
        else
        {
            _logger?.Log(fatal ? LogLevel.Critical : LogLevel.Error, "{Source} fatal={Fatal} detail={Detail}", source, fatal, detail);
        }
    }

    public static void WriteBootstrap(string stage, Exception? exception = null) =>
        WriteDiagnostic("bootstrap", stage, exception);

    static void WriteDiagnostic(string prefix, string stage, Exception? exception)
    {
        try
        {
            lock (Gate)
            {
                var path = ResolveCrashLogPath(prefix);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var line = $"{DateTime.Now:O}\t{stage}";
                if (exception is not null)
                {
                    line += Environment.NewLine + exception;
                }

                File.AppendAllText(path, line + Environment.NewLine);
            }
        }
        catch
        {
        }
    }

    static void OnAppDomainUnhandled(object? sender, UnhandledExceptionEventArgs args)
    {
        var exception = args.ExceptionObject as Exception
            ?? new Exception(args.ExceptionObject?.ToString() ?? "unknown");
        Record("AppDomain.UnhandledException", exception, fatal: args.IsTerminating);
    }

    static void OnUnobservedTask(object? sender, UnobservedTaskExceptionEventArgs args)
    {
        Record("TaskScheduler.UnobservedTaskException", args.Exception, fatal: false);
        args.SetObserved();
    }

    static void OnProcessExit(object? sender, EventArgs e)
    {
        try
        {
            lock (Gate)
            {
                var path = ResolveExitLogPath();
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var line =
                    $"{DateTime.Now:O}\tProcessExit role={RoleLabel()} pid={Environment.ProcessId} version={VersionLabel()}";
                File.AppendAllText(path, line + Environment.NewLine);
            }
        }
        catch
        {
        }
    }

    static string BuildMessage(string source, Exception? exception, bool fatal, string? detail)
    {
        var builder = new System.Text.StringBuilder();
        builder.AppendLine($"{DateTime.Now:O}");
        builder.AppendLine($"source={source}");
        builder.AppendLine($"fatal={fatal}");
        builder.AppendLine($"role={RoleLabel()}");
        builder.AppendLine($"pid={Environment.ProcessId}");
        builder.AppendLine($"version={VersionLabel()}");
        if (!string.IsNullOrWhiteSpace(detail))
        {
            builder.AppendLine($"detail={detail}");
        }

        if (exception is not null)
        {
            builder.AppendLine(exception.ToString());
        }

        return builder.ToString();
    }

    static void WriteCrashFile(string message)
    {
        try
        {
            lock (Gate)
            {
                var path = ResolveCrashLogPath("crash");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.AppendAllText(path, message + Environment.NewLine + "---" + Environment.NewLine);
            }
        }
        catch
        {
        }
    }

    static string ResolveCrashLogPath(string prefix)
    {
        ref var cache = ref _crashLogPath;
        if (prefix != "crash")
        {
            return Path.Combine(ResolveLogDirectory(), $"{prefix}-{DateTime.Now:yyyyMMdd}.log");
        }

        cache ??= Path.Combine(ResolveLogDirectory(), $"crash-{DateTime.Now:yyyyMMdd}.log");
        return cache;
    }

    static string ResolveExitLogPath()
    {
        _exitLogPath ??= Path.Combine(ResolveLogDirectory(), $"exit-{DateTime.Now:yyyyMMdd}.log");
        return _exitLogPath;
    }

    static string ResolveLogDirectory()
    {
        try
        {
            return AppPaths.LogDirectory;
        }
        catch
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                AppPaths.PackageId,
                "Data",
                "logs");
        }
    }

    static string RoleLabel() => string.IsNullOrWhiteSpace(_role) ? "app" : _role!;

    static string VersionLabel()
    {
        if (!string.IsNullOrWhiteSpace(_version))
        {
            return _version!;
        }

        return AssemblyVersion();
    }

    static string AssemblyVersion()
    {
        try
        {
            var version = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version;
            return version?.ToString() ?? "unknown";
        }
        catch
        {
            return "unknown";
        }
    }
}
