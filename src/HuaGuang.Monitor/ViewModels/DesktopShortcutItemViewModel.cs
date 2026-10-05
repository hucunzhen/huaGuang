using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HuaGuang.Monitor.Services;

namespace HuaGuang.Monitor.ViewModels;

public sealed partial class DesktopShortcutItemViewModel : ObservableObject
{
    readonly IDesktopShortcutService _shortcuts;
    readonly Action<string> _notify;

    public DesktopShortcutItemViewModel(
        DesktopShortcutDefinition definition,
        IDesktopShortcutService shortcuts,
        Action<string> notify)
    {
        Definition = definition;
        _shortcuts = shortcuts;
        _notify = notify;
        Refresh();
    }

    public DesktopShortcutDefinition Definition { get; }

    public string DisplayName => Definition.DisplayName;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanAdd))]
    [NotifyPropertyChangedFor(nameof(CanRemove))]
    string statusText = "未添加";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    bool canAdd = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveCommand))]
    bool canRemove;

    public void Refresh()
    {
        var presence = _shortcuts.GetPresence(Definition.Id);
        StatusText = presence.StatusText;
        CanAdd = !presence.OnDesktop || !presence.OnStartMenu;
        CanRemove = presence.Exists;
    }

    [RelayCommand(CanExecute = nameof(CanAdd))]
    void Add()
    {
        try
        {
            _shortcuts.Add(Definition.Id);
            Refresh();
            _notify($"已添加快捷方式：{DisplayName}（桌面和开始菜单）");
        }
        catch (Exception ex)
        {
            _notify($"添加快捷方式失败：{ex.Message}");
        }
    }

    [RelayCommand(CanExecute = nameof(CanRemove))]
    void Remove()
    {
        try
        {
            _shortcuts.Remove(Definition.Id);
            Refresh();
            _notify($"已删除快捷方式：{DisplayName}");
        }
        catch (Exception ex)
        {
            _notify($"删除快捷方式失败：{ex.Message}");
        }
    }
}
