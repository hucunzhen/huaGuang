using System.Text.Json;

namespace HuaGuang.Monitor.Services;

/// <summary>
/// 操作员主动 Stop 后持久化到 ProgramData，禁止守护/服务自动 Start，直到再次 Start。
/// </summary>
public static class MonitorRuntimeOperatorControl
{
    const int Unloaded = -1;

    static readonly object Gate = new();
    static int _pausedByOperator = Unloaded;

    static string StateFilePath => Path.Combine(AppPaths.UserDataDirectory, "runtime-operator-state.json");

    public static bool IsPausedByOperator
    {
        get
        {
            EnsureLoaded();
            return Volatile.Read(ref _pausedByOperator) == 1;
        }
    }

    public static void SetPausedByOperator(bool paused)
    {
        lock (Gate)
        {
            EnsureLoadedCore();
            var value = paused ? 1 : 0;
            if (_pausedByOperator == value)
            {
                return;
            }

            _pausedByOperator = value;
            PersistLocked();
        }
    }

    static void EnsureLoaded()
    {
        if (Volatile.Read(ref _pausedByOperator) != Unloaded)
        {
            return;
        }

        lock (Gate)
        {
            EnsureLoadedCore();
        }
    }

    static void EnsureLoadedCore()
    {
        if (_pausedByOperator != Unloaded)
        {
            return;
        }

        _pausedByOperator = ReadFromDisk() ? 1 : 0;
    }

    static bool ReadFromDisk()
    {
        try
        {
            if (!File.Exists(StateFilePath))
            {
                return false;
            }

            var json = File.ReadAllText(StateFilePath);
            var state = JsonSerializer.Deserialize<RuntimeOperatorState>(json);
            return state?.PausedByOperator == true;
        }
        catch
        {
            return false;
        }
    }

    static void PersistLocked()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.UserDataDirectory);
            var json = JsonSerializer.Serialize(
                new RuntimeOperatorState { PausedByOperator = _pausedByOperator == 1 },
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(StateFilePath, json);
        }
        catch
        {
            // 持久化失败仍保留进程内状态；守护同进程内仍生效。
        }
    }

    sealed class RuntimeOperatorState
    {
        public bool PausedByOperator { get; set; }
    }
}
