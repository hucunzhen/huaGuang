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
    readonly IMonitorAcquisition _acquisition;
    readonly DashboardViewModel _dashboard;

    string? _reloadSignature;

    public TagsViewModel(SettingsStore store, IMonitorAcquisition acquisition, DashboardViewModel dashboard)
    {
        _store = store;
        _acquisition = acquisition;
        _dashboard = dashboard;
        Reload(force: true);
    }

    public ObservableCollection<TagConfigGroupViewModel> TagGroups { get; } = [];

    [ObservableProperty]
    string statusMessage = string.Empty;

    [ObservableProperty]
    string lineSummary = string.Empty;

    [ObservableProperty]
    bool showEmptyHint;

    public void ReloadIfNeeded() => Reload(force: false);

    public void Reload(bool force = true)
    {
        var storeTags = _store.Current.Tags;
        var signature = BuildReloadSignature(storeTags);
        if (!force && signature == _reloadSignature)
        {
            UpdateSummary(storeTags);
            return;
        }

        _reloadSignature = signature;
        GroupedCollectionHelper.ClearConfigGroups(TagGroups);
        var tags = storeTags.Select(tag => new TagConfigViewModel(tag)).ToList();

        foreach (var group in tags
                     .GroupBy(item => item.Category)
                     .OrderBy(g => TagDisplayCategoryHelper.GetSortOrder(g.Key)))
        {
            var groupViewModel = new TagConfigGroupViewModel(group.Key);
            foreach (var item in group)
            {
                groupViewModel.Add(item);
            }

            TagGroups.Add(groupViewModel);
        }

        UpdateSummary(storeTags);
    }

    static string BuildReloadSignature(IReadOnlyList<PlcTag> tags) =>
        string.Join("|", tags
            .OrderBy(tag => tag.Id, StringComparer.Ordinal)
            .Select(tag =>
                $"{tag.Id}:{tag.Name}:{tag.Enabled}:{tag.DisplayAddress}:{tag.MqttField}:{tag.DataType}:{tag.DisplayCategory}:{tag.IsManual}"));

    void UpdateSummary(IReadOnlyList<PlcTag> tags)
    {
        var enabledCount = tags.Count(tag => tag.Enabled);
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
        await _store.SaveAsync(_store.Current);

        if (!TryRemoveTagFromGroups(item))
        {
            Reload(force: true);
        }
        else
        {
            UpdateSummary(_store.Current.Tags);
            _reloadSignature = BuildReloadSignature(_store.Current.Tags);
        }

        _dashboard.Reload();
        StatusMessage = $"已删除 {tag.Name}";
    }

    bool TryRemoveTagFromGroups(TagConfigViewModel item)
    {
        foreach (var group in TagGroups)
        {
            if (!group.Contains(item))
            {
                continue;
            }

            group.Remove(item);
            if (group.Count == 0)
            {
                TagGroups.Remove(group);
            }

            return true;
        }

        return false;
    }
}
