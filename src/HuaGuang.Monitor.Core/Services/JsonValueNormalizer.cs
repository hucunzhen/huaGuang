using System.Text.Json;

namespace HuaGuang.Monitor.Services;

public static class JsonValueNormalizer
{
    public static object? Normalize(object? value) =>
        value is JsonElement element ? FromJsonElement(element) : value;

    public static object? FromJsonElement(JsonElement element)
    {
        try
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.TryGetInt64(out var integer) && !element.GetRawText().Contains('.')
                    ? integer
                    : element.TryGetDouble(out var number)
                        ? number
                        : element.GetRawText(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => element.GetRawText()
            };
        }
        catch
        {
            try
            {
                return element.GetRawText();
            }
            catch
            {
                return null;
            }
        }
    }

    public static bool ValuesEqual(object? left, object? right)
    {
        left = Normalize(left);
        right = Normalize(right);
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        if (left.Equals(right))
        {
            return true;
        }

        return left is IFormattable && right is IFormattable &&
               left.ToString() == right.ToString();
    }
}
