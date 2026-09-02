using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace HuaGuang.Monitor.Services.Logging;

public static class InMemoryLoggerExtensions
{
    public static ILoggingBuilder AddInMemoryRuntimeLogger(
        this ILoggingBuilder builder,
        LogLevel minimumLevel = LogLevel.Debug)
    {
        builder.Services.AddSingleton<RuntimeLogStore>();
        builder.Services.AddSingleton<ILoggerProvider, InMemoryLoggerProvider>(sp =>
            new InMemoryLoggerProvider(sp.GetRequiredService<RuntimeLogStore>(), minimumLevel));
        return builder;
    }
}

sealed class InMemoryLoggerProvider : ILoggerProvider
{
    readonly RuntimeLogStore _store;
    readonly LogLevel _minimumLevel;

    public InMemoryLoggerProvider(RuntimeLogStore store, LogLevel minimumLevel)
    {
        _store = store;
        _minimumLevel = minimumLevel;
    }

    public ILogger CreateLogger(string categoryName) =>
        new InMemoryLogger(categoryName, _store, _minimumLevel);

    public void Dispose()
    {
    }
}

sealed class InMemoryLogger : ILogger
{
    readonly string _category;
    readonly RuntimeLogStore _store;
    readonly LogLevel _minimumLevel;

    public InMemoryLogger(string category, RuntimeLogStore store, LogLevel minimumLevel)
    {
        _category = category;
        _store = store;
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
        if (exception is not null)
        {
            message = string.IsNullOrWhiteSpace(message)
                ? exception.ToString()
                : message + Environment.NewLine + exception;
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        _store.Add(logLevel, _category, message);
    }
}
