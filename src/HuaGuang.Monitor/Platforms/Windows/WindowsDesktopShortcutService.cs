using HuaGuang.Monitor.Services;

namespace HuaGuang.Monitor.Platforms.Windows;

public sealed class WindowsDesktopShortcutService : IDesktopShortcutService
{
    public bool IsSupported => true;

    public IReadOnlyList<DesktopShortcutDefinition> Definitions => DesktopShortcutCatalog.Definitions;

    public DesktopShortcutPresence GetPresence(string id)
    {
        var definition = Require(id);
        return new DesktopShortcutPresence(
            File.Exists(DesktopPath(definition)),
            File.Exists(StartMenuPath(definition)));
    }

    public void Add(string id)
    {
        var definition = Require(id);
        var exe = ResolveExePath();
        Directory.CreateDirectory(StartMenuGroupDirectory());
        WriteShortcut(DesktopPath(definition), exe, definition);
        WriteShortcut(StartMenuPath(definition), exe, definition);
    }

    public void Remove(string id)
    {
        var definition = Require(id);
        TryDelete(DesktopPath(definition));
        TryDelete(StartMenuPath(definition));
    }

    static DesktopShortcutDefinition Require(string id) =>
        DesktopShortcutCatalog.Definitions.FirstOrDefault(item => item.Id == id)
        ?? throw new InvalidOperationException($"未知快捷方式：{id}");

    static string DesktopPath(DesktopShortcutDefinition definition) =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            definition.DisplayName + ".lnk");

    static string StartMenuGroupDirectory() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
            "Programs",
            DesktopShortcutCatalog.StartMenuGroupName);

    static string StartMenuPath(DesktopShortcutDefinition definition) =>
        Path.Combine(StartMenuGroupDirectory(), definition.DisplayName + ".lnk");

    static string ResolveExePath()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe))
        {
            throw new InvalidOperationException("无法确定程序路径，无法创建快捷方式。");
        }

        return exe;
    }

    static void WriteShortcut(string path, string exe, DesktopShortcutDefinition definition)
    {
        var workingDir = Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory;
        var icon = Path.Combine(workingDir, "appicon.ico");
        var iconLocation = File.Exists(icon) ? icon : exe;

        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("无法创建快捷方式（WScript.Shell 不可用）。");
        dynamic shell = Activator.CreateInstance(shellType)
            ?? throw new InvalidOperationException("无法创建快捷方式。");
        try
        {
            var shortcut = shell.CreateShortcut(path);
            shortcut.TargetPath = exe;
            shortcut.WorkingDirectory = workingDir;
            shortcut.Arguments = definition.Arguments;
            shortcut.Description = definition.DisplayName;
            shortcut.IconLocation = iconLocation;
            shortcut.Save();
        }
        finally
        {
            if (shell is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}
