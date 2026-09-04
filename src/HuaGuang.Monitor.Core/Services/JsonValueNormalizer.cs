using System.Text.Json;

namespace HuaGuang.Monitor.Services;

public static class JsonValueNormalizer
{
    public static object? Normalize(object? value) =>
        value is JsonElement element ? FromJsonElement(element) : value;

    public static object? FromJsonElement(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var integer) && !element.GetRawText().Contains('.')
            ? integer
            : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => element.GetRawText()
    };
}
