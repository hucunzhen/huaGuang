using System.Text.Json;

namespace HuaGuang.Monitor.Services;

/// <summary>
/// 主窗口开机拉起开关。写在共享数据目录，供 SYSTEM 守护读取（HKCU Run 在服务账户下看不到用户登录项）。
/// </summary>
public static class PrimaryUiAutoStartStore
{
    static string FilePath => Path.Combine(AppPaths.SharedDataDirectory, "primary-ui-autostart.json");

    public static bool IsEnabled()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return false;
            }

            var state = JsonSerializer.Deserialize<State>(File.ReadAllText(FilePath));
            return state?.Enabled == true;
        }
        catch
        {
            return false;
        }
    }

    public static void Set(bool enabled)
    {
        Directory.CreateDirectory(AppPaths.SharedDataDirectory);
        var json = JsonSerializer.Serialize(
            new State { Enabled = enabled },
            new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json);
    }

    sealed class State
    {
        public bool Enabled { get; set; }
    }
}
