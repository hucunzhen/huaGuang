namespace HuaGuang.Monitor.Models;

public sealed class TagSnapshot
{
    public required string TagId { get; init; }
    public required string Name { get; init; }
    public string Unit { get; init; } = string.Empty;
    public object? Value { get; init; }
    public string Quality { get; init; } = "Bad";
    public string? Error { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
}
