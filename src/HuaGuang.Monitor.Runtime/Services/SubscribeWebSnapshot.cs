using System.Text.Json;
using HuaGuang.Monitor.Models;

namespace HuaGuang.Monitor.Services;

public sealed class SubscribeWebStatus
{
    public bool Running { get; init; }
    public bool MqttConnected { get; init; }
    public string LineName { get; init; } = string.Empty;
    public string LastError { get; init; } = string.Empty;
    public DateTimeOffset GeneratedAt { get; init; }
    public List<SubscribeWebDevice> Devices { get; init; } = [];
}

public sealed class SubscribeWebDevice
{
    public string DeviceId { get; init; } = string.Empty;
    public string Topic { get; init; } = string.Empty;
    public string Quality { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTimeOffset ReceivedAt { get; init; }
    public List<SubscribeWebTag> Tags { get; init; } = [];
}

public sealed class SubscribeWebTag
{
    public string Name { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;
    public string Group { get; init; } = string.Empty;
    public string GroupColor { get; init; } = "#8AA0B5";
    public bool IsSwitch { get; init; }
    public string Indicator { get; init; } = string.Empty;
    public string? Color { get; init; }
    public string Background { get; init; } = "#152536";
    public string Address { get; init; } = string.Empty;
    public string UpdatedAt { get; init; } = string.Empty;
    public string QualityColor { get; init; } = "#8AA0B5";
}

/// <summary>把订阅进程里的设备快照整理成网页要的只读数据。</summary>
public static class SubscribeWebSnapshot
{
    public static SubscribeWebStatus Create(
        AppSettings settings,
        bool running,
        bool mqttConnected,
        string? lastError,
        IEnumerable<RemoteDeviceState> devices)
    {
        var profile = settings.MqttPayload ?? new MqttPayloadProfile();
        var precision = settings.TemperaturePrecision;
        return new SubscribeWebStatus
        {
            Running = running,
            MqttConnected = mqttConnected,
            LineName = settings.LineName,
            LastError = lastError ?? string.Empty,
            GeneratedAt = DateTimeOffset.Now,
            Devices = devices.Select(device => CreateDevice(settings, profile, precision, device)).ToList()
        };
    }

    static SubscribeWebDevice CreateDevice(
        AppSettings settings,
        MqttPayloadProfile profile,
        int precision,
        RemoteDeviceState device)
    {
        var catalog = SubscribeLineCatalog.ResolveTags(device.DeviceId, device.SourceTopic, settings.Tags);
        var updatedAt = (device.Timestamp == default ? device.ReceivedAt : device.Timestamp)
            .ToLocalTime()
            .ToString("HH:mm:ss");
        var qualityColor = QualityColor(device.Quality);
        var built = new List<(SubscribeWebTag Tag, TagDisplayCategory Category)>();
        foreach (var (name, value, catalogTag) in TagDisplayOrder.OrderRemoteTags(device.Tags, catalog, profile))
        {
            var tag = catalogTag ?? new PlcTag { Name = name };
            var category = TagDisplayCategoryHelper.Resolve(tag, value);
            var isSwitch = category == TagDisplayCategory.Switch;
            var display = isSwitch
                ? SwitchStatusFormatting.FormatDisplayText(tag, value)
                : catalogTag is null
                    ? FormatLoose(value)
                    : ValueFormatting.FormatDisplay(tag, value, precision);
            if (isSwitch && display == "—" && value is not null)
            {
                display = FormatLoose(value);
            }

            built.Add((new SubscribeWebTag
            {
                Name = name,
                Value = display,
                Unit = isSwitch ? string.Empty : tag.Unit,
                Group = TagDisplayCategoryHelper.GetTitle(category),
                GroupColor = TagDisplayCategoryHelper.GetAccentColor(category),
                IsSwitch = isSwitch,
                Indicator = isSwitch ? SwitchStatusFormatting.FormatIndicatorText(tag, value) : string.Empty,
                Color = isSwitch
                    ? SwitchStatusFormatting.GetAccentColor(tag, value)
                    : TagDisplayCategoryHelper.GetAccentColor(category),
                Background = CardBackground(category, tag, value),
                Address = device.SourceTopic,
                UpdatedAt = updatedAt,
                QualityColor = qualityColor
            }, category));
        }

        var tags = built
            .GroupBy(item => item.Category)
            .OrderBy(group => TagDisplayCategoryHelper.GetSortOrder(group.Key))
            .SelectMany(group => CurrentInjectionFormatting.OrderByDisplay(group.Key, group, item => item.Tag.Name))
            .Select(item => item.Tag)
            .ToList();

        return new SubscribeWebDevice
        {
            DeviceId = device.DeviceId,
            Topic = device.SourceTopic,
            Quality = device.Quality,
            Status = BuildStatus(device),
            ReceivedAt = device.ReceivedAt,
            Tags = tags
        };
    }

    static string BuildStatus(RemoteDeviceState device)
    {
        var host = string.IsNullOrWhiteSpace(device.PlcHost) ? "—" : device.PlcHost;
        var mode = device.Simulator ? "模拟" : "PLC";
        return $"{device.SourceTopic} · {mode} · PLC {host} · 更新 {device.ReceivedAt.ToLocalTime():HH:mm:ss}";
    }

    static string CardBackground(TagDisplayCategory category, PlcTag tag, object? value) =>
        category == TagDisplayCategory.Switch
            ? SwitchStatusFormatting.GetBackgroundColor(tag, value)
            : category switch
            {
                TagDisplayCategory.Temperature => "#1E2430",
                TagDisplayCategory.Process => "#152536",
                TagDisplayCategory.Setting => "#182030",
                _ => "#152536"
            };

    static string QualityColor(string? quality) => quality switch
    {
        "Good" => "#3DDC97",
        "Bad" => "#FF6B6B",
        _ => "#8AA0B5"
    };

    static string FormatLoose(object? value) => value switch
    {
        null => "—",
        JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString() ?? "—",
        JsonElement element => element.ToString(),
        bool flag => flag ? "开" : "关",
        _ => value.ToString() ?? "—"
    };
}
