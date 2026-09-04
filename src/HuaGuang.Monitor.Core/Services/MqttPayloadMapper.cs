using System.Text.Encodings.Web;
using System.Text.Json;
using HuaGuang.Monitor.Models;

namespace HuaGuang.Monitor.Services;

public static class MqttPayloadMapper
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string ResolveFieldKey(PlcTag tag, MqttPayloadProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(tag.MqttField))
        {
            return tag.MqttField.Trim();
        }

        return profile.UseTagNameWhenFieldEmpty ? tag.Name : tag.Name;
    }

    public static Dictionary<string, object?> BuildTagsObject(
        IEnumerable<PlcTag> tags,
        IReadOnlyDictionary<string, object?> valuesByTagName,
        MqttPayloadProfile profile,
        int globalPrecision = AppSettings.DefaultTemperaturePrecision)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var tag in tags)
        {
            if (!valuesByTagName.TryGetValue(tag.Name, out var value))
            {
                continue;
            }

            result[ResolveFieldKey(tag, profile)] = FormatMqttValue(tag, value, globalPrecision);
        }

        return result;
    }

    public static object? FormatMqttValue(PlcTag tag, object? value, int globalPrecision)
    {
        if (value is null)
        {
            return null;
        }

        if (SwitchStatusFormatting.TryFormatMqttText(tag, value, out var switchText))
        {
            return switchText;
        }

        if (ValueFormatting.SupportsPrecision(tag) && ValueFormatting.TryAsDouble(value, out var number))
        {
            var precision = ValueFormatting.ResolvePrecision(tag, globalPrecision);
            return Math.Round(number, precision, MidpointRounding.AwayFromZero);
        }

        return value;
    }

    public static string BuildPayload(
        AppSettings settings,
        IReadOnlyDictionary<string, object?> valuesByTagName,
        bool allGood)
    {
        var profile = settings.MqttPayload ?? new MqttPayloadProfile();
        var root = new Dictionary<string, object?>(StringComparer.Ordinal);

        SetOptionalByPath(root, profile.DeviceIdPath, settings.DeviceId);
        SetOptionalByPath(root, profile.TimestampPath,
            FormatTimestamp(DateTimeOffset.UtcNow, profile.TimestampFormat));
        SetOptionalByPath(root, profile.SimulatorPath, settings.UseSimulator);
        SetOptionalByPath(root, profile.PlcHostPath, settings.Plc.Host);
        SetOptionalByPath(root, profile.QualityPath, allGood ? "Good" : "Uncertain");

        var tagsObject = BuildTagsObject(
            settings.Tags.Where(t => t.Enabled),
            valuesByTagName,
            profile,
            settings.TemperaturePrecision);
        if (string.IsNullOrWhiteSpace(profile.TagsPath))
        {
            foreach (var pair in tagsObject)
            {
                root[pair.Key] = pair.Value;
            }
        }
        else
        {
            SetByPath(root, profile.TagsPath, tagsObject);
        }

        return JsonSerializer.Serialize(root, JsonOptions);
    }

    public static TelemetryParseResult Parse(JsonElement root, MqttPayloadProfile profile)
    {
        var result = new TelemetryParseResult
        {
            DeviceId = ReadString(root, profile.DeviceIdPath),
            Quality = ReadString(root, profile.QualityPath) ?? "Good",
            PlcHost = ReadString(root, profile.PlcHostPath) ?? string.Empty,
            Simulator = ReadBool(root, profile.SimulatorPath),
            Timestamp = ReadTimestamp(root, profile.TimestampPath, profile.TimestampFormat)
        };

        if (TryGetByPath(root, profile.TagsPath, out var tagsElement) &&
            tagsElement.ValueKind == JsonValueKind.Object)
        {
            AppendTags(result, tagsElement);
        }
        else if (string.IsNullOrWhiteSpace(profile.TagsPath) && root.ValueKind == JsonValueKind.Object)
        {
            var reserved = GetReservedRootKeys(profile);
            foreach (var property in root.EnumerateObject())
            {
                if (reserved.Contains(property.Name))
                {
                    continue;
                }

                result.Tags[property.Name] = JsonElementToObject(property.Value);
            }
        }
        else if (result.Tags.Count == 0)
        {
            TryAppendAlternateTagPaths(root, profile.TagsPath, result);
        }

        return result;
    }

    static void TryAppendAlternateTagPaths(JsonElement root, string? primaryPath, TelemetryParseResult result)
    {
        foreach (var path in new[] { "properties", "tags", "data.tags", "data" })
        {
            if (string.Equals(path, primaryPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!TryGetByPath(root, path, out var tagsElement) ||
                tagsElement.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            AppendTags(result, tagsElement);
            if (result.Tags.Count > 0)
            {
                return;
            }
        }
    }

    static void AppendTags(TelemetryParseResult result, JsonElement tagsElement)
    {
        foreach (var property in tagsElement.EnumerateObject())
        {
            result.Tags[property.Name] = JsonElementToObject(property.Value);
        }
    }

    public static PlcTag? MatchCatalogTag(string remoteFieldKey, IReadOnlyList<PlcTag> catalogTags, MqttPayloadProfile profile)
    {
        foreach (var tag in catalogTags)
        {
            if (string.Equals(tag.Name, remoteFieldKey, StringComparison.Ordinal))
            {
                return tag;
            }

            var mqttField = ResolveFieldKey(tag, profile);
            if (string.Equals(mqttField, remoteFieldKey, StringComparison.Ordinal))
            {
                return tag;
            }
        }

        return null;
    }

    public static object FormatTimestamp(DateTimeOffset timestamp, string format) =>
        format.Trim().ToLowerInvariant() switch
        {
            "unix_ms" or "unixms" => timestamp.ToUnixTimeMilliseconds(),
            "unix_s" or "unixs" => timestamp.ToUnixTimeSeconds(),
            _ => timestamp.ToString("O")
        };

    static DateTimeOffset ReadTimestamp(JsonElement root, string path, string format)
    {
        if (!TryGetByPath(root, path, out var element))
        {
            return DateTimeOffset.Now;
        }

        return format.Trim().ToLowerInvariant() switch
        {
            "unix_ms" or "unixms" when element.ValueKind == JsonValueKind.Number =>
                DateTimeOffset.FromUnixTimeMilliseconds((long)element.GetDouble()),
            "unix_s" or "unixs" when element.ValueKind == JsonValueKind.Number =>
                DateTimeOffset.FromUnixTimeSeconds((long)element.GetDouble()),
            _ when element.ValueKind == JsonValueKind.String &&
                   DateTimeOffset.TryParse(element.GetString(), out var parsed) => parsed,
            _ => DateTimeOffset.Now
        };
    }

    static string? ReadString(JsonElement root, string path) =>
        TryGetByPath(root, path, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;

    static bool ReadBool(JsonElement root, string path)
    {
        if (!TryGetByPath(root, path, out var element))
        {
            return false;
        }

        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => bool.TryParse(element.GetString(), out var parsed) && parsed,
            JsonValueKind.Number => element.GetDouble() != 0,
            _ => false
        };
    }

    static bool TryGetByPath(JsonElement root, string path, out JsonElement element)
    {
        element = root;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        foreach (var part in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(part, out element))
            {
                element = default;
                return false;
            }
        }

        return true;
    }

    static HashSet<string> GetReservedRootKeys(MqttPayloadProfile profile)
    {
        var reserved = new HashSet<string>(StringComparer.Ordinal);
        AddReservedKey(reserved, profile.DeviceIdPath);
        AddReservedKey(reserved, profile.TimestampPath);
        AddReservedKey(reserved, profile.QualityPath);
        AddReservedKey(reserved, profile.PlcHostPath);
        AddReservedKey(reserved, profile.SimulatorPath);
        return reserved;
    }

    static void AddReservedKey(ISet<string> reserved, string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('.', StringComparison.Ordinal))
        {
            return;
        }

        reserved.Add(path.Trim());
    }

    static void SetOptionalByPath(IDictionary<string, object?> root, string path, object? value)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        SetByPath(root, path, value);
    }

    static void SetByPath(IDictionary<string, object?> root, string path, object? value)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return;
        }

        if (parts.Length == 1)
        {
            root[parts[0]] = value;
            return;
        }

        IDictionary<string, object?> current = root;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (!current.TryGetValue(parts[i], out var next) || next is not Dictionary<string, object?> nested)
            {
                nested = new Dictionary<string, object?>(StringComparer.Ordinal);
                current[parts[i]] = nested;
            }

            current = nested;
        }

        current[parts[^1]] = value;
    }

    static object? JsonElementToObject(JsonElement element) => JsonValueNormalizer.FromJsonElement(element);
}

public sealed class TelemetryParseResult
{
    public string? DeviceId { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;
    public string Quality { get; init; } = "Good";
    public string PlcHost { get; init; } = string.Empty;
    public bool Simulator { get; init; }
    public Dictionary<string, object?> Tags { get; init; } = new(StringComparer.Ordinal);
}
