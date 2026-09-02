using HuaGuang.Monitor.Models;
using Microsoft.Extensions.Logging;

namespace HuaGuang.Monitor.Services.Logging;

public static class FileLoggerExtensions
{
    public static ILoggingBuilder AddRuntimeFileLogger(
        this ILoggingBuilder builder,
        string logDirectory,
        LogLevel minimumLevel = LogLevel.Information,
        int retentionDays = 14)
    {
        Directory.CreateDirectory(logDirectory);
        LogRetention.Cleanup(logDirectory, retentionDays);
        builder.AddProvider(new FileLoggerProvider(logDirectory, minimumLevel));
        return builder;
    }
}

sealed class FileLoggerProvider : ILoggerProvider
{
    readonly string _logDirectory;
    readonly LogLevel _minimumLevel;

    public FileLoggerProvider(string logDirectory, LogLevel minimumLevel)
    {
        _logDirectory = logDirectory;
        _minimumLevel = minimumLevel;
    }

    public ILogger CreateLogger(string categoryName) =>
        new FileLogger(categoryName, _logDirectory, _minimumLevel);

    public void Dispose()
    {
    }
}

sealed class FileLogger : ILogger
{
    readonly string _category;
    readonly string _logDirectory;
    readonly LogLevel _minimumLevel;

    public FileLogger(string category, string logDirectory, LogLevel minimumLevel)
    {
        _category = category;
        _logDirectory = logDirectory;
        _minimumLevel = minimumLevel;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => logLevel >= _minimumLevel;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        var message = formatter(state, exception);
        if (string.IsNullOrWhiteSpace(message) && exception is null)
        {
            return;
        }

        RuntimeLogWriter.Write(_logDirectory, logLevel, _category, message, exception);
    }
}

static class RuntimeLogWriter
{
    static readonly Lock Gate = new();
    static string? _currentDateKey;
    static string? _currentFilePath;

    public static void Write(
        string logDirectory,
        LogLevel level,
        string category,
        string message,
        Exception? exception)
    {
        var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        var shortCategory = ShortenCategory(category);
        var line = $"{timestamp} [{level}] {shortCategory}: {message}";
        if (exception is not null)
        {
            line += Environment.NewLine + exception;
        }

        lock (Gate)
        {
            var filePath = ResolveLogFilePath(logDirectory);
            File.AppendAllText(filePath, line + Environment.NewLine);
        }
    }

    static string ResolveLogFilePath(string logDirectory)
    {
        var dateKey = DateTime.Now.ToString("yyyyMMdd");
        if (_currentDateKey == dateKey && _currentFilePath is not null)
        {
            return _currentFilePath;
        }

        _currentDateKey = dateKey;
        _currentFilePath = Path.Combine(logDirectory, $"runtime-{dateKey}.log");
        return _currentFilePath;
    }

    static string ShortenCategory(string category)
    {
        const string prefix = "HuaGuang.Monitor.";
        return category.StartsWith(prefix, StringComparison.Ordinal)
            ? category[prefix.Length..]
            : category;
    }
}

static class LogRetention
{
    public static void Cleanup(string logDirectory, int retentionDays)
    {
        if (retentionDays <= 0 || !Directory.Exists(logDirectory))
        {
            return;
        }

        var cutoff = DateTime.Now.AddDays(-retentionDays);
        foreach (var file in Directory.EnumerateFiles(logDirectory, "runtime-*.log"))
        {
            try
            {
                if (File.GetLastWriteTime(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
            catch
            {
            }
        }
    }
}

public static class LogFormatting
{
    public static string DescribeMqtt(MqttSettings settings, string? lineName = null)
    {
        var clientId = string.IsNullOrWhiteSpace(settings.ClientId)
            ? LineMqttDefaults.ResolveClientIdForLine(lineName ?? string.Empty)
            : settings.ClientId;
        return $"host={settings.Host}:{settings.Port}, clientId={clientId}, user={settings.Username}, tls={settings.UseTls}, qos={settings.Qos}, topic={settings.Topic}";
    }

    public static string DescribePlc(PlcSettings settings) =>
        $"host={settings.Host}:{settings.Port}, station={settings.Station}, timeout={settings.TimeoutMs}ms";

    public static string Truncate(string? text, int maxLength = 512) =>
        string.IsNullOrEmpty(text)
            ? string.Empty
            : text.Length <= maxLength
                ? text
                : text[..maxLength] + "…";
}
