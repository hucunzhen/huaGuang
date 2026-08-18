using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HuaGuang.Monitor.Models;
using HuaGuang.Monitor.Services;
using HuaGuang.Monitor.Views;

namespace HuaGuang.Monitor.ViewModels;

public partial class TagsViewModel : ObservableObject
{
    readonly SettingsStore _store;
    readonly AcquisitionService _acquisition;
    readonly DashboardViewModel _dashboard;

    public TagsViewModel(SettingsStore store, AcquisitionService acquisition, DashboardViewModel dashboard)
    {
        _store = store;
        _acquisition = acquisition;
        _dashboard = dashboard;
        Reload();
    }

    public ObservableCollection<PlcTag> Tags { get; } = [];

    [ObservableProperty]
    string statusMessage = string.Empty;

    public void Reload()
    {
        Tags.Clear();
        foreach (var tag in _store.Current.Tags)
        {
            Tags.Add(tag);
        }
    }

    [RelayCommand]
    Task AddAsync() => Shell.Current.GoToAsync(nameof(TagEditPage));

    [RelayCommand]
    Task EditAsync(PlcTag? tag)
    {
        if (tag is null)
        {
            return Task.CompletedTask;
        }

        return Shell.Current.GoToAsync($"{nameof(TagEditPage)}?id={tag.Id}");
    }

    [RelayCommand]
    async Task DeleteAsync(PlcTag? tag)
    {
        if (tag is null)
        {
            return;
        }

        if (_acquisition.IsRunning)
        {
            StatusMessage = "请先停止采集再修改点位。";
            return;
        }

        var confirm = await Shell.Current.DisplayAlertAsync("删除点位", $"确定删除“{tag.Name}”？", "删除", "取消");
        if (!confirm)
        {
            return;
        }

        _store.Current.Tags.RemoveAll(t => t.Id == tag.Id);
        await _store.SaveAsync(_store.Current);
        Reload();
        _dashboard.Reload();
        StatusMessage = $"已删除 {tag.Name}";
    }
}
