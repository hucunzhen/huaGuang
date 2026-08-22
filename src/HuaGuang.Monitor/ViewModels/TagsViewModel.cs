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

    public ObservableCollection<TagConfigGroupViewModel> TagGroups { get; } = [];

    [ObservableProperty]
    string statusMessage = string.Empty;

    [ObservableProperty]
    string lineSummary = string.Empty;

    [ObservableProperty]
    bool showEmptyHint;

    public void Reload()
    {
        TagGroups.Clear();
        var tags = _store.Current.Tags.Select(tag => new TagConfigViewModel(tag)).ToList();

        foreach (var group in tags
                     .GroupBy(item => item.Category)
                     .OrderBy(g => TagDisplayCategoryHelper.GetSortOrder(g.Key)))
        {
            var groupViewModel = new TagConfigGroupViewModel(group.Key);
            foreach (var item in group)
            {
                groupViewModel.Tags.Add(item);
            }

            TagGroups.Add(groupViewModel);
        }

        var enabledCount = tags.Count(item => item.Tag.Enabled);
        LineSummary = $"产线：{_store.Current.LineName} · {tags.Count} 个点位（{enabledCount} 启用）";
        ShowEmptyHint = tags.Count == 0;
    }

    [RelayCommand]
    Task AddAsync() => Shell.Current.GoToAsync(nameof(TagEditPage));

    [RelayCommand]
    Task EditAsync(TagConfigViewModel? item)
    {
        if (item?.Tag is null)
        {
            return Task.CompletedTask;
        }

        return Shell.Current.GoToAsync($"{nameof(TagEditPage)}?id={item.Tag.Id}");
    }

    [RelayCommand]
    async Task DeleteAsync(TagConfigViewModel? item)
    {
        var tag = item?.Tag;
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
        LineConfigPaths.SaveCurrentLine(_store.Current);
        await _store.SaveAsync(_store.Current);
        Reload();
        _dashboard.Reload();
        StatusMessage = $"已删除 {tag.Name}";
    }
}
