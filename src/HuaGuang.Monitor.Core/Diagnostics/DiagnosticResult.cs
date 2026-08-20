namespace HuaGuang.Monitor.Diagnostics;

public sealed class DiagnosticResult
{
    public required string Name { get; init; }
    public bool Passed { get; init; }
    public string Message { get; init; } = string.Empty;
    public TimeSpan Duration { get; init; }
}
