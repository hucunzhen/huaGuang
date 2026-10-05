using System.Text.Json;
using HuaGuang.Monitor.Models;

namespace HuaGuang.Monitor.Services;

/// <summary>多开实例把运行模式存在实例目录，避免写回共享产线 Excel。</summary>
public static class MonitorInstanceOverlay
{
    static string FilePath => Path.Combine(AppPaths.UserDataDirectory, "instance-overlay.json");

    public static void Save(AppOperationMode operationMode, string lineName, bool startWithWindows)
    {
        if (!MonitorProcessInstance.IsIsolated)
        {
            return;
        }

        Directory.CreateDirectory(AppPaths.UserDataDirectory);
        var json = JsonSerializer.Serialize(
            new OverlayState
            {
                OperationMode = operationMode.ToString(),
                LineName = lineName,
                StartWithWindows = startWithWindows
            },
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json);
    }

    public static bool TryLoad(
        out AppOperationMode operationMode,
        out bool? startWithWindows,
        out string? lineName)
    {
        operationMode = AppOperationMode.Acquisition;
        startWithWindows = null;
        lineName = null;
        if (!MonitorProcessInstance.IsIsolated || !File.Exists(FilePath))
        {
            return false;
        }

        try
        {
            var state = JsonSerializer.Deserialize<OverlayState>(File.ReadAllText(FilePath));
            if (state is null || string.IsNullOrWhiteSpace(state.OperationMode))
            {
                return false;
            }

            if (!Enum.TryParse(state.OperationMode, ignoreCase: true, out operationMode))
            {
                return false;
            }

            startWithWindows = state.StartWithWindows;
            if (LineCatalog.TryResolveLineName(state.LineName, out var resolved))
            {
                lineName = resolved;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryLoad(out AppOperationMode operationMode, out bool? startWithWindows) =>
        TryLoad(out operationMode, out startWithWindows, out _);

    public static bool TryLoad(out AppOperationMode operationMode) =>
        TryLoad(out operationMode, out _, out _);

    sealed class OverlayState
    {
        public string OperationMode { get; set; } = nameof(AppOperationMode.Acquisition);
        public string LineName { get; set; } = string.Empty;
        public bool? StartWithWindows { get; set; }
    }
}
