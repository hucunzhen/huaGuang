using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HuaGuang.Monitor.Services;

namespace HuaGuang.Monitor.ViewModels;

public partial class HistoryDetailViewModel : ObservableObject, IQueryAttributable
{
    readonly HistoryStore _store;
    readonly SettingsStore _settings;

    public HistoryDetailViewModel(HistoryStore store, SettingsStore settings)
    {
        _store = store;
        _settings = settings;
    }

    public ObservableCollection<HistoryTagValueRow> Tags { get; } = [];

    [ObservableProperty] string title = "历史详情";
    [ObservableProperty] string headerText = string.Empty;
    [ObservableProperty] string metaText = string.Empty;
    [ObservableProperty] string payloadPreview = string.Empty;
    [ObservableProperty] bool hasPayload;

    public async void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (!query.TryGetValue("id", out var raw) || raw is not string idText || !long.TryParse(idText, out var sampleId))
        {
            HeaderText = "无效的历史记录。";
            return;
        }

        var detail = await _store.GetDetailAsync(sampleId, _settings.Current.TemperaturePrecision).ConfigureAwait(false);
        if (detail is null)
        {
            HeaderText = "找不到该条历史记录。";
            return;
        }

        Title = detail.RecordedAt.ToLocalTime().ToString("HH:mm:ss");
        HeaderText = $"{detail.DeviceId} · {detail.OperationModeLabel} · {detail.Quality}";
        MetaText = detail.SourceTimestamp is { } source
            ? $"记录 {detail.RecordedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss} · 报文时间 {source.ToLocalTime():yyyy-MM-dd HH:mm:ss}"
            : $"记录 {detail.RecordedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}";
        if (!string.IsNullOrWhiteSpace(detail.SourceTopic))
        {
            MetaText += $" · 主题 {detail.SourceTopic}";
        }

        Tags.Clear();
        foreach (var tag in detail.Tags)
        {
            Tags.Add(tag);
        }

        PayloadPreview = detail.PayloadJson ?? string.Empty;
        HasPayload = !string.IsNullOrWhiteSpace(PayloadPreview);
    }

    [RelayCommand]
    Task GoBackAsync() => Shell.Current.GoToAsync("..");
}
