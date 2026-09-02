using Microsoft.Extensions.Logging;

namespace HuaGuang.Monitor.Services.Logging;

public sealed class RuntimeLogEntry
{
    public required DateTimeOffset Timestamp { get; init; }
    public required LogLevel Level { get; init; }
    public required string Category { get; init; }
    public required string Message { get; init; }

    public string TimeText => Timestamp.LocalDateTime.ToString("HH:mm:ss.fff");
    public string LevelText => Level.ToString();

    public string FullText =>
        $"{Timestamp.LocalDateTime:yyyy-MM-dd HH:mm:ss.fff} [{Level}] {Category}: {Message}";

    public static string ShortenCategory(string category)
    {
        const string prefix = "HuaGuang.Monitor.";
        return category.StartsWith(prefix, StringComparison.Ordinal)
            ? category[prefix.Length..]
            : category;
    }
}
