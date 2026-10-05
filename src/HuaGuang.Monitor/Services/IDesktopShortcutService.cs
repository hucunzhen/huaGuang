namespace HuaGuang.Monitor.Services;

public sealed record DesktopShortcutDefinition(string Id, string DisplayName, string Arguments);

public sealed record DesktopShortcutPresence(bool OnDesktop, bool OnStartMenu)
{
    public bool Exists => OnDesktop || OnStartMenu;

    public string StatusText => (OnDesktop, OnStartMenu) switch
    {
        (true, true) => "已添加：桌面和开始菜单",
        (true, false) => "已添加：仅桌面",
        (false, true) => "已添加：仅开始菜单",
        _ => "未添加"
    };
}

public interface IDesktopShortcutService
{
    bool IsSupported { get; }

    IReadOnlyList<DesktopShortcutDefinition> Definitions { get; }

    DesktopShortcutPresence GetPresence(string id);

    void Add(string id);

    void Remove(string id);
}
