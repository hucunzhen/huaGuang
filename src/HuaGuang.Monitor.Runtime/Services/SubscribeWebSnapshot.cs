using System.Text.Json;
using HuaGuang.Monitor.Models;

namespace HuaGuang.Monitor.Services;

public sealed class SubscribeWebStatus
{
    public bool Running { get; init; }
    public bool MqttConnected { get; init; }
    public string LineName { get; init; } = string.Empty;
    public string LastError { get; init; } = string.Empty;
    public string? DetailDeviceId { get; init; }
    public DateTimeOffset GeneratedAt { get; init; }
    /// <summary>全部机台 Id，详情模式下 Devices 可能只有当前一台。</summary>
    public List<string> DeviceIds { get; init; } = [];
    public List<SubscribeWebDevice> Devices { get; init; } = [];
}

public sealed class SubscribeWebDevice
{
    public string DeviceId { get; init; } = string.Empty;
    public string LineName { get; init; } = string.Empty;
    public string Topic { get; init; } = string.Empty;
    public string Quality { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string RunStatus { get; init; } = "—";
    public string RunStatusColor { get; init; } = "#8AA0B5";
    public int TagCount { get; init; }
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
        IEnumerable<RemoteDeviceState> devices,
        string? detailDeviceId = null)
    {
        var profile = settings.MqttPayload ?? new MqttPayloadProfile();
        var precision = settings.TemperaturePrecision;
        var detail = string.IsNullOrWhiteSpace(detailDeviceId) ? null : detailDeviceId.Trim();
        var list = SubscribeDeviceOrder
            .Order(
                devices as IList<RemoteDeviceState> ?? devices.ToList(),
                device => device.DeviceId,
                device => device.SourceTopic)
            .ToList();
        var deviceIds = list
            .Select(device => device.DeviceId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        List<SubscribeWebDevice> built;
        if (detail is null)
        {
            built = [];
            foreach (var device in list)
            {
                try
                {
                    built.Add(CreateDeviceSummary(settings, profile, device));
                }
                catch
                {
                    // 单台点位异常（无设备状态/畸形值）不能拖垮整页汇总。
                    built.Add(CreateFallbackDevice(device, "机台状态解析失败"));
                }
            }
        }
        else
        {
            // 详情只生成当前机台全量点位，避免点击后又把所有机台重算一遍导致超时。
            var match = list.FirstOrDefault(device =>
                device.DeviceId.Equals(detail, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                built =
                [
                    new SubscribeWebDevice
                    {
                        DeviceId = detail,
                        Status = "未找到该机台的遥测，请返回汇总。",
                        RunStatus = "—",
                        Tags = []
                    }
                ];
            }
            else
            {
                try
                {
                    built = [CreateDeviceDetail(settings, profile, precision, match)];
                }
                catch (Exception ex)
                {
                    built =
                    [
                        CreateFallbackDevice(
                            match,
                            "点位解析失败，请检查该机台点表/报文（" + ex.GetType().Name + "）")
                    ];
                }
            }
        }

        return new SubscribeWebStatus
        {
            Running = running,
            MqttConnected = mqttConnected,
            LineName = settings.LineName,
            LastError = lastError ?? string.Empty,
            DetailDeviceId = detail,
            GeneratedAt = DateTimeOffset.Now,
            DeviceIds = deviceIds,
            Devices = built
        };
    }

    static SubscribeWebDevice CreateFallbackDevice(RemoteDeviceState device, string status) =>
        new()
        {
            DeviceId = string.IsNullOrWhiteSpace(device.DeviceId) ? "未知设备" : device.DeviceId,
            LineName = SubscribeLineCatalog.TryResolveLineName(device.DeviceId, device.SourceTopic, out var line)
                ? line
                : string.Empty,
            Topic = device.SourceTopic,
            Quality = device.Quality,
            Status = status,
            RunStatus = "—",
            RunStatusColor = "#8AA0B5",
            TagCount = device.TagCountHint > 0 ? device.TagCountHint : device.Tags.Count,
            ReceivedAt = device.ReceivedAt,
            Tags = []
        };

    /// <summary>机台卡：带全部开关类状态，不展开温度/工艺等全量点位。</summary>
    static SubscribeWebDevice CreateDeviceSummary(
        AppSettings settings,
        MqttPayloadProfile profile,
        RemoteDeviceState device)
    {
        var catalog = SubscribeLineCatalog.ResolveTags(device.DeviceId, device.SourceTopic, settings.Tags);
        var updatedAt = (device.Timestamp == default ? device.ReceivedAt : device.Timestamp)
            .ToLocalTime()
            .ToString("HH:mm:ss");
        var qualityColor = QualityColor(device.Quality);
        var switches = new List<SubscribeWebTag>();
        foreach (var (name, value, catalogTag) in TagDisplayOrder.OrderRemoteTags(device.Tags, catalog, profile))
        {
            try
            {
                var tag = catalogTag ?? new PlcTag { Name = name };
                var category = TagDisplayCategoryHelper.Resolve(tag, value);
                if (category != TagDisplayCategory.Switch)
                {
                    continue;
                }

                switches.Add(new SubscribeWebTag
                {
                    Name = name,
                    Value = SwitchStatusFormatting.FormatDisplayText(tag, value),
                    Unit = string.Empty,
                    Group = TagDisplayCategoryHelper.GetTitle(category),
                    GroupColor = TagDisplayCategoryHelper.GetAccentColor(category),
                    IsSwitch = true,
                    Indicator = SwitchStatusFormatting.FormatIndicatorText(tag, value),
                    Color = SwitchStatusFormatting.GetAccentColor(tag, value),
                    Background = SwitchStatusFormatting.GetBackgroundColor(tag, value),
                    Address = device.SourceTopic,
                    UpdatedAt = updatedAt,
                    QualityColor = qualityColor
                });
            }
            catch
            {
                // 跳过异常开关点，避免无「设备状态」等点位拖垮整机。
            }
        }

        var runTag = switches.FirstOrDefault(tag =>
            string.Equals(tag.Name, RunStatusFormatting.TagName, StringComparison.Ordinal));
        var runValue = runTag is null ? TryFindRunStatus(device.Tags) : null;
        var lineName = SubscribeLineCatalog.TryResolveLineName(device.DeviceId, device.SourceTopic, out var resolved)
            ? resolved
            : string.Empty;
        var tagCount = device.TagCountHint > 0 ? device.TagCountHint : device.Tags.Count;
        return new SubscribeWebDevice
        {
            DeviceId = device.DeviceId,
            LineName = lineName,
            Topic = device.SourceTopic,
            Quality = device.Quality,
            Status = BuildStatus(device),
            RunStatus = runTag?.Value ?? RunStatusFormatting.GetStatusText(runValue),
            RunStatusColor = runTag?.Color ?? RunStatusFormatting.GetAccentColor(runValue),
            TagCount = tagCount,
            ReceivedAt = device.ReceivedAt,
            Tags = switches
        };
    }

    static SubscribeWebDevice CreateDeviceDetail(
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
            try
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
            catch
            {
                // 跳过单个坏点，无设备状态或畸形值时仍能打开机台页。
            }
        }

        var tags = OrderDetailTags(built);

        var runTag = tags.FirstOrDefault(tag =>
            string.Equals(tag.Name, RunStatusFormatting.TagName, StringComparison.Ordinal));
        var lineName = SubscribeLineCatalog.TryResolveLineName(device.DeviceId, device.SourceTopic, out var resolved)
            ? resolved
            : string.Empty;

        return new SubscribeWebDevice
        {
            DeviceId = device.DeviceId,
            LineName = lineName,
            Topic = device.SourceTopic,
            Quality = device.Quality,
            Status = BuildStatus(device),
            RunStatus = runTag?.Value ?? RunStatusFormatting.GetStatusText(TryFindRunStatus(device.Tags)),
            RunStatusColor = runTag?.Color ?? RunStatusFormatting.GetAccentColor(TryFindRunStatus(device.Tags)),
            TagCount = tags.Count,
            ReceivedAt = device.ReceivedAt,
            Tags = tags
        };
    }

    static List<SubscribeWebTag> OrderDetailTags(
        List<(SubscribeWebTag Tag, TagDisplayCategory Category)> built)
    {
        var result = new List<SubscribeWebTag>(built.Count);
        foreach (var group in built
                     .Where(item => item.Tag is not null)
                     .GroupBy(item => item.Category)
                     .OrderBy(group => TagDisplayCategoryHelper.GetSortOrder(group.Key)))
        {
            foreach (var item in CurrentInjectionFormatting.OrderByDisplay(
                         group.Key,
                         group,
                         entry => entry.Tag.Name ?? string.Empty))
            {
                if (item.Tag is not null)
                {
                    result.Add(item.Tag);
                }
            }
        }

        return result;
    }

    static object? TryFindRunStatus(IReadOnlyDictionary<string, object?> tags)
    {
        if (tags.TryGetValue(RunStatusFormatting.TagName, out var byName))
        {
            return byName;
        }

        if (tags.TryGetValue("run_status", out var byField))
        {
            return byField;
        }

        foreach (var pair in tags)
        {
            if (pair.Key.Equals(RunStatusFormatting.TagName, StringComparison.OrdinalIgnoreCase) ||
                pair.Key.Equals("run_status", StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
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

    static string FormatLoose(object? value)
    {
        try
        {
            return value switch
            {
                null => "—",
                JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString() ?? "—",
                JsonElement element => element.ToString(),
                bool flag => flag ? "开" : "关",
                _ => value.ToString() ?? "—"
            };
        }
        catch
        {
            return "—";
        }
    }
}
